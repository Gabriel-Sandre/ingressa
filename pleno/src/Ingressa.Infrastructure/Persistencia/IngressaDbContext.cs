using System.Diagnostics;
using Ingressa.Domain.Comum;
using Ingressa.Domain.Eventos;
using Ingressa.Domain.Pedidos;
using Ingressa.Domain.Usuarios;
using Ingressa.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Ingressa.Infrastructure.Persistencia;

public class IngressaDbContext(DbContextOptions<IngressaDbContext> options) : DbContext(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Evento> Eventos => Set<Evento>();
    public DbSet<Setor> Setores => Set<Setor>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<ItemPedido> ItensPedido => Set<ItemPedido>();
    public DbSet<Ingresso> Ingressos => Set<Ingresso>();
    public DbSet<MensagemOutbox> MensagensOutbox => Set<MensagemOutbox>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IngressaDbContext).Assembly);

    /// <summary>
    /// Antes de gravar, transforma os eventos de domínio em mensagens da outbox.
    /// Como tudo vai no mesmo SaveChanges, a mudança de estado e a mensagem são
    /// gravadas juntas ou não são gravadas (padrão Transactional Outbox).
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entidades = ChangeTracker.Entries<Entidade>()
            .Select(e => e.Entity)
            .Where(e => e.Eventos.Count > 0)
            .ToList();

        if (entidades.Count == 0)
        {
            return await base.SaveChangesAsync(cancellationToken);
        }

        // Os eventos carregam o Id da entidade, que só existe depois da primeira gravação.
        // Gravar em duas etapas quebraria a atomicidade, então isso é proibido por projeto.
        if (entidades.Exists(e => e.Id == 0))
        {
            throw new InvalidOperationException("Entidades novas não podem registrar eventos de domínio antes de serem gravadas.");
        }

        var correlacao = Activity.Current?.TraceId.ToString();
        foreach (var entidade in entidades)
        {
            foreach (var evento in entidade.Eventos)
            {
                MensagensOutbox.Add(MensagemOutbox.De(evento, correlacao));
            }

            entidade.LimparEventos();
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
