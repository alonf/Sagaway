using ApprovalTests;
using ApprovalTests.Core.Exceptions;
using ApprovalTests.Reporters;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Sagaway.IntegrationTests.TestProject;

[Collection("Sagaway Integration Tests")]
[UseReporter(typeof(VisualStudioReporter))]
public class IntegrationTests
{
    private readonly IntegrationTestSupportFixture _testServiceHelper;

    public IntegrationTests(ITestOutputHelper testOutputHelper, IntegrationTestSupportFixture testServiceHelper)
    {
        _testServiceHelper = testServiceHelper;
        _testServiceHelper.Initiate(testOutputHelper);
    }

    private ITestOutputHelper TestOutputHelper => _testServiceHelper.TestOutputHelper;
    private HttpClient HttpClient => _testServiceHelper.HttpClient;
    private JsonSerializerOptions SerializeOptions => _testServiceHelper.SerializeOptions;

    private ISignalRWrapper SignalR => _testServiceHelper.SignalR;

    [Trait("Integration Test", "Simple Saga")]
    [Fact]
    public async Task CreateSimpleSagaTest()
    {
        TestInfo testInfo = new()
        {
            TestName = "Simple Saga Test",
            Id = Guid.NewGuid(),
            ServiceACall = new ServiceTestInfo
            {
                CallId = Guid.NewGuid().ToString(),
                DelayOnCallInSeconds = [0,0,0,0,0],
                SuccessOnCall = 1,
                ShouldReturnCallbackResultOnCall = [true, true, true, true, true]
            },
            ServiceBCall = new ServiceTestInfo
            {
                CallId = Guid.NewGuid().ToString(),
                DelayOnCallInSeconds = [0, 0, 0, 0, 0],
                SuccessOnCall = 1,
                ShouldReturnCallbackResultOnCall = [true, true, true, true, true]
            }
        };

        await SignalR.StartSignalRAsync(testInfo.Id.ToString());

        var body = new StringContent(JsonSerializer.Serialize(testInfo, SerializeOptions), Encoding.UTF8, "application/json");

        var response = await HttpClient.PostAsync("run-test", body);

        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TestOutputHelper.WriteLine(responseContent);

        var result = await SignalR.WaitForSignalREventAsync(50);

        Assert.True(result);
        
        var testResult = _testServiceHelper.GetTestResultFromSignalR(testInfo.Id);

        Assert.True(testResult.IsSuccess);
    }

    [Theory]
    [Trait("Integration Test", "Scenarios")]
    [InlineData("test_ok_on_1", true, 1, 30, "", 1)]
    [InlineData("test_2_errors_revert", true, 2, 30, "", -1, "", true, 1, 30, "", 1)]
    [InlineData("test_3_errors_then_success", true,4,10,"", 4)]
    [InlineData("test_1_errors_revert", true,1,0,"", -1, "", true,1, 0, "", 1)]
    [InlineData("test_a_1_success_b_1_success", true, 1, 0, "", 1, "", false, 1, 0, "", 1, "true", true, 1, 0, "", 1, "")]
    [InlineData("test_a_1_failed_wait10_2_failed_b_1_success", true, 2, 30, "10,10", -1, "", false, 0, 0, "", 1, "true", true, 1, 30, "", 1, "", true)]
    [InlineData("test_a_1_failed_wait5_2_success_b_1_failed_wait5_2_failed", true, 2, 10, "5,5", 2, "", false, 2, 10, "5,5", -1, "", true, 2, 30, "5,5", -1, "")]
    [InlineData("test_a_1_success_no_callback", true, 1, 5, "1", 1,"false,false")]
    [InlineData("test_a_1_failed_no_callback", true, 1, 5, "1", -1,"false")]
    [InlineData("test_a_1_failed_no_callback_revert", true, 1, 5, "1", -1, "", true, 1, 5, "1", 1)]
    [InlineData("test_a_1_failed_no_callback_revert_failed", true, 1, 5, "1", -1, "", true, 1, 5, "1", -1)]
    [InlineData("test_a_1_failed_no_callback_revert_failed_no_callback", true, 1, 5, "1", -1, "", true, 1, 5, "1", -1, "")]
    [InlineData("test_a_success_on_2_no_callback", true, 2, 5, "2,2", 2)]
    [InlineData("test_a_failed_on_2_no_callback", true, 2, 5, "2,2", -1)]
    [InlineData("test_a_failed_on_2_no_callback_revert", true, 2, 5, "2,2", -1, "", true, 2, 5, "2,2", 1)]
    [InlineData("test_a_success_on_3_no_callback_b_success_on_4",true,5,8,"",3,"false,false,false",false,0,0,"",0,"",true,5,8,"",4)]

    public async Task TestSagaAsync(
        string testName, 
        bool aInUse = false,
        int aMaxRetries = 1,
        int aRetryDelayInSeconds = 0,
        string aDelays = "", 
        int aSuccessOnCall = 1, 
        string aCallbackReturn = "",
        bool aRevertInUse = false,
        int aRevertMaxRetries = 1,
        int aRevertRetryDelayInSeconds = 0,
        string aRevertDelays = "",
        int aRevertSuccessOnCall = 1,
        string aRevertCallbackReturn = "",
        bool bInUse = false,
        int bMaxRetries = 1,
        int bRetryDelayInSeconds = 0,
        string bDelays = "",
        int bSuccessOnCall = 1,
        string bCallbackReturn = "",
        bool bRevertInUse = false,
        int bRevertMaxRetries = 1,
        int bRevertRetryDelayInSeconds = 0,
        string bRevertDelays = "",
        int bRevertSuccessOnCall = 1,
        string bRevertCallbackReturn = ""
       )
    {
        try
        {
            Approvals.RegisterDefaultNamerCreation(() => new AprovalNamer(testName));

            aDelays = ExpandStringArray(aDelays, "0", 10);
            aRevertDelays = ExpandStringArray(aRevertDelays, "0", 10);
            bDelays = ExpandStringArray(bDelays, "0", 10);
            bRevertDelays = ExpandStringArray(bRevertDelays, "0", 10);

            aCallbackReturn = ExpandStringArray(aCallbackReturn, "true", 10);
            aRevertCallbackReturn = ExpandStringArray(aRevertCallbackReturn, "true", 10);
            bCallbackReturn = ExpandStringArray(bCallbackReturn, "true", 10);
            bRevertCallbackReturn = ExpandStringArray(bRevertCallbackReturn, "true", 10);

            TestInfo testInfo = new()
            {
                TestName = testName,
                Id = Guid.NewGuid(),
                ServiceACall = new ServiceTestInfo
                {
                    InUse = aInUse,
                    MaxRetries = aMaxRetries,
                    RetryDelayInSeconds = aRetryDelayInSeconds,
                    CallId = Guid.NewGuid().ToString(),
                    DelayOnCallInSeconds = aDelays.Split(',').Select(int.Parse).ToArray(),
                    SuccessOnCall = aSuccessOnCall,
                    ShouldReturnCallbackResultOnCall =
                        aCallbackReturn.Split(',').Select(bool.Parse).ToArray()
                },
                ServiceARevert = new ServiceTestInfo
                {
                    InUse = aRevertInUse,
                    MaxRetries = aRevertMaxRetries,
                    RetryDelayInSeconds = aRevertRetryDelayInSeconds,
                    CallId = Guid.NewGuid().ToString(),
                    DelayOnCallInSeconds = aRevertDelays.Split(',').Select(int.Parse).ToArray(),
                    SuccessOnCall = aRevertSuccessOnCall,
                    ShouldReturnCallbackResultOnCall =
                        aRevertCallbackReturn.Split(',').Select(bool.Parse).ToArray()
                },
                ServiceBCall = new ServiceTestInfo
                {
                    InUse = bInUse,
                    MaxRetries = bMaxRetries,
                    RetryDelayInSeconds = bRetryDelayInSeconds,
                    CallId = Guid.NewGuid().ToString(),
                    DelayOnCallInSeconds = bDelays.Split(',').Select(int.Parse).ToArray(),
                    SuccessOnCall = bSuccessOnCall,
                    ShouldReturnCallbackResultOnCall =
                        bCallbackReturn.Split(',').Select(bool.Parse).ToArray()
                },
                ServiceBRevert = new ServiceTestInfo
                {
                    InUse = bRevertInUse,
                    MaxRetries = bRevertMaxRetries,
                    RetryDelayInSeconds = bRevertRetryDelayInSeconds,
                    CallId = Guid.NewGuid().ToString(),
                    DelayOnCallInSeconds = bRevertDelays.Split(',').Select(int.Parse).ToArray(),
                    SuccessOnCall = bRevertSuccessOnCall,
                    ShouldReturnCallbackResultOnCall =
                        bRevertCallbackReturn.Split(',').Select(bool.Parse).ToArray()
                },
            };

            await SignalR.StartSignalRAsync(testInfo.Id.ToString());

            var body = new StringContent(JsonSerializer.Serialize(testInfo, SerializeOptions), Encoding.UTF8,
                "application/json");

            var response = await HttpClient.PostAsync("run-test", body);

            var responseContent = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            TestOutputHelper.WriteLine(responseContent);

            var result = await SignalR.WaitForSignalREventAsync(40);

            Assert.True(result);

            var testResult = _testServiceHelper.GetTestResultFromSignalR(testInfo.Id);

            var resultText = "Test Name: " + testName + Environment.NewLine +
                             "Result: " + testResult.IsSuccess + Environment.NewLine +
                             "Saga Log:" + Environment.NewLine +  testResult.SagaLog;

            ApprovalVerifyWithDump.Verify(resultText, TestOutputHelper, RemoveDynamic);
        }
        catch (ApprovalMismatchException)
        {
            throw;
        }
        catch (ApprovalMissingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ApprovalVerifyWithDump.Verify(testName + Environment.NewLine + ex.Message, TestOutputHelper);
        }
    }

    private string ExpandStringArray(string inValue, string defaultValue , int amount)
    {
        StringBuilder sb = new();

        string[] values = string.IsNullOrEmpty(inValue) ? [] : inValue.Split(',');
        
        for (int i = 0; i < amount; i++)
        {
            sb.Append(i < values.Length ? values[i] : defaultValue);

            if (i < amount - 1)
            {
                sb.Append(",");
            }
        }

        return sb.ToString();
    }

    private string RemoveDynamic(string text)
    {
        string pattern = @"\[\d{1,2}:\d{1,2}:\d{1,2}\]";
        string replacement = "[*time*]";
        string result = Regex.Replace(text, pattern, replacement);

        return result;
    }
}