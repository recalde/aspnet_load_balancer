using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;
using YourProject.Interfaces;
using YourProject.Models;

namespace YourProject.StateManagers
{
    public class RedisStateManager : IStateManager
    {
        private readonly IDistributedCache _cache;
        private readonly IConnectionMultiplexer _redis;
        private readonly List<DestinationEndpoint> _destinationEndpoints;
        private readonly IDatabase _db;
        private const string EndpointCapacityKeyPrefix = "EndpointCapacity:";

        public RedisStateManager(IDistributedCache cache, IConnectionMultiplexer redis, List<DestinationEndpoint> destinationEndpoints)
        {
            _cache = cache;
            _redis = redis;
            _destinationEndpoints = destinationEndpoints;
            _db = _redis.GetDatabase();
        }

        // Calculation request methods...

        public async Task SaveCalculationRequestAsync(CalculationRequest request)
        {
            var cacheKey = $"Calculation:{request.CalculationId}";
            var serializedData = JsonSerializer.Serialize(request);
            await _cache.SetStringAsync(cacheKey, serializedData);

            await _db.SetAddAsync("CalculationsInProgress", request.CalculationId);
        }

        public async Task<CalculationRequest?> GetCalculationRequestAsync(string calculationId)
        {
            var cacheKey = $"Calculation:{calculationId}";
            var serializedData = await _cache.GetStringAsync(cacheKey);

            return string.IsNullOrEmpty(serializedData)
                ? null
                : JsonSerializer.Deserialize<CalculationRequest>(serializedData);
        }

        public async Task RemoveCalculationRequestAsync(string calculationId)
        {
            var cacheKey = $"Calculation:{calculationId}";
            await _cache.RemoveAsync(cacheKey);
            await _db.SetRemoveAsync("CalculationsInProgress", calculationId);

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await _db.SortedSetAddAsync("CalculationsHistory", calculationId, timestamp);
        }

        public async Task<IEnumerable<CalculationRequest>> GetCalculationsInProgressAsync()
        {
            var calculationIds = await _db.SetMembersAsync("CalculationsInProgress");
            var calculations = new List<CalculationRequest>();

            foreach (var id in calculationIds)
            {
                var request = await GetCalculationRequestAsync(id);
                if (request != null)
                {
                    calculations.Add(request);
                }
            }

            return calculations;
        }

        public async Task<IEnumerable<CalculationRequest>> GetCalculationHistoryAsync(int hours)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var since = now - (hours * 3600);
            var calculationIds = await _db.SortedSetRangeByScoreAsync("CalculationsHistory", since, now);

            var calculations = new List<CalculationRequest>();
            foreach (var id in calculationIds)
            {
                var request = await GetCalculationRequestAsync(id);
                if (request != null)
                {
                    calculations.Add(request);
                }
            }

            return calculations;
        }

        public Task CleanupOldEntriesAsync(TimeSpan retentionPeriod)
        {
            // Redis has built-in expiry; no need for explicit cleanup
            return Task.CompletedTask;
        }

        // Capacity tracking methods...

        public async Task<bool> TryAcquireEndpointCapacityAsync(string endpointName, long fileSize)
        {
            var endpoint = _destinationEndpoints.First(e => e.Name == endpointName);
            var transaction = _db.CreateTransaction();

            // Keys for current capacities
            var concurrentRequestsKey = $"{EndpointCapacityKeyPrefix}{endpointName}:ConcurrentRequests";
            var totalFileSizeKey = $"{EndpointCapacityKeyPrefix}{endpointName}:TotalFileSize";

            // Get current capacities
            var currentConcurrentRequests = (int)(await _db.StringGetAsync(concurrentRequestsKey) ?? 0);
            var currentTotalFileSize = (long)(await _db.StringGetAsync(totalFileSizeKey) ?? 0);

            // Check if capacities are available
            if (currentConcurrentRequests >= endpoint.ConcurrentCapacity ||
                (currentTotalFileSize + fileSize) > endpoint.TotalFileSizeCapacity)
            {
                return false;
            }

            // Increment capacities atomically
            transaction.AddCondition(Condition.StringEqual(concurrentRequestsKey, currentConcurrentRequests));
            transaction.AddCondition(Condition.StringEqual(totalFileSizeKey, currentTotalFileSize));

            transaction.StringIncrementAsync(concurrentRequestsKey);
            transaction.StringIncrementAsync(totalFileSizeKey, fileSize);

            var committed = await transaction.ExecuteAsync();
            return committed;
        }

        public async Task ReleaseEndpointCapacityAsync(string endpointName, long fileSize)
        {
            var concurrentRequestsKey = $"{EndpointCapacityKeyPrefix}{endpointName}:ConcurrentRequests";
            var totalFileSizeKey = $"{EndpointCapacityKeyPrefix}{endpointName}:TotalFileSize";

            // Decrement capacities
            await _db.StringDecrementAsync(concurrentRequestsKey);
            await _db.StringDecrementAsync(totalFileSizeKey, fileSize);
        }

        public async Task<IEnumerable<EndpointCapacityStatus>> GetEndpointCapacitiesAsync()
        {
            var capacities = new List<EndpointCapacityStatus>();

            foreach (var endpoint in _destinationEndpoints)
            {
                var concurrentRequestsKey = $"{EndpointCapacityKeyPrefix}{endpoint.Name}:ConcurrentRequests";
                var totalFileSizeKey = $"{EndpointCapacityKeyPrefix}{endpoint.Name}:TotalFileSize";

                var currentConcurrentRequests = (int)(await _db.StringGetAsync(concurrentRequestsKey) ?? 0);
                var currentTotalFileSize = (long)(await _db.StringGetAsync(totalFileSizeKey) ?? 0);

                capacities.Add(new EndpointCapacityStatus
                {
                    EndpointName = endpoint.Name,
                    CurrentConcurrentRequests = currentConcurrentRequests,
                    CurrentTotalFileSize = currentTotalFileSize
                });
            }

            return capacities;
        }
    }
}