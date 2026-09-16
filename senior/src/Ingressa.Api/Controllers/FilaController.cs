using Ingressa.Api.Auth;
using Ingressa.Api.Infra;
using Ingressa.Application.Fila;
using Ingressa.Domain.Usuarios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingressa.Api.Controllers;

/// <summary>Sala de espera dos eventos de alta demanda.</summary>
[ApiController]
[Route("api/eventos/{eventoId:int}/fila")]
[Produces("application/json")]
[Authorize(Roles = nameof(PerfilUsuario.Cliente))]
public sealed class FilaController(FilaVirtualService fila) : ControllerBase
{
    /// <summary>Entra na fila (ou devolve a posição atual, se já estiver nela).</summary>
    [HttpPost]
    [LimiteDistribuido("fila")]
    [ProducesResponseType<FilaResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<FilaResponse>> Entrar(int eventoId, CancellationToken ct) =>
        Ok(await fila.EntrarAsync(eventoId, User.ObterUsuarioId(), ct));

    /// <summary>
    /// Consulta a posição. Quando a situação for <c>Liberado</c>, o campo <c>passe</c>
    /// deve ser enviado no cabeçalho <c>X-Passe-Fila</c> da reserva.
    /// </summary>
    [HttpGet]
    [LimiteDistribuido("fila-consulta")]
    public async Task<ActionResult<FilaResponse>> Consultar(int eventoId, CancellationToken ct) =>
        Ok(await fila.ConsultarAsync(eventoId, User.ObterUsuarioId(), ct));
}
