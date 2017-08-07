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
using System;
using System.Linq;
using System.Runtime.Caching;

namespace azure_exporter
{
    public class ResourceIdCachedService
    {
        readonly ObjectCache _cache;
        readonly TimeSpan _cacheExpiration;

        public ResourceIdCachedService(TimeSpan cacheExpiration)
        {
            _cache = MemoryCache.Default;
            _cacheExpiration = cacheExpiration;
        }

        public string GetResourceId(IAzure azure, string resourceId, string resourceType, string resourceName)
        {            
            var resourceCacheKey = $"resourceId-{resourceType}-{resourceName}";
            resourceId = _cache[resourceCacheKey] as string;

            if (string.IsNullOrEmpty(resourceId))
            {
                if (resourceType.Equals("webapp", StringComparison.InvariantCultureIgnoreCase))
                {
                    var webapp = azure.AppServices.WebApps.List().SingleOrDefault(app => app.Name == resourceName);
                    if (webapp != null)
                    {
                        resourceId = webapp.Id;
                        Console.WriteLine("WebApp: {0}", webapp.Name);
                    }
                } else if (resourceType.Equals("storageaccount", StringComparison.InvariantCultureIgnoreCase))
                {
                    var storageAccount = azure.StorageAccounts.List().SingleOrDefault(app => app.Name == resourceName);

                    if (storageAccount != null)
                    {
                        resourceId = storageAccount.Id;
                        Console.WriteLine("Storage account: {0}", storageAccount.Name);
                    }
                }
                else if (resourceType.Equals("appserviceplan", StringComparison.InvariantCultureIgnoreCase))
                {
                    var appPlan = azure.AppServices.AppServicePlans.List().SingleOrDefault(plan => plan.Name == resourceName);


                    if (appPlan != null)
                    {
                        resourceId = appPlan.Id;
                        Console.WriteLine("AppServicePlan account: {0}", appPlan.Name);
                    }
                }

                else if (resourceType.Equals("certificate", StringComparison.InvariantCultureIgnoreCase))
                {
                    var rgs = azure.ResourceGroups.List();
                    foreach (var rg in rgs)
                    {
                        var certificate = azure.AppServices.AppServiceCertificates.ListByResourceGroup(rg.Name).SingleOrDefault(cert => cert.Name == resourceName);
                        if (certificate != null)
                        {
                            resourceId = certificate.Id;
                            break;
                        }
                    }
                }
                else if (resourceType.Equals("vm", StringComparison.InvariantCultureIgnoreCase))
                {
                    var vm = azure.VirtualMachines.List().SingleOrDefault(app => app.Name == resourceName);

                    if (vm != null)
                    {
                        resourceId = vm.Id;
                        Console.WriteLine("certificate: {0} ({1})", vm.Name, vm.OSType);
                    }
                }
                /*                else if (resourceType.Equals("apimanagement", StringComparison.InvariantCultureIgnoreCase))
                                {
                                    var storageAccount = azure.ap.List().SingleOrDefault(app => app.Name == resourceName);

                                    if (storageAccount != null)
                                    {
                                        resourceId = storageAccount.Id;
                                        Console.WriteLine("Storage account: {0}", storageAccount.Name);
                                    }
                                }
                */
                else if (resourceType.Equals("sqldatabase", StringComparison.InvariantCultureIgnoreCase))
                {
                    var resNameSplitted = resourceName.Split('/');
                    if (resNameSplitted.Length != 2)
                    {
                        Console.WriteLine("resourceName: {0} not a valid db name", resourceName);
                        return null;
                    }
                    var sqlServerList = azure.SqlServers.List();
                    var sqlServer = sqlServerList.SingleOrDefault(app => app.Name == resNameSplitted[0]);
                    //azure.SqlServers.List().First().Databases
                    
                    if (sqlServer != null)
                    {
                        var db = sqlServer.Databases.List().SingleOrDefault(dbinst => dbinst.Name == resNameSplitted[1]);
                        if (db != null)
                        {
                            resourceId = db.Id;
                            Console.WriteLine("SQL Database found: {0} / {1}", sqlServer.Name, db.Name);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(resourceId))
                {
                    _cache.Set(resourceCacheKey, resourceId, new CacheItemPolicy()
                    {
                        AbsoluteExpiration = DateTimeOffset.Now.AddSeconds(_cacheExpiration.TotalSeconds)
                    });
                }
            }
            
            return resourceId;
        }
    }
}
