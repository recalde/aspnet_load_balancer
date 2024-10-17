using Microsoft.AspNetCore.Mvc;
using YourProject.Interfaces;

namespace YourProject.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class HistoryController : ControllerBase
    {
        private readonly IStateManager _stateManager;

        public HistoryController(IStateManager stateManager)
        {
            _stateManager = stateManager;
        }

        [HttpGet]
        public async Task<IActionResult> GetHistory(int hours = 24)
        {
            var calculations = await _stateManager.GetCalculationHistoryAsync(hours);
            return Ok(calculations);
        }
    }
}