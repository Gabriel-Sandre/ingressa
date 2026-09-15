using Ingressa.Api.Auth;
using Ingressa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Ingressa.Api.Data;

/// <summary>
/// Cria o banco na primeira execução e, em desenvolvimento, insere dados de exemplo.
/// A versão Júnior usa EnsureCreated por simplicidade; a Pleno troca por migrations.
/// </summary>
public static class DbInitializer
{
    public const string SenhaDemo = "Senha@123";

    public static async Task InicializarAsync(IServiceProvider services, bool inserirDadosDeExemplo)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IngressaDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<SenhaHasher>();
        var relogio = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        await db.Database.EnsureCreatedAsync();

        if (!inserirDadosDeExemplo || await db.Usuarios.AnyAsync())
        {
            return;
        }

        var agora = relogio.GetUtcNow().UtcDateTime;
        var hashDemo = hasher.GerarHash(SenhaDemo);

        var organizador = new Usuario
        {
            Nome = "Produtora Baixada Viva",
            Email = "organizador@ingressa.dev",
            SenhaHash = hashDemo,
            Perfil = PerfilUsuario.Organizador,
            CriadoEm = agora
        };
        var cliente = new Usuario
        {
            Nome = "Cliente de Teste",
            Email = "cliente@ingressa.dev",
            SenhaHash = hashDemo,
            Perfil = PerfilUsuario.Cliente,
            CriadoEm = agora
        };

        db.Usuarios.AddRange(organizador, cliente);

        db.Eventos.AddRange(
            NovoEvento(organizador, agora, "Festival de Rock da Baixada", "Belford Roxo", "Arena Municipal", 30,
                "Três palcos e doze bandas independentes do Rio de Janeiro.",
                ("Pista", 80m, 500, 120), ("Pista Premium", 150m, 200, 190), ("Camarote", 320m, 50, 50)),
            NovoEvento(organizador, agora, "Stand-up: Noite de Comédia", "Rio de Janeiro", "Teatro Central", 12,
                "Quatro comediantes em uma noite de humor sem roteiro.",
                ("Plateia", 60m, 300, 30), ("Mezanino", 40m, 120, 0)),
            NovoEvento(organizador, agora, "Workshop de .NET para Iniciantes", "Nova Iguaçu", "Centro de Tecnologia", 45,
                "Um dia de mão na massa com C#, ASP.NET Core e Entity Framework Core.",
                ("Presencial", 0m, 40, 38)),
            NovoEvento(organizador, agora, "Samba no Quintal", "Duque de Caxias", "Quadra da Vila", 7,
                "Roda de samba com feijoada. Ingressos esgotados!",
                ("Entrada", 35m, 150, 150)));

        await db.SaveChangesAsync();
    }

    private static Evento NovoEvento(
        Usuario organizador, DateTime agora, string titulo, string cidade, string local, int diasAFrente,
        string descricao, params (string Nome, decimal Preco, int Capacidade, int Vendidos)[] setores) => new()
    {
        Organizador = organizador,
        Titulo = titulo,
        Descricao = descricao,
        Cidade = cidade,
        Local = local,
        DataInicio = agora.Date.AddDays(diasAFrente).AddHours(22), // 19h no horário de Brasília
        Publicado = true,
        CriadoEm = agora,
        Setores = setores
            .Select(s => new Setor { Nome = s.Nome, Preco = s.Preco, Capacidade = s.Capacidade, Vendidos = s.Vendidos })
            .ToList()
    };
}
