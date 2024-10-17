using System.Text.Json;
using YourProject.Interfaces;
using YourProject.Models;

namespace YourProject.StateManagers
{
    public class FileStateManager : IStateManager
    {
        private readonly string _baseDirectory;
        private readonly TimeSpan _retentionPeriod;
        private readonly List<DestinationEndpoint> _destinationEndpoints;

        public FileStateManager(string baseDirectory, TimeSpan retentionPeriod, List<DestinationEndpoint> destinationEndpoints)
        {
            _baseDirectory = baseDirectory;
            _retentionPeriod = retentionPeriod;
            _destinationEndpoints = destinationEndpoints;

            // Ensure directories exist
            Directory.CreateDirectory(_baseDirectory);
            Directory.CreateDirectory(GetCapacityDirectory());
        }

        // Calculation request methods...

        public async Task SaveCalculationRequestAsync(CalculationRequest request)
        {
            var filePath = GetFilePath(request.CalculationId);
            var serializedData = JsonSerializer.Serialize(request);

            await File.WriteAllTextAsync(filePath, serializedData);
        }

        public async Task<CalculationRequest?> GetCalculationRequestAsync(string calculationId)
        {
            var filePath = GetFilePath(calculationId);
            if (!File.Exists(filePath))
                return null;

            var serializedData = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<CalculationRequest>(serializedData);
        }

        public Task RemoveCalculationRequestAsync(string calculationId)
        {
            var filePath = GetFilePath(calculationId);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            return Task.CompletedTask;
        }

        public Task<IEnumerable<CalculationRequest>> GetCalculationsInProgressAsync()
        {
            var files = Directory.GetFiles(_baseDirectory, "*.json");
            var calculations = files.Select(file =>
            {
                var serializedData = File.ReadAllText(file);
                return JsonSerializer.Deserialize<CalculationRequest>(serializedData);
            });

            return Task.FromResult(calculations);
        }

        public Task<IEnumerable<CalculationRequest>> GetCalculationHistoryAsync(int hours)
        {
            var cutoff = DateTime.UtcNow - TimeSpan.FromHours(hours);
            var files = Directory.GetFiles(_baseDirectory, "*.json")
                .Where(file => File.GetCreationTimeUtc(file) >= cutoff);

            var calculations = files.Select(file =>
            {
                var serializedData = File.ReadAllText(file);
                return JsonSerializer.Deserialize<CalculationRequest>(serializedData);
            });

            return Task.FromResult(calculations);
        }

        public Task CleanupOldEntriesAsync(TimeSpan retentionPeriod)
        {
            var cutoff = DateTime.UtcNow - retentionPeriod;
            var files = Directory.GetFiles(_baseDirectory, "*.json")
                .Where(file => File.GetCreationTimeUtc(file) < cutoff);

            foreach (var file in files)
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Handle exceptions (e.g., file in use)
                }
            }

            return Task.CompletedTask;
        }

        private string GetFilePath(string calculationId)
        {
            return Path.Combine(_baseDirectory, $"{calculationId}.json");
        }

        // Capacity tracking methods...

        public async Task<bool> TryAcquireEndpointCapacityAsync(string endpointName, long fileSize)
        {
            var endpoint = _destinationEndpoints.FirstOrDefault(e => e.Name == endpointName);
            if (endpoint == null)
                throw new Exception($"Endpoint {endpointName} not found");

            var capacityFilePath = GetCapacityFilePath(endpointName);

            // Use a lock file to synchronize access
            var lockFilePath = capacityFilePath + ".lock";
            using (var lockFile = File.Open(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                try
                {
                    // Lock the file
                    lockFile.Lock(0, 0);

                    // Read current capacity
                    EndpointCapacityStatus? capacityStatus = await ReadCapacityStatusAsync(capacityFilePath) ?? new EndpointCapacityStatus
                    {
                        EndpointName = endpointName,
                        CurrentConcurrentRequests = 0,
                        CurrentTotalFileSize = 0
                    };

                    // Check capacities
                    if (capacityStatus.CurrentConcurrentRequests >= endpoint.ConcurrentCapacity ||
                        (capacityStatus.CurrentTotalFileSize + fileSize) > endpoint.TotalFileSizeCapacity)
                    {
                        return false;
                    }

                    // Update capacities
                    capacityStatus.CurrentConcurrentRequests++;
                    capacityStatus.CurrentTotalFileSize += fileSize;

                    // Write back to the capacity file
                    await WriteCapacityStatusAsync(capacityFilePath, capacityStatus);

                    return true;
                }
                finally
                {
                    // Unlock and close the lock file
                    lockFile.Unlock(0, 0);
                    lockFile.Close();
                }
            }
        }

        public async Task ReleaseEndpointCapacityAsync(string endpointName, long fileSize)
        {
            var capacityFilePath = GetCapacityFilePath(endpointName);

            // Use a lock file to synchronize access
            var lockFilePath = capacityFilePath + ".lock";
            using (var lockFile = File.Open(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                try
                {
                    // Lock the file
                    lockFile.Lock(0, 0);

                    // Read current capacity
                    EndpointCapacityStatus? capacityStatus = await ReadCapacityStatusAsync(capacityFilePath);
                    if (capacityStatus == null)
                    {
                        // This should not happen; log error or throw exception
                        return;
                    }

                    // Update capacities
                    capacityStatus.CurrentConcurrentRequests = Math.Max(0, capacityStatus.CurrentConcurrentRequests - 1);
                    capacityStatus.CurrentTotalFileSize = Math.Max(0, capacityStatus.CurrentTotalFileSize - fileSize);

                    // Write back to the capacity file
                    await WriteCapacityStatusAsync(capacityFilePath, capacityStatus);
                }
                finally
                {
                    // Unlock and close the lock file
                    lockFile.Unlock(0, 0);
                    lockFile.Close();
                }
            }
        }

        public async Task<IEnumerable<EndpointCapacityStatus>> GetEndpointCapacitiesAsync()
        {
            var capacities = new List<EndpointCapacityStatus>();
            foreach (var endpoint in _destinationEndpoints)
            {
                var capacityFilePath = GetCapacityFilePath(endpoint.Name);
                EndpointCapacityStatus? capacityStatus = await ReadCapacityStatusAsync(capacityFilePath) ?? new EndpointCapacityStatus
                {
                    EndpointName = endpoint.Name,
                    CurrentConcurrentRequests = 0,
                    CurrentTotalFileSize = 0
                };
                capacities.Add(capacityStatus);
            }
            return capacities;
        }

        // Helper methods
        private string GetCapacityDirectory()
        {
            return Path.Combine(_baseDirectory, "capacities");
        }

        private string GetCapacityFilePath(string endpointName)
        {
            return Path.Combine(GetCapacityDirectory(), $"{endpointName}.json");
        }

        private async Task<EndpointCapacityStatus?> ReadCapacityStatusAsync(string capacityFilePath)
        {
            if (!File.Exists(capacityFilePath))
                return null;

            var content = await File.ReadAllTextAsync(capacityFilePath);
            return JsonSerializer.Deserialize<EndpointCapacityStatus>(content);
        }

        private async Task WriteCapacityStatusAsync(string capacityFilePath, EndpointCapacityStatus capacityStatus)
        {
            var content = JsonSerializer.Serialize(capacityStatus);
            await File.WriteAllTextAsync(capacityFilePath, content);
        }
    }
}