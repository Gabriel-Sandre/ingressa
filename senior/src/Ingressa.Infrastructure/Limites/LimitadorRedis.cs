using Ingressa.Application.Abstracoes;
using Ingressa.Infrastructure.FilaVirtual;
using StackExchange.Redis;

namespace Ingressa.Infrastructure.Limites;

/// <summary>
/// Limite de requisições em janela fixa, guardado no Redis. Diferente do limitador
/// em memória do ASP.NET Core, a contagem é a mesma para todas as instâncias da API:
/// com 4 réplicas, "10 por minuto" continua sendo 10, e não 40.
/// </summary>
public sealed class LimitadorRedis(IConnectionMultiplexer redis, TimeProvider relogio) : ILimitadorDistribuido
{
    private static readonly string Script = FilaVirtualRedis.Carregar("janela-fixa");

    public async Task<ResultadoDoLimite> VerificarAsync(
        string politica, string particao, int limite, TimeSpan janela, CancellationToken ct)
    {
        var janelaMs = (long)janela.TotalMilliseconds;
        var numeroDaJanela = relogio.GetUtcNow().ToUnixTimeMilliseconds() / janelaMs;
        var chave = $"limite:{politica}:{particao}:{numeroDaJanela}";

        var r = (RedisResult[]?)await redis.GetDatabase().ScriptEvaluateAsync(Script, [chave], [limite, janelaMs])
                ?? throw new InvalidOperationException("Resposta inesperada do Redis.");

        return new ResultadoDoLimite((int)r[0] == 1, (long)r[1], TimeSpan.FromMilliseconds(Math.Max(0, (long)r[2])));
    }
}
