using Microsoft.AspNetCore.Mvc;
using YourProject.Interfaces;
using System.Text;
using System.Net.Http.Headers;

namespace YourProject.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class CallbackController : ControllerBase
    {
        private readonly IStateManager _stateManager;
        private readonly IHttpClientFactory _httpClientFactory;

        public CallbackController(IStateManager stateManager, IHttpClientFactory httpClientFactory)
        {
            _stateManager = stateManager;
            _httpClientFactory = httpClientFactory;
        }

        [HttpPost]
        public async Task<IActionResult> PostCallback()
        {
            // Extract calculationId from query string
            var calculationId = HttpContext.Request.Query["calculationId"].ToString();

            if (string.IsNullOrEmpty(calculationId))
            {
                return BadRequest("calculationId is required");
            }

            // Look up the calculationId
            var calculationRequest = await _stateManager.GetCalculationRequestAsync(calculationId);

            if (calculationRequest == null)
            {
                return NotFound("CalculationId not found");
            }

            // Release capacities
            if (!string.IsNullOrEmpty(calculationRequest.AssignedEndpointName))
            {
                await _stateManager.ReleaseEndpointCapacityAsync(calculationRequest.AssignedEndpointName, calculationRequest.InputFileSize);
            }

            // Remove the calculation from the state manager
            await _stateManager.RemoveCalculationRequestAsync(calculationId);

            // Forward the callback to the callbackUrl
            var client = _httpClientFactory.CreateClient();

            // Copy headers
            foreach (var header in Request.Headers)
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value.ToString());
            }

            // Read the body
            using var reader = new StreamReader(HttpContext.Request.Body);
            var bodyContent = await reader.ReadToEndAsync();

            var content = new StringContent(bodyContent, Encoding.UTF8, Request.ContentType);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(Request.ContentType));

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync(calculationRequest.CallbackUrl, content);
            }
            catch (Exception ex)
            {
                return StatusCode(502, $"Failed to forward callback: {ex.Message}");
            }

            // Return the response from the callback URL
            var responseContent = await response.Content.ReadAsStringAsync();
            return new ContentResult
            {
                StatusCode = (int)response.StatusCode,
                Content = responseContent,
                ContentType = response.Content.Headers.ContentType?.ToString()
            };
        }
    }
}