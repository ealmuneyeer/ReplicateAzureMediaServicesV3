using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace ReplicateAMSv3
{
    [JsonObject("appConfig")]
    public class AppSettings
    {
        public AppSettings()
        {
            Source = null;
            Destination = null;
            Miscellaneous = new Miscellaneous();
        }

        public AppSettings(ServicePrincipalAuth source, ServicePrincipalAuth destination, Miscellaneous miscellaneous)
        {
            Source = source;
            Destination = destination;
            Miscellaneous = miscellaneous;
        }

        [JsonProperty("sourceConfig")]
        public ServicePrincipalAuth Source { get; set; }

        [JsonProperty("destinationConfig")]
        public ServicePrincipalAuth Destination { get; set; }

        [JsonProperty("miscellaneous")]
        public Miscellaneous Miscellaneous { get; set; }
    }

    [JsonObject]
    public class ServicePrincipalAuth
    {
        [JsonProperty("AadClientId")]
        public string AadClientId { get; set; }

        [JsonProperty("AadSecret")]
        public string AadSecret { get; set; }

        [JsonProperty("AadTenantDomain")]
        public string AadTenantDomain { get; set; }

        [JsonProperty("AadTenantId")]
        public string AadTenantId { get; set; }

        [JsonProperty("AccountName")]
        public string AccountName { get; set; }

        [JsonProperty("Location")]
        public string Location { get; set; }

        [JsonProperty("ResourceGroup")]
        public string ResourceGroup { get; set; }

        [JsonProperty("SubscriptionId")]
        public string SubscriptionId { get; set; }

        [JsonProperty("ArmAadAudience")]
        public string ArmAadAudience { get; set; }

        [JsonProperty("ArmEndpoint")]
        public Uri ArmEndpoint { get; set; }

        [JsonProperty("StorageAccountName")]
        public string StorageAccountName { get; set; }

        [JsonProperty("StorageAccountKey")]
        public string StorageAccountKey { get; set; }

        [JsonProperty("StorageAccountUrl")]
        public Uri StorageAccountUrl { get; set; }

        [JsonProperty("AADSettings")]
        public string AADSettings { get; set; }
    }

    [JsonObject]
    public class Miscellaneous
    {
        [JsonProperty("CopyUsingLocalNetwork")]
        public bool CopyUsingLocalNetwork { get; set; } = false;

        [JsonProperty("CopyUsingAzCopy")]
        public bool CopyUsingAzCopy { get; set; } = false;
    }
}
