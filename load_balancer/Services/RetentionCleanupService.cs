using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YourProject.Interfaces;

namespace YourProject.Services
{
    public class RetentionCleanupService : BackgroundService
    {
        private readonly IStateManager _stateManager;
        private readonly TimeSpan _retentionPeriod;
        private readonly TimeSpan _cleanupInterval;
        private readonly ILogger<RetentionCleanupService> _logger;

        public RetentionCleanupService(IStateManager stateManager, TimeSpan retentionPeriod, ILogger<RetentionCleanupService> logger)
        {
            _stateManager = stateManager;
            _retentionPeriod = retentionPeriod;
            _cleanupInterval = TimeSpan.FromHours(1); // Run cleanup every hour
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Starting retention cleanup");
                await _stateManager.CleanupOldEntriesAsync(_retentionPeriod);
                _logger.LogInformation("Retention cleanup completed");

                await Task.Delay(_cleanupInterval, stoppingToken);
            }
        }
    }
}