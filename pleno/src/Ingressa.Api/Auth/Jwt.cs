using System.Security.Claims;
using System.Text;
using Ingressa.Application.Abstracoes;
using Ingressa.Domain.Usuarios;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ingressa.Api.Auth;

public sealed class OpcoesJwt
{
    public const string Secao = "Jwt";

    public string Emissor { get; set; } = "Ingressa";
    public string Audiencia { get; set; } = "Ingressa.Clientes";
    public string Chave { get; set; } = string.Empty;

    /// <summary>Curto de propósito: a sessão é mantida pelo refresh token.</summary>
    public int ExpiracaoMinutos { get; set; } = 15;

    public void Validar()
    {
        if (Encoding.UTF8.GetByteCount(Chave) < 32)
        {
            throw new InvalidOperationException(
                "A chave JWT (Jwt:Chave) precisa ter pelo menos 32 bytes. Configure com user-secrets ou a variável Jwt__Chave.");
        }

        if (ExpiracaoMinutos is < 1 or > 60)
        {
            throw new InvalidOperationException("Jwt:ExpiracaoMinutos deve estar entre 1 e 60.");
        }
    }

    public TokenValidationParameters ParametrosDeValidacao() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Emissor,
        ValidateAudience = true,
        ValidAudience = Audiencia,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Chave)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = Claims.Nome,
        RoleClaimType = Claims.Perfil
    };
}

public static class Claims
{
    public const string Id = "sub";
    public const string Nome = "name";
    public const string Email = "email";
    public const string Perfil = "role";

    public static int ObterUsuarioId(this ClaimsPrincipal usuario) =>
        int.TryParse(usuario.FindFirstValue(Id), out var id)
            ? id
            : throw new InvalidOperationException("Token sem a claim 'sub'.");

    public static int? ObterUsuarioIdOuNulo(this ClaimsPrincipal usuario) =>
        int.TryParse(usuario.FindFirstValue(Id), out var id) ? id : null;
}

public sealed class GeradorDeAccessTokenJwt(OpcoesJwt opcoes, TimeProvider relogio) : IGeradorDeAccessToken
{
    public (string Token, DateTime ExpiraEm) Gerar(Usuario usuario)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var expiraEm = agora.AddMinutes(opcoes.ExpiracaoMinutos);

        var descritor = new SecurityTokenDescriptor
        {
            Issuer = opcoes.Emissor,
            Audience = opcoes.Audiencia,
            IssuedAt = agora,
            NotBefore = agora,
            Expires = expiraEm,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opcoes.Chave)), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [Claims.Id] = usuario.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [Claims.Nome] = usuario.Nome,
                [Claims.Email] = usuario.Email,
                [Claims.Perfil] = usuario.Perfil.ToString()
            }
        };

        return (new JsonWebTokenHandler().CreateToken(descritor), expiraEm);
    }
}
