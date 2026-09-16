namespace Ingressa.Domain.Comum;

/// <summary>Algo relevante que aconteceu no domínio e que outras partes do sistema podem querer saber.</summary>
public interface IEventoDeDominio
{
    DateTime OcorridoEm { get; }
}

/// <summary>
/// Base das entidades que publicam eventos de domínio. Os eventos ficam guardados
/// até a gravação; a infraestrutura os transforma em mensagens da outbox na mesma transação.
/// </summary>
public abstract class Entidade
{
    private readonly List<IEventoDeDominio> _eventos = [];

    public int Id { get; protected set; }

    public IReadOnlyList<IEventoDeDominio> Eventos => _eventos;

    protected void Registrar(IEventoDeDominio evento) => _eventos.Add(evento);

    public void LimparEventos() => _eventos.Clear();
}

internal static class Guarda
{
    public static string TextoObrigatorio(string? valor, string campo, int maximo, int minimo = 1)
    {
        var texto = valor?.Trim() ?? string.Empty;
        if (texto.Length < minimo || texto.Length > maximo)
        {
            throw new RegraDeNegocioException($"{campo} deve ter entre {minimo} e {maximo} caracteres.");
        }

        return texto;
    }

    public static DateTime Utc(DateTime data) => data.Kind switch
    {
        DateTimeKind.Utc => data,
        DateTimeKind.Local => data.ToUniversalTime(),
        _ => DateTime.SpecifyKind(data, DateTimeKind.Utc)
    };
}
