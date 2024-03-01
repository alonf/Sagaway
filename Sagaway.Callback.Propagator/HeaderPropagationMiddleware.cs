﻿namespace Sagaway.Callback.Propagator;

using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

public class HeaderPropagationMiddleware(RequestDelegate next)
{
    // Using AsyncLocal to store the headers per request context
    public static readonly AsyncLocal<SagawayContext> SagawayContext = new();

    // The propagated callback binding name
    public static string CallbackBindingName => SagawayContext.Value?.CallbackBindingName ?? throw new InvalidOperationException("CallbackBindingName is not set");

    public async Task InvokeAsync(HttpContext context)
    {
        // Store the headers in the SagawayContext
        SagawayContext.Value = new SagawayContext(
            context.Request.Headers["x-sagaway-dapr-actor-id"],
            context.Request.Headers["x-sagaway-dapr-actor-type"],
            context.Request.Headers["x-sagaway-callback-binding-name"],
            context.Request.Headers["x-sagaway-callback-method"],
            context.Request.Headers["x-sagaway-message-dispatch-time"]
        );
    
        
        // Call the next delegate/middleware in the pipeline
        await next(context);
    }
}