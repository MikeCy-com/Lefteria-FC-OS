using LefteriaFc.Infrastructure.Persistence;
using LefteriaFc.Infrastructure.WebAdmin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace LefteriaFc.Api.Health;

/// <summary>
/// A true health readiness probe must prove the API can use the current
/// database as the actual least-privilege mobile and (when enabled) web-admin
/// roles. This only reads catalog metadata; it never migrates or changes grants.
/// </summary>
internal static class RuntimeReadiness
{
    private const string MobileProof = """
        SELECT current_database() NOT IN ('postgres','template0','template1')
          AND EXISTS (SELECT 1 FROM pg_roles r WHERE r.rolname=current_user
            AND r.rolcanlogin AND NOT (r.rolsuper OR r.rolinherit
              OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR r.rolbypassrls))
          AND NOT EXISTS (SELECT 1 FROM pg_auth_members m
            JOIN pg_roles r ON r.oid=m.member WHERE r.rolname=current_user)
          AND to_regclass('identity.mobile_identities') IS NOT NULL
          AND to_regclass('identity.admin_accounts') IS NOT NULL
          AND has_table_privilege(current_user,'identity.mobile_identities','SELECT')
          AND row_security_active('identity.mobile_identities')
          AND row_security_active('club.calendar_responses')
          AND NOT has_column_privilege(current_user,'identity.admin_accounts','password_hash','SELECT')
          AND NOT has_table_privilege(current_user,'club.staff','SELECT')
          AND has_function_privilege(current_user,
            to_regprocedure('identity.runtime_schema_ready(text,integer)'),'EXECUTE') IS TRUE
          AND identity.runtime_schema_ready(@last,@count)
          AND has_function_privilege(current_user,
            to_regprocedure('public_content.read_published_website_pages(text,text)'),'EXECUTE') IS TRUE
          AND has_function_privilege(current_user,
            to_regprocedure('club.read_mobile_chat_groups(uuid,text,text,timestamptz)'),'EXECUTE') IS TRUE
          AND has_function_privilege(current_user,
            to_regprocedure('hub.read_mobile_player_classrooms(uuid,text,text,timestamptz,text)'),'EXECUTE') IS TRUE
        """;

    private const string AdminProof = """
        SELECT current_database()=@database
          AND to_regclass('identity.admin_sessions') IS NOT NULL
          AND has_table_privilege(current_user,'identity.admin_sessions','SELECT')
          AND NOT has_column_privilege(current_user,'identity.mobile_identities','id','SELECT')
          AND NOT has_table_privilege(current_user,'club.staff','SELECT')
          AND has_function_privilege(current_user,
            to_regprocedure('club.read_admin_coaches(text,text,integer,integer)'),'EXECUTE') IS TRUE
          AND has_function_privilege(current_user,
            to_regprocedure('club.create_admin_native_coach(text,text,uuid,text,text,text,uuid)'),'EXECUTE') IS TRUE
          AND has_function_privilege(current_user,
            to_regprocedure('identity.create_managed_admin(text,uuid,text,text,text,text,text[])'),'EXECUTE') IS TRUE
        """;

    public static async Task<bool> CheckAsync(
        LefteriaDbContext database,
        WebAdminConnections adminConnections,
        IOptions<WebAdminOptions> adminOptions,
        CancellationToken cancellationToken)
    {
        // GetMigrations reads the compiled assembly, not migration history via
        // elevated credentials. A narrowly-granted SECURITY DEFINER routine
        // proves the equivalent baseline exists in the connected database.
        var migrations=database.Database.GetMigrations().ToArray();
        if(migrations.Length==0)return false;
        var opened=database.Database.GetDbConnection().State!=System.Data.ConnectionState.Open;
        if(opened)await database.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            if(database.Database.GetDbConnection() is not NpgsqlConnection mobile)
                return false;
            await using var command=new NpgsqlCommand(MobileProof,mobile) {CommandTimeout=2};
            command.Parameters.AddWithValue("last",migrations[^1]);
            command.Parameters.AddWithValue("count",migrations.Length);
            if(await command.ExecuteScalarAsync(cancellationToken) is not true)
                return false;
        }
        finally
        {
            if(opened)await database.Database.CloseConnectionAsync();
        }

        // A public-only fixture deliberately disables the web admin. When
        // enabled, proving only the mobile connection is *not* operational
        // readiness: the admin may still lack credential/Coach grants.
        if(!adminOptions.Value.Enabled)return true;
        await using var admin=await adminConnections.OpenAsync(cancellationToken);
        await using var adminCommand=new NpgsqlCommand(AdminProof,admin) {CommandTimeout=2};
        adminCommand.Parameters.AddWithValue("database",adminOptions.Value.Database);
        return await adminCommand.ExecuteScalarAsync(cancellationToken) is true;
    }
}
