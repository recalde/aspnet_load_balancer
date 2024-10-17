using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using YourProject.Interfaces;
using YourProject.Models;

namespace YourProject.StateManagers
{
    public class DynamoDbStateManager : IStateManager
    {
        private readonly IAmazonDynamoDB _dynamoDb;
        private readonly string _tableName;
        private readonly string _capacityTableName;
        private readonly TimeSpan _retentionPeriod;
        private readonly List<DestinationEndpoint> _destinationEndpoints;

        public DynamoDbStateManager(IAmazonDynamoDB dynamoDb, string tableName, string capacityTableName, TimeSpan retentionPeriod, List<DestinationEndpoint> destinationEndpoints)
        {
            _dynamoDb = dynamoDb;
            _tableName = tableName;
            _capacityTableName = capacityTableName;
            _retentionPeriod = retentionPeriod;
            _destinationEndpoints = destinationEndpoints;
        }

        // Calculation request methods...

        public async Task SaveCalculationRequestAsync(CalculationRequest request)
        {
            var item = new Dictionary<string, AttributeValue>
            {
                {"CalculationId", new AttributeValue { S = request.CalculationId } },
                {"Data", new AttributeValue { S = JsonSerializer.Serialize(request) } },
                {"CreatedAt", new AttributeValue { N = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() } }
            };

            var putItemRequest = new PutItemRequest
            {
                TableName = _tableName,
                Item = item
            };

            await _dynamoDb.PutItemAsync(putItemRequest);
        }

        public async Task<CalculationRequest?> GetCalculationRequestAsync(string calculationId)
        {
            var getItemRequest = new GetItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    {"CalculationId", new AttributeValue { S = calculationId } }
                }
            };

            var response = await _dynamoDb.GetItemAsync(getItemRequest);
            if (response.Item == null || !response.Item.ContainsKey("Data"))
                return null;

            var serializedData = response.Item["Data"].S;
            return JsonSerializer.Deserialize<CalculationRequest>(serializedData);
        }

        public async Task RemoveCalculationRequestAsync(string calculationId)
        {
            var deleteItemRequest = new DeleteItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    {"CalculationId", new AttributeValue { S = calculationId } }
                }
            };

            await _dynamoDb.DeleteItemAsync(deleteItemRequest);
        }

        public async Task<IEnumerable<CalculationRequest>> GetCalculationsInProgressAsync()
        {
            var cutoff = DateTimeOffset.UtcNow - _retentionPeriod;

            var scanRequest = new ScanRequest
            {
                TableName = _tableName,
                FilterExpression = "CreatedAt >= :cutoff",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    {":cutoff", new AttributeValue { N = cutoff.ToUnixTimeSeconds().ToString() } }
                }
            };

            var response = await _dynamoDb.ScanAsync(scanRequest);
            var calculations = response.Items.Select(item =>
            {
                var serializedData = item["Data"].S;
                return JsonSerializer.Deserialize<CalculationRequest>(serializedData);
            });

            return calculations;
        }

        public async Task<IEnumerable<CalculationRequest>> GetCalculationHistoryAsync(int hours)
        {
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromHours(hours);

            var scanRequest = new ScanRequest
            {
                TableName = _tableName,
                FilterExpression = "CreatedAt >= :cutoff",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    {":cutoff", new AttributeValue { N = cutoff.ToUnixTimeSeconds().ToString() } }
                }
            };

            var response = await _dynamoDb.ScanAsync(scanRequest);
            var calculations = response.Items.Select(item =>
            {
                var serializedData = item["Data"].S;
                return JsonSerializer.Deserialize<CalculationRequest>(serializedData);
            });

            return calculations;
        }

        public async Task CleanupOldEntriesAsync(TimeSpan retentionPeriod)
        {
            var cutoff = DateTimeOffset.UtcNow - retentionPeriod;

            var scanRequest = new ScanRequest
            {
                TableName = _tableName,
                FilterExpression = "CreatedAt < :cutoff",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    {":cutoff", new AttributeValue { N = cutoff.ToUnixTimeSeconds().ToString() } }
                },
                ProjectionExpression = "CalculationId"
            };

            var response = await _dynamoDb.ScanAsync(scanRequest);

            var deleteTasks = response.Items.Select(item =>
            {
                var calculationId = item["CalculationId"].S;
                return RemoveCalculationRequestAsync(calculationId);
            });

            await Task.WhenAll(deleteTasks);
        }

        // Capacity tracking methods...

        public async Task<bool> TryAcquireEndpointCapacityAsync(string endpointName, long fileSize)
        {
            var endpoint = _destinationEndpoints.FirstOrDefault(e => e.Name == endpointName);
            if (endpoint == null)
                throw new Exception($"Endpoint {endpointName} not found");

            var key = new Dictionary<string, AttributeValue>
            {
                { "EndpointName", new AttributeValue { S = endpointName } }
            };

            // Retrieve current capacities
            var getItemResponse = await _dynamoDb.GetItemAsync(new GetItemRequest
            {
                TableName = _capacityTableName,
                Key = key,
                ConsistentRead = true
            });

            var currentConcurrentRequests = getItemResponse.Item != null && getItemResponse.Item.ContainsKey("CurrentConcurrentRequests")
                ? int.Parse(getItemResponse.Item["CurrentConcurrentRequests"].N)
                : 0;

            var currentTotalFileSize = getItemResponse.Item != null && getItemResponse.Item.ContainsKey("CurrentTotalFileSize")
                ? long.Parse(getItemResponse.Item["CurrentTotalFileSize"].N)
                : 0;

            // Check capacities
            if (currentConcurrentRequests >= endpoint.ConcurrentCapacity ||
                (currentTotalFileSize + fileSize) > endpoint.TotalFileSizeCapacity)
            {
                return false;
            }

            // Update capacities conditionally
            var updateItemRequest = new UpdateItemRequest
            {
                TableName = _capacityTableName,
                Key = key,
                UpdateExpression = "SET CurrentConcurrentRequests = if_not_exists(CurrentConcurrentRequests, :zero) + :inc, " +
                                   "CurrentTotalFileSize = if_not_exists(CurrentTotalFileSize, :zero) + :fileSize",
                ConditionExpression = "CurrentConcurrentRequests < :maxConcurrent AND if_not_exists(CurrentTotalFileSize, :zero) + :fileSize <= :maxFileSize",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    { ":inc", new AttributeValue { N = "1" } },
                    { ":fileSize", new AttributeValue { N = fileSize.ToString() } },
                    { ":maxConcurrent", new AttributeValue { N = endpoint.ConcurrentCapacity.ToString() } },
                    { ":maxFileSize", new AttributeValue { N = endpoint.TotalFileSizeCapacity.ToString() } },
                    { ":zero", new AttributeValue { N = "0" } }
                }
            };

            try
            {
                await _dynamoDb.UpdateItemAsync(updateItemRequest);
                return true;
            }
            catch (ConditionalCheckFailedException)
            {
                return false;
            }
        }

        public async Task ReleaseEndpointCapacityAsync(string endpointName, long fileSize)
        {
            var key = new Dictionary<string, AttributeValue>
            {
                { "EndpointName", new AttributeValue { S = endpointName } }
            };

            // Decrement capacities
            var updateItemRequest = new UpdateItemRequest
            {
                TableName = _capacityTableName,
                Key = key,
                UpdateExpression = "SET CurrentConcurrentRequests = CurrentConcurrentRequests - :dec, " +
                                   "CurrentTotalFileSize = CurrentTotalFileSize - :fileSize",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    { ":dec", new AttributeValue { N = "1" } },
                    { ":fileSize", new AttributeValue { N = fileSize.ToString() } }
                }
            };

            await _dynamoDb.UpdateItemAsync(updateItemRequest);
        }

        public async Task<IEnumerable<EndpointCapacityStatus>> GetEndpointCapacitiesAsync()
        {
            var scanRequest = new ScanRequest
            {
                TableName = _capacityTableName
            };

            var response = await _dynamoDb.ScanAsync(scanRequest);
            var capacities = response.Items.Select(item => new EndpointCapacityStatus
            {
                EndpointName = item["EndpointName"].S,
                CurrentConcurrentRequests = int.Parse(item["CurrentConcurrentRequests"].N),
                CurrentTotalFileSize = long.Parse(item["CurrentTotalFileSize"].N)
            });

            return capacities;
        }
    }
}