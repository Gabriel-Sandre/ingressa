using Ingressa.Application.Pedidos;
using Ingressa.Domain.Comum;
using Ingressa.Domain.Pedidos;

namespace Ingressa.Application.Tests;

public class PedidoServiceTests
{
    private readonly Cenario _c = new();

    [Fact]
    public async Task Reservar_DeveOcuparLugaresEDeixarAguardandoPagamento()
    {
        var cliente = await _c.ClienteAsync();
        var evento = await _c.EventoAsync();

        var pedido = await _c.Pedidos.ReservarAsync(cliente.Id, Cenario.Pedido(evento, (0, 2), (1, 1)), null, default);

        Assert.Equal(StatusPedido.AguardandoPagamento, pedido.Status);
        Assert.Equal(500m, pedido.Total);
        Assert.Equal(_c.Relogio.Agora + Pedido.PrazoDaReserva, pedido.ExpiraEm);
        Assert.Empty(pedido.Ingressos);
        Assert.Equal(2, evento.Setores[0].Ocupados);
        Assert.Equal(1, evento.Setores[1].Ocupados);
    }

    [Fact]
    public async Task Reservar_QuandoUmSetorNaoTemLugarNadaFicaReservado()
    {
        var cliente = await _c.ClienteAsync();
        var evento = await _c.EventoAsync(capacidadeVip: 1);

        var erro = await Assert.ThrowsAsync<ConflitoException>(() =>
            _c.Pedidos.ReservarAsync(cliente.Id, Cenario.Pedido(evento, (0, 3), (1, 2)), null, default));

        Assert.Contains("VIP", erro.Message);
        Assert.Equal(0, evento.Setores[0].Ocupados); // a Pista foi ocupada e depois desfeita
        Assert.Empty(_c.Banco.Pedidos);
    }

    [Fact]
    public async Task Reservar_ComprasSimultaneasNuncaUltrapassamACapacidade()
    {
        var evento = await _c.EventoAsync(capacidadePista: 10);
        var clientes = new List<int>();
        for (var i = 0; i < 50; i++)
        {
            clientes.Add((await _c.ClienteAsync($"c{i}@teste.com")).Id);
        }

        var tentativas = clientes.Select(id => Task.Run(async () =>
        {
            try
            {
                await _c.Pedidos.ReservarAsync(id, Cenario.Pedido(evento, (0, 1)), null, default);
                return true;
            }
            catch (ConflitoException)
            {
                return false;
            }
        }));

        var resultados = await Task.WhenAll(tentativas);

        Assert.Equal(10, resultados.Count(r => r));
        Assert.Equal(10, evento.Setores[0].Ocupados);
    }

    [Fact]
    public async Task Reservar_EventoNaoPublicadoRespondeNaoEncontrado()
    {
        var cliente = await _c.ClienteAsync();
        var organizador = await _c.OrganizadorAsync(email: "o2@teste.com");
        var rascunho = await _c.Eventos.CriarAsync(organizador.Id,
            new Eventos.EventoRequest("Rascunho", null, "Local", "Cidade", _c.Relogio.Agora.AddDays(3)), default);

        await Assert.ThrowsAsync<NaoEncontradoException>(() =>
            _c.Pedidos.ReservarAsync(cliente.Id, new CriarPedidoRequest(rascunho.Id, [new ItemPedidoRequest(1, 1)]), null, default));
    }

    [Fact]
    public async Task Pagar_AprovadoConfirmaEPublicaEventoNaOutbox()
    {
        var cliente = await _c.ClienteAsync();
        var evento = await _c.EventoAsync();
        var reserva = await _c.Pedidos.ReservarAsync(cliente.Id, Cenario.Pedido(evento, (0, 1)), null, default);

        var pago = await _c.Pedidos.PagarAsync(cliente.Id, reserva.Id, new PagamentoRequest("tok"), default);

        Assert.Equal(StatusPedido.Pago, pago.Status);
        Assert.Equal(_c.Relogio.Agora, pago.PagoEm);
        Assert.Contains(_c.Banco.Outbox, e => e is PedidoPago p && p.PedidoId == reserva.Id);
    }

    [Fact]
    public async Task Pagar_RecusadoMantemAReserva()
    {
        var cliente = await _c.ClienteAsync();
        var evento = await _c.EventoAsync();
        var reserva = await _c.Pedidos.ReservarAsync(cliente.Id, Cenario.Pedido(evento, (0, 1)), null, default);
        _c.Gateway.Aprovar = false;

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            _c.Pedidos.PagarAsync(cliente.Id, reserva.Id, new PagamentoRequest("tok"), default));

        Assert.Contains("recusado", erro.Message);
        Assert.Equal(StatusPedido.AguardandoPagamento, _c.Banco.Pedidos.Single().Status);
        Assert.Equal(1, evento.Setores[0].Ocupados);
    }

    [Fact]
    public async Task Pagar_DepoisDoPrazoNaoChegaAoGateway()
    {
        var cliente = await _c.ClienteAsync();
        var evento = await _c.EventoAsync();
        var reserva = await _c.Pedidos.ReservarAsync(cliente.Id, Cenario.Pedido(evento, (0, 1)), null, default);
        _c.Relogio.Avancar(Pedido.PrazoDaReserva);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            _c.Pedidos.PagarAsync(cliente.Id, reserva.Id, new PagamentoRequest("tok"), default));

        Assert.Equal(0, _c.Gateway.Cobrancas);
    }

    [Fact]
    public async Task Pagar_SeAReservaExpirarDuranteACobrancaOValorEEstornado()
    {
        var cliente = await _c.ClienteAsync();
        var evento = await _c.EventoAsync();
        var reserva = await _c.Pedidos.ReservarAsync(cliente.Id, Cenario.Pedido(evento, (0, 1)), null, default);

        // O gateway demora e, enquanto isso, o prazo acaba.
        _c.Gateway.DuranteACobranca = () => _c.Relogio.Avancar(Pedido.PrazoDaReserva);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            _c.Pedidos.PagarAsync(cliente.Id, reserva.Id, new PagamentoRequest("tok"), default));

        Assert.Equal($"pay_{reserva.Id}", Assert.Single(_c.Gateway.Estornos));
        Assert.Equal(StatusPedido.AguardandoPagamento, _c.Banco.Pedidos.Single().Status);
    }

    [Fact]
    public async Task Pagar_PedidoDeOutroClienteRespondeNaoEncontrado()
    {
        var dono = await _c.ClienteAsync();
        var outro = await _c.ClienteAsync("outro@teste.com");
        var evento = await _c.EventoAsync();
        var reserva = await _c.Pedidos.ReservarAsync(dono.Id, Cenario.Pedido(evento, (0, 1)), null, default);

        await Assert.ThrowsAsync<NaoEncontradoException>(() =>
            _c.Pedidos.PagarAsync(outro.Id, reserva.Id, new PagamentoRequest("tok"), default));
        await Assert.ThrowsAsync<NaoEncontradoException>(() => _c.Pedidos.ObterAsync(outro.Id, reserva.Id, default));
        Assert.Equal(0, _c.Gateway.Cobrancas);
    }

    [Fact]
    public async Task Cancelar_DevolveOsLugaresEPublicaEvento()
    {
        var cliente = await _c.ClienteAsync();
        var evento = await _c.EventoAsync();
        var reserva = await _c.Pedidos.ReservarAsync(cliente.Id, Cenario.Pedido(evento, (0, 3)), null, default);
        await _c.Pedidos.PagarAsync(cliente.Id, reserva.Id, new PagamentoRequest("tok"), default);

        var cancelado = await _c.Pedidos.CancelarAsync(cliente.Id, reserva.Id, default);

        Assert.Equal(StatusPedido.Cancelado, cancelado.Status);
        Assert.Equal(0, evento.Setores[0].Ocupados);
        Assert.Contains(_c.Banco.Outbox, e => e is PedidoCancelado { EstavaPago: true });
    }

    [Fact]
    public async Task Cancelar_PagoEmCimaDaHoraNaoDevolveLugares()
    {
        var cliente = await _c.ClienteAsync();
        var evento = await _c.EventoAsync(dias: 1);
        var reserva = await _c.Pedidos.ReservarAsync(cliente.Id, Cenario.Pedido(evento, (0, 2)), null, default);
        await _c.Pedidos.PagarAsync(cliente.Id, reserva.Id, new PagamentoRequest("tok"), default);
        _c.Relogio.Avancar(TimeSpan.FromHours(2));

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _c.Pedidos.CancelarAsync(cliente.Id, reserva.Id, default));

        Assert.Equal(2, evento.Setores[0].Ocupados);
    }
}
