using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Ingressa.Infrastructure.FilaVirtual;

public sealed class VerificacaoRedis(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var latencia = await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"Latência {latencia.TotalMilliseconds:0.0} ms");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Sem conexão com o Redis.", ex);
        }
    }
}
