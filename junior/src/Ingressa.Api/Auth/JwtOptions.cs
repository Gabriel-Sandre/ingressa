using System.Text;

namespace Ingressa.Api.Auth;

public sealed class JwtOptions
{
    public const string Secao = "Jwt";

    public string Emissor { get; set; } = "Ingressa";
    public string Audiencia { get; set; } = "Ingressa.Clientes";
    public string Chave { get; set; } = string.Empty;
    public int ExpiracaoMinutos { get; set; } = 60;

    /// <summary>
    /// Falha na inicialização se a configuração for insegura, em vez de
    /// descobrir o problema em produção.
    /// </summary>
    public void Validar()
    {
        if (Encoding.UTF8.GetByteCount(Chave) < 32)
        {
            throw new InvalidOperationException(
                "A chave JWT (Jwt:Chave) precisa ter pelo menos 32 bytes. " +
                "Configure-a com 'dotnet user-secrets' ou com a variável de ambiente Jwt__Chave.");
        }

        if (ExpiracaoMinutos is < 1 or > 24 * 60)
        {
            throw new InvalidOperationException("Jwt:ExpiracaoMinutos deve estar entre 1 e 1440.");
        }
    }
}
