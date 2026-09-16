using System.Reflection;
using Ingressa.Application.Abstracoes;
using Ingressa.Application.Eventos;
using Ingressa.Application.Pedidos;
using Ingressa.Domain.Comum;
using Ingressa.Domain.Eventos;
using Ingressa.Domain.Pedidos;
using Ingressa.Domain.Usuarios;

namespace Ingressa.Application.Tests.Fakes;

/// <summary>
/// Implementação em memória das portas de persistência, suficiente para testar os casos de uso
/// sem banco. Reproduz o que importa para as regras: Ids, transação com rollback,
/// operações atômicas de estoque e a coleta dos eventos de domínio (outbox).
/// O comportamento real (SQL, xmin, SKIP LOCKED) é coberto pelos testes de integração.
/// </summary>
public sealed class BancoEmMemoria :
    IUsuarioRepositorio, IRefreshTokenRepositorio, IEventoRepositorio, IPedidoRepositorio,
    IEstoque, IUnidadeDeTrabalho, IConsultasDeEventos, IConsultasDePedidos
{
    private readonly object _trava = new();
    private readonly AsyncLocal<bool> _emTransacao = new();
    private int _proximoId = 1;

    public List<Usuario> Usuarios { get; } = [];
    public List<RefreshToken> Tokens { get; } = [];
    public List<Evento> Eventos { get; } = [];
    public List<Pedido> Pedidos { get; } = [];
    public List<IEventoDeDominio> Outbox { get; } = [];

    /// <summary>Permite ao teste interferir entre a leitura e a gravação (simular outra operação).</summary>
    public Action? AntesDeSalvar { get; set; }

    // ---------- Unidade de trabalho ----------

    public Task SalvarAsync(CancellationToken ct)
    {
        AntesDeSalvar?.Invoke();
        lock (_trava)
        {
            foreach (var entidade in Usuarios.Cast<object>().Concat(Eventos).Concat(Pedidos).Concat(Tokens))
            {
                GarantirId(entidade);
            }

            foreach (var evento in Eventos)
            {
                foreach (var setor in evento.Setores)
                {
                    GarantirId(setor);
                    Definir(setor, nameof(Setor.EventoId), evento.Id);
                }
            }

            foreach (var pedido in Pedidos)
            {
                foreach (var ingresso in pedido.Ingressos)
                {
                    GarantirId(ingresso);
                }
            }

            foreach (var entidade in Usuarios.Cast<Entidade>().Concat(Eventos).Concat(Pedidos))
            {
                Outbox.AddRange(entidade.Eventos);
                entidade.LimparEventos();
            }
        }

        return Task.CompletedTask;
    }

    public async Task<T> EmTransacaoAsync<T>(Func<CancellationToken, Task<T>> operacao, CancellationToken ct)
    {
        if (_emTransacao.Value)
        {
            return await operacao(ct);
        }

        Dictionary<Setor, int> ocupados;
        int pedidos;
        int outbox;
        lock (_trava)
        {
            ocupados = Eventos.SelectMany(e => e.Setores).ToDictionary(s => s, s => s.Ocupados);
            pedidos = Pedidos.Count;
            outbox = Outbox.Count;
        }

        _emTransacao.Value = true;
        try
        {
            return await operacao(ct);
        }
        catch
        {
            lock (_trava)
            {
                foreach (var (setor, valor) in ocupados)
                {
                    Definir(setor, nameof(Setor.Ocupados), valor);
                }

                Pedidos.RemoveRange(pedidos, Pedidos.Count - pedidos);
                Outbox.RemoveRange(outbox, Outbox.Count - outbox);
            }

            throw;
        }
        finally
        {
            _emTransacao.Value = false;
        }
    }

    // ---------- Estoque (atômico, como o UPDATE condicional) ----------

    public Task<bool> TentarOcuparAsync(int setorId, int quantidade, CancellationToken ct)
    {
        lock (_trava)
        {
            var setor = ObterSetor(setorId);
            if (setor.Capacidade - setor.Ocupados < quantidade)
            {
                return Task.FromResult(false);
            }

            Definir(setor, nameof(Setor.Ocupados), setor.Ocupados + quantidade);
            return Task.FromResult(true);
        }
    }

    public Task LiberarAsync(int setorId, int quantidade, CancellationToken ct)
    {
        lock (_trava)
        {
            var setor = ObterSetor(setorId);
            if (setor.Ocupados < quantidade)
            {
                throw new InvalidOperationException("Liberação maior que a ocupação.");
            }

            Definir(setor, nameof(Setor.Ocupados), setor.Ocupados - quantidade);
        }

        return Task.CompletedTask;
    }

    public Setor ObterSetor(int id) => Eventos.SelectMany(e => e.Setores).Single(s => s.Id == id);

    // ---------- Repositórios ----------

    Task<Usuario?> IUsuarioRepositorio.ObterAsync(int id, CancellationToken ct) => Task.FromResult(Usuarios.Find(u => u.Id == id));
    public Task<Usuario?> ObterPorEmailAsync(string emailNormalizado, CancellationToken ct) => Task.FromResult(Usuarios.Find(u => u.Email == emailNormalizado));
    public Task<bool> EmailExisteAsync(string emailNormalizado, CancellationToken ct) => Task.FromResult(Usuarios.Exists(u => u.Email == emailNormalizado));
    public Task<IReadOnlyList<Usuario>> ListarOrganizadoresPendentesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Usuario>>(Usuarios.Where(u => u.Perfil == PerfilUsuario.Organizador && u.Status == StatusConta.AguardandoAprovacao).ToList());
    public void Adicionar(Usuario usuario) => Usuarios.Add(usuario);

    public Task<RefreshToken?> ObterPorHashAsync(string tokenHash, CancellationToken ct) => Task.FromResult(Tokens.Find(t => t.TokenHash == tokenHash));
    public Task RevogarFamiliaAsync(Guid familia, DateTime agora, CancellationToken ct)
    {
        Tokens.Where(t => t.Familia == familia).ToList().ForEach(t => t.Revogar(agora));
        return Task.CompletedTask;
    }
    public void Adicionar(RefreshToken token) => Tokens.Add(token);

    Task<Evento?> IEventoRepositorio.ObterAsync(int id, CancellationToken ct) => Task.FromResult(Eventos.Find(e => e.Id == id));
    public Task<IReadOnlyList<Evento>> ListarDoOrganizadorAsync(int organizadorId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Evento>>(Eventos.Where(e => e.OrganizadorId == organizadorId).ToList());
    public void Adicionar(Evento evento) => Eventos.Add(evento);
    public void Remover(Evento evento) => Eventos.Remove(evento);

    Task<Pedido?> IPedidoRepositorio.ObterAsync(int id, CancellationToken ct)
    {
        lock (_trava)
        {
            return Task.FromResult(Pedidos.Find(p => p.Id == id));
        }
    }

    public Task<IReadOnlyList<int>> ListarIdsDeReservasVencidasAsync(DateTime agora, int limite, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<int>>(Pedidos
            .Where(p => p.Status == StatusPedido.AguardandoPagamento && p.ExpiraEm <= agora)
            .Select(p => p.Id).Take(limite).ToList());

    public void Adicionar(Pedido pedido)
    {
        lock (_trava)
        {
            Pedidos.Add(pedido);
        }
    }

    // ---------- Consultas ----------

    public Task<PaginaResponse<EventoResumoResponse>> ListarVitrineAsync(EventoFiltro filtro, DateTime agora, CancellationToken ct)
    {
        var itens = Eventos.Where(e => e.Publicado && e.DataInicio > agora)
            .OrderBy(e => e.DataInicio)
            .Select(e => new EventoResumoResponse(e.Id, e.Titulo, e.Local, e.Cidade, e.DataInicio,
                e.Setores.Count == 0 ? null : e.Setores.Min(s => s.Preco), e.Setores.All(s => s.Disponiveis == 0)))
            .ToList();
        return Task.FromResult(Paginas.Criar<EventoResumoResponse>(itens, 1, 50, itens.Count));
    }

    public Task<EventoDetalheResponse?> ObterDetalheAsync(int id, CancellationToken ct)
    {
        var evento = Eventos.Find(e => e.Id == id);
        return Task.FromResult(evento is null ? null : MapeamentoDeEventos.ParaDetalhe(evento));
    }

    Task<PedidoResponse?> IConsultasDePedidos.ObterAsync(int pedidoId, CancellationToken ct)
    {
        Pedido? pedido;
        lock (_trava)
        {
            pedido = Pedidos.Find(p => p.Id == pedidoId);
        }

        if (pedido is null)
        {
            return Task.FromResult<PedidoResponse?>(null);
        }

        var evento = Eventos.Single(e => e.Id == pedido.EventoId);
        return Task.FromResult<PedidoResponse?>(MapeamentoDePedidos.ParaResponse(pedido, evento.Titulo, evento.DataInicio));
    }

    public Task<IReadOnlyList<PedidoResponse>> ListarDoUsuarioAsync(int usuarioId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PedidoResponse>>(Pedidos.Where(p => p.UsuarioId == usuarioId)
            .Select(p =>
            {
                var e = Eventos.Single(x => x.Id == p.EventoId);
                return MapeamentoDePedidos.ParaResponse(p, e.Titulo, e.DataInicio);
            }).ToList());

    public Task<DadosParaNotificacao?> ObterDadosParaNotificacaoAsync(int pedidoId, CancellationToken ct)
    {
        var p = Pedidos.Find(x => x.Id == pedidoId);
        if (p is null)
        {
            return Task.FromResult<DadosParaNotificacao?>(null);
        }

        var u = Usuarios.Single(x => x.Id == p.UsuarioId);
        var e = Eventos.Single(x => x.Id == p.EventoId);
        return Task.FromResult<DadosParaNotificacao?>(new DadosParaNotificacao(
            p.Id, u.Nome, u.Email, e.Titulo, e.DataInicio, e.Local, p.Total, p.Ingressos.Select(i => i.Codigo).ToList()));
    }

    // ---------- Auxiliares ----------

    private void GarantirId(object entidade)
    {
        var propriedade = entidade.GetType().GetProperty("Id")!;
        if (Convert.ToInt64(propriedade.GetValue(entidade), System.Globalization.CultureInfo.InvariantCulture) == 0)
        {
            propriedade.SetValue(entidade, _proximoId++);
        }
    }

    public static void Definir(object entidade, string propriedade, object valor) =>
        entidade.GetType().GetProperty(propriedade, BindingFlags.Instance | BindingFlags.Public)!.SetValue(entidade, valor);
}
