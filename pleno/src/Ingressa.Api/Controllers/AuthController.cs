using Ingressa.Api.Auth;
using Ingressa.Api.Infra;
using Ingressa.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Ingressa.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    /// <summary>Nome do cookie do refresh token. HttpOnly: o JavaScript da página não consegue lê-lo.</summary>
    public const string CookieDeSessao = "ingressa_sessao";

    [HttpPost("registrar")]
    [EnableRateLimiting(LimiteDeRequisicoes.Autenticacao)]
    [ProducesResponseType<UsuarioResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UsuarioResponse>> Registrar(RegistrarRequest request, CancellationToken ct)
    {
        var usuario = await authService.RegistrarAsync(request, ct);
        return CreatedAtAction(nameof(Eu), null, usuario);
    }

    /// <summary>Retorna o access token (15 min) e grava o refresh token (7 dias) em cookie.</summary>
    [HttpPost("login")]
    [EnableRateLimiting(LimiteDeRequisicoes.Autenticacao)]
    [ProducesResponseType<SessaoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<SessaoResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var sessao = await authService.LoginAsync(request, ct);
        GravarCookie(sessao);
        return Ok(sessao.Sessao);
    }

    /// <summary>Emite um novo access token a partir do cookie de sessão (com rotação do refresh token).</summary>
    [HttpPost("renovar")]
    [ProducesResponseType<SessaoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SessaoResponse>> Renovar(CancellationToken ct)
    {
        try
        {
            var sessao = await authService.RenovarAsync(Request.Cookies[CookieDeSessao], ct);
            GravarCookie(sessao);
            return Ok(sessao.Sessao);
        }
        catch
        {
            ApagarCookie();
            throw;
        }
    }

    [HttpPost("sair")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Sair(CancellationToken ct)
    {
        await authService.SairAsync(Request.Cookies[CookieDeSessao], ct);
        ApagarCookie();
        return NoContent();
    }

    [HttpGet("eu")]
    [Authorize]
    [ProducesResponseType<UsuarioResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UsuarioResponse>> Eu(CancellationToken ct) =>
        Ok(await authService.ObterAsync(User.ObterUsuarioId(), ct));

    private void GravarCookie(SessaoEmitida sessao) =>
        Response.Cookies.Append(CookieDeSessao, sessao.RefreshToken, OpcoesDoCookie(sessao.RefreshExpiraEm));

    private void ApagarCookie() => Response.Cookies.Delete(CookieDeSessao, OpcoesDoCookie(null));

    private static CookieOptions OpcoesDoCookie(DateTime? expiraEm) => new()
    {
        HttpOnly = true,
        Secure = true,                  // navegadores aceitam Secure em http://localhost
        SameSite = SameSiteMode.Strict, // não é enviado em requisições vindas de outros sites (proteção CSRF)
        Path = "/api/auth",             // só vai para os endpoints de sessão
        Expires = expiraEm,
        IsEssential = true
    };
}
