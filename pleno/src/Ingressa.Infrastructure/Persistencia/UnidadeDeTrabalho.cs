using Ingressa.Application.Abstracoes;
using Ingressa.Domain.Comum;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ingressa.Infrastructure.Persistencia;

internal sealed class UnidadeDeTrabalho(IngressaDbContext db) : IUnidadeDeTrabalho
{
    public async Task SalvarAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Outra operação alterou o mesmo registro depois que ele foi lido (xmin mudou).
            throw new ConflitoException("Este registro foi alterado por outra operação. Recarregue e tente novamente.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflitoException("Já existe um registro com estes dados.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.CheckViolation })
        {
            throw new ConflitoException("A operação violaria um limite de capacidade.");
        }
    }

    public async Task<T> EmTransacaoAsync<T>(Func<CancellationToken, Task<T>> operacao, CancellationToken ct)
    {
        // Transações aninhadas reaproveitam a transação externa.
        if (db.Database.CurrentTransaction is not null)
        {
            return await operacao(ct);
        }

        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var resultado = await operacao(ct);
            await transacao.CommitAsync(ct);
            return resultado;
        }
        catch
        {
            await transacao.RollbackAsync(CancellationToken.None);
            // As entidades rastreadas podem ter ficado com valores que não foram gravados.
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
