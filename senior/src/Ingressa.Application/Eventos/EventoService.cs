using Ingressa.Application.Abstracoes;
using Ingressa.Domain.Comum;
using Ingressa.Domain.Eventos;
using Ingressa.Domain.Usuarios;

namespace Ingressa.Application.Eventos;

public sealed class EventoService(
    IEventoRepositorio eventos,
    IUsuarioRepositorio usuarios,
    IConsultasDeEventos consultas,
    IUnidadeDeTrabalho unidade,
    IInvalidadorDeCache cache,
    TimeProvider relogio)
{
    private DateTime Agora => relogio.GetUtcNow().UtcDateTime;

    public Task<PaginaResponse<EventoResumoResponse>> ListarVitrineAsync(EventoFiltro filtro, CancellationToken ct) =>
        consultas.ListarVitrineAsync(filtro, Agora, ct);

    /// <summary>Rascunhos só são visíveis para o dono; para os demais respondem 404.</summary>
    public async Task<EventoDetalheResponse> ObterAsync(int id, int? usuarioId, CancellationToken ct)
    {
        var evento = await consultas.ObterDetalheAsync(id, ct);
        if (evento is null || (!evento.Publicado && evento.OrganizadorId != usuarioId))
        {
            throw new NaoEncontradoException($"Evento {id} não encontrado.");
        }

        return evento;
    }

    public async Task<IReadOnlyList<EventoDetalheResponse>> ListarDoOrganizadorAsync(int organizadorId, CancellationToken ct) =>
        (await eventos.ListarDoOrganizadorAsync(organizadorId, ct)).Select(MapeamentoDeEventos.ParaDetalhe).ToList();

    public async Task<EventoDetalheResponse> CriarAsync(int organizadorId, EventoRequest request, CancellationToken ct)
    {
        var organizador = await usuarios.ObterAsync(organizadorId, ct)
            ?? throw new AcessoNegadoException("Conta não encontrada.");

        if (!organizador.PodeVender)
        {
            throw new AcessoNegadoException(organizador.Status == StatusConta.AguardandoAprovacao
                ? "Sua conta de organizador ainda está aguardando aprovação."
                : "Somente organizadores podem criar eventos.");
        }

        var evento = Evento.Criar(organizadorId, ParaDados(request), Agora);
        eventos.Adicionar(evento);
        await unidade.SalvarAsync(ct);
        await cache.EventoAlteradoAsync(evento.Id, ct);
        return MapeamentoDeEventos.ParaDetalhe(evento);
    }

    public async Task<EventoDetalheResponse> AtualizarAsync(int organizadorId, int id, EventoRequest request, CancellationToken ct)
    {
        var evento = await CarregarDoOrganizadorAsync(organizadorId, id, ct);
        evento.Atualizar(ParaDados(request), Agora);
        await unidade.SalvarAsync(ct);
        await cache.EventoAlteradoAsync(id, ct);
        return MapeamentoDeEventos.ParaDetalhe(evento);
    }

    public async Task ExcluirAsync(int organizadorId, int id, CancellationToken ct)
    {
        var evento = await CarregarDoOrganizadorAsync(organizadorId, id, ct);
        evento.GarantirQuePodeSerExcluido();
        eventos.Remover(evento);
        await unidade.SalvarAsync(ct);
        await cache.EventoAlteradoAsync(id, ct);
    }

    public async Task<EventoDetalheResponse> PublicarAsync(int organizadorId, int id, CancellationToken ct)
    {
        var evento = await CarregarDoOrganizadorAsync(organizadorId, id, ct);
        evento.Publicar(Agora);
        await unidade.SalvarAsync(ct);
        await cache.EventoAlteradoAsync(id, ct);
        return MapeamentoDeEventos.ParaDetalhe(evento);
    }

    public async Task<SetorResponse> AdicionarSetorAsync(int organizadorId, int eventoId, SetorRequest request, CancellationToken ct)
    {
        var evento = await CarregarDoOrganizadorAsync(organizadorId, eventoId, ct);
        var setor = evento.AdicionarSetor(request.Nome, request.Preco, request.Capacidade);
        await unidade.SalvarAsync(ct);
        await cache.EventoAlteradoAsync(eventoId, ct);
        return MapeamentoDeEventos.ParaResponse(setor);
    }

    public async Task<SetorResponse> AtualizarSetorAsync(
        int organizadorId, int eventoId, int setorId, SetorRequest request, CancellationToken ct)
    {
        var evento = await CarregarDoOrganizadorAsync(organizadorId, eventoId, ct);
        var setor = evento.ObterSetor(setorId);
        setor.Atualizar(request.Nome, request.Preco, request.Capacidade);
        await unidade.SalvarAsync(ct);
        await cache.EventoAlteradoAsync(eventoId, ct);
        return MapeamentoDeEventos.ParaResponse(setor);
    }

    public async Task RemoverSetorAsync(int organizadorId, int eventoId, int setorId, CancellationToken ct)
    {
        var evento = await CarregarDoOrganizadorAsync(organizadorId, eventoId, ct);
        evento.RemoverSetor(setorId);
        await unidade.SalvarAsync(ct);
        await cache.EventoAlteradoAsync(eventoId, ct);
    }

    private async Task<Evento> CarregarDoOrganizadorAsync(int organizadorId, int id, CancellationToken ct)
    {
        var evento = await eventos.ObterAsync(id, ct) ?? throw new NaoEncontradoException($"Evento {id} não encontrado.");
        evento.GarantirQuePertenceA(organizadorId);
        return evento;
    }

    private static DadosDoEvento ParaDados(EventoRequest r) => new(r.Titulo, r.Descricao, r.Local, r.Cidade, r.DataInicio, r.FilaVirtual);
}
