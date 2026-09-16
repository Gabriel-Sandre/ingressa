using System.Reflection;
using Ingressa.Domain.Eventos;

namespace Ingressa.Domain.Tests;

internal static class Construtores
{
    public static readonly DateTime Agora = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    public static DadosDoEvento Dados(int dias = 10) =>
        new("Show de Teste", "Descrição", "Arena", "Rio de Janeiro", Agora.AddDays(dias));

    /// <summary>Evento publicado com setores já "gravados" (Ids definidos, como viriam do banco).</summary>
    public static Evento EventoPublicado(params (string Nome, decimal Preco, int Capacidade)[] setores)
    {
        var evento = Evento.Criar(organizadorId: 1, Dados(), Agora);
        DefinirId(evento, 100);
        var id = 1;
        foreach (var (nome, preco, capacidade) in setores)
        {
            DefinirId(evento.AdicionarSetor(nome, preco, capacidade), id++);
        }

        evento.Publicar(Agora);
        return evento;
    }

    /// <summary>Simula o Id atribuído pelo banco (as entidades não expõem setter público).</summary>
    public static void DefinirId(object entidade, int id) =>
        entidade.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)!.SetValue(entidade, id);

    public static void DefinirOcupados(Setor setor, int ocupados) =>
        typeof(Setor).GetProperty(nameof(Setor.Ocupados))!.SetValue(setor, ocupados);
}
