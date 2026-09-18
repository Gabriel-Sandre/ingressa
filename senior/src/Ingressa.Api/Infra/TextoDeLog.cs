namespace Ingressa.Api.Infra;

/// <summary>
/// Texto vindo do cliente não entra no log como veio: uma quebra de linha no caminho da
/// requisição permitiria forjar linhas inteiras de log (o CodeQL aponta isso como
/// "log entries created from user input"). Também corta o valor, para uma URL gigante
/// não inundar o log.
/// </summary>
internal static class TextoDeLog
{
    private const int Limite = 200;

    public static string Sanitizar(string? valor)
    {
        if (string.IsNullOrEmpty(valor))
        {
            return string.Empty;
        }

        var limpo = new string(valor.Where(caractere => !char.IsControl(caractere)).ToArray());
        return limpo.Length <= Limite ? limpo : limpo[..Limite];
    }
}
