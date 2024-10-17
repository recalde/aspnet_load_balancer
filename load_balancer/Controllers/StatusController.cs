using Microsoft.AspNetCore.Mvc;
using YourProject.Interfaces;
using YourProject.Models;

namespace YourProject.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class StatusController : ControllerBase
    {
        private readonly IStateManager _stateManager;

        public StatusController(IStateManager stateManager)
        {
            _stateManager = stateManager;
        }

        [HttpGet]
        public async Task<IActionResult> GetStatus()
        {
            var calculations = await _stateManager.GetCalculationsInProgressAsync();
            var capacities = await _stateManager.GetEndpointCapacitiesAsync();

            var status = new
            {
                CalculationsInProgress = calculations,
                EndpointCapacities = capacities
            };

            return Ok(status);
        }
    }
}