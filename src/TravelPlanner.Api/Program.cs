using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Cosmos;
using TravelPlanner.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Configure Azure Service Bus
// Priority: Managed Identity (production) > Connection String (local development)
var serviceBusNamespace = builder.Configuration["ServiceBus:Namespace"];
if (!string.IsNullOrEmpty(serviceBusNamespace))
{
    // Production: Use managed identity with Service Bus namespace
    var serviceBusOptions = new ServiceBusClientOptions
    {
        TransportType = ServiceBusTransportType.AmqpWebSockets
    };
    
    builder.Services.AddSingleton(sp =>
        new ServiceBusClient(serviceBusNamespace, new DefaultAzureCredential(), serviceBusOptions));
}
else
{
    // Local development: Use connection string
    var connectionString = builder.Configuration["ServiceBus:ConnectionString"];
    if (!string.IsNullOrEmpty(connectionString))
    {
        builder.Services.AddSingleton(sp =>
            new ServiceBusClient(connectionString, new ServiceBusClientOptions
            {
                TransportType = ServiceBusTransportType.AmqpWebSockets
            }));
    }
}

// Configure Cosmos DB
// Priority: Managed Identity (production) > Connection String (if implemented for local dev)
var cosmosEndpoint = builder.Configuration["CosmosDb:Endpoint"];
var databaseName = builder.Configuration["CosmosDb:DatabaseName"];
var containerName = builder.Configuration["CosmosDb:ContainerName"];

if (!string.IsNullOrEmpty(cosmosEndpoint) && !string.IsNullOrEmpty(databaseName) && !string.IsNullOrEmpty(containerName))
{
    // Use managed identity for authentication
    builder.Services.AddSingleton(sp =>
    {
        var credential = new DefaultAzureCredential();
        var cosmosClient = new CosmosClient(cosmosEndpoint, credential);
        return cosmosClient;
    });
    
    builder.Services.AddSingleton(sp =>
    {
        var cosmosClient = sp.GetRequiredService<CosmosClient>();
        var database = cosmosClient.GetDatabase(databaseName);
        return database.GetContainer(containerName);
    });
}

// Configure Agent options
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));

// Register application services
builder.Services.AddScoped<ITravelPlanService, TravelPlanService>();

// Configure CORS for development
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("AllowAll");
    app.UseHttpsRedirection();
}

app.MapControllers();

app.Run();
