using Ingressa.Api.Dtos;
using Ingressa.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Ingressa.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    /// <summary>Cria uma conta de cliente ou de organizador.</summary>
    [HttpPost("registrar")]
    [ProducesResponseType<UsuarioResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UsuarioResponse>> Registrar(RegistrarRequest request, CancellationToken ct)
    {
        var usuario = await authService.RegistrarAsync(request, ct);
        return Created($"/api/usuarios/{usuario.Id}", usuario);
    }

    /// <summary>Troca e-mail e senha por um token JWT.</summary>
    [HttpPost("login")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken ct) =>
        Ok(await authService.LoginAsync(request, ct));
}
