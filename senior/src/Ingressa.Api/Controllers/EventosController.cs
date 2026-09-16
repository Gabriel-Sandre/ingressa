using Ingressa.Api.Auth;
using Ingressa.Application.Eventos;
using Ingressa.Domain.Usuarios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingressa.Api.Controllers;

[ApiController]
[Route("api/eventos")]
[Produces("application/json")]
public sealed class EventosController(EventoService eventoService) : ControllerBase
{
    private const string Organizador = nameof(PerfilUsuario.Organizador);

    /// <summary>Vitrine com busca, filtro por cidade, ordenação (Data, Titulo, Preco) e paginação.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<PaginaResponse<EventoResumoResponse>>> Listar([FromQuery] EventoFiltro filtro, CancellationToken ct) =>
        Ok(await eventoService.ListarVitrineAsync(filtro, ct));

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    [ProducesResponseType<EventoDetalheResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventoDetalheResponse>> Obter(int id, CancellationToken ct) =>
        Ok(await eventoService.ObterAsync(id, User.ObterUsuarioIdOuNulo(), ct));

    [HttpGet("meus")]
    [Authorize(Roles = Organizador)]
    public async Task<ActionResult<IReadOnlyList<EventoDetalheResponse>>> ListarMeus(CancellationToken ct) =>
        Ok(await eventoService.ListarDoOrganizadorAsync(User.ObterUsuarioId(), ct));

    [HttpPost]
    [Authorize(Roles = Organizador)]
    [ProducesResponseType<EventoDetalheResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<EventoDetalheResponse>> Criar(EventoRequest request, CancellationToken ct)
    {
        var evento = await eventoService.CriarAsync(User.ObterUsuarioId(), request, ct);
        return CreatedAtAction(nameof(Obter), new { id = evento.Id }, evento);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Organizador)]
    public async Task<ActionResult<EventoDetalheResponse>> Atualizar(int id, EventoRequest request, CancellationToken ct) =>
        Ok(await eventoService.AtualizarAsync(User.ObterUsuarioId(), id, request, ct));

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Organizador)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Excluir(int id, CancellationToken ct)
    {
        await eventoService.ExcluirAsync(User.ObterUsuarioId(), id, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/publicar")]
    [Authorize(Roles = Organizador)]
    public async Task<ActionResult<EventoDetalheResponse>> Publicar(int id, CancellationToken ct) =>
        Ok(await eventoService.PublicarAsync(User.ObterUsuarioId(), id, ct));

    [HttpPost("{id:int}/setores")]
    [Authorize(Roles = Organizador)]
    [ProducesResponseType<SetorResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<SetorResponse>> AdicionarSetor(int id, SetorRequest request, CancellationToken ct)
    {
        var setor = await eventoService.AdicionarSetorAsync(User.ObterUsuarioId(), id, request, ct);
        return CreatedAtAction(nameof(Obter), new { id }, setor);
    }

    [HttpPut("{id:int}/setores/{setorId:int}")]
    [Authorize(Roles = Organizador)]
    public async Task<ActionResult<SetorResponse>> AtualizarSetor(int id, int setorId, SetorRequest request, CancellationToken ct) =>
        Ok(await eventoService.AtualizarSetorAsync(User.ObterUsuarioId(), id, setorId, request, ct));

    [HttpDelete("{id:int}/setores/{setorId:int}")]
    [Authorize(Roles = Organizador)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoverSetor(int id, int setorId, CancellationToken ct)
    {
        await eventoService.RemoverSetorAsync(User.ObterUsuarioId(), id, setorId, ct);
        return NoContent();
    }
}
