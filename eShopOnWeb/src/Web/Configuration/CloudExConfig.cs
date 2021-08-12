using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Web.Configuration
{
    public class CloudExConfig
    {
        public static string CONFIG_NAME = "CloudExConfig";

        public string ServiceBusKeyName { get; set; }
        public string ServiceBusKey { get; set; }

        public string ServiceBusEndpoint { get; set; }

        public string DeliveryOrderProcessorEndpoint { get; set; }
    }
}
