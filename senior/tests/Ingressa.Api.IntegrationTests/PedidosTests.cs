using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ingressa.Application.Abstracoes;
using Ingressa.Application.Pedidos;
using Ingressa.Domain.Pedidos;
using Ingressa.Domain.Usuarios;
using Ingressa.Infrastructure.Outbox;
using Ingressa.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ingressa.Api.IntegrationTests;

[Collection(ColecaoApi.Nome)]
public sealed class PedidosTests(ApiFactory api)
{
    [Fact]
    public async Task ComprasSimultaneas_NuncaVendemMaisQueACapacidade()
    {
        const int capacidade = 5;
        const int compradores = 40;
        var (_, organizadorId) = await api.EntrarComoAsync(PerfilUsuario.Organizador);
        var (eventoId, setorId) = await api.CriarEventoAsync(organizadorId, capacidade);

        var clientes = new List<HttpClient>();
        for (var i = 0; i < compradores; i++)
        {
            clientes.Add((await api.EntrarComoAsync(PerfilUsuario.Cliente)).Cliente);
        }

        // Todos disparam ao mesmo tempo.
        var largada = new TaskCompletionSource();
        var tentativas = clientes.Select(async cliente =>
        {
            await largada.Task;
            var resposta = await cliente.PostIdempotenteAsync("/api/pedidos",
                new CriarPedidoRequest(eventoId, [new ItemPedidoRequest(setorId, 1)]));
            return resposta.StatusCode;
        }).ToList();
        largada.SetResult();
        var status = await Task.WhenAll(tentativas);

        Assert.Equal(capacidade, status.Count(s => s == HttpStatusCode.Created));
        Assert.Equal(compradores - capacidade, status.Count(s => s == HttpStatusCode.Conflict));

        var ocupados = await api.NoEscopoAsync(sp =>
            sp.GetRequiredService<IngressaDbContext>().Setores.Where(s => s.Id == setorId).Select(s => s.Ocupados).SingleAsync());
        Assert.Equal(capacidade, ocupados);
    }

    [Fact]
    public async Task FluxoCompleto_ReservaPagamentoEmissao()
    {
        var (_, organizadorId) = await api.EntrarComoAsync(PerfilUsuario.Organizador);
        var (eventoId, setorId) = await api.CriarEventoAsync(organizadorId, 100);
        var (cliente, _) = await api.EntrarComoAsync(PerfilUsuario.Cliente);

        var reserva = await LerAsync(await cliente.PostIdempotenteAsync("/api/pedidos",
            new CriarPedidoRequest(eventoId, [new ItemPedidoRequest(setorId, 2)])), HttpStatusCode.Created);
        Assert.Equal(StatusPedido.AguardandoPagamento, reserva.Status);

        var pago = await LerAsync(await cliente.PostIdempotenteAsync($"/api/pedidos/{reserva.Id}/pagamento",
            new PagamentoRequest("tok_aprovado")), HttpStatusCode.OK);
        Assert.Equal(StatusPedido.Pago, pago.Status);
        Assert.False(pago.IngressosEmitidos);

        // O Worker faria isto ao receber a mensagem "pedido.pago".
        await api.NoEscopoAsync(sp => ActivatorUtilities
            .CreateInstance<ProcessamentoDePedidosService>(sp, new EmailNulo())
            .EmitirIngressosAsync(reserva.Id, default));

        var final = await cliente.GetFromJsonAsync<PedidoResponse>($"/api/pedidos/{reserva.Id}", Json.Opcoes);
        Assert.Equal(2, final!.Ingressos.Count);

        var mensagens = await api.NoEscopoAsync(sp => sp.GetRequiredService<IngressaDbContext>().MensagensOutbox
            .Select(m => new { m.Tipo, m.Conteudo }).ToListAsync());
        var tipos = mensagens
            .Where(m => JsonDocument.Parse(m.Conteudo).RootElement.GetProperty("pedidoId").GetInt32() == reserva.Id)
            .Select(m => m.Tipo)
            .ToList();
        Assert.Contains(CatalogoDeMensagens.PedidoPago, tipos);
        Assert.Contains(CatalogoDeMensagens.IngressosEmitidos, tipos);
    }

    [Fact]
    public async Task Pagamento_RecusadoRetorna422()
    {
        var (_, organizadorId) = await api.EntrarComoAsync(PerfilUsuario.Organizador);
        var (eventoId, setorId) = await api.CriarEventoAsync(organizadorId, 10);
        var (cliente, _) = await api.EntrarComoAsync(PerfilUsuario.Cliente);
        var reserva = await LerAsync(await cliente.PostIdempotenteAsync("/api/pedidos",
            new CriarPedidoRequest(eventoId, [new ItemPedidoRequest(setorId, 1)])), HttpStatusCode.Created);

        var resposta = await cliente.PostIdempotenteAsync($"/api/pedidos/{reserva.Id}/pagamento", new PagamentoRequest("tok_recusado"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
    }

    [Fact]
    public async Task PedidoDeOutroCliente_Retorna404()
    {
        var (_, organizadorId) = await api.EntrarComoAsync(PerfilUsuario.Organizador);
        var (eventoId, setorId) = await api.CriarEventoAsync(organizadorId, 10);
        var (dono, _) = await api.EntrarComoAsync(PerfilUsuario.Cliente);
        var (outro, _) = await api.EntrarComoAsync(PerfilUsuario.Cliente);
        var reserva = await LerAsync(await dono.PostIdempotenteAsync("/api/pedidos",
            new CriarPedidoRequest(eventoId, [new ItemPedidoRequest(setorId, 1)])), HttpStatusCode.Created);

        Assert.Equal(HttpStatusCode.NotFound, (await outro.GetAsync($"/api/pedidos/{reserva.Id}")).StatusCode);
    }

    [Fact]
    public async Task ConcorrenciaOtimista_PagarEExpirarAoMesmoTempo()
    {
        var (_, organizadorId) = await api.EntrarComoAsync(PerfilUsuario.Organizador);
        var (eventoId, setorId) = await api.CriarEventoAsync(organizadorId, 10);
        var (cliente, _) = await api.EntrarComoAsync(PerfilUsuario.Cliente);
        var reserva = await LerAsync(await cliente.PostIdempotenteAsync("/api/pedidos",
            new CriarPedidoRequest(eventoId, [new ItemPedidoRequest(setorId, 1)])), HttpStatusCode.Created);

        // Duas operações leem o mesmo pedido (mesmo xmin)...
        await using var escopoA = api.Services.CreateAsyncScope();
        await using var escopoB = api.Services.CreateAsyncScope();
        var dbA = escopoA.ServiceProvider.GetRequiredService<IngressaDbContext>();
        var dbB = escopoB.ServiceProvider.GetRequiredService<IngressaDbContext>();
        var pedidoA = await dbA.Pedidos.SingleAsync(p => p.Id == reserva.Id);
        var pedidoB = await dbB.Pedidos.SingleAsync(p => p.Id == reserva.Id);

        // ...a primeira paga e grava...
        pedidoA.ConfirmarPagamento("pay_teste", DateTime.UtcNow);
        await dbA.SaveChangesAsync();

        // ...a segunda tenta expirar com a versão antiga e é recusada pelo banco.
        Assert.True(pedidoB.TentarExpirar(pedidoB.ExpiraEm));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());

        var status = await api.NoEscopoAsync(sp =>
            sp.GetRequiredService<IngressaDbContext>().Pedidos.Where(p => p.Id == reserva.Id).Select(p => p.Status).SingleAsync());
        Assert.Equal(StatusPedido.Pago, status);
    }

    private static async Task<PedidoResponse> LerAsync(HttpResponseMessage resposta, HttpStatusCode esperado)
    {
        Assert.Equal(esperado, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<PedidoResponse>(Json.Opcoes))!;
    }
}

internal sealed class EmailNulo : IEnviadorDeEmail
{
    public Task EnviarAsync(string para, string assunto, string corpoTexto, CancellationToken ct) => Task.CompletedTask;
}
