﻿using System.Text.Json.Nodes;
using Sagaway.Telemetry;

namespace Sagaway
{
    /// <summary>
    /// Provide the required methods for a Saga
    /// </summary>
    public interface ISagaSupport
    {
        /// <summary>
        /// A function to set reminder. The reminder should bring the saga back to life and call the OnReminder function
        /// With the reminder name.
        /// </summary>
        /// <param name="reminderName">A unique name for the reminder</param>
        /// <param name="dueTime">The time to re-activate the saga</param>
        /// <returns>Async operation</returns>
        Task SetReminderAsync(string reminderName, TimeSpan dueTime);

        /// <summary>
        /// A function to cancel a reminder
        /// </summary>
        /// <param name="reminderName">The reminder to cancel</param>
        /// <returns>Async operation</returns>
        Task CancelReminderAsync(string reminderName);

        /// <summary>
        /// Provide a mechanism to persist the saga state
        /// </summary>
        /// <param name="sagaId">The saga unique id</param>
        /// <param name="state">The saga serialized state</param>
        /// <returns>Async operation</returns>
        Task SaveSagaStateAsync(string sagaId, JsonObject state);

        /// <summary>
        /// Provide a mechanism to load the saga state
        /// </summary>
        /// <param name="sagaId">The saga unique id</param>
        /// <returns>The serialized saga state</returns>
        Task<JsonObject?> LoadSagaAsync(string sagaId);

        /// <summary>
        /// Provide the lock for a thread safe saga, if required
        /// Can utilize the <see cref="ReentrantAsyncLock"/> or <see cref="NonLockAsync"/>
        /// </summary>
        /// <returns>A lock implementor</returns>
        ILockWrapper CreateLock();


        private static readonly ITelemetryAdapter NullTelemetryAdapterInstance = new NullTelemetryAdapter();

        /// <summary>
        /// Provide the telemetry adapter for the saga
        /// </summary>
        ITelemetryAdapter TelemetryAdapter  => NullTelemetryAdapterInstance;
    }
}