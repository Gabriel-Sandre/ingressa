using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Ingressa.Api.Controllers;
using Ingressa.Application.Abstracoes;
using Ingressa.Application.Eventos;
using Ingressa.Application.Fila;
using Ingressa.Application.Pedidos;
using Ingressa.Domain.Eventos;
using Ingressa.Domain.Usuarios;
using Ingressa.Infrastructure.Idempotencia;
using Ingressa.Infrastructure.Outbox;
using Ingressa.Infrastructure.Manutencao;
using Ingressa.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ingressa.Api.IntegrationTests;

[Collection(ColecaoApi.Nome)]
public sealed class FilaVirtualIntegracaoTests(ApiFactory api)
{
    private async Task<(int EventoId, int SetorId)> EventoComFilaAsync(int capacidade = 100)
    {
        var (_, organizadorId) = await api.EntrarComoAsync(PerfilUsuario.Organizador);
        return await api.NoEscopoAsync(async sp =>
        {
            var db = sp.GetRequiredService<IngressaDbContext>();
            var agora = DateTime.UtcNow;
            var evento = Evento.Criar(organizadorId,
                new DadosDoEvento("Show Concorrido", null, "Estádio", "Rio", agora.AddDays(30), FilaVirtual: true), agora);
            evento.AdicionarSetor("Pista", 100m, capacidade);
            evento.Publicar(agora);
            db.Eventos.Add(evento);
            await db.SaveChangesAsync();
            return (evento.Id, evento.Setores[0].Id);
        });
    }

    private Task<int> AdmitirAsync() =>
        api.NoEscopoAsync(sp => sp.GetRequiredService<FilaVirtualService>().AdmitirEmTodasAsync(default));

    [Fact]
    public async Task SemPasse_ReservaEhNegada()
    {
        var (eventoId, setorId) = await EventoComFilaAsync();
        var (cliente, _) = await api.EntrarComoAsync(PerfilUsuario.Cliente);

        var resposta = await cliente.PostIdempotenteAsync("/api/pedidos",
            new CriarPedidoRequest(eventoId, [new ItemPedidoRequest(setorId, 1)]));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task FluxoCompleto_FilaPassesEReserva()
    {
        var (eventoId, setorId) = await EventoComFilaAsync();
        var clientes = new List<HttpClient>();
        for (var i = 0; i < 3; i++)
        {
            var (cliente, _) = await api.EntrarComoAsync(PerfilUsuario.Cliente);
            clientes.Add(cliente);
            var entrada = await cliente.PostAsync($"/api/eventos/{eventoId}/fila", null);
            var fila = await entrada.Content.ReadFromJsonAsync<FilaResponse>(Json.Opcoes);
            Assert.Equal(SituacaoNaFila.Aguardando, fila!.Situacao);
            Assert.Equal(i + 1, fila.Posicao);
        }

        Assert.True(await AdmitirAsync() >= 3);

        foreach (var cliente in clientes)
        {
            var fila = await cliente.GetFromJsonAsync<FilaResponse>($"/api/eventos/{eventoId}/fila", Json.Opcoes);
            Assert.Equal(SituacaoNaFila.Liberado, fila!.Situacao);

            var reserva = await cliente.PostIdempotenteAsync("/api/pedidos",
                new CriarPedidoRequest(eventoId, [new ItemPedidoRequest(setorId, 1)]), passe: fila.Passe);
            Assert.Equal(HttpStatusCode.Created, reserva.StatusCode);

            // Uso único.
            var repetida = await cliente.PostIdempotenteAsync("/api/pedidos",
                new CriarPedidoRequest(eventoId, [new ItemPedidoRequest(setorId, 1)]), passe: fila.Passe);
            Assert.Equal(HttpStatusCode.Forbidden, repetida.StatusCode);
        }
    }

    [Fact]
    public async Task EntradasSimultaneas_RecebemPosicoesUnicas()
    {
        var (eventoId, _) = await EventoComFilaAsync();
        var clientes = new List<HttpClient>();
        for (var i = 0; i < 30; i++)
        {
            clientes.Add((await api.EntrarComoAsync(PerfilUsuario.Cliente)).Cliente);
        }

        var respostas = await Task.WhenAll(clientes.Select(async c =>
        {
            var r = await c.PostAsync($"/api/eventos/{eventoId}/fila", null);
            return (await r.Content.ReadFromJsonAsync<FilaResponse>(Json.Opcoes))!.Posicao;
        }));

        Assert.Equal(Enumerable.Range(1, 30).Select(p => (long)p), respostas.Order());
    }

    [Fact]
    public async Task EventoSemFila_Retorna422()
    {
        var (_, organizadorId) = await api.EntrarComoAsync(PerfilUsuario.Organizador);
        var (eventoId, _) = await api.CriarEventoAsync(organizadorId, 10);
        var (cliente, _) = await api.EntrarComoAsync(PerfilUsuario.Cliente);

        var resposta = await cliente.PostAsync($"/api/eventos/{eventoId}/fila", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
    }
}

[Collection(ColecaoApi.Nome)]
public sealed class IdempotenciaTests(ApiFactory api)
{
    [Fact]
    public async Task MesmaChave_DevolveAMesmaRespostaSemReservarDeNovo()
    {
        var (_, organizadorId) = await api.EntrarComoAsync(PerfilUsuario.Organizador);
        var (eventoId, setorId) = await api.CriarEventoAsync(organizadorId, 10);
        var (cliente, _) = await api.EntrarComoAsync(PerfilUsuario.Cliente);
        var corpo = new CriarPedidoRequest(eventoId, [new ItemPedidoRequest(setorId, 2)]);
        var chave = Guid.NewGuid().ToString();

        var primeira = await cliente.PostIdempotenteAsync("/api/pedidos", corpo, chave);
        var segunda = await cliente.PostIdempotenteAsync("/api/pedidos", corpo, chave);

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.Created, segunda.StatusCode);
        Assert.Equal("true", segunda.Headers.GetValues("Idempotent-Replayed").Single());
        var p1 = await primeira.Content.ReadFromJsonAsync<PedidoResponse>(Json.Opcoes);
        var p2 = await segunda.Content.ReadFromJsonAsync<PedidoResponse>(Json.Opcoes);
        Assert.Equal(p1!.Id, p2!.Id);

        var ocupados = await api.NoEscopoAsync(sp => sp.GetRequiredService<IngressaDbContext>()
            .Setores.Where(s => s.Id == setorId).Select(s => s.Ocupados).SingleAsync());
        Assert.Equal(2, ocupados);
    }

    [Fact]
    public async Task CliquesSimultaneos_CriamUmUnicoPedido()
    {
        var (_, organizadorId) = await api.EntrarComoAsync(PerfilUsuario.Organizador);
        var (eventoId, setorId) = await api.CriarEventoAsync(organizadorId, 10);
        var (cliente, usuarioId) = await api.EntrarComoAsync(PerfilUsuario.Cliente);
        var corpo = new CriarPedidoRequest(eventoId, [new ItemPedidoRequest(setorId, 1)]);
        var chave = Guid.NewGuid().ToString();

        var status = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => cliente.PostIdempotenteAsync("/api/pedidos", corpo, chave)));

        Assert.All(status, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict));
        var pedidos = await api.NoEscopoAsync(sp => sp.GetRequiredService<IngressaDbContext>()
            .Pedidos.CountAsync(p => p.UsuarioId == usuarioId));
        Assert.Equal(1, pedidos);
    }

    [Fact]
    public async Task MesmaChaveComOutroConteudo_Retorna422()
    {
        var (_, organizadorId) = await api.EntrarComoAsync(PerfilUsuario.Organizador);
        var (eventoId, setorId) = await api.CriarEventoAsync(organizadorId, 10);
        var (cliente, _) = await api.EntrarComoAsync(PerfilUsuario.Cliente);
        var chave = Guid.NewGuid().ToString();

        await cliente.PostIdempotenteAsync("/api/pedidos", new CriarPedidoRequest(eventoId, [new ItemPedidoRequest(setorId, 1)]), chave);
        var outra = await cliente.PostIdempotenteAsync("/api/pedidos", new CriarPedidoRequest(eventoId, [new ItemPedidoRequest(setorId, 3)]), chave);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, outra.StatusCode);
    }

    [Fact]
    public async Task SemChave_Retorna400()
    {
        var (cliente, _) = await api.EntrarComoAsync(PerfilUsuario.Cliente);

        var resposta = await cliente.PostAsJsonAsync("/api/pedidos", new CriarPedidoRequest(1, [new ItemPedidoRequest(1, 1)]));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task ChaveAbandonadaPorQuedaDoProcesso_PodeSerRetomada()
    {
        var (_, usuarioId) = await api.EntrarComoAsync(PerfilUsuario.Cliente);
        var chave = Guid.NewGuid().ToString();

        // Simula um processo que registrou a chave e caiu antes de concluir.
        var primeira = await api.NoEscopoAsync(sp => sp.GetRequiredService<ControleDeIdempotencia>()
            .ReivindicarAsync(usuarioId, chave, "POST /api/pedidos", "hash", default));
        var logoDepois = await api.NoEscopoAsync(sp => sp.GetRequiredService<ControleDeIdempotencia>()
            .ReivindicarAsync(usuarioId, chave, "POST /api/pedidos", "hash", default));

        ReivindicacaoDeChave retomada;
        using (api.Relogio.Adiantar(TimeSpan.FromMinutes(3)))
        {
            retomada = await api.NoEscopoAsync(sp => sp.GetRequiredService<ControleDeIdempotencia>()
                .ReivindicarAsync(usuarioId, chave, "POST /api/pedidos", "hash", default));
        }

        Assert.Equal(SituacaoDaChave.Nova, primeira.Situacao);
        Assert.Equal(SituacaoDaChave.EmAndamento, logoDepois.Situacao);
        Assert.Equal(SituacaoDaChave.Nova, retomada.Situacao);
        Assert.Equal(primeira.Id, retomada.Id);
    }

    [Fact]
    public async Task FalhaNaoFicaGuardada_ClientePodeTentarDeNovo()
    {
        var (_, organizadorId) = await api.EntrarComoAsync(PerfilUsuario.Organizador);
        var (eventoId, setorId) = await api.CriarEventoAsync(organizadorId, 1);
        var (cliente, _) = await api.EntrarComoAsync(PerfilUsuario.Cliente);
        var chave = Guid.NewGuid().ToString();

        var semEstoque = await cliente.PostIdempotenteAsync("/api/pedidos",
            new CriarPedidoRequest(eventoId, [new ItemPedidoRequest(setorId, 2)]), chave);
        Assert.Equal(HttpStatusCode.Conflict, semEstoque.StatusCode);

        // Mesma chave, conteúdo corrigido: a chave foi liberada e a nova tentativa funciona.
        var corrigida = await cliente.PostIdempotenteAsync("/api/pedidos",
            new CriarPedidoRequest(eventoId, [new ItemPedidoRequest(setorId, 1)]), chave);
        Assert.Equal(HttpStatusCode.Created, corrigida.StatusCode);
    }
}

[Collection(ColecaoApi.Nome)]
public sealed class OperacaoTests(ApiFactory api)
{
    [Fact]
    public async Task HealthCheck_IncluiRedis()
    {
        var resposta = await api.NovoCliente().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Fact]
    public async Task ReservaGravaOTraceNaOutbox_ParaContinuarNoWorker()
    {
        var (_, organizadorId) = await api.EntrarComoAsync(PerfilUsuario.Organizador);
        var (eventoId, setorId) = await api.CriarEventoAsync(organizadorId, 10);
        var (cliente, _) = await api.EntrarComoAsync(PerfilUsuario.Cliente);

        var resposta = await cliente.PostIdempotenteAsync("/api/pedidos",
            new CriarPedidoRequest(eventoId, [new ItemPedidoRequest(setorId, 1)]));
        var pedido = await resposta.Content.ReadFromJsonAsync<PedidoResponse>(Json.Opcoes);
        await cliente.PostIdempotenteAsync($"/api/pedidos/{pedido!.Id}/pagamento", new PagamentoRequest("tok_aprovado"));

        var correlacao = await api.NoEscopoAsync(sp => sp.GetRequiredService<IngressaDbContext>()
            .MensagensOutbox.AsNoTracking()
            .Where(m => m.Tipo == "pedido.pago")
            .OrderByDescending(m => m.Id)
            .Select(m => m.CorrelacaoId)
            .FirstAsync());

        // Formato W3C: 00-<trace de 32 dígitos>-<span de 16>-<flags>; é o que o Worker usa como pai do trace.
        Assert.NotNull(correlacao);
        Assert.Matches("^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$", correlacao);
    }

    [Fact]
    public async Task RespostasDaApi_NaoSaoGuardadasEmCache()
    {
        var (cliente, _) = await api.EntrarComoAsync(PerfilUsuario.Cliente);

        var resposta = await cliente.GetAsync("/api/pedidos");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.True(resposta.Headers.CacheControl is { NoStore: true });
        Assert.False(string.IsNullOrEmpty(resposta.Headers.GetValues("X-Trace-Id").Single()));
    }

    [Fact]
    public async Task TokenAssinadoComChaveAnterior_AindaEhAceito()
    {
        var (_, usuarioId) = await api.EntrarComoAsync(PerfilUsuario.Cliente);
        var agora = DateTime.UtcNow;
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "Ingressa",
            Audience = "Ingressa.Clientes",
            IssuedAt = agora,
            NotBefore = agora,
            Expires = agora.AddMinutes(5),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.ChaveAnterior)), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object> { ["sub"] = usuarioId.ToString(System.Globalization.CultureInfo.InvariantCulture), ["role"] = "Cliente" }
        });

        var cliente = api.NovoCliente();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/api/pedidos")).StatusCode);
    }

    [Fact]
    public async Task VitrineEmCache_EhInvalidadaQuandoOEventoMuda()
    {
        var (organizador, organizadorId) = await api.EntrarComoAsync(PerfilUsuario.Organizador);
        var (eventoId, _) = await api.CriarEventoAsync(organizadorId, 10, "Titulo Original");
        var anonimo = api.NovoCliente();

        var antes = await anonimo.GetFromJsonAsync<EventoDetalheResponse>($"/api/eventos/{eventoId}", Json.Opcoes);
        Assert.Equal("Titulo Original", antes!.Titulo);

        var atualizado = await organizador.PutAsJsonAsync($"/api/eventos/{eventoId}",
            new EventoRequest("Titulo Novo", null, "Arena", "Rio", DateTime.UtcNow.AddDays(10)));
        Assert.Equal(HttpStatusCode.OK, atualizado.StatusCode);

        var depois = await anonimo.GetFromJsonAsync<EventoDetalheResponse>($"/api/eventos/{eventoId}", Json.Opcoes);
        Assert.Equal("Titulo Novo", depois!.Titulo);
    }

    [Fact]
    public async Task Limpeza_RemoveChavesDeIdempotenciaAntigas()
    {
        var (_, organizadorId) = await api.EntrarComoAsync(PerfilUsuario.Organizador);
        var (eventoId, setorId) = await api.CriarEventoAsync(organizadorId, 10);
        var (cliente, usuarioId) = await api.EntrarComoAsync(PerfilUsuario.Cliente);
        await cliente.PostIdempotenteAsync("/api/pedidos", new CriarPedidoRequest(eventoId, [new ItemPedidoRequest(setorId, 1)]));

        using (api.Relogio.Adiantar(TimeSpan.FromDays(2)))
        {
            var limpeza = new LimpezaDeDados(
                api.Services.GetRequiredService<IServiceScopeFactory>(), api.Relogio, NullLogger<LimpezaDeDados>.Instance);
            var (_, chaves, _) = await limpeza.LimparAsync(default);
            Assert.True(chaves >= 1);
        }

        var restantes = await api.NoEscopoAsync(sp => sp.GetRequiredService<IngressaDbContext>()
            .RequisicoesIdempotentes.CountAsync(r => r.UsuarioId == usuarioId));
        Assert.Equal(0, restantes);
    }

    [Fact]
    public async Task Admin_ListaEReprocessaMensagensComFalha()
    {
        var id = await api.NoEscopoAsync(async sp =>
        {
            var db = sp.GetRequiredService<IngressaDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "MensagensOutbox" ("MensagemId", "Tipo", "Conteudo", "OcorridoEm", "Tentativas", "UltimoErro")
                VALUES (gen_random_uuid(), 'pedido.pago', '{"pedidoId":0}', now(), 10, 'broker fora do ar')
                """);
            return await db.MensagensOutbox.Where(m => m.UltimoErro == "broker fora do ar").Select(m => m.Id).FirstAsync();
        });
        var (admin, _) = await api.EntrarComoAsync(PerfilUsuario.Admin);

        var falhas = await admin.GetFromJsonAsync<List<MensagemComFalha>>("/api/admin/outbox/falhas", Json.Opcoes);
        Assert.Contains(falhas!, f => f.Id == id);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/outbox/{id}/reprocessar", null)).StatusCode);
        var tentativas = await api.NoEscopoAsync(sp => sp.GetRequiredService<IngressaDbContext>()
            .MensagensOutbox.Where(m => m.Id == id).Select(m => m.Tentativas).SingleAsync());
        Assert.Equal(0, tentativas);
    }
}

/// <summary>API com limite de reservas baixo, separada das outras.</summary>
public sealed class LimiteDistribuidoTests : IAsyncLifetime
{
    private readonly ApiFactory _api = new() { LimiteDeReservas = "2" };

    public Task InitializeAsync() => _api.InitializeAsync();

    public Task DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Reservas_LimitePorUsuario()
    {
        var (_, organizadorId) = await _api.EntrarComoAsync(PerfilUsuario.Organizador);
        var (eventoId, setorId) = await _api.CriarEventoAsync(organizadorId, 100);
        var (cliente, _) = await _api.EntrarComoAsync(PerfilUsuario.Cliente);
        var (outroCliente, _) = await _api.EntrarComoAsync(PerfilUsuario.Cliente);
        var corpo = new CriarPedidoRequest(eventoId, [new ItemPedidoRequest(setorId, 1)]);

        var status = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            status.Add((await cliente.PostIdempotenteAsync("/api/pedidos", corpo)).StatusCode);
        }

        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests }, status);

        // O limite é por usuário: outra pessoa continua comprando normalmente.
        Assert.Equal(HttpStatusCode.Created, (await outroCliente.PostIdempotenteAsync("/api/pedidos", corpo)).StatusCode);
    }
}
