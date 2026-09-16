using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ingressa.Infrastructure.Persistencia;

/// <summary>
/// Usada apenas pelo "dotnet ef migrations add". A conexão não é aberta para gerar migrations.
/// </summary>
internal sealed class FabricaEmTempoDeProjeto : IDesignTimeDbContextFactory<IngressaDbContext>
{
    public IngressaDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<IngressaDbContext>()
            .UseNpgsql("Host=localhost;Database=ingressa;Username=ingressa")
            .Options);
}
