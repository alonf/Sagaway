﻿using Microsoft.Extensions.Logging;
using Sagaway.Callback.Context;

namespace Sagaway.Callback.Propagator;

/// <summary>
/// This handler is responsible for propagating the Sagaway context in HTTP requests.
/// It adds the Sagaway context to the HTTP headers if not already present, ensuring that 
/// downstream services can access the necessary context information for callbacks.
/// </summary>
public class SagawayContextPropagationHandler : DelegatingHandler
{
    private readonly ILogger<SagawayContextPropagationHandler> _logger;
    private readonly ISagawayContextManager _sagawayContextManager;

    // ReSharper disable once ConvertToPrimaryConstructor
    /// <summary>
    /// Initializes a new instance of the <see cref="SagawayContextPropagationHandler"/> class.
    /// </summary>
    /// <param name="logger">The logger used to log information and errors.</param>
    /// <param name="sagawayContextManager">The context manager responsible for managing and retrieving Sagaway context.</param>
    public SagawayContextPropagationHandler(
        ILogger<SagawayContextPropagationHandler> logger,
        ISagawayContextManager sagawayContextManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sagawayContextManager = sagawayContextManager ?? throw new ArgumentNullException(nameof(sagawayContextManager));
    }

    /// <summary>
    /// Sends the HTTP request, ensuring that the Sagaway context is propagated by adding 
    /// the appropriate headers if they are not already present in the request.
    /// </summary>
    /// <param name="request">The outgoing HTTP request.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the request.</param>
    /// <returns>The HTTP response message.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Propagating Sagaway context");

        //If the request does not contain the sagaway context header (set by the Dapr InvokeBindingAsync downstream call, we assume an upstream call)
        if (!request.Headers.Contains(_sagawayContextManager.SagaWayContextHeaderKeyName))
        {
            _logger.LogInformation("Sagaway context header is not found, Add Sagaway context header to the downstream call.");
            var sagawayContext = _sagawayContextManager.GetUpStreamCallContext();
            
            foreach (var headers in sagawayContext)
            {
                if (request.Headers.Contains(headers.Key))
                {
                    request.Headers.Remove(headers.Key);
                }
                request.Headers.Add(headers.Key, headers.Value);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}