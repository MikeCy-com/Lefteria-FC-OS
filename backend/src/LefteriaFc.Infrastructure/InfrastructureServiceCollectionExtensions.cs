using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LefteriaFc.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddLefteriaInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = new NpgsqlConnectionStringBuilder
            {
                Host = Required("Database:Host"),
                Database = Required("Database:Name"),
                Username = Required("Database:Username"),
                Password = Required("Database:Password")
            }.ConnectionString;
        }

        services.AddDbContext<LefteriaDbContext>(options => options.UseNpgsql(connectionString));
        return services;

        string Required(string key) =>
            configuration[key] is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"{key} is required.");
    }
}
