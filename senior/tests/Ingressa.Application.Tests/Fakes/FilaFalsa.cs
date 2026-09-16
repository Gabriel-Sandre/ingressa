using Ingressa.Application.Abstracoes;

namespace Ingressa.Application.Tests.Fakes;

/// <summary>
/// Fila virtual em memória com as mesmas regras dos scripts Lua (ordem de chegada,
/// limite de compradores, passe de uso único que pode ser devolvido).
/// A implementação real é testada contra Redis nos testes de integração.
/// </summary>
public sealed class FilaFalsa(int compradoresSimultaneos = 2) : IFilaVirtual, IInvalidadorDeCache
{
    private readonly object _trava = new();
    private readonly Dictionary<int, List<int>> _espera = [];
    private readonly Dictionary<(int Evento, int Usuario), string> _passes = [];
    private readonly Dictionary<(int Evento, int Usuario), string> _usados = [];
    private readonly Dictionary<int, HashSet<int>> _ativos = [];
    private int _sequencia;

    public List<int> EventosInvalidados { get; } = [];

    public Task<PosicaoNaFila> EntrarAsync(int eventoId, int usuarioId, CancellationToken ct)
    {
        lock (_trava)
        {
            var fila = Fila(eventoId);
            if (!_passes.ContainsKey((eventoId, usuarioId)) && !fila.Contains(usuarioId))
            {
                fila.Add(usuarioId);
            }

            return Task.FromResult(Situacao(eventoId, usuarioId));
        }
    }

    public Task<PosicaoNaFila> ConsultarAsync(int eventoId, int usuarioId, CancellationToken ct)
    {
        lock (_trava)
        {
            return Task.FromResult(Situacao(eventoId, usuarioId));
        }
    }

    public Task<ResultadoDaAdmissao> AdmitirAsync(int eventoId, CancellationToken ct)
    {
        lock (_trava)
        {
            var fila = Fila(eventoId);
            var ativos = Ativos(eventoId);
            var liberados = 0;
            while (fila.Count > 0 && ativos.Count < compradoresSimultaneos)
            {
                var usuario = fila[0];
                fila.RemoveAt(0);
                ativos.Add(usuario);
                _passes[(eventoId, usuario)] = $"passe-{++_sequencia}";
                liberados++;
            }

            return Task.FromResult(new ResultadoDaAdmissao(liberados, fila.Count));
        }
    }

    public Task<IReadOnlyList<int>> ListarEventosComFilaAsync(CancellationToken ct)
    {
        lock (_trava)
        {
            return Task.FromResult<IReadOnlyList<int>>(
                _espera.Where(e => e.Value.Count > 0).Select(e => e.Key)
                    .Union(_ativos.Where(a => a.Value.Count > 0).Select(a => a.Key))
                    .ToList());
        }
    }

    public Task EncerrarSeVaziaAsync(int eventoId, CancellationToken ct) => Task.CompletedTask;

    public Task<bool> UsarPasseAsync(int eventoId, int usuarioId, string passe, CancellationToken ct)
    {
        lock (_trava)
        {
            if (_passes.TryGetValue((eventoId, usuarioId), out var atual) && atual == passe)
            {
                _passes.Remove((eventoId, usuarioId));
                _usados[(eventoId, usuarioId)] = passe;
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
    }

    public Task DevolverPasseAsync(int eventoId, int usuarioId, string passe, CancellationToken ct)
    {
        lock (_trava)
        {
            if (_usados.Remove((eventoId, usuarioId), out var usado) && usado == passe)
            {
                _passes[(eventoId, usuarioId)] = passe;
            }
        }

        return Task.CompletedTask;
    }

    public Task ConcluirAsync(int eventoId, int usuarioId, CancellationToken ct)
    {
        lock (_trava)
        {
            Ativos(eventoId).Remove(usuarioId);
            _usados.Remove((eventoId, usuarioId));
        }

        return Task.CompletedTask;
    }

    public Task EventoAlteradoAsync(int eventoId, CancellationToken ct)
    {
        EventosInvalidados.Add(eventoId);
        return Task.CompletedTask;
    }

    public int CompradoresAtivos(int eventoId)
    {
        lock (_trava)
        {
            return Ativos(eventoId).Count;
        }
    }

    private PosicaoNaFila Situacao(int eventoId, int usuarioId)
    {
        if (_passes.TryGetValue((eventoId, usuarioId), out var passe))
        {
            return new PosicaoNaFila(SituacaoNaFila.Liberado, 0, passe);
        }

        var posicao = Fila(eventoId).IndexOf(usuarioId);
        return posicao >= 0
            ? new PosicaoNaFila(SituacaoNaFila.Aguardando, posicao + 1, null)
            : new PosicaoNaFila(SituacaoNaFila.Fora, 0, null);
    }

    private List<int> Fila(int eventoId) =>
        _espera.TryGetValue(eventoId, out var fila) ? fila : _espera[eventoId] = [];

    private HashSet<int> Ativos(int eventoId) =>
        _ativos.TryGetValue(eventoId, out var ativos) ? ativos : _ativos[eventoId] = [];
}
