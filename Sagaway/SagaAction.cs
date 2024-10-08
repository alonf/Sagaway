﻿using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using Sagaway.Telemetry;

namespace Sagaway
{
    public partial class Saga<TEOperations> where TEOperations : Enum
    {
        internal abstract class SagaAction
        {
            #region Transient State - built on each activation

            private readonly Saga<TEOperations> _saga;
            private readonly ILogger _logger;

            #endregion //Transient State - built on each activation


            #region Persistent State - kept in the state store

            bool _isReminderOn;
            private int _retryCount;
            protected Saga<TEOperations> Saga => _saga; //persisted in another class

            //the operation has been executed successfully 
            public bool Succeeded { get; private set; }
            //the operation has been executed and failed with all retries
            public bool Failed { get; private set; }

            #endregion //Persistent State - kept in the state store


            // ReSharper disable once ConvertToPrimaryConstructor
            protected SagaAction(Saga<TEOperations> saga, SagaOperation sagaOperation, ILogger logger)
            {
                _saga = saga;
                SagaOperation = sagaOperation;
                _logger = logger;

                _logger.LogTrace("Initialized {OperationName} for saga {SagaId}", sagaOperation.Operation, saga._sagaUniqueId);
            }

            protected SagaOperation SagaOperation { get; }

            protected abstract bool IsRevert { get; }

            protected abstract TimeSpan GetRetryInterval(int retryIteration);

            protected abstract Task ExecuteActionAsync();

            protected abstract int MaxRetries { get; }

            protected abstract Task OnActionFailureAsync();
            
            protected abstract Task<bool> ValidateAsync();
            
            private string RevertText => IsRevert ? "Revert " : string.Empty;

            private string ReminderName => $"{SagaOperation.Operation}:Retry";

            private string OperationName => $"{RevertText}{SagaOperation.Operation}";
            
            protected void LogAndRecord(string message)
            {
                _logger.LogInformation(message);
                _saga.RecordStep(SagaOperation.Operation, message);
            }

            public void StoreState(JsonObject json)
            {
                json["isReminderOn"] = _isReminderOn;
                json["retryCount"] = _retryCount;
                json["succeeded"] = Succeeded;
                json["failed"] = Failed;
            }

            public void LoadState(JsonObject json)
            {
                _isReminderOn = json["isReminderOn"]?.GetValue<bool>() ?? throw new Exception("Error when loading state, missing isReminderOn entry");
                _retryCount = json["retryCount"]?.GetValue<int>() ?? throw new Exception("Error when loading state, missing retryCount entry");
                Succeeded = json["succeeded"]?.GetValue<bool>() ?? throw new Exception("Error when loading state, missing succeeded entry");
                Failed = json["failed"]?.GetValue<bool>() ?? throw new Exception("Error when loading state, missing failed entry");
            }

            private async Task<TimeSpan> ResetReminderAsync()
            {
                await CancelReminderIfOnAsync();

                var retryInterval = GetRetryInterval(_retryCount);
                
                if (retryInterval == default || MaxRetries == 0)
                {
                    _logger.LogDebug("No reminder needed for {OperationName} in saga {SagaId} as retry interval is default or max retries is 0.", OperationName, _saga._sagaUniqueId);
                    return default;
                }

                LogAndRecord($"Registering reminder {ReminderName} for {OperationName} with interval {retryInterval}");
                await _saga._sagaSupportOperations.SetReminderAsync(ReminderName, retryInterval);
                _isReminderOn = true;

                _logger.LogTrace("Reminder {ReminderName} set for operation {OperationName} in saga {SagaId} with interval {RetryInterval}", ReminderName, OperationName, _saga._sagaUniqueId, retryInterval);

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
                    await _saga.RecordTelemetryExceptionAsync(ex, $"Error when calling {OperationName}");

                    if (!_isReminderOn)
                    {
                        //no reminder and we failed. Take failure action right away
                        LogAndRecord($"No reminder set for {OperationName}. Taking failure action");
                        await InformFailureOperationAsync(false);
                    }
                }
            }
            
            public async Task CancelReminderIfOnAsync(bool forceCancel = false)
            {
                if (_isReminderOn || forceCancel)
                {
                    _logger.LogInformation("Canceling old reminder {ReminderName} for {OperationName}", ReminderName, OperationName);
                    _isReminderOn = false;
                    await _saga._sagaSupportOperations.CancelReminderAsync(ReminderName);
                }
            }
            
            public async Task InformFailureOperationAsync(bool failFast)
            {
                if (Succeeded || Failed)
                {
                    _logger.LogDebug("Operation {OperationName} already succeeded or failed. No action needed.", OperationName);
                    return;
                }

                if (failFast)
                {
                    _logger.LogWarning("The Operation {OperationName} Failed fast, reverting Saga", OperationName);
                }
                else
                {
                    _logger.LogInformation("Operation {OperationName} Failed", OperationName);
                }

                _retryCount++;
                if (!failFast && _retryCount <= MaxRetries)
                {
                    LogAndRecord($"Retry {OperationName}. Retry count: {_retryCount}");
                    await _saga.RecordRetryAttemptTelemetryAsync(SagaOperation.Operation, _retryCount, IsRevert);

                    await ExecuteAsync();
                    return;
                }

                Failed = true;
                LogAndRecord(failFast ? $"{OperationName} Failed Fast." : $"{OperationName} Failed. Retries exhausted.");
                await _saga.RecordEndOperationTelemetry(SagaOperation.Operation, 
                    IsRevert ? OperationOutcome.RevertFailed : OperationOutcome.Failed, IsRevert);

                await OnActionFailureAsync();
            }

            public async Task InformSuccessOperationAsync()
            {
                await CancelReminderIfOnAsync();

                if (Succeeded || Failed)
                {
                    _logger.LogDebug("Operation {OperationName} already succeeded or failed. No action needed.", OperationName);
                    return;
                }

                LogAndRecord($"{OperationName} Success");
                
                Succeeded = true;
                
                await _saga.RecordEndOperationTelemetry(SagaOperation.Operation, IsRevert ? OperationOutcome.Reverted : OperationOutcome.Succeeded, IsRevert);
                await _saga.CheckForCompletionAsync();
            }

            public async Task OnReminderAsync()
            {
                _isReminderOn = true;
                await CancelReminderIfOnAsync();

                LogAndRecord("Wake by a reminder");

                if (Succeeded || Failed)
                {
                    _logger.LogWarning("OnReminderAsync: Operation {OperationName} already succeeded or failed. No action needed.", OperationName);
                    return;
                }

                try
                {
                    //try to get the action state by calling a state check function if exists
                    var hasValidated = await ValidateAsync();

                    _logger.LogTrace("OnReminderAsync: Operation {OperationName} validated: {HasValidated}", OperationName, hasValidated);

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
                LogAndRecord($"The validate function does not exist or raised an exception, retry {OperationName} action");
                await InformFailureOperationAsync(false);
            }
            
            public void MarkSucceeded()
            {
                Succeeded = true;
            }
        }
    }
}