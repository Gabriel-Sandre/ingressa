using Ingressa.Application.Abstracoes;
using Ingressa.Domain.Comum;

namespace Ingressa.Application.Tests;

public class FilaVirtualTests
{
    private readonly Cenario _c = new();

    [Fact]
    public async Task Entrar_EventoSemFilaEhRecusado()
    {
        var cliente = await _c.ClienteAsync();
        var evento = await _c.EventoAsync(filaVirtual: false);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _c.FilaVirtual.EntrarAsync(evento.Id, cliente.Id, default));
    }

    [Fact]
    public async Task Entrar_RespeitaOrdemDeChegada()
    {
        var evento = await _c.EventoAsync(filaVirtual: true);
        var a = await _c.ClienteAsync("a@teste.com");
        var b = await _c.ClienteAsync("b@teste.com");

        var primeiro = await _c.FilaVirtual.EntrarAsync(evento.Id, a.Id, default);
        var segundo = await _c.FilaVirtual.EntrarAsync(evento.Id, b.Id, default);
        var denovo = await _c.FilaVirtual.EntrarAsync(evento.Id, a.Id, default);

        Assert.Equal((SituacaoNaFila.Aguardando, 1L), (primeiro.Situacao, primeiro.Posicao));
        Assert.Equal(2L, segundo.Posicao);
        Assert.Equal(1L, denovo.Posicao);
    }

    [Fact]
    public async Task Reservar_EventoComFilaExigePasse()
    {
        var evento = await _c.EventoAsync(filaVirtual: true);
        var cliente = await _c.ClienteAsync();

        await Assert.ThrowsAsync<AcessoNegadoException>(() =>
            _c.Pedidos.ReservarAsync(cliente.Id, Cenario.Pedido(evento, (0, 1)), null, default));
        await Assert.ThrowsAsync<AcessoNegadoException>(() =>
            _c.Pedidos.ReservarAsync(cliente.Id, Cenario.Pedido(evento, (0, 1)), "inventado", default));
        Assert.Equal(0, evento.Setores[0].Ocupados);
    }

    [Fact]
    public async Task FluxoCompleto_FilaLiberaReservaEAbreVaga()
    {
        var evento = await _c.EventoAsync(filaVirtual: true);
        var clientes = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            var c = await _c.ClienteAsync($"c{i}@teste.com");
            clientes.Add(c.Id);
            await _c.FilaVirtual.EntrarAsync(evento.Id, c.Id, default);
        }

        // Limite de 2 compradores simultâneos.
        Assert.Equal(2, await _c.FilaVirtual.AdmitirEmTodasAsync(default));
        var terceiro = await _c.FilaVirtual.ConsultarAsync(evento.Id, clientes[2], default);
        Assert.Equal((SituacaoNaFila.Aguardando, 1L), (terceiro.Situacao, terceiro.Posicao));

        var liberado = await _c.FilaVirtual.ConsultarAsync(evento.Id, clientes[0], default);
        Assert.Equal(SituacaoNaFila.Liberado, liberado.Situacao);

        await _c.Pedidos.ReservarAsync(clientes[0], Cenario.Pedido(evento, (0, 1)), liberado.Passe, default);

        // O passe é de uso único...
        await Assert.ThrowsAsync<AcessoNegadoException>(() =>
            _c.Pedidos.ReservarAsync(clientes[0], Cenario.Pedido(evento, (0, 1)), liberado.Passe, default));

        // ...e a reserva concluída libera a vaga para o terceiro.
        Assert.Equal(1, await _c.FilaVirtual.AdmitirEmTodasAsync(default));
        Assert.Equal(SituacaoNaFila.Liberado, (await _c.FilaVirtual.ConsultarAsync(evento.Id, clientes[2], default)).Situacao);
    }

    [Fact]
    public async Task Reservar_SeFalharOPasseVoltaAValer()
    {
        var evento = await _c.EventoAsync(capacidadeVip: 1, filaVirtual: true);
        var cliente = await _c.ClienteAsync();
        await _c.FilaVirtual.EntrarAsync(evento.Id, cliente.Id, default);
        await _c.FilaVirtual.AdmitirEmTodasAsync(default);
        var passe = (await _c.FilaVirtual.ConsultarAsync(evento.Id, cliente.Id, default)).Passe;

        // Pede 2 VIPs, mas só existe 1.
        await Assert.ThrowsAsync<ConflitoException>(() =>
            _c.Pedidos.ReservarAsync(cliente.Id, Cenario.Pedido(evento, (1, 2)), passe, default));

        // Com o mesmo passe, tenta outra combinação e consegue.
        var pedido = await _c.Pedidos.ReservarAsync(cliente.Id, Cenario.Pedido(evento, (1, 1)), passe, default);
        Assert.Single(pedido.Itens);
        Assert.Equal(0, _c.Fila.CompradoresAtivos(evento.Id));
    }

    [Fact]
    public async Task AlterarEvento_InvalidaOCache()
    {
        var organizador = await _c.OrganizadorAsync();
        var evento = await _c.Eventos.CriarAsync(organizador.Id,
            new Eventos.EventoRequest("Show", null, "Arena", "Rio", _c.Relogio.Agora.AddDays(5), FilaVirtual: true), default);

        await _c.Eventos.AdicionarSetorAsync(organizador.Id, evento.Id, new Eventos.SetorRequest("Pista", 10m, 10), default);
        await _c.Eventos.PublicarAsync(organizador.Id, evento.Id, default);

        Assert.True(evento.FilaVirtual);
        Assert.Equal(3, _c.Fila.EventosInvalidados.Count(id => id == evento.Id));
    }
}
