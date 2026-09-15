namespace Ingressa.Api.Models;

public class Pedido
{
    public int Id { get; set; }

    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }

    public int EventoId { get; set; }
    public Evento? Evento { get; set; }

    public DateTime CriadoEm { get; set; }
    public StatusPedido Status { get; set; }

    /// <summary>Soma dos preços pagos. Guardado para não depender do preço atual do setor.</summary>
    public decimal Total { get; set; }

    public List<Ingresso> Ingressos { get; set; } = [];
}
