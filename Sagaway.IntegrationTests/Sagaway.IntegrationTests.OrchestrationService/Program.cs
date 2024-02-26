using System.Text.Json.Serialization;
using System.Text.Json;
using Dapr.Actors;
using Microsoft.AspNetCore.Mvc;
using Dapr.Actors.Client;
using Sagaway.Callback.Router;
using Sagaway.IntegrationTests.OrchestrationService;
using Sagaway.IntegrationTests.OrchestrationService.Actors;

var builder = WebApplication.CreateBuilder(args);


builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Register DaprClient
builder.Services.AddControllers().AddDapr().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.AddActors(options =>
{
    // Register actor types and configure actor settings
    options.Actors.RegisterActor<TestActor>();
    // Configure default settings
    options.ActorIdleTimeout = TimeSpan.FromMinutes(10);
    options.ActorScanInterval = TimeSpan.FromSeconds(35);
    options.DrainOngoingCallTimeout = TimeSpan.FromSeconds(35);
    options.DrainRebalancedActors = true;

    options.JsonSerializerOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        PropertyNameCaseInsensitive = true
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//enable callback router
app.UseSagawayCallbackRouter("response-queue", "TestActor");

app.MapPost("/run-test", async (
        [FromServices] IActorProxyFactory actorProxyFactory,
        [FromServices] ILogger < Program > logger,
        [FromBody] TestInfo? testInfo) =>
{

    if (string.IsNullOrEmpty(testInfo?.TestName))
    {
        logger.LogError("Test name is required");
        return Results.BadRequest("Test name is required");
    }

    logger.LogInformation("Starting test {TestName}", testInfo.TestName);

    var proxy = actorProxyFactory.CreateActorProxy<ITestActor>(
        new ActorId(testInfo.Id.ToString("D")), "TestActor");
    
    await proxy.RunTestAsync(testInfo);

    return Results.Ok();
})
.WithName("run-test")
.WithOpenApi();

app.MapControllers();
app.MapSubscribeHandler();
app.UseRouting();
app.MapActorsHandlers();

app.Run();