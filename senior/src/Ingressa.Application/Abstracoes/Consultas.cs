using Ingressa.Application.Eventos;
using Ingressa.Application.Pedidos;

namespace Ingressa.Application.Abstracoes;

/// <summary>
/// Leituras otimizadas (projeções direto do banco), separadas das escritas que
/// passam pelo domínio. É uma forma leve de CQRS.
/// </summary>
public interface IConsultasDeEventos
{
    Task<PaginaResponse<EventoResumoResponse>> ListarVitrineAsync(EventoFiltro filtro, DateTime agora, CancellationToken ct);
    Task<EventoDetalheResponse?> ObterDetalheAsync(int id, CancellationToken ct);
}

public interface IConsultasDePedidos
{
    Task<PedidoResponse?> ObterAsync(int pedidoId, CancellationToken ct);
    Task<IReadOnlyList<PedidoResponse>> ListarDoUsuarioAsync(int usuarioId, CancellationToken ct);
    Task<DadosParaNotificacao?> ObterDadosParaNotificacaoAsync(int pedidoId, CancellationToken ct);
}

public sealed record DadosParaNotificacao(
    int PedidoId,
    string NomeCliente,
    string EmailCliente,
    string Evento,
    DateTime DataEvento,
    string Local,
    decimal Total,
    IReadOnlyList<string> CodigosIngressos);

public interface IEnviadorDeEmail
{
    Task EnviarAsync(string para, string assunto, string corpoTexto, CancellationToken ct);
}

/// <summary>Avisa o cache de leitura que um evento mudou.</summary>
public interface IInvalidadorDeCache
{
    Task EventoAlteradoAsync(int eventoId, CancellationToken ct);
}
