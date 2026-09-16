using Ingressa.Application.Abstracoes;
using Microsoft.Extensions.Logging;

namespace Ingressa.Infrastructure.Pagamentos;

/// <summary>
/// Simula um gateway de pagamento. Os tokens imitam o que um gateway real devolveria
/// para o navegador depois que o cliente digita o cartão no formulário DELE:
/// <list type="bullet">
/// <item><c>tok_aprovado</c> — pagamento aprovado</item>
/// <item><c>tok_recusado</c> — recusado pelo emissor</item>
/// <item><c>tok_sem_saldo</c> — saldo insuficiente</item>
/// </list>
/// </summary>
public sealed class GatewayDePagamentoSimulado(ILogger<GatewayDePagamentoSimulado> logger) : IGatewayDePagamento
{
    public const string TokenAprovado = "tok_aprovado";
    public const string TokenRecusado = "tok_recusado";
    public const string TokenSemSaldo = "tok_sem_saldo";

    public async Task<ResultadoPagamento> CobrarAsync(int pedidoId, decimal valor, string tokenDePagamento, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(250), ct); // latência de rede simulada

        var resultado = tokenDePagamento switch
        {
            TokenAprovado => new ResultadoPagamento(true, $"pay_{Guid.NewGuid():N}", null),
            TokenRecusado => new ResultadoPagamento(false, null, "cartão recusado pelo emissor."),
            TokenSemSaldo => new ResultadoPagamento(false, null, "saldo insuficiente."),
            _ => new ResultadoPagamento(false, null, "token de pagamento inválido.")
        };

        logger.LogInformation(
            "Gateway simulado: pedido {PedidoId}, valor {Valor}, aprovado={Aprovado}", pedidoId, valor, resultado.Aprovado);
        return resultado;
    }

    public Task EstornarAsync(string codigoPagamento, CancellationToken ct)
    {
        logger.LogInformation("Gateway simulado: estorno do pagamento {Codigo}", codigoPagamento);
        return Task.CompletedTask;
    }
}
