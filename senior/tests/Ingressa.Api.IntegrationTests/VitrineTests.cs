using System.Net;
using System.Net.Http.Json;
using Ingressa.Application.Eventos;
using Ingressa.Domain.Usuarios;

namespace Ingressa.Api.IntegrationTests;

[Collection(ColecaoApi.Nome)]
public sealed class VitrineTests(ApiFactory api)
{
    [Fact]
    public async Task Busca_TrataCuringasComoTextoEOrdenaPorPreco()
    {
        var marcador = Guid.NewGuid().ToString("N")[..8];
        var (_, organizadorId) = await api.EntrarComoAsync(PerfilUsuario.Organizador);
        await api.CriarEventoAsync(organizadorId, 10, $"Show 100% {marcador}");
        await api.CriarEventoAsync(organizadorId, 10, $"Show 100 {marcador}");
        var cliente = api.NovoCliente();

        var comPercentual = await cliente.GetFromJsonAsync<PaginaResponse<EventoResumoResponse>>(
            $"/api/eventos?busca={Uri.EscapeDataString("100%")}&tamanhoPagina=50", Json.Opcoes);
        Assert.Contains(comPercentual!.Itens, e => e.Titulo.Contains(marcador, StringComparison.Ordinal) && e.Titulo.Contains('%', StringComparison.Ordinal));
        Assert.DoesNotContain(comPercentual.Itens, e => e.Titulo == $"Show 100 {marcador}");

        var porMarcador = await cliente.GetFromJsonAsync<PaginaResponse<EventoResumoResponse>>(
            $"/api/eventos?busca={marcador}&ordem=Preco", Json.Opcoes);
        Assert.Equal(2, porMarcador!.TotalItens);
        Assert.All(porMarcador.Itens, e => Assert.Equal(50m, e.PrecoAPartirDe));
    }

    [Fact]
    public async Task Paginacao_InvalidaRetorna400()
    {
        var resposta = await api.NovoCliente().GetAsync("/api/eventos?tamanhoPagina=500");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }
}
