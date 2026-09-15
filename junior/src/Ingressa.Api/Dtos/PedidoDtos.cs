using System.ComponentModel.DataAnnotations;
using Ingressa.Api.Models;

namespace Ingressa.Api.Dtos;

public sealed record ItemPedidoRequest(
    int SetorId,
    [Range(1, 6, ErrorMessage = "A quantidade por setor deve estar entre 1 e 6.")] int Quantidade);

public sealed record CriarPedidoRequest(
    int EventoId,
    [Required(ErrorMessage = "Informe os itens do pedido."), MinLength(1, ErrorMessage = "O pedido precisa de pelo menos um item.")] IReadOnlyList<ItemPedidoRequest> Itens);

public sealed record IngressoResponse(int Id, string Codigo, string Setor, decimal PrecoPago);

public sealed record PedidoResponse(
    int Id,
    int EventoId,
    string Evento,
    DateTime DataEvento,
    DateTime CriadoEm,
    StatusPedido Status,
    decimal Total,
    IReadOnlyList<IngressoResponse> Ingressos);
