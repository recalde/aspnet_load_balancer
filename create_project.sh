#!/bin/bash

# Set the project name
PROJECT_NAME="load_balancer"

# Create the main project directory
mkdir -p "$PROJECT_NAME"

cd "$PROJECT_NAME" || exit

# Create directories
mkdir -p Controllers
mkdir -p Interfaces
mkdir -p Models
mkdir -p Services
mkdir -p StateManagers
mkdir -p Properties

# Create empty files in Controllers
touch Controllers/CalculateController.cs
touch Controllers/CallbackController.cs
touch Controllers/StatusController.cs
touch Controllers/HistoryController.cs

# Create empty file in Interfaces
touch Interfaces/IStateManager.cs

# Create empty files in Models
touch Models/CalculationRequest.cs
touch Models/DestinationEndpoint.cs
touch Models/EndpointCapacityStatus.cs

# Create empty file in Services
touch Services/RetentionCleanupService.cs

# Create empty files in StateManagers
touch StateManagers/RedisStateManager.cs
touch StateManagers/FileStateManager.cs
touch StateManagers/DynamoDbStateManager.cs

# Create empty file in Properties
mkdir -p Properties
touch Properties/launchSettings.json

# Create empty files in the root dire