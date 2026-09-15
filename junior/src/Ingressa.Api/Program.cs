using System.Text.Json.Serialization;
using Ingressa.Api.Auth;
using Ingressa.Api.Data;
using Ingressa.Api.Erros;
using Ingressa.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ---------- Configuração ----------
var jwtOptions = builder.Configuration.GetSection(JwtOptions.Secao).Get<JwtOptions>() ?? new JwtOptions();
jwtOptions.Validar();
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton(TimeProvider.System);

// ---------- Banco de dados ----------
builder.Services.AddDbContext<IngressaDbContext>(opcoes =>
    opcoes.UseSqlite(builder.Configuration.GetConnectionString("Ingressa")));

// ---------- Autenticação e autorização ----------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opcoes =>
    {
        // Mantém os nomes curtos das claims ("sub", "role") em vez dos URIs longos da Microsoft.
        opcoes.MapInboundClaims = false;
        opcoes.TokenValidationParameters = TokenService.CriarParametrosDeValidacao(jwtOptions);
    });
builder.Services.AddAuthorization();

// ---------- Serviços da aplicação ----------
builder.Services.AddSingleton<SenhaHasher>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<EventoService>();
builder.Services.AddScoped<PedidoService>();

// ---------- API ----------
builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<TratadorDeExcecoes>();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(); // documentação interativa em /scalar
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await DbInitializer.InicializarAsync(app.Services, inserirDadosDeExemplo: app.Environment.IsDevelopment());

app.Run();

/// <summary>Exposto para os testes de integração (WebApplicationFactory) da versão Pleno.</summary>
public partial class Program;
