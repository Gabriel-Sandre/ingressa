using Ingressa.Domain.Comum;

namespace Ingressa.Domain.Pedidos;

// Os eventos carregam só o identificador. Quem consome busca os dados atuais —
// assim a mensagem nunca carrega informação pessoal desatualizada.

public sealed record PedidoPago(int PedidoId, DateTime OcorridoEm) : IEventoDeDominio;

public sealed record PedidoExpirado(int PedidoId, DateTime OcorridoEm) : IEventoDeDominio;

public sealed record PedidoCancelado(int PedidoId, bool EstavaPago, DateTime OcorridoEm) : IEventoDeDominio;

public sealed record IngressosEmitidos(int PedidoId, int Quantidade, DateTime OcorridoEm) : IEventoDeDominio;
