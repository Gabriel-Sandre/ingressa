using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Ingressa.Api.Erros;

/// <summary>
/// Converte exceções em respostas no formato Problem Details (RFC 9457).
/// Erros inesperados nunca expõem detalhes internos ao cliente.
/// </summary>
public sealed class TratadorDeExcecoes(
    IProblemDetailsService problemDetailsService,
    ILogger<TratadorDeExcecoes> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, titulo) = exception switch
        {
            NaoEncontradoException => (StatusCodes.Status404NotFound, "Recurso não encontrado"),
            ConflitoException => (StatusCodes.Status409Conflict, "Conflito"),
            RegraDeNegocioException => (StatusCodes.Status422UnprocessableEntity, "Regra de negócio violada"),
            AcessoNegadoException => (StatusCodes.Status403Forbidden, "Acesso negado"),
            CredenciaisInvalidasException => (StatusCodes.Status401Unauthorized, "Não autenticado"),
            _ => (StatusCodes.Status500InternalServerError, "Erro interno")
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Erro não tratado em {Metodo} {Caminho}",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("Requisição recusada com {Status}: {Mensagem}", status, exception.Message);
        }

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = titulo,
                Detail = status == StatusCodes.Status500InternalServerError
                    ? "Ocorreu um erro inesperado. Tente novamente mais tarde."
                    : exception.Message
            }
        });
    }
}
