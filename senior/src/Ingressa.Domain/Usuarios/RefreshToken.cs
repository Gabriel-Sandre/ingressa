namespace Ingressa.Domain.Usuarios;

/// <summary>
/// Token de renovação de sessão. Só o hash é guardado. Cada uso gera um novo token
/// (rotação); todos os tokens de um mesmo login compartilham a mesma <see cref="Familia"/>.
/// Se um token já usado aparecer de novo, é sinal de roubo: a família inteira é revogada.
/// </summary>
public class RefreshToken
{
    private RefreshToken() { } // EF Core

    public int Id { get; private set; }
    public int UsuarioId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public Guid Familia { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime ExpiraEm { get; private set; }
    public DateTime? UsadoEm { get; private set; }
    public DateTime? RevogadoEm { get; private set; }

    public static RefreshToken Emitir(int usuarioId, string tokenHash, Guid familia, DateTime agora, TimeSpan validade) => new()
    {
        UsuarioId = usuarioId,
        TokenHash = tokenHash,
        Familia = familia,
        CriadoEm = agora,
        ExpiraEm = agora.Add(validade)
    };

    public bool FoiUsado => UsadoEm is not null;

    public bool EstaAtivo(DateTime agora) => UsadoEm is null && RevogadoEm is null && ExpiraEm > agora;

    public void MarcarComoUsado(DateTime agora) => UsadoEm ??= agora;

    public void Revogar(DateTime agora) => RevogadoEm ??= agora;
}
