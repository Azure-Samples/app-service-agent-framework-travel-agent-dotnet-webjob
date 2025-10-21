namespace TravelPlanner.Shared.Models;

/// <summary>
/// Request to create a new travel plan
/// </summary>
public class TravelPlanRequest
{
    public string Destination { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal Budget { get; set; }
    public List<string> Interests { get; set; } = new();
    public string TravelStyle { get; set; } = string.Empty;
    public string? SpecialRequests { get; set; }
}

/// <summary>
/// Response when a travel planning task is initiated
/// </summary>
public class TravelPlanResponse
{
    public string TaskId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusUrl { get; set; } = string.Empty;
    public string ResultUrl { get; set; } = string.Empty;
    public string? Message { get; set; }
}

/// <summary>
/// Current status of a travel planning task
/// </summary>
public class TaskStatus
{
    public string id { get; set; } = string.Empty; // Cosmos DB requires lowercase 'id'
    public string TaskId 
    { 
        get => id; 
        set => id = value; 
    }
    public string Status { get; set; } = string.Empty; // "queued", "processing", "completed", "failed"
    public int ProgressPercentage { get; set; }
    public string? CurrentStep { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public TravelItinerary? Result { get; set; } // Include result when completed
}

/// <summary>
/// Complete travel itinerary result
/// </summary>
public class TravelItinerary
{
    public string TaskId { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<DayPlan> DailyPlans { get; set; } = new();
    public BudgetBreakdown Budget { get; set; } = new();
    public List<string> TravelTips { get; set; } = new();
    public List<string> PackingList { get; set; } = new();
    public EmergencyInfo EmergencyContacts { get; set; } = new();
}

public class DayPlan
{
    public int DayNumber { get; set; }
    public DateTime Date { get; set; }
    public string Theme { get; set; } = string.Empty;
    public Activity Morning { get; set; } = new();
    public Activity Lunch { get; set; } = new();
    public Activity Afternoon { get; set; } = new();
    public Activity Dinner { get; set; } = new();
    public Activity? Evening { get; set; }
}

public class Activity
{
    public string Time { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal EstimatedCost { get; set; }
    public string? Notes { get; set; }
}

public class BudgetBreakdown
{
    public decimal TotalBudget { get; set; }
    public decimal Accommodation { get; set; }
    public decimal Food { get; set; }
    public decimal Activities { get; set; }
    public decimal Transportation { get; set; }
    public decimal Shopping { get; set; }
    public decimal Emergency { get; set; }
}

public class EmergencyInfo
{
    public string LocalEmergencyNumber { get; set; } = string.Empty;
    public string NearestEmbassy { get; set; } = string.Empty;
    public string HealthcareInfo { get; set; } = string.Empty;
}
