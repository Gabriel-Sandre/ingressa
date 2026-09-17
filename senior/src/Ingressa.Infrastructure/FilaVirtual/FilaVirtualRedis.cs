using System.Globalization;
using System.Reflection;
using Ingressa.Application.Abstracoes;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Ingressa.Infrastructure.FilaVirtual;

public sealed class OpcoesDaFila
{
    public const string Secao = "FilaVirtual";

    /// <summary>Quantas pessoas podem estar comprando ao mesmo tempo em cada evento.</summary>
    public int CompradoresSimultaneos { get; set; } = 200;

    /// <summary>Por quanto tempo o passe vale depois de liberado.</summary>
    public int ValidadeDoPasseEmMinutos { get; set; } = 10;

    /// <summary>Intervalo entre rodadas de liberação no Worker.</summary>
    public int IntervaloDeAdmissaoEmSegundos { get; set; } = 2;
}

/// <summary>
/// Fila virtual no Redis. Cada operação é um script Lua: o Redis executa o script
/// inteiro sem intercalar outros comandos, então não há condição de corrida
/// mesmo com várias instâncias da API e do Worker.
/// </summary>
/// <remarks>
/// As chaves de um mesmo evento usam a hash tag <c>{evento:ID}</c>, para ficarem no
/// mesmo slot caso o Redis rode em cluster (scripts só podem tocar chaves de um slot).
/// </remarks>
public sealed class FilaVirtualRedis(
    IConnectionMultiplexer redis,
    IGeradorDeCodigos codigos,
    TimeProvider relogio,
    IOptions<OpcoesDaFila> opcoes) : IFilaVirtual
{
    private const string FilasAtivas = "filas:ativas";

    private static readonly string ScriptEntrar = Carregar("entrar");
    private static readonly string ScriptConsultar = Carregar("consultar");
    private static readonly string ScriptAdmitir = Carregar("admitir");
    private static readonly string ScriptUsarPasse = Carregar("usar-passe");
    private static readonly string ScriptDevolverPasse = Carregar("devolver-passe");
    private static readonly string ScriptConcluir = Carregar("concluir");

    private IDatabase Db => redis.GetDatabase();

    internal static string Prefixo(int eventoId) => $"fila:{{evento:{eventoId}}}:";
    private static string Espera(int eventoId) => Prefixo(eventoId) + "espera";
    private static string Sequencia(int eventoId) => Prefixo(eventoId) + "seq";
    private static string Ativos(int eventoId) => Prefixo(eventoId) + "ativos";
    private static string PrefixoDoPasse(int eventoId) => Prefixo(eventoId) + "passe:";

    private static string Passe(int eventoId, int usuarioId) =>
        PrefixoDoPasse(eventoId) + usuarioId.ToString(CultureInfo.InvariantCulture);

    public async Task<PosicaoNaFila> EntrarAsync(int eventoId, int usuarioId, CancellationToken ct)
    {
        var resultado = await Db.ScriptEvaluateAsync(ScriptEntrar,
            [Espera(eventoId), Sequencia(eventoId), Passe(eventoId, usuarioId), FilasAtivas],
            [usuarioId, eventoId]);
        return Ler(resultado);
    }

    public async Task<PosicaoNaFila> ConsultarAsync(int eventoId, int usuarioId, CancellationToken ct)
    {
        var resultado = await Db.ScriptEvaluateAsync(ScriptConsultar,
            [Espera(eventoId), Passe(eventoId, usuarioId)],
            [usuarioId]);
        return Ler(resultado);
    }

    public async Task<ResultadoDaAdmissao> AdmitirAsync(int eventoId, CancellationToken ct)
    {
        var o = opcoes.Value;
        var argumentos = new List<RedisValue>
        {
            o.CompradoresSimultaneos,
            (long)TimeSpan.FromMinutes(o.ValidadeDoPasseEmMinutos).TotalMilliseconds,
            relogio.GetUtcNow().ToUnixTimeMilliseconds(),
            PrefixoDoPasse(eventoId)
        };

        // Um passe aleatório para cada vaga possível; o script usa só os que precisar.
        // O lote é limitado para não enviar milhares de valores por rodada.
        var lote = Math.Min(o.CompradoresSimultaneos, 500);
        for (var i = 0; i < lote; i++)
        {
            argumentos.Add(codigos.GerarTokenSeguro(24));
        }

        var r = (RedisResult[]?)await Db.ScriptEvaluateAsync(ScriptAdmitir,
                    [Espera(eventoId), Ativos(eventoId)], [.. argumentos])
                ?? throw new InvalidOperationException("Resposta inesperada do Redis.");
        return new ResultadoDaAdmissao((int)r[0], (long)r[1]);
    }

    public async Task<IReadOnlyList<int>> ListarEventosComFilaAsync(CancellationToken ct) =>
        (await Db.SetMembersAsync(FilasAtivas)).Select(v => (int)v).ToList();

    public async Task EncerrarSeVaziaAsync(int eventoId, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().ToUnixTimeMilliseconds();
        await Db.SortedSetRemoveRangeByScoreAsync(Ativos(eventoId), double.NegativeInfinity, agora);

        // Só sai da lista quando ninguém espera e ninguém está comprando.
        if (await Db.SortedSetLengthAsync(Espera(eventoId)) == 0 &&
            await Db.SortedSetLengthAsync(Ativos(eventoId)) == 0)
        {
            await Db.SetRemoveAsync(FilasAtivas, eventoId);
        }
    }

    public async Task<bool> UsarPasseAsync(int eventoId, int usuarioId, string passe, CancellationToken ct) =>
        (int)await Db.ScriptEvaluateAsync(ScriptUsarPasse, [Passe(eventoId, usuarioId)], [passe]) == 1;

    public async Task DevolverPasseAsync(int eventoId, int usuarioId, string passe, CancellationToken ct) =>
        await Db.ScriptEvaluateAsync(ScriptDevolverPasse, [Passe(eventoId, usuarioId)], [passe]);

    public async Task ConcluirAsync(int eventoId, int usuarioId, CancellationToken ct) =>
        await Db.ScriptEvaluateAsync(ScriptConcluir,
            [Ativos(eventoId), Passe(eventoId, usuarioId) + ":usado"], [usuarioId]);

    private static PosicaoNaFila Ler(RedisResult resultado)
    {
        var partes = (string?[]?)resultado ?? [];
        return partes.ElementAtOrDefault(0) switch
        {
            "liberado" => new PosicaoNaFila(SituacaoNaFila.Liberado, 0, partes[1]),
            "aguardando" => new PosicaoNaFila(
                SituacaoNaFila.Aguardando, long.Parse(partes[1]!, CultureInfo.InvariantCulture), null),
            _ => new PosicaoNaFila(SituacaoNaFila.Fora, 0, null)
        };
    }

    /// <summary>Os scripts ficam em arquivos .lua embutidos na DLL (e podem ser testados no redis-cli).</summary>
    internal static string Carregar(string nome)
    {
        using var stream = Assembly.GetExecutingAssembly()
                               .GetManifestResourceStream($"Ingressa.Infrastructure.Scripts.{nome}.lua")
                           ?? throw new InvalidOperationException($"Script Lua '{nome}' não encontrado.");
        using var leitor = new StreamReader(stream);
        return leitor.ReadToEnd();
    }
}
