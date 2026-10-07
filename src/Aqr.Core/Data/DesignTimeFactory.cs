using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Aqr.Core.Data;

/// <summary>Lets `dotnet ef migrations add` build the model for SQL Server without a live database.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AqrDbContext>
{
    public AqrDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AqrDbContext>()
            .UseSqlServer("Server=design-time-only;Database=aqr;Integrated Security=false")
            .Options);
}
