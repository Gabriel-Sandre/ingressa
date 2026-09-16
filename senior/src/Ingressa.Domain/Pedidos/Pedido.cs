using Ingressa.Domain.Comum;
using Ingressa.Domain.Eventos;

namespace Ingressa.Domain.Pedidos;

public enum StatusPedido
{
    AguardandoPagamento,
    Pago,
    Expirado,
    Cancelado
}

/// <summary>
/// Máquina de estados do pedido:
/// <code>
/// AguardandoPagamento ──pagar──▶ Pago ──cancelar──▶ Cancelado
///        │  └────────desistir────────────────────▶ Cancelado
///        └──prazo vence──▶ Expirado
/// </code>
/// A coluna de versão (concorrência otimista) impede que "pagar" e "expirar"
/// aconteçam ao mesmo tempo para o mesmo pedido.
/// </summary>
public class Pedido : Entidade
{
    public const int LimiteIngressosPorPedido = 6;
    public static readonly TimeSpan PrazoDaReserva = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan AntecedenciaMinimaParaCancelar = TimeSpan.FromHours(24);

    private readonly List<ItemPedido> _itens = [];
    private readonly List<Ingresso> _ingressos = [];

    private Pedido() { } // EF Core

    public int UsuarioId { get; private set; }
    public int EventoId { get; private set; }
    public StatusPedido Status { get; private set; }
    public decimal Total { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime ExpiraEm { get; private set; }
    public DateTime? PagoEm { get; private set; }
    public DateTime? FinalizadoEm { get; private set; }
    public string? CodigoPagamento { get; private set; }

    /// <summary>Token de concorrência otimista (no PostgreSQL, a coluna de sistema xmin).</summary>
    public uint Versao { get; private set; }

    public IReadOnlyList<ItemPedido> Itens => _itens;
    public IReadOnlyList<Ingresso> Ingressos => _ingressos;

    public bool IngressosEmitidos => _ingressos.Count > 0;

    /// <summary>
    /// Cria o pedido já com a reserva. Quem chama é responsável por reservar o estoque
    /// no banco na mesma transação.
    /// </summary>
    public static Pedido Reservar(int usuarioId, Evento evento, IEnumerable<(int SetorId, int Quantidade)> itens, DateTime agora)
    {
        if (!evento.VendasAbertas(agora))
        {
            throw new RegraDeNegocioException("As vendas deste evento não estão abertas.");
        }

        var agrupados = itens
            .GroupBy(i => i.SetorId)
            .Select(g => (SetorId: g.Key, Quantidade: g.Sum(i => i.Quantidade)))
            .ToList();

        if (agrupados.Exists(i => i.Quantidade < 1))
        {
            throw new RegraDeNegocioException("A quantidade de cada item deve ser maior que zero.");
        }

        var total = agrupados.Sum(i => i.Quantidade);
        if (total is < 1 or > LimiteIngressosPorPedido)
        {
            throw new RegraDeNegocioException($"Cada pedido deve ter entre 1 e {LimiteIngressosPorPedido} ingressos.");
        }

        var pedido = new Pedido
        {
            UsuarioId = usuarioId,
            EventoId = evento.Id,
            Status = StatusPedido.AguardandoPagamento,
            CriadoEm = agora,
            ExpiraEm = agora.Add(PrazoDaReserva)
        };

        foreach (var (setorId, quantidade) in agrupados.OrderBy(i => i.SetorId))
        {
            var setor = evento.ObterSetor(setorId);
            pedido._itens.Add(new ItemPedido(setor.Id, setor.Nome, quantidade, setor.Preco));
        }

        pedido.Total = pedido._itens.Sum(i => i.Subtotal);
        return pedido;
    }

    public void GarantirQuePertenceA(int usuarioId)
    {
        // 404 e não 403: não confirmamos que o pedido de outra pessoa existe.
        if (UsuarioId != usuarioId)
        {
            throw new NaoEncontradoException($"Pedido {Id} não encontrado.");
        }
    }

    public void ConfirmarPagamento(string codigoPagamento, DateTime agora)
    {
        GarantirStatus(StatusPedido.AguardandoPagamento, "Este pedido não está aguardando pagamento.");

        if (agora >= ExpiraEm)
        {
            throw new RegraDeNegocioException("O prazo da reserva terminou. Faça um novo pedido.");
        }

        Status = StatusPedido.Pago;
        PagoEm = agora;
        CodigoPagamento = codigoPagamento;
        Registrar(new PedidoPago(Id, agora));
    }

    /// <summary>Retorna falso (sem erro) quando o pedido não está mais elegível — útil para o job de expiração.</summary>
    public bool TentarExpirar(DateTime agora)
    {
        if (Status != StatusPedido.AguardandoPagamento || agora < ExpiraEm)
        {
            return false;
        }

        Status = StatusPedido.Expirado;
        FinalizadoEm = agora;
        Registrar(new PedidoExpirado(Id, agora));
        return true;
    }

    public void Cancelar(DateTime dataDoEvento, DateTime agora)
    {
        if (Status is StatusPedido.Cancelado or StatusPedido.Expirado)
        {
            throw new ConflitoException("Este pedido já foi encerrado.");
        }

        if (Status == StatusPedido.Pago && dataDoEvento - agora < AntecedenciaMinimaParaCancelar)
        {
            throw new RegraDeNegocioException(
                $"Pedidos pagos só podem ser cancelados até {AntecedenciaMinimaParaCancelar.TotalHours:0} horas antes do evento.");
        }

        var estavaPago = Status == StatusPedido.Pago;
        Status = StatusPedido.Cancelado;
        FinalizadoEm = agora;
        Registrar(new PedidoCancelado(Id, estavaPago, agora));
    }

    /// <summary>
    /// Emite um ingresso por lugar. Idempotente: se a mensagem "pedido pago" chegar
    /// duas vezes, a segunda chamada não faz nada e retorna falso.
    /// </summary>
    public bool EmitirIngressos(Func<string> gerarCodigo, DateTime agora)
    {
        if (Status != StatusPedido.Pago || IngressosEmitidos)
        {
            return false;
        }

        foreach (var item in _itens)
        {
            for (var i = 0; i < item.Quantidade; i++)
            {
                _ingressos.Add(new Ingresso(item.SetorId, gerarCodigo(), item.PrecoUnitario));
            }
        }

        Registrar(new IngressosEmitidos(Id, _ingressos.Count, agora));
        return true;
    }

    private void GarantirStatus(StatusPedido esperado, string mensagem)
    {
        if (Status != esperado)
        {
            throw new ConflitoException(mensagem);
        }
    }
}

public class ItemPedido
{
    private ItemPedido() { } // EF Core

    internal ItemPedido(int setorId, string nomeSetor, int quantidade, decimal precoUnitario)
    {
        SetorId = setorId;
        NomeSetor = nomeSetor;
        Quantidade = quantidade;
        PrecoUnitario = precoUnitario;
    }

    public int Id { get; private set; }
    public int PedidoId { get; private set; }
    public int SetorId { get; private set; }
    public string NomeSetor { get; private set; } = string.Empty;
    public int Quantidade { get; private set; }
    public decimal PrecoUnitario { get; private set; }
    public decimal Subtotal => PrecoUnitario * Quantidade;
}

public class Ingresso
{
    private Ingresso() { } // EF Core

    internal Ingresso(int setorId, string codigo, decimal precoPago)
    {
        SetorId = setorId;
        Codigo = codigo;
        PrecoPago = precoPago;
    }

    public int Id { get; private set; }
    public int PedidoId { get; private set; }
    public int SetorId { get; private set; }
    public string Codigo { get; private set; } = string.Empty;
    public decimal PrecoPago { get; private set; }
}
