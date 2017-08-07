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

using Microsoft.Azure.Management.AppService.Fluent;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Monitor;
using Prometheus.Advanced;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace azure_exporter
{
    public class MetricReader
    {
        readonly MonitorClient _monitorClient;
        readonly IAzure _azure;
        public static Dictionary<string, string> metricDefCache = new Dictionary<string, string>();
        public MetricReader(IAzure azure, MonitorClient mc)
        {
            _azure = azure;
            _monitorClient = mc;
        }

        public DefaultCollectorRegistry ReadMetrics(string resourceId)
        {
            var reg = new DefaultCollectorRegistry();
            var metricFactory = new MetricFactory(reg);

            var now = DateTime.UtcNow.AddMinutes(-1);
            string prefix = "azure"; // fallback prefix

            var resourceGroupMatch = Regex.Match(resourceId, "resourceGroups/(.+)/providers");
            var resourceGroup = resourceGroupMatch.Groups[1].Value;
            var labels = new List<string>() { "resource_group" };
            var labelValues = new List<string>() { resourceGroup };

            if (resourceId.Contains("Microsoft.Web/certificates"))
            {
                var certificate = _azure.AppServices.AppServiceCertificates.GetById(resourceId);
//                var certificate = _azure.AppServices.AppServiceCertificates.List().SingleOrDefault(cert => cert.Id == resourceId);
//                _azure.AppServices.AppServiceCertificates.List().
                prefix = "certificate";
                metricFactory.CreateGauge(prefix +"_expires_in", "Certificate will expire in X days", labels.ToArray())
                     .Labels(labelValues.ToArray()).Set((certificate.ExpirationDate - DateTime.UtcNow).TotalDays);
                metricFactory.CreateGauge(prefix + "_expired", "Certificate is expired", labels.ToArray())
                     .Labels(labelValues.ToArray()).Set(((certificate.ExpirationDate - DateTime.UtcNow).TotalDays < 0) ? 1 : 0);
                metricFactory.CreateGauge(prefix + "_lifetime", "Had a lifetime of X days", labels.ToArray())
                     .Labels(labelValues.ToArray()).Set((certificate.ExpirationDate - certificate.IssueDate).TotalDays);
               // certificate.Inner.
                var kvStatus = 0; // Not in keyvault
                if (!String.IsNullOrEmpty(certificate.Inner.KeyVaultId)) {
                    if (certificate.Inner.KeyVaultSecretStatus == Microsoft.Azure.Management.AppService.Fluent.Models.KeyVaultSecretStatus.Succeeded)
                    {
                        kvStatus = 1; // Ok
                    } else
                    {
                        kvStatus = 2; // ???
                    }
                        
                }
                metricFactory.CreateGauge(prefix + "_keyvault_status", "Keyvault status", labels.ToArray())
                     .Labels(labelValues.ToArray()).Set(kvStatus);
                return reg;
            }

            var before = now.AddMinutes(-1);
            var periodString = "startTime eq " + before.ToString("s", System.Globalization.CultureInfo.InvariantCulture) +
                " and endTime eq " + now.ToString("s", System.Globalization.CultureInfo.InvariantCulture);
            
            try
            {
                var filter = "";
                if (resourceId.Contains("Microsoft.Web/sites"))
                    prefix = "webapp";
                else if (resourceId.Contains("Microsoft.Web/serverfarms/"))
                    prefix = "appplan";
                else if (resourceId.Contains("Microsoft.Sql/servers/") && resourceId.Contains("/databases/"))
                    prefix = "database";
                else if (resourceId.Contains("Microsoft.Compute/virtualMachines"))
                    prefix = "vm";
                else if (resourceId.Contains("Microsoft.Storage"))
                    prefix = "storage";
                else if (resourceId.Contains("ApiManagement"))
                    prefix = "apim";

                if (resourceId.Contains("Microsoft.Web") && false) 
                {
                    filter = "$filter=(name.value eq 'AverageResponseTime'" +
                    " or name.value eq 'Requests'" +
                    " or name.value eq 'CpuTime'" +
                    " or name.value eq 'Http2xx'" +
                    " or name.value eq 'Http4xx'" +
                    " or name.value eq 'Http5xx'" +
                    " or name.value eq 'BytesReceived'" +
                    " or name.value eq 'BytesSent'" +
                    " or name.value eq 'MemoryWorkingSet'" +
                    ")" +
                    " and (aggregationType eq 'Total' or aggregationType eq 'Maximum' or aggregationType eq 'Minimum' or aggregationType eq 'Average' or aggregationType eq 'Count')" +
                    " and " + periodString +
                    " and timeGrain eq duration'PT1M'";
                } else // if (resourceId.Contains("Microsoft.Sql"))
                {
                    var sqlInfoMatch = Regex.Match(resourceId, "servers/(\\w+)/databases/(\\w+)");
                    if (sqlInfoMatch.Groups[1].Success)
                    {
                        labels.Add("server");
                        labelValues.Add(sqlInfoMatch.Groups[1].Value);
                    }
                    if (sqlInfoMatch.Groups[2].Success)
                    {
                        labels.Add("database");
                        labelValues.Add(sqlInfoMatch.Groups[2].Value);
                    }
                    //var resourceGroup = resourceGroupMatch.Groups[1].Value;
                    string metricFilter;
                    if (metricDefCache.ContainsKey(resourceId))
                    {
                        metricFilter = metricDefCache[resourceId];
                        Console.WriteLine("FROM CACHE");
                    }
                    else
                    {
                        var defs = new List<String>();
                        var metricDefinitions = _monitorClient.MetricDefinitions.List(resourceId);
                        foreach (var mDef in metricDefinitions)
                        {
                            defs.Add($"name.value eq '{mDef.Name.Value}'");
                        }
                        if (defs.Count == 0)
                        {
                            Console.WriteLine("Failed to get metric defs!!!");
                            return null;
                        }
                        metricFilter = String.Join(" or ", defs);
                        metricDefCache.Add(resourceId, metricFilter);
                    }
                    filter = "$filter=(" +
                        metricFilter+
                        ")" +
                    " and (aggregationType eq 'Total' or aggregationType eq 'Maximum' or aggregationType eq 'Minimum' or aggregationType eq 'Average' or aggregationType eq 'Count')" +
                    " and " + periodString +
                    " and timeGrain eq duration'PT1M'";
                }


                var metrics = _monitorClient.Metrics.List(resourceId, filter);

                foreach (var metric in metrics)
                {
                    var localLabels = new List<String>(labels);
                    var localLabelValues = new List<String>(labelValues);
                    string metricName = metric.Name.Value.Trim();
                    if (Regex.Match(metricName, "^Http\\d").Success)
                    {
                        metricName = "Http";
                        localLabels.Add("status_code");
                        localLabelValues.Add(metric.Name.Value.Substring(4));
                    }

                    var name = prefix +"_"
                        + Regex.Replace(metricName, "(?<=.)([A-Z])", "_$0", RegexOptions.Compiled).ToLower().Trim()
                        + "_" + metric.Unit.ToString().ToLower().Trim();
                    name = name.Replace("percent_percent", "percent"); // Fix double pct ... hackish
                    name = name.Replace("bytes_bytes", "bytes"); 
                    name = name.Replace(" ", "_");
                    name = name.Replace("/", "_");
                    name = name.Replace("__", "_"); 
                    name = name.Replace("c_p_u", "cpu"); 
                    Console.WriteLine("Metric: {0} ({1} {2} {3})",metric.Name.Value, metricName, String.Join(",", localLabels), String.Join(",", localLabelValues));
                    Console.WriteLine("Metric: {0}", name);
                    if (metric.Data.Any())
                    {
                        metricFactory.CreateGauge(name + "_total", "", localLabels.ToArray())
                            .Labels(localLabelValues.ToArray()).Set(metric.Data.Last().Total.GetValueOrDefault());
                        metricFactory.CreateGauge(name + "_average", "", localLabels.ToArray())
                            .Labels(localLabelValues.ToArray()).Set(metric.Data.Last().Average.GetValueOrDefault());
                        metricFactory.CreateGauge(name + "_minimum", "", localLabels.ToArray())
                            .Labels(localLabelValues.ToArray()).Set(metric.Data.Last().Minimum.GetValueOrDefault());
                        metricFactory.CreateGauge(name + "_maximum", "", localLabels.ToArray())
                            .Labels(localLabelValues.ToArray()).Set(metric.Data.Last().Maximum.GetValueOrDefault());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Outer exception: {0}", ex.ToString());
                return null;
            }

            return reg;
        }
    }
}
