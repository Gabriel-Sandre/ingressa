using Ingressa.Infrastructure.Idempotencia;
using Ingressa.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ingressa.Infrastructure.Manutencao;

/// <summary>
/// Remove periodicamente dados que só têm valor por um tempo:
/// mensagens já publicadas, chaves de idempotência e sessões vencidas.
/// Sem isso, essas tabelas crescem para sempre.
/// </summary>
public sealed class LimpezaDeDados(IServiceScopeFactory scopes, TimeProvider relogio, ILogger<LimpezaDeDados> logger)
    : BackgroundService
{
    public static readonly TimeSpan RetencaoDaOutbox = TimeSpan.FromDays(7);
    public static readonly TimeSpan RetencaoDeSessoes = TimeSpan.FromDays(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var temporizador = new PeriodicTimer(TimeSpan.FromHours(1), relogio);
        do
        {
            try
            {
                await LimparAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha na limpeza de dados; nova tentativa na próxima hora");
            }
        }
        while (await temporizador.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task<(int Mensagens, int Chaves, int Sessoes)> LimparAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IngressaDbContext>();
        var agora = relogio.GetUtcNow().UtcDateTime;

        var limiteOutbox = agora - RetencaoDaOutbox;
        var mensagens = await db.MensagensOutbox
            .Where(m => m.PublicadaEm != null && m.PublicadaEm < limiteOutbox)
            .ExecuteDeleteAsync(ct);

        var limiteChaves = agora - RequisicaoIdempotente.Retencao;
        var chaves = await db.RequisicoesIdempotentes
            .Where(r => r.CriadaEm < limiteChaves)
            .ExecuteDeleteAsync(ct);

        var limiteSessoes = agora - RetencaoDeSessoes;
        var sessoes = await db.RefreshTokens
            .Where(t => t.ExpiraEm < limiteSessoes)
            .ExecuteDeleteAsync(ct);

        if (mensagens + chaves + sessoes > 0)
        {
            logger.LogInformation(
                "Limpeza: {Mensagens} mensagem(ns), {Chaves} chave(s) de idempotência e {Sessoes} sessão(ões) removidas",
                mensagens, chaves, sessoes);
        }

        return (mensagens, chaves, sessoes);
    }
}
