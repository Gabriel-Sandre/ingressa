using Ingressa.Application.Abstracoes;
using Microsoft.EntityFrameworkCore;

namespace Ingressa.Infrastructure.Persistencia;

/// <summary>
/// Reserva de lugares com um único comando SQL:
/// <code>
/// UPDATE "Setores" SET "Ocupados" = "Ocupados" + @qtd
/// WHERE "Id" = @id AND "Capacidade" - "Ocupados" >= @qtd
/// </code>
/// O PostgreSQL trava a linha durante o UPDATE, então duas compras simultâneas
/// são aplicadas uma depois da outra, e a segunda reavalia a condição com o valor novo.
/// Não há "ler, calcular, gravar" na aplicação — por isso não existe janela para venda duplicada.
/// </summary>
internal sealed class Estoque(IngressaDbContext db) : IEstoque
{
    public async Task<bool> TentarOcuparAsync(int setorId, int quantidade, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quantidade, 1);

        var linhas = await db.Setores
            .Where(s => s.Id == setorId && s.Capacidade - s.Ocupados >= quantidade)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Ocupados, x => x.Ocupados + quantidade), ct);

        return linhas == 1;
    }

    public async Task LiberarAsync(int setorId, int quantidade, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quantidade, 1);

        var linhas = await db.Setores
            .Where(s => s.Id == setorId && s.Ocupados >= quantidade)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Ocupados, x => x.Ocupados - quantidade), ct);

        if (linhas != 1)
        {
            // Nunca deveria acontecer: indica inconsistência. Falhar desfaz a transação inteira.
            throw new InvalidOperationException($"Não foi possível liberar {quantidade} lugar(es) do setor {setorId}.");
        }
    }
}
