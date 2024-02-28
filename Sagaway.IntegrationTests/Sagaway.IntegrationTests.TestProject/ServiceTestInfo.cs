namespace Sagaway.IntegrationTests.TestProject;

public record ServiceTestInfo
{
    //unique id for the call
    public string CallId { get; init; } = Guid.NewGuid().ToString();

    //array of int to define the delay on each call
    public int[]? DelayOnCallInSeconds { get; init; }

    //The call retry that should return a success result
    public int SuccessOnCall { get; init; } = 1;

    //array of bool to define if the call should return a result in the callback
    public bool[]? ShouldReturnCallbackResultOnCall { get; init; }
}