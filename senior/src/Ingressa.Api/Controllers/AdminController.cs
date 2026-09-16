using Ingressa.Api.Auth;
using Ingressa.Application.Admin;
using Ingressa.Application.Auth;
using Ingressa.Domain.Usuarios;
using Ingressa.Infrastructure.Outbox;
using Ingressa.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingressa.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Produces("application/json")]
[Authorize(Roles = nameof(PerfilUsuario.Admin))]
public sealed class AdminController(AdminService adminService, ILogger<AdminController> logger) : ControllerBase
{
    [HttpGet("organizadores/pendentes")]
    public async Task<ActionResult<IReadOnlyList<UsuarioResponse>>> ListarPendentes(CancellationToken ct) =>
        Ok(await adminService.ListarOrganizadoresPendentesAsync(ct));

    [HttpPost("organizadores/{id:int}/aprovar")]
    public async Task<ActionResult<UsuarioResponse>> Aprovar(int id, CancellationToken ct) =>
        Ok(await adminService.AprovarOrganizadorAsync(User.ObterUsuarioId(), id, ct));

    /// <summary>Mensagens que esgotaram as tentativas de publicação.</summary>
    [HttpGet("outbox/falhas")]
    public async Task<ActionResult<IReadOnlyList<MensagemComFalha>>> ListarFalhas(
        [FromServices] IngressaDbContext db, CancellationToken ct) =>
        Ok(await db.MensagensOutbox.AsNoTracking()
            .Where(m => m.PublicadaEm == null && m.Tentativas >= MensagemOutbox.MaximoTentativas)
            .OrderBy(m => m.Id)
            .Take(100)
            .Select(m => new MensagemComFalha(m.Id, m.Tipo, m.OcorridoEm, m.Tentativas, m.UltimoErro))
            .ToListAsync(ct));

    /// <summary>Devolve a mensagem para a fila de publicação (depois de corrigida a causa da falha).</summary>
    [HttpPost("outbox/{id:long}/reprocessar")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reprocessar(long id, [FromServices] IngressaDbContext db, CancellationToken ct)
    {
        var alteradas = await db.MensagensOutbox
            .Where(m => m.Id == id && m.PublicadaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Tentativas, 0).SetProperty(m => m.UltimoErro, (string?)null), ct);

        if (alteradas == 0)
        {
            return NotFound();
        }

        logger.LogWarning("Mensagem {MensagemId} da outbox reenviada pelo admin {AdminId}", id, User.ObterUsuarioId());
        return NoContent();
    }
}

public sealed record MensagemComFalha(long Id, string Tipo, DateTime OcorridoEm, int Tentativas, string? UltimoErro);
