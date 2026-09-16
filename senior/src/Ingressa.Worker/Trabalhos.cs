using Ingressa.Application.Pedidos;
using Ingressa.Domain.Comum;
using Ingressa.Domain.Pedidos;
using Ingressa.Infrastructure.Mensageria;

namespace Ingressa.Worker;

/// <summary>Fila "emissão": quando um pedido é pago, gera os ingressos.</summary>
public sealed class ConsumidorDeEmissao(ConexaoRabbitMq conexao, IServiceScopeFactory scopes, ILogger<ConsumidorDeEmissao> logger)
    : ConsumidorRabbitMq(conexao, scopes, logger)
{
    protected override string Fila => Topologia.FilaEmissao;

    protected override async Task ProcessarAsync(IEventoDeDominio mensagem, IServiceProvider servicos, CancellationToken ct)
    {
        if (mensagem is PedidoPago pago)
        {
            await servicos.GetRequiredService<ProcessamentoDePedidosService>().EmitirIngressosAsync(pago.PedidoId, ct);
        }
    }
}

/// <summary>Fila "notificações": e-mails e estornos.</summary>
public sealed class ConsumidorDeNotificacoes(ConexaoRabbitMq conexao, IServiceScopeFactory scopes, ILogger<ConsumidorDeNotificacoes> logger)
    : ConsumidorRabbitMq(conexao, scopes, logger)
{
    protected override string Fila => Topologia.FilaNotificacoes;

    protected override Task ProcessarAsync(IEventoDeDominio mensagem, IServiceProvider servicos, CancellationToken ct)
    {
        var processamento = servicos.GetRequiredService<ProcessamentoDePedidosService>();
        return mensagem switch
        {
            IngressosEmitidos e => processamento.NotificarIngressosEmitidosAsync(e.PedidoId, ct),
            PedidoExpirado e => processamento.NotificarExpiracaoAsync(e.PedidoId, ct),
            PedidoCancelado e => processamento.ProcessarCancelamentoAsync(e.PedidoId, e.EstavaPago, ct),
            _ => Task.CompletedTask
        };
    }
}

/// <summary>A cada intervalo, devolve ao estoque os lugares de reservas não pagas.</summary>
public sealed class ExpiradorDeReservas(IServiceScopeFactory scopes, IConfiguration configuracao, ILogger<ExpiradorDeReservas> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromSeconds(configuracao.GetValue("Expiracao:IntervaloEmSegundos", 30));
        using var temporizador = new PeriodicTimer(intervalo);

        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ProcessamentoDePedidosService>()
                    .ExpirarReservasVencidasAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao expirar reservas; nova tentativa no próximo ciclo");
            }
        }
        while (await temporizador.WaitForNextTickAsync(stoppingToken));
    }
}
