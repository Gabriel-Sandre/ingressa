using Ingressa.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ingressa.Infrastructure.Idempotencia;

/// <summary>
/// Registro de uma requisição com cabeçalho Idempotency-Key. Garante que repetir a mesma
/// requisição (clique duplo, nova tentativa após queda de rede) não cria um segundo pedido
/// nem uma segunda cobrança: a resposta original é devolvida.
/// </summary>
public class RequisicaoIdempotente
{
    public static readonly TimeSpan Retencao = TimeSpan.FromHours(24);

    /// <summary>
    /// Nenhuma requisição legítima fica tanto tempo em execução: uma chave "em andamento"
    /// mais antiga que isso pertence a um processo que caiu, e pode ser retomada.
    /// </summary>
    public static readonly TimeSpan TempoMaximoEmAndamento = TimeSpan.FromMinutes(2);

    public long Id { get; private set; }
    public int UsuarioId { get; private set; }
    public string Chave { get; private set; } = string.Empty;
    public string Rota { get; private set; } = string.Empty;
    public string HashDaRequisicao { get; private set; } = string.Empty;
    public int? StatusHttp { get; private set; }
    public string? Resposta { get; private set; }

    /// <summary>Cabeçalho Location da resposta original (o 201 da reserva aponta para o pedido criado).</summary>
    public string? Local { get; private set; }
    public DateTime CriadaEm { get; private set; }
    public DateTime? ConcluidaEm { get; private set; }
}

public enum SituacaoDaChave
{
    /// <summary>Primeira vez: a requisição deve ser executada.</summary>
    Nova,

    /// <summary>Já executada: devolver a resposta guardada.</summary>
    Concluida,

    /// <summary>Outra requisição com a mesma chave ainda está em execução.</summary>
    EmAndamento,

    /// <summary>A mesma chave foi usada com um conteúdo diferente.</summary>
    Divergente
}

public sealed record ReivindicacaoDeChave(SituacaoDaChave Situacao, long Id, int? StatusHttp, string? Resposta, string? Local = null);

public sealed class ControleDeIdempotencia(IngressaDbContext db, TimeProvider relogio)
{
    public const int TamanhoMaximoDaChave = 100;

    /// <summary>
    /// Tenta registrar a chave. O INSERT ... ON CONFLICT DO NOTHING é atômico:
    /// se duas requisições iguais chegarem juntas, só uma consegue.
    /// </summary>
    public async Task<ReivindicacaoDeChave> ReivindicarAsync(
        int usuarioId, string chave, string rota, string hash, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var inseridos = await db.Database.SqlQuery<long>($"""
            INSERT INTO "RequisicoesIdempotentes" ("UsuarioId", "Chave", "Rota", "HashDaRequisicao", "CriadaEm")
            VALUES ({usuarioId}, {chave}, {rota}, {hash}, {agora})
            ON CONFLICT ("UsuarioId", "Chave") DO NOTHING
            RETURNING "Id" AS "Value"
            """).ToListAsync(ct);

        if (inseridos.Count == 1)
        {
            return new ReivindicacaoDeChave(SituacaoDaChave.Nova, inseridos[0], null, null);
        }

        var existente = await db.RequisicoesIdempotentes.AsNoTracking()
            .SingleAsync(r => r.UsuarioId == usuarioId && r.Chave == chave, ct);

        if (existente.HashDaRequisicao != hash || existente.Rota != rota)
        {
            return new ReivindicacaoDeChave(SituacaoDaChave.Divergente, existente.Id, null, null);
        }

        if (existente.StatusHttp is not null)
        {
            return new ReivindicacaoDeChave(
                SituacaoDaChave.Concluida, existente.Id, existente.StatusHttp, existente.Resposta, existente.Local);
        }

        if (agora - existente.CriadaEm > RequisicaoIdempotente.TempoMaximoEmAndamento)
        {
            // Retomada atômica: só uma das tentativas concorrentes consegue trocar a data.
            var retomadas = await db.RequisicoesIdempotentes
                .Where(r => r.Id == existente.Id && r.StatusHttp == null && r.CriadaEm == existente.CriadaEm)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.CriadaEm, agora), ct);
            if (retomadas == 1)
            {
                return new ReivindicacaoDeChave(SituacaoDaChave.Nova, existente.Id, null, null);
            }
        }

        return new ReivindicacaoDeChave(SituacaoDaChave.EmAndamento, existente.Id, null, null);
    }

    public Task ConcluirAsync(long id, int statusHttp, string resposta, string? local, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        return db.RequisicoesIdempotentes
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.StatusHttp, statusHttp)
                .SetProperty(r => r.Resposta, resposta)
                .SetProperty(r => r.Local, local)
                .SetProperty(r => r.ConcluidaEm, agora), ct);
    }

    /// <summary>Remove a chave quando a requisição falhou, para que o cliente possa tentar de novo.</summary>
    public Task DescartarAsync(long id, CancellationToken ct) =>
        db.RequisicoesIdempotentes.Where(r => r.Id == id).ExecuteDeleteAsync(ct);
}

internal sealed class RequisicaoIdempotenteConfiguracao : IEntityTypeConfiguration<RequisicaoIdempotente>
{
    public void Configure(EntityTypeBuilder<RequisicaoIdempotente> builder)
    {
        builder.ToTable("RequisicoesIdempotentes");
        builder.Property(r => r.Chave).HasMaxLength(ControleDeIdempotencia.TamanhoMaximoDaChave).IsRequired();
        builder.Property(r => r.Rota).HasMaxLength(200).IsRequired();
        builder.Property(r => r.HashDaRequisicao).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Local).HasMaxLength(300);
        builder.Property(r => r.Resposta).HasColumnType("jsonb");
        builder.HasIndex(r => new { r.UsuarioId, r.Chave }).IsUnique();
        builder.HasIndex(r => r.CriadaEm);
    }
}
