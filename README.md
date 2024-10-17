# Load Balancer Application

This application is a load balancer implemented in C# using ASP.NET Core Web API. It acts as a man-in-the-middle between clients and a collection of destination URLs, forwarding `/calculate` requests and handling `/callback` responses. It supports scalable state management and is designed to run in a Kubernetes environment.

## Features

### 1. API Endpoints

- **`POST /calculate`**
  - Accepts calculation requests with `calculationId` and other parameters.
  - Parses payload and query string arguments.
  - Forwards the request to a suitable destination endpoint based on load balancing logic.
  - Tracks the calculation request in state management.

- **`POST /callback`**
  - Receives callbacks from destination endpoints.
  - Looks up the original calculation request using `calculationId`.
  - Forwards the callback to the original `callbackUrl`.
  - Releases capacities and updates state management.

- **`GET /status`**
  - Returns a JSON summary of calculations in progress.
  - Includes current capacities of destination endpoints.

- **`GET /history`**
  - Returns recent calculation requests from the previous specified hours (default is 24).

### 2. Load Balancing and Capacity Management

- **Destination Endpoints Configuration**
  - Configured via environment variable `DESTINATION_URLS`.
  - Each endpoint has:
    - `Name`
    - `Order`
    - `ConcurrentCapacity` (max concurrent requests)
    - `TotalFileSizeCapacity` (max total file size of concurrent requests)
    - `IndividualFileSizeCapacity` (max individual request file size)
    - `Url`

- **Load Balancing Logic**
  - Selects a destination endpoint based on:
    - Individual request file size capacity.
    - Endpoint order (priority).
    - Available capacities (concurrent requests and total file size).
  - Uses shared state management for capacity tracking across replicas.

- **Capacity Tracking**
  - Acquires capacities atomically using state management.
  - Releases capacities after processing is completed or failed.
  - Ensures global awareness of endpoint loads in a multi-replica environment.

### 3. State Management

- **State Manager Interface (`IStateManager`)**
  - Defines methods for:
    - Saving, retrieving, and removing calculation requests.
    - Getting calculations in progress and history.
    - Cleaning up old entries based on retention policy.
    - Capacity tracking methods for endpoints.

- **Implementations**
  - **RedisStateManager**
    - Uses Redis for scalable state management.
    - Handles capacity tracking using Redis transactions and atomic operations.
  - **FileStateManager**
    - Uses shared file storage with file locking for state management.
    - Coordinates capacities across replicas using lock files.
  - **DynamoDbStateManager**
    - Uses AWS DynamoDB for state management.
    - Handles capacity tracking using conditional writes.

- **Retention Policy**
  - Cleans up calculation requests older than a specified retention period (`RETENTION_HOURS` environment variable).
  - Implemented via `RetentionCleanupService`, which runs periodically.

### 4. Configuration and Deployment

- **Environment Variables**
  - `STATE_MANAGER_TYPE`: "Redis", "File", or "DynamoDB".
  - `RETENTION_HOURS`: Number of hours to retain calculation requests.
  - `DESTINATION_URLS`: Configuration string for destination endpoints.
  - State manager-specific configurations:
    - Redis: `REDIS_CONNECTION_STRING`.
    - File: `FILE_STATE_MANAGER_DIRECTORY`.
    - DynamoDB: `DYNAMODB_TABLE_NAME`, `DYNAMODB_CAPACITY_TABLE_NAME`.

- **Kubernetes Deployment**
  - Application designed to run in Kubernetes pods/containers.
  - Supports scaling with multiple replicas.
  - For FileStateManager, ensure shared file storage is accessible (e.g., via Persistent Volume Claim).

- **Debugging and Development**
  - Project includes `.csproj` and `launchSettings.json` for debugging in Visual Studio Code.
  - Uses internal terminal for running and debugging the application.

### 5. Error Handling and Concurrency

- **Concurrency Management**
  - Uses atomic operations and locks to manage capacities across replicas.
  - Ensures capacities are respected globally in a multi-replica environment.

- **Error Handling**
  - Gracefully handles cases where no suitable endpoint is available.
  - Releases capacities in case of failures to prevent capacity leaks.
  - Provides meaningful HTTP status codes and error messages.

- **Timeouts and Retries**
  - Can be extended to include timeouts and retries for robustness.
  - Important for handling cases where callbacks fail or are delayed.

---

## **Getting Started**

### **Prerequisites**

- .NET 7.0 SDK
- Redis server (if using `RedisStateManager`)
- AWS credentials and DynamoDB tables (if using `DynamoDbStateManager`)
- Shared file storage (if using `FileStateManager`)

### **Setup**

1. **Clone or Create the Project**

   Create a new directory and copy the provided project structure and code files.

2. **Install Dependencies**

   Navigate to the project directory and restore NuGet packages:

   ```bash
   dotnet restore