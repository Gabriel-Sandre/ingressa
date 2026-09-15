using System.Text;
using Ingressa.Api.Models;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ingressa.Api.Auth;

public sealed class TokenService(JwtOptions opcoes, TimeProvider relogio)
{
    public const string ClaimId = "sub";
    public const string ClaimNome = "name";
    public const string ClaimEmail = "email";
    public const string ClaimPerfil = "role";

    public (string Token, DateTime ExpiraEm) Gerar(Usuario usuario)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var expiraEm = agora.AddMinutes(opcoes.ExpiracaoMinutos);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = opcoes.Emissor,
            Audience = opcoes.Audiencia,
            IssuedAt = agora,
            NotBefore = agora,
            Expires = expiraEm,
            SigningCredentials = new SigningCredentials(CriarChave(opcoes), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [ClaimId] = usuario.Id.ToString(),
                [ClaimNome] = usuario.Nome,
                [ClaimEmail] = usuario.Email,
                [ClaimPerfil] = usuario.Perfil.ToString()
            }
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expiraEm);
    }

    public static SymmetricSecurityKey CriarChave(JwtOptions opcoes) =>
        new(Encoding.UTF8.GetBytes(opcoes.Chave));

    public static TokenValidationParameters CriarParametrosDeValidacao(JwtOptions opcoes) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = opcoes.Emissor,
        ValidateAudience = true,
        ValidAudience = opcoes.Audiencia,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = CriarChave(opcoes),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = ClaimNome,
        RoleClaimType = ClaimPerfil
    };
}
