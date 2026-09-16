using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ingressa.Application.Abstracoes;
using Ingressa.Application.Auth;
using Ingressa.Domain.Eventos;
using Ingressa.Domain.Usuarios;
using Ingressa.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Ingressa.Api.IntegrationTests;

/// <summary>
/// Sobe a API inteira em memória, apontando para um PostgreSQL de verdade em container.
/// Um container é compartilhado por todos os testes da coleção; cada teste cria seus próprios dados.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Senha = "Senha@123";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string? LimitePorMinuto { get; init; } = "10000";

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Ingressa", _postgres.GetConnectionString());
        builder.UseSetting("Jwt:Chave", "chave-dos-testes-de-integracao-com-32-bytes-ou-mais");
        builder.UseSetting("Banco:DadosDeDemonstracao", "false");
        builder.UseSetting("LimiteDeRequisicoes:AutenticacaoPorMinuto", LimitePorMinuto);
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
