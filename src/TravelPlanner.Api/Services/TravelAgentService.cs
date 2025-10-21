using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.AI.Agents.Persistent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;
using TravelPlanner.Shared.Models;

namespace TravelPlanner.Api.Services;

public class TravelAgentService : ITravelAgentService
{
    private readonly ILogger<TravelAgentService> _logger;
    private readonly AgentOptions _options;

    public TravelAgentService(
        ILogger<TravelAgentService> logger,
        IOptions<AgentOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task<TravelItinerary> GenerateTravelPlanAsync(
        TravelPlanRequest request,
        string taskId,
        IProgress<(int percentage, string step)> progress,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting travel plan generation for task {TaskId}", taskId);

        // Create Persistent Agents client for Azure Foundry
        var persistentAgentsClient = new PersistentAgentsClient(
            _options.AzureOpenAIEndpoint,
            new DefaultAzureCredential());

        try
        {
            // Create the travel planner agent
            progress.Report((5, "Creating AI travel planner agent..."));
            
            var agent = await persistentAgentsClient.CreateAIAgentAsync(
                model: _options.ModelDeploymentName,
                name: "Travel Planner",
                cancellationToken: cancellationToken);

            _logger.LogInformation("Created agent {AgentId} for task {TaskId}", agent.Id, taskId);

            // Create a new thread for this conversation
            progress.Report((10, "Initializing planning session..."));
            var thread = agent.GetNewThread();
            
            _logger.LogInformation("Created thread for task {TaskId}", taskId);

            // Build the travel request prompt
            progress.Report((15, "Analyzing travel requirements..."));
            var requestPrompt = BuildTravelRequestPrompt(request);

            // Run the agent to generate the travel plan
            progress.Report((20, "Agent analyzing destination and planning itinerary..."));
            var response = await agent.RunAsync(requestPrompt, thread, cancellationToken: cancellationToken);

            _logger.LogInformation("Agent completed run for task {TaskId}", taskId);

            // Parse the agent's response into a structured itinerary
            progress.Report((95, "Formatting travel plan..."));
            var itinerary = ParseAgentResponse(response, request, taskId);

            // Clean up resources
            progress.Report((98, "Finalizing..."));
            await persistentAgentsClient.Administration.DeleteAgentAsync(agent.Id, cancellationToken: cancellationToken);

            progress.Report((100, "Travel plan complete!"));
            
            _logger.LogInformation("Completed travel plan generation for task {TaskId}", taskId);
            
            return itinerary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating travel plan for task {TaskId}", taskId);
            throw;
        }
    }

    private string BuildTravelRequestPrompt(TravelPlanRequest request)
    {
        var days = (request.EndDate - request.StartDate).Days + 1;
        
        return $@"{GetAgentInstructions()}

Create a comprehensive {days}-day travel itinerary for {request.Destination}.

TRAVEL DETAILS:
- Destination: {request.Destination}
- Start Date: {request.StartDate:MMMM d, yyyy}
- End Date: {request.EndDate:MMMM d, yyyy}
- Duration: {days} days
- Budget: ${request.Budget:N0} USD
- Interests: {string.Join(", ", request.Interests)}
- Travel Style: {request.TravelStyle}
{(string.IsNullOrEmpty(request.SpecialRequests) ? "" : $"- Special Requests: {request.SpecialRequests}")}

Please provide a detailed itinerary with the following structure:

1. DAILY ITINERARY: For each day, provide:
   - Day theme
   - Morning activity (9:00 AM - 12:00 PM) with location, description, estimated cost
   - Lunch recommendation (12:00 PM - 1:30 PM) with location, description, estimated cost
   - Afternoon activity (2:00 PM - 6:00 PM) with location, description, estimated cost
   - Dinner recommendation (7:00 PM - 9:00 PM) with location, description, estimated cost
   - Optional evening activity if appropriate

2. BUDGET BREAKDOWN: Allocate the ${request.Budget:N0} budget across:
   - Accommodation
   - Food & Dining
   - Activities & Attractions
   - Transportation
   - Shopping & Souvenirs
   - Emergency Fund

3. TRAVEL TIPS: 5-7 practical tips specific to {request.Destination}

4. PACKING LIST: Essential items based on destination, season, and activities

5. EMERGENCY INFORMATION: Local emergency numbers, embassy contacts, healthcare info

Format your response clearly with headers and bullet points for easy parsing.";

    }

    private TravelItinerary ParseAgentResponse(
        AgentRunResponse agentResponse,
        TravelPlanRequest request,
        string taskId)
    {
        // Get the text content from the agent's response
        var responseText = agentResponse.Text ?? string.Empty;

        _logger.LogInformation("Agent response length: {Length} characters", responseText.Length);
        _logger.LogInformation("Agent response: {Response}", responseText);

        // Extract travel tips from the response
        var travelTips = ExtractTravelTips(responseText);
        
        // Return the agent's actual response in a simple format
        var days = (request.EndDate - request.StartDate).Days + 1;
        
        return new TravelItinerary
        {
            TaskId = taskId,
            Destination = request.Destination,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            DailyPlans = new List<DayPlan>
            {
                new DayPlan
                {
                    DayNumber = 1,
                    Date = request.StartDate,
                    Theme = $"{days}-Day {request.Destination} Itinerary",
                    Morning = new Activity
                    {
                        Time = "",
                        Title = "",
                        Location = request.Destination,
                        Description = responseText,
                        EstimatedCost = 0
                    }
                }
            },
            Budget = AllocateBudget(request.Budget, days),
            TravelTips = travelTips,
            PackingList = GeneratePackingList(request),
            EmergencyContacts = GetEmergencyInfo(request.Destination)
        };
    }

    private List<string> ExtractTravelTips(string responseText)
    {
        var tips = new List<string>();
        
        // Try to find a TRAVEL TIPS section
        var tipsMatch = System.Text.RegularExpressions.Regex.Match(
            responseText, 
            @"(?:TRAVEL TIPS|Tips|TIPS):?\s*([\s\S]*?)(?:\n\n|###|PACKING|$)", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (tipsMatch.Success)
        {
            var tipsSection = tipsMatch.Groups[1].Value;
            var lines = tipsSection.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim().TrimStart('-', '*', '•', '1', '2', '3', '4', '5', '6', '7', '8', '9', '0', '.', ')').Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) && trimmed.Length > 10)
                {
                    tips.Add(trimmed);
                }
            }
        }
        
        // If no tips found, provide generic ones
        if (tips.Count == 0)
        {
            tips.Add("Check the weather forecast before your trip");
            tips.Add("Book popular attractions in advance");
            tips.Add("Keep important documents and valuables secure");
            tips.Add("Learn a few basic phrases in the local language");
            tips.Add("Stay hydrated and take breaks between activities");
        }
        
        return tips.Take(7).ToList();
    }

    private string GetDayTheme(List<string> interests, int dayNumber)
    {
        var themes = new Dictionary<int, string>
        {
            { 1, "Arrival & City Introduction" },
            { 2, "Cultural Immersion" },
            { 3, "Adventure & Activities" },
            { 4, "Relaxation & Local Life" },
            { 5, "Highlights & Shopping" },
            { 6, "Hidden Gems Discovery" },
            { 7, "Farewell & Departure" }
        };

        return themes.ContainsKey(dayNumber)
            ? themes[dayNumber]
            : $"{interests.FirstOrDefault() ?? "Exploration"} Day";
    }

    private string GetAgentInstructions()
    {
        return @"You are an expert travel planner. Create detailed, personalized itineraries with:
- Day-by-day plans with activities, timing, and costs
- Accommodation and dining recommendations
- Local transportation and practical tips
- Cultural considerations and safety advice
Format with clear sections. Be realistic about timing and budgets. Prioritize the traveler's interests.";
    }

    private List<string> GeneratePackingList(TravelPlanRequest request)
    {
        var packingList = new List<string>
        {
            "Passport and travel documents",
            "Comfortable walking shoes",
            "Weather-appropriate clothing",
            "Phone charger and power adapter",
            "Reusable water bottle",
            "Sunscreen and sunglasses",
            "Basic first aid kit",
            "Travel insurance documents"
        };

        // Add interest-specific items
        if (request.Interests.Any(i => i.Contains("hiking", StringComparison.OrdinalIgnoreCase)))
        {
            packingList.Add("Hiking boots and backpack");
        }
        if (request.Interests.Any(i => i.Contains("beach", StringComparison.OrdinalIgnoreCase)))
        {
            packingList.Add("Swimsuit and beach towel");
        }
        if (request.Interests.Any(i => i.Contains("photography", StringComparison.OrdinalIgnoreCase)))
        {
            packingList.Add("Camera equipment and extra memory cards");
        }

        return packingList;
    }

    private BudgetBreakdown AllocateBudget(decimal totalBudget, int days)
    {
        return new BudgetBreakdown
        {
            TotalBudget = totalBudget,
            Accommodation = totalBudget * 0.35m, // 35%
            Food = totalBudget * 0.25m,          // 25%
            Activities = totalBudget * 0.20m,    // 20%
            Transportation = totalBudget * 0.10m, // 10%
            Shopping = totalBudget * 0.05m,       // 5%
            Emergency = totalBudget * 0.05m       // 5%
        };
    }

    private EmergencyInfo GetEmergencyInfo(string destination)
    {
        return new EmergencyInfo
        {
            LocalEmergencyNumber = "112 (EU) or 911 (US)",
            NearestEmbassy = $"Contact your embassy in {destination}",
            HealthcareInfo = "Travel with comprehensive health insurance. Keep emergency numbers saved in your phone."
        };
    }
}
