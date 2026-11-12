using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Verified, assignment-scoped player picker for a coach composing
/// private feedback. No player contacts, birth dates, evaluations or messages.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261112170000_CoachFeedbackTargetDirectory")]
public sealed class CoachFeedbackTargetDirectory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE FUNCTION club.read_mobile_coach_feedback_targets(
              _identity uuid,_phone text,_role text,_expires timestamptz
            ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path=pg_catalog,pg_temp
            AS $feedback_targets$
            DECLARE
              v_staff text;
              v_result jsonb;
              v_count integer;
            BEGIN
              IF _identity IS NULL OR _phone IS NULL OR length(_phone)>32
                OR _role IS DISTINCT FROM 'coach' OR _expires IS NULL
                OR _expires<=clock_timestamp()
              THEN RAISE EXCEPTION 'coach_targets_denied' USING ERRCODE='42501';
              END IF;
              IF NOT EXISTS (SELECT 1 FROM club.roster_import_state WHERE id='initial')
                OR NOT EXISTS (SELECT 1 FROM migration.staff_relationship_import_state WHERE id='initial')
              THEN RAISE EXCEPTION 'coach_targets_roster_unready' USING ERRCODE='55000';
              END IF;

              SELECT s.id INTO STRICT v_staff
                FROM identity.mobile_identities i
                JOIN identity.mobile_record_links l
                  ON l.identity_id=i.id AND l.role='coach'
                  AND l.normalized_phone=i.normalized_phone
                  AND l.collection='staff' AND l.kind='staff_own'
                JOIN identity.canonical_contact_bindings b
                  ON b.collection=l.collection AND b.kind=l.kind
                  AND b.source_id=l.record_id AND b.normalized_phone=i.normalized_phone
                JOIN club.staff s ON s.id=l.record_id
                  AND s.is_active AND NOT s.is_archived
                WHERE i.id=_identity AND i.normalized_phone=_phone
                  AND i.role='coach' AND i.is_active
                FOR SHARE OF i,l,b,s;

              WITH eligible AS (
                SELECT p.id,p.organization_id,p.name
                  FROM club.players p
                  WHERE p.is_active AND NOT p.is_archived AND NOT p.is_transferred
                    AND (
                      (p.organization_id='lefteria_fc'
                        AND p.team_type='First Team'
                        AND EXISTS (
                          SELECT 1 FROM club.staff_teams st
                          WHERE st.staff_id=v_staff
                            AND st.organization_id=p.organization_id
                            AND st.team_id=p.team_id))
                      OR
                      (p.organization_id='lefteria_fc_academy'
                        AND p.team_type='Academy'
                        AND EXISTS (
                          SELECT 1 FROM (
                            SELECT sg.academy_group_id AS group_id
                            FROM club.staff_academy_groups sg
                            WHERE sg.staff_id=v_staff
                              AND sg.organization_id=p.organization_id
                              AND (sg.from_plural OR sg.from_singular OR
                                (sg.from_group_coach AND EXISTS (
                                  SELECT 1 FROM club.academy_group_coaches gc
                                  WHERE gc.staff_id=v_staff
                                    AND gc.academy_group_id=sg.academy_group_id
                                    AND gc.organization_id=p.organization_id)))
                            UNION
                            SELECT gc.academy_group_id
                            FROM club.academy_group_coaches gc
                            WHERE gc.staff_id=v_staff
                              AND gc.organization_id=p.organization_id
                          ) memberships
                          WHERE memberships.group_id=p.primary_academy_group_id
                            OR EXISTS (
                              SELECT 1 FROM club.player_academy_groups pg
                              WHERE pg.organization_id=p.organization_id
                                AND pg.player_id=p.id
                                AND pg.academy_group_id=memberships.group_id)))
                    )
                  ORDER BY p.organization_id,p.name COLLATE "C",p.id COLLATE "C"
                  LIMIT 151
              )
              SELECT count(*)::integer,
                     coalesce(jsonb_agg(jsonb_build_object(
                       'id',id,'organization_id',organization_id,'name',name)
                       ORDER BY organization_id,name COLLATE "C",id COLLATE "C"),
                       '[]'::jsonb)
                INTO v_count,v_result FROM eligible;
              IF v_count>150 THEN
                RAISE EXCEPTION 'coach_targets_limit' USING ERRCODE='54000';
              END IF;
              RETURN jsonb_build_object('items',v_result);
            EXCEPTION WHEN no_data_found OR too_many_rows THEN
              RAISE EXCEPTION 'coach_targets_denied' USING ERRCODE='42501';
            END $feedback_targets$;
            REVOKE ALL ON FUNCTION club.read_mobile_coach_feedback_targets(
              uuid,text,text,timestamptz) FROM PUBLIC;
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP FUNCTION club.read_mobile_coach_feedback_targets(uuid,text,text,timestamptz);
            """);
}
