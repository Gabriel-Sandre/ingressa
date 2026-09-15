using Ingressa.Api.Data;
using Ingressa.Api.Dtos;
using Ingressa.Api.Erros;
using Ingressa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Ingressa.Api.Services;

public sealed class EventoService(IngressaDbContext db, TimeProvider relogio)
{
    private DateTime Agora => relogio.GetUtcNow().UtcDateTime;

    // ---------- Vitrine (público) ----------

    public async Task<PaginaResponse<EventoResumoResponse>> ListarPublicadosAsync(
        EventoFiltro filtro, CancellationToken ct = default)
    {
        var agora = Agora;
        var consulta = db.Eventos.AsNoTracking().Where(e => e.Publicado && e.DataInicio > agora);

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var termo = filtro.Busca.Trim().ToLower();
            consulta = consulta.Where(e =>
                e.Titulo.ToLower().Contains(termo) || e.Descricao.ToLower().Contains(termo));
        }

        if (!string.IsNullOrWhiteSpace(filtro.Cidade))
        {
            var cidade = filtro.Cidade.Trim().ToLower();
            consulta = consulta.Where(e => e.Cidade.ToLower() == cidade);
        }

        // O desempate por Id garante uma ordem estável entre as páginas.
        consulta = filtro.Ordem == OrdemEventos.Titulo
            ? consulta.OrderBy(e => e.Titulo).ThenBy(e => e.Id)
            : consulta.OrderBy(e => e.DataInicio).ThenBy(e => e.Id);

        var totalItens = await consulta.CountAsync(ct);

        var pagina = await consulta
            .Skip((filtro.Pagina - 1) * filtro.TamanhoPagina)
            .Take(filtro.TamanhoPagina)
            .Select(e => new
            {
                e.Id,
                e.Titulo,
                e.Local,
                e.Cidade,
                e.DataInicio,
                Setores = e.Setores.Select(s => new { s.Preco, s.Capacidade, s.Vendidos }).ToList()
            })
            .ToListAsync(ct);

        // Preço mínimo e "esgotado" são calculados em memória: o SQLite não
        // consegue agregar colunas decimal. Na versão Pleno (PostgreSQL) isso vai para o banco.
        var itens = pagina
            .Select(e => new EventoResumoResponse(
                e.Id,
                e.Titulo,
                e.Local,
                e.Cidade,
                e.DataInicio,
                e.Setores.Count == 0 ? null : e.Setores.Min(s => s.Preco),
                e.Setores.Count > 0 && e.Setores.All(s => s.Vendidos >= s.Capacidade)))
            .ToList();

        var totalPaginas = (int)Math.Ceiling(totalItens / (double)filtro.TamanhoPagina);
        return new PaginaResponse<EventoResumoResponse>(itens, filtro.Pagina, filtro.TamanhoPagina, totalItens, totalPaginas);
    }

    /// <summary>
    /// Detalhe de um evento. Rascunhos só são visíveis para o próprio organizador;
    /// para os demais, respondem 404 como se não existissem.
    /// </summary>
    public async Task<EventoDetalheResponse> ObterAsync(int id, int? usuarioId, CancellationToken ct = default)
    {
        var evento = await db.Eventos.AsNoTracking()
            .Include(e => e.Setores)
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        if (evento is null || (!evento.Publicado && evento.OrganizadorId != usuarioId))
        {
            throw new NaoEncontradoException($"Evento {id} não encontrado.");
        }

        return ParaDetalhe(evento);
    }

    // ---------- Área do organizador ----------

    public async Task<IReadOnlyList<EventoDetalheResponse>> ListarDoOrganizadorAsync(
        int organizadorId, CancellationToken ct = default)
    {
        var eventos = await db.Eventos.AsNoTracking()
            .Include(e => e.Setores)
            .Where(e => e.OrganizadorId == organizadorId)
            .OrderByDescending(e => e.DataInicio)
            .ToListAsync(ct);

        return eventos.Select(ParaDetalhe).ToList();
    }

    public async Task<EventoDetalheResponse> CriarAsync(int organizadorId, EventoRequest request, CancellationToken ct = default)
    {
        ValidarData(request.DataInicio);

        var evento = new Evento
        {
            OrganizadorId = organizadorId,
            CriadoEm = Agora,
            Publicado = false
        };
        Aplicar(evento, request);

        db.Eventos.Add(evento);
        await db.SaveChangesAsync(ct);

        return ParaDetalhe(evento);
    }

    public async Task<EventoDetalheResponse> AtualizarAsync(
        int organizadorId, int id, EventoRequest request, CancellationToken ct = default)
    {
        var evento = await CarregarDoOrganizadorAsync(organizadorId, id, ct);
        ValidarData(request.DataInicio);

        Aplicar(evento, request);
        await db.SaveChangesAsync(ct);

        return ParaDetalhe(evento);
    }

    public async Task ExcluirAsync(int organizadorId, int id, CancellationToken ct = default)
    {
        var evento = await CarregarDoOrganizadorAsync(organizadorId, id, ct);

        if (evento.Setores.Any(s => s.Vendidos > 0))
        {
            throw new ConflitoException("O evento já tem ingressos vendidos e não pode ser excluído.");
        }

        db.Eventos.Remove(evento);
        await db.SaveChangesAsync(ct);
    }

    public async Task<EventoDetalheResponse> PublicarAsync(int organizadorId, int id, CancellationToken ct = default)
    {
        var evento = await CarregarDoOrganizadorAsync(organizadorId, id, ct);

        if (evento.Setores.Count == 0)
        {
            throw new RegraDeNegocioException("Cadastre pelo menos um setor antes de publicar o evento.");
        }

        ValidarData(evento.DataInicio);

        evento.Publicado = true;
        await db.SaveChangesAsync(ct);

        return ParaDetalhe(evento);
    }

    public async Task<SetorResponse> AdicionarSetorAsync(
        int organizadorId, int eventoId, SetorRequest request, CancellationToken ct = default)
    {
        var evento = await CarregarDoOrganizadorAsync(organizadorId, eventoId, ct);

        if (evento.Setores.Any(s => string.Equals(s.Nome, request.Nome.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflitoException($"O evento já tem um setor chamado '{request.Nome.Trim()}'.");
        }

        var setor = new Setor
        {
            Nome = request.Nome.Trim(),
            Preco = request.Preco,
            Capacidade = request.Capacidade
        };
        evento.Setores.Add(setor);
        await db.SaveChangesAsync(ct);

        return ParaResponse(setor);
    }

    public async Task<SetorResponse> AtualizarSetorAsync(
        int organizadorId, int eventoId, int setorId, SetorRequest request, CancellationToken ct = default)
    {
        var evento = await CarregarDoOrganizadorAsync(organizadorId, eventoId, ct);
        var setor = ObterSetor(evento, setorId);

        if (request.Capacidade < setor.Vendidos)
        {
            throw new RegraDeNegocioException(
                $"A capacidade não pode ser menor que os {setor.Vendidos} ingressos já vendidos.");
        }

        setor.Nome = request.Nome.Trim();
        setor.Preco = request.Preco; // pedidos antigos guardam o preço pago, então não são afetados
        setor.Capacidade = request.Capacidade;
        await db.SaveChangesAsync(ct);

        return ParaResponse(setor);
    }

    public async Task RemoverSetorAsync(int organizadorId, int eventoId, int setorId, CancellationToken ct = default)
    {
        var evento = await CarregarDoOrganizadorAsync(organizadorId, eventoId, ct);
        var setor = ObterSetor(evento, setorId);

        if (setor.Vendidos > 0)
        {
            throw new ConflitoException("O setor já tem ingressos vendidos e não pode ser removido.");
        }

        if (evento.Publicado && evento.Setores.Count == 1)
        {
            throw new RegraDeNegocioException("Um evento publicado precisa ter pelo menos um setor.");
        }

        db.Setores.Remove(setor);
        await db.SaveChangesAsync(ct);
    }

    // ---------- Auxiliares ----------

    private async Task<Evento> CarregarDoOrganizadorAsync(int organizadorId, int id, CancellationToken ct)
    {
        var evento = await db.Eventos
            .Include(e => e.Setores)
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NaoEncontradoException($"Evento {id} não encontrado.");

        if (evento.OrganizadorId != organizadorId)
        {
            throw new AcessoNegadoException("Você só pode alterar os seus próprios eventos.");
        }

        return evento;
    }

    private static Setor ObterSetor(Evento evento, int setorId) =>
        evento.Setores.FirstOrDefault(s => s.Id == setorId)
        ?? throw new NaoEncontradoException($"Setor {setorId} não encontrado neste evento.");

    private void ValidarData(DateTime dataInicio)
    {
        if (ParaUtc(dataInicio) <= Agora)
        {
            throw new RegraDeNegocioException("A data do evento precisa estar no futuro.");
        }
    }

    private static void Aplicar(Evento evento, EventoRequest request)
    {
        evento.Titulo = request.Titulo.Trim();
        evento.Descricao = request.Descricao?.Trim() ?? string.Empty;
        evento.Local = request.Local.Trim();
        evento.Cidade = request.Cidade.Trim();
        evento.DataInicio = ParaUtc(request.DataInicio);
    }

    /// <summary>Datas sem fuso informado são tratadas como UTC.</summary>
    private static DateTime ParaUtc(DateTime data) => data.Kind switch
    {
        DateTimeKind.Utc => data,
        DateTimeKind.Local => data.ToUniversalTime(),
        _ => DateTime.SpecifyKind(data, DateTimeKind.Utc)
    };

    private static EventoDetalheResponse ParaDetalhe(Evento e) => new(
        e.Id,
        e.Titulo,
        e.Descricao,
        e.Local,
        e.Cidade,
        e.DataInicio,
        e.Publicado,
        e.Setores.OrderBy(s => s.Preco).Select(ParaResponse).ToList());

    private static SetorResponse ParaResponse(Setor s) => new(s.Id, s.Nome, s.Preco, s.Capacidade, s.Disponiveis);
}
