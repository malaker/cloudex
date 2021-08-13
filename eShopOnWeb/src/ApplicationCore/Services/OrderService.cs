using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace Microsoft.eShopWeb.ApplicationCore.Services
{
    public class OrderService : IOrderService
    {
        private readonly IAsyncRepository<Order> _orderRepository;
        private readonly IUriComposer _uriComposer;
        private readonly IAsyncRepository<Basket> _basketRepository;
        private readonly IAsyncRepository<CatalogItem> _itemRepository;
        private readonly HttpClient _client;
        private readonly IAppLogger<OrderService> _logger;
        private readonly OrderSettings _orderSetting;

        public OrderService(IAsyncRepository<Basket> basketRepository,
            IAsyncRepository<CatalogItem> itemRepository,
            IAsyncRepository<Order> orderRepository,
            IUriComposer uriComposer, HttpClient client, OrderSettings orderSettings,
            IAppLogger<OrderService> logger)
        {
            _logger = logger;
            _orderSetting = orderSettings;
            _orderRepository = orderRepository;
            _uriComposer = uriComposer;
            _basketRepository = basketRepository;
            _itemRepository = itemRepository;
            _client = client;
        }

        public async Task CreateOrderAsync(int basketId, Address shippingAddress)
        {
            var basketSpec = new BasketWithItemsSpecification(basketId);
            var basket = await _basketRepository.FirstOrDefaultAsync(basketSpec);

            Guard.Against.NullBasket(basketId, basket);
            Guard.Against.EmptyBasketOnCheckout(basket.Items);

            var catalogItemsSpecification = new CatalogItemsSpecification(basket.Items.Select(item => item.CatalogItemId).ToArray());
            var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

            var items = basket.Items.Select(basketItem =>
            {
                var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);
                var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
                var orderItem = new OrderItem(itemOrdered, basketItem.UnitPrice, basketItem.Quantity);
                return orderItem;
            }).ToList();


            var order = new Order(basket.BuyerId, shippingAddress, items);

            await _orderRepository.AddAsync(order);

            await PublishEvent(order);

            await TriggerDeliveryOrderService(order);
        }

        private async Task PublishEvent(Order order)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, GetServiceBusUri());

            request.Content = new StringContent(JsonExtensions.ToJson(order), System.Text.Encoding.UTF8, "application/json");
            _logger.LogInformation("URI: " + GetServiceBusUri() + " KEYNAME: "+GetKeyNameFromConfig()+" KEY: "+ GetKeyFromConfig());
            request.Headers.Add("Authorization", GetSasToken(GetServiceBusUri(), GetKeyNameFromConfig(), GetKeyFromConfig(), TimeSpan.FromDays(1)));

            var r = await _client.SendAsync(request);

            _logger.LogInformation($"Publishing event to service bus status call: {r.StatusCode} and response {await r.Content.ReadAsStringAsync()}");
        }

        private async Task TriggerDeliveryOrderService(Order order)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, GetDeliveryOrderServiceUri());

            request.Content = new StringContent(JsonExtensions.ToJson(order), System.Text.Encoding.UTF8, "application/json");

            var r = await _client.SendAsync(request);

            _logger.LogInformation($"Delivery order service status call: {r.StatusCode} and response {await r.Content.ReadAsStringAsync()}");
        }

        protected string GetDeliveryOrderServiceUri()
        {
            return _orderSetting.DeliveryOrderProcessorEndpoint;
        }

        protected string GetServiceBusUri()
        {
            return _orderSetting.ServiceBusEndpoint;
        }

        protected string GetKeyNameFromConfig()
        {
            return _orderSetting.ServiceBusKeyName;
        }

        protected string GetKeyFromConfig()
        {
            return _orderSetting.ServiceBusKey;
        }

        protected string GetSasToken(string resourceUri, string keyName, string key, TimeSpan ttl)
        {
            var expiry = GetExpiry(ttl);
            string stringToSign = HttpUtility.UrlEncode(resourceUri) + "\n" + expiry;
            HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
            var sasToken = String.Format(CultureInfo.InvariantCulture, "SharedAccessSignature sr={0}&sig={1}&se={2}&skn={3}",
            HttpUtility.UrlEncode(resourceUri), HttpUtility.UrlEncode(signature), expiry, keyName);
            return sasToken;
        }

        protected string GetExpiry(TimeSpan ttl)
        {
            TimeSpan expirySinceEpoch = DateTime.UtcNow - new DateTime(1970, 1, 1) + ttl;
            return Convert.ToString((int)expirySinceEpoch.TotalSeconds);
        }
    }
}
