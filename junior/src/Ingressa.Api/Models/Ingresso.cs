namespace Ingressa.Api.Models;

public class Ingresso
{
    public int Id { get; set; }

    public int PedidoId { get; set; }
    public Pedido? Pedido { get; set; }

    public int SetorId { get; set; }
    public Setor? Setor { get; set; }

    /// <summary>Código único e imprevisível apresentado na entrada do evento.</summary>
    public string Codigo { get; set; } = string.Empty;

    /// <summary>Preço no momento da compra (o preço do setor pode mudar depois).</summary>
    public decimal PrecoPago { get; set; }
}
