using Ingressa.Application.Admin;
using Ingressa.Application.Auth;
using Ingressa.Application.Eventos;
using Ingressa.Application.Pedidos;
using Microsoft.Extensions.DependencyInjection;

namespace Ingressa.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAplicacao(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<AdminService>();
        services.AddScoped<EventoService>();
        services.AddScoped<PedidoService>();
        return services;
    }

    /// <summary>Casos de uso executados em segundo plano (usados pelo Worker).</summary>
    public static IServiceCollection AddProcessamentoDePedidos(this IServiceCollection services)
    {
        services.AddScoped<ProcessamentoDePedidosService>();
        return services;
    }
}
