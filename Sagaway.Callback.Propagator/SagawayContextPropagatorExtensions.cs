﻿using Dapr.Client;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Sagaway.Callback.Propagator;

public static class SagawayContextPropagatorExtensions
{
    // ReSharper disable once UnusedMethodReturnValue.Global
    public static IApplicationBuilder UseSagawayContextPropagator(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<HeaderPropagationMiddleware>();
    }

    // ReSharper disable once UnusedMethodReturnValue.Global
    public static IServiceCollection AddSagawayContextPropagator(this IServiceCollection services)
    {
        services.AddSingleton<ISagawayContextManager, SagawayContextManager>();
        return services.AddSingleton<ICallbackBindingNameProvider, CallbackBindingNameProvider>();
    }

    public static IMvcBuilder AddDaprWithSagawayContextPropagator(this IMvcBuilder builder)
    {
        return builder.AddDapr(b => b.AddSagawayContextPropagator());
    }

    // ReSharper disable once UnusedMethodReturnValue.Global
    public static DaprClientBuilder AddSagawayContextPropagator(this DaprClientBuilder builder, 
        Func<HttpMessageHandler, HttpClient>? httpClientProvider = null)
    {
        httpClientProvider ??= _ =>
        {
            // Create an instance of the HttpClientHandler or your specific inner handler here
            var innerHandler = new HttpClientHandler();

            // Ensure SagawayContextPropagationHandler is properly chained
            var propagationHandler = new SagawayContextPropagationHandler()
            {
                InnerHandler = innerHandler
            };
            return new HttpClient(propagationHandler);
        };

        //add http middleware to propagate sagaway context
        builder.UseGrpcChannelOptions(new GrpcChannelOptions()
        {
            HttpClient = httpClientProvider(new SagawayContextPropagationHandler())
        });

        return builder;

    }
}