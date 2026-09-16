using Ingressa.Application.Eventos;
using Ingressa.Domain.Comum;

namespace Ingressa.Application.Tests;

public class EventoServiceTests
{
    private readonly Cenario _c = new();

    private EventoRequest Request() => new("Novo Show", null, "Arena", "Rio", _c.Relogio.Agora.AddDays(5));

    [Fact]
    public async Task Criar_OrganizadorPendenteNaoPodeCriar()
    {
        var pendente = await _c.OrganizadorAsync(aprovado: false);

        var erro = await Assert.ThrowsAsync<AcessoNegadoException>(() => _c.Eventos.CriarAsync(pendente.Id, Request(), default));
        Assert.Contains("aguardando aprovação", erro.Message);
    }

    [Fact]
    public async Task Criar_DepoisDaAprovacaoFunciona()
    {
        var pendente = await _c.OrganizadorAsync(aprovado: false);
        var admin = await _c.ClienteAsync("admin@teste.com");
        await _c.Admin.AprovarOrganizadorAsync(admin.Id, pendente.Id, default);

        var evento = await _c.Eventos.CriarAsync(pendente.Id, Request(), default);

        Assert.False(evento.Publicado);
    }

    [Fact]
    public async Task Obter_RascunhoSoParaODono()
    {
        var dono = await _c.OrganizadorAsync();
        var evento = await _c.Eventos.CriarAsync(dono.Id, Request(), default);

        await _c.Eventos.ObterAsync(evento.Id, dono.Id, default);
        await Assert.ThrowsAsync<NaoEncontradoException>(() => _c.Eventos.ObterAsync(evento.Id, null, default));
    }

    [Fact]
    public async Task Alterar_EventoDeOutroOrganizadorENegado()
    {
        var dono = await _c.OrganizadorAsync();
        var intruso = await _c.OrganizadorAsync(email: "intruso@teste.com");
        var evento = await _c.Eventos.CriarAsync(dono.Id, Request(), default);

        await Assert.ThrowsAsync<AcessoNegadoException>(() => _c.Eventos.AtualizarAsync(intruso.Id, evento.Id, Request(), default));
        await Assert.ThrowsAsync<AcessoNegadoException>(() => _c.Eventos.ExcluirAsync(intruso.Id, evento.Id, default));
    }

    [Fact]
    public async Task FluxoCompleto_CriarSetorPublicarEVitrine()
    {
        var dono = await _c.OrganizadorAsync();
        var evento = await _c.Eventos.CriarAsync(dono.Id, Request(), default);

        await _c.Eventos.AdicionarSetorAsync(dono.Id, evento.Id, new SetorRequest("Pista", 70m, 100), default);
        await _c.Eventos.PublicarAsync(dono.Id, evento.Id, default);

        var vitrine = await _c.Eventos.ListarVitrineAsync(new EventoFiltro(), default);
        var item = Assert.Single(vitrine.Itens);
        Assert.Equal(70m, item.PrecoAPartirDe);
    }

    [Fact]
    public async Task Excluir_EventoComReservaGeraConflito()
    {
        var cliente = await _c.ClienteAsync();
        var evento = await _c.EventoAsync();
        await _c.Pedidos.ReservarAsync(cliente.Id, Cenario.Pedido(evento, (0, 1)), default);

        await Assert.ThrowsAsync<ConflitoException>(() => _c.Eventos.ExcluirAsync(evento.OrganizadorId, evento.Id, default));
    }
}
