using Ingressa.Application.Abstracoes;
using Ingressa.Infrastructure.Cache;
using Ingressa.Infrastructure.FilaVirtual;
using Ingressa.Infrastructure.Idempotencia;
using Ingressa.Infrastructure.Limites;
using Ingressa.Infrastructure.Manutencao;
using Ingressa.Infrastructure.Email;
using Ingressa.Infrastructure.Mensageria;
using Ingressa.Infrastructure.Outbox;
using Ingressa.Infrastructure.Pagamentos;
using Ingressa.Infrastructure.Persistencia;
using Ingressa.Infrastructure.Seguranca;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Ingressa.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Banco, repositórios e serviços técnicos usados pela API e pelo Worker.</summary>
    public static IServiceCollection AddInfraestrutura(this IServiceCollection services, IConfiguration configuracao)
    {
        var conexao = configuracao.GetConnectionString("Ingressa")
            ?? throw new InvalidOperationException("Connection string 'Ingressa' não configurada.");

        services.AddDbContext<IngressaDbContext>(o => o.UseNpgsql(conexao));

        services.AddScoped<IUnidadeDeTrabalho, UnidadeDeTrabalho>();
        services.AddScoped<IEstoque, Estoque>();
        services.AddScoped<IUsuarioRepositorio, UsuarioRepositorio>();
        services.AddScoped<IRefreshTokenRepositorio, RefreshTokenRepositorio>();
        services.AddScoped<IEventoRepositorio, EventoRepositorio>();
        services.AddScoped<IPedidoRepositorio, PedidoRepositorio>();
        services.AddScoped<ConsultasDeEventos>();
        services.AddScoped<IConsultasDeEventos, ConsultasDeEventosEmCache>();
        services.AddScoped<IInvalidadorDeCache, InvalidadorDeCache>();
        services.AddScoped<ControleDeIdempotencia>();
        services.AddScoped<IConsultasDePedidos, ConsultasDePedidos>();

        services.AddSingleton<ISenhaHasher, SenhaHasherPbkdf2>();
        services.AddSingleton<IGeradorDeCodigos, GeradorDeCodigos>();
        services.AddSingleton<IGatewayDePagamento, GatewayDePagamentoSimulado>();
        services.AddSingleton(TimeProvider.System);

        // ---------- Redis: fila virtual, limites distribuídos e cache ----------
        var redis = configuracao.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Connection string 'Redis' não configurada.");
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            // Não derruba a aplicação se o Redis demorar a subir: a conexão é refeita sozinha.
            var opcoes = ConfigurationOptions.Parse(redis);
            opcoes.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(opcoes);
        });
        services.Configure<OpcoesDaFila>(configuracao.GetSection(OpcoesDaFila.Secao));
        services.AddSingleton<IFilaVirtual, FilaVirtualRedis>();
        services.AddSingleton<ILimitadorDistribuido, LimitadorRedis>();
        services.AddStackExchangeRedisCache(o =>
        {
            o.Configuration = redis;
            o.InstanceName = "cache:";
        });
        services.AddHybridCache();

        services.AddHealthChecks()
            .AddDbContextCheck<IngressaDbContext>("postgres", tags: ["pronto"])
            .AddCheck<VerificacaoRedis>("redis", tags: ["pronto"]);
        return services;
    }

    /// <summary>Mensageria e trabalhos em segundo plano (somente no Worker).</summary>
    public static IServiceCollection AddProcessamentoEmSegundoPlano(this IServiceCollection services, IConfiguration configuracao)
    {
        services.Configure<OpcoesRabbitMq>(configuracao.GetSection(OpcoesRabbitMq.Secao));
        services.Configure<OpcoesDeEmail>(configuracao.GetSection(OpcoesDeEmail.Secao));

        services.AddSingleton<ConexaoRabbitMq>();
        services.AddSingleton<IPublicadorDeMensagens, PublicadorRabbitMq>();
        services.AddSingleton<IEnviadorDeEmail, EnviadorDeEmailSmtp>();

        services.AddHostedService<DespachanteDaOutbox>();
        services.AddHostedService<LimpezaDeDados>();

        services.AddHealthChecks().AddCheck<VerificacaoRabbitMq>("rabbitmq", tags: ["pronto"]);
        return services;
    }
}
