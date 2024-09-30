using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Cloud2BPart2Functions
{
    public class QueueOrderFunction
    {
        private readonly ILogger<QueueOrderFunction> _logger;

        public QueueOrderFunction(ILogger<QueueOrderFunction> logger)
        {
            _logger = logger;
        }

        [Function("QueueOrder")]
        public async Task<HttpResponseData> Run(
     [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
        {
            _logger.LogInformation("QueueOrder function triggered.");

            // Read the incoming request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            OrderMessage order;
            try
            {
                order = JsonSerializer.Deserialize<OrderMessage>(requestBody);
            }
            catch (JsonException)
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Invalid JSON format.");
                return badResponse;
            }

            // Validate order
            if (order == null || string.IsNullOrEmpty(order.RowKey) || order.Quantity <= 0)
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Invalid order data.");
                return badResponse;
            }

            // Retrieve connection string for Azure Queue Storage
            string storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            if (string.IsNullOrEmpty(storageConnectionString))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("AzureWebJobsStorage is not configured.");
                return errorResponse;
            }

            try
            {
                // Create a client to interact with the Azure Queue
                QueueClient queueClient = new QueueClient(storageConnectionString, "order-queue");
                await queueClient.CreateIfNotExistsAsync();

                // Serialize the order message
                string message = JsonSerializer.Serialize(order);

                // Send the message to the queue
                await queueClient.SendMessageAsync(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(message)));

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteStringAsync("Order added to the queue.");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding order to the queue.");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("Error adding order to the queue.");
                return errorResponse;
            }
        }

        // The updated model representing the order message
        public class OrderMessage
        {
            public string RowKey { get; set; } // Updated identifier
            public int Quantity { get; set; }
            public string ProductName { get; set; }
        }

    }
}

