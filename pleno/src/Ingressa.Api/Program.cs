using System.Text.Json.Serialization;
using Ingressa.Api.Auth;
using Ingressa.Api.Infra;
using Ingressa.Application;
using Ingressa.Application.Abstracoes;
using Ingressa.Infrastructure;
using Ingressa.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ---------- Configuração obrigatória ----------
var jwt = builder.Configuration.GetSection(OpcoesJwt.Secao).Get<OpcoesJwt>() ?? new OpcoesJwt();
// Sem chave configurada em desenvolvimento, gera uma temporária: nenhuma chave fica no repositório.
// Em produção a falta da chave impede a aplicação de subir (Validar abaixo).
var chaveJwtTemporaria = false;
if (string.IsNullOrEmpty(jwt.Chave) && builder.Environment.IsDevelopment())
{
    jwt.Chave = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
    chaveJwtTemporaria = true;
}
jwt.Validar();
builder.Services.AddSingleton(jwt);

// ---------- Camadas ----------
builder.Services.AddAplicacao();
builder.Services.AddInfraestrutura(builder.Configuration);
builder.Services.AddSingleton<IGeradorDeAccessToken, GeradorDeAccessTokenJwt>();

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

if (chaveJwtTemporaria)
{
    app.Logger.LogWarning("Jwt:Chave não configurada: usando uma chave temporária. As sessões expiram a cada reinício.");
}

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
