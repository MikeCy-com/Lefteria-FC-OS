using LefteriaFc.Application.Marketing;
using LefteriaFc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace LefteriaFc.Infrastructure.Marketing;

// Not a public endpoint. This adapter can only call the separately reviewed
// marketing stored operation; the runtime role has NO EXECUTE privilege yet.
// A denied/missing operation is always unavailable, never a false success.
public sealed class PostgresAcademyLeadWriter(LefteriaDbContext database) : IAcademyLeadWriter
{
    public async Task<AcademyLeadStoreResult> WriteAsync(
        ValidatedAcademyLead lead,string organization,string trustedFingerprint,CancellationToken ct)
    {
        if(organization!="lefteria_fc_academy" || !lead.ConsentGdpr ||
           trustedFingerprint.Length!=64 ||
           trustedFingerprint.Any(c=>c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            return AcademyLeadStoreResult.Unavailable;

        var cs=database.Database.GetConnectionString();
        if(string.IsNullOrWhiteSpace(cs))return AcademyLeadStoreResult.Unavailable;
        try
        {
            await using var conn=new NpgsqlConnection(cs);
            await conn.OpenAsync(ct);
            await using var command=new NpgsqlCommand("""
                SELECT marketing.store_academy_lead(
                  @id,@name,@phone,@email,@age,@interest,@message,
                  @partner,@gdpr,@marketing,@fingerprint)
                """,conn);
            command.CommandTimeout=8;
            command.Parameters.Add(new NpgsqlParameter("id",NpgsqlDbType.Uuid)
                {Value=Guid.NewGuid()});
            command.Parameters.Add(new NpgsqlParameter("name",NpgsqlDbType.Text)
                {Value=lead.Name});
            command.Parameters.Add(new NpgsqlParameter("phone",NpgsqlDbType.Text)
                {Value=(object?)lead.Phone??DBNull.Value});
            command.Parameters.Add(new NpgsqlParameter("email",NpgsqlDbType.Text)
                {Value=(object?)lead.Email??DBNull.Value});
            command.Parameters.Add(new NpgsqlParameter("age",NpgsqlDbType.Integer)
                {Value=(object?)lead.PlayerAge??DBNull.Value});
            command.Parameters.Add(new NpgsqlParameter("interest",NpgsqlDbType.Text)
                {Value=(object?)lead.InterestedIn??DBNull.Value});
            command.Parameters.Add(new NpgsqlParameter("message",NpgsqlDbType.Text)
                {Value=(object?)lead.Message??DBNull.Value});
            command.Parameters.Add(new NpgsqlParameter("partner",NpgsqlDbType.Text)
                {Value=(object?)lead.PartnerSlug??DBNull.Value});
            command.Parameters.Add(new NpgsqlParameter("gdpr",NpgsqlDbType.Boolean)
                {Value=lead.ConsentGdpr});
            command.Parameters.Add(new NpgsqlParameter("marketing",NpgsqlDbType.Boolean)
                {Value=lead.ConsentMarketing});
            command.Parameters.Add(new NpgsqlParameter("fingerprint",NpgsqlDbType.Text)
                {Value=trustedFingerprint});
            var status=await command.ExecuteScalarAsync(ct) as string;
            return status switch
            {
                "stored"=>AcademyLeadStoreResult.Stored,
                "rate_limited"=>AcademyLeadStoreResult.RateLimited,
                _=>AcademyLeadStoreResult.Unavailable
            };
        }
        catch(NpgsqlException){return AcademyLeadStoreResult.Unavailable;}
        catch(TimeoutException){return AcademyLeadStoreResult.Unavailable;}
    }
}
