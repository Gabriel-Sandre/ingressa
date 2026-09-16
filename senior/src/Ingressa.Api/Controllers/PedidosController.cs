using Ingressa.Api.Auth;
using Ingressa.Application.Pedidos;
using Ingressa.Domain.Usuarios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingressa.Api.Controllers;

[ApiController]
[Route("api/pedidos")]
[Produces("application/json")]
[Authorize(Roles = nameof(PerfilUsuario.Cliente))]
public sealed class PedidosController(PedidoService pedidoService) : ControllerBase
{
    /// <summary>Reserva os ingressos por 10 minutos. O pedido fica aguardando pagamento.</summary>
    [HttpPost]
    [ProducesResponseType<PedidoResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PedidoResponse>> Reservar(CriarPedidoRequest request, CancellationToken ct)
    {
        var pedido = await pedidoService.ReservarAsync(User.ObterUsuarioId(), request, ct);
        return CreatedAtAction(nameof(Obter), new { id = pedido.Id }, pedido);
    }

    /// <summary>
    /// Paga a reserva com um token do gateway simulado:
    /// <c>tok_aprovado</c>, <c>tok_recusado</c> ou <c>tok_sem_saldo</c>.
    /// Os ingressos são emitidos em segundo plano logo depois.
    /// </summary>
    [HttpPost("{id:int}/pagamento")]
    [ProducesResponseType<PedidoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PedidoResponse>> Pagar(int id, PagamentoRequest request, CancellationToken ct) =>
        Ok(await pedidoService.PagarAsync(User.ObterUsuarioId(), id, request, ct));

    [HttpPost("{id:int}/cancelar")]
    public async Task<ActionResult<PedidoResponse>> Cancelar(int id, CancellationToken ct) =>
        Ok(await pedidoService.CancelarAsync(User.ObterUsuarioId(), id, ct));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PedidoResponse>>> Listar(CancellationToken ct) =>
        Ok(await pedidoService.ListarAsync(User.ObterUsuarioId(), ct));

    [HttpGet("{id:int}")]
    [ProducesResponseType<PedidoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PedidoResponse>> Obter(int id, CancellationToken ct) =>
        Ok(await pedidoService.ObterAsync(User.ObterUsuarioId(), id, ct));
}
