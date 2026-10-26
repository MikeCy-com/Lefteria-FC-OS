using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Infrastructure.Identity;

namespace LefteriaFc.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddLefteriaInfrastructure(
        this IServiceCollection services)
    {
        services.AddSingleton<IAvatarImageSanitizer, SkiaAvatarImageSanitizer>();
        services.AddDbContext<LefteriaDbContext>((provider, options) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
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

            options.UseNpgsql(connectionString);
            string Required(string key) =>
                configuration[key] is { Length: > 0 } value
                    ? value
                    : throw new InvalidOperationException($"{key} is required.");
        });
        return services;
    }
}
