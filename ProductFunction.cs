using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Azure.Data.Tables;
using Azure;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Cloud2BPart2Functions
{
    public class ProductFunctions
    {
        private readonly ILogger<ProductFunctions> _logger;

        public ProductFunctions(ILogger<ProductFunctions> logger)
        {
            _logger = logger;
        }

        [Function("AddProduct")]
        public async Task<HttpResponseData> AddProduct(
    [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequestData req, FunctionContext executionContext)
        {
            var logger = executionContext.GetLogger("AddProduct");
            logger.LogInformation("Processing a request to add a product.");

            // Read the request body
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            logger.LogInformation("Received request body: {RequestBody}", requestBody);

            Product product;
            try
            {
                // Deserialize the product from the request body
                product = JsonSerializer.Deserialize<Product>(requestBody);
                logger.LogInformation("Deserialized product: {@Product}", product);
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Failed to deserialize product data.");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Invalid product data format.");
                return badResponse;
            }

            // Validate the product data
            if (product == null || string.IsNullOrEmpty(product.Name))
            {
                logger.LogWarning("Product data is null or missing required fields.");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Invalid product data.");
                return badResponse;
            }

            // Create connection to Azure Table Storage
            string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            if (string.IsNullOrEmpty(connectionString))
            {
                logger.LogError("AzureWebJobsStorage connection string is not set.");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("Server configuration error.");
                return errorResponse;
            }

            // Create a TableClient
            var tableClient = new TableClient(connectionString, "Products");
            await tableClient.CreateIfNotExistsAsync();

            // Ensure PartitionKey and RowKey are set
            product.PartitionKey = "ProductPartition"; // Default PartitionKey
            product.RowKey = Guid.NewGuid().ToString(); // Generate a unique RowKey if not set

            // Create a new TableEntity from the Product
            var entity = new TableEntity(product.PartitionKey, product.RowKey)
    {
        { "Name", product.Name },
        { "Price", product.Price },
        { "Description", product.description },
        { "InventoryCount", product.InventoryCount },
        { "ImageUrl", product.ImageUrl }
    };

            try
            {
                // Add the entity to the Azure Table Storage asynchronously
                await tableClient.AddEntityAsync(entity);
                logger.LogInformation("Product added to Table Storage with RowKey '{RowKey}'.", product.RowKey);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error adding product to Table Storage.");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while adding the product.");
                return errorResponse;
            }

            // Create and return an HTTP response
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync($"Product '{product.Name}' added successfully with RowKey '{product.RowKey}'.");
            return response;
        }



        // Get All Products
        [Function("GetAllProducts")]
        public async Task<HttpResponseData> GetAllProducts(
            [HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req)
        {
            _logger.LogInformation("Processing a request to get all products.");

            string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";

            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("AzureWebJobsStorage environment variable is not set.");
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("Server configuration error.");
                return errorResponse;
            }

            var tableClient = new TableClient(connectionString, "Products");

            var products = new List<Product>();
            await foreach (var product in tableClient.QueryAsync<Product>())
            {
                products.Add(product);
            }

            var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
            await response.WriteAsJsonAsync(products);
            return response;
        }

        // Get a Product by RowKey
        [Function("GetProduct")]
        public async Task<HttpResponseData> GetProduct(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "GetProduct/{rowKey}")] HttpRequestData req,
            string rowKey)
        {
            _logger.LogInformation($"Processing a request to get product with RowKey: {rowKey}");

            string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";

            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("AzureWebJobsStorage environment variable is not set.");
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("Server configuration error.");
                return errorResponse;
            }

            var tableClient = new TableClient(connectionString, "Products");

            try
            {
                var product = await tableClient.GetEntityAsync<Product>("ProductPartition", rowKey);

                var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
                await response.WriteAsJsonAsync(product.Value);
                return response;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogWarning($"Product with RowKey '{rowKey}' not found.");
                var notFoundResponse = req.CreateResponse(System.Net.HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Product not found.");
                return notFoundResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving product.");
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("Error retrieving product.");
                return errorResponse;
            }
        }

        // Update a Product
        [Function("UpdateProduct")]
        public async Task<HttpResponseData> UpdateProduct(
            [HttpTrigger(AuthorizationLevel.Function, "put", Route = "UpdateProduct/{rowKey}")] HttpRequestData req,
            string rowKey)
        {
            _logger.LogInformation($"Processing a request to update product with RowKey: {rowKey}");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var updatedProduct = JsonSerializer.Deserialize<Product>(requestBody);

            if (updatedProduct == null || string.IsNullOrEmpty(updatedProduct.Name))
            {
                var badResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Invalid product data.");
                return badResponse;
            }

            string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";

            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("AzureWebJobsStorage environment variable is not set.");
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("Server configuration error.");
                return errorResponse;
            }

            var tableClient = new TableClient(connectionString, "Products");

            try
            {
                // Retrieve the existing product
                var existingProduct = await tableClient.GetEntityAsync<Product>("ProductPartition", rowKey);

                // Update the product properties
                existingProduct.Value.Name = updatedProduct.Name;
                existingProduct.Value.Price = updatedProduct.Price;
                existingProduct.Value.description = updatedProduct.description;
                existingProduct.Value.ImageUrl = updatedProduct.ImageUrl;
                existingProduct.Value.InventoryCount = updatedProduct.InventoryCount;

                // Update the entity in the table
                await tableClient.UpdateEntityAsync(existingProduct.Value, existingProduct.Value.ETag, TableUpdateMode.Replace);

                var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
                await response.WriteStringAsync($"Product '{updatedProduct.Name}' updated successfully.");
                return response;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogWarning($"Product with RowKey '{rowKey}' not found.");
                var notFoundResponse = req.CreateResponse(System.Net.HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Product not found.");
                return notFoundResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating product.");
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("Error updating product.");
                return errorResponse;
            }
        }

        // Delete a Product
        [Function("DeleteProduct")]
        public async Task<HttpResponseData> DeleteProduct(
            [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "DeleteProduct/{rowKey}")] HttpRequestData req,
            string rowKey)
        {
            _logger.LogInformation($"Processing a request to delete product with RowKey: {rowKey}");

            string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "";

            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("AzureWebJobsStorage environment variable is not set.");
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("Server configuration error.");
                return errorResponse;
            }

            var tableClient = new TableClient(connectionString, "Products");

            try
            {
                await tableClient.DeleteEntityAsync("ProductPartition", rowKey);

                var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
                await response.WriteStringAsync($"Product with RowKey '{rowKey}' deleted successfully.");
                return response;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogWarning($"Product with RowKey '{rowKey}' not found.");
                var notFoundResponse = req.CreateResponse(System.Net.HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync("Product not found.");
                return notFoundResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting product.");
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("Error deleting product.");
                return errorResponse;
            }
        }
    }

    // Product entity to store in Azure Table Storage
    public class Product : ITableEntity
    {
        public string PartitionKey { get; set; } = "ProductPartition";
        public string RowKey { get; set; } // Acts as the unique identifier

        public string Name { get; set; }
        public double Price { get; set; }
        public string description { get; set; }
        public string ImageUrl { get; set; }
        public int InventoryCount { get; set; }

        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
