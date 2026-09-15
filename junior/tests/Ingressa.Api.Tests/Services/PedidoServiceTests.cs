using Ingressa.Api.Dtos;
using Ingressa.Api.Erros;
using Ingressa.Api.Models;
using Ingressa.Api.Services;
using Ingressa.Api.Tests.Infra;

namespace Ingressa.Api.Tests.Services;

public sealed class PedidoServiceTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly RelogioFixo _relogio = new();

    private PedidoService CriarServico(Ingressa.Api.Data.IngressaDbContext db) => new(db, _relogio);

    /// <summary>Cenário padrão: um evento com Pista (R$ 100, 10 lugares) e VIP (R$ 250, 2 lugares, 1 vendido).</summary>
    private async Task<(Usuario Cliente, Evento Evento, int Pista, int Vip)> CenarioAsync(bool publicado = true, int diasAFrente = 10)
    {
        var org = await _banco.CriarUsuarioAsync(PerfilUsuario.Organizador, "org@teste.com");
        var cliente = await _banco.CriarUsuarioAsync(PerfilUsuario.Cliente, "cliente@teste.com");
        var evento = await _banco.CriarEventoAsync(
            org.Id, [("Pista", 100m, 10, 0), ("VIP", 250m, 2, 1)], publicado: publicado, diasAFrente: diasAFrente);
        return (cliente, evento, evento.Setores[0].Id, evento.Setores[1].Id);
    }

    private static CriarPedidoRequest Pedido(int eventoId, params (int SetorId, int Quantidade)[] itens) =>
        new(eventoId, itens.Select(i => new ItemPedidoRequest(i.SetorId, i.Quantidade)).ToList());

    [Fact]
    public async Task Criar_DeveEmitirIngressosCalcularTotalEBaixarEstoque()
    {
        var (cliente, evento, pista, vip) = await CenarioAsync();

        PedidoResponse pedido;
        await using (var db = _banco.NovoContexto())
        {
            pedido = await CriarServico(db).CriarAsync(cliente.Id, Pedido(evento.Id, (pista, 2), (vip, 1)));
        }

        Assert.Equal(StatusPedido.Confirmado, pedido.Status);
        Assert.Equal(450m, pedido.Total);
        Assert.Equal(3, pedido.Ingressos.Count);
        Assert.Equal(3, pedido.Ingressos.Select(i => i.Codigo).Distinct().Count());
        Assert.All(pedido.Ingressos, i => Assert.Equal(16, i.Codigo.Length));

        Assert.Equal(2, (await _banco.ObterSetorAsync(pista)).Vendidos);
        Assert.Equal(2, (await _banco.ObterSetorAsync(vip)).Vendidos);
    }

    [Fact]
    public async Task Criar_DeveSomarItensRepetidosDoMesmoSetor()
    {
        var (cliente, evento, pista, _) = await CenarioAsync();

        await using (var db = _banco.NovoContexto())
        {
            var pedido = await CriarServico(db).CriarAsync(cliente.Id, Pedido(evento.Id, (pista, 1), (pista, 2)));
            Assert.Equal(3, pedido.Ingressos.Count);
        }

        Assert.Equal(3, (await _banco.ObterSetorAsync(pista)).Vendidos);
    }

    [Fact]
    public async Task Criar_DeveRecusarQuantidadeMaiorQueADisponivelSemAlterarEstoque()
    {
        var (cliente, evento, pista, vip) = await CenarioAsync();

        await using (var db = _banco.NovoContexto())
        {
            var erro = await Assert.ThrowsAsync<ConflitoException>(() =>
                CriarServico(db).CriarAsync(cliente.Id, Pedido(evento.Id, (pista, 1), (vip, 2))));
            Assert.Contains("apenas 1", erro.Message);
        }

        // Nada foi gravado, nem mesmo o item válido (Pista).
        Assert.Equal(0, (await _banco.ObterSetorAsync(pista)).Vendidos);
        Assert.Equal(1, (await _banco.ObterSetorAsync(vip)).Vendidos);
    }

    [Fact]
    public async Task Criar_DeveInformarSetorEsgotado()
    {
        var (cliente, evento, _, vip) = await CenarioAsync();
        await using (var db = _banco.NovoContexto())
        {
            await CriarServico(db).CriarAsync(cliente.Id, Pedido(evento.Id, (vip, 1)));
        }

        await using var db2 = _banco.NovoContexto();
        var erro = await Assert.ThrowsAsync<ConflitoException>(() =>
            CriarServico(db2).CriarAsync(cliente.Id, Pedido(evento.Id, (vip, 1))));
        Assert.Contains("esgotado", erro.Message);
    }

    [Fact]
    public async Task Criar_DeveRespeitarOLimiteDeIngressosPorPedido()
    {
        var (cliente, evento, pista, _) = await CenarioAsync();

        await using var db = _banco.NovoContexto();
        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            CriarServico(db).CriarAsync(cliente.Id, Pedido(evento.Id, (pista, 4), (pista, 3))));
    }

    [Fact]
    public async Task Criar_DeveRecusarEventoNaoPublicado()
    {
        var (cliente, evento, pista, _) = await CenarioAsync(publicado: false);

        await using var db = _banco.NovoContexto();
        await Assert.ThrowsAsync<NaoEncontradoException>(() =>
            CriarServico(db).CriarAsync(cliente.Id, Pedido(evento.Id, (pista, 1))));
    }

    [Fact]
    public async Task Criar_DeveRecusarEventoQueJaComecou()
    {
        var (cliente, evento, pista, _) = await CenarioAsync(diasAFrente: 1);
        _relogio.Avancar(TimeSpan.FromDays(2));

        await using var db = _banco.NovoContexto();
        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            CriarServico(db).CriarAsync(cliente.Id, Pedido(evento.Id, (pista, 1))));
    }

    [Fact]
    public async Task Criar_DeveRecusarSetorDeOutroEvento()
    {
        var (cliente, evento, _, _) = await CenarioAsync();
        var org2 = await _banco.CriarUsuarioAsync(PerfilUsuario.Organizador, "org2@teste.com");
        var outro = await _banco.CriarEventoAsync(org2.Id, [("Outro setor", 10m, 10, 0)], titulo: "Outro");

        await using var db = _banco.NovoContexto();
        await Assert.ThrowsAsync<NaoEncontradoException>(() =>
            CriarServico(db).CriarAsync(cliente.Id, Pedido(evento.Id, (outro.Setores[0].Id, 1))));
    }

    [Fact]
    public async Task Criar_DeveGuardarOPrecoPagoMesmoSeOSetorMudarDepois()
    {
        var (cliente, evento, pista, _) = await CenarioAsync();
        int pedidoId;
        await using (var db = _banco.NovoContexto())
        {
            pedidoId = (await CriarServico(db).CriarAsync(cliente.Id, Pedido(evento.Id, (pista, 1)))).Id;
        }

        await using (var db = _banco.NovoContexto())
        {
            var setor = await db.Setores.FindAsync(pista);
            setor!.Preco = 999m;
            await db.SaveChangesAsync();
        }

        await using var leitura = _banco.NovoContexto();
        var pedido = await CriarServico(leitura).ObterAsync(cliente.Id, pedidoId);
        Assert.Equal(100m, pedido.Total);
        Assert.Equal(100m, Assert.Single(pedido.Ingressos).PrecoPago);
    }

    [Fact]
    public async Task Obter_PedidoDeOutroClienteDeveResponderNaoEncontrado()
    {
        var (cliente, evento, pista, _) = await CenarioAsync();
        var outroCliente = await _banco.CriarUsuarioAsync(PerfilUsuario.Cliente, "outro@teste.com");
        int pedidoId;
        await using (var db = _banco.NovoContexto())
        {
            pedidoId = (await CriarServico(db).CriarAsync(cliente.Id, Pedido(evento.Id, (pista, 1)))).Id;
        }

        await using var leitura = _banco.NovoContexto();
        await Assert.ThrowsAsync<NaoEncontradoException>(() =>
            CriarServico(leitura).ObterAsync(outroCliente.Id, pedidoId));
    }

    [Fact]
    public async Task Listar_DeveTrazerSomenteOsPedidosDoCliente()
    {
        var (cliente, evento, pista, _) = await CenarioAsync();
        var outroCliente = await _banco.CriarUsuarioAsync(PerfilUsuario.Cliente, "outro@teste.com");
        await using (var db = _banco.NovoContexto())
        {
            var servico = CriarServico(db);
            await servico.CriarAsync(cliente.Id, Pedido(evento.Id, (pista, 1)));
            await servico.CriarAsync(outroCliente.Id, Pedido(evento.Id, (pista, 1)));
            await servico.CriarAsync(cliente.Id, Pedido(evento.Id, (pista, 2)));
        }

        await using var leitura = _banco.NovoContexto();
        var pedidos = await CriarServico(leitura).ListarDoUsuarioAsync(cliente.Id);

        Assert.Equal(2, pedidos.Count);
        Assert.Equal(2, pedidos[0].Ingressos.Count); // mais recente primeiro
        Assert.Equal("Pista", pedidos[0].Ingressos[0].Setor);
    }

    [Fact]
    public async Task Cancelar_DeveDevolverOsIngressosAoEstoque()
    {
        var (cliente, evento, pista, vip) = await CenarioAsync();
        int pedidoId;
        await using (var db = _banco.NovoContexto())
        {
            pedidoId = (await CriarServico(db).CriarAsync(cliente.Id, Pedido(evento.Id, (pista, 3), (vip, 1)))).Id;
        }

        await using (var db = _banco.NovoContexto())
        {
            var cancelado = await CriarServico(db).CancelarAsync(cliente.Id, pedidoId);
            Assert.Equal(StatusPedido.Cancelado, cancelado.Status);
        }

        Assert.Equal(0, (await _banco.ObterSetorAsync(pista)).Vendidos);
        Assert.Equal(1, (await _banco.ObterSetorAsync(vip)).Vendidos);
    }

    [Fact]
    public async Task Cancelar_NaoPodeCancelarDuasVezes()
    {
        var (cliente, evento, pista, _) = await CenarioAsync();
        int pedidoId;
        await using (var db = _banco.NovoContexto())
        {
            var servico = CriarServico(db);
            pedidoId = (await servico.CriarAsync(cliente.Id, Pedido(evento.Id, (pista, 1)))).Id;
            await servico.CancelarAsync(cliente.Id, pedidoId);
        }

        await using var db2 = _banco.NovoContexto();
        await Assert.ThrowsAsync<ConflitoException>(() => CriarServico(db2).CancelarAsync(cliente.Id, pedidoId));
        Assert.Equal(0, (await _banco.ObterSetorAsync(pista)).Vendidos);
    }

    [Fact]
    public async Task Cancelar_DeveRecusarAMenosDe24HorasDoEvento()
    {
        var (cliente, evento, pista, _) = await CenarioAsync(diasAFrente: 2);
        int pedidoId;
        await using (var db = _banco.NovoContexto())
        {
            pedidoId = (await CriarServico(db).CriarAsync(cliente.Id, Pedido(evento.Id, (pista, 1)))).Id;
        }

        _relogio.Avancar(TimeSpan.FromHours(25)); // faltam 23 horas

        await using var db2 = _banco.NovoContexto();
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => CriarServico(db2).CancelarAsync(cliente.Id, pedidoId));
    }

    public void Dispose() => _banco.Dispose();
}
