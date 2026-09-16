using Ingressa.Domain.Comum;

namespace Ingressa.Domain.Usuarios;

public enum PerfilUsuario
{
    Cliente,
    Organizador,
    Admin
}

public enum StatusConta
{
    Ativa,
    AguardandoAprovacao
}

public class Usuario : Entidade
{
    public const int MaximoTentativasDeLogin = 5;
    public static readonly TimeSpan DuracaoDoBloqueio = TimeSpan.FromMinutes(15);

    private Usuario() { } // EF Core

    public string Nome { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string SenhaHash { get; private set; } = string.Empty;
    public PerfilUsuario Perfil { get; private set; }
    public StatusConta Status { get; private set; }
    public int TentativasFalhas { get; private set; }
    public DateTime? BloqueadoAte { get; private set; }
    public DateTime CriadoEm { get; private set; }

    /// <summary>
    /// Clientes começam ativos. Organizadores aguardam a aprovação de um administrador,
    /// porque podem vender ingressos em nome da plataforma.
    /// </summary>
    public static Usuario Registrar(string nome, string email, string senhaHash, PerfilUsuario perfil, DateTime agora)
    {
        if (perfil == PerfilUsuario.Admin)
        {
            throw new RegraDeNegocioException("Contas de administrador não podem ser criadas pelo cadastro público.");
        }

        return new Usuario
        {
            Nome = Guarda.TextoObrigatorio(nome, "O nome", 100, 3),
            Email = NormalizarEmail(email),
            SenhaHash = senhaHash,
            Perfil = perfil,
            Status = perfil == PerfilUsuario.Organizador ? StatusConta.AguardandoAprovacao : StatusConta.Ativa,
            CriadoEm = agora
        };
    }

    public static Usuario CriarAdmin(string nome, string email, string senhaHash, DateTime agora) => new()
    {
        Nome = Guarda.TextoObrigatorio(nome, "O nome", 100, 3),
        Email = NormalizarEmail(email),
        SenhaHash = senhaHash,
        Perfil = PerfilUsuario.Admin,
        Status = StatusConta.Ativa,
        CriadoEm = agora
    };

    public static string NormalizarEmail(string email) => email.Trim().ToLowerInvariant();

    public bool EstaBloqueado(DateTime agora) => BloqueadoAte is { } ate && ate > agora;

    public bool PodeVender => Perfil == PerfilUsuario.Organizador && Status == StatusConta.Ativa;

    /// <summary>Após várias senhas erradas seguidas, a conta fica bloqueada por um tempo (defesa contra força bruta).</summary>
    public void RegistrarFalhaDeLogin(DateTime agora)
    {
        TentativasFalhas++;
        if (TentativasFalhas >= MaximoTentativasDeLogin)
        {
            BloqueadoAte = agora.Add(DuracaoDoBloqueio);
            TentativasFalhas = 0;
        }
    }

    public void RegistrarLoginComSucesso()
    {
        TentativasFalhas = 0;
        BloqueadoAte = null;
    }

    public void AprovarComoOrganizador()
    {
        if (Perfil != PerfilUsuario.Organizador)
        {
            throw new RegraDeNegocioException("Somente contas de organizador precisam de aprovação.");
        }

        if (Status == StatusConta.Ativa)
        {
            throw new ConflitoException("Este organizador já foi aprovado.");
        }

        Status = StatusConta.Ativa;
    }
}
