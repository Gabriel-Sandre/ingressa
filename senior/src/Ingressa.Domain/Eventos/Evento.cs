using Ingressa.Domain.Comum;

namespace Ingressa.Domain.Eventos;

public class Evento : Entidade
{
    private readonly List<Setor> _setores = [];

    private Evento() { } // EF Core

    public int OrganizadorId { get; private set; }
    public string Titulo { get; private set; } = string.Empty;
    public string Descricao { get; private set; } = string.Empty;
    public string Local { get; private set; } = string.Empty;
    public string Cidade { get; private set; } = string.Empty;
    public DateTime DataInicio { get; private set; }
    public bool Publicado { get; private set; }

    /// <summary>
    /// Eventos de alta demanda usam a fila virtual: para reservar, o comprador
    /// precisa de um passe liberado pela sala de espera.
    /// </summary>
    public bool FilaVirtual { get; private set; }
    public DateTime CriadoEm { get; private set; }

    public IReadOnlyList<Setor> Setores => _setores;

    public bool TemVendas => _setores.Exists(s => s.Ocupados > 0);

    public static Evento Criar(int organizadorId, DadosDoEvento dados, DateTime agora)
    {
        var evento = new Evento { OrganizadorId = organizadorId, CriadoEm = agora };
        evento.Aplicar(dados, agora);
        return evento;
    }

    public void Atualizar(DadosDoEvento dados, DateTime agora) => Aplicar(dados, agora);

    public void Publicar(DateTime agora)
    {
        if (Publicado)
        {
            throw new ConflitoException("O evento já está publicado.");
        }

        if (_setores.Count == 0)
        {
            throw new RegraDeNegocioException("Cadastre pelo menos um setor antes de publicar o evento.");
        }

        GarantirFuturo(DataInicio, agora);
        Publicado = true;
    }

    public bool VendasAbertas(DateTime agora) => Publicado && DataInicio > agora;

    public void GarantirQuePertenceA(int organizadorId)
    {
        if (OrganizadorId != organizadorId)
        {
            throw new AcessoNegadoException("Você só pode alterar os seus próprios eventos.");
        }
    }

    public void GarantirQuePodeSerExcluido()
    {
        if (TemVendas)
        {
            throw new ConflitoException("O evento já tem ingressos vendidos ou reservados e não pode ser excluído.");
        }
    }

    public Setor AdicionarSetor(string nome, decimal preco, int capacidade)
    {
        var nomeLimpo = Guarda.TextoObrigatorio(nome, "O nome do setor", 80, 2);
        if (_setores.Exists(s => string.Equals(s.Nome, nomeLimpo, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflitoException($"O evento já tem um setor chamado '{nomeLimpo}'.");
        }

        var setor = Setor.Criar(nomeLimpo, preco, capacidade);
        _setores.Add(setor);
        return setor;
    }

    public Setor ObterSetor(int setorId) =>
        _setores.Find(s => s.Id == setorId)
        ?? throw new NaoEncontradoException($"Setor {setorId} não encontrado neste evento.");

    public void RemoverSetor(int setorId)
    {
        var setor = ObterSetor(setorId);
        if (setor.Ocupados > 0)
        {
            throw new ConflitoException("O setor já tem ingressos vendidos ou reservados e não pode ser removido.");
        }

        if (Publicado && _setores.Count == 1)
        {
            throw new RegraDeNegocioException("Um evento publicado precisa ter pelo menos um setor.");
        }

        _setores.Remove(setor);
    }

    private void Aplicar(DadosDoEvento dados, DateTime agora)
    {
        var data = Guarda.Utc(dados.DataInicio);
        GarantirFuturo(data, agora);

        Titulo = Guarda.TextoObrigatorio(dados.Titulo, "O título", 150, 3);
        Descricao = dados.Descricao?.Trim() ?? string.Empty;
        Local = Guarda.TextoObrigatorio(dados.Local, "O local", 150);
        Cidade = Guarda.TextoObrigatorio(dados.Cidade, "A cidade", 100);
        DataInicio = data;
        FilaVirtual = dados.FilaVirtual;
    }

    private static void GarantirFuturo(DateTime data, DateTime agora)
    {
        if (data <= agora)
        {
            throw new RegraDeNegocioException("A data do evento precisa estar no futuro.");
        }
    }
}

public sealed record DadosDoEvento(
    string Titulo, string? Descricao, string Local, string Cidade, DateTime DataInicio, bool FilaVirtual = false);
