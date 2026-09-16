using Ingressa.Application.Abstracoes;
using Ingressa.Infrastructure.Email;
using Ingressa.Infrastructure.Mensageria;
using Ingressa.Infrastructure.Outbox;
using Ingressa.Infrastructure.Pagamentos;
using Ingressa.Infrastructure.Persistencia;
using Ingressa.Infrastructure.Seguranca;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddScoped<IConsultasDeEventos, ConsultasDeEventos>();
        services.AddScoped<IConsultasDePedidos, ConsultasDePedidos>();

        services.AddSingleton<ISenhaHasher, SenhaHasherPbkdf2>();
        services.AddSingleton<IGeradorDeCodigos, GeradorDeCodigos>();
        services.AddSingleton<IGatewayDePagamento, GatewayDePagamentoSimulado>();
        services.AddSingleton(TimeProvider.System);

        services.AddHealthChecks().AddDbContextCheck<IngressaDbContext>("postgres", tags: ["pronto"]);
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

        services.AddHealthChecks().AddCheck<VerificacaoRabbitMq>("rabbitmq", tags: ["pronto"]);
        return services;
    }
}
