using System.Diagnostics;

namespace Ingressa.Api.Infra;

public static class CabecalhosDeSeguranca
{
    /// <summary>
    /// Cabeçalhos defensivos em todas as respostas e o identificador de rastreamento,
    /// que permite achar nos logs a requisição de um erro relatado pelo usuário.
    /// </summary>
    public static IApplicationBuilder UseCabecalhosPadrao(this IApplicationBuilder app) =>
        app.Use(async (contexto, proximo) =>
        {
            contexto.Response.OnStarting(() =>
            {
                var h = contexto.Response.Headers;
                h.XContentTypeOptions = "nosniff";
                h.XFrameOptions = "DENY";
                h["Referrer-Policy"] = "no-referrer";
                // Respostas da API podem conter dados pessoais: nenhum proxy ou navegador deve guardá-las.
                if (string.IsNullOrEmpty(h.CacheControl))
                {
                    h.CacheControl = "no-store";
                }

                if (Activity.Current is { } atividade)
                {
                    h["X-Trace-Id"] = atividade.TraceId.ToString();
                }

                return Task.CompletedTask;
            });
            await proximo(contexto);
        });
}
