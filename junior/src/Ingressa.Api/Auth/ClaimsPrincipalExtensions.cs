using System.Security.Claims;

namespace Ingressa.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Id do usuário autenticado, lido da claim "sub" do token.</summary>
    public static int ObterUsuarioId(this ClaimsPrincipal usuario)
    {
        var valor = usuario.FindFirstValue(TokenService.ClaimId);
        return int.TryParse(valor, out var id)
            ? id
            : throw new InvalidOperationException("Token sem a claim 'sub' válida.");
    }

    public static int? ObterUsuarioIdOuNulo(this ClaimsPrincipal usuario) =>
        int.TryParse(usuario.FindFirstValue(TokenService.ClaimId), out var id) ? id : null;
}
