using Ingressa.Domain.Eventos;
using Ingressa.Domain.Pedidos;
using Ingressa.Domain.Usuarios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ingressa.Infrastructure.Persistencia.Configuracoes;

internal sealed class PedidoConfiguracao : IEntityTypeConfiguration<Pedido>
{
    public void Configure(EntityTypeBuilder<Pedido> builder)
    {
        builder.ToTable("Pedidos");
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.Total).HasPrecision(10, 2);
        builder.Property(p => p.CodigoPagamento).HasMaxLength(64);

        // No PostgreSQL, IsRowVersion em uma propriedade uint usa a coluna de sistema xmin:
        // um UPDATE só acontece se ninguém alterou a linha desde a leitura.
        builder.Property(p => p.Versao).IsRowVersion();

        // Índice parcial para o job de expiração encontrar reservas vencidas rapidamente.
        builder.HasIndex(p => p.ExpiraEm)
            .HasFilter("\"Status\" = 'AguardandoPagamento'");
        builder.HasIndex(p => new { p.UsuarioId, p.CriadoEm });

        builder.Ignore(p => p.Eventos);
        builder.Ignore(p => p.IngressosEmitidos);

        builder.HasOne<Usuario>().WithMany().HasForeignKey(p => p.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Evento>().WithMany().HasForeignKey(p => p.EventoId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Itens).WithOne().HasForeignKey(i => i.PedidoId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Itens).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Ingressos).WithOne().HasForeignKey(i => i.PedidoId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Ingressos).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ItemPedidoConfiguracao : IEntityTypeConfiguration<ItemPedido>
{
    public void Configure(EntityTypeBuilder<ItemPedido> builder)
    {
        builder.ToTable("ItensPedido", t => t.HasCheckConstraint("CK_ItensPedido_Quantidade", "\"Quantidade\" > 0"));
        builder.Property(i => i.NomeSetor).HasMaxLength(80).IsRequired();
        builder.Property(i => i.PrecoUnitario).HasPrecision(10, 2);
        builder.Ignore(i => i.Subtotal);
        builder.HasOne<Setor>().WithMany().HasForeignKey(i => i.SetorId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IngressoConfiguracao : IEntityTypeConfiguration<Ingresso>
{
    public void Configure(EntityTypeBuilder<Ingresso> builder)
    {
        builder.ToTable("Ingressos");
        builder.Property(i => i.Codigo).HasMaxLength(32).IsRequired();
        builder.HasIndex(i => i.Codigo).IsUnique();
        builder.Property(i => i.PrecoPago).HasPrecision(10, 2);
        builder.HasOne<Setor>().WithMany().HasForeignKey(i => i.SetorId).OnDelete(DeleteBehavior.Restrict);
    }
}
