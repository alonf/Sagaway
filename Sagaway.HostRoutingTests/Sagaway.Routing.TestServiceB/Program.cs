using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sagaway.Callback.Propagator;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Sagaway.Routing.Tracking;
using Sagaway.Callback.Router;
using Sagaway.Routing.TestServiceB;


var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Register DaprClient that support Sagaway context propagator
builder.Services.AddDaprWithSagawayContextPropagator().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});


builder.Services.AddOpenTelemetry().WithTracing(tracing =>
{
    tracing.AddAspNetCoreInstrumentation(options =>
    {
        options.Filter = (httpContext) => httpContext.Request.Path != "/healthz";
    });
    tracing.AddHttpClientInstrumentation();
    tracing.AddZipkinExporter(options =>
    {
        options.Endpoint = new Uri("http://zipkin:9411/api/v2/spans");
    }).SetResourceBuilder(
        ResourceBuilder.CreateDefault().AddService("TestServiceB"));
});

builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSagawayCallbackRouter("TestServiceBQueue",
    async (
    [FromBody] CallChainInfo request,
    [FromServices] ILogger<Program> logger,
    [FromServices] ITestServiceB testServiceB,
    [FromServices] DaprClient daprClient) =>
    {
        logger.LogInformation("Received test request: {request}", request);

        await testServiceB.InvokeAsync(request);

    }).ExcludeFromDescription();


app.MapHealthChecks("/healthz");
app.UseSagawayContextPropagator();
app.MapControllers();
app.MapSubscribeHandler();
app.UseRouting();

app.Run();