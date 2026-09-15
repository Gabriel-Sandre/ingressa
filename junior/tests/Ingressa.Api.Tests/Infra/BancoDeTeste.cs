using Ingressa.Api.Data;
using Ingressa.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Ingressa.Api.Tests.Infra;

/// <summary>
/// Banco SQLite em memória, real (não um "mock"), criado do zero para cada teste.
/// A conexão precisa ficar aberta: o banco em memória some quando ela fecha.
/// </summary>
public sealed class BancoDeTeste : IDisposable
{
    private readonly SqliteConnection _conexao;
    private readonly DbContextOptions<IngressaDbContext> _opcoes;

    public BancoDeTeste()
    {
        _conexao = new SqliteConnection("DataSource=:memory:");
        _conexao.Open();

        _opcoes = new DbContextOptionsBuilder<IngressaDbContext>()
            .UseSqlite(_conexao)
            .Options;

        using var db = NovoContexto();
        db.Database.EnsureCreated();
    }

    /// <summary>
    /// Cada chamada devolve um contexto novo, como acontece a cada requisição HTTP.
    /// Assim os testes não "enxergam" objetos que só existem em memória.
    /// </summary>
    public IngressaDbContext NovoContexto() => new(_opcoes);

    public async Task<Usuario> CriarUsuarioAsync(PerfilUsuario perfil, string email)
    {
        await using var db = NovoContexto();
        var usuario = new Usuario
        {
            Nome = $"Usuário {email}",
            Email = email,
            SenhaHash = "v1.1.AAAA.AAAA",
            Perfil = perfil,
            CriadoEm = RelogioFixo.Agora
        };
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        return usuario;
    }

    public async Task<Evento> CriarEventoAsync(
        int organizadorId,
        (string Nome, decimal Preco, int Capacidade, int Vendidos)[] setores,
        string titulo = "Evento de Teste",
        bool publicado = true,
        int diasAFrente = 10,
        string cidade = "Rio de Janeiro")
    {
        await using var db = NovoContexto();
        var evento = new Evento
        {
            OrganizadorId = organizadorId,
            Titulo = titulo,
            Descricao = $"Descrição de {titulo}",
            Local = "Local de Teste",
            Cidade = cidade,
            DataInicio = RelogioFixo.Agora.AddDays(diasAFrente),
            Publicado = publicado,
            CriadoEm = RelogioFixo.Agora,
            Setores = setores
                .Select(s => new Setor { Nome = s.Nome, Preco = s.Preco, Capacidade = s.Capacidade, Vendidos = s.Vendidos })
                .ToList()
        };
        db.Eventos.Add(evento);
        await db.SaveChangesAsync();
        return evento;
    }

    public async Task<Setor> ObterSetorAsync(int setorId)
    {
        await using var db = NovoContexto();
        return await db.Setores.AsNoTracking().SingleAsync(s => s.Id == setorId);
    }

    public void Dispose() => _conexao.Dispose();
}
