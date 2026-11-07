using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using LefteriaFc.Application.MobileAuth;
using LefteriaFc.Infrastructure.Identity;
using LefteriaFc.Application.Roster;
using LefteriaFc.Infrastructure.Roster;
using LefteriaFc.Application.ClubCalendar;
using LefteriaFc.Infrastructure.ClubCalendar;
using LefteriaFc.Application.Family;
using LefteriaFc.Infrastructure.Family;
using LefteriaFc.Application.Marketing;
using LefteriaFc.Infrastructure.Marketing;

namespace LefteriaFc.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddLefteriaInfrastructure(
        this IServiceCollection services)
    {
        services.AddSingleton<IAvatarImageSanitizer, SkiaAvatarImageSanitizer>();
        services.AddScoped<IRosterQueries, PostgresRosterQueries>();
        services.AddScoped<MobileRosterService>();
        services.AddScoped<IMobileCalendarRepository,PostgresMobileCalendarRepository>();
        services.AddScoped<MobileCalendarService>();
        services.AddScoped<IParentFamilyQueries, PostgresParentFamilyQueries>();
        services.AddScoped<ParentFamilyService>();
        services.AddScoped<IFamilyInvitationRepository, PostgresFamilyInvitationRepository>();
        services.AddScoped<FamilyInvitationService>();
        services.AddScoped<IAcademyLeadWriter, PostgresAcademyLeadWriter>();
        services.AddScoped<AcademyLeadSubmissionService>();
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
