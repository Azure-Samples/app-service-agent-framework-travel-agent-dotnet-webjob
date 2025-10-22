namespace TravelPlanner.WebJob.Services;

public class AgentOptions
{
    public string AzureOpenAIEndpoint { get; set; } = string.Empty;
    public string ModelDeploymentName { get; set; } = "gpt-4o";
}
