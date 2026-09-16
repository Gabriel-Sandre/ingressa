using Ingressa.Domain.Comum;

namespace Ingressa.Domain.Eventos;

/// <summary>
/// Área do evento com preço e capacidade. <see cref="Ocupados"/> soma ingressos pagos
/// e reservados. Ele NÃO é alterado por métodos desta classe: a reserva é feita por
/// um UPDATE condicional e atômico no banco (ver IEstoque), que é o que garante que
/// duas compras simultâneas não vendam o mesmo lugar.
/// </summary>
public class Setor
{
    public const decimal PrecoMaximo = 100_000m;
    public const int CapacidadeMaxima = 100_000;

    private Setor() { } // EF Core

    public int Id { get; private set; }
    public int EventoId { get; private set; }
    public string Nome { get; private set; } = string.Empty;
    public decimal Preco { get; private set; }
    public int Capacidade { get; private set; }
    public int Ocupados { get; private set; }

    public int Disponiveis => Capacidade - Ocupados;

    internal static Setor Criar(string nome, decimal preco, int capacidade)
    {
        var setor = new Setor { Nome = nome };
        setor.DefinirPrecoECapacidade(preco, capacidade);
        return setor;
    }

    public void Atualizar(string nome, decimal preco, int capacidade)
    {
        Nome = Guarda.TextoObrigatorio(nome, "O nome do setor", 80, 2);
        DefinirPrecoECapacidade(preco, capacidade);
    }

    private void DefinirPrecoECapacidade(decimal preco, int capacidade)
    {
        if (preco is < 0 or > PrecoMaximo)
        {
            throw new RegraDeNegocioException($"O preço deve estar entre 0 e {PrecoMaximo:N0}.");
        }

        if (capacidade is < 1 or > CapacidadeMaxima)
        {
            throw new RegraDeNegocioException($"A capacidade deve estar entre 1 e {CapacidadeMaxima:N0}.");
        }

        if (capacidade < Ocupados)
        {
            throw new RegraDeNegocioException(
                $"A capacidade não pode ser menor que os {Ocupados} ingressos já vendidos ou reservados.");
        }

        Preco = decimal.Round(preco, 2);
        Capacidade = capacidade;
    }
}
