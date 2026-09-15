using Ingressa.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ingressa.Api.Data;

public class IngressaDbContext(DbContextOptions<IngressaDbContext> options) : DbContext(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Evento> Eventos => Set<Evento>();
    public DbSet<Setor> Setores => Set<Setor>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<Ingresso> Ingressos => Set<Ingresso>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Toda data é gravada e lida como UTC. Sem isso, o SQLite devolve
        // DateTime sem fuso (Kind = Unspecified) e o JSON sairia ambíguo.
        configurationBuilder.Properties<DateTime>().HaveConversion<ConversorDataUtc>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Usuario>(u =>
        {
            u.Property(x => x.Nome).HasMaxLength(100).IsRequired();
            u.Property(x => x.Email).HasMaxLength(200).IsRequired();
            u.HasIndex(x => x.Email).IsUnique();
            u.Property(x => x.SenhaHash).HasMaxLength(200).IsRequired();
            u.Property(x => x.Perfil).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<Evento>(e =>
        {
            e.Property(x => x.Titulo).HasMaxLength(150).IsRequired();
            e.Property(x => x.Descricao).HasMaxLength(4000);
            e.Property(x => x.Local).HasMaxLength(150).IsRequired();
            e.Property(x => x.Cidade).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.Publicado, x.DataInicio });
            e.HasOne(x => x.Organizador)
                .WithMany()
                .HasForeignKey(x => x.OrganizadorId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Setores)
                .WithOne(s => s.Evento)
                .HasForeignKey(s => s.EventoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Setor>(s =>
        {
            s.Property(x => x.Nome).HasMaxLength(80).IsRequired();
            s.Property(x => x.Preco).HasPrecision(10, 2);
            s.Ignore(x => x.Disponiveis);
        });

        modelBuilder.Entity<Pedido>(p =>
        {
            p.Property(x => x.Total).HasPrecision(10, 2);
            p.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            p.HasOne(x => x.Usuario)
                .WithMany()
                .HasForeignKey(x => x.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict);
            p.HasOne(x => x.Evento)
                .WithMany()
                .HasForeignKey(x => x.EventoId)
                .OnDelete(DeleteBehavior.Restrict);
            p.HasMany(x => x.Ingressos)
                .WithOne(i => i.Pedido)
                .HasForeignKey(i => i.PedidoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Ingresso>(i =>
        {
            i.Property(x => x.Codigo).HasMaxLength(32).IsRequired();
            i.HasIndex(x => x.Codigo).IsUnique();
            i.Property(x => x.PrecoPago).HasPrecision(10, 2);
            i.HasOne(x => x.Setor)
                .WithMany()
                .HasForeignKey(x => x.SetorId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}

internal sealed class ConversorDataUtc() : ValueConverter<DateTime, DateTime>(
    valor => valor.Kind == DateTimeKind.Utc ? valor : valor.ToUniversalTime(),
    valor => DateTime.SpecifyKind(valor, DateTimeKind.Utc));
