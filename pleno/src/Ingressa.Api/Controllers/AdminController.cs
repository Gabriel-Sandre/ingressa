using Ingressa.Api.Auth;
using Ingressa.Application.Admin;
using Ingressa.Application.Auth;
using Ingressa.Domain.Usuarios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingressa.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Produces("application/json")]
[Authorize(Roles = nameof(PerfilUsuario.Admin))]
public sealed class AdminController(AdminService adminService) : ControllerBase
{
    [HttpGet("organizadores/pendentes")]
    public async Task<ActionResult<IReadOnlyList<UsuarioResponse>>> ListarPendentes(CancellationToken ct) =>
        Ok(await adminService.ListarOrganizadoresPendentesAsync(ct));

    [HttpPost("organizadores/{id:int}/aprovar")]
    public async Task<ActionResult<UsuarioResponse>> Aprovar(int id, CancellationToken ct) =>
        Ok(await adminService.AprovarOrganizadorAsync(User.ObterUsuarioId(), id, ct));
}
