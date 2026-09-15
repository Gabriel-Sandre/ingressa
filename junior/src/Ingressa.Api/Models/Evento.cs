namespace Ingressa.Api.Models;

public class Evento
{
    public int Id { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public string Local { get; set; } = string.Empty;
    public string Cidade { get; set; } = string.Empty;

    /// <summary>Data e hora de início em UTC.</summary>
    public DateTime DataInicio { get; set; }

    /// <summary>Só eventos publicados aparecem na vitrine e podem ser comprados.</summary>
    public bool Publicado { get; set; }

    public int OrganizadorId { get; set; }
    public Usuario? Organizador { get; set; }

    public DateTime CriadoEm { get; set; }

    public List<Setor> Setores { get; set; } = [];
}
