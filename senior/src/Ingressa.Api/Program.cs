using System.Text.Json.Serialization;
using Ingressa.Api.Auth;
using Ingressa.Api.Infra;
using Ingressa.Application;
using Ingressa.Application.Abstracoes;
using Ingressa.Infrastructure;
using Ingressa.Infrastructure.Observabilidade;
using Ingressa.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ---------- Configuração obrigatória ----------
var jwt = builder.Configuration.GetSection(OpcoesJwt.Secao).Get<OpcoesJwt>() ?? new OpcoesJwt();
jwt.Validar();
builder.Services.AddSingleton(jwt);

// ---------- Camadas ----------
builder.Services.AddAplicacao();
builder.Services.AddInfraestrutura(builder.Configuration);
builder.Services.AddSingleton<IGeradorDeAccessToken, GeradorDeAccessTokenJwt>();
builder.Services.AddObservabilidade(builder.Configuration, "ingressa-api");

// ---------- Proxy reverso (nginx / load balancer) ----------
// Sem isto, todo cliente teria o IP do proxy, e o limite por IP bloquearia todo mundo junto.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    // Os proxies ficam na rede privada dos containers/VPC, que não é acessível de fora.
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// ---------- Segurança ----------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = jwt.ParametrosDeValidacao();
    });
builder.Services.AddAuthorization();
builder.Services.AddLimiteDeRequisicoes(builder.Configuration);

// ---------- HTTP ----------
builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<TratadorDeExcecoes>();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCabecalhosPadrao();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
else
{
    app.UseHsts();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

// Liveness: o processo responde. Readiness: consegue atender (banco acessível).
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("pronto") });

// Em produção as migrations rodam numa tarefa separada, antes do deploy
// (`dotnet Ingressa.Api.dll --migrar-e-sair`): várias réplicas subindo juntas
// não disputam a mesma migration, e uma migration com erro impede o deploy.
if (args.Contains("--migrar-e-sair"))
{
    await InicializadorDoBanco.InicializarAsync(app.Services, app.Configuration, dadosDeDemonstracao: false);
    app.Logger.LogInformation("Banco atualizado; encerrando");
    return;
}

if (app.Configuration.GetValue("Banco:InicializarAoIniciar", true))
{
    await InicializadorDoBanco.InicializarAsync(
        app.Services,
        app.Configuration,
        dadosDeDemonstracao: app.Configuration.GetValue("Banco:DadosDeDemonstracao", app.Environment.IsDevelopment()));
}

await app.RunAsync();

/// <summary>Exposto para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
