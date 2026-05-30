using Microsoft.EntityFrameworkCore.Design;

namespace ReflowOven.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations</c> build the context without starting the web host or a live DB.
/// Reads <c>ConnectionStrings__Default</c> from the environment, falling back to the dev connection.
/// </summary>
public sealed class ReflowDbContextFactory : IDesignTimeDbContextFactory<ReflowDbContext>
{
    public ReflowDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                 ?? "Host=localhost;Port=5432;Database=reflowoven;Username=reflow;Password=reflow";
        var options = new DbContextOptionsBuilder<ReflowDbContext>().UseNpgsql(cs).Options;
        return new ReflowDbContext(options);
    }
}
