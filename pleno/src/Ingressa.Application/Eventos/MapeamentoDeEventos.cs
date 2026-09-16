using Ingressa.Domain.Eventos;

namespace Ingressa.Application.Eventos;

public static class MapeamentoDeEventos
{
    public static EventoDetalheResponse ParaDetalhe(Evento e) => new(
        e.Id, e.OrganizadorId, e.Titulo, e.Descricao, e.Local, e.Cidade, e.DataInicio, e.Publicado,
        e.Setores.OrderBy(s => s.Preco).ThenBy(s => s.Id).Select(ParaResponse).ToList());

    public static SetorResponse ParaResponse(Setor s) => new(s.Id, s.Nome, s.Preco, s.Capacidade, s.Disponiveis);
}
