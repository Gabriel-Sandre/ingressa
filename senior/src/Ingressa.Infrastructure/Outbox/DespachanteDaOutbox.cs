using Ingressa.Infrastructure.Mensageria;
using Ingressa.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ingressa.Infrastructure.Outbox;

/// <summary>
/// Lê mensagens pendentes da outbox e publica no RabbitMQ. Garante entrega
/// "pelo menos uma vez": se o processo cair entre publicar e marcar como publicada,
/// a mensagem sai de novo — por isso os consumidores são idempotentes.
/// </summary>
public sealed class DespachanteDaOutbox(
    IServiceScopeFactory scopes,
    IPublicadorDeMensagens publicador,
    TimeProvider relogio,
    ILogger<DespachanteDaOutbox> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(1);
    private const int Lote = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int publicadas;
            try
            {
                publicadas = await PublicarLoteAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao processar a outbox; nova tentativa em instantes");
                publicadas = 0;
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }

            if (publicadas < Lote)
            {
                await Task.Delay(Intervalo, stoppingToken);
            }
        }
    }

    internal async Task<int> PublicarLoteAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IngressaDbContext>();

        await using var transacao = await db.Database.BeginTransactionAsync(ct);

        // SKIP LOCKED: várias instâncias do Worker podem rodar ao mesmo tempo
        // sem pegar a mesma mensagem.
        var mensagens = await db.MensagensOutbox
            .FromSql($"""
                SELECT * FROM "MensagensOutbox"
                WHERE "PublicadaEm" IS NULL AND "Tentativas" < {MensagemOutbox.MaximoTentativas}
                ORDER BY "Id"
                LIMIT {Lote}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        foreach (var mensagem in mensagens)
        {
            try
            {
                await publicador.PublicarAsync(mensagem, ct);
                mensagem.MarcarComoPublicada(relogio.GetUtcNow().UtcDateTime);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                mensagem.RegistrarFalha(ex.Message);
                logger.LogWarning(ex, "Falha ao publicar a mensagem {MensagemId} ({Tipo}), tentativa {Tentativa}",
                    mensagem.MensagemId, mensagem.Tipo, mensagem.Tentativas);
            }
        }

        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return mensagens.Count;
    }
}
