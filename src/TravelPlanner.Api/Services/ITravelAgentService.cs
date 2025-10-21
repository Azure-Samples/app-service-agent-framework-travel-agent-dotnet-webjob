using TravelPlanner.Shared.Models;

namespace TravelPlanner.Api.Services;

public interface ITravelAgentService
{
    Task<TravelItinerary> GenerateTravelPlanAsync(
        TravelPlanRequest request, 
        string taskId,
        IProgress<(int percentage, string step)> progress,
        CancellationToken cancellationToken);
}
