using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ingressa.Api.Auth;
using Ingressa.Application.Abstracoes;
using Ingressa.Application.Observabilidade;
using Ingressa.Infrastructure.Idempotencia;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using JsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace Ingressa.Api.Infra;

/// <summary>
/// Exige o cabeçalho <c>Idempotency-Key</c> e garante que repetir a requisição devolve
/// a mesma resposta, sem executar a ação de novo.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotenteAttribute() : TypeFilterAttribute(typeof(FiltroDeIdempotencia));

public sealed class FiltroDeIdempotencia(
    ControleDeIdempotencia controle,
    IOptions<JsonOptions> json,
    ILogger<FiltroDeIdempotencia> logger) : IAsyncActionFilter
{
    public const string Cabecalho = "Idempotency-Key";
    public const string CabecalhoDeRepeticao = "Idempotent-Replayed";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var chave = http.Request.Headers[Cabecalho].ToString();

        if (string.IsNullOrWhiteSpace(chave) || chave.Length > ControleDeIdempotencia.TamanhoMaximoDaChave)
        {
            context.Result = Problema(StatusCodes.Status400BadRequest, "Cabeçalho obrigatório",
                $"Envie o cabeçalho {Cabecalho} (até {ControleDeIdempotencia.TamanhoMaximoDaChave} caracteres) para que a operação possa ser repetida com segurança.");
            return;
        }

        var opcoesJson = json.Value.JsonSerializerOptions;
        var rota = $"{http.Request.Method} {http.Request.Path}";
        var argumentos = context.ActionArguments
            .Where(a => a.Value is not CancellationToken)
            .OrderBy(a => a.Key, StringComparer.Ordinal)
            .ToDictionary(a => a.Key, a => a.Value);
        var hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(argumentos, opcoesJson))));

        var usuarioId = http.User.ObterUsuarioId();
        var reivindicacao = await controle.ReivindicarAsync(usuarioId, chave, rota, hash, http.RequestAborted);

        switch (reivindicacao.Situacao)
        {
            case SituacaoDaChave.Divergente:
                context.Result = Problema(StatusCodes.Status422UnprocessableEntity, "Chave de idempotência reutilizada",
                    "Esta chave já foi usada com outra requisição. Gere uma chave nova para cada operação.");
                return;

            case SituacaoDaChave.EmAndamento:
                context.Result = Problema(StatusCodes.Status409Conflict, "Requisição em andamento",
                    "Uma requisição igual ainda está sendo processada. Aguarde a resposta.");
                return;

            case SituacaoDaChave.Concluida:
                http.Response.Headers[CabecalhoDeRepeticao] = "true";
                if (!string.IsNullOrEmpty(reivindicacao.Local))
                {
                    http.Response.Headers.Location = reivindicacao.Local;
                }

                context.Result = new ContentResult
                {
                    StatusCode = reivindicacao.StatusHttp,
                    Content = reivindicacao.Resposta,
                    ContentType = "application/json; charset=utf-8"
                };
                return;
        }

        var executado = await next();

        if (executado.Exception is null && executado.Result is ObjectResult { StatusCode: >= 200 and < 300 } resultado)
        {
            var resposta = JsonSerializer.Serialize(resultado.Value, opcoesJson);
            var status = resultado.StatusCode!.Value;

            // O cabeçalho Location só existe depois que o resultado é executado (o 201 da reserva
            // aponta para o pedido criado), por isso a resposta é guardada quando ela vai começar
            // a ser enviada — assim a repetição devolve exatamente a mesma resposta.
            http.Response.OnStarting(async () =>
            {
                try
                {
                    await controle.ConcluirAsync(
                        reivindicacao.Id, status, resposta, http.Response.Headers.Location, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    // Não vale derrubar uma operação bem-sucedida por causa do registro da chave:
                    // a chave fica "em andamento" e pode ser retomada depois de dois minutos.
                    logger.LogError(ex, "Falha ao guardar a resposta idempotente de {Rota}", rota);
                }
            });
            return;
        }

        // Falhou: libera a chave para o cliente tentar de novo.
        await controle.DescartarAsync(reivindicacao.Id, CancellationToken.None);
        logger.LogInformation("Chave de idempotência descartada após falha em {Rota}", rota);
    }

    internal static ObjectResult Problema(int status, string titulo, string detalhe) =>
        new(new ProblemDetails { Status = status, Title = titulo, Detail = detalhe }) { StatusCode = status };
}

/// <summary>
/// Limite de requisições distribuído (Redis), configurado em <c>LimitesDistribuidos:{politica}</c>.
/// A partição é o usuário autenticado ou, sem login, o IP.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class LimiteDistribuidoAttribute : TypeFilterAttribute
{
    public LimiteDistribuidoAttribute(string politica) : base(typeof(FiltroDeLimite))
    {
        Arguments = [politica];
        Politica = politica;
    }

    public string Politica { get; }
}

public sealed class PoliticaDeLimite
{
    public int Limite { get; set; } = 10;
    public int JanelaEmSegundos { get; set; } = 60;
}

public sealed class FiltroDeLimite(
    string politica,
    ILimitadorDistribuido limitador,
    IConfiguration configuracao,
    ILogger<FiltroDeLimite> logger) : IAsyncActionFilter
{
    // Ligar a configuração por reflexão a cada requisição custa caro num caminho que existe
    // justamente para aguentar pico; a regra muda só com reinício, então fica em cache.
    private static readonly ConcurrentDictionary<string, PoliticaDeLimite> Regras = new();

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var regra = Regras.GetOrAdd(politica, nome =>
            configuracao.GetSection($"LimitesDistribuidos:{nome}").Get<PoliticaDeLimite>() ?? new PoliticaDeLimite());
        var http = context.HttpContext;
        var particao = http.User.ObterUsuarioIdOuNulo() is { } id
            ? $"u{id}"
            : $"ip{http.Connection.RemoteIpAddress ?? IPAddress.None}";

        ResultadoDoLimite resultado;
        try
        {
            resultado = await limitador.VerificarAsync(
                politica, particao, regra.Limite, TimeSpan.FromSeconds(regra.JanelaEmSegundos), http.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Decisão consciente: se o Redis cair, o sistema continua atendendo
            // (o limitador local por IP e o bloqueio de conta continuam ativos).
            logger.LogWarning(ex, "Limitador distribuído indisponível; requisição liberada");
            await next();
            return;
        }

        if (!resultado.Permitido)
        {
            Metricas.RequisicoesLimitadas.Add(1, new KeyValuePair<string, object?>("politica", politica));
            http.Response.Headers.RetryAfter = Math.Ceiling(resultado.TentarNovamenteEm.TotalSeconds)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            context.Result = FiltroDeIdempotencia.Problema(StatusCodes.Status429TooManyRequests, "Muitas tentativas",
                "Você fez muitas tentativas em pouco tempo. Aguarde e tente de novo.");
            return;
        }

        await next();
    }
}
