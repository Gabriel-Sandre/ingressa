using Ingressa.Application;
using Ingressa.Infrastructure;
using Ingressa.Worker;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProcessamentoDePedidos();
builder.Services.AddInfraestrutura(builder.Configuration);
builder.Services.AddProcessamentoEmSegundoPlano(builder.Configuration);

builder.Services.AddHostedService<ConsumidorDeEmissao>();
builder.Services.AddHostedService<ConsumidorDeNotificacoes>();
builder.Services.AddHostedService<ExpiradorDeReservas>();

var app = builder.Build();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("pronto") });

await app.RunAsync();
