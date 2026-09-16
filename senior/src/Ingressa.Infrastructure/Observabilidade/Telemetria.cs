using System.Diagnostics;
using Ingressa.Application.Observabilidade;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Ingressa.Infrastructure.Observabilidade;

public static class Telemetria
{
    /// <summary>
    /// Rastreamento, métricas e logs com OpenTelemetry. Quando <c>Otlp:Endpoint</c> está
    /// configurado, tudo é enviado por OTLP (no ambiente local, para o Grafana LGTM).
    /// </summary>
    public static IServiceCollection AddObservabilidade(
        this IServiceCollection services, IConfiguration configuracao, string nomeDoServico)
    {
        var endpoint = configuracao["Otlp:Endpoint"];
        var ambiente = configuracao["ASPNETCORE_ENVIRONMENT"] ?? "Production";

        var otel = services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(nomeDoServico, serviceNamespace: "ingressa", serviceVersion: "3.0.0")
                .AddAttributes([new KeyValuePair<string, object>("deployment.environment.name", ambiente)]))
            .WithTracing(t => t
                .AddSource(Metricas.Nome)
                .AddSource("Npgsql")
                .AddAspNetCoreInstrumentation(o => o.Filter = IgnorarHealthChecks)
                .AddHttpClientInstrumentation())
            .WithMetrics(m => m
                .AddMeter(Metricas.Nome)
                .AddMeter("Npgsql")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithLogging(configureOptions: o =>
            {
                o.IncludeScopes = true;
                o.IncludeFormattedMessage = true;
            });

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            otel.UseOtlpExporter(OtlpExportProtocol.Grpc, new Uri(endpoint));
        }

        return services;
    }

    private static bool IgnorarHealthChecks(HttpContext contexto) =>
        !contexto.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);

    /// <summary>Lê um traceparent (W3C) guardado na outbox ou recebido numa mensagem.</summary>
    public static ActivityContext ContextoPai(string? traceparent) =>
        ActivityContext.TryParse(traceparent, null, out var contexto) ? contexto : default;
}
