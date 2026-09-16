using System.Net;
using System.Net.Mail;
using Ingressa.Application.Abstracoes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ingressa.Infrastructure.Email;

public sealed class OpcoesDeEmail
{
    public const string Secao = "Email";

    public string Host { get; set; } = "localhost";
    public int Porta { get; set; } = 1025;
    public string Remetente { get; set; } = "nao-responda@ingressa.dev";

    /// <summary>STARTTLS. Obrigatório em provedores reais (ex.: Amazon SES na porta 587); o Mailpit local não usa.</summary>
    public bool UsarTls { get; set; }

    /// <summary>Credenciais SMTP. Vazias = servidor sem autenticação (Mailpit). Nunca ficam no appsettings.</summary>
    public string? Usuario { get; set; }
    public string? Senha { get; set; }
}

/// <summary>
/// Envio por SMTP. No ambiente local o servidor é o Mailpit, que guarda os e-mails
/// e os mostra em uma página web (http://localhost:8025) em vez de entregá-los.
/// </summary>
public sealed class EnviadorDeEmailSmtp(IOptions<OpcoesDeEmail> opcoes, ILogger<EnviadorDeEmailSmtp> logger) : IEnviadorDeEmail
{
    public async Task EnviarAsync(string para, string assunto, string corpoTexto, CancellationToken ct)
    {
        var o = opcoes.Value;
        using var mensagem = new MailMessage(o.Remetente, para, assunto, corpoTexto);
        using var cliente = new SmtpClient(o.Host, o.Porta) { EnableSsl = o.UsarTls };
        if (!string.IsNullOrEmpty(o.Usuario))
        {
            cliente.Credentials = new NetworkCredential(o.Usuario, o.Senha);
        }
        await cliente.SendMailAsync(mensagem, ct);
        logger.LogInformation("E-mail '{Assunto}' enviado", assunto);
    }
}
