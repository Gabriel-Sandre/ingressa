using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ingressa.Api.Data;

/// <summary>
/// Usada apenas pelo "dotnet ef" (por exemplo, para gerar database/schema.sql),
/// sem precisar iniciar a aplicação inteira.
/// </summary>
internal sealed class FabricaEmTempoDeProjeto : IDesignTimeDbContextFactory<IngressaDbContext>
{
    public IngressaDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<IngressaDbContext>().UseSqlite("Data Source=ingressa.db").Options);
}
