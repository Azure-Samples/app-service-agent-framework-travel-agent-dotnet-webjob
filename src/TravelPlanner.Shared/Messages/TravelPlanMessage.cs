using TravelPlanner.Shared.Models;

namespace TravelPlanner.Shared.Messages;

/// <summary>
/// Message sent to Service Bus queue to process a travel plan
/// </summary>
public class TravelPlanMessage
{
    public string TaskId { get; set; } = string.Empty;
    public TravelPlanRequest Request { get; set; } = new();
    public DateTime EnqueuedAt { get; set; }
}
