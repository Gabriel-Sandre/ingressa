using System.Text.Json;
using Ingressa.Domain.Comum;
using Ingressa.Domain.Pedidos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ingressa.Infrastructure.Outbox;

/// <summary>Mensagem gravada na mesma transação da mudança de estado e publicada depois.</summary>
public class MensagemOutbox
{
    public const int MaximoTentativas = 10;

    public long Id { get; private set; }
    public Guid MensagemId { get; private set; }
    public string Tipo { get; private set; } = string.Empty;
    public string Conteudo { get; private set; } = string.Empty;
    public DateTime OcorridoEm { get; private set; }
    public string? CorrelacaoId { get; private set; }
    public DateTime? PublicadaEm { get; private set; }
    public int Tentativas { get; private set; }
    public string? UltimoErro { get; private set; }

    public static MensagemOutbox De(IEventoDeDominio evento, string? correlacaoId) => new()
    {
        MensagemId = Guid.NewGuid(),
        Tipo = CatalogoDeMensagens.TipoDe(evento),
        Conteudo = JsonSerializer.Serialize(evento, evento.GetType(), CatalogoDeMensagens.Json),
        OcorridoEm = evento.OcorridoEm,
        CorrelacaoId = correlacaoId
    };

    public void MarcarComoPublicada(DateTime agora)
    {
        PublicadaEm = agora;
        UltimoErro = null;
    }

    public void RegistrarFalha(string erro)
    {
        Tentativas++;
        UltimoErro = erro.Length > 2000 ? erro[..2000] : erro;
    }
}

/// <summary>
/// Nome estável de cada mensagem. É também a routing key no RabbitMQ.
/// Renomear uma classe C# não pode quebrar mensagens já gravadas.
/// </summary>
public static class CatalogoDeMensagens
{
    public const string PedidoPago = "pedido.pago";
    public const string PedidoExpirado = "pedido.expirado";
    public const string PedidoCancelado = "pedido.cancelado";
    public const string IngressosEmitidos = "ingressos.emitidos";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<Type, string> PorTipo = new()
    {
        [typeof(PedidoPago)] = PedidoPago,
        [typeof(PedidoExpirado)] = PedidoExpirado,
        [typeof(PedidoCancelado)] = PedidoCancelado,
        [typeof(IngressosEmitidos)] = IngressosEmitidos
    };

    private static readonly Dictionary<string, Type> PorNome = PorTipo.ToDictionary(p => p.Value, p => p.Key);

    public static string TipoDe(IEventoDeDominio evento) =>
        PorTipo.TryGetValue(evento.GetType(), out var nome)
            ? nome
            : throw new InvalidOperationException($"Evento {evento.GetType().Name} não está no catálogo de mensagens.");

    public static IEventoDeDominio? Ler(string tipo, ReadOnlySpan<byte> conteudo) =>
        PorNome.TryGetValue(tipo, out var clrType)
            ? (IEventoDeDominio?)JsonSerializer.Deserialize(conteudo, clrType, Json)
            : null;
}

internal sealed class MensagemOutboxConfiguracao : IEntityTypeConfiguration<MensagemOutbox>
{
    public void Configure(EntityTypeBuilder<MensagemOutbox> builder)
    {
        builder.ToTable("MensagensOutbox");
        builder.Property(m => m.Tipo).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Conteudo).HasColumnType("jsonb").IsRequired();
        builder.Property(m => m.CorrelacaoId).HasMaxLength(64);
        builder.Property(m => m.UltimoErro).HasMaxLength(2000);
        builder.HasIndex(m => m.MensagemId).IsUnique();
        // Só as pendentes interessam ao despachante.
        builder.HasIndex(m => m.Id).HasFilter("\"PublicadaEm\" IS NULL").HasDatabaseName("IX_MensagensOutbox_Pendentes");
    }
}
