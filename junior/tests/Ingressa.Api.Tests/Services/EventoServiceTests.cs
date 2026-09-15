using Ingressa.Api.Dtos;
using Ingressa.Api.Erros;
using Ingressa.Api.Models;
using Ingressa.Api.Services;
using Ingressa.Api.Tests.Infra;
using Microsoft.EntityFrameworkCore;

namespace Ingressa.Api.Tests.Services;

public sealed class EventoServiceTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly RelogioFixo _relogio = new();

    private EventoService CriarServico(Ingressa.Api.Data.IngressaDbContext db) => new(db, _relogio);

    private static readonly (string, decimal, int, int)[] UmSetor = [("Pista", 100m, 100, 0)];

    private static EventoRequest NovoRequest(DateTime? data = null) =>
        new("Show Novo", "Descrição", "Arena", "Rio de Janeiro", data ?? RelogioFixo.Agora.AddDays(30));

    // ---------- Vitrine ----------

    [Fact]
    public async Task Listar_DeveMostrarSomentePublicadosEFuturos()
    {
        var org = await _banco.CriarUsuarioAsync(PerfilUsuario.Organizador, "org@teste.com");
        await _banco.CriarEventoAsync(org.Id, UmSetor, titulo: "Publicado");
        await _banco.CriarEventoAsync(org.Id, UmSetor, titulo: "Rascunho", publicado: false);
        await _banco.CriarEventoAsync(org.Id, UmSetor, titulo: "Já aconteceu", diasAFrente: -1);

        await using var db = _banco.NovoContexto();
        var resultado = await CriarServico(db).ListarPublicadosAsync(new EventoFiltro());

        var evento = Assert.Single(resultado.Itens);
        Assert.Equal("Publicado", evento.Titulo);
    }

    [Fact]
    public async Task Listar_DeveBuscarSemDiferenciarMaiusculas()
    {
        var org = await _banco.CriarUsuarioAsync(PerfilUsuario.Organizador, "org@teste.com");
        await _banco.CriarEventoAsync(org.Id, UmSetor, titulo: "Festival de ROCK");
        await _banco.CriarEventoAsync(org.Id, UmSetor, titulo: "Noite de Jazz");

        await using var db = _banco.NovoContexto();
        var resultado = await CriarServico(db).ListarPublicadosAsync(new EventoFiltro { Busca = "  rock " });

        Assert.Equal("Festival de ROCK", Assert.Single(resultado.Itens).Titulo);
    }

    [Fact]
    public async Task Listar_DeveFiltrarPorCidade()
    {
        var org = await _banco.CriarUsuarioAsync(PerfilUsuario.Organizador, "org@teste.com");
        await _banco.CriarEventoAsync(org.Id, UmSetor, titulo: "No Rio", cidade: "Rio de Janeiro");
        await _banco.CriarEventoAsync(org.Id, UmSetor, titulo: "Em Belford Roxo", cidade: "Belford Roxo");

        await using var db = _banco.NovoContexto();
        var resultado = await CriarServico(db).ListarPublicadosAsync(new EventoFiltro { Cidade = "belford roxo" });

        Assert.Equal("Em Belford Roxo", Assert.Single(resultado.Itens).Titulo);
    }

    [Fact]
    public async Task Listar_DevePaginarEOrdenarPorData()
    {
        var org = await _banco.CriarUsuarioAsync(PerfilUsuario.Organizador, "org@teste.com");
        for (var dia = 5; dia >= 1; dia--)
        {
            await _banco.CriarEventoAsync(org.Id, UmSetor, titulo: $"Dia {dia}", diasAFrente: dia);
        }

        await using var db = _banco.NovoContexto();
        var pagina2 = await CriarServico(db).ListarPublicadosAsync(
            new EventoFiltro { Pagina = 2, TamanhoPagina = 2 });

        Assert.Equal(5, pagina2.TotalItens);
        Assert.Equal(3, pagina2.TotalPaginas);
        Assert.Equal(new[] { "Dia 3", "Dia 4" }, pagina2.Itens.Select(e => e.Titulo));
    }

    [Fact]
    public async Task Listar_DeveOrdenarPorTitulo()
    {
        var org = await _banco.CriarUsuarioAsync(PerfilUsuario.Organizador, "org@teste.com");
        await _banco.CriarEventoAsync(org.Id, UmSetor, titulo: "Zumbido");
        await _banco.CriarEventoAsync(org.Id, UmSetor, titulo: "Acústico");

        await using var db = _banco.NovoContexto();
        var resultado = await CriarServico(db).ListarPublicadosAsync(new EventoFiltro { Ordem = OrdemEventos.Titulo });

        Assert.Equal(new[] { "Acústico", "Zumbido" }, resultado.Itens.Select(e => e.Titulo));
    }

    [Fact]
    public async Task Listar_DeveCalcularPrecoMinimoEEsgotado()
    {
        var org = await _banco.CriarUsuarioAsync(PerfilUsuario.Organizador, "org@teste.com");
        await _banco.CriarEventoAsync(org.Id, [("Pista", 80m, 10, 3), ("VIP", 200m, 5, 0)], titulo: "Com vagas");
        await _banco.CriarEventoAsync(org.Id, [("Única", 50m, 10, 10)], titulo: "Lotado", diasAFrente: 20);

        await using var db = _banco.NovoContexto();
        var itens = (await CriarServico(db).ListarPublicadosAsync(new EventoFiltro())).Itens;

        Assert.Equal(80m, itens[0].PrecoAPartirDe);
        Assert.False(itens[0].Esgotado);
        Assert.True(itens[1].Esgotado);
    }

    [Fact]
    public async Task Obter_RascunhoDeveSerVisivelApenasParaODono()
    {
        var dono = await _banco.CriarUsuarioAsync(PerfilUsuario.Organizador, "dono@teste.com");
        var outro = await _banco.CriarUsuarioAsync(PerfilUsuario.Organizador, "outro@teste.com");
        var rascunho = await _banco.CriarEventoAsync(dono.Id, UmSetor, publicado: false);

        await using var db = _banco.NovoContexto();
        var servico = CriarServico(db);

        var visto = await servico.ObterAsync(rascunho.Id, dono.Id);
        Assert.False(visto.Publicado);
        await Assert.ThrowsAsync<NaoEncontradoException>(() => servico.ObterAsync(rascunho.Id, outro.Id));
        await Assert.ThrowsAsync<NaoEncontradoException>(() => servico.ObterAsync(rascunho.Id, usuarioId: null));
    }

    // ---------- Organizador ----------

    [Fact]
    public async Task Criar_DeveSalvarComoRascunho()
    {
        var org = await _banco.CriarUsuarioAsync(PerfilUsuario.Organizador, "org@teste.com");

        await using var db = _banco.NovoContexto();
        var criado = await CriarServico(db).CriarAsync(org.Id, NovoRequest());

        Assert.True(criado.Id > 0);
        Assert.False(criado.Publicado);
        Assert.Empty(criado.Setores);
    }

    [Fact]
    public async Task Criar_DeveRecusarDataNoPassado()
    {
        var org = await _banco.CriarUsuarioAsync(PerfilUsuario.Organizador, "org@teste.com");

        await using var db = _banco.NovoContexto();
        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            CriarServico(db).CriarAsync(org.Id, NovoRequest(RelogioFixo.Agora.AddMinutes(-1))));
    }

    [Fact]
    public async Task Atualizar_DeveRecusarEventoDeOutroOrganizador()
    {
        var dono = await _banco.CriarUsuarioAsync(PerfilUsuario.Organizador, "dono@teste.com");
        var intruso = await _banco.CriarUsuarioAsync(PerfilUsuario.Organizador, "intruso@teste.com");
        var evento = await _banco.CriarEventoAsync(dono.Id, UmSetor);

        await using var db = _banco.NovoContexto();
        await Assert.ThrowsAsync<AcessoNegadoException>(() =>
            CriarServico(db).AtualizarAsync(intruso.Id, evento.Id, NovoRequest()));
    }

    [Fact]
    public async Task Publicar_DeveExigirPeloMenosUmSetor()
    {
        var org = await _banco.CriarUsuarioAsync(PerfilUsuario.Organizador, "org@teste.com");
        var evento = await _banco.CriarEventoAsync(org.Id, [], publicado: false);

        await using var db = _banco.NovoContexto();
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => CriarServico(db).PublicarAsync(org.Id, evento.Id));
    }

    [Fact]
    public async Task Publicar_ComSetorDeveTornarOEventoVisivel()
    {
        var org = await _banco.CriarUsuarioAsync(PerfilUsuario.Organizador, "org@teste.com");
        var evento = await _banco.CriarEventoAsync(org.Id, [], publicado: false);

        await using (var db = _banco.NovoContexto())
        {
            var servico = CriarServico(db);
            await servico.AdicionarSetorAsync(org.Id, evento.Id, new SetorRequest("Pista", 50m, 100));
            await servico.PublicarAsync(org.Id, evento.Id);
        }

        await using var leitura = _banco.NovoContexto();
        var vitrine = await CriarServico(leitura).ListarPublicadosAsync(new EventoFiltro());
        Assert.Equal(evento.Id, Assert.Single(vitrine.Itens).Id);
    }

    [Fact]
    public async Task AdicionarSetor_DeveRecusarNomeRepetido()
    {
        var org = await _banco.CriarUsuarioAsync(PerfilUsuario.Organizador, "org@teste.com");
        var evento = await _banco.CriarEventoAsync(org.Id, UmSetor);

        await using var db = _banco.NovoContexto();
        await Assert.ThrowsAsync<ConflitoException>(() =>
            CriarServico(db).AdicionarSetorAsync(org.Id, evento.Id, new SetorRequest(" PISTA ", 10m, 10)));
    }

    [Fact]
    public async Task AtualizarSetor_NaoPodeReduzirCapacidadeAbaixoDosVendidos()
    {
        var org = await _banco.CriarUsuarioAsync(PerfilUsuario.Organizador, "org@teste.com");
        var evento = await _banco.CriarEventoAsync(org.Id, [("Pista", 100m, 100, 40)]);
        var setorId = evento.Setores[0].Id;

        await using var db = _banco.NovoContexto();
        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            CriarServico(db).AtualizarSetorAsync(org.Id, evento.Id, setorId, new SetorRequest("Pista", 100m, 39)));
    }

    [Fact]
    public async Task Excluir_DeveRecusarEventoComVendas()
    {
        var org = await _banco.CriarUsuarioAsync(PerfilUsuario.Organizador, "org@teste.com");
        var evento = await _banco.CriarEventoAsync(org.Id, [("Pista", 100m, 100, 1)]);

        await using var db = _banco.NovoContexto();
        await Assert.ThrowsAsync<ConflitoException>(() => CriarServico(db).ExcluirAsync(org.Id, evento.Id));
    }

    [Fact]
    public async Task Excluir_SemVendasDeveRemoverEventoESetores()
    {
        var org = await _banco.CriarUsuarioAsync(PerfilUsuario.Organizador, "org@teste.com");
        var evento = await _banco.CriarEventoAsync(org.Id, UmSetor);

        await using (var db = _banco.NovoContexto())
        {
            await CriarServico(db).ExcluirAsync(org.Id, evento.Id);
        }

        await using var leitura = _banco.NovoContexto();
        Assert.False(await leitura.Eventos.AnyAsync());
        Assert.False(await leitura.Setores.AnyAsync());
    }

    public void Dispose() => _banco.Dispose();
}
