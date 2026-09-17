using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;

namespace Ingressa.Api.Infra;

public static class LimiteDeRequisicoes
{
    /// <summary>
    /// Primeira linha de defesa, em memória e por IP, para todas as rotas (exceto health checks).
    /// Os limites finos por usuário e por operação são distribuídos (Redis): ver <see cref="LimiteDistribuidoAttribute"/>.
    /// </summary>
    public static IServiceCollection AddLimiteDeRequisicoes(this IServiceCollection services, IConfiguration configuracao)
    {
        var porMinuto = configuracao.GetValue("LimiteDeRequisicoes:GlobalPorMinuto", 600);
        services.AddSingleton<PoliticasDeLimite>();

        return services.AddRateLimiter(opcoes =>
        {
            opcoes.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            opcoes.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
                contexto.Request.Path.StartsWithSegments("/health")
                    ? RateLimitPartition.GetNoLimiter("health")
                    : RateLimitPartition.GetSlidingWindowLimiter(
                        contexto.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
                        _ => new SlidingWindowRateLimiterOptions
                        {
                            PermitLimit = porMinuto,
                            Window = TimeSpan.FromMinutes(1),
                            SegmentsPerWindow = 6,
                            QueueLimit = 0
                        }));

            opcoes.OnRejected = async (contexto, ct) =>
            {
                if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
                {
                    contexto.HttpContext.Response.Headers.RetryAfter =
                        ((int)espera.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                await contexto.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Muitas requisições",
                    Detail = "Muitas requisições deste endereço. Aguarde um pouco e tente de novo."
                }, ct);
            };
        });
    }
}
