using Ingressa.Application.Abstracoes;
using Ingressa.Application.Eventos;
using Ingressa.Application.Pedidos;
using Ingressa.Domain.Pedidos;
using Microsoft.EntityFrameworkCore;

namespace Ingressa.Infrastructure.Persistencia;

internal sealed class ConsultasDeEventos(IngressaDbContext db) : IConsultasDeEventos
{
    public async Task<PaginaResponse<EventoResumoResponse>> ListarVitrineAsync(
        EventoFiltro filtro, DateTime agora, CancellationToken ct)
    {
        var consulta = db.Eventos.AsNoTracking().Where(e => e.Publicado && e.DataInicio > agora);

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var padrao = $"%{EscaparLike(filtro.Busca.Trim())}%";
            consulta = consulta.Where(e =>
                EF.Functions.ILike(e.Titulo, padrao, "\\") || EF.Functions.ILike(e.Descricao, padrao, "\\"));
        }

        if (!string.IsNullOrWhiteSpace(filtro.Cidade))
        {
            var cidade = EscaparLike(filtro.Cidade.Trim());
            consulta = consulta.Where(e => EF.Functions.ILike(e.Cidade, cidade, "\\"));
        }

        // Agora o preço mínimo e o "esgotado" são calculados pelo banco (o PostgreSQL agrega decimal).
        var projetada = consulta.Select(e => new
        {
            e.Id,
            e.Titulo,
            e.Local,
            e.Cidade,
            e.DataInicio,
            PrecoMinimo = e.Setores.Min(s => (decimal?)s.Preco),
            Esgotado = e.Setores.Any() && e.Setores.All(s => s.Ocupados >= s.Capacidade),
            e.FilaVirtual
        });

        projetada = filtro.Ordem switch
        {
            OrdemEventos.Titulo => projetada.OrderBy(e => e.Titulo).ThenBy(e => e.Id),
            OrdemEventos.Preco => projetada.OrderBy(e => e.PrecoMinimo).ThenBy(e => e.Id),
            _ => projetada.OrderBy(e => e.DataInicio).ThenBy(e => e.Id)
        };

        var total = await consulta.CountAsync(ct);
        var itens = await projetada
            .Skip((filtro.Pagina - 1) * filtro.TamanhoPagina)
            .Take(filtro.TamanhoPagina)
            .Select(e => new EventoResumoResponse(e.Id, e.Titulo, e.Local, e.Cidade, e.DataInicio, e.PrecoMinimo, e.Esgotado, e.FilaVirtual))
            .ToListAsync(ct);

        return Paginas.Criar(itens, filtro.Pagina, filtro.TamanhoPagina, total);
    }

    public async Task<EventoDetalheResponse?> ObterDetalheAsync(int id, CancellationToken ct)
    {
        var evento = await db.Eventos.AsNoTracking().Include(e => e.Setores).FirstOrDefaultAsync(e => e.Id == id, ct);
        return evento is null ? null : MapeamentoDeEventos.ParaDetalhe(evento);
    }

    /// <summary>Impede que % e _ digitados pelo usuário virem curingas do LIKE.</summary>
    internal static string EscaparLike(string texto) =>
        texto.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("%", "\\%", StringComparison.Ordinal)
             .Replace("_", "\\_", StringComparison.Ordinal);
}

internal sealed class ConsultasDePedidos(IngressaDbContext db) : IConsultasDePedidos
{
    public async Task<PedidoResponse?> ObterAsync(int pedidoId, CancellationToken ct)
    {
        var pedido = await ComDetalhes().FirstOrDefaultAsync(p => p.Id == pedidoId, ct);
        if (pedido is null)
        {
            return null;
        }

        var evento = await db.Eventos.AsNoTracking()
            .Where(e => e.Id == pedido.EventoId)
            .Select(e => new { e.Titulo, e.DataInicio })
            .SingleAsync(ct);

        return MapeamentoDePedidos.ParaResponse(pedido, evento.Titulo, evento.DataInicio);
    }

    public async Task<IReadOnlyList<PedidoResponse>> ListarDoUsuarioAsync(int usuarioId, CancellationToken ct)
    {
        var pedidos = await ComDetalhes()
            .Where(p => p.UsuarioId == usuarioId)
            .OrderByDescending(p => p.CriadoEm)
            .ThenByDescending(p => p.Id)
            .Take(100)
            .ToListAsync(ct);

        var idsEventos = pedidos.Select(p => p.EventoId).Distinct().ToList();
        var eventos = await db.Eventos.AsNoTracking()
            .Where(e => idsEventos.Contains(e.Id))
            .Select(e => new { e.Id, e.Titulo, e.DataInicio })
            .ToDictionaryAsync(e => e.Id, ct);

        return pedidos
            .Select(p => MapeamentoDePedidos.ParaResponse(p, eventos[p.EventoId].Titulo, eventos[p.EventoId].DataInicio))
            .ToList();
    }

    public async Task<DadosParaNotificacao?> ObterDadosParaNotificacaoAsync(int pedidoId, CancellationToken ct)
    {
        var dados = await (
            from p in db.Pedidos.AsNoTracking()
            join u in db.Usuarios.AsNoTracking() on p.UsuarioId equals u.Id
            join e in db.Eventos.AsNoTracking() on p.EventoId equals e.Id
            where p.Id == pedidoId
            select new { p.Id, u.Nome, u.Email, e.Titulo, e.DataInicio, e.Local, p.Total })
            .FirstOrDefaultAsync(ct);

        if (dados is null)
        {
            return null;
        }

        var codigos = await db.Ingressos.AsNoTracking()
            .Where(i => i.PedidoId == pedidoId)
            .OrderBy(i => i.Id)
            .Select(i => i.Codigo)
            .ToListAsync(ct);

        return new DadosParaNotificacao(
            dados.Id, dados.Nome, dados.Email, dados.Titulo, dados.DataInicio, dados.Local, dados.Total, codigos);
    }

    private IQueryable<Pedido> ComDetalhes() =>
        db.Pedidos.AsNoTracking().Include(p => p.Itens).Include(p => p.Ingressos).AsSplitQuery();
}
