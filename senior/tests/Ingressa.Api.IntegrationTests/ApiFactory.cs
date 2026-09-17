using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ingressa.Application.Abstracoes;
using Ingressa.Application.Auth;
using Ingressa.Domain.Eventos;
using Ingressa.Domain.Usuarios;
using Ingressa.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Ingressa.Api.IntegrationTests;

/// <summary>
/// Sobe a API inteira em memória, apontando para PostgreSQL e Redis de verdade em containers.
/// Um container é compartilhado por todos os testes da coleção; cada teste cria seus próprios dados.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Senha = "Senha@123";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7.4-alpine").Build();

    /// <summary>Relógio da aplicação, que os testes podem adiantar.</summary>
    public RelogioAjustavel Relogio { get; } = new();

    public string ConexaoRedis => _redis.GetConnectionString();

    public string? LimitePorMinuto { get; init; } = "10000";

    public string LimiteDeReservas { get; init; } = "10000";

    /// <summary>Chave antiga ainda aceita na validação (rotação de chaves).</summary>
    public const string ChaveAnterior = "chave-antiga-que-ainda-vale-durante-a-rotacao-123";

    public Task InitializeAsync() => Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Ingressa", _postgres.GetConnectionString());
        builder.UseSetting("Jwt:Chave", "chave-dos-testes-de-integracao-com-32-bytes-ou-mais");
        builder.UseSetting("Banco:DadosDeDemonstracao", "false");
        builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
        builder.UseSetting("LimitesDistribuidos:autenticacao:Limite", LimitePorMinuto);
        builder.UseSetting("LimitesDistribuidos:reservas:Limite", LimiteDeReservas);
        builder.UseSetting("LimiteDeRequisicoes:GlobalPorMinuto", "100000");
        builder.UseSetting("Jwt:ChavesAnteriores:0", ChaveAnterior);
        builder.ConfigureTestServices(s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(Relogio);
        });
    }

    /// <summary>Cliente HTTPS (o cookie de sessão é Secure) que guarda cookies.</summary>
    public HttpClient NovoCliente() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true
    });

    public async Task<T> NoEscopoAsync<T>(Func<IServiceProvider, Task<T>> acao)
    {
        await using var scope = Services.CreateAsyncScope();
        return await acao(scope.ServiceProvider);
    }

    /// <summary>Cria um usuário direto no banco e devolve um cliente HTTP já autenticado.</summary>
    public async Task<(HttpClient Cliente, int UsuarioId)> EntrarComoAsync(PerfilUsuario perfil, bool aprovado = true)
    {
        var email = $"{perfil.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}@teste.dev";
        var id = await NoEscopoAsync(async sp =>
        {
            var db = sp.GetRequiredService<IngressaDbContext>();
            var hash = sp.GetRequiredService<ISenhaHasher>().GerarHash(Senha);
            var agora = DateTime.UtcNow;
            var usuario = perfil == PerfilUsuario.Admin
                ? Usuario.CriarAdmin("Admin", email, hash, agora)
                : Usuario.Registrar("Usuário de Teste", email, hash, perfil, agora);
            if (perfil == PerfilUsuario.Organizador && aprovado)
            {
                usuario.AprovarComoOrganizador();
            }

            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync();
            return usuario.Id;
        });

        var cliente = NovoCliente();
        var resposta = await cliente.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Senha));
        resposta.EnsureSuccessStatusCode();
        var sessao = await resposta.Content.ReadFromJsonAsync<SessaoResponse>(Json.Opcoes);
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessao!.AccessToken);
        return (cliente, id);
    }

    /// <summary>Evento publicado com um setor, criado direto no banco.</summary>
    public Task<(int EventoId, int SetorId)> CriarEventoAsync(int organizadorId, int capacidade, string titulo = "Show de Integração") =>
        NoEscopoAsync(async sp =>
        {
            var db = sp.GetRequiredService<IngressaDbContext>();
            var agora = DateTime.UtcNow;
            var evento = Evento.Criar(organizadorId, new DadosDoEvento(titulo, "Teste", "Arena", "Rio", agora.AddDays(10)), agora);
            evento.AdicionarSetor("Pista", 50m, capacidade);
            evento.Publicar(agora);
            db.Eventos.Add(evento);
            await db.SaveChangesAsync();
            return (evento.Id, evento.Setores[0].Id);
        });
}

[CollectionDefinition(Nome)]
public sealed class ColecaoApi : ICollectionFixture<ApiFactory>
{
    public const string Nome = "api";
}

public static class Json
{
    public static readonly System.Text.Json.JsonSerializerOptions Opcoes = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}

public sealed class RelogioAjustavel : TimeProvider
{
    private TimeSpan _deslocamento;

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + _deslocamento;

    public IDisposable Adiantar(TimeSpan tempo)
    {
        _deslocamento += tempo;
        return new Restaurar(() => _deslocamento -= tempo);
    }

    private sealed class Restaurar(Action acao) : IDisposable
    {
        public void Dispose() => acao();
    }
}

public static class ClienteHttpExtensions
{
    /// <summary>POST com Idempotency-Key (nova, se não for informada) e, opcionalmente, o passe da fila.</summary>
    public static Task<HttpResponseMessage> PostIdempotenteAsync<T>(
        this HttpClient cliente, string url, T corpo, string? chave = null, string? passe = null)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(corpo) };
        requisicao.Headers.Add("Idempotency-Key", chave ?? Guid.NewGuid().ToString());
        if (passe is not null)
        {
            requisicao.Headers.Add("X-Passe-Fila", passe);
        }

        return cliente.SendAsync(requisicao);
    }
}
