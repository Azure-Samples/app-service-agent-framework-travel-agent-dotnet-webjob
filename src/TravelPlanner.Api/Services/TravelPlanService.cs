using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Cosmos;
using TravelPlanner.Shared.Constants;
using TravelPlanner.Shared.Messages;
using TravelPlanner.Shared.Models;
using System.Text.Json;
using TaskStatus = TravelPlanner.Shared.Models.TaskStatus;

namespace TravelPlanner.Api.Services;

/// <summary>
/// Service for managing travel plan requests, task status tracking, and itinerary retrieval.
/// Handles async request-reply pattern using Service Bus and Cosmos DB.
/// </summary>
public interface ITravelPlanService
{
    /// <summary>
    /// Creates a new travel plan request and queues it for processing
    /// </summary>
    /// <param name="request">The travel plan request details</param>
    /// <returns>Response containing task ID and status URLs</returns>
    Task<TravelPlanResponse> CreateTravelPlanAsync(TravelPlanRequest request);
    
    /// <summary>
    /// Gets the current status of a travel planning task
    /// </summary>
    /// <param name="taskId">The unique task identifier</param>
    /// <returns>Current task status or null if not found</returns>
    Task<TaskStatus?> GetTaskStatusAsync(string taskId);
    
    /// <summary>
    /// Retrieves the completed travel itinerary for a task
    /// </summary>
    /// <param name="taskId">The unique task identifier</param>
    /// <returns>Complete itinerary or null if not found</returns>
    Task<TravelItinerary?> GetTravelItineraryAsync(string taskId);
}

/// <summary>
/// Implementation of travel plan service using Azure Service Bus and Cosmos DB
/// </summary>
public class TravelPlanService : ITravelPlanService
{
    private readonly ServiceBusSender _serviceBusSender;
    private readonly Container _cosmosContainer;
    private readonly ILogger<TravelPlanService> _logger;
    private readonly IConfiguration _configuration;

    public TravelPlanService(
        ServiceBusClient serviceBusClient,
        Container cosmosContainer,
        ILogger<TravelPlanService> logger,
        IConfiguration configuration)
    {
        _cosmosContainer = cosmosContainer;
        _logger = logger;
        _configuration = configuration;
        
        var queueName = configuration["ServiceBus:QueueName"] ?? "travel-plans";
        _serviceBusSender = serviceBusClient.CreateSender(queueName);
    }

    public async Task<TravelPlanResponse> CreateTravelPlanAsync(TravelPlanRequest request)
    {
        var taskId = Guid.NewGuid().ToString("N");
        
        _logger.LogInformation("Creating travel plan task {TaskId} for destination {Destination}", 
            taskId, request.Destination);

        // Create the message for Service Bus
        var message = new TravelPlanMessage
        {
            TaskId = taskId,
            Request = request,
            EnqueuedAt = DateTime.UtcNow
        };

        // Send to Service Bus queue
        var serviceBusMessage = new ServiceBusMessage(JsonSerializer.Serialize(message))
        {
            MessageId = taskId,
            ContentType = "application/json"
        };

        await _serviceBusSender.SendMessageAsync(serviceBusMessage);

        // Store initial status in Cosmos DB with 24-hour TTL
        var taskStatus = new TaskStatus
        {
            TaskId = taskId,
            Status = TaskStatusConstants.Queued,
            ProgressPercentage = 0,
            CurrentStep = "Request queued for processing",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Ttl = 86400 // 24 hours in seconds
        };

        await StoreTaskStatusAsync(taskStatus);

        var baseUrl = _configuration["App:BaseUrl"] ?? "http://localhost:5000";
        
        return new TravelPlanResponse
        {
            TaskId = taskId,
            Status = TaskStatusConstants.Queued,
            StatusUrl = $"{baseUrl}/api/travel-plans/{taskId}",
            ResultUrl = $"{baseUrl}/api/travel-plans/{taskId}/result",
            Message = "Your travel plan is being created. Please check the status URL for updates."
        };
    }

    public async Task<TaskStatus?> GetTaskStatusAsync(string taskId)
    {
        try
        {
            var response = await _cosmosContainer.ReadItemAsync<TaskStatus>(taskId, new PartitionKey(taskId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<TravelItinerary?> GetTravelItineraryAsync(string taskId)
    {
        try
        {
            var resultId = $"{taskId}_result";
            var response = await _cosmosContainer.ReadItemAsync<TravelItineraryDocument>(resultId, new PartitionKey(resultId));
            return response.Resource.Itinerary;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task StoreTaskStatusAsync(TaskStatus status)
    {
        await _cosmosContainer.UpsertItemAsync(status, new PartitionKey(status.TaskId));
    }
}

// Helper class to store travel itinerary with id for Cosmos DB
public class TravelItineraryDocument
{
    public string id { get; set; } = null!;
    public TravelItinerary Itinerary { get; set; } = null!;
}
