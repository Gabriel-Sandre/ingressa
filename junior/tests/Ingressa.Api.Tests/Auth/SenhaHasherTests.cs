using Ingressa.Api.Auth;

namespace Ingressa.Api.Tests.Auth;

public class SenhaHasherTests
{
    // Poucas iterações só para o teste ser rápido; a regra testada é a mesma.
    private readonly SenhaHasher _hasher = new(iteracoes: 1_000);

    [Fact]
    public void Verificar_DeveAceitarASenhaCorreta()
    {
        var hash = _hasher.GerarHash("Senha@123");

        Assert.True(_hasher.Verificar("Senha@123", hash));
    }

    [Theory]
    [InlineData("senha@123")]
    [InlineData("Senha@1234")]
    [InlineData("")]
    public void Verificar_DeveRecusarSenhaDiferente(string tentativa)
    {
        var hash = _hasher.GerarHash("Senha@123");

        Assert.False(_hasher.Verificar(tentativa, hash));
    }

    [Fact]
    public void GerarHash_DeveProduzirResultadosDiferentesParaAMesmaSenha()
    {
        // O salt aleatório impede que duas contas com a mesma senha tenham o mesmo hash.
        var primeiro = _hasher.GerarHash("Senha@123");
        var segundo = _hasher.GerarHash("Senha@123");

        Assert.NotEqual(primeiro, segundo);
    }

    [Fact]
    public void GerarHash_NaoDeveConterASenha()
    {
        var hash = _hasher.GerarHash("MinhaSenhaSecreta");

        Assert.DoesNotContain("MinhaSenhaSecreta", hash);
        Assert.StartsWith("v1.1000.", hash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("texto-qualquer")]
    [InlineData("v1.abc.salt.hash")]
    [InlineData("v2.1000.AAAA.AAAA")]
    [InlineData("v1.1000.nao-e-base64!.AAAA")]
    public void Verificar_DeveRetornarFalsoParaHashMalFormado(string hashInvalido)
    {
        Assert.False(_hasher.Verificar("Senha@123", hashInvalido));
    }

    [Fact]
    public void Verificar_DeveUsarAsIteracoesGravadasNoHash()
    {
        // Um hash antigo, gerado com outra quantidade de iterações, continua válido.
        var hashAntigo = new SenhaHasher(iteracoes: 500).GerarHash("Senha@123");

        Assert.True(_hasher.Verificar("Senha@123", hashAntigo));
    }
}
