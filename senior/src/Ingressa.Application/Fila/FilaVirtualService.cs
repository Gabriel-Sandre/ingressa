using Ingressa.Application.Abstracoes;
using Ingressa.Application.Observabilidade;
using Ingressa.Domain.Comum;
using Microsoft.Extensions.Logging;

namespace Ingressa.Application.Fila;

public sealed record FilaResponse(int EventoId, SituacaoNaFila Situacao, long Posicao, string? Passe);

public sealed class FilaVirtualService(
    IEventoRepositorio eventos,
    IFilaVirtual fila,
    TimeProvider relogio,
    ILogger<FilaVirtualService> logger)
{
    public async Task<FilaResponse> EntrarAsync(int eventoId, int usuarioId, CancellationToken ct)
    {
        var evento = await eventos.ObterAsync(eventoId, ct);
        if (evento is null || !evento.Publicado)
        {
            throw new NaoEncontradoException($"Evento {eventoId} não encontrado.");
        }

        if (!evento.FilaVirtual)
        {
            throw new RegraDeNegocioException("Este evento não usa fila virtual: a reserva pode ser feita diretamente.");
        }

        if (!evento.VendasAbertas(relogio.GetUtcNow().UtcDateTime))
        {
            throw new RegraDeNegocioException("As vendas deste evento não estão abertas.");
        }

        var posicao = await fila.EntrarAsync(eventoId, usuarioId, ct);
        return ParaResponse(eventoId, posicao);
    }

    public async Task<FilaResponse> ConsultarAsync(int eventoId, int usuarioId, CancellationToken ct) =>
        ParaResponse(eventoId, await fila.ConsultarAsync(eventoId, usuarioId, ct));

    /// <summary>Executado periodicamente pelo Worker: libera os próximos de cada fila ativa.</summary>
    public async Task<int> AdmitirEmTodasAsync(CancellationToken ct)
    {
        var total = 0;
        foreach (var eventoId in await fila.ListarEventosComFilaAsync(ct))
        {
            var resultado = await fila.AdmitirAsync(eventoId, ct);
            total += resultado.Liberados;
            Metricas.CompradoresLiberados.Add(resultado.Liberados, new KeyValuePair<string, object?>("evento", eventoId));
            Metricas.RegistrarTamanhoDaFila(eventoId, resultado.AindaNaFila);

            if (resultado.Liberados > 0)
            {
                logger.LogInformation(
                    "Fila do evento {EventoId}: {Liberados} liberado(s), {Restantes} aguardando",
                    eventoId, resultado.Liberados, resultado.AindaNaFila);
            }

            if (resultado.AindaNaFila == 0)
            {
                await fila.EncerrarSeVaziaAsync(eventoId, ct);
            }
        }

        return total;
    }

    private static FilaResponse ParaResponse(int eventoId, PosicaoNaFila p) =>
        new(eventoId, p.Situacao, p.Posicao, p.Passe);
}
