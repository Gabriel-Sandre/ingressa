using Ingressa.Domain.Comum;
using Ingressa.Domain.Pedidos;
using static Ingressa.Domain.Tests.Construtores;

namespace Ingressa.Domain.Tests;

public class PedidoTests
{
    private static Pedido NovoPedido(out Eventos.Evento evento, params (int, int)[] itens)
    {
        evento = EventoPublicado(("Pista", 100m, 50), ("VIP", 250m, 10));
        var pedido = Pedido.Reservar(usuarioId: 9, evento, itens.Length == 0 ? [(1, 2)] : itens, Agora);
        DefinirId(pedido, 500);
        return pedido;
    }

    private static int _sequencia;
    private static string Codigo() => $"COD{Interlocked.Increment(ref _sequencia):D6}";

    [Fact]
    public void Reservar_DeveCriarItensComPrecoCongeladoEPrazo()
    {
        var pedido = NovoPedido(out _, (1, 2), (2, 1), (1, 1));

        Assert.Equal(StatusPedido.AguardandoPagamento, pedido.Status);
        Assert.Equal(2, pedido.Itens.Count);
        Assert.Equal(3, pedido.Itens.Single(i => i.SetorId == 1).Quantidade);
        Assert.Equal(550m, pedido.Total);
        Assert.Equal(Agora + Pedido.PrazoDaReserva, pedido.ExpiraEm);
        Assert.Equal("Pista", pedido.Itens[0].NomeSetor);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(0)]
    public void Reservar_DeveRespeitarLimiteDeIngressos(int quantidade)
    {
        var evento = EventoPublicado(("Pista", 100m, 50));

        Assert.Throws<RegraDeNegocioException>(() => Pedido.Reservar(1, evento, [(1, quantidade)], Agora));
    }

    [Fact]
    public void Reservar_DeveRecusarSetorDeOutroEvento()
    {
        var evento = EventoPublicado(("Pista", 100m, 50));

        Assert.Throws<NaoEncontradoException>(() => Pedido.Reservar(1, evento, [(99, 1)], Agora));
    }

    [Fact]
    public void Reservar_DeveRecusarEventoJaIniciado()
    {
        var evento = EventoPublicado(("Pista", 100m, 50));

        Assert.Throws<RegraDeNegocioException>(() => Pedido.Reservar(1, evento, [(1, 1)], evento.DataInicio));
    }

    [Fact]
    public void ConfirmarPagamento_DeveMudarStatusERegistrarEvento()
    {
        var pedido = NovoPedido(out _);

        pedido.ConfirmarPagamento("pay_1", Agora.AddMinutes(5));

        Assert.Equal(StatusPedido.Pago, pedido.Status);
        Assert.Equal("pay_1", pedido.CodigoPagamento);
        var evento = Assert.IsType<PedidoPago>(Assert.Single(pedido.Eventos));
        Assert.Equal(500, evento.PedidoId);
    }

    [Fact]
    public void ConfirmarPagamento_DepoisDoPrazoDeveFalhar()
    {
        var pedido = NovoPedido(out _);

        Assert.Throws<RegraDeNegocioException>(() => pedido.ConfirmarPagamento("pay_1", pedido.ExpiraEm));
        Assert.Equal(StatusPedido.AguardandoPagamento, pedido.Status);
    }

    [Fact]
    public void ConfirmarPagamento_DuasVezesDeveGerarConflito()
    {
        var pedido = NovoPedido(out _);
        pedido.ConfirmarPagamento("pay_1", Agora);

        Assert.Throws<ConflitoException>(() => pedido.ConfirmarPagamento("pay_2", Agora));
    }

    [Fact]
    public void TentarExpirar_SoExpiraReservaVencida()
    {
        var pedido = NovoPedido(out _);

        Assert.False(pedido.TentarExpirar(Agora.AddMinutes(9)));
        Assert.True(pedido.TentarExpirar(pedido.ExpiraEm));
        Assert.Equal(StatusPedido.Expirado, pedido.Status);
        Assert.False(pedido.TentarExpirar(pedido.ExpiraEm.AddHours(1)));
        Assert.IsType<PedidoExpirado>(Assert.Single(pedido.Eventos));
    }

    [Fact]
    public void TentarExpirar_NaoAfetaPedidoPago()
    {
        var pedido = NovoPedido(out _);
        pedido.ConfirmarPagamento("pay_1", Agora);

        Assert.False(pedido.TentarExpirar(Agora.AddDays(1)));
        Assert.Equal(StatusPedido.Pago, pedido.Status);
    }

    [Fact]
    public void Cancelar_ReservaPodeSerCanceladaAQualquerMomento()
    {
        var pedido = NovoPedido(out var evento);

        pedido.Cancelar(evento.DataInicio, evento.DataInicio.AddHours(-1));

        var cancelado = Assert.IsType<PedidoCancelado>(Assert.Single(pedido.Eventos));
        Assert.False(cancelado.EstavaPago);
    }

    [Fact]
    public void Cancelar_PedidoPagoRespeitaAntecedencia()
    {
        var pedido = NovoPedido(out var evento);
        pedido.ConfirmarPagamento("pay_1", Agora);
        pedido.LimparEventos();

        Assert.Throws<RegraDeNegocioException>(() => pedido.Cancelar(evento.DataInicio, evento.DataInicio.AddHours(-23)));

        pedido.Cancelar(evento.DataInicio, evento.DataInicio.AddHours(-25));
        Assert.True(Assert.IsType<PedidoCancelado>(Assert.Single(pedido.Eventos)).EstavaPago);
    }

    [Fact]
    public void Cancelar_PedidoEncerradoDeveGerarConflito()
    {
        var pedido = NovoPedido(out var evento);
        pedido.TentarExpirar(pedido.ExpiraEm);

        Assert.Throws<ConflitoException>(() => pedido.Cancelar(evento.DataInicio, Agora));
    }

    [Fact]
    public void EmitirIngressos_UmPorLugarEIdempotente()
    {
        var pedido = NovoPedido(out _, (1, 2), (2, 1));
        Assert.False(pedido.EmitirIngressos(Codigo, Agora)); // ainda não pago

        pedido.ConfirmarPagamento("pay_1", Agora);
        Assert.True(pedido.EmitirIngressos(Codigo, Agora));
        Assert.False(pedido.EmitirIngressos(Codigo, Agora)); // mensagem repetida

        Assert.Equal(3, pedido.Ingressos.Count);
        Assert.Equal(250m, pedido.Ingressos.Single(i => i.SetorId == 2).PrecoPago);
        Assert.Equal(3, pedido.Ingressos.Select(i => i.Codigo).Distinct().Count());
        Assert.Single(pedido.Eventos.OfType<IngressosEmitidos>());
    }

    [Fact]
    public void GarantirQuePertenceA_OutroUsuarioRecebeNaoEncontrado()
    {
        var pedido = NovoPedido(out _);

        Assert.Throws<NaoEncontradoException>(() => pedido.GarantirQuePertenceA(10));
    }
}
