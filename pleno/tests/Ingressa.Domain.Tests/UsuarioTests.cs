using Ingressa.Domain.Comum;
using Ingressa.Domain.Usuarios;
using static Ingressa.Domain.Tests.Construtores;

namespace Ingressa.Domain.Tests;

public class UsuarioTests
{
    private static Usuario Novo(PerfilUsuario perfil = PerfilUsuario.Cliente) =>
        Usuario.Registrar("Maria Silva", "  Maria@Email.com ", "hash", perfil, Agora);

    [Fact]
    public void Registrar_ClienteComecaAtivoEComEmailNormalizado()
    {
        var usuario = Novo();

        Assert.Equal("maria@email.com", usuario.Email);
        Assert.Equal(StatusConta.Ativa, usuario.Status);
        Assert.False(usuario.PodeVender);
    }

    [Fact]
    public void Registrar_OrganizadorPrecisaDeAprovacao()
    {
        var organizador = Novo(PerfilUsuario.Organizador);

        Assert.Equal(StatusConta.AguardandoAprovacao, organizador.Status);
        Assert.False(organizador.PodeVender);

        organizador.AprovarComoOrganizador();
        Assert.True(organizador.PodeVender);
        Assert.Throws<ConflitoException>(organizador.AprovarComoOrganizador);
    }

    [Fact]
    public void Registrar_NaoPermiteCriarAdmin()
    {
        Assert.Throws<RegraDeNegocioException>(() => Novo(PerfilUsuario.Admin));
    }

    [Fact]
    public void AprovarComoOrganizador_ClienteNaoPrecisa()
    {
        Assert.Throws<RegraDeNegocioException>(Novo().AprovarComoOrganizador);
    }

    [Fact]
    public void FalhasDeLogin_BloqueiamAContaTemporariamente()
    {
        var usuario = Novo();

        for (var i = 0; i < Usuario.MaximoTentativasDeLogin - 1; i++)
        {
            usuario.RegistrarFalhaDeLogin(Agora);
        }

        Assert.False(usuario.EstaBloqueado(Agora));

        usuario.RegistrarFalhaDeLogin(Agora);
        Assert.True(usuario.EstaBloqueado(Agora.AddMinutes(14)));
        Assert.False(usuario.EstaBloqueado(Agora + Usuario.DuracaoDoBloqueio));
    }

    [Fact]
    public void LoginComSucesso_ZeraAsFalhas()
    {
        var usuario = Novo();
        usuario.RegistrarFalhaDeLogin(Agora);
        usuario.RegistrarFalhaDeLogin(Agora);

        usuario.RegistrarLoginComSucesso();

        Assert.Equal(0, usuario.TentativasFalhas);
    }
}

public class RefreshTokenTests
{
    [Fact]
    public void Token_AtivoAteSerUsadoRevogadoOuExpirar()
    {
        var token = RefreshToken.Emitir(1, "hash", Guid.NewGuid(), Agora, TimeSpan.FromDays(7));

        Assert.True(token.EstaAtivo(Agora));
        Assert.False(token.EstaAtivo(Agora.AddDays(7)));

        token.MarcarComoUsado(Agora);
        Assert.True(token.FoiUsado);
        Assert.False(token.EstaAtivo(Agora));
    }

    [Fact]
    public void Revogar_DesativaOToken()
    {
        var token = RefreshToken.Emitir(1, "hash", Guid.NewGuid(), Agora, TimeSpan.FromDays(7));

        token.Revogar(Agora);

        Assert.False(token.EstaAtivo(Agora));
    }
}
