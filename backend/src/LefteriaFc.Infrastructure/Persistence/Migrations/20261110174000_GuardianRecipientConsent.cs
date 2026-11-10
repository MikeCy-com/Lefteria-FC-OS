using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>
/// OTP-verified recipient consent only. ACCEPTED_PENDING_ACCESS is not
/// an authorization grant and is deliberately not consumed by parent APIs.
/// </summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261110174000_GuardianRecipientConsent")]
public sealed class GuardianRecipientConsent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE FUNCTION identity.read_recipient_guardian_permissions(
        _actor uuid,_phone text,_role text,_expires timestamptz
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $inbox$
      DECLARE v_items jsonb;
      BEGIN
        PERFORM 1 FROM identity.mobile_identities i
        WHERE i.id=_actor AND i.normalized_phone=_phone AND i.is_active
          AND i.role=_role AND _role IN ('fan','parent')
          AND _expires>statement_timestamp();
        IF NOT FOUND THEN RAISE EXCEPTION 'guardian_recipient_denied'
          USING ERRCODE='42501'; END IF;

        SELECT coalesce(jsonb_agg(jsonb_build_object(
          'id',g.id,'organization_id',g.organization_id,
          'status','pending','expires_at',g.expires_at)
          ORDER BY g.created_at DESC,g.id),'[]'::jsonb)
        INTO v_items
        FROM identity.family_guardian_permissions g
        JOIN identity.mobile_identities source ON source.id=g.issuer_identity_id
          AND source.is_active AND source.role='parent'
        JOIN identity.mobile_record_links link
          ON link.identity_id=source.id AND link.normalized_phone=source.normalized_phone
          AND link.role='parent' AND link.kind='guardian'
          AND link.collection='players' AND link.record_id=g.player_id
        JOIN identity.canonical_contact_bindings binding
          ON binding.collection=link.collection AND binding.source_id=link.record_id
          AND binding.kind='guardian' AND binding.normalized_phone=source.normalized_phone
        JOIN club.players player
          ON player.id=g.player_id AND player.organization_id=g.organization_id
        WHERE g.target_phone=_phone AND g.status='pending'
          AND g.expires_at>statement_timestamp()
          AND player.is_active AND NOT player.is_archived AND NOT player.is_transferred
          AND EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial');
        IF jsonb_array_length(v_items)>100
        THEN RAISE EXCEPTION 'guardian_recipient_list_unavailable'
          USING ERRCODE='54000'; END IF;
        -- No child ID/name, guardian identity, target phone or grant role.
        RETURN jsonb_build_object('items',v_items);
      END $inbox$;

      CREATE FUNCTION identity.accept_recipient_guardian_permission(
        _actor uuid,_phone text,_role text,_expires timestamptz,
        _device uuid,_permission uuid
      ) RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $accept$
      DECLARE v_record identity.family_guardian_permissions%ROWTYPE;
        v_now timestamptz:=statement_timestamp();
      BEGIN
        IF _actor IS NULL OR _device IS NULL OR _permission IS NULL
          OR _role NOT IN ('fan','parent') OR _expires IS NULL OR _expires<=v_now
          OR _phone IS NULL OR _phone !~ '^[1-9][0-9]{7,14}$'
        THEN RAISE EXCEPTION 'guardian_recipient_denied' USING ERRCODE='42501'; END IF;

        -- Recipient controls the exact verified phone on their active account.
        -- Source guardian and exact roster association are still current.
        SELECT g.* INTO v_record
        FROM identity.family_guardian_permissions g
        JOIN identity.mobile_identities recipient
          ON recipient.id=_actor AND recipient.normalized_phone=_phone
          AND recipient.role=_role AND recipient.is_active
        JOIN identity.mobile_identities source
          ON source.id=g.issuer_identity_id AND source.is_active
          AND source.role='parent'
        JOIN identity.mobile_record_links link
          ON link.identity_id=source.id AND link.normalized_phone=source.normalized_phone
          AND link.role='parent' AND link.kind='guardian'
          AND link.collection='players' AND link.record_id=g.player_id
        JOIN identity.canonical_contact_bindings binding
          ON binding.collection=link.collection AND binding.source_id=link.record_id
          AND binding.kind='guardian' AND binding.normalized_phone=source.normalized_phone
        JOIN club.players player
          ON player.id=g.player_id AND player.organization_id=g.organization_id
        WHERE g.id=_permission AND g.target_phone=_phone
          AND g.status='pending' AND g.expires_at>v_now
          AND player.is_active AND NOT player.is_archived AND NOT player.is_transferred
          AND EXISTS(SELECT 1 FROM club.roster_import_state WHERE id='initial')
        FOR SHARE OF recipient,source,link,binding,player;
        IF NOT FOUND THEN RAISE EXCEPTION 'guardian_recipient_unavailable'
          USING ERRCODE='42501'; END IF;

        -- Lock after canonical guardian/child rows, matching the revoker.
        PERFORM 1 FROM identity.family_guardian_permissions
        WHERE id=_permission AND status='pending' AND target_phone=_phone
          AND expires_at>v_now AND issuer_identity_id=v_record.issuer_identity_id
          AND organization_id=v_record.organization_id AND player_id=v_record.player_id
        FOR UPDATE;
        IF NOT FOUND THEN RAISE EXCEPTION 'guardian_recipient_unavailable'
          USING ERRCODE='42501'; END IF;
        UPDATE identity.family_guardian_permissions
          SET target_identity_id=_actor,accepted_at=v_now,
              status='accepted_pending_access'
          WHERE id=_permission AND target_phone=_phone
            AND status='pending' AND expires_at>v_now;
        IF NOT FOUND THEN RAISE EXCEPTION 'guardian_recipient_unavailable'
          USING ERRCODE='42501'; END IF;
        INSERT INTO audit.security_events(
          id,occurred_at,actor_identity_id,device_session_id,action,
          outcome,organization_id)
        VALUES(gen_random_uuid(),v_now,_actor,_device,
          'mobile.family.guardian.permission.consent','success',
          v_record.organization_id);

        -- Deliberately not 'active': parent role and child data remain closed.
        RETURN jsonb_build_object('ok',true,'status','accepted_pending_access');
      END $accept$;

      CREATE FUNCTION identity.decline_recipient_guardian_permission(
        _actor uuid,_phone text,_role text,_expires timestamptz,
        _device uuid,_permission uuid
      ) RETURNS boolean LANGUAGE plpgsql VOLATILE SECURITY DEFINER
      SET search_path=pg_catalog,pg_temp AS $decline$
      DECLARE v_owner text;
        v_now timestamptz:=statement_timestamp();
      BEGIN
        IF _actor IS NULL OR _device IS NULL OR _permission IS NULL
          OR _role NOT IN ('fan','parent') OR _expires IS NULL OR _expires<=v_now
          OR _phone IS NULL OR _phone !~ '^[1-9][0-9]{7,14}$'
        THEN RAISE EXCEPTION 'guardian_recipient_denied' USING ERRCODE='42501'; END IF;
        PERFORM 1 FROM identity.mobile_identities i
        WHERE i.id=_actor AND i.normalized_phone=_phone
          AND i.role=_role AND i.is_active;
        IF NOT FOUND THEN RAISE EXCEPTION 'guardian_recipient_denied'
          USING ERRCODE='42501'; END IF;
        SELECT organization_id INTO v_owner
        FROM identity.family_guardian_permissions
        WHERE id=_permission AND target_phone=_phone AND status='pending'
          AND expires_at>v_now FOR UPDATE;
        IF NOT FOUND THEN RAISE EXCEPTION 'guardian_recipient_unavailable'
          USING ERRCODE='42501'; END IF;
        UPDATE identity.family_guardian_permissions SET status='declined'
          WHERE id=_permission AND target_phone=_phone AND status='pending';
        INSERT INTO audit.security_events(
          id,occurred_at,actor_identity_id,device_session_id,action,
          outcome,organization_id)
        VALUES(gen_random_uuid(),v_now,_actor,_device,
          'mobile.family.guardian.permission.decline','success',v_owner);
        RETURN true;
      END $decline$;

      REVOKE ALL ON FUNCTION identity.read_recipient_guardian_permissions(
        uuid,text,text,timestamptz) FROM PUBLIC;
      REVOKE ALL ON FUNCTION identity.accept_recipient_guardian_permission(
        uuid,text,text,timestamptz,uuid,uuid) FROM PUBLIC;
      REVOKE ALL ON FUNCTION identity.decline_recipient_guardian_permission(
        uuid,text,text,timestamptz,uuid,uuid) FROM PUBLIC;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $guard$ BEGIN
        IF EXISTS(SELECT 1 FROM identity.family_guardian_permissions
           WHERE status='accepted_pending_access') THEN
          RAISE EXCEPTION 'guardian_consent_history_requires_forward_recovery'; END IF;
      END $guard$;
      DROP FUNCTION identity.decline_recipient_guardian_permission(
        uuid,text,text,timestamptz,uuid,uuid);
      DROP FUNCTION identity.accept_recipient_guardian_permission(
        uuid,text,text,timestamptz,uuid,uuid);
      DROP FUNCTION identity.read_recipient_guardian_permissions(
        uuid,text,text,timestamptz);
    """);
}
