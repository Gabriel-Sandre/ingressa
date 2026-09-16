using Ingressa.Application.Pedidos;
using Ingressa.Domain.Pedidos;

namespace Ingressa.Application.Tests;

public class ProcessamentoDePedidosTests
{
    private readonly Cenario _c = new();

    private async Task<(Domain.Eventos.Evento Evento, PedidoResponse Pedido, Domain.Usuarios.Usuario Cliente)> ReservaAsync(int quantidade = 2)
    {
        var cliente = await _c.ClienteAsync();
        var evento = await _c.EventoAsync();
        var pedido = await _c.Pedidos.ReservarAsync(cliente.Id, Cenario.Pedido(evento, (0, quantidade)), null, default);
        return (evento, pedido, cliente);
    }

    [Fact]
    public async Task Expirar_DevolveLugaresSomenteDasReservasVencidas()
    {
        var (evento, pedido, cliente) = await ReservaAsync(2);
        _c.Relogio.Avancar(TimeSpan.FromMinutes(5));
        var recente = await _c.Pedidos.ReservarAsync(cliente.Id, Cenario.Pedido(evento, (0, 1)), null, default);
        _c.Relogio.Avancar(TimeSpan.FromMinutes(5)); // a primeira venceu; a segunda ainda não

        var expirados = await _c.Processamento.ExpirarReservasVencidasAsync(default);

        Assert.Equal(1, expirados);
        Assert.Equal(StatusPedido.Expirado, _c.Banco.Pedidos.Single(p => p.Id == pedido.Id).Status);
        Assert.Equal(StatusPedido.AguardandoPagamento, _c.Banco.Pedidos.Single(p => p.Id == recente.Id).Status);
        Assert.Equal(1, evento.Setores[0].Ocupados);
        Assert.Contains(_c.Banco.Outbox, e => e is PedidoExpirado x && x.PedidoId == pedido.Id);
    }

    [Fact]
    public async Task Expirar_RodarDuasVezesNaoDevolveEmDobro()
    {
        var (evento, _, _) = await ReservaAsync(2);
        _c.Relogio.Avancar(TimeSpan.FromMinutes(11));

        await _c.Processamento.ExpirarReservasVencidasAsync(default);
        var segunda = await _c.Processamento.ExpirarReservasVencidasAsync(default);

        Assert.Equal(0, segunda);
        Assert.Equal(0, evento.Setores[0].Ocupados);
    }

    [Fact]
    public async Task Expirar_PedidoPagoNoMesmoInstanteNaoEExpirado()
    {
        var (evento, pedido, cliente) = await ReservaAsync(2);
        _c.Relogio.Avancar(TimeSpan.FromMinutes(9));
        await _c.Pedidos.PagarAsync(cliente.Id, pedido.Id, new PagamentoRequest("tok"), default);
        _c.Relogio.Avancar(TimeSpan.FromMinutes(2));

        Assert.Equal(0, await _c.Processamento.ExpirarReservasVencidasAsync(default));
        Assert.Equal(2, evento.Setores[0].Ocupados);
    }

    [Fact]
    public async Task EmitirIngressos_EIdempotente()
    {
        var (_, pedido, cliente) = await ReservaAsync(3);
        await _c.Pedidos.PagarAsync(cliente.Id, pedido.Id, new PagamentoRequest("tok"), default);

        Assert.True(await _c.Processamento.EmitirIngressosAsync(pedido.Id, default));
        Assert.False(await _c.Processamento.EmitirIngressosAsync(pedido.Id, default));

        var atualizado = await _c.Pedidos.ObterAsync(cliente.Id, pedido.Id, default);
        Assert.True(atualizado.IngressosEmitidos);
        Assert.Equal(3, atualizado.Ingressos.Count);
        Assert.All(atualizado.Ingressos, i => Assert.Equal("Pista", i.Setor));
        Assert.Single(_c.Banco.Outbox.OfType<IngressosEmitidos>());
    }

    [Fact]
    public async Task EmitirIngressos_PedidoNaoPagoNaoEmite()
    {
        var (_, pedido, _) = await ReservaAsync();

        Assert.False(await _c.Processamento.EmitirIngressosAsync(pedido.Id, default));
    }

    [Fact]
    public async Task Notificacao_EnviaOsCodigosParaOCliente()
    {
        var (_, pedido, cliente) = await ReservaAsync(2);
        await _c.Pedidos.PagarAsync(cliente.Id, pedido.Id, new PagamentoRequest("tok"), default);
        await _c.Processamento.EmitirIngressosAsync(pedido.Id, default);

        await _c.Processamento.NotificarIngressosEmitidosAsync(pedido.Id, default);

        var (para, assunto, corpo) = Assert.Single(_c.Email.Enviados);
        Assert.Equal(cliente.Email, para);
        Assert.Contains("Show", assunto);
        foreach (var ingresso in _c.Banco.Pedidos.Single().Ingressos)
        {
            Assert.Contains(ingresso.Codigo, corpo);
        }

        Assert.Contains("R$", corpo);
        Assert.Contains("horário de Brasília", corpo);
    }

    [Fact]
    public async Task Cancelamento_PagoGeraEstornoEAviso()
    {
        var (_, pedido, cliente) = await ReservaAsync();
        await _c.Pedidos.PagarAsync(cliente.Id, pedido.Id, new PagamentoRequest("tok"), default);
        await _c.Pedidos.CancelarAsync(cliente.Id, pedido.Id, default);

        await _c.Processamento.ProcessarCancelamentoAsync(pedido.Id, estavaPago: true, default);

        Assert.Equal($"pay_{pedido.Id}", Assert.Single(_c.Gateway.Estornos));
        Assert.Contains("devolvido", Assert.Single(_c.Email.Enviados).Corpo);
    }

    [Fact]
    public async Task Cancelamento_DeReservaNaoGeraEstorno()
    {
        var (_, pedido, cliente) = await ReservaAsync();
        await _c.Pedidos.CancelarAsync(cliente.Id, pedido.Id, default);

        await _c.Processamento.ProcessarCancelamentoAsync(pedido.Id, estavaPago: false, default);

        Assert.Empty(_c.Gateway.Estornos);
        Assert.DoesNotContain("devolvido", Assert.Single(_c.Email.Enviados).Corpo);
    }
}
