using Ingressa.Api.Auth;
using Ingressa.Api.Dtos;
using Ingressa.Api.Erros;
using Ingressa.Api.Models;
using Ingressa.Api.Services;
using Ingressa.Api.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Ingressa.Api.Tests.Services;

public sealed class AuthServiceTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly RelogioFixo _relogio = new();
    private readonly SenhaHasher _hasher = new(iteracoes: 1_000);
    private readonly JwtOptions _jwt = new() { Chave = "chave-de-teste-com-mais-de-32-bytes!!" };

    private AuthService CriarServico(Ingressa.Api.Data.IngressaDbContext db) =>
        new(db, _hasher, new TokenService(_jwt, _relogio), _relogio);

    [Fact]
    public async Task Registrar_DeveSalvarEmailNormalizadoESenhaComHash()
    {
        await using (var db = _banco.NovoContexto())
        {
            await CriarServico(db).RegistrarAsync(
                new RegistrarRequest("  Maria Silva ", "  Maria@Email.COM ", "Senha@123"));
        }

        await using var verificacao = _banco.NovoContexto();
        var usuario = await verificacao.Usuarios.SingleAsync();
        Assert.Equal("Maria Silva", usuario.Nome);
        Assert.Equal("maria@email.com", usuario.Email);
        Assert.Equal(PerfilUsuario.Cliente, usuario.Perfil);
        Assert.NotEqual("Senha@123", usuario.SenhaHash);
        Assert.True(_hasher.Verificar("Senha@123", usuario.SenhaHash));
    }

    [Fact]
    public async Task Registrar_DeveRecusarEmailJaCadastradoIgnorandoMaiusculas()
    {
        await _banco.CriarUsuarioAsync(PerfilUsuario.Cliente, "joao@email.com");
        await using var db = _banco.NovoContexto();

        await Assert.ThrowsAsync<ConflitoException>(() =>
            CriarServico(db).RegistrarAsync(new RegistrarRequest("João", "JOAO@email.com", "Senha@123")));
    }

    [Fact]
    public async Task Login_DeveRetornarTokenComAsClaimsDoUsuario()
    {
        await using (var db = _banco.NovoContexto())
        {
            await CriarServico(db).RegistrarAsync(
                new RegistrarRequest("Ana", "ana@email.com", "Senha@123", PerfilUsuario.Organizador));
        }

        await using var contexto = _banco.NovoContexto();
        var resposta = await CriarServico(contexto).LoginAsync(new LoginRequest("ANA@email.com", "Senha@123"));

        Assert.Equal(RelogioFixo.Agora.AddMinutes(_jwt.ExpiracaoMinutos), resposta.ExpiraEm);
        Assert.Equal(PerfilUsuario.Organizador, resposta.Usuario.Perfil);

        var token = new JsonWebTokenHandler().ReadJsonWebToken(resposta.AccessToken);
        Assert.Equal(resposta.Usuario.Id.ToString(), token.Claims.Single(c => c.Type == "sub").Value);
        Assert.Equal("Organizador", token.Claims.Single(c => c.Type == "role").Value);
        Assert.Equal(_jwt.Emissor, token.Issuer);
    }

    [Fact]
    public async Task Login_TokenGeradoDeveSerAceitoPelaValidacaoDaApi()
    {
        await using (var db = _banco.NovoContexto())
        {
            await CriarServico(db).RegistrarAsync(new RegistrarRequest("Ana", "ana@email.com", "Senha@123"));
        }

        await using var contexto = _banco.NovoContexto();
        var resposta = await CriarServico(contexto).LoginAsync(new LoginRequest("ana@email.com", "Senha@123"));

        var parametros = TokenService.CriarParametrosDeValidacao(_jwt);
        parametros.ValidateLifetime = false; // o relógio do teste está parado em 2026-06-01
        var resultado = await new JsonWebTokenHandler().ValidateTokenAsync(resposta.AccessToken, parametros);

        Assert.True(resultado.IsValid);
    }

    [Fact]
    public async Task Login_DeveRecusarTokenAssinadoComOutraChave()
    {
        await using (var db = _banco.NovoContexto())
        {
            await CriarServico(db).RegistrarAsync(new RegistrarRequest("Ana", "ana@email.com", "Senha@123"));
        }

        await using var contexto = _banco.NovoContexto();
        var resposta = await CriarServico(contexto).LoginAsync(new LoginRequest("ana@email.com", "Senha@123"));

        var outraChave = new JwtOptions { Chave = "uma-chave-completamente-diferente-123" };
        var parametros = TokenService.CriarParametrosDeValidacao(outraChave);
        parametros.ValidateLifetime = false;
        var resultado = await new JsonWebTokenHandler().ValidateTokenAsync(resposta.AccessToken, parametros);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public async Task Login_DeveRecusarSenhaErrada()
    {
        await using (var db = _banco.NovoContexto())
        {
            await CriarServico(db).RegistrarAsync(new RegistrarRequest("Ana", "ana@email.com", "Senha@123"));
        }

        await using var contexto = _banco.NovoContexto();
        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() =>
            CriarServico(contexto).LoginAsync(new LoginRequest("ana@email.com", "senha-errada")));
    }

    [Fact]
    public async Task Login_DeveDarAMesmaRespostaParaEmailInexistente()
    {
        await using var db = _banco.NovoContexto();

        var erro = await Assert.ThrowsAsync<CredenciaisInvalidasException>(() =>
            CriarServico(db).LoginAsync(new LoginRequest("ninguem@email.com", "Senha@123")));

        // Mesma mensagem da senha errada: não revela se o e-mail existe.
        Assert.Equal("E-mail ou senha inválidos.", erro.Message);
    }

    public void Dispose() => _banco.Dispose();
}
