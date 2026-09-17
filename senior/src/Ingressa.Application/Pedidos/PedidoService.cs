using System.Diagnostics;
using Ingressa.Application.Abstracoes;
using Ingressa.Application.Observabilidade;
using Ingressa.Domain.Comum;
using Ingressa.Domain.Pedidos;
using Microsoft.Extensions.Logging;

namespace Ingressa.Application.Pedidos;

public sealed class PedidoService(
    IPedidoRepositorio pedidos,
    IEventoRepositorio eventos,
    IConsultasDePedidos consultas,
    IEstoque estoque,
    IUnidadeDeTrabalho unidade,
    IGatewayDePagamento gateway,
    IFilaVirtual fila,
    TimeProvider relogio,
    ILogger<PedidoService> logger)
{
    private DateTime Agora => relogio.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Reserva os lugares por <see cref="Pedido.PrazoDaReserva"/>. A ocupação de cada setor
    /// é um UPDATE atômico e condicional; se qualquer setor falhar, a transação inteira
    /// é desfeita e nenhum lugar fica preso.
    /// Em eventos com fila virtual, exige o passe liberado pela sala de espera (uso único).
    /// </summary>
    public async Task<PedidoResponse> ReservarAsync(
        int usuarioId, CriarPedidoRequest request, string? passeDaFila, CancellationToken ct)
    {
        using var atividade = Metricas.Rastreamento.StartActivity("pedido.reservar");
        atividade?.SetTag("ingressa.evento_id", request.EventoId);

        var evento = await eventos.ObterAsync(request.EventoId, ct);
        if (evento is null || !evento.Publicado)
        {
            throw new NaoEncontradoException($"Evento {request.EventoId} não encontrado.");
        }

        var pedido = Pedido.Reservar(
            usuarioId, evento, request.Itens.Select(i => (i.SetorId, i.Quantidade)), Agora);

        if (evento.FilaVirtual)
        {
            if (string.IsNullOrWhiteSpace(passeDaFila) ||
                !await fila.UsarPasseAsync(evento.Id, usuarioId, passeDaFila, ct))
            {
                Metricas.Reservas.Add(1, Metricas.Resultado("sem_passe"));
                throw new AcessoNegadoException(
                    "Este evento tem fila virtual. Entre na fila e aguarde sua vez para reservar.");
            }
        }

        int pedidoId;
        try
        {
            pedidoId = await ReservarNoBancoAsync(pedido, ct);
        }
        catch (Exception) when (evento.FilaVirtual)
        {
            // A reserva não deu certo — por regra de negócio (acabou o setor escolhido) ou por
            // falha inesperada (banco fora do ar, tempo esgotado). Em qualquer caso o passe volta
            // a valer: o comprador tenta de novo sem voltar para o fim da fila e sem ficar preso
            // ocupando uma vaga até o passe vencer.
            await fila.DevolverPasseAsync(evento.Id, usuarioId, passeDaFila!, CancellationToken.None);
            throw;
        }

        if (evento.FilaVirtual)
        {
            await fila.ConcluirAsync(evento.Id, usuarioId, CancellationToken.None);
        }

        var ingressos = pedido.Itens.Sum(i => i.Quantidade);
        Metricas.Reservas.Add(1, Metricas.Resultado("sucesso"));
        Metricas.IngressosReservados.Add(ingressos);
        atividade?.SetTag("ingressa.pedido_id", pedidoId);

        logger.LogInformation(
            "Pedido {PedidoId} reservado: {Ingressos} ingresso(s) do evento {EventoId}, expira em {ExpiraEm:o}",
            pedidoId, ingressos, evento.Id, pedido.ExpiraEm);

        return await ObterAsync(usuarioId, pedidoId, ct);
    }

    private Task<int> ReservarNoBancoAsync(Pedido pedido, CancellationToken ct) =>
        unidade.EmTransacaoAsync(async token =>
        {
            foreach (var item in pedido.Itens)
            {
                if (!await estoque.TentarOcuparAsync(item.SetorId, item.Quantidade, token))
                {
                    Metricas.Reservas.Add(1, Metricas.Resultado("sem_estoque"));
                    throw new ConflitoException(
                        $"Não há ingressos suficientes no setor '{item.NomeSetor}' para esta quantidade.");
                }
            }

            pedidos.Adicionar(pedido);
            await unidade.SalvarAsync(token);
            return pedido.Id;
        }, ct);

    public async Task<PedidoResponse> PagarAsync(int usuarioId, int pedidoId, PagamentoRequest request, CancellationToken ct)
    {
        var pedido = await CarregarAsync(usuarioId, pedidoId, ct);

        if (pedido.Status != StatusPedido.AguardandoPagamento)
        {
            throw new ConflitoException("Este pedido não está aguardando pagamento.");
        }

        if (Agora >= pedido.ExpiraEm)
        {
            throw new RegraDeNegocioException("O prazo da reserva terminou. Faça um novo pedido.");
        }

        // Chamada externa fora de transação: nunca segurar conexão do banco esperando outro sistema.
        var cronometro = Stopwatch.StartNew();
        var resultado = await gateway.CobrarAsync(pedido.Id, pedido.Total, request.TokenDePagamento, ct);
        Metricas.DuracaoDoPagamento.Record(cronometro.Elapsed.TotalSeconds);
        if (!resultado.Aprovado)
        {
            Metricas.Pagamentos.Add(1, Metricas.Resultado("recusado"));
            logger.LogInformation("Pagamento do pedido {PedidoId} recusado: {Motivo}", pedido.Id, resultado.MotivoRecusa);
            throw new RegraDeNegocioException($"Pagamento recusado: {resultado.MotivoRecusa}");
        }

        try
        {
            pedido.ConfirmarPagamento(resultado.Codigo!, Agora);
            await unidade.SalvarAsync(ct);
        }
        catch (IngressaException)
        {
            // A reserva expirou (ou foi cancelada) enquanto o gateway processava:
            // o cliente foi cobrado por algo que não vai receber, então devolvemos o valor.
            logger.LogWarning(
                "Pedido {PedidoId} mudou de estado durante o pagamento {Codigo}; estornando", pedido.Id, resultado.Codigo);
            await gateway.EstornarAsync(resultado.Codigo!, CancellationToken.None);
            Metricas.Pagamentos.Add(1, Metricas.Resultado("estornado"));
            throw;
        }

        Metricas.Pagamentos.Add(1, Metricas.Resultado("aprovado"));
        logger.LogInformation("Pedido {PedidoId} pago ({Codigo})", pedido.Id, resultado.Codigo);
        return await ObterAsync(usuarioId, pedido.Id, ct);
    }

    public async Task<PedidoResponse> CancelarAsync(int usuarioId, int pedidoId, CancellationToken ct)
    {
        var pedido = await CarregarAsync(usuarioId, pedidoId, ct);
        var evento = await eventos.ObterAsync(pedido.EventoId, ct)
            ?? throw new NaoEncontradoException($"Evento {pedido.EventoId} não encontrado.");

        await unidade.EmTransacaoAsync(async token =>
        {
            pedido.Cancelar(evento.DataInicio, Agora);
            await LiberarLugaresAsync(pedido, token);
            await unidade.SalvarAsync(token);
            return true;
        }, ct);

        // O estorno (se o pedido estava pago) e o e-mail acontecem no Worker, a partir do
        // evento PedidoCancelado gravado na outbox junto com esta transação.
        logger.LogInformation("Pedido {PedidoId} cancelado pelo cliente", pedido.Id);
        return await ObterAsync(usuarioId, pedido.Id, ct);
    }

    public async Task<PedidoResponse> ObterAsync(int usuarioId, int pedidoId, CancellationToken ct)
    {
        var pedido = await consultas.ObterAsync(pedidoId, ct);
        if (pedido is null || pedido.UsuarioId != usuarioId)
        {
            throw new NaoEncontradoException($"Pedido {pedidoId} não encontrado.");
        }

        return pedido;
    }

    public Task<IReadOnlyList<PedidoResponse>> ListarAsync(int usuarioId, CancellationToken ct) =>
        consultas.ListarDoUsuarioAsync(usuarioId, ct);

    private async Task<Pedido> CarregarAsync(int usuarioId, int pedidoId, CancellationToken ct)
    {
        var pedido = await pedidos.ObterAsync(pedidoId, ct)
            ?? throw new NaoEncontradoException($"Pedido {pedidoId} não encontrado.");
        pedido.GarantirQuePertenceA(usuarioId);
        return pedido;
    }

    internal async Task LiberarLugaresAsync(Pedido pedido, CancellationToken ct)
    {
        foreach (var item in pedido.Itens)
        {
            await estoque.LiberarAsync(item.SetorId, item.Quantidade, ct);
        }
    }
}
