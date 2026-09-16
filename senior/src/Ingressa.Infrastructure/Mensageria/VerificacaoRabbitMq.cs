using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ingressa.Infrastructure.Mensageria;

public sealed class VerificacaoRabbitMq(ConexaoRabbitMq conexao) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var canal = await conexao.CriarCanalAsync(comConfirmacao: false, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Sem conexão com o RabbitMQ.", ex);
        }
    }
}
