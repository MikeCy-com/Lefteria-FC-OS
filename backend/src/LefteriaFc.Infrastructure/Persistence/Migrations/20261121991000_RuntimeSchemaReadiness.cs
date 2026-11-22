using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// Only the unprivileged application runtime can ask the migration owner
/// whether the full migration history expected by this binary is installed.
/// No migration rows, ownership details or other private data are returned.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261121991000_RuntimeSchemaReadiness")]
public sealed class RuntimeSchemaReadiness : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION identity.runtime_schema_ready(
        _latest text,_expected_count integer
      ) RETURNS boolean
      LANGUAGE plpgsql STABLE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $runtime_ready$
      DECLARE
        v_before bigint;
        v_present boolean;
      BEGIN
        IF _latest IS NULL OR _latest !~ '^[0-9]{14}_[A-Za-z][A-Za-z0-9_]{1,128}$'
          OR _expected_count IS NULL OR _expected_count<1 OR _expected_count>10000
        THEN RETURN false; END IF;

        -- The runtime cannot SELECT the EF migrations table or infer which
        -- private features are installed. The migration owner releases just
        -- one boolean. Later additive migrations are allowed so compatible
        -- old API images can safely drain during a rolling deploy.
        SELECT count(*) FILTER (WHERE "MigrationId"<=_latest),
               coalesce(bool_or("MigrationId"=_latest),false)
          INTO v_before,v_present
          FROM public."__EFMigrationsHistory";
        RETURN v_present AND v_before=_expected_count;
      END $runtime_ready$;
      REVOKE ALL ON FUNCTION identity.runtime_schema_ready(text,integer)
        FROM PUBLIC;
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION identity.runtime_schema_ready(text,integer);
      """);
}
