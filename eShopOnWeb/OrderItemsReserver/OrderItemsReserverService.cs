using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Collections.Generic;
using System.Linq;

namespace OrderItemsReserver
{
    public class CatalogItemOrdered
    {
        public int CatalogItemId { get; set; }
        public string ProductName { get; set; }
        public string PictureUri { get; set; }
    }
    public class OrderItem
    {
        public int Id { get; set; }
        public CatalogItemOrdered ItemOrdered { get; set; }
        public decimal UnitPrice { get; set; }
        public int Units { get; set; }
    }

    public class Order
    {
        public int Id { get; set; }

        public List<OrderItem> OrderItems { get; set; }

        public decimal TotalCost { get; set; }
    }


    public class LogicAppMessage
    {
        [JsonProperty("message")]
        public string Message { get; set; }
        [JsonProperty("order")]
        public Order Order { get; set; }
    }

    public class CloudExFakeException : Exception
    {
        public CloudExFakeException(string message) : base(message)
        {

        }
    }

    public static class OrderItemsReserverService
    {
        static int executionCount = 0;
        static string logicAppUri = "";
        static bool testFailureFlag=false;

        [FunctionName("OrderItemsReserverService")]
        [ExponentialBackoffRetry(3, "00:00:15", "00:01:00")]
        public static async Task Run(
            [ServiceBusTrigger(queueName: "orders", AutoComplete = true, Connection = "AzureServiceBusString")] string mySbMsg,
            ILogger log, ExecutionContext context)
        {
            
            executionCount += 1;
            log.LogInformation("Counting1 " + executionCount);
            string requestBody = mySbMsg;
            log.LogInformation(requestBody);

            CloudBlobContainer container = null;
            try
            {
                CreateContainerIfNotExists(log, context);

                log.LogInformation("Getting storage account");

                CloudStorageAccount storageAccount = GetCloudStorageAccount(log, context);

                log.LogInformation("Getting blob client");

                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

                log.LogInformation("Getting blob container");

                container = blobClient.GetContainerReference("orders");
                string randomStr = Guid.NewGuid().ToString();

                log.LogInformation("Getting block block");

                CloudBlockBlob blob = container.GetBlockBlobReference(randomStr);


                blob.Properties.ContentType = "application/json";

                string serializeJesonObject = requestBody;
                var orderDto = JsonConvert.DeserializeObject<Order>(requestBody);
                
                if (testFailureFlag && orderDto.OrderItems.Any(i=>i.ItemOrdered.CatalogItemId==3))
                {
                    throw new CloudExFakeException("Fake exception on catalogItemId 3");
                }

                using (var ms = new MemoryStream())
                {

                    LoadStreamWithJson(ms, serializeJesonObject);
                    await blob.UploadFromStreamAsync(ms);
                }

                log.LogInformation($"Bolb {randomStr} is uploaded to container {container.Name}");

                await blob.SetPropertiesAsync();

                log.LogInformation("order persist in blob storage");
            }
            catch (Exception ex)
            {
                log.LogError(ex.Message);

                log.LogInformation("Counting " + executionCount);

                if (executionCount < 3)
                {
                    throw;
                }
                else
                {
                    executionCount = 0;
                    log.LogInformation("Should call logic app");
                    var orderDto = JsonConvert.DeserializeObject<Order>(requestBody);
                    HttpClient client = new HttpClient();
                    HttpRequestMessage msg = new HttpRequestMessage(HttpMethod.Post, logicAppUri);
                    var content = Newtonsoft.Json.JsonConvert.SerializeObject(new LogicAppMessage() { Message = $"Could not save order in blob storage. Order details Id: {orderDto.Id}" });
                    log.LogInformation(content);
                    msg.Content = new StringContent(content, System.Text.Encoding.UTF8,"application/json");
                    var response = client.SendAsync(msg).GetAwaiter().GetResult();
                    log.LogInformation(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                }

            }
        }

        private static void CreateContainerIfNotExists(ILogger logger, ExecutionContext executionContext)
        {
            logger.LogInformation("CreateContainerIfNotExists - Getting cloud storage");
            CloudStorageAccount storageAccount = GetCloudStorageAccount(logger, executionContext);

            logger.LogInformation("CreateContainerIfNotExists - Getting blob client");
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            string[] containers = new string[] { "orders" };
            foreach (var item in containers)
            {
                logger.LogInformation("CreateContainerIfNotExists - Getting blob container");
                CloudBlobContainer blobContainer = blobClient.GetContainerReference(item);

                bool r = blobContainer.CreateIfNotExistsAsync().GetAwaiter().GetResult();

                if (r)
                {
                    logger.LogInformation("container created");
                }

                else if (!blobContainer.ExistsAsync().GetAwaiter().GetResult())
                {
                    logger.LogError("container not created!!!!!!!!!!!!!!!!!");
                }

            }
        }

        private static CloudStorageAccount GetCloudStorageAccount(ILogger logger, ExecutionContext executionContext)
        {
            var config = new ConfigurationBuilder()
                            .SetBasePath(executionContext.FunctionAppDirectory)
                            .AddJsonFile("local.settings.json", true, true)
                            .AddEnvironmentVariables().Build();
            logger.LogInformation("GetCloudStorageAccount config " + config["CloudStorageAccount"]);
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(config["CloudStorageAccount"]);
            logicAppUri = config["LogicAppUri"];
            testFailureFlag = bool.Parse(config["TestFailure"]);
            return storageAccount;
        }

        private static void LoadStreamWithJson(Stream ms, object obj)
        {
            StreamWriter writer = new StreamWriter(ms);
            writer.Write(obj);
            writer.Flush();
            ms.Position = 0;
        }
    }
}
