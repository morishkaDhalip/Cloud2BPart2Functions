using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Files.Shares;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Cloud2BPart2Functions
{
    public class WriteToFileShareFunction
    {
        private readonly ILogger<WriteToFileShareFunction> _logger;

        public WriteToFileShareFunction(ILogger<WriteToFileShareFunction> logger)
        {
            _logger = logger;
        }

        [Function("WriteToFileShare")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
        {
            _logger.LogInformation("WriteToFileShare function triggered.");

            // Define the file share, directory, and filename
            var fileShareName = "customer-service-files";
            var directoryName = "reviews-complaints"; // Folder in the file share
            var fileName = $"review-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt"; // Dynamic file name

            string storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

            // Create ShareClient
            ShareClient shareClient = new ShareClient(storageConnectionString, fileShareName);
            await shareClient.CreateIfNotExistsAsync();

            // Create DirectoryClient
            ShareDirectoryClient directoryClient = shareClient.GetDirectoryClient(directoryName);
            await directoryClient.CreateIfNotExistsAsync();

            // Create FileClient
            ShareFileClient fileClient = directoryClient.GetFileClient(fileName);

            // Read the review content from the request body
            string reviewContent = await new StreamReader(req.Body).ReadToEndAsync();

            if (string.IsNullOrEmpty(reviewContent))
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteStringAsync("Review content is empty.");
                return badResponse;
            }

            // Write the content to the file in Azure File Share
            using (var stream = new MemoryStream())
            {
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(reviewContent);
                    writer.Flush();
                    stream.Position = 0;

                    await fileClient.CreateAsync(stream.Length);
                    await fileClient.UploadRangeAsync(new HttpRange(0, stream.Length), stream);
                }
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync($"File '{fileName}' written to the Azure File Share in '{directoryName}' directory.");

            return response;
        }
    }
}

