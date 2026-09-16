using System.Text;
using Ingressa.Infrastructure.Outbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Ingressa.Infrastructure.Mensageria;

public sealed class OpcoesRabbitMq
{
    public const string Secao = "RabbitMq";

    public string Host { get; set; } = "localhost";
    public int Porta { get; set; } = 5672;
    public string Usuario { get; set; } = "guest";
    public string Senha { get; set; } = "guest";
}

/// <summary>Nomes de exchanges e filas em um só lugar.</summary>
public static class Topologia
{
    public const string Exchange = "ingressa.eventos";
    public const string ExchangeDeFalhas = "ingressa.eventos.falhas";
    public const string FilaDeFalhas = "ingressa.falhas";
    public const string FilaEmissao = "ingressa.emissao-ingressos";
    public const string FilaNotificacoes = "ingressa.notificacoes";

    /// <summary>Após este número de entregas sem sucesso, a mensagem vai para a fila de falhas.</summary>
    public const int LimiteDeEntregas = 5;

    public static async Task DeclararAsync(IChannel canal, CancellationToken ct)
    {
        await canal.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
        await canal.ExchangeDeclareAsync(ExchangeDeFalhas, ExchangeType.Fanout, durable: true, cancellationToken: ct);

        await canal.QueueDeclareAsync(FilaDeFalhas, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" }, cancellationToken: ct);
        await canal.QueueBindAsync(FilaDeFalhas, ExchangeDeFalhas, routingKey: string.Empty, cancellationToken: ct);

        await DeclararFilaAsync(canal, FilaEmissao, [CatalogoDeMensagens.PedidoPago], ct);
        await DeclararFilaAsync(canal, FilaNotificacoes,
            [CatalogoDeMensagens.IngressosEmitidos, CatalogoDeMensagens.PedidoExpirado, CatalogoDeMensagens.PedidoCancelado], ct);
    }

    private static async Task DeclararFilaAsync(IChannel canal, string fila, string[] routingKeys, CancellationToken ct)
    {
        // Filas quorum são replicadas e contam as entregas; passando do limite,
        // a mensagem "venenosa" é desviada para a fila de falhas em vez de travar a fila.
        var argumentos = new Dictionary<string, object?>
        {
            ["x-queue-type"] = "quorum",
            ["x-delivery-limit"] = LimiteDeEntregas,
            ["x-dead-letter-exchange"] = ExchangeDeFalhas
        };

        await canal.QueueDeclareAsync(fila, durable: true, exclusive: false, autoDelete: false, arguments: argumentos, cancellationToken: ct);
        foreach (var chave in routingKeys)
        {
            await canal.QueueBindAsync(fila, Exchange, chave, cancellationToken: ct);
        }
    }
}

/// <summary>Uma conexão por processo (conexões AMQP são caras); canais são criados por uso.</summary>
public sealed class ConexaoRabbitMq(IOptions<OpcoesRabbitMq> opcoes, ILogger<ConexaoRabbitMq> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _trava = new(1, 1);
    private IConnection? _conexao;
    private bool _topologiaDeclarada;

    public async Task<IChannel> CriarCanalAsync(bool comConfirmacao, CancellationToken ct)
    {
        var conexao = await ObterConexaoAsync(ct);
        var opcoesDoCanal = new CreateChannelOptions(
            publisherConfirmationsEnabled: comConfirmacao,
            publisherConfirmationTrackingEnabled: comConfirmacao);
        return await conexao.CreateChannelAsync(opcoesDoCanal, ct);
    }

    private async Task<IConnection> ObterConexaoAsync(CancellationToken ct)
    {
        if (_conexao is { IsOpen: true } && _topologiaDeclarada)
        {
            return _conexao;
        }

        await _trava.WaitAsync(ct);
        try
        {
            if (_conexao is not { IsOpen: true })
            {
                var o = opcoes.Value;
                var fabrica = new ConnectionFactory
                {
                    HostName = o.Host,
                    Port = o.Porta,
                    UserName = o.Usuario,
                    Password = o.Senha,
                    ClientProvidedName = $"ingressa-{Environment.MachineName}",
                    AutomaticRecoveryEnabled = true
                };
                _conexao = await fabrica.CreateConnectionAsync(ct);
                _topologiaDeclarada = false;
                logger.LogInformation("Conectado ao RabbitMQ em {Host}:{Porta}", o.Host, o.Porta);
            }

            if (!_topologiaDeclarada)
            {
                await using var canal = await _conexao.CreateChannelAsync(cancellationToken: ct);
                await Topologia.DeclararAsync(canal, ct);
                _topologiaDeclarada = true;
            }

            return _conexao;
        }
        finally
        {
            _trava.Release();
        }
    }

    public bool EstaConectado => _conexao is { IsOpen: true };

    public async ValueTask DisposeAsync()
    {
        if (_conexao is not null)
        {
            await _conexao.DisposeAsync();
        }

        _trava.Dispose();
    }
}

public interface IPublicadorDeMensagens
{
    Task PublicarAsync(MensagemOutbox mensagem, CancellationToken ct);
}

public sealed class PublicadorRabbitMq(ConexaoRabbitMq conexao) : IPublicadorDeMensagens, IAsyncDisposable
{
    private readonly SemaphoreSlim _trava = new(1, 1);
    private IChannel? _canal;

    public async Task PublicarAsync(MensagemOutbox mensagem, CancellationToken ct)
    {
        await _trava.WaitAsync(ct);
        try
        {
            if (_canal is not { IsOpen: true })
            {
                _canal = await conexao.CriarCanalAsync(comConfirmacao: true, ct);
            }

            var propriedades = new BasicProperties
            {
                Persistent = true,
                MessageId = mensagem.MensagemId.ToString(),
                Type = mensagem.Tipo,
                ContentType = "application/json",
                CorrelationId = mensagem.CorrelacaoId,
                Timestamp = new AmqpTimestamp(new DateTimeOffset(mensagem.OcorridoEm).ToUnixTimeSeconds())
            };

            // Com confirmação habilitada, o await só termina quando o broker confirma
            // que gravou a mensagem; se ele recusar, uma exceção é lançada.
            await _canal.BasicPublishAsync(
                exchange: Topologia.Exchange,
                routingKey: mensagem.Tipo,
                mandatory: true,
                basicProperties: propriedades,
                body: Encoding.UTF8.GetBytes(mensagem.Conteudo),
                cancellationToken: ct);
        }
        finally
        {
            _trava.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_canal is not null)
        {
            await _canal.DisposeAsync();
        }

        _trava.Dispose();
    }
}
