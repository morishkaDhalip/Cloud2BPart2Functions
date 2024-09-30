using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace FunctionApp
{
    public class UploadBlobFunction
    {
        private readonly ILogger _logger;

        public UploadBlobFunction(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<UploadBlobFunction>();
        }

        [Function("UploadBlob")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequestData req)
        {
            _logger.LogInformation("Processing image upload request.");

            // Validate Content-Type header
            if (!req.Headers.TryGetValues("Content-Type", out var contentTypeValues))
            {
                var badResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Missing Content-Type header.");
                return badResponse;
            }

            var contentType = contentTypeValues.FirstOrDefault();
            if (string.IsNullOrEmpty(contentType) || !contentType.Contains("multipart/form-data"))
            {
                var badResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Content-Type must be multipart/form-data.");
                return badResponse;
            }

            // Extract boundary from Content-Type header
            var mediaTypeHeader = MediaTypeHeaderValue.Parse(contentType);
            var boundary = HeaderUtilities.RemoveQuotes(mediaTypeHeader.Boundary).Value;

            if (string.IsNullOrEmpty(boundary))
            {
                var badResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Missing boundary in multipart/form-data.");
                return badResponse;
            }

            var reader = new MultipartReader(boundary, req.Body);
            var section = await reader.ReadNextSectionAsync();

            while (section != null)
            {
                // Check if the section has a Content-Disposition header
                if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition))
                {
                    // This example expects the file to have the form field name "file"
                    if (disposition.DispositionType.Equals("form-data") &&
                        disposition.Name.Equals("file", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrEmpty(disposition.FileName.Value))
                    {
                        var fileName = Path.GetFileName(disposition.FileName.Value);
                        _logger.LogInformation($"Uploading file: {fileName}");

                        // Upload to Blob Storage
                        string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
                        string containerName = "product-images"; // Ensure this container exists or is created

                        var blobServiceClient = new BlobServiceClient(connectionString);
                        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

                        string blobName = $"{Guid.NewGuid()}_{fileName}";
                        var blobClient = containerClient.GetBlobClient(blobName);

                        using (var fileStream = section.Body)
                        {
                            await blobClient.UploadAsync(fileStream, overwrite: true);
                        }

                        string blobUrl = blobClient.Uri.ToString();
                        _logger.LogInformation($"Image uploaded successfully. URL: {blobUrl}");

                        // Return the blob URL as the response
                        var successResponse = req.CreateResponse(System.Net.HttpStatusCode.OK);
                        await successResponse.WriteStringAsync(blobUrl);
                        return successResponse;
                    }
                }

                // Read the next section
                section = await reader.ReadNextSectionAsync();
            }

            // If no file was found in the request
            var noFileResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await noFileResponse.WriteStringAsync("No file found in the request.");
            return noFileResponse;
        }
    }
}
