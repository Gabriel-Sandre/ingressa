using Ingressa.Application.Abstracoes;
using Ingressa.Domain.Comum;
using Ingressa.Domain.Usuarios;
using Microsoft.Extensions.Logging;

namespace Ingressa.Application.Auth;

public sealed class AuthService(
    IUsuarioRepositorio usuarios,
    IRefreshTokenRepositorio refreshTokens,
    IUnidadeDeTrabalho unidade,
    ISenhaHasher senhaHasher,
    IGeradorDeAccessToken accessTokens,
    IGeradorDeCodigos codigos,
    TimeProvider relogio,
    ILogger<AuthService> logger)
{
    public static readonly TimeSpan ValidadeDoRefreshToken = TimeSpan.FromDays(7);

    private DateTime Agora => relogio.GetUtcNow().UtcDateTime;

    public async Task<UsuarioResponse> RegistrarAsync(RegistrarRequest request, CancellationToken ct)
    {
        var email = Usuario.NormalizarEmail(request.Email);
        if (await usuarios.EmailExisteAsync(email, ct))
        {
            throw new ConflitoException("Já existe uma conta com este e-mail.");
        }

        var usuario = Usuario.Registrar(request.Nome, email, senhaHasher.GerarHash(request.Senha), request.Perfil, Agora);
        usuarios.Adicionar(usuario);
        await unidade.SalvarAsync(ct);

        logger.LogInformation("Conta {UsuarioId} criada com perfil {Perfil}", usuario.Id, usuario.Perfil);
        return ParaResponse(usuario);
    }

    public async Task<SessaoEmitida> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var agora = Agora;
        var usuario = await usuarios.ObterPorEmailAsync(Usuario.NormalizarEmail(request.Email), ct);

        if (usuario is null)
        {
            // Mesmo custo de um login real: o tempo de resposta não revela se o e-mail existe.
            senhaHasher.Verificar(request.Senha, HashFicticio.Value(senhaHasher));
            throw new NaoAutenticadoException();
        }

        if (usuario.EstaBloqueado(agora))
        {
            logger.LogWarning("Tentativa de login em conta bloqueada {UsuarioId}", usuario.Id);
            throw new NaoAutenticadoException("Conta temporariamente bloqueada por excesso de tentativas. Tente mais tarde.");
        }

        if (!senhaHasher.Verificar(request.Senha, usuario.SenhaHash))
        {
            usuario.RegistrarFalhaDeLogin(agora);
            await unidade.SalvarAsync(ct);
            logger.LogWarning("Senha incorreta para a conta {UsuarioId}", usuario.Id);
            throw new NaoAutenticadoException();
        }

        usuario.RegistrarLoginComSucesso();
        var sessao = EmitirSessao(usuario, Guid.NewGuid(), agora);
        await unidade.SalvarAsync(ct);
        return sessao;
    }

    /// <summary>
    /// Troca um refresh token válido por uma nova sessão (rotação). Se o token já
    /// tiver sido usado, toda a família é revogada: alguém pode ter copiado o token.
    /// </summary>
    public async Task<SessaoEmitida> RenovarAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new NaoAutenticadoException("Sessão expirada.");
        }

        var agora = Agora;
        var atual = await refreshTokens.ObterPorHashAsync(codigos.Hash(refreshToken), ct)
            ?? throw new NaoAutenticadoException("Sessão expirada.");

        if (atual.FoiUsado && !atual.DentroDaJanelaDeTolerancia(agora))
        {
            await refreshTokens.RevogarFamiliaAsync(atual.Familia, agora, ct);
            await unidade.SalvarAsync(ct);
            logger.LogWarning(
                "Reutilização de refresh token detectada para o usuário {UsuarioId}; família {Familia} revogada",
                atual.UsuarioId, atual.Familia);
            throw new NaoAutenticadoException("Sessão expirada.");
        }

        if (!atual.FoiUsado && !atual.EstaAtivo(agora))
        {
            throw new NaoAutenticadoException("Sessão expirada.");
        }

        var usuario = await usuarios.ObterAsync(atual.UsuarioId, ct)
            ?? throw new NaoAutenticadoException("Sessão expirada.");

        atual.MarcarComoUsado(agora);
        var sessao = EmitirSessao(usuario, atual.Familia, agora);
        await unidade.SalvarAsync(ct);
        return sessao;
    }

    public async Task SairAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var atual = await refreshTokens.ObterPorHashAsync(codigos.Hash(refreshToken), ct);
        if (atual is not null)
        {
            await refreshTokens.RevogarFamiliaAsync(atual.Familia, Agora, ct);
            await unidade.SalvarAsync(ct);
        }
    }

    public async Task<UsuarioResponse> ObterAsync(int usuarioId, CancellationToken ct)
    {
        var usuario = await usuarios.ObterAsync(usuarioId, ct)
            ?? throw new NaoEncontradoException("Usuário não encontrado.");
        return ParaResponse(usuario);
    }

    private SessaoEmitida EmitirSessao(Usuario usuario, Guid familia, DateTime agora)
    {
        var (accessToken, expiraEm) = accessTokens.Gerar(usuario);
        var refresh = codigos.GerarTokenSeguro(32);
        refreshTokens.Adicionar(RefreshToken.Emitir(usuario.Id, codigos.Hash(refresh), familia, agora, ValidadeDoRefreshToken));

        return new SessaoEmitida(
            new SessaoResponse(accessToken, expiraEm, ParaResponse(usuario)),
            refresh,
            agora.Add(ValidadeDoRefreshToken));
    }

    internal static UsuarioResponse ParaResponse(Usuario u) => new(u.Id, u.Nome, u.Email, u.Perfil, u.Status);

    private static class HashFicticio
    {
        private static string? _valor;
        public static string Value(ISenhaHasher hasher) => _valor ??= hasher.GerarHash("senha-ficticia-para-igualar-tempo");
    }
}
