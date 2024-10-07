﻿using Dapr.Client;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sagaway.Callback.Context;

namespace Sagaway.Callback.Propagator;

/// <summary>
/// Extension methods for adding and configuring Sagaway context propagation within a Dapr-based system.
/// </summary>
public static class SagawayContextPropagatorExtensions
{
    // ReSharper disable once UnusedMethodReturnValue.Global
    /// <summary>
    /// Adds middleware to the application pipeline to handle propagation of Sagaway context via HTTP headers.
    /// </summary>
    /// <param name="builder">The application builder to add the middleware to.</param>
    /// <returns>The modified application builder.</returns>
    public static IApplicationBuilder UseSagawayContextPropagator(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<HeaderPropagationMiddleware>();
    }

    // ReSharper disable once UnusedMethodReturnValue.Global
    // ReSharper disable once UnusedMember.Global
    /// <summary>
    /// Adds Dapr integration and Sagaway context propagation capabilities to the service collection.
    /// This includes setting up context managers, metadata providers, and dynamic callback invokers.
    /// </summary>
    /// <param name="services">The service collection to add the context propagators to.</param>
    /// <returns>The services instance to add additional services.</returns>
    public static IServiceCollection AddDaprWithSagawayContextPropagator(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        services.AddScoped<ISagawayContextManager, SagawayContextManager>();

        //add the caller id provider if the user had not provided one
        services.TryAddScoped<ISagawayCallerIdProvider, SagawayCallerIdProvider>();
        
        services.AddScoped<ISagawayCallbackMetadataProvider, SagawayCallbackMetadataProvider>();

        // Conditionally register ISagawayCallbackInvoker using dynamic to avoid direct package dependency
        RegisterCallbackInvokerIfExists(services);


        //for backward compatibility
        services.AddScoped<ICallbackBindingNameProvider>(provider =>
        {
            var callbackMetadataProvider = provider.GetRequiredService<ISagawayCallbackMetadataProvider>();
            return callbackMetadataProvider;
        });

        services.AddScoped<ISagawayContextInformationProvider>(provider =>
        {
            var callbackMetadataProvider = provider.GetRequiredService<ISagawayCallbackMetadataProvider>();
            return (callbackMetadataProvider as ISagawayContextInformationProvider)!;
        });

        // Register the SagawayContextPropagationHandler as a transient service
        services.AddTransient<SagawayContextPropagationHandler>();

        // Configure HttpClient with the SagawayContextPropagationHandler
        services.AddHttpClient("SagawayHttpClient")
            .AddHttpMessageHandler<SagawayContextPropagationHandler>();


        // Add Dapr client and configure it to use the named HttpClient
        services.AddScoped<DaprClient>(provider =>
        {
            var daprClientBuilder = new DaprClientBuilder();

            // Allow users to customize the DaprClientBuilder here
            var jsonOptions = provider.GetRequiredService<IOptions<JsonOptions>>().Value;
            daprClientBuilder.UseJsonSerializationOptions(jsonOptions.SerializerOptions);

            // Get the named HttpClient from IHttpClientFactory
            var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("SagawayHttpClient");

            // Configure DaprClientBuilder to use GrpcChannelOptions with the custom HttpClient
            daprClientBuilder.UseGrpcChannelOptions(new GrpcChannelOptions
            {
                HttpClient = httpClient
            });

            return daprClientBuilder.Build();
        });


        return services;
    }

    /// <summary>
    /// Dynamically registers the `ISagawayCallbackInvoker` and its implementation if they exist
    /// in the loaded assemblies. This avoids direct package dependency while allowing the invoker to be registered.
    /// </summary>
    /// <param name="services">The service collection to add the callback invoker to.</param>
    private static void RegisterCallbackInvokerIfExists(IServiceCollection services)
    {
        // Load types dynamically using AppDomain
        var callbackInvokerType = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "ISagawayCallbackInvoker");

        var callbackInvokerImplType = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "SagaWayCallbackInvoker");

        if (callbackInvokerType != null && callbackInvokerImplType != null)
        {
            // Use reflection to call TryAddScoped<TService, TImplementation>
            var method = typeof(ServiceCollectionDescriptorExtensions)
                .GetMethod(nameof(ServiceCollectionDescriptorExtensions.TryAddScoped), [typeof(IServiceCollection), typeof(Type), typeof(Type)
                ]);

            if (method != null)
            {
                method.Invoke(null, [services, callbackInvokerType, callbackInvokerImplType]);
            }
        }
    }

}