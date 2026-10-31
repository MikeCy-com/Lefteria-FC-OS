using LefteriaFc.Application.WebAdmin;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LefteriaFc.Infrastructure.WebAdmin;

public sealed class WebAdminConnections(IOptions<WebAdminOptions> options) : IAsyncDisposable
{
    private readonly Lazy<NpgsqlDataSource> source = new(() => NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder
    {
        Host = options.Value.Host, Port = options.Value.Port, Database = options.Value.Database,
        Username = options.Value.Username, Password = options.Value.Password, MaxPoolSize = 10,
        Timeout = 5, CommandTimeout = 10, IncludeErrorDetail = false, ApplicationName = "lefteria-web-admin-auth"
    }.ConnectionString));

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled) throw new WebAdminException(503, "admin_auth_unavailable", "Admin authentication is not configured here.");
        var connection = await source.Value.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT current_user=@username AND current_database()=@database
                  AND NOT (r.rolinherit OR r.rolsuper OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR r.rolbypassrls)
                  AND NOT EXISTS (SELECT 1 FROM pg_auth_members WHERE member=r.oid)
                  AND NOT EXISTS (SELECT 1 FROM pg_database WHERE datdba=r.oid)
                  AND NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspowner=r.oid)
                FROM pg_roles r WHERE rolname=current_user
                """;
            command.Parameters.AddWithValue("username", options.Value.Username);
            command.Parameters.AddWithValue("database", options.Value.Database);
            if (await command.ExecuteScalarAsync(cancellationToken) is not true)
                throw new WebAdminException(503, "admin_auth_unavailable", "Admin authentication is not configured here.");
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        if (source.IsValueCreated) await source.Value.DisposeAsync();
    }
}
