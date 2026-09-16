using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Ingressa.Application.Observabilidade;

/// <summary>
/// Métricas e rastreamento de negócio. Usa só as APIs do próprio .NET
/// (System.Diagnostics); o OpenTelemetry, configurado na API e no Worker,
/// coleta e exporta esses dados.
/// </summary>
public static class Metricas
{
    public const string Nome = "Ingressa";

    public static readonly ActivitySource Rastreamento = new(Nome);

    private static readonly Meter Medidor = new(Nome);

    /// <summary>Reservas por resultado: sucesso, sem_estoque, recusada.</summary>
    public static readonly Counter<long> Reservas =
        Medidor.CreateCounter<long>("ingressa.reservas", unit: "{reserva}", description: "Tentativas de reserva por resultado");

    /// <summary>Ingressos reservados com sucesso.</summary>
    public static readonly Counter<long> IngressosReservados =
        Medidor.CreateCounter<long>("ingressa.ingressos.reservados", unit: "{ingresso}", description: "Ingressos reservados");

    /// <summary>Pagamentos por resultado: aprovado, recusado, estornado.</summary>
    public static readonly Counter<long> Pagamentos =
        Medidor.CreateCounter<long>("ingressa.pagamentos", unit: "{pagamento}", description: "Pagamentos por resultado");

    public static readonly Counter<long> ReservasExpiradas =
        Medidor.CreateCounter<long>("ingressa.reservas.expiradas", unit: "{reserva}", description: "Reservas que venceram sem pagamento");

    public static readonly Counter<long> IngressosEmitidos =
        Medidor.CreateCounter<long>("ingressa.ingressos.emitidos", unit: "{ingresso}", description: "Ingressos emitidos pelo Worker");

    public static readonly Counter<long> CompradoresLiberados =
        Medidor.CreateCounter<long>("ingressa.fila.liberados", unit: "{usuario}", description: "Pessoas liberadas da fila virtual");

    public static readonly Counter<long> RequisicoesLimitadas =
        Medidor.CreateCounter<long>("ingressa.limite.recusadas", unit: "{requisicao}", description: "Requisições recusadas por limite");

    public static readonly Histogram<double> DuracaoDoPagamento =
        Medidor.CreateHistogram<double>("ingressa.pagamento.duracao", unit: "s", description: "Tempo de resposta do gateway de pagamento");

    private static readonly ConcurrentDictionary<int, long> TamanhoDasFilas = new();

    static Metricas()
    {
        Medidor.CreateObservableGauge(
            "ingressa.fila.tamanho",
            () => TamanhoDasFilas.Select(f => new Measurement<long>(f.Value, new KeyValuePair<string, object?>("evento", f.Key))),
            unit: "{usuario}",
            description: "Pessoas aguardando na fila virtual");
    }

    public static void RegistrarTamanhoDaFila(int eventoId, long tamanho)
    {
        if (tamanho == 0)
        {
            TamanhoDasFilas.TryRemove(eventoId, out _);
        }
        else
        {
            TamanhoDasFilas[eventoId] = tamanho;
        }
    }

    public static KeyValuePair<string, object?> Resultado(string valor) => new("resultado", valor);
}
