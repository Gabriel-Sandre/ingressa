using Ingressa.Domain.Comum;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Ingressa.Api.Infra;

/// <summary>Converte exceções em Problem Details (RFC 9457) sem expor detalhes internos.</summary>
public sealed class TratadorDeExcecoes(IProblemDetailsService problemDetails, ILogger<TratadorDeExcecoes> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, titulo) = exception switch
        {
            NaoEncontradoException => (StatusCodes.Status404NotFound, "Recurso não encontrado"),
            ConflitoException => (StatusCodes.Status409Conflict, "Conflito"),
            RegraDeNegocioException => (StatusCodes.Status422UnprocessableEntity, "Regra de negócio violada"),
            AcessoNegadoException => (StatusCodes.Status403Forbidden, "Acesso negado"),
            NaoAutenticadoException => (StatusCodes.Status401Unauthorized, "Não autenticado"),
            BadHttpRequestException bad => (bad.StatusCode, "Requisição inválida"),
            _ => (StatusCodes.Status500InternalServerError, "Erro interno")
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Erro não tratado em {Metodo} {Caminho}",
                TextoDeLog.Sanitizar(httpContext.Request.Method), TextoDeLog.Sanitizar(httpContext.Request.Path));
        }
        else
        {
            logger.LogInformation("Requisição recusada com {Status}: {Mensagem}", status, exception.Message);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = titulo,
                Detail = status >= 500 ? "Ocorreu um erro inesperado. Tente novamente mais tarde." : exception.Message
            }
        });
    }
}
