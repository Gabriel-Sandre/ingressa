using System.ComponentModel.DataAnnotations;
using Ingressa.Domain.Pedidos;

namespace Ingressa.Application.Pedidos;

public sealed record ItemPedidoRequest(
    int SetorId,
    [Range(1, 6, ErrorMessage = "A quantidade por setor deve estar entre 1 e 6.")] int Quantidade);

public sealed record CriarPedidoRequest(
    int EventoId,
    [Required(ErrorMessage = "Informe os itens do pedido."), MinLength(1, ErrorMessage = "O pedido precisa de pelo menos um item."), MaxLength(6)]
    IReadOnlyList<ItemPedidoRequest> Itens);

public sealed record PagamentoRequest(
    [Required(ErrorMessage = "Informe o token de pagamento."), StringLength(100)] string TokenDePagamento);

public sealed record ItemPedidoResponse(int SetorId, string Setor, int Quantidade, decimal PrecoUnitario, decimal Subtotal);

public sealed record IngressoResponse(int Id, string Codigo, string Setor, decimal PrecoPago);

public sealed record PedidoResponse(
    int Id,
    int UsuarioId,
    int EventoId,
    string Evento,
    DateTime DataEvento,
    StatusPedido Status,
    decimal Total,
    DateTime CriadoEm,
    DateTime ExpiraEm,
    DateTime? PagoEm,
    bool IngressosEmitidos,
    IReadOnlyList<ItemPedidoResponse> Itens,
    IReadOnlyList<IngressoResponse> Ingressos);
