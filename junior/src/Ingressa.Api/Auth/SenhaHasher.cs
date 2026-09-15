using System.Security.Cryptography;

namespace Ingressa.Api.Auth;

/// <summary>
/// Gera e confere hashes de senha com PBKDF2-HMAC-SHA256 e salt aleatório.
/// Formato armazenado: <c>v1.{iteracoes}.{salt base64}.{hash base64}</c>.
/// Guardar as iterações no próprio hash permite aumentá-las no futuro sem
/// invalidar senhas antigas.
/// </summary>
public sealed class SenhaHasher
{
    // Recomendação da OWASP (Password Storage Cheat Sheet) para PBKDF2-HMAC-SHA256.
    public const int IteracoesPadrao = 600_000;
    private const int TamanhoSalt = 16;
    private const int TamanhoHash = 32;
    private const string Versao = "v1";

    private readonly int _iteracoes;

    public SenhaHasher() : this(IteracoesPadrao) { }

    /// <summary>Permite menos iterações nos testes, para que rodem rápido.</summary>
    internal SenhaHasher(int iteracoes)
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

    public bool Verificar(string senha, string hashArmazenado)
    {
        if (string.IsNullOrEmpty(senha) || string.IsNullOrEmpty(hashArmazenado))
        {
            return false;
        }

        var partes = hashArmazenado.Split('.');
        if (partes.Length != 4 || partes[0] != Versao || !int.TryParse(partes[1], out var iteracoes) || iteracoes < 1)
        {
            return false;
        }

        byte[] salt;
        byte[] esperado;
        try
        {
            salt = Convert.FromBase64String(partes[2]);
            esperado = Convert.FromBase64String(partes[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var calculado = Rfc2898DeriveBytes.Pbkdf2(senha, salt, iteracoes, HashAlgorithmName.SHA256, esperado.Length);

        // Comparação em tempo constante: não revela quantos bytes coincidiram.
        return CryptographicOperations.FixedTimeEquals(calculado, esperado);
    }
}
