using System.Security.Cryptography;
using Ingressa.Application.Abstracoes;

namespace Ingressa.Infrastructure.Seguranca;

/// <summary>
/// PBKDF2-HMAC-SHA256 com salt aleatório. Formato: <c>v1.{iteracoes}.{salt}.{hash}</c>.
/// Mesmo formato da versão Júnior, então senhas antigas continuam válidas.
/// </summary>
public sealed class SenhaHasherPbkdf2 : ISenhaHasher
{
    public const int IteracoesPadrao = 600_000;
    private const int TamanhoSalt = 16;
    private const int TamanhoHash = 32;
    private const string Versao = "v1";

    private readonly int _iteracoes;

    public SenhaHasherPbkdf2() : this(IteracoesPadrao) { }

    public SenhaHasherPbkdf2(int iteracoes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iteracoes, 1);
        _iteracoes = iteracoes;
    }

    public string GerarHash(string senha)
    {
        ArgumentException.ThrowIfNullOrEmpty(senha);
        var salt = RandomNumberGenerator.GetBytes(TamanhoSalt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(senha, salt, _iteracoes, HashAlgorithmName.SHA256, TamanhoHash);
        return $"{Versao}.{_iteracoes}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verificar(string senha, string hash)
    {
        if (string.IsNullOrEmpty(senha) || string.IsNullOrEmpty(hash))
        {
            return false;
        }

        var partes = hash.Split('.');
        if (partes.Length != 4 || partes[0] != Versao || !int.TryParse(partes[1], out var iteracoes) || iteracoes < 1)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(partes[2]);
            var esperado = Convert.FromBase64String(partes[3]);
            var calculado = Rfc2898DeriveBytes.Pbkdf2(senha, salt, iteracoes, HashAlgorithmName.SHA256, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(calculado, esperado);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed class GeradorDeCodigos : IGeradorDeCodigos
{
    public string GerarTokenSeguro(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    public string Hash(string valor) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(valor)));

    /// <summary>16 caracteres hexadecimais (64 bits aleatórios).</summary>
    public string GerarCodigoDeIngresso() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

    private static string Base64Url(byte[] dados) =>
        Convert.ToBase64String(dados).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
