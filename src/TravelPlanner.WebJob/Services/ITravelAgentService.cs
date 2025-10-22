using TravelPlanner.Shared.Models;

namespace TravelPlanner.WebJob.Services;

public interface ITravelAgentService
{
    Task<TravelItinerary> GenerateTravelPlanAsync(
        TravelPlanRequest request, 
        string taskId,
        IProgress<(int percentage, string step)> progress,
        CancellationToken cancellationToken);
}
