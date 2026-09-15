using Ingressa.Api.Auth;
using Ingressa.Api.Dtos;
using Ingressa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingressa.Api.Controllers;

[ApiController]
[Route("api/pedidos")]
[Produces("application/json")]
[Authorize(Roles = nameof(Models.PerfilUsuario.Cliente))]
public sealed class PedidosController(PedidoService pedidoService) : ControllerBase
{
    /// <summary>Compra ingressos de um ou mais setores de um evento.</summary>
    [HttpPost]
    [ProducesResponseType<PedidoResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PedidoResponse>> Criar(CriarPedidoRequest request, CancellationToken ct)
    {
        var pedido = await pedidoService.CriarAsync(User.ObterUsuarioId(), request, ct);
        return CreatedAtAction(nameof(Obter), new { id = pedido.Id }, pedido);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PedidoResponse>>> Listar(CancellationToken ct) =>
        Ok(await pedidoService.ListarDoUsuarioAsync(User.ObterUsuarioId(), ct));

    [HttpGet("{id:int}")]
    [ProducesResponseType<PedidoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PedidoResponse>> Obter(int id, CancellationToken ct) =>
        Ok(await pedidoService.ObterAsync(User.ObterUsuarioId(), id, ct));

    /// <summary>Cancela o pedido e devolve os ingressos ao estoque (até 24 h antes do evento).</summary>
    [HttpPost("{id:int}/cancelar")]
    public async Task<ActionResult<PedidoResponse>> Cancelar(int id, CancellationToken ct) =>
        Ok(await pedidoService.CancelarAsync(User.ObterUsuarioId(), id, ct));
}
