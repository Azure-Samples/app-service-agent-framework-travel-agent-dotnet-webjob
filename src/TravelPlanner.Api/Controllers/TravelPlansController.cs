using Microsoft.AspNetCore.Mvc;
using TravelPlanner.Api.Services;
using TravelPlanner.Shared.Constants;
using TravelPlanner.Shared.Models;
using TaskStatus = TravelPlanner.Shared.Models.TaskStatus;

namespace TravelPlanner.Api.Controllers;

[ApiController]
[Route("api/travel-plans")]
public class TravelPlansController : ControllerBase
{
    private readonly ITravelPlanService _travelPlanService;
    private readonly ILogger<TravelPlansController> _logger;

    public TravelPlansController(
        ITravelPlanService travelPlanService,
        ILogger<TravelPlansController> logger)
    {
        _travelPlanService = travelPlanService;
        _logger = logger;
    }

    /// <summary>
    /// Create a new travel plan request
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(TravelPlanResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TravelPlanResponse>> CreateTravelPlan(
        [FromBody] TravelPlanRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        _logger.LogInformation("Received travel plan request for {Destination}", request.Destination);

        var response = await _travelPlanService.CreateTravelPlanAsync(request);
        
        return AcceptedAtAction(
            nameof(GetTaskStatus), 
            new { taskId = response.TaskId }, 
            response);
    }

    /// <summary>
    /// Get the current status of a travel plan task
    /// </summary>
    [HttpGet("{taskId}")]
    [ProducesResponseType(typeof(TaskStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskStatus>> GetTaskStatus(string taskId)
    {
        var status = await _travelPlanService.GetTaskStatusAsync(taskId);
        
        if (status == null)
        {
            return NotFound(new { message = $"Task {taskId} not found" });
        }

        // If completed, include the result in the response
        if (status.Status == TaskStatusConstants.Completed)
        {
            var itinerary = await _travelPlanService.GetTravelItineraryAsync(taskId);
            status.Result = itinerary;
        }

        return Ok(status);
    }

    /// <summary>
    /// Get the completed travel itinerary
    /// </summary>
    [HttpGet("{taskId}/result")]
    [ProducesResponseType(typeof(TravelItinerary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(425)]
    public async Task<ActionResult<TravelItinerary>> GetTravelItinerary(string taskId)
    {
        // First check the status
        var status = await _travelPlanService.GetTaskStatusAsync(taskId);
        
        if (status == null)
        {
            return NotFound(new { message = $"Task {taskId} not found" });
        }

        if (status.Status != TaskStatusConstants.Completed)
        {
            return StatusCode(425, new 
            { 
                message = $"Task is not yet completed. Current status: {status.Status}",
                currentStatus = status
            });
        }

        var itinerary = await _travelPlanService.GetTravelItineraryAsync(taskId);
        
        if (itinerary == null)
        {
            return NotFound(new { message = $"Result for task {taskId} not found" });
        }

        return Ok(itinerary);
    }

    /// <summary>
    /// Health check endpoint
    /// </summary>
    [HttpGet("/health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult HealthCheck()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
}
