using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Roster-bound reactions for app-only published notices.
/// Down is for isolated disposable database tests; retain production data with a forward migration.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261108280000_AppAnnouncementEmojiReactions")]
public sealed class AppAnnouncementEmojiReactions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE club.app_announcement_reactions (
          announcement_id varchar(128) COLLATE "C" NOT NULL
            REFERENCES club.app_announcements(id) ON DELETE RESTRICT,
          actor_identity_id uuid NOT NULL
            REFERENCES identity.mobile_identities(id) ON DELETE RESTRICT,
          emoji varchar(8) NOT NULL,
          created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(announcement_id,actor_identity_id,emoji),
          CONSTRAINT ck_app_announcement_reaction_emoji CHECK (
            emoji IN ('👍','❤️','😂','⚽','👏','🎉','🙏','🔥'))
        );
        CREATE INDEX ix_app_announcement_reaction_counts
          ON club.app_announcement_reactions(announcement_id,emoji);
        REVOKE ALL ON TABLE club.app_announcement_reactions FROM PUBLIC;

        CREATE OR REPLACE FUNCTION club.read_mobile_app_announcements(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _organization text,_group text,_offset integer)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $mobile_announcements$
        DECLARE v_items jsonb;
        BEGIN
          IF _organization NOT IN ('lefteria_fc','lefteria_fc_academy')
            OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
            OR _offset IS NULL OR _offset NOT BETWEEN 0 AND 100000 THEN
            RAISE EXCEPTION 'app_announcement_invalid' USING ERRCODE='22023';
          END IF;
          IF NOT EXISTS(SELECT 1 FROM jsonb_array_elements(
            club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
          ) AS scoped(value)
            WHERE scoped.value->>'organization_id'=_organization
              AND scoped.value->>'id'=_group) THEN
            RAISE EXCEPTION 'app_announcement_denied' USING ERRCODE='42501';
          END IF;
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',item.id,'organization_id',item.organization_id,
            'group_id',item.group_id,'title',item.title,'body',item.body,
            'published_at',item.published_at,'status','published',
            'reactions',coalesce((SELECT jsonb_agg(jsonb_build_object(
              'emoji',reaction_totals.emoji,'count',reaction_totals.total,
              'mine',reaction_totals.mine
            ) ORDER BY reaction_totals.emoji COLLATE "C")
              FROM (SELECT r.emoji,count(*)::integer AS total,
                bool_or(r.actor_identity_id=_actor) AS mine
                FROM club.app_announcement_reactions r
                WHERE r.announcement_id=item.id
                GROUP BY r.emoji) reaction_totals),'[]'::jsonb))
            ORDER BY item.published_at DESC,item.id DESC),'[]'::jsonb)
          INTO v_items FROM (
            SELECT a.* FROM club.app_announcements a
            WHERE a.organization_id=_organization AND a.group_id=_group
              AND a.removed_at IS NULL
              AND EXISTS(SELECT 1 FROM jsonb_array_elements(
                club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
              ) AS scoped(value)
                WHERE scoped.value->>'organization_id'=_organization
                  AND scoped.value->>'id'=_group)
            ORDER BY a.published_at DESC,a.id DESC
            LIMIT 50 OFFSET _offset
          ) item;
          RETURN jsonb_build_object('announcements',v_items,
            'next_offset',CASE WHEN jsonb_array_length(v_items)=50
              THEN _offset+50 ELSE NULL END);
        END $mobile_announcements$;
        REVOKE ALL ON FUNCTION club.read_mobile_app_announcements(
          uuid,text,text,timestamptz,text,text,integer) FROM PUBLIC;

        CREATE FUNCTION club.set_mobile_app_announcement_reaction(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _organization text,_group text,_announcement text,
          _emoji text,_active boolean)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $announcement_react$
        DECLARE v_announcement text;
          v_changed integer;
          v_reactions jsonb;
        BEGIN
          IF _organization NOT IN ('lefteria_fc','lefteria_fc_academy')
            OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
            OR _announcement IS NULL OR _announcement !~ '^ann_[0-9a-f]{32}$'
            OR _active IS NULL OR _emoji IS NULL
            OR _emoji NOT IN ('👍','❤️','😂','⚽','👏','🎉','🙏','🔥') THEN
            RAISE EXCEPTION 'app_announcement_reaction_invalid' USING ERRCODE='22023';
          END IF;
          -- Serialize every write against an admin removal of this notice.
          -- The server always resolves the current canonical roster, not a
          -- cached subscription or a client supplied role/member flag.
          SELECT a.id INTO v_announcement
          FROM club.app_announcements a
          WHERE a.id=_announcement AND a.organization_id=_organization
            AND a.group_id=_group AND a.removed_at IS NULL
            AND EXISTS(SELECT 1 FROM jsonb_array_elements(
              club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
            ) AS scoped(value) WHERE scoped.value->>'organization_id'=_organization
              AND scoped.value->>'id'=_group)
          FOR UPDATE OF a;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'app_announcement_reaction_denied' USING ERRCODE='42501';
          END IF;
          IF _active THEN
            INSERT INTO club.app_announcement_reactions(
              announcement_id,actor_identity_id,emoji)
            SELECT a.id,_actor,_emoji FROM club.app_announcements a
            WHERE a.id=v_announcement AND a.organization_id=_organization
              AND a.group_id=_group AND a.removed_at IS NULL
              AND EXISTS(SELECT 1 FROM jsonb_array_elements(
                club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
              ) AS scoped(value) WHERE scoped.value->>'organization_id'=_organization
                AND scoped.value->>'id'=_group)
            ON CONFLICT DO NOTHING;
          ELSE
            DELETE FROM club.app_announcement_reactions r
            USING club.app_announcements a
            WHERE r.announcement_id=a.id AND r.actor_identity_id=_actor
              AND r.emoji=_emoji AND a.id=v_announcement
              AND a.organization_id=_organization AND a.group_id=_group
              AND a.removed_at IS NULL
              AND EXISTS(SELECT 1 FROM jsonb_array_elements(
                club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
              ) AS scoped(value) WHERE scoped.value->>'organization_id'=_organization
                AND scoped.value->>'id'=_group);
          END IF;
          GET DIAGNOSTICS v_changed=ROW_COUNT;
          IF v_changed>0 THEN
            INSERT INTO audit.security_events(
              id,actor_realm,occurred_at,actor_identity_id,
              action,outcome,organization_id,reason_code)
            VALUES(gen_random_uuid(),'mobile',clock_timestamp(),_actor,
              CASE WHEN _active THEN 'app.announcement.reaction.add'
                ELSE 'app.announcement.reaction.remove' END,
              'succeeded',_organization,'published_group_notice');
          END IF;
          SELECT coalesce((SELECT jsonb_agg(jsonb_build_object(
              'emoji',totals.emoji,'count',totals.total,'mine',totals.mine
            ) ORDER BY totals.emoji COLLATE "C")
            FROM (SELECT r.emoji,count(*)::integer AS total,
              bool_or(r.actor_identity_id=_actor) AS mine
              FROM club.app_announcement_reactions r
              WHERE r.announcement_id=v_announcement
              GROUP BY r.emoji) totals),'[]'::jsonb) INTO v_reactions;
          RETURN jsonb_build_object(
            'announcement_id',v_announcement,'reactions',v_reactions);
        END $announcement_react$;
        REVOKE ALL ON FUNCTION club.set_mobile_app_announcement_reaction(
          uuid,text,text,timestamptz,text,text,text,text,boolean) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP FUNCTION club.set_mobile_app_announcement_reaction(
          uuid,text,text,timestamptz,text,text,text,text,boolean);
        CREATE OR REPLACE FUNCTION club.read_mobile_app_announcements(
          _actor uuid,_phone text,_role text,_expires timestamptz,
          _organization text,_group text,_offset integer)
        RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER
        SET search_path=pg_catalog,pg_temp AS $mobile_announcements$
        DECLARE v_items jsonb;
        BEGIN
          IF _organization NOT IN ('lefteria_fc','lefteria_fc_academy')
            OR _group IS NULL OR length(_group) NOT BETWEEN 1 AND 128
            OR _offset IS NULL OR _offset NOT BETWEEN 0 AND 100000 THEN
            RAISE EXCEPTION 'app_announcement_invalid' USING ERRCODE='22023';
          END IF;
          IF NOT EXISTS(SELECT 1 FROM jsonb_array_elements(
            club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
          ) AS scoped(value)
            WHERE scoped.value->>'organization_id'=_organization
              AND scoped.value->>'id'=_group) THEN
            RAISE EXCEPTION 'app_announcement_denied' USING ERRCODE='42501';
          END IF;
          SELECT coalesce(jsonb_agg(jsonb_build_object(
            'id',item.id,'organization_id',item.organization_id,
            'group_id',item.group_id,'title',item.title,'body',item.body,
            'published_at',item.published_at,'status','published')
            ORDER BY item.published_at DESC,item.id DESC),'[]'::jsonb)
          INTO v_items FROM (
            SELECT a.* FROM club.app_announcements a
            WHERE a.organization_id=_organization AND a.group_id=_group
              AND a.removed_at IS NULL
              AND EXISTS(SELECT 1 FROM jsonb_array_elements(
                club.read_mobile_chat_groups(_actor,_phone,_role,_expires)->'groups'
              ) AS scoped(value)
                WHERE scoped.value->>'organization_id'=_organization
                  AND scoped.value->>'id'=_group)
            ORDER BY a.published_at DESC,a.id DESC
            LIMIT 50 OFFSET _offset
          ) item;
          RETURN jsonb_build_object('announcements',v_items,
            'next_offset',CASE WHEN jsonb_array_length(v_items)=50
              THEN _offset+50 ELSE NULL END);
        END $mobile_announcements$;
        REVOKE ALL ON FUNCTION club.read_mobile_app_announcements(
          uuid,text,text,timestamptz,text,text,integer) FROM PUBLIC;
        DROP TABLE club.app_announcement_reactions;
        """);
}
