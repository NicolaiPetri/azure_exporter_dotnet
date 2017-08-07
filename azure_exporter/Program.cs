/*
   Copyright 2017 Cloudeon A/S, Denmark

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

     http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
   */

using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Monitor;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Newtonsoft.Json.Linq;
using Prometheus;
using Prometheus.Advanced;
using Prometheus.Advanced.DataContracts;
using System;
using System.Collections.Generic;
using System.Net;
using System.Reactive.Concurrency;

namespace azure_exporter
{
    class Program
    {
        static HttpListener _httpListener = new HttpListener();

        static void Main(string[] args)
        {            
            JObject config = JObject.Parse(System.IO.File.ReadAllText("config.json"));
            ResourceIdCachedService resourceIdCachedService = new ResourceIdCachedService(TimeSpan.Parse((string)config["cacheExpiration"] ?? "03:00:00"));

            ServicePointManager.DefaultConnectionLimit = ((int?)config["connectionLimit"] ?? 50);
            ServicePointManager.UseNagleAlgorithm = false;
           
            _httpListener.Prefixes.Add($"http://localhost:{config["port"]}/metrics/");
            _httpListener.Start();
            Console.WriteLine("Listening on http://localhost:{0}/metrics/", config["port"]);
            
            var scheduler = Scheduler.Default.Schedule(
                repeatAction =>
                {
                    try
                    {
                        _httpListener.BeginGetContext(ar =>
                        {
                            HttpListenerContext outerContext = null;
                            try
                            {
                                DateTime startTime = DateTime.UtcNow;
                                var httpListenerContext = _httpListener.EndGetContext(ar);
                                outerContext = httpListenerContext;
                                string subscriptionId = httpListenerContext.Request.QueryString["subscription_id"] ?? "default";

                                string resourceId = httpListenerContext.Request.QueryString["resource_id"];

                                string resourceType = httpListenerContext.Request.QueryString["resource_type"];
                                string resourceName = httpListenerContext.Request.QueryString["resource_name"];

                                if (string.IsNullOrEmpty(resourceId) && (string.IsNullOrEmpty(resourceType) || string.IsNullOrEmpty(resourceName)))
                                {
                                    httpListenerContext.Response.StatusCode = 404;
                                    byte[] buffer = System.Text.Encoding.UTF8.GetBytes("resource_id or resource_type and resource_name is required");
                                    httpListenerContext.Response.OutputStream.Write(buffer, 0, buffer.Length);
                                    httpListenerContext.Response.Close();
                                    repeatAction.Invoke();
                                    return;
                                }

                                string key = string.IsNullOrEmpty(subscriptionId) ? "default" : subscriptionId;
                                var subscriptionCredentials = config["credentials"][key] ?? config["credentials"]["default"];

                                string contentType = ConfigureHeaders(httpListenerContext);

                                AzureCredentials credentials = SdkContext.AzureCredentialsFactory.FromServicePrincipal(
                                    (string)subscriptionCredentials["clientId"], (string)subscriptionCredentials["clientKey"], (string)subscriptionCredentials["tenantId"], AzureEnvironment.AzureGlobalCloud);

                                var azureAuth = Azure
                                    .Configure()
                                    .WithLogLevel(HttpLoggingDelegatingHandler.Level.Basic)
                                    .Authenticate(credentials);
                                IAzure azure;
                                if (subscriptionId.Equals("default"))
                                {
                                    azure = azureAuth.WithDefaultSubscription();
                                } else
                                {
                                    azure = azureAuth.WithSubscription(subscriptionId);
                                }

                                var mc = new MonitorClient(credentials) { SubscriptionId = subscriptionId };

                                if (string.IsNullOrEmpty(resourceId))
                                {
                                    resourceId = resourceIdCachedService.GetResourceId(azure, resourceId, resourceType, resourceName);
                                }

                                if (string.IsNullOrEmpty(resourceId)) {
                                    Console.WriteLine(" .. failed getting metrics - resource id not found:\n{0}::{1} >> {2}", subscriptionId, resourceType, resourceName);
                                    httpListenerContext.Response.StatusCode = 404;
                                    byte[] buffer = System.Text.Encoding.UTF8.GetBytes("resource_id not found!");
                                    httpListenerContext.Response.OutputStream.Write(buffer, 0, buffer.Length);
                                    httpListenerContext.Response.Close();
                                    repeatAction.Invoke();
                                    return;
                                 }

                                Console.WriteLine("ResourceId: {0}", resourceId);
                                DefaultCollectorRegistry reg = new MetricReader(azure, mc).ReadMetrics(resourceId);

                                if (reg == null)
                                {
                                    Console.WriteLine(" .. failed getting metrics - ReadMetrics failed");
                                    httpListenerContext.Response.StatusCode = 500;
                                    byte[] buffer = System.Text.Encoding.UTF8.GetBytes("Readmetrics failed for resource "+resourceId);
                                    httpListenerContext.Response.OutputStream.Write(buffer, 0, buffer.Length);
                                    httpListenerContext.Response.Close();
                                    repeatAction.Invoke();
                                    return;
                                }

                                IEnumerable<MetricFamily> metricFamily = reg.CollectAll();
                                new MetricWriter().WriteMetrics(httpListenerContext.Response, contentType, metricFamily);
                                httpListenerContext.Response.Close();
                                // PrintMetrics(metricFamily);

                                Console.WriteLine("Scrape request done in {0} seconds", DateTime.UtcNow - startTime);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(string.Format("Error in MetricsServer: {0}", e));
                                if (outerContext != null)
                                {
                                    outerContext.Response.StatusCode = 500;
                                    byte[] buffer = System.Text.Encoding.UTF8.GetBytes("Exception: " + e.ToString());
                                    outerContext.Response.OutputStream.Write(buffer, 0, buffer.Length);
                                    outerContext.Response.Close();
                                }
                            }
                            repeatAction.Invoke();
                        }, null);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(string.Format("Error in MetricsServer: {0}", e));
                        repeatAction.Invoke();
                    }
                }
            );
            
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e) {
                e.Cancel = true;                
                Environment.Exit(0);
                scheduler.Dispose();
            };
                        
            while (true)
            {
                Console.ReadKey();
            }
        }

        private static void PrintMetrics(IEnumerable<MetricFamily> metricFamily)
        {
            foreach (var f in metricFamily)
            {
                foreach (var m in f.metric)
                {
                    if (m.gauge != null) Console.WriteLine("Metric: {0} = {1}", f.name, m.gauge.value);
                    if (m.counter != null) Console.WriteLine("Metric: {0} = {1}", f.name, m.counter.value);
                }
            }
        }

        private static string ConfigureHeaders(HttpListenerContext httpListenerContext)
        {
            httpListenerContext.Response.StatusCode = 200;
            var acceptHeader = httpListenerContext.Request.Headers.Get("Accept");
            var acceptHeaders = acceptHeader == null ? null : acceptHeader.Split(',');
            var contentType = ScrapeHandler.GetContentType(acceptHeaders);
            httpListenerContext.Response.ContentType = contentType;
            return contentType;
        }        
    }    
}
