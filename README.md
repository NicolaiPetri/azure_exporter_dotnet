# azure_exporter
Azure metrics exporter for prometheus written in c#

## Getting started

### Prerequisites
To expose your Azure Metrics using this azure_exporter you'll need to have an Azure AD Service Principal with access to metrics data for your subscription(s).

See https://docs.microsoft.com/en-us/azure/azure-resource-manager/resource-group-create-service-principal-portal for how to create this if in doubt.
After the service principal is created, you must create a client key and then grant access to your service principal so it can read metrics for your subscription(s).
I suggest setting the "Monitoring Reader" for you service principal on the subscriptions, resource groups or resources you want to monitor from prometheus.

### Configuration
Next part is to make a config.json file in the working directory (normally same folder as azure_exporter.exe is located).

Here is an example file, fill in default credentials with your Azure service principal.
```javascript
{
  "port": 9091,
  "connectionLimit": 50,
  "cacheExpiration": "03:00:00",
  "credentials": {
    "default": {
      "tenantId": "{your-azure-tenant-id-that-contains-your-subscription}",
      "clientId": "{your-service-principal-client-id}",
      "clientKey": "{your-service-principal-client-secret}"
    }
  }
}
```

### Usage
Make sure you have an instance of azure_exporter.exe running.

When running you can now query metrics for your Azure resources using the following syntax:
http://localhost:9091/metrics?resource_type=webapp&resource_name=mywebappname&subscription_id={azure_subscription_id}

You can also ask for metrics for an resource_id directly like:
http://localhost:9091/metrics?resource_id=/my/resource/id/in/azure&subscription_id={azure_subscription_id}

Currrently the following resource types are supported:
 * webapp
 * storageaccount
 * appserviceplan
 * certificate
 * vm
 * sqldatabase

## Comments or bugs
For bugs and feature requests please use the GitHub issues function.

For further comments or information I can be reached at npe (at) cloudeon.com
