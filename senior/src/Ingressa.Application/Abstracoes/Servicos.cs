using Ingressa.Domain.Usuarios;

namespace Ingressa.Application.Abstracoes;

public interface ISenhaHasher
{
    string GerarHash(string senha);
    bool Verificar(string senha, string hash);
}

public interface IGeradorDeAccessToken
{
    (string Token, DateTime ExpiraEm) Gerar(Usuario usuario);
}

public interface IGeradorDeCodigos
{
    /// <summary>Valor aleatório de um gerador criptográfico, seguro para uso em URL.</summary>
    string GerarTokenSeguro(int bytes);

    /// <summary>Hash SHA-256 usado para guardar tokens sem guardar o valor original.</summary>
    string Hash(string valor);

    /// <summary>Código do ingresso apresentado na entrada do evento.</summary>
    string GerarCodigoDeIngresso();
}

public sealed record ResultadoPagamento(bool Aprovado, string? Codigo, string? MotivoRecusa);

/// <summary>
/// Gateway de pagamento. A aplicação nunca recebe número de cartão: o navegador
/// envia um token gerado pelo gateway (como exige o padrão PCI DSS).
/// </summary>
public interface IGatewayDePagamento
{
    Task<ResultadoPagamento> CobrarAsync(int pedidoId, decimal valor, string tokenDePagamento, CancellationToken ct);

    /// <summary>Devolve o valor ao cliente. Deve ser idempotente para o mesmo código.</summary>
    Task EstornarAsync(string codigoPagamento, CancellationToken ct);
}
