﻿/*
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

   using Prometheus;
using Prometheus.Advanced.DataContracts;
using System.Collections.Generic;
using System.Net;

namespace azure_exporter
{
    public class MetricWriter
    {
        public void WriteMetrics(HttpListenerResponse response, string contentType, IEnumerable<MetricFamily> metricFamily)
        {
            using (var outputStream = response.OutputStream)
            {
                try
                {
                    ScrapeHandler.ProcessScrapeRequest(metricFamily, contentType, outputStream);
                }
                catch (HttpListenerException) { }                
            }            
        }
    }
}
