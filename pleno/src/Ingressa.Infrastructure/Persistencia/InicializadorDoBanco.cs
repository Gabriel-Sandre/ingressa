using Ingressa.Application.Abstracoes;
using Ingressa.Domain.Eventos;
using Ingressa.Domain.Usuarios;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ingressa.Infrastructure.Persistencia;

public static class InicializadorDoBanco
{
    public const string SenhaDemo = "Senha@123";

    /// <summary>
    /// Aplica as migrations, garante a conta de administrador (se configurada)
    /// e, quando pedido, insere dados de demonstração.
    /// </summary>
    public static async Task InicializarAsync(IServiceProvider services, IConfiguration configuracao, bool dadosDeDemonstracao)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<IngressaDbContext>();
        var hasher = sp.GetRequiredService<ISenhaHasher>();
        var agora = sp.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(InicializadorDoBanco));

        await db.Database.MigrateAsync();

        var emailAdmin = configuracao["Admin:Email"];
        var senhaAdmin = configuracao["Admin:Senha"];
        if (!string.IsNullOrWhiteSpace(emailAdmin) && !string.IsNullOrWhiteSpace(senhaAdmin))
        {
            var email = Usuario.NormalizarEmail(emailAdmin);
            if (!await db.Usuarios.AnyAsync(u => u.Email == email))
            {
                db.Usuarios.Add(Usuario.CriarAdmin("Administrador", email, hasher.GerarHash(senhaAdmin), agora));
                await db.SaveChangesAsync();
                logger.LogInformation("Conta de administrador criada");
            }
        }

        if (!dadosDeDemonstracao || await db.Eventos.AnyAsync())
        {
            return;
        }

        var hash = hasher.GerarHash(SenhaDemo);
        var organizador = Usuario.Registrar("Produtora Baixada Viva", "organizador@ingressa.dev", hash, PerfilUsuario.Organizador, agora);
        organizador.AprovarComoOrganizador();
        var pendente = Usuario.Registrar("Casa de Shows Nova", "pendente@ingressa.dev", hash, PerfilUsuario.Organizador, agora);
        db.Usuarios.AddRange(
            organizador,
            pendente,
            Usuario.Registrar("Cliente de Teste", "cliente@ingressa.dev", hash, PerfilUsuario.Cliente, agora));
        if (!await db.Usuarios.AnyAsync(u => u.Perfil == PerfilUsuario.Admin))
        {
            db.Usuarios.Add(Usuario.CriarAdmin("Administrador", "admin@ingressa.dev", hash, agora));
        }

        await db.SaveChangesAsync();

        var hoje = agora.Date;
        var eventos = new List<(Evento Evento, int[] Ocupar)>
        {
            (NovoEvento(organizador.Id, hoje, 30, "Festival de Rock da Baixada", "Belford Roxo", "Arena Municipal",
                "Três palcos e doze bandas independentes do Rio de Janeiro.",
                ("Pista", 80m, 500), ("Pista Premium", 150m, 200), ("Camarote", 320m, 50)), [120, 190, 50]),
            (NovoEvento(organizador.Id, hoje, 12, "Stand-up: Noite de Comédia", "Rio de Janeiro", "Teatro Central",
                "Quatro comediantes em uma noite de humor sem roteiro.",
                ("Plateia", 60m, 300), ("Mezanino", 40m, 120)), [30, 0]),
            (NovoEvento(organizador.Id, hoje, 45, "Workshop de .NET para Iniciantes", "Nova Iguaçu", "Centro de Tecnologia",
                "Um dia de mão na massa com C#, ASP.NET Core e Entity Framework Core.",
                ("Presencial", 0m, 40)), [38]),
            (NovoEvento(organizador.Id, hoje, 7, "Samba no Quintal", "Duque de Caxias", "Quadra da Vila",
                "Roda de samba com feijoada. Ingressos esgotados!",
                ("Entrada", 35m, 150)), [150])
        };

        foreach (var (evento, _) in eventos)
        {
            evento.Publicar(agora);
            db.Eventos.Add(evento);
        }

        await db.SaveChangesAsync();

        // Simula vendas anteriores usando o mesmo mecanismo atômico da compra real.
        var estoque = sp.GetRequiredService<IEstoque>();
        foreach (var (evento, ocupar) in eventos)
        {
            for (var i = 0; i < ocupar.Length; i++)
            {
                if (ocupar[i] > 0)
                {
                    await estoque.TentarOcuparAsync(evento.Setores[i].Id, ocupar[i], CancellationToken.None);
                }
            }
        }

        logger.LogInformation("Dados de demonstração criados (senha das contas: {Senha})", SenhaDemo);
    }

    private static Evento NovoEvento(
        int organizadorId, DateTime hoje, int dias, string titulo, string cidade, string local, string descricao,
        params (string Nome, decimal Preco, int Capacidade)[] setores)
    {
        // 22h UTC = 19h no horário de Brasília. "agora" é a data de criação; o evento fica no futuro.
        var evento = Evento.Criar(organizadorId, new DadosDoEvento(titulo, descricao, local, cidade, hoje.AddDays(dias).AddHours(22)), hoje);
        foreach (var (nome, preco, capacidade) in setores)
        {
            evento.AdicionarSetor(nome, preco, capacidade);
        }

        return evento;
    }
}
