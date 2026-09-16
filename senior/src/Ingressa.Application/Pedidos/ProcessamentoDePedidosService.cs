using System.Globalization;
using Ingressa.Application.Abstracoes;
using Ingressa.Domain.Comum;
using Microsoft.Extensions.Logging;

namespace Ingressa.Application.Pedidos;

/// <summary>
/// Trabalhos que rodam fora da requisição do cliente (no Worker):
/// expirar reservas, emitir ingressos e avisar o cliente.
/// Todos são idempotentes: podem rodar mais de uma vez sem efeito duplicado.
/// </summary>
public sealed class ProcessamentoDePedidosService(
    IPedidoRepositorio pedidos,
    IConsultasDePedidos consultas,
    IEstoque estoque,
    IUnidadeDeTrabalho unidade,
    IGeradorDeCodigos codigos,
    IGatewayDePagamento gateway,
    IEnviadorDeEmail email,
    TimeProvider relogio,
    ILogger<ProcessamentoDePedidosService> logger)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private DateTime Agora => relogio.GetUtcNow().UtcDateTime;

    /// <summary>Expira reservas vencidas e devolve os lugares. Retorna quantas foram expiradas.</summary>
    public async Task<int> ExpirarReservasVencidasAsync(CancellationToken ct, int lote = 100)
    {
        var ids = await pedidos.ListarIdsDeReservasVencidasAsync(Agora, lote, ct);
        var expirados = 0;

        foreach (var id in ids)
        {
            try
            {
                var expirou = await unidade.EmTransacaoAsync(async token =>
                {
                    var pedido = await pedidos.ObterAsync(id, token);
                    if (pedido is null || !pedido.TentarExpirar(Agora))
                    {
                        return false;
                    }

                    foreach (var item in pedido.Itens)
                    {
                        await estoque.LiberarAsync(item.SetorId, item.Quantidade, token);
                    }

                    await unidade.SalvarAsync(token);
                    return true;
                }, ct);

                if (expirou)
                {
                    expirados++;
                }
            }
            catch (ConflitoException)
            {
                // O cliente pagou no mesmo instante: a concorrência otimista deu prioridade ao pagamento.
                logger.LogInformation("Pedido {PedidoId} mudou de estado durante a expiração; ignorado", id);
            }
        }

        if (expirados > 0)
        {
            logger.LogInformation("{Quantidade} reserva(s) expirada(s)", expirados);
        }

        return expirados;
    }

    public async Task<bool> EmitirIngressosAsync(int pedidoId, CancellationToken ct)
    {
        var pedido = await pedidos.ObterAsync(pedidoId, ct);
        if (pedido is null || !pedido.EmitirIngressos(codigos.GerarCodigoDeIngresso, Agora))
        {
            logger.LogInformation("Pedido {PedidoId}: nada a emitir (já emitido ou não pago)", pedidoId);
            return false;
        }

        await unidade.SalvarAsync(ct);
        logger.LogInformation("Pedido {PedidoId}: {Quantidade} ingresso(s) emitido(s)", pedidoId, pedido.Ingressos.Count);
        return true;
    }

    public async Task NotificarIngressosEmitidosAsync(int pedidoId, CancellationToken ct)
    {
        var dados = await consultas.ObterDadosParaNotificacaoAsync(pedidoId, ct);
        if (dados is null)
        {
            return;
        }

        var corpo = $"""
            Olá, {dados.NomeCliente}!

            Seu pagamento foi confirmado. Estes são os seus ingressos para "{dados.Evento}":

            {string.Join(Environment.NewLine, dados.CodigosIngressos.Select(c => $"  • {c}"))}

            Data: {TimeZoneInfo.ConvertTimeFromUtc(dados.DataEvento, Brasilia).ToString("dd/MM/yyyy 'às' HH:mm", PtBr)} (horário de Brasília)
            Local: {dados.Local}
            Total pago: {dados.Total.ToString("C", PtBr)}

            Apresente o código na entrada. Até lá!
            Equipe Ingressa
            """;

        await email.EnviarAsync(dados.EmailCliente, $"Seus ingressos para {dados.Evento}", corpo, ct);
    }

    public async Task NotificarExpiracaoAsync(int pedidoId, CancellationToken ct)
    {
        var dados = await consultas.ObterDadosParaNotificacaoAsync(pedidoId, ct);
        if (dados is null)
        {
            return;
        }

        await email.EnviarAsync(
            dados.EmailCliente,
            $"Sua reserva para {dados.Evento} expirou",
            $"Olá, {dados.NomeCliente}. O prazo para pagar o pedido nº {dados.PedidoId} terminou e os lugares foram liberados. " +
            "Se ainda quiser ir, é só fazer um novo pedido.",
            ct);
    }

    public async Task ProcessarCancelamentoAsync(int pedidoId, bool estavaPago, CancellationToken ct)
    {
        var pedido = await pedidos.ObterAsync(pedidoId, ct);
        if (pedido is null)
        {
            return;
        }

        if (estavaPago && pedido.CodigoPagamento is { } codigo)
        {
            await gateway.EstornarAsync(codigo, ct);
            logger.LogInformation("Pedido {PedidoId}: estorno solicitado ({Codigo})", pedidoId, codigo);
        }

        var dados = await consultas.ObterDadosParaNotificacaoAsync(pedidoId, ct);
        if (dados is not null)
        {
            await email.EnviarAsync(
                dados.EmailCliente,
                $"Pedido nº {dados.PedidoId} cancelado",
                $"Olá, {dados.NomeCliente}. Seu pedido para \"{dados.Evento}\" foi cancelado." +
                (estavaPago ? $" O valor de {dados.Total.ToString("C", PtBr)} será devolvido na forma de pagamento original." : string.Empty),
                ct);
        }
    }
}
