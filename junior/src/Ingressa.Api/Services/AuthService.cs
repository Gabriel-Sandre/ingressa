using Ingressa.Api.Auth;
using Ingressa.Api.Data;
using Ingressa.Api.Dtos;
using Ingressa.Api.Erros;
using Ingressa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Ingressa.Api.Services;

public sealed class AuthService(
    IngressaDbContext db,
    SenhaHasher senhaHasher,
    TokenService tokenService,
    TimeProvider relogio)
{
    // Hash calculado uma vez para igualar o tempo de resposta quando o e-mail não existe.
    private static readonly Lazy<string> HashFicticio = new(() => new SenhaHasher().GerarHash("senha-ficticia"));

    public async Task<UsuarioResponse> RegistrarAsync(RegistrarRequest request, CancellationToken ct = default)
    {
        var email = NormalizarEmail(request.Email);

        if (await db.Usuarios.AnyAsync(u => u.Email == email, ct))
        {
            throw new ConflitoException("Já existe uma conta com este e-mail.");
        }

        var usuario = new Usuario
        {
            Nome = request.Nome.Trim(),
            Email = email,
            SenhaHash = senhaHasher.GerarHash(request.Senha),
            Perfil = request.Perfil,
            CriadoEm = relogio.GetUtcNow().UtcDateTime
        };

        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync(ct);

        return ParaResponse(usuario);
    }

    public async Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = NormalizarEmail(request.Email);
        var usuario = await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email, ct);

        if (usuario is null)
        {
            // Faz o mesmo trabalho de um login real para não revelar,
            // pelo tempo de resposta, quais e-mails estão cadastrados.
            senhaHasher.Verificar(request.Senha, HashFicticio.Value);
            throw new CredenciaisInvalidasException();
        }

        if (!senhaHasher.Verificar(request.Senha, usuario.SenhaHash))
        {
            throw new CredenciaisInvalidasException();
        }

        var (token, expiraEm) = tokenService.Gerar(usuario);
        return new TokenResponse(token, expiraEm, ParaResponse(usuario));
    }

    internal static string NormalizarEmail(string email) => email.Trim().ToLowerInvariant();

    private static UsuarioResponse ParaResponse(Usuario u) => new(u.Id, u.Nome, u.Email, u.Perfil);
}
