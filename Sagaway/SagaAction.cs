﻿using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using Sagaway.Telemetry;

namespace Sagaway
{
    public partial class Saga<TEOperations> where TEOperations : Enum
    {
        internal abstract class SagaAction
        {
            private readonly Saga<TEOperations> _saga;
            private readonly SagaOperation _sagaOperation;
            private readonly ILogger _logger;
            bool _isReminderOn;
            private int _retryCount;

            // ReSharper disable once ConvertToPrimaryConstructor
            protected SagaAction(Saga<TEOperations> saga, SagaOperation sagaOperation, ILogger logger)
            {
                _saga = saga;
                _sagaOperation = sagaOperation;
                _logger = logger;
            }

            protected Saga<TEOperations> Saga => _saga;

            protected SagaOperation SagaOperation => _sagaOperation;

            protected abstract bool IsRevert { get; }

            protected abstract TimeSpan GetRetryInterval(int retryIteration);

            protected abstract Task ExecuteActionAsync();

            protected abstract int MaxRetries { get; }

            protected abstract Task OnActionFailureAsync();
            
            protected abstract Task<bool> ValidateAsync();
            
            private string RevertText => IsRevert ? "Revert " : string.Empty;

            private string ReminderName => $"{RevertText.Trim()}{_sagaOperation.Operation}:Retry";

            private string OperationName => $"{RevertText}{_sagaOperation.Operation}";
            
            protected void LogAndRecord(string message)
            {
                _logger.LogInformation(message);
                _saga.RecordStep(_sagaOperation.Operation, message);
            }

            public void StoreState(JsonObject json)
            {
                json["isReminderOn"] = _isReminderOn;
                json["retryCount"] = _retryCount;
            }

            public void LoadState(JsonObject json)
            {
                _isReminderOn = json["isReminderOn"]?.GetValue<bool>() ?? throw new Exception("Error when loading state, missing isReminderOn entry");
                _retryCount = json["retryCount"]?.GetValue<int>() ?? throw new Exception("Error when loading state, missing retryCount entry");
            }

            private async Task<TimeSpan> ResetReminderAsync()
            {
                var retryInterval = GetRetryInterval(_retryCount);
                
                if (retryInterval == default || MaxRetries == 0)
                {
                    return default;
                }

                await CancelReminderIfOnAsync();

                LogAndRecord($"Registering reminder {ReminderName} for {OperationName} with interval {retryInterval}");
                await _saga._sagaSupportOperations.SetReminderAsync(ReminderName, retryInterval);
                _isReminderOn = true;

                return retryInterval;
            }

            public async Task ExecuteAsync()
            {
                

                LogAndRecord($"Start Executing {OperationName}");
                TimeSpan retryInterval = default;

                try
                {
                    retryInterval = await ResetReminderAsync();
                    await ExecuteActionAsync(); 
                }
                catch (Exception ex)
                {
                    LogAndRecord($"Error when calling {OperationName}. Error: {ex.Message}. Retry in {retryInterval} seconds");
                    _saga.RecordException(ex, $"Error when calling {OperationName}");

                    if (!_isReminderOn)
                    {
                        //no reminder and we failed. Take failure action right away
                        LogAndRecord($"No reminder set for {OperationName}. Taking failure action");
                        await InformFailureOperationAsync(false);
                    }
                }
            }
            
            public async Task CancelReminderIfOnAsync()
            {
                if (_isReminderOn)
                {
                    _logger.LogInformation($"Canceling old reminder {ReminderName} for {OperationName}");
                    _isReminderOn = false;
                    await _saga._sagaSupportOperations.CancelReminderAsync(ReminderName);
                }
            }
            
            public async Task InformFailureOperationAsync(bool failFast)
            {
                if (Succeeded || Failed)
                {
                    return;
                }

                if (failFast)
                {
                    _logger.LogWarning($"The Operation {OperationName} Failed fast, reverting Saga");
                }
                else
                {
                    _logger.LogInformation($"Operation {OperationName} Failed");
                }

                _retryCount++;
                if (!failFast && _retryCount <= MaxRetries)
                {
                    LogAndRecord($"Retry {OperationName}. Retry count: {_retryCount}");
                    _saga.RecordRetryAttemptTelemetry(_sagaOperation.Operation, _retryCount, IsRevert);

                    await ExecuteAsync();
                    return;
                }

                Failed = true;
                LogAndRecord(failFast ? $"{OperationName} Failed Fast." : $"{OperationName} Failed. Retries exhausted.");
                _saga.RecordEndOperationTelemetry(_sagaOperation.Operation, 
                    IsRevert ? OperationOutcome.RevertFailed : OperationOutcome.Failed, IsRevert);

                await OnActionFailureAsync();
            }

            public async Task InformSuccessOperationAsync()
            {
                if (Succeeded || Failed)
                {
                    return;
                }

                LogAndRecord($"{OperationName} Success");
                await CancelReminderIfOnAsync();
                Succeeded = true;
                
                _saga.RecordEndOperationTelemetry(_sagaOperation.Operation, IsRevert ? OperationOutcome.Reverted : OperationOutcome.Succeeded, IsRevert);
                _saga.CheckForCompletion();
            }

            public async Task OnReminderAsync()
            {
                _isReminderOn = true;
                await CancelReminderIfOnAsync();

                LogAndRecord("Wake by a reminder");

                if (Succeeded || Failed)
                {
                    return;
                }


                try
                {
                    //try to get the action state by calling a state check function if exists
                    var hasValidated = await ValidateAsync();

                    if (hasValidated)
                    {
                        await InformSuccessOperationAsync();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    LogAndRecord($"Error when calling {OperationName} validate. Error: {ex.Message}.");
                }
                //the state is unknown, retry action
                LogAndRecord($"The state is unknown in the reminder, retry {OperationName} action");
                await InformFailureOperationAsync(false);
            }
            
            //the operation has been executed successfully 
            public bool Succeeded { get; private set; }
            //the operation has been executed and failed with all retries
            public bool Failed { get; private set; }
            
            public void MarkSucceeded()
            {
                Succeeded = true;
            }
        }
    }
}