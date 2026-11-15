using System.Text;
using System.Text.RegularExpressions;
using LefteriaFc.Application.WebAdmin;
using Npgsql;
using NpgsqlTypes;
using LefteriaFc.Infrastructure.WebAdmin;

namespace LefteriaFc.Api.WebAdmin;

/// <summary>Super-admin-scoped account directory and audited direct credential provisioning.
/// Password hashes never leave the API; DB writes use a narrow SECURITY DEFINER function.</summary>
public sealed record ManagedAdminCreateCommand(
    string? Username, string? Name, string? Role, string? Password,
    string[]? Organizations)
{
    public override string ToString() => "Managed web administrator creation (credential redacted)";
}

/// <summary>Optimistic, role-locked changes to existing delegated admin status.</summary>
public sealed record ManagedAdminStatusCommand(
    Guid? Id, string? ExpectedRole, bool? ExpectedActive, bool? Active);

public sealed record ManagedAdminRoleCommand(
    Guid? Id, string? ExpectedRole, bool? ExpectedActive, string? NextRole);

public static class AdminUserDirectoryEndpoints
{
    private static readonly SemaphoreSlim PasswordHashQueue = new(2,2);
    public static IEndpointRouteBuilder MapAdminUserDirectory(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/access/users", async (
            int? offset, int? limit, HttpContext context, WebAdminService service,
            WebAdminConnections connections, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.Pragma = "no-cache";

            var session=await service.CurrentAsync(context.Request.Cookies[WebAdminServices.CookieName], token);
            if(session is null)
                return Results.Json(new WebAdminError("Sign in again.", "not_authenticated"),statusCode:401);
            if(session.User.Role!="super_admin")
                return Results.Json(new WebAdminError("Super admin access required.", "not_authorized"),statusCode:403);
            if(context.Request.Query["offset"].Count>1 || context.Request.Query["limit"].Count>1 ||
                offset is <0 or >20000 || limit is <1 or >50)
                return Results.Json(new WebAdminError("Invalid page.", "invalid_filters"),statusCode:400);

            var take=limit??20;
            var skip=offset??0;
            // Never expose an account's identity or existence to an operator
            // who does not hold ALL of that account's organization grants.
            // A dual-organization account is invisible to single-owner admins.
            // Authoritative, freshly verified owners come only from the signed session.
            var owned=session.User.Organizations
                .Select(o=>o.Id)
                .Where(id=>id is "lefteria_fc" or "lefteria_fc_academy")
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            await using var connection=await connections.OpenAsync(token);
            await using var command=connection.CreateCommand();
            command.CommandText="""
                SELECT a.id,a.display_name,a.role,a.is_active,a.invite_pending,
                    COALESCE(array_agg(g.organization_id ORDER BY g.organization_id)
                      FILTER (WHERE g.organization_id IN ('lefteria_fc','lefteria_fc_academy')),ARRAY[]::text[]) AS organizations,
                    (count(*) OVER ())::integer AS total
                FROM identity.admin_accounts a
                LEFT JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id
                WHERE EXISTS (
                  SELECT 1 FROM identity.admin_organization_grants subject_owner
                  WHERE subject_owner.admin_account_id=a.id
                    AND subject_owner.organization_id=ANY(@owners)
                ) AND NOT EXISTS (
                  SELECT 1 FROM identity.admin_organization_grants foreign_owner
                  WHERE foreign_owner.admin_account_id=a.id
                    AND NOT(foreign_owner.organization_id=ANY(@owners))
                )
                GROUP BY a.id,a.display_name,a.role,a.is_active,a.invite_pending
                ORDER BY lower(a.display_name),a.id
                LIMIT @take OFFSET @skip
                """;
            command.Parameters.AddWithValue("take",take);
            command.Parameters.AddWithValue("skip",skip);
            command.Parameters.Add("owners",NpgsqlDbType.Array|NpgsqlDbType.Text).Value=owned;
            var accounts=new List<object>();
            int total=0;
            await using (var reader=await command.ExecuteReaderAsync(token))
            {
                while(await reader.ReadAsync(token))
                {
                    total=reader.GetInt32(6);
                    accounts.Add(new {
                        id=reader.GetGuid(0),
                        name=reader.GetString(1),
                        role=reader.GetString(2),
                        active=reader.GetBoolean(3),
                        invitation_pending=reader.GetBoolean(4),
                        organizations=reader.GetFieldValue<string[]>(5)
                    });
                }
            }
            // A page past the end still reports the authoritative total.
            if(accounts.Count==0 && skip>0)
            {
                await using var count=connection.CreateCommand();
                count.CommandText="""
                    SELECT count(*)::integer FROM identity.admin_accounts a
                    WHERE EXISTS (
                      SELECT 1 FROM identity.admin_organization_grants subject_owner
                      WHERE subject_owner.admin_account_id=a.id
                        AND subject_owner.organization_id=ANY(@owners)
                    ) AND NOT EXISTS (
                      SELECT 1 FROM identity.admin_organization_grants foreign_owner
                      WHERE foreign_owner.admin_account_id=a.id
                        AND NOT(foreign_owner.organization_id=ANY(@owners))
                    )
                    """;
                count.Parameters.Add("owners",NpgsqlDbType.Array|NpgsqlDbType.Text).Value=owned;
                total=Convert.ToInt32(await count.ExecuteScalarAsync(token));
            }
            return Results.Json(new {offset=skip,limit=take,total,accounts});
        })
        .RequireAuthorization(WebAdminServices.Policy)
        .WithName("GetAdminUsersDirectory")
        .WithTags("Web administration")
        // Do not silently change the frozen canonical API contract in this slice.
        // A versioned OpenAPI contract update is a separate integration review.
        .ExcludeFromDescription();
        endpoints.MapPost("/api/admin/access/users", async (
            ManagedAdminCreateCommand request,
            HttpContext context,WebAdminService service,
            WebAdminConnections connections,IWebAdminSecrets secrets,
            CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.Pragma = "no-cache";
            var cookie=context.Request.Cookies[WebAdminServices.CookieName];
            var session=await service.CurrentAsync(cookie,token);
            if(session is null)
                throw new WebAdminException(401,"not_authenticated","Sign in again.");
            if(session.User.Role!="super_admin")
                throw new WebAdminException(403,"not_authorized","Super administrator required.");

            // Do not create additional super administrators, assign arbitrary
            // roles, accept ungranted owners, or activate legacy invite flags.
            var username=request.Username?.Trim().ToLowerInvariant();
            var name=request.Name?.Trim();
            var password=request.Password;
            var owners=request.Organizations;
            var granted=session.User.Organizations.Select(o=>o.Id).ToHashSet(StringComparer.Ordinal);
            var permittedRoles=new HashSet<string>(StringComparer.Ordinal){
                "manager","editor","marketing","coach","matchday_editor","accountant","cashier"
            };
            int bytes=0;
            try {if(password is not null)bytes=new UTF8Encoding(false,true).GetByteCount(password);}
            catch (EncoderFallbackException) { }
            if(username is null ||
                !Regex.IsMatch(username,@"\A[a-z0-9][a-z0-9._@+\-]{2,79}\z",RegexOptions.CultureInvariant) ||
                name is not {Length:>0 and <=200} || name.Any(char.IsControl) ||
                password is not {Length:>=12 and <=72} || password.Contains('\0') ||
                bytes<12 || bytes>72 ||
                request.Role is null || !permittedRoles.Contains(request.Role) ||
                owners is null || owners.Length is <1 or >2 ||
                owners.Distinct(StringComparer.Ordinal).Count()!=owners.Length ||
                owners.Any(owner=>owner is not ("lefteria_fc" or "lefteria_fc_academy") ||
                    !granted.Contains(owner)))
                throw new WebAdminException(422,"managed_admin_invalid",
                    "Check username, display name, role, password and assigned organizations.");

            // Bound synchronous bcrypt work. Do not log sensitive input or
            // return either the password or its hash to the caller.
            if(!await PasswordHashQueue.WaitAsync(0,token))
                throw new WebAdminException(503,"managed_admin_busy",
                    "Credential provisioning is busy.");
            string hash;
            try { hash=await Task.Run(()=>BCrypt.Net.BCrypt.HashPassword(password,workFactor:12),
                CancellationToken.None); }
            finally {PasswordHashQueue.Release();}
            var id=Guid.NewGuid();
            await using var connection=await connections.OpenAsync(token);
            await using var cmd=new NpgsqlCommand(
                "SELECT identity.create_managed_admin(@digest,@id,@username,@name,@role,@hash,@owners)",
                connection);
            cmd.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie!));
            cmd.Parameters.AddWithValue("id",id);
            cmd.Parameters.AddWithValue("username",username);
            cmd.Parameters.AddWithValue("name",name);
            cmd.Parameters.AddWithValue("role",request.Role);
            cmd.Parameters.AddWithValue("hash",hash);
            cmd.Parameters.Add("owners",NpgsqlDbType.Array|NpgsqlDbType.Text).Value=owners;
            try {
                if(await cmd.ExecuteScalarAsync(token) is not Guid created || created!=id)
                    throw new WebAdminException(503,"managed_admin_unconfirmed",
                        "Account creation could not be confirmed.");
            } catch(PostgresException e) when(e.SqlState is "23505") {
                throw new WebAdminException(409,"managed_admin_username_exists",
                    "An account with this username already exists.");
            } catch(PostgresException e) when(e.SqlState is "23503" or "23514" or "22023" or "23502") {
                throw new WebAdminException(422,"managed_admin_invalid",
                    "The account could not be created with these assignments.");
            } catch(PostgresException e) when(e.SqlState is "42501") {
                throw new WebAdminException(403,"managed_admin_denied",
                    "This account is not authorized to create these grants.");
            } catch(PostgresException e) when(e.SqlState is "42883" or "3F000") {
                throw new WebAdminException(503,"managed_admin_unavailable",
                    "Account provisioning is not enabled in this environment.");
            }
            return Results.Json(new {id,username,name,role=request.Role,
                organizations=owners},statusCode:201);
        })
        .RequireAuthorization(WebAdminServices.Policy)
        .WithName("CreateManagedAdminAccount")
        .WithTags("Web administration")
        .Accepts<ManagedAdminCreateCommand>("application/json")
        .ExcludeFromDescription();
        endpoints.MapPost("/api/admin/access/users/status", async (
            ManagedAdminStatusCommand request,HttpContext context,
            WebAdminService service,WebAdminConnections connections,
            IWebAdminSecrets secrets,CancellationToken token) =>
        {
            context.Response.Headers.CacheControl="private, no-store";
            context.Response.Headers.Pragma="no-cache";
            var cookie=context.Request.Cookies[WebAdminServices.CookieName];
            var session=await service.CurrentAsync(cookie,token);
            if(session is null)
                throw new WebAdminException(401,"not_authenticated","Sign in again.");
            if(session.User.Role!="super_admin")
                throw new WebAdminException(403,"not_authorized","Super administrator required.");
            if(request.Id is null || request.Id==Guid.Empty ||
                request.Id==session.User.Id)
                throw new WebAdminException(422,"managed_admin_status_invalid",
                    "Select an eligible account other than your own.");
            if(request.ExpectedRole is not ("manager" or "editor" or "marketing"
                  or "coach" or "matchday_editor" or "accountant"))
                throw new WebAdminException(422,"managed_admin_status_invalid",
                    "The expected role is not an eligible delegated role.");
            if(request.Active is null || request.ExpectedActive is null ||
                request.Active==request.ExpectedActive)
                throw new WebAdminException(422,"managed_admin_status_invalid",
                    "Provide different current and next active states.");

            await using var connection=await connections.OpenAsync(token);
            // Defense in depth for privileged targets. The SECURITY DEFINER
            // routine repeats this under row locks, so a concurrent role edit
            // cannot turn this preflight into authority.
            await using(var targetRoleQuery=new NpgsqlCommand(
                "SELECT role FROM identity.admin_accounts WHERE id=@id",connection))
            {
                targetRoleQuery.Parameters.AddWithValue("id",request.Id.Value);
                if(await targetRoleQuery.ExecuteScalarAsync(token) is string targetRole &&
                    targetRole=="super_admin")
                    throw new WebAdminException(403,"managed_admin_status_denied",
                        "Other super administrator accounts cannot be changed.");
            }
            await using var cmd=new NpgsqlCommand(
                "SELECT identity.set_managed_admin_status(@digest,@subject,@role,@expected,@active)",
                connection);
            cmd.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie!));
            cmd.Parameters.AddWithValue("subject",request.Id.Value);
            cmd.Parameters.AddWithValue("role",request.ExpectedRole);
            cmd.Parameters.AddWithValue("expected",request.ExpectedActive.Value);
            cmd.Parameters.AddWithValue("active",request.Active.Value);
            try
            {
                if(await cmd.ExecuteScalarAsync(token) is not bool persisted ||
                   persisted!=request.Active.Value)
                    throw new WebAdminException(503,"managed_admin_status_unconfirmed",
                        "The account state was not confirmed.");
            }
            catch(PostgresException e) when(e.SqlState=="40001")
            {
                throw new WebAdminException(409,"managed_admin_status_conflict",
                    "The account changed. Refresh before trying again.");
            }
            catch(PostgresException e) when(e.SqlState=="42501")
            {
                throw new WebAdminException(403,"managed_admin_status_denied",
                    "Current session or ownership does not permit this change.");
            }
            catch(PostgresException e) when(e.SqlState is "22023" or "23514")
            {
                throw new WebAdminException(422,"managed_admin_status_invalid",
                    "The requested account status is invalid.");
            }
            catch(PostgresException e) when(e.SqlState is "42883" or "3F000")
            {
                throw new WebAdminException(503,"managed_admin_status_unavailable",
                    "Account status management is not installed in this environment.");
            }
            return Results.Json(new {id=request.Id,role=request.ExpectedRole,active=request.Active});
        })
        .RequireAuthorization(WebAdminServices.Policy)
        .WithName("SetManagedAdminStatus")
        .WithTags("Web administration")
        .Accepts<ManagedAdminStatusCommand>("application/json")
        .ExcludeFromDescription();
        // This endpoint does not delegate scope. The writer refuses to
        // reinterpret any pre-existing city/location/team/staff assignment.
        endpoints.MapPost("/api/admin/access/users/role", async (
            ManagedAdminRoleCommand request,HttpContext context,
            WebAdminService service,WebAdminConnections connections,
            IWebAdminSecrets secrets,CancellationToken token) =>
        {
            context.Response.Headers.CacheControl="private, no-store";
            context.Response.Headers.Pragma="no-cache";
            var cookie=context.Request.Cookies[WebAdminServices.CookieName];
            var session=await service.CurrentAsync(cookie,token);
            if(session is null)
                throw new WebAdminException(401,"not_authenticated","Sign in again.");
            if(session.User.Role!="super_admin")
                throw new WebAdminException(403,"not_authorized","Super administrator required.");

            var allowed=new HashSet<string>(StringComparer.Ordinal){
                "manager","coach","editor","marketing","matchday_editor","accountant"
            };
            if(request.Id is null || request.Id==Guid.Empty ||
                request.Id==session.User.Id ||
                request.ExpectedRole is null || !allowed.Contains(request.ExpectedRole) ||
                request.NextRole is null || !allowed.Contains(request.NextRole) ||
                request.NextRole==request.ExpectedRole || request.ExpectedActive is null)
                throw new WebAdminException(422,"managed_admin_role_invalid",
                    "Choose another delegated account and a different permitted role.");

            await using var connection=await connections.OpenAsync(token);
            await using var cmd=new NpgsqlCommand(
                "SELECT identity.change_managed_admin_role(@digest,@subject,@expected_role,@expected_active,@next_role)",
                connection);
            cmd.Parameters.AddWithValue("digest",secrets.TokenDigest(cookie!));
            cmd.Parameters.AddWithValue("subject",request.Id.Value);
            cmd.Parameters.AddWithValue("expected_role",request.ExpectedRole);
            cmd.Parameters.AddWithValue("expected_active",request.ExpectedActive.Value);
            cmd.Parameters.AddWithValue("next_role",request.NextRole);
            try
            {
                if(await cmd.ExecuteScalarAsync(token) is not string persisted ||
                   persisted!=request.NextRole)
                    throw new WebAdminException(503,"managed_admin_role_unconfirmed",
                        "The account role change was not confirmed.");
            }
            catch(PostgresException e) when(e.SqlState=="40001")
            {
                throw new WebAdminException(409,"managed_admin_role_conflict",
                    "The account changed; refresh before choosing a role.");
            }
            catch(PostgresException e) when(e.SqlState=="23514")
            {
                throw new WebAdminException(409,"managed_admin_role_assignments_exist",
                    "Revoke existing team, city, location and staff assignments first.");
            }
            catch(PostgresException e) when(e.SqlState=="42501")
            {
                throw new WebAdminException(403,"managed_admin_role_denied",
                    "This account or its organizations cannot be managed here.");
            }
            catch(PostgresException e) when(e.SqlState=="22023")
            {
                throw new WebAdminException(422,"managed_admin_role_invalid",
                    "The new role or expected state is invalid.");
            }
            catch(PostgresException e) when(e.SqlState is "42883" or "3F000")
            {
                throw new WebAdminException(503,"managed_admin_role_unavailable",
                    "Safe account role management is not installed here.");
            }
            return Results.Json(new {id=request.Id,role=request.NextRole,
                active=request.ExpectedActive});
        })
        .RequireAuthorization(WebAdminServices.Policy)
        .WithName("ChangeManagedAdminRole")
        .WithTags("Web administration")
        .Accepts<ManagedAdminRoleCommand>("application/json")
        .ExcludeFromDescription();
        return endpoints;
    }
}
