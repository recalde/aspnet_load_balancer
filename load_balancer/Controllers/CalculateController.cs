using Microsoft.AspNetCore.Mvc;
using YourProject.Interfaces;
using YourProject.Models;
using System.Text;
using System.Net.Http.Headers;

namespace YourProject.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class CalculateController : ControllerBase
    {
        private readonly IStateManager _stateManager;
        private readonly List<DestinationEndpoint> _destinationEndpoints;
        private readonly IHttpClientFactory _httpClientFactory;

        public CalculateController(IStateManager stateManager, List<DestinationEndpoint> destinationEndpoints, IHttpClientFactory httpClientFactory)
        {
            _stateManager = stateManager;
            _destinationEndpoints = destinationEndpoints;
            _httpClientFactory = httpClientFactory;
        }

        [HttpPost]
        public async Task<IActionResult> PostCalculate()
        {
            // Extract calculationId from query string
            var calculationId = HttpContext.Request.Query["calculationId"].ToString();

            if (string.IsNullOrEmpty(calculationId))
            {
                return BadRequest("calculationId is required");
            }

            // Read the payload body
            using var reader = new StreamReader(HttpContext.Request.Body);
            var body = await reader.ReadToEndAsync();

            // The body is "{inputFilePath}\n{callbackUrl}"
            var lines = body.Split('\n');
            if (lines.Length < 2)
            {
                return BadRequest("Invalid body format. Expected '{inputFilePath}\\n{callbackUrl}'");
            }

            var inputFilePath = lines[0];
            var callbackUrl = lines[1];

            // Collect the weights from query string
            if (!long.TryParse(HttpContext.Request.Query["inputFileSize"], out var inputFileSize))
                return BadRequest("inputFileSize is required and must be a valid long integer");

            if (!int.TryParse(HttpContext.Request.Query["transaction"], out var transaction))
                return BadRequest("transaction is required and must be a valid integer");

            if (!bool.TryParse(HttpContext.Request.Query["manualCache"], out var manualCache))
                return BadRequest("manualCache is required and must be 'true' or 'false'");

            if (!bool.TryParse(HttpContext.Request.Query["expenseCache"], out var expenseCache))
                return BadRequest("expenseCache is required and must be 'true' or 'false'");

            var sourceSystem = HttpContext.Request.Query["sourceSystem"].ToString();

            // Create calculation request object
            var calculationRequest = new CalculationRequest
            {
                CalculationId = calculationId,
                InputFilePath = inputFilePath,
                CallbackUrl = callbackUrl,
                InputFileSize = inputFileSize,
                Transaction = transaction,
                ManualCache = manualCache,
                ExpenseCache = expenseCache,
                SourceSystem = sourceSystem,
                QueryString = HttpContext.Request.QueryString.ToString(),
                CreatedAt = DateTime.UtcNow,
            };

            // Decide which destination URL to forward the request to
            try
            {
                calculationRequest.DestinationUrl = await SelectDestinationUrlAsync(calculationRequest);
            }
            catch (Exception ex)
            {
                return StatusCode(503, ex.Message);
            }

            // Store the calculationId and the necessary data
            await _stateManager.SaveCalculationRequestAsync(calculationRequest);

            // Forward the request to the selected destination URL
            var client = _httpClientFactory.CreateClient();

            // Copy headers
            foreach (var header in Request.Headers)
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value.ToString());
            }

            // Send the request
            var content = new StringContent(body, Encoding.UTF8, Request.ContentType);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(Request.ContentType));

            var destinationUri = calculationRequest.DestinationUrl + Request.Path + Request.QueryString;

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync(destinationUri, content);
            }
            catch (Exception ex)
            {
                // Release capacity in case of failure
                if (!string.IsNullOrEmpty(calculationRequest.AssignedEndpointName))
                {
                    await _stateManager.ReleaseEndpointCapacityAsync(calculationRequest.AssignedEndpointName, calculationRequest.InputFileSize);
                }
                return StatusCode(502, $"Failed to forward request: {ex.Message}");
            }

            // Return the response from the destination
            var responseContent = await response.Content.ReadAsStringAsync();
            return new ContentResult
            {
                StatusCode = (int)response.StatusCode,
                Content = responseContent,
                ContentType = response.Content.Headers.ContentType?.ToString()
            };
        }

        private async Task<string> SelectDestinationUrlAsync(CalculationRequest request)
        {
            // Filter endpoints based on individual file size capacity
            var suitableEndpoints = _destinationEndpoints
                .Where(e => request.InputFileSize <= e.IndividualFileSizeCapacity)
                .OrderBy(e => e.Order)
                .ToList();

            foreach (var endpoint in suitableEndpoints)
            {
                // Try to acquire capacity
                var acquired = await _stateManager.TryAcquireEndpointCapacityAsync(endpoint.Name, request.InputFileSize);

                if (acquired)
                {
                    // Associate the endpoint name with the request for later release
                    request.AssignedEndpointName = endpoint.Name;
                    return endpoint.Url;
                }
            }

            // If no endpoint is available, throw an exception
            throw new Exception("No suitable endpoint available for the request");
        }
    }
}