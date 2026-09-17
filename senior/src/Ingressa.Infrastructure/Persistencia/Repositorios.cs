using Ingressa.Application.Abstracoes;
using Ingressa.Domain.Eventos;
using Ingressa.Domain.Pedidos;
using Ingressa.Domain.Usuarios;
using Microsoft.EntityFrameworkCore;

namespace Ingressa.Infrastructure.Persistencia;

internal sealed class UsuarioRepositorio(IngressaDbContext db) : IUsuarioRepositorio
{
    public Task<Usuario?> ObterAsync(int id, CancellationToken ct) =>
        db.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<Usuario?> ObterPorEmailAsync(string emailNormalizado, CancellationToken ct) =>
        db.Usuarios.FirstOrDefaultAsync(u => u.Email == emailNormalizado, ct);

    public Task<bool> EmailExisteAsync(string emailNormalizado, CancellationToken ct) =>
        db.Usuarios.AnyAsync(u => u.Email == emailNormalizado, ct);

    public async Task<IReadOnlyList<Usuario>> ListarOrganizadoresPendentesAsync(CancellationToken ct) =>
        await db.Usuarios
            .Where(u => u.Perfil == PerfilUsuario.Organizador && u.Status == StatusConta.AguardandoAprovacao)
            .OrderBy(u => u.CriadoEm)
            .ToListAsync(ct);

    public void Adicionar(Usuario usuario) => db.Usuarios.Add(usuario);
}

internal sealed class RefreshTokenRepositorio(IngressaDbContext db) : IRefreshTokenRepositorio
{
    public Task<RefreshToken?> ObterPorHashAsync(string tokenHash, CancellationToken ct) =>
        db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task RevogarFamiliaAsync(Guid familia, DateTime agora, CancellationToken ct) =>
        await db.RefreshTokens
            .Where(t => t.Familia == familia && t.RevogadoEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevogadoEm, agora), ct);

    public void Adicionar(RefreshToken token) => db.RefreshTokens.Add(token);
}

internal sealed class EventoRepositorio(IngressaDbContext db) : IEventoRepositorio
{
    public Task<Evento?> ObterAsync(int id, CancellationToken ct) =>
        db.Eventos.Include(e => e.Setores).FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<IReadOnlyList<Evento>> ListarDoOrganizadorAsync(int organizadorId, CancellationToken ct) =>
        await db.Eventos.AsNoTracking()
            .Include(e => e.Setores)
            .Where(e => e.OrganizadorId == organizadorId)
            .OrderByDescending(e => e.DataInicio)
            .ToListAsync(ct);

    public void Adicionar(Evento evento) => db.Eventos.Add(evento);

    public void Remover(Evento evento) => db.Eventos.Remove(evento);
}

internal sealed class PedidoRepositorio(IngressaDbContext db) : IPedidoRepositorio
{
    public Task<Pedido?> ObterAsync(int id, CancellationToken ct) =>
        db.Pedidos
            .Include(p => p.Itens)
            .Include(p => p.Ingressos)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<int>> ListarIdsDeReservasVencidasAsync(DateTime agora, int limite, CancellationToken ct) =>
        await db.Pedidos
            .Where(p => p.Status == StatusPedido.AguardandoPagamento && p.ExpiraEm <= agora)
            .OrderBy(p => p.ExpiraEm)
            .Select(p => p.Id)
            .Take(limite)
            .ToListAsync(ct);

    public void Adicionar(Pedido pedido) => db.Pedidos.Add(pedido);
}
