using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Separate super-admin archive directory. Active roster and mobile
/// eligibility queries remain unchanged.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261128207000_AcademyArchivedPlayerDirectory")]
public sealed class AcademyArchivedPlayerDirectory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION club.search_admin_archived_academy_players(
        _digest text,_search text,_offset integer,_limit integer)
      RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $archive$
      DECLARE v_role text;v_total integer;v_rows jsonb;
        v_owner constant text:='lefteria_fc_academy';
      BEGIN
        IF _digest IS NULL OR _digest !~ '^[0-9a-f]{64}$'
          OR _search IS NULL OR length(_search)>100 OR _search<>btrim(_search)
          OR _search ~ '[[:cntrl:]]' OR _offset IS NULL OR _offset NOT BETWEEN 0 AND 20000
          OR _limit IS NULL OR _limit NOT BETWEEN 1 AND 50
        THEN RAISE EXCEPTION 'archive_search_invalid' USING ERRCODE='22023'; END IF;
        PERFORM identity.lock_admin_club_authority(_digest,v_owner);
        SELECT a.role INTO v_role FROM identity.admin_sessions s
          JOIN identity.admin_accounts a ON a.id=s.admin_account_id
          JOIN identity.admin_organization_grants g ON g.admin_account_id=a.id AND g.organization_id=v_owner
          WHERE s.token_digest=_digest AND s.revoked_at IS NULL AND s.expires_at>clock_timestamp()
            AND a.is_active AND NOT a.invite_pending AND NOT a.otp2fa_enabled AND a.password_hash IS NOT NULL;
        IF NOT FOUND OR v_role<>'super_admin'
        THEN RAISE EXCEPTION 'archive_search_denied' USING ERRCODE='42501'; END IF;
        SELECT count(*) INTO v_total FROM club.players p
          WHERE p.organization_id=v_owner AND p.team_type='Academy'
            AND (NOT p.is_active OR p.is_archived OR p.is_transferred)
            AND (_search='' OR strpos(lower(p.name),lower(_search))>0 OR strpos(lower(p.id),lower(_search))>0);
        IF v_total>20050 THEN RAISE EXCEPTION 'archive_search_too_large' USING ERRCODE='54000'; END IF;
        SELECT coalesce(jsonb_agg(jsonb_build_object('id',p.id,'name',p.name,
          'state',CASE WHEN p.is_transferred THEN 'transferred' WHEN p.is_archived AND NOT p.is_active THEN 'archived' ELSE 'review' END,
          'groups',coalesce((SELECT jsonb_agg(g.name ORDER BY g.name COLLATE "C",g.id COLLATE "C")
            FROM club.academy_groups g WHERE g.organization_id=v_owner
              AND (g.id=p.primary_academy_group_id OR EXISTS(
                SELECT 1 FROM club.player_academy_groups m
                WHERE m.organization_id=v_owner AND m.player_id=p.id AND m.academy_group_id=g.id))),'[]'::jsonb))
          ORDER BY p.name COLLATE "C",p.id COLLATE "C"),'[]'::jsonb)
          INTO v_rows FROM (
            SELECT id,name,is_active,is_archived,is_transferred,primary_academy_group_id
            FROM club.players p WHERE p.organization_id=v_owner AND p.team_type='Academy'
              AND (NOT p.is_active OR p.is_archived OR p.is_transferred)
              AND (_search='' OR strpos(lower(p.name),lower(_search))>0 OR strpos(lower(p.id),lower(_search))>0)
            ORDER BY name COLLATE "C",id COLLATE "C" LIMIT _limit OFFSET _offset
          ) p;
        RETURN jsonb_build_object('organization_id',v_owner,'offset',_offset,'limit',_limit,'total',v_total,'players',v_rows);
      END $archive$;
      REVOKE ALL ON FUNCTION club.search_admin_archived_academy_players(text,text,integer,integer) FROM PUBLIC;
      """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DROP FUNCTION club.search_admin_archived_academy_players(text,text,integer,integer);
      """);
}
