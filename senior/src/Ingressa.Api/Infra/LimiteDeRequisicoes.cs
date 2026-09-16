using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Ingressa.Api.Infra;

public static class LimiteDeRequisicoes
{
    public const string Autenticacao = "autenticacao";

    /// <summary>
    /// Limita tentativas de login/cadastro por IP. Junto com o bloqueio de conta,
    /// dificulta ataques de força bruta e de "credential stuffing".
    /// </summary>
    public static IServiceCollection AddLimiteDeRequisicoes(this IServiceCollection services, IConfiguration configuracao)
    {
        var porMinuto = configuracao.GetValue("LimiteDeRequisicoes:AutenticacaoPorMinuto", 10);

        return services.AddRateLimiter(opcoes =>
        {
            opcoes.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            opcoes.AddPolicy(Autenticacao, contexto => RateLimitPartition.GetFixedWindowLimiter(
                contexto.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = porMinuto,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));

            opcoes.OnRejected = async (contexto, ct) =>
            {
                if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
                {
                    contexto.HttpContext.Response.Headers.RetryAfter = ((int)espera.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                await contexto.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Muitas tentativas",
                    Detail = "Você fez muitas tentativas em pouco tempo. Aguarde um minuto e tente de novo."
                }, ct);
            };
        });
    }
}
