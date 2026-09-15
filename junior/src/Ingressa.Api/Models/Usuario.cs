namespace Ingressa.Api.Models;

public class Usuario
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;

    /// <summary>Sempre armazenado em minúsculas e sem espaços nas pontas.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Hash PBKDF2 no formato gerado por <see cref="Auth.SenhaHasher"/>. Nunca a senha.</summary>
    public string SenhaHash { get; set; } = string.Empty;

    public PerfilUsuario Perfil { get; set; }
    public DateTime CriadoEm { get; set; }
}
