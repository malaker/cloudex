namespace Microsoft.eShopWeb
{
    public class CatalogSettings
    {
        public string CatalogBaseUrl { get; set; }
    }

    public class OrderSettings
    {
        public string ServiceBusKeyName { get; set; }
        public string ServiceBusKey { get; set; }

        public string ServiceBusEndpoint { get; set; }

        public string DeliveryOrderProcessorEndpoint { get; set; }
    }
}
