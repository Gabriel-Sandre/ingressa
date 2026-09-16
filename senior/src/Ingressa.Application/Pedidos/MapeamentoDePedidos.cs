using Ingressa.Domain.Pedidos;

namespace Ingressa.Application.Pedidos;

public static class MapeamentoDePedidos
{
    public static PedidoResponse ParaResponse(Pedido p, string tituloEvento, DateTime dataEvento)
    {
        var nomes = p.Itens.ToDictionary(i => i.SetorId, i => i.NomeSetor);
        return new PedidoResponse(
            p.Id,
            p.UsuarioId,
            p.EventoId,
            tituloEvento,
            dataEvento,
            p.Status,
            p.Total,
            p.CriadoEm,
            p.ExpiraEm,
            p.PagoEm,
            p.IngressosEmitidos,
            p.Itens.Select(i => new ItemPedidoResponse(i.SetorId, i.NomeSetor, i.Quantidade, i.PrecoUnitario, i.Subtotal)).ToList(),
            p.Ingressos
                .OrderBy(i => i.Id)
                .Select(i => new IngressoResponse(i.Id, i.Codigo, nomes.GetValueOrDefault(i.SetorId, string.Empty), i.PrecoPago))
                .ToList());
    }
}
