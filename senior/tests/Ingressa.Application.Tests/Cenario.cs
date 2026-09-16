using Ingressa.Application.Admin;
using Ingressa.Application.Auth;
using Ingressa.Application.Eventos;
using Ingressa.Application.Pedidos;
using Ingressa.Application.Tests.Fakes;
using Ingressa.Domain.Eventos;
using Ingressa.Domain.Usuarios;

namespace Ingressa.Application.Tests;

/// <summary>Monta os serviços reais sobre os dublês, como a injeção de dependência faria.</summary>
public sealed class Cenario
{
    public BancoEmMemoria Banco { get; } = new();
    public RelogioFixo Relogio { get; } = new();
    public GatewayFalso Gateway { get; } = new();
    public EmailFalso Email { get; } = new();
    public GeradorDeCodigosFalso Codigos { get; } = new();

    public AuthService Auth => new(Banco, Banco, Banco, new SenhaHasherFalso(),
        new GeradorDeAccessTokenFalso(Relogio), Codigos, Relogio, Log.Nulo<AuthService>());

    public AdminService Admin => new(Banco, Banco, Log.Nulo<AdminService>());

    public EventoService Eventos => new(Banco, Banco, Banco, Banco, Relogio);

    public PedidoService Pedidos => new(Banco, Banco, Banco, Banco, Banco, Gateway, Relogio, Log.Nulo<PedidoService>());

    public ProcessamentoDePedidosService Processamento => new(
        Banco, Banco, Banco, Banco, Codigos, Gateway, Email, Relogio, Log.Nulo<ProcessamentoDePedidosService>());

    public async Task<Usuario> ClienteAsync(string email = "cliente@teste.com")
    {
        var usuario = Usuario.Registrar("Cliente Teste", email, "hash:Senha@123", PerfilUsuario.Cliente, Relogio.Agora);
        Banco.Adicionar(usuario);
        await Banco.SalvarAsync(default);
        return usuario;
    }

    public async Task<Usuario> OrganizadorAsync(bool aprovado = true, string email = "org@teste.com")
    {
        var usuario = Usuario.Registrar("Organizador", email, "hash:Senha@123", PerfilUsuario.Organizador, Relogio.Agora);
        if (aprovado)
        {
            usuario.AprovarComoOrganizador();
        }

        Banco.Adicionar(usuario);
        await Banco.SalvarAsync(default);
        return usuario;
    }

    /// <summary>Evento publicado daqui a <paramref name="dias"/> dias com Pista (R$ 100) e VIP (R$ 300).</summary>
    public async Task<Evento> EventoAsync(int capacidadePista = 10, int capacidadeVip = 2, int dias = 10)
    {
        var organizador = await OrganizadorAsync(email: $"org{Guid.NewGuid():N}@teste.com");
        var evento = Evento.Criar(organizador.Id,
            new DadosDoEvento("Show", "Descrição", "Arena", "Rio", Relogio.Agora.AddDays(dias)), Relogio.Agora);
        evento.AdicionarSetor("Pista", 100m, capacidadePista);
        evento.AdicionarSetor("VIP", 300m, capacidadeVip);
        evento.Publicar(Relogio.Agora);
        Banco.Adicionar(evento);
        await Banco.SalvarAsync(default);
        return evento;
    }

    public static CriarPedidoRequest Pedido(Evento evento, params (int Indice, int Quantidade)[] itens) =>
        new(evento.Id, itens.Select(i => new ItemPedidoRequest(evento.Setores[i.Indice].Id, i.Quantidade)).ToList());
}
