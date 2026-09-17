using System.Globalization;
using Ingressa.Application.Abstracoes;
using Ingressa.Application.Eventos;
using Ingressa.Infrastructure.Persistencia;
using Microsoft.Extensions.Caching.Hybrid;

namespace Ingressa.Infrastructure.Cache;

/// <summary>
/// Cache das leituras mais acessadas (vitrine e detalhe do evento), no padrão decorator:
/// a consulta real não sabe que existe cache.
/// </summary>
/// <remarks>
/// O HybridCache guarda em memória (L1) e no Redis (L2), e protege contra "estouro de cache"
/// (cache stampede): se mil requisições chegam com a chave vencida, só uma vai ao banco.
/// A validade é curta de propósito: a disponibilidade mostrada pode ficar alguns segundos
/// atrasada, mas a reserva sempre confere o estoque no banco, de forma atômica.
/// </remarks>
internal sealed class ConsultasDeEventosEmCache(ConsultasDeEventos consultas, HybridCache cache) : IConsultasDeEventos
{
    public const string TagVitrine = "vitrine";

    internal static readonly HybridCacheEntryOptions Validade = new()
    {
        Expiration = TimeSpan.FromSeconds(10),
        LocalCacheExpiration = TimeSpan.FromSeconds(5)
    };

    public async Task<PaginaResponse<EventoResumoResponse>> ListarVitrineAsync(
        EventoFiltro filtro, DateTime agora, CancellationToken ct)
    {
        var chave = string.Create(CultureInfo.InvariantCulture,
            $"vitrine:{filtro.Busca?.Trim().ToLowerInvariant()}:{filtro.Cidade?.Trim().ToLowerInvariant()}:{filtro.Ordem}:{filtro.Pagina}:{filtro.TamanhoPagina}");

        // "agora" não entra na chave: dentro da validade do cache, a diferença é irrelevante.
        return await cache.GetOrCreateAsync(
            chave,
            (consultas, filtro, agora),
            static (estado, token) => new ValueTask<PaginaResponse<EventoResumoResponse>>(
                estado.consultas.ListarVitrineAsync(estado.filtro, estado.agora, token)),
            Validade,
            [TagVitrine],
            ct);
    }

    public async Task<EventoDetalheResponse?> ObterDetalheAsync(int id, CancellationToken ct) =>
        await cache.GetOrCreateAsync(
            ChaveDoEvento(id),
            (consultas, id),
            static (estado, token) => new ValueTask<EventoDetalheResponse?>(
                estado.consultas.ObterDetalheAsync(estado.id, token)),
            Validade,
            [TagVitrine, ChaveDoEvento(id)],
            ct);

    internal static string ChaveDoEvento(int id) => string.Create(CultureInfo.InvariantCulture, $"evento:{id}");
}

/// <summary>Invalida o cache quando um organizador altera um evento.</summary>
public sealed class InvalidadorDeCache(HybridCache cache) : IInvalidadorDeCache
{
    public async Task EventoAlteradoAsync(int eventoId, CancellationToken ct)
    {
        await cache.RemoveByTagAsync(ConsultasDeEventosEmCache.ChaveDoEvento(eventoId), ct);
        await cache.RemoveByTagAsync(ConsultasDeEventosEmCache.TagVitrine, ct);
    }
}
