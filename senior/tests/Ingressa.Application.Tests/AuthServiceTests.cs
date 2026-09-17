using Ingressa.Application.Auth;
using Ingressa.Domain.Comum;
using Ingressa.Domain.Usuarios;

namespace Ingressa.Application.Tests;

public class AuthServiceTests
{
    private readonly Cenario _c = new();

    private Task<UsuarioResponse> RegistrarAsync(PerfilUsuario perfil = PerfilUsuario.Cliente) =>
        _c.Auth.RegistrarAsync(new RegistrarRequest("Ana Souza", "ana@teste.com", "Senha@123", perfil), default);

    [Fact]
    public async Task Registrar_EmailDuplicadoGeraConflito()
    {
        await RegistrarAsync();

        await Assert.ThrowsAsync<ConflitoException>(() =>
            _c.Auth.RegistrarAsync(new RegistrarRequest("Outra", "ANA@teste.com", "Senha@123"), default));
    }

    [Fact]
    public async Task Registrar_OrganizadorFicaPendente()
    {
        var usuario = await RegistrarAsync(PerfilUsuario.Organizador);

        Assert.Equal(StatusConta.AguardandoAprovacao, usuario.Status);
        Assert.Single(await _c.Admin.ListarOrganizadoresPendentesAsync(default));
    }

    [Fact]
    public async Task Login_GuardaApenasOHashDoRefreshToken()
    {
        await RegistrarAsync();

        var sessao = await _c.Auth.LoginAsync(new LoginRequest("ana@teste.com", "Senha@123"), default);

        var guardado = Assert.Single(_c.Banco.Tokens);
        Assert.NotEqual(sessao.RefreshToken, guardado.TokenHash);
        Assert.Equal("sha:" + sessao.RefreshToken, guardado.TokenHash);
        Assert.Equal(_c.Relogio.Agora + AuthService.ValidadeDoRefreshToken, sessao.RefreshExpiraEm);
    }

    [Fact]
    public async Task Login_CincoSenhasErradasBloqueiamAConta()
    {
        await RegistrarAsync();
        for (var i = 0; i < Usuario.MaximoTentativasDeLogin; i++)
        {
            await Assert.ThrowsAsync<NaoAutenticadoException>(() =>
                _c.Auth.LoginAsync(new LoginRequest("ana@teste.com", "errada"), default));
        }

        // Mesmo com a senha certa, a conta está bloqueada.
        var erro = await Assert.ThrowsAsync<NaoAutenticadoException>(() =>
            _c.Auth.LoginAsync(new LoginRequest("ana@teste.com", "Senha@123"), default));
        Assert.Contains("bloqueada", erro.Message);

        _c.Relogio.Avancar(Usuario.DuracaoDoBloqueio);
        await _c.Auth.LoginAsync(new LoginRequest("ana@teste.com", "Senha@123"), default);
    }

    [Fact]
    public async Task Login_EmailInexistenteTemAMesmaMensagem()
    {
        var erro = await Assert.ThrowsAsync<NaoAutenticadoException>(() =>
            _c.Auth.LoginAsync(new LoginRequest("ninguem@teste.com", "x"), default));

        Assert.Equal("E-mail ou senha inválidos.", erro.Message);
    }

    [Fact]
    public async Task Renovar_TrocaOTokenEMantemAFamilia()
    {
        await RegistrarAsync();
        var login = await _c.Auth.LoginAsync(new LoginRequest("ana@teste.com", "Senha@123"), default);

        var renovada = await _c.Auth.RenovarAsync(login.RefreshToken, default);

        Assert.NotEqual(login.RefreshToken, renovada.RefreshToken);
        Assert.Equal(2, _c.Banco.Tokens.Count);
        Assert.Single(_c.Banco.Tokens.Select(t => t.Familia).Distinct());
        Assert.True(_c.Banco.Tokens[0].FoiUsado);
    }

    [Fact]
    public async Task Renovar_ReusoDeTokenRevogaTodaAFamilia()
    {
        await RegistrarAsync();
        var login = await _c.Auth.LoginAsync(new LoginRequest("ana@teste.com", "Senha@123"), default);
        var renovada = await _c.Auth.RenovarAsync(login.RefreshToken, default);
        _c.Relogio.Avancar(TimeSpan.FromMinutes(1)); // fora da janela de tolerância

        // Um atacante com uma cópia do token antigo tenta usá-lo.
        await Assert.ThrowsAsync<NaoAutenticadoException>(() => _c.Auth.RenovarAsync(login.RefreshToken, default));

        // A sessão legítima também cai: o usuário precisa entrar de novo.
        await Assert.ThrowsAsync<NaoAutenticadoException>(() => _c.Auth.RenovarAsync(renovada.RefreshToken, default));
    }

    [Fact]
    public async Task Renovar_TokenExpiradoOuInexistenteFalha()
    {
        await RegistrarAsync();
        var login = await _c.Auth.LoginAsync(new LoginRequest("ana@teste.com", "Senha@123"), default);

        await Assert.ThrowsAsync<NaoAutenticadoException>(() => _c.Auth.RenovarAsync("inventado", default));
        await Assert.ThrowsAsync<NaoAutenticadoException>(() => _c.Auth.RenovarAsync(null, default));

        _c.Relogio.Avancar(AuthService.ValidadeDoRefreshToken);
        await Assert.ThrowsAsync<NaoAutenticadoException>(() => _c.Auth.RenovarAsync(login.RefreshToken, default));
    }

    [Fact]
    public async Task Sair_RevogaASessao()
    {
        await RegistrarAsync();
        var login = await _c.Auth.LoginAsync(new LoginRequest("ana@teste.com", "Senha@123"), default);

        await _c.Auth.SairAsync(login.RefreshToken, default);

        await Assert.ThrowsAsync<NaoAutenticadoException>(() => _c.Auth.RenovarAsync(login.RefreshToken, default));
    }
}

public class RenovacaoSimultaneaTests
{
    private readonly Cenario _c = new();

    [Fact]
    public async Task DuasAbasRenovandoJuntas_NaoDerrubamASessao()
    {
        await _c.Auth.RegistrarAsync(new RegistrarRequest("Ana Souza", "ana@teste.com", "Senha@123"), default);
        var login = await _c.Auth.LoginAsync(new LoginRequest("ana@teste.com", "Senha@123"), default);

        var abaA = await _c.Auth.RenovarAsync(login.RefreshToken, default);
        _c.Relogio.Avancar(TimeSpan.FromSeconds(3));
        var abaB = await _c.Auth.RenovarAsync(login.RefreshToken, default);

        // As duas sessões continuam válidas.
        await _c.Auth.RenovarAsync(abaA.RefreshToken, default);
        await _c.Auth.RenovarAsync(abaB.RefreshToken, default);
    }

    [Fact]
    public async Task ReusoForaDaJanela_AindaRevogaAFamilia()
    {
        await _c.Auth.RegistrarAsync(new RegistrarRequest("Ana Souza", "ana@teste.com", "Senha@123"), default);
        var login = await _c.Auth.LoginAsync(new LoginRequest("ana@teste.com", "Senha@123"), default);
        var renovada = await _c.Auth.RenovarAsync(login.RefreshToken, default);

        _c.Relogio.Avancar(Domain.Usuarios.RefreshToken.JanelaDeTolerancia + TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<NaoAutenticadoException>(() => _c.Auth.RenovarAsync(login.RefreshToken, default));
        await Assert.ThrowsAsync<NaoAutenticadoException>(() => _c.Auth.RenovarAsync(renovada.RefreshToken, default));
    }
}
