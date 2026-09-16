using Ingressa.Domain.Comum;
using Ingressa.Domain.Eventos;
using static Ingressa.Domain.Tests.Construtores;

namespace Ingressa.Domain.Tests;

public class EventoTests
{
    [Fact]
    public void Criar_DeveComecarComoRascunhoComTextosLimpos()
    {
        var evento = Evento.Criar(7, new DadosDoEvento("  Show  ", null, " Arena ", " Rio ", Agora.AddDays(1)), Agora);

        Assert.False(evento.Publicado);
        Assert.Equal("Show", evento.Titulo);
        Assert.Equal(string.Empty, evento.Descricao);
        Assert.Equal("Arena", evento.Local);
        Assert.Equal(7, evento.OrganizadorId);
    }

    [Fact]
    public void Criar_DeveTratarDataSemFusoComoUtc()
    {
        var semFuso = DateTime.SpecifyKind(Agora.AddDays(1), DateTimeKind.Unspecified);

        var evento = Evento.Criar(1, Dados() with { DataInicio = semFuso }, Agora);

        Assert.Equal(DateTimeKind.Utc, evento.DataInicio.Kind);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Criar_DeveRecusarDataQueNaoEstejaNoFuturo(int dias)
    {
        Assert.Throws<RegraDeNegocioException>(() => Evento.Criar(1, Dados(dias), Agora));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    public void Criar_DeveRecusarTituloInvalido(string titulo)
    {
        Assert.Throws<RegraDeNegocioException>(() => Evento.Criar(1, Dados() with { Titulo = titulo }, Agora));
    }

    [Fact]
    public void Publicar_DeveExigirSetor()
    {
        var evento = Evento.Criar(1, Dados(), Agora);

        Assert.Throws<RegraDeNegocioException>(() => evento.Publicar(Agora));
    }

    [Fact]
    public void Publicar_DuasVezesDeveGerarConflito()
    {
        var evento = EventoPublicado(("Pista", 50m, 10));

        Assert.Throws<ConflitoException>(() => evento.Publicar(Agora));
    }

    [Fact]
    public void VendasAbertas_SomentePublicadoEAntesDoInicio()
    {
        var evento = EventoPublicado(("Pista", 50m, 10));

        Assert.True(evento.VendasAbertas(Agora));
        Assert.False(evento.VendasAbertas(evento.DataInicio));
    }

    [Fact]
    public void AdicionarSetor_DeveRecusarNomeRepetidoIgnorandoMaiusculas()
    {
        var evento = Evento.Criar(1, Dados(), Agora);
        evento.AdicionarSetor("Pista", 10m, 10);

        Assert.Throws<ConflitoException>(() => evento.AdicionarSetor(" PISTA ", 20m, 5));
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(100_001, 10)]
    [InlineData(10, 0)]
    [InlineData(10, 100_001)]
    public void AdicionarSetor_DeveValidarPrecoECapacidade(int preco, int capacidade)
    {
        var evento = Evento.Criar(1, Dados(), Agora);

        Assert.Throws<RegraDeNegocioException>(() => evento.AdicionarSetor("Pista", preco, capacidade));
    }

    [Fact]
    public void AdicionarSetor_DeveArredondarPrecoParaCentavos()
    {
        var evento = Evento.Criar(1, Dados(), Agora);

        var setor = evento.AdicionarSetor("Pista", 10.005m, 10);

        Assert.Equal(10.00m, setor.Preco);
    }

    [Fact]
    public void AtualizarSetor_NaoPodeReduzirCapacidadeAbaixoDosOcupados()
    {
        var evento = EventoPublicado(("Pista", 50m, 10));
        var setor = evento.Setores[0];
        DefinirOcupados(setor, 6);

        Assert.Throws<RegraDeNegocioException>(() => setor.Atualizar("Pista", 50m, 5));
        setor.Atualizar("Pista", 60m, 6);
        Assert.Equal(0, setor.Disponiveis);
    }

    [Fact]
    public void RemoverSetor_ComOcupacaoDeveGerarConflito()
    {
        var evento = EventoPublicado(("Pista", 50m, 10), ("VIP", 90m, 5));
        DefinirOcupados(evento.Setores[0], 1);

        Assert.Throws<ConflitoException>(() => evento.RemoverSetor(evento.Setores[0].Id));
    }

    [Fact]
    public void RemoverSetor_NaoPodeDeixarEventoPublicadoSemSetor()
    {
        var evento = EventoPublicado(("Pista", 50m, 10));

        Assert.Throws<RegraDeNegocioException>(() => evento.RemoverSetor(evento.Setores[0].Id));
    }

    [Fact]
    public void GarantirQuePodeSerExcluido_DeveConsiderarReservas()
    {
        var evento = EventoPublicado(("Pista", 50m, 10));
        evento.GarantirQuePodeSerExcluido();

        DefinirOcupados(evento.Setores[0], 2);

        Assert.Throws<ConflitoException>(evento.GarantirQuePodeSerExcluido);
    }

    [Fact]
    public void GarantirQuePertenceA_DeveNegarOutroOrganizador()
    {
        var evento = Evento.Criar(1, Dados(), Agora);

        Assert.Throws<AcessoNegadoException>(() => evento.GarantirQuePertenceA(2));
    }
}
