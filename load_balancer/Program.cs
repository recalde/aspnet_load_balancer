using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Amazon.DynamoDBv2;
using YourProject.Interfaces;
using YourProject.StateManagers;
using YourProject.Models;
using YourProject.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Environment variables
var stateManagerType = Environment.GetEnvironmentVariable("STATE_MANAGER_TYPE") ?? "Redis";
var retentionHours = int.Parse(Environment.GetEnvironmentVariable("RETENTION_HOURS") ?? "24");
var destinationUrlsConfig = Environment.GetEnvironmentVariable("DESTINATION_URLS") ?? "";

// Parse Destination URLs from environment variable
var destinationEndpoints = new List<DestinationEndpoint>();

if (!string.IsNullOrEmpty(destinationUrlsConfig))
{
    var destinations = destinationUrlsConfig.Split(';');
    foreach (var dest in destinations)
    {
        var parts = dest.Split('|');
        if (parts.Length >= 6)
        {
            destinationEndpoints.Add(new DestinationEndpoint
            {
                Name = parts[0],
                Order = int.Parse(parts[1]),
                ConcurrentCapacity = int.Parse(parts[2]),
                TotalFileSizeCapacity = long.Parse(parts[3]),
                IndividualFileSizeCapacity = long.Parse(parts[4]),
                Url = parts[5]
            });
        }
    }
}

// Register DestinationEndpoints
builder.Services.AddSingleton(destinationEndpoints);

// Configure State Manager
switch (stateManagerType)
{
    case "File":
        var baseDirectory = Environment.GetEnvironmentVariable("FILE_STATE_MANAGER_DIRECTORY") ?? "StateFiles";
        builder.Services.AddSingleton<IStateManager>(sp =>
        {
            return new FileStateManager(baseDirectory, TimeSpan.FromHours(retentionHours), destinationEndpoints);
        });
        break;

    case "DynamoDB":
        var dynamoDbTableName = Environment.GetEnvironmentVariable("DYNAMODB_TABLE_NAME") ?? "CalculationRequests";
        var dynamoDbCapacityTableName = Environment.GetEnvironmentVariable("DYNAMODB_CAPACITY_TABLE_NAME") ?? "EndpointCapacities";
        builder.Services.AddAWSService<IAmazonDynamoDB>();
        builder.Services.AddSingleton<IStateManager>(sp =>
        {
            var dynamoDb = sp.GetRequiredService<IAmazonDynamoDB>();
            return new DynamoDbStateManager(dynamoDb, dynamoDbTableName, dynamoDbCapacityTableName, TimeSpan.FromHours(retentionHours), destinationEndpoints);
        });
        break;

    case "Redis":
    default:
        var redisConnectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING") ?? "localhost:6379";
        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnectionString;
            options.InstanceName = "LoadBalancerInstance";
        });
        builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnectionString));
        builder.Services.AddSingleton<IStateManager>(sp =>
        {
            var cache = sp.GetRequiredService<IDistributedCache>();
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            return new RedisStateManager(cache, redis, destinationEndpoints);
        });
        break;
}

// Add other services
builder.Services.AddHttpClient();
builder.Services.AddHostedService<RetentionCleanupService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseAuthorization();

app.MapControllers();

app.Run();