using Dapr.Client;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Sagaway.Routing.Tracking;

public class SignalRPublisher(ILogger<SignalRPublisher> logger, DaprClient daprClient) : ISignalRPublisher
{
    public async Task PublishMessageToSignalRAsync(string testName, string? callChainResult)
    {
        var jsonSerializationOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var argument = new Argument
        {
            Sender = "dapr",
            Text = callChainResult
        };

        SignalRMessage message = new()
        {
            UserId = "testUser",
            Target = testName,
            Arguments = [argument]
        };

        logger.LogInformation("publishing message to SignalR: {argumentText}", argument.Text);

        await daprClient.InvokeBindingAsync("testcallback", "create", message); //sending through dapr to the signalR Hub
    }
}