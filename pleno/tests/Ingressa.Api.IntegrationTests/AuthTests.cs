using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ingressa.Application.Auth;
using Ingressa.Application.Eventos;
using Ingressa.Domain.Usuarios;

namespace Ingressa.Api.IntegrationTests;

[Collection(ColecaoApi.Nome)]
public sealed class AuthTests(ApiFactory api)
{
    [Fact]
    public async Task RegistrarELogar_GravaCookieHttpOnlyEAcessaRotaProtegida()
    {
        var cliente = api.NovoCliente();
        var email = $"novo-{Guid.NewGuid():N}@teste.dev";

        var registro = await cliente.PostAsJsonAsync("/api/auth/registrar", new RegistrarRequest("Novo Usuário", email, ApiFactory.Senha));
        Assert.Equal(HttpStatusCode.Created, registro.StatusCode);

        var login = await cliente.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, ApiFactory.Senha));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);

        var sessao = await login.Content.ReadFromJsonAsync<SessaoResponse>(Json.Opcoes);
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessao!.AccessToken);
        var eu = await cliente.GetFromJsonAsync<UsuarioResponse>("/api/auth/eu", Json.Opcoes);
        Assert.Equal(email, eu!.Email);
    }

    [Fact]
    public async Task Renovar_RotacionaEDetectaTokenRoubado()
    {
        var (vitima, _) = await api.EntrarComoAsync(PerfilUsuario.Cliente);

        // A vítima renova a sessão e passa a ter o token T1 no cookie.
        var renovacao = await vitima.PostAsync("/api/auth/renovar", null);
        Assert.Equal(HttpStatusCode.OK, renovacao.StatusCode);
        var t1 = ExtrairCookie(renovacao);

        // Um atacante copia T1 e o usa primeiro: recebe T2.
        var atacante = api.NovoCliente();
        var roubo = await RenovarComAsync(atacante, t1);
        Assert.Equal(HttpStatusCode.OK, roubo.StatusCode);
        var t2 = ExtrairCookie(roubo);

        // Quando a vítima tenta usar T1 (já usado), o reuso é detectado...
        Assert.Equal(HttpStatusCode.Unauthorized, (await vitima.PostAsync("/api/auth/renovar", null)).StatusCode);

        // ...e a família inteira é revogada: o token do atacante também deixa de valer.
        Assert.Equal(HttpStatusCode.Unauthorized, (await RenovarComAsync(api.NovoCliente(), t2)).StatusCode);
    }

    private static Task<HttpResponseMessage> RenovarComAsync(HttpClient cliente, string token)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Post, "/api/auth/renovar");
        requisicao.Headers.Add("Cookie", $"ingressa_sessao={token}");
        return cliente.SendAsync(requisicao);
    }

    [Fact]
    public async Task Sair_InvalidaASessao()
    {
        var (cliente, _) = await api.EntrarComoAsync(PerfilUsuario.Cliente);

        Assert.Equal(HttpStatusCode.NoContent, (await cliente.PostAsync("/api/auth/sair", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.PostAsync("/api/auth/renovar", null)).StatusCode);
    }

    [Fact]
    public async Task Autorizacao_PorPerfil()
    {
        var anonimo = api.NovoCliente();
        var (cliente, _) = await api.EntrarComoAsync(PerfilUsuario.Cliente);
        var (pendente, _) = await api.EntrarComoAsync(PerfilUsuario.Organizador, aprovado: false);
        var novoEvento = new EventoRequest("Show", null, "Arena", "Rio", DateTime.UtcNow.AddDays(5));

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync("/api/eventos", novoEvento)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.PostAsJsonAsync("/api/eventos", novoEvento)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await pendente.PostAsJsonAsync("/api/eventos", novoEvento)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/api/admin/organizadores/pendentes")).StatusCode);
    }

    [Fact]
    public async Task Admin_AprovaOrganizadorQuePassaACriarEventos()
    {
        var (admin, _) = await api.EntrarComoAsync(PerfilUsuario.Admin);
        var (organizador, organizadorId) = await api.EntrarComoAsync(PerfilUsuario.Organizador, aprovado: false);

        var aprovado = await admin.PostAsync($"/api/admin/organizadores/{organizadorId}/aprovar", null);
        Assert.Equal(HttpStatusCode.OK, aprovado.StatusCode);

        var criado = await organizador.PostAsJsonAsync("/api/eventos",
            new EventoRequest("Show Aprovado", null, "Arena", "Rio", DateTime.UtcNow.AddDays(5)));
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
    }

    [Fact]
    public async Task RespostasTemCabecalhosDeSeguranca()
    {
        var resposta = await api.NovoCliente().GetAsync("/api/eventos");

        Assert.Equal("nosniff", resposta.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", resposta.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task HealthCheck_ProntoQuandoOBancoResponde()
    {
        Assert.Equal(HttpStatusCode.OK, (await api.NovoCliente().GetAsync("/health/ready")).StatusCode);
    }

    private static string ExtrairCookie(HttpResponseMessage resposta)
    {
        var cabecalho = resposta.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("ingressa_sessao=", StringComparison.Ordinal));
        return cabecalho["ingressa_sessao=".Length..cabecalho.IndexOf(';', StringComparison.Ordinal)];
    }
}

/// <summary>Usa uma API com limite baixo, separada das outras para não interferir.</summary>
public sealed class LimiteDeRequisicoesTests : IAsyncLifetime
{
    private readonly ApiFactory _api = new() { LimitePorMinuto = "3" };

    public Task InitializeAsync() => _api.InitializeAsync();

    public Task DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Login_ExcessoDeTentativasRetorna429()
    {
        var cliente = _api.NovoCliente();
        var status = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            status.Add((await cliente.PostAsJsonAsync("/api/auth/login", new LoginRequest("x@teste.dev", "errada"))).StatusCode);
        }

        Assert.Equal(
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests },
            status);
    }
}
