namespace Ingressa.Api.Models;

/// <summary>
/// Área do evento com preço e capacidade próprios (pista, arquibancada, camarote...).
/// </summary>
public class Setor
{
    public int Id { get; set; }

    public int EventoId { get; set; }
    public Evento? Evento { get; set; }

    public string Nome { get; set; } = string.Empty;
    public decimal Preco { get; set; }
    public int Capacidade { get; set; }
    public int Vendidos { get; set; }

    /// <summary>Calculado em memória; não é coluna no banco.</summary>
    public int Disponiveis => Capacidade - Vendidos;
}
