using Ingressa.Domain.Comum;
using Ingressa.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Ingressa.Infrastructure.Mensageria;

/// <summary>
/// Base dos consumidores: reconecta se o broker cair, cria um escopo de DI por mensagem,
/// confirma (ack) só depois do processamento e devolve a mensagem para a fila em caso de erro.
/// </summary>
public abstract class ConsumidorRabbitMq(
    ConexaoRabbitMq conexao,
    IServiceScopeFactory scopes,
    ILogger logger) : BackgroundService
{
    protected abstract string Fila { get; }

    protected virtual ushort MensagensSimultaneas => 10;

    protected abstract Task ProcessarAsync(IEventoDeDominio mensagem, IServiceProvider servicos, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumirAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Consumidor da fila {Fila} parou; reconectando em 5 s", Fila);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ConsumirAsync(CancellationToken stoppingToken)
    {
        await using var canal = await conexao.CriarCanalAsync(comConfirmacao: false, stoppingToken);
        await canal.BasicQosAsync(prefetchSize: 0, prefetchCount: MensagensSimultaneas, global: false, stoppingToken);

        var consumidor = new AsyncEventingBasicConsumer(canal);
        consumidor.ReceivedAsync += (_, entrega) => TratarAsync(canal, entrega, stoppingToken);

        await canal.BasicConsumeAsync(Fila, autoAck: false, consumer: consumidor, cancellationToken: stoppingToken);
        logger.LogInformation("Consumindo a fila {Fila}", Fila);

        // Fica aqui até o canal fechar (queda do broker) ou o serviço parar.
        while (canal.IsOpen && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task TratarAsync(IChannel canal, BasicDeliverEventArgs entrega, CancellationToken ct)
    {
        var tipo = entrega.BasicProperties.Type ?? entrega.RoutingKey;
        using var escopoDeLog = logger.BeginScope(new Dictionary<string, object?>
        {
            ["MensagemId"] = entrega.BasicProperties.MessageId,
            ["CorrelacaoId"] = entrega.BasicProperties.CorrelationId,
            ["TipoMensagem"] = tipo
        });

        IEventoDeDominio? mensagem;
        try
        {
            mensagem = CatalogoDeMensagens.Ler(tipo, entrega.Body.Span);
        }
        catch (System.Text.Json.JsonException ex)
        {
            logger.LogError(ex, "Mensagem ilegível; enviada para a fila de falhas");
            await canal.BasicNackAsync(entrega.DeliveryTag, multiple: false, requeue: false, ct);
            return;
        }

        if (mensagem is null)
        {
            logger.LogError("Tipo de mensagem desconhecido; enviada para a fila de falhas");
            await canal.BasicNackAsync(entrega.DeliveryTag, multiple: false, requeue: false, ct);
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await ProcessarAsync(mensagem, scope.ServiceProvider, ct);
            await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Volta para a fila; depois de Topologia.LimiteDeEntregas tentativas, vai para a fila de falhas.
            logger.LogWarning(ex, "Falha ao processar a mensagem (entrega repetida: {Repetida})", entrega.Redelivered);
            await canal.BasicNackAsync(entrega.DeliveryTag, multiple: false, requeue: true, ct);
        }
    }
}
