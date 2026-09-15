using System.Security.Cryptography;
using Ingressa.Api.Data;
using Ingressa.Api.Dtos;
using Ingressa.Api.Erros;
using Ingressa.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Ingressa.Api.Services;

public sealed class PedidoService(IngressaDbContext db, TimeProvider relogio)
{
    public const int LimiteIngressosPorPedido = 6;
    public static readonly TimeSpan PrazoMinimoParaCancelar = TimeSpan.FromHours(24);

    public async Task<PedidoResponse> CriarAsync(int usuarioId, CriarPedidoRequest request, CancellationToken ct = default)
    {
        // Junta itens repetidos do mesmo setor antes de validar.
        var itens = request.Itens
            .GroupBy(i => i.SetorId)
            .Select(g => (SetorId: g.Key, Quantidade: g.Sum(i => i.Quantidade)))
            .ToList();

        var totalIngressos = itens.Sum(i => i.Quantidade);
        if (totalIngressos is < 1 or > LimiteIngressosPorPedido)
        {
            throw new RegraDeNegocioException(
                $"Cada pedido deve ter entre 1 e {LimiteIngressosPorPedido} ingressos.");
        }

        var evento = await db.Eventos
            .Include(e => e.Setores)
            .FirstOrDefaultAsync(e => e.Id == request.EventoId && e.Publicado, ct)
            ?? throw new NaoEncontradoException($"Evento {request.EventoId} não encontrado.");

        var agora = relogio.GetUtcNow().UtcDateTime;
        if (evento.DataInicio <= agora)
        {
            throw new RegraDeNegocioException("As vendas deste evento já foram encerradas.");
        }

        // 1) Valida tudo antes de alterar qualquer coisa.
        var selecionados = new List<(Setor Setor, int Quantidade)>();
        foreach (var (setorId, quantidade) in itens)
        {
            var setor = evento.Setores.FirstOrDefault(s => s.Id == setorId)
                ?? throw new NaoEncontradoException($"Setor {setorId} não pertence a este evento.");

            if (setor.Disponiveis < quantidade)
            {
                throw new ConflitoException(setor.Disponiveis == 0
                    ? $"O setor '{setor.Nome}' está esgotado."
                    : $"O setor '{setor.Nome}' tem apenas {setor.Disponiveis} ingresso(s) disponível(is).");
            }

            selecionados.Add((setor, quantidade));
        }

        // 2) Reserva o estoque e emite os ingressos.
        //
        // LIMITAÇÃO CONHECIDA (proposital na versão Júnior):
        // este é um padrão "ler → alterar → gravar" sem controle de concorrência.
        // Se duas compras simultâneas lerem Vendidos = 99 de 100, as duas passam
        // na validação acima e o setor termina com 101 vendidos (overselling).
        // A versão Pleno resolve isso com concorrência otimista no PostgreSQL.
        var pedido = new Pedido
        {
            UsuarioId = usuarioId,
            EventoId = evento.Id,
            CriadoEm = agora,
            Status = StatusPedido.Confirmado
        };

        foreach (var (setor, quantidade) in selecionados)
        {
            setor.Vendidos += quantidade;

            for (var i = 0; i < quantidade; i++)
            {
                pedido.Ingressos.Add(new Ingresso
                {
                    Setor = setor,
                    SetorId = setor.Id,
                    Codigo = GerarCodigo(),
                    PrecoPago = setor.Preco
                });
            }
        }

        pedido.Total = pedido.Ingressos.Sum(i => i.PrecoPago);

        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync(ct);

        return ParaResponse(pedido, evento);
    }

    public async Task<IReadOnlyList<PedidoResponse>> ListarDoUsuarioAsync(int usuarioId, CancellationToken ct = default)
    {
        var pedidos = await ConsultaCompleta()
            .Where(p => p.UsuarioId == usuarioId)
            .OrderByDescending(p => p.CriadoEm)
            .ThenByDescending(p => p.Id)
            .ToListAsync(ct);

        return pedidos.Select(p => ParaResponse(p, p.Evento!)).ToList();
    }

    public async Task<PedidoResponse> ObterAsync(int usuarioId, int pedidoId, CancellationToken ct = default)
    {
        var pedido = await ConsultaCompleta().FirstOrDefaultAsync(p => p.Id == pedidoId, ct);

        // Pedido de outra pessoa responde 404: não confirmamos que o id existe.
        if (pedido is null || pedido.UsuarioId != usuarioId)
        {
            throw new NaoEncontradoException($"Pedido {pedidoId} não encontrado.");
        }

        return ParaResponse(pedido, pedido.Evento!);
    }

    public async Task<PedidoResponse> CancelarAsync(int usuarioId, int pedidoId, CancellationToken ct = default)
    {
        var pedido = await db.Pedidos
            .Include(p => p.Evento)
            .Include(p => p.Ingressos)
                .ThenInclude(i => i.Setor)
            .FirstOrDefaultAsync(p => p.Id == pedidoId, ct);

        if (pedido is null || pedido.UsuarioId != usuarioId)
        {
            throw new NaoEncontradoException($"Pedido {pedidoId} não encontrado.");
        }

        if (pedido.Status == StatusPedido.Cancelado)
        {
            throw new ConflitoException("Este pedido já foi cancelado.");
        }

        var agora = relogio.GetUtcNow().UtcDateTime;
        if (pedido.Evento!.DataInicio - agora < PrazoMinimoParaCancelar)
        {
            throw new RegraDeNegocioException(
                $"Cancelamentos só são aceitos até {PrazoMinimoParaCancelar.TotalHours:0} horas antes do evento.");
        }

        // Devolve os ingressos ao estoque de cada setor.
        foreach (var ingresso in pedido.Ingressos)
        {
            ingresso.Setor!.Vendidos -= 1;
        }

        pedido.Status = StatusPedido.Cancelado;
        await db.SaveChangesAsync(ct);

        return ParaResponse(pedido, pedido.Evento);
    }

    private IQueryable<Pedido> ConsultaCompleta() =>
        db.Pedidos.AsNoTracking()
            .Include(p => p.Evento)
            .Include(p => p.Ingressos)
                .ThenInclude(i => i.Setor);

    /// <summary>16 caracteres hexadecimais de um gerador criptográfico (64 bits).</summary>
    internal static string GerarCodigo() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

    private static PedidoResponse ParaResponse(Pedido pedido, Evento evento) => new(
        pedido.Id,
        evento.Id,
        evento.Titulo,
        evento.DataInicio,
        pedido.CriadoEm,
        pedido.Status,
        pedido.Total,
        pedido.Ingressos
            .OrderBy(i => i.Id)
            .Select(i => new IngressoResponse(i.Id, i.Codigo, i.Setor?.Nome ?? string.Empty, i.PrecoPago))
            .ToList());
}
