using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace eShop.ServiceDefaults;

public static partial class Extensions
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.AddBasicServiceDefaults();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>
    /// Adds the services except for making outgoing HTTP calls.
    /// </summary>
    /// <remarks>
    /// This allows for things like Polly to be trimmed out of the app if it isn't used.
    /// </remarks>
    public static IHostApplicationBuilder AddBasicServiceDefaults(this IHostApplicationBuilder builder)
    {
        // Default health checks assume the event bus and self health checks
        builder.AddDefaultHealthChecks();

        builder.ConfigureOpenTelemetry();

        return builder;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("eShop.Basket.API")
                    .AddMeter("Experimental.Microsoft.Extensions.AI");
            })
            .WithTracing(tracing =>
            {
                if (builder.Environment.IsDevelopment())
                {
                    // We want to view all traces in development
                    tracing.SetSampler(new AlwaysOnSampler());
                }

                tracing.AddAspNetCoreInstrumentation()
                    .AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddSource("eShop.WebApp.BasketService")
                    .AddSource("Experimental.Microsoft.Extensions.AI")
                    .AddProcessor(new SensitiveDataMaskingProcessor());
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.Configure<OpenTelemetryLoggerOptions>(logging => logging.AddOtlpExporter());
            builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddOtlpExporter());
            builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddOtlpExporter());
        }

        return builder;
    }

    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Uncomment the following line to enable the Prometheus endpoint (requires the OpenTelemetry.Exporter.Prometheus.AspNetCore package)
        //app.MapPrometheusScrapingEndpoint();

        // Adding health checks endpoints to applications in non-development environments has security implications.
        // See https://aka.ms/dotnet/aspire/healthchecks for details before enabling these endpoints in non-development environments.
        if (app.Environment.IsDevelopment())
        {
            // All health checks must pass for app to be considered ready to accept traffic after starting
            app.MapHealthChecks("/health");

            // Only health checks tagged with the "live" tag must pass for app to be considered alive
            app.MapHealthChecks("/alive", new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }
}

/// <summary>
/// Processador para mascarar dados sensíveis em telemetria
/// </summary>
public class SensitiveDataMaskingProcessor : BaseProcessor<Activity>
{
    private static readonly string[] SensitiveTagKeys =
    {
        "user_id", "userId", "sub", "subject", "card_number", "card_security_number",
        "http.request.header.Authorization", "enduser.id", "user.id", "userId"
    };

    public override void OnEnd(Activity activity)
    {
        if (activity == null) return;

        // Mascarar atributos sensíveis nos tags
        foreach (var tag in activity.Tags.ToList())
        {
            foreach (var sensitiveKey in SensitiveTagKeys)
            {
                if (tag.Key.Contains(sensitiveKey, StringComparison.OrdinalIgnoreCase))
                {
                    activity.SetTag(tag.Key, "***MASKED***");
                }
            }

            // Verificar e mascarar valores que parecem ser IDs de utilizadores em qualquer tag
            if (tag.Value?.ToString()?.Contains("user_id=") == true)
            {
                activity.SetTag(tag.Key, MaskSensitiveData(tag.Value.ToString()));
            }
        }

        // Mascarar dados em eventos
        foreach (var evt in activity.Events)
        {
            foreach (var tag in evt.Tags.ToList())
            {
                foreach (var sensitiveKey in SensitiveTagKeys)
                {
                    if (tag.Key.Contains(sensitiveKey, StringComparison.OrdinalIgnoreCase))
                    {
                        // Como não podemos modificar os Tags diretamente em um evento,
                        // registramos que processamos a informação sensível
                        activity.AddTag($"masked.{tag.Key}", "true");
                    }
                }
            }
        }

        base.OnEnd(activity);
    }



    /// <summary>
    /// Mascara dados sensíveis como IDs de utilizadores em strings
    /// </summary>
    private static string MaskSensitiveData(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // Padrão para user_id=valor em URLs
        var userIdInUrlPattern = @"user_id=([^&\s]+)";
        input = Regex.Replace(input, userIdInUrlPattern, "user_id=***MASKED***");

        // Padrão para "sub":"valor" ou "userId":"valor" em JSON 
        var userIdInJsonPattern = @"(""sub""|""userId""|""user_id""):""([^""]+)""";
        input = Regex.Replace(input, userIdInJsonPattern, "$1:\"***MASKED***\"");

        return input;
    }
}
