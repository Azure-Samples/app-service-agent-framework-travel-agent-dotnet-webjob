using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Cosmos;
using TravelPlanner.Shared.Constants;
using TravelPlanner.Shared.Messages;
using TravelPlanner.Shared.Models;
using TravelPlanner.Api.Services;
using System.Text.Json;
using TaskStatus = TravelPlanner.Shared.Models.TaskStatus;

namespace TravelPlanner.Api.Services;

public class TravelPlanWorker : BackgroundService
{
    private readonly ILogger<TravelPlanWorker> _logger;
    private readonly ServiceBusProcessor _processor;
    private readonly Container _cosmosContainer;
    private readonly IServiceProvider _serviceProvider;

    public TravelPlanWorker(
        ILogger<TravelPlanWorker> logger,
        ServiceBusClient serviceBusClient,
        Container cosmosContainer,
        IServiceProvider serviceProvider,
        IConfiguration configuration)
    {
        _logger = logger;
        _cosmosContainer = cosmosContainer;
        _serviceProvider = serviceProvider;

        var queueName = configuration["ServiceBus:QueueName"] ?? "travel-plans";
        _processor = serviceBusClient.CreateProcessor(queueName, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 1,
            AutoCompleteMessages = false
        });

        _processor.ProcessMessageAsync += MessageHandler;
        _processor.ProcessErrorAsync += ErrorHandler;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Travel Planner Worker starting...");
        
        await _processor.StartProcessingAsync(stoppingToken);
        
        _logger.LogInformation("Worker is now processing messages");

        // Keep the worker running
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }

        _logger.LogInformation("Worker shutting down...");
        await _processor.StopProcessingAsync(stoppingToken);
    }

    private async Task MessageHandler(ProcessMessageEventArgs args)
    {
        var messageBody = args.Message.Body.ToString();
        _logger.LogInformation("Received message: {MessageId}", args.Message.MessageId);

        try
        {
            var message = JsonSerializer.Deserialize<TravelPlanMessage>(messageBody);
            if (message == null)
            {
                _logger.LogWarning("Failed to deserialize message {MessageId}", args.Message.MessageId);
                await args.DeadLetterMessageAsync(args.Message, "InvalidMessage", "Could not deserialize message");
                return;
            }

            // Check if this task is already completed or being processed
            try
            {
                var existingStatus = await _cosmosContainer.ReadItemAsync<TaskStatus>(
                    message.TaskId, 
                    new PartitionKey(message.TaskId));
                
                if (existingStatus.Resource.Status == TaskStatusConstants.Completed)
                {
                    _logger.LogInformation(
                        "Task {TaskId} is already completed. Skipping reprocessing and completing message.",
                        message.TaskId);
                    await args.CompleteMessageAsync(args.Message);
                    return;
                }
                
                if (existingStatus.Resource.Status == TaskStatusConstants.Processing)
                {
                    _logger.LogWarning(
                        "Task {TaskId} is already being processed. This might be a duplicate message.",
                        message.TaskId);
                    // Let it continue - could be a legitimate retry after failure
                }
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Task status doesn't exist yet, this is a new task
            }

            // Update status to processing
            await UpdateTaskStatusAsync(message.TaskId, TaskStatusConstants.Processing, 0, "Starting travel plan generation...");

            // Create a progress reporter
            var progress = new Progress<(int percentage, string step)>(async update =>
            {
                await UpdateTaskStatusAsync(message.TaskId, TaskStatusConstants.Processing, update.percentage, update.step);
            });

            // Use scoped service for agent
            using var scope = _serviceProvider.CreateScope();
            var agentService = scope.ServiceProvider.GetRequiredService<ITravelAgentService>();

            // Generate the travel plan
            var itinerary = await agentService.GenerateTravelPlanAsync(
                message.Request,
                message.TaskId,
                progress,
                args.CancellationToken);

            // Store the result in Cosmos DB
            await StoreResultAsync(message.TaskId, itinerary);

            // Update final status
            await UpdateTaskStatusAsync(message.TaskId, TaskStatusConstants.Completed, 100, "Travel plan completed successfully!");

            // Complete the message
            await args.CompleteMessageAsync(args.Message);

            _logger.LogInformation("Successfully processed travel plan {TaskId}", message.TaskId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message {MessageId}", args.Message.MessageId);

            // Extract task ID if possible
            try
            {
                var message = JsonSerializer.Deserialize<TravelPlanMessage>(messageBody);
                if (message != null)
                {
                    await UpdateTaskStatusAsync(
                        message.TaskId,
                        TaskStatusConstants.Failed,
                        0,
                        "An error occurred while generating your travel plan.",
                        ex.Message);
                }
            }
            catch
            {
                // Ignore errors in error handling
            }

            // Dead letter the message after max retries
            if (args.Message.DeliveryCount >= 3)
            {
                await args.DeadLetterMessageAsync(args.Message, "ProcessingFailed", ex.Message);
            }
            else
            {
                await args.AbandonMessageAsync(args.Message);
            }
        }
    }

    private Task ErrorHandler(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, 
            "Error in Service Bus processor. Source: {ErrorSource}, Entity: {EntityPath}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }

    private async Task UpdateTaskStatusAsync(
        string taskId, 
        string status, 
        int progressPercentage, 
        string currentStep,
        string? errorMessage = null)
    {
        // Try to read existing status to preserve CreatedAt timestamp
        DateTime createdAt = DateTime.UtcNow;
        try
        {
            var existingStatus = await _cosmosContainer.ReadItemAsync<TaskStatus>(taskId, new PartitionKey(taskId));
            createdAt = existingStatus.Resource.CreatedAt;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Item doesn't exist yet, use current time
        }

        var taskStatus = new TaskStatus
        {
            TaskId = taskId,
            Status = status,
            ProgressPercentage = progressPercentage,
            CurrentStep = currentStep,
            CreatedAt = createdAt,
            UpdatedAt = DateTime.UtcNow,
            ErrorMessage = errorMessage
        };

        await _cosmosContainer.UpsertItemAsync(taskStatus, new PartitionKey(taskId));

        _logger.LogInformation(
            "Updated task {TaskId} status: {Status} - {Progress}% - {Step}",
            taskId, status, progressPercentage, currentStep);
    }

    private async Task StoreResultAsync(string taskId, TravelItinerary itinerary)
    {
        var resultId = $"{taskId}_result";
        var document = new TravelItineraryDocument
        {
            id = resultId,
            Itinerary = itinerary
        };
        
        await _cosmosContainer.UpsertItemAsync(document, new PartitionKey(resultId));
        
        _logger.LogInformation("Stored travel itinerary result for task {TaskId}", taskId);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Worker stopping...");
        await _processor.StopProcessingAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}
