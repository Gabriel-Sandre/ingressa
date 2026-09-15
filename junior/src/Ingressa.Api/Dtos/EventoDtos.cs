using System.ComponentModel.DataAnnotations;

namespace Ingressa.Api.Dtos;

public sealed record EventoRequest(
    [Required(ErrorMessage = "Informe o título."), StringLength(150, MinimumLength = 3, ErrorMessage = "O título deve ter entre 3 e 150 caracteres.")] string Titulo,
    [StringLength(4000, ErrorMessage = "A descrição pode ter no máximo 4000 caracteres.")] string? Descricao,
    [Required(ErrorMessage = "Informe o local."), StringLength(150)] string Local,
    [Required(ErrorMessage = "Informe a cidade."), StringLength(100)] string Cidade,
    DateTime DataInicio);

public sealed record SetorRequest(
    [Required(ErrorMessage = "Informe o nome do setor."), StringLength(80, MinimumLength = 2, ErrorMessage = "O nome do setor deve ter entre 2 e 80 caracteres.")] string Nome,
    [Range(typeof(decimal), "0", "100000", ErrorMessage = "O preço deve estar entre 0 e 100.000.")] decimal Preco,
    [Range(1, 100_000, ErrorMessage = "A capacidade deve estar entre 1 e 100.000.")] int Capacidade);

public sealed record SetorResponse(int Id, string Nome, decimal Preco, int Capacidade, int Disponiveis);

public sealed record EventoResumoResponse(
    int Id,
    string Titulo,
    string Local,
    string Cidade,
    DateTime DataInicio,
    decimal? PrecoAPartirDe,
    bool Esgotado);

public sealed record EventoDetalheResponse(
    int Id,
    string Titulo,
    string Descricao,
    string Local,
    string Cidade,
    DateTime DataInicio,
    bool Publicado,
    IReadOnlyList<SetorResponse> Setores);

public enum OrdemEventos
{
    Data,
    Titulo
}

/// <summary>Parâmetros de busca da vitrine, lidos da query string.</summary>
public sealed class EventoFiltro
{
    [StringLength(100)]
    public string? Busca { get; init; }

    [StringLength(100)]
    public string? Cidade { get; init; }

    public OrdemEventos Ordem { get; init; } = OrdemEventos.Data;

    [Range(1, int.MaxValue, ErrorMessage = "A página deve ser 1 ou maior.")]
    public int Pagina { get; init; } = 1;

    [Range(1, 50, ErrorMessage = "O tamanho da página deve estar entre 1 e 50.")]
    public int TamanhoPagina { get; init; } = 10;
}

public sealed record PaginaResponse<T>(
    IReadOnlyList<T> Itens,
    int Pagina,
    int TamanhoPagina,
    int TotalItens,
    int TotalPaginas);
