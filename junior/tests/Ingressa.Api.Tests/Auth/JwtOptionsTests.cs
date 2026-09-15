using Ingressa.Api.Auth;

namespace Ingressa.Api.Tests.Auth;

public class JwtOptionsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("curta-demais")]
    public void Validar_DeveFalharComChaveCurta(string chave)
    {
        var opcoes = new JwtOptions { Chave = chave };

        var erro = Assert.Throws<InvalidOperationException>(opcoes.Validar);
        Assert.Contains("32 bytes", erro.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public void Validar_DeveFalharComExpiracaoForaDoIntervalo(int minutos)
    {
        var opcoes = new JwtOptions { Chave = new string('x', 32), ExpiracaoMinutos = minutos };

        Assert.Throws<InvalidOperationException>(opcoes.Validar);
    }

    [Fact]
    public void Validar_DeveAceitarConfiguracaoSegura()
    {
        var opcoes = new JwtOptions { Chave = new string('x', 32), ExpiracaoMinutos = 60 };

        opcoes.Validar();
    }
}
