using Ingressa.Domain.Eventos;
using Ingressa.Domain.Usuarios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ingressa.Infrastructure.Persistencia.Configuracoes;

internal sealed class EventoConfiguracao : IEntityTypeConfiguration<Evento>
{
    public void Configure(EntityTypeBuilder<Evento> builder)
    {
        builder.ToTable("Eventos");
        builder.Property(e => e.Titulo).HasMaxLength(150).IsRequired();
        builder.Property(e => e.Descricao).HasMaxLength(4000);
        builder.Property(e => e.Local).HasMaxLength(150).IsRequired();
        builder.Property(e => e.Cidade).HasMaxLength(100).IsRequired();
        builder.HasIndex(e => new { e.Publicado, e.DataInicio });
        builder.HasIndex(e => e.OrganizadorId);
        builder.Ignore(e => e.Eventos);
        builder.Ignore(e => e.TemVendas);

        builder.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(e => e.OrganizadorId)
            .OnDelete(DeleteBehavior.Restrict);

        // A lista de setores é um campo privado; a entidade só expõe leitura.
        builder.HasMany(e => e.Setores)
            .WithOne()
            .HasForeignKey(s => s.EventoId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Setores).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class SetorConfiguracao : IEntityTypeConfiguration<Setor>
{
    public void Configure(EntityTypeBuilder<Setor> builder)
    {
        // Última linha de defesa: mesmo que um bug passe pela aplicação,
        // o banco recusa ocupar mais lugares do que existem.
        builder.ToTable("Setores", t =>
            t.HasCheckConstraint("CK_Setores_Ocupados", "\"Ocupados\" >= 0 AND \"Ocupados\" <= \"Capacidade\""));
        builder.Property(s => s.Nome).HasMaxLength(80).IsRequired();
        builder.Property(s => s.Preco).HasPrecision(10, 2);
        builder.Ignore(s => s.Disponiveis);
    }
}
