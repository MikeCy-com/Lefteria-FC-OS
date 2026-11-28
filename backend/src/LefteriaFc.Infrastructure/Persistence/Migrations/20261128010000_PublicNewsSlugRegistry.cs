using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LefteriaFc.Infrastructure.Persistence.Migrations;

/// <summary>Owner-only persistent news alias reservations. No public resolution,
/// editor capability, runtime grant or URL cutover is enabled by this migration.</summary>
[DbContext(typeof(LefteriaDbContext))]
[Migration("20261128010000_PublicNewsSlugRegistry")]
public sealed class PublicNewsSlugRegistry : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      CREATE TABLE public_content.news_slug_aliases (
        slug varchar(80) COLLATE "C" PRIMARY KEY
          CHECK(slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'),
        kind varchar(24) NOT NULL DEFAULT 'news' CHECK(kind='news'),
        organization_id varchar(32) NOT NULL
          CHECK(organization_id IN ('lefteria_fc','lefteria_fc_academy')),
        article_id varchar(128) COLLATE "C" NOT NULL,
        reserved_at timestamptz NOT NULL DEFAULT clock_timestamp(),
        FOREIGN KEY(kind,organization_id,article_id)
          REFERENCES public_content.documents(kind,organization_id,id),
        UNIQUE(slug,kind,organization_id,article_id)
      );
      CREATE TABLE public_content.news_slug_current (
        kind varchar(24) NOT NULL DEFAULT 'news' CHECK(kind='news'),
        organization_id varchar(32) NOT NULL,
        article_id varchar(128) COLLATE "C" NOT NULL,
        slug varchar(80) COLLATE "C" NOT NULL,
        revision bigint NOT NULL CHECK(revision>0),
        PRIMARY KEY(kind,organization_id,article_id),
        FOREIGN KEY(slug,kind,organization_id,article_id)
          REFERENCES public_content.news_slug_aliases(slug,kind,organization_id,article_id)
      );
      ALTER TABLE public_content.news_slug_aliases ENABLE ROW LEVEL SECURITY;
      ALTER TABLE public_content.news_slug_current ENABLE ROW LEVEL SECURITY;
      REVOKE ALL ON public_content.news_slug_aliases,public_content.news_slug_current FROM PUBLIC;

      -- Invoker rights only. An owner may reserve a slug for an already public
      -- news record; no anonymous or application role receives this capability.
      -- Serialize the small administrative claim operation; this is never a
      -- request-time allocator. Preserve old aliases and reject stale edits.
      CREATE FUNCTION public_content.claim_news_slug(_org text,_id text,_slug text,_expected_revision bigint)
      RETURNS text LANGUAGE plpgsql VOLATILE SECURITY INVOKER
      SET search_path=pg_catalog,pg_temp AS $news_slug$
      DECLARE v_current text; v_revision bigint; v_owner text; v_id text;
      BEGIN
        IF _org IS NULL OR _org NOT IN ('lefteria_fc','lefteria_fc_academy')
          OR _id IS NULL OR length(_id) NOT BETWEEN 1 AND 128
          OR _expected_revision IS NULL OR _expected_revision<0
          OR _slug IS NULL OR length(_slug) NOT BETWEEN 1 AND 80
          OR _slug COLLATE "C" !~ '^[a-z0-9]+(-[a-z0-9]+)*$'
          OR _slug IN ('new','edit','search','feed','archive','latest','rss','sitemap')
        THEN RAISE EXCEPTION 'news_slug_invalid' USING ERRCODE='22023'; END IF;
        -- READ COMMITTED is required so a claim sees preceding committed
        -- document writes after acquiring the lock. Never use a stale snapshot.
        IF current_setting('transaction_isolation') <> 'read committed'
        THEN RAISE EXCEPTION 'news_slug_read_committed_required' USING ERRCODE='25001'; END IF;
        LOCK TABLE public_content.documents IN SHARE MODE;
        LOCK TABLE public_content.news_slug_aliases IN SHARE ROW EXCLUSIVE MODE;
        IF NOT EXISTS(SELECT 1 FROM public_content.import_state s
          WHERE s.id='initial' AND s.documents_count=(SELECT count(*) FROM public_content.documents))
        THEN RAISE EXCEPTION 'news_slug_unavailable' USING ERRCODE='55000'; END IF;
        IF NOT EXISTS(SELECT 1 FROM public_content.documents d
          WHERE d.kind='news' AND d.organization_id=_org AND d.id=_id)
        THEN RAISE EXCEPTION 'news_slug_article_not_found' USING ERRCODE='22023'; END IF;
        -- Legacy raw-ID lookup is still supported. Reserve its namespace
        -- globally, even for the same article, rather than shadowing a link.
        IF EXISTS(SELECT 1 FROM public_content.documents d WHERE d.kind='news' AND d.id=_slug)
        THEN RAISE EXCEPTION 'news_slug_legacy_collision' USING ERRCODE='23505'; END IF;
        SELECT c.slug,c.revision INTO v_current,v_revision FROM public_content.news_slug_current c
          WHERE c.kind='news' AND c.organization_id=_org AND c.article_id=_id;
        v_revision:=coalesce(v_revision,0);
        IF v_revision<>_expected_revision
        THEN RAISE EXCEPTION 'news_slug_stale' USING ERRCODE='40001'; END IF;
        IF v_current=_slug THEN RETURN _slug; END IF;
        IF v_revision=9223372036854775807
        THEN RAISE EXCEPTION 'news_slug_revision_exhausted' USING ERRCODE='55000'; END IF;
        SELECT a.organization_id,a.article_id INTO v_owner,v_id
          FROM public_content.news_slug_aliases a WHERE a.slug=_slug;
        IF FOUND AND (v_owner<>_org OR v_id<>_id)
        THEN RAISE EXCEPTION 'news_slug_reserved' USING ERRCODE='23505'; END IF;
        INSERT INTO public_content.news_slug_aliases(slug,organization_id,article_id)
          VALUES(_slug,_org,_id) ON CONFLICT(slug) DO NOTHING;
        INSERT INTO public_content.news_slug_current(organization_id,article_id,slug,revision)
          VALUES(_org,_id,_slug,v_revision+1)
          ON CONFLICT(kind,organization_id,article_id) DO UPDATE SET slug=excluded.slug,revision=excluded.revision;
        RETURN _slug;
      END $news_slug$;
      REVOKE ALL ON FUNCTION public_content.claim_news_slug(text,text,text,bigint) FROM PUBLIC;

      -- A future imported or native published record must not take over a
      -- previously reserved URL, including historical aliases. INSERT/UPDATE
      -- already hold a ROW EXCLUSIVE documents lock, conflicting with the
      -- claim function's SHARE lock. The volatile trigger reads committed
      -- reservations after that lock has been acquired.
      CREATE FUNCTION public_content.guard_news_legacy_id()
      RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY INVOKER
      SET search_path=pg_catalog,pg_temp AS $legacy_guard$
      BEGIN
        IF NEW.kind<>'news' OR length(NEW.id)>80
          OR NEW.id COLLATE "C" !~ '^[a-z0-9]+(-[a-z0-9]+)*$'
        THEN RETURN NEW; END IF;
        IF TG_OP='UPDATE' THEN
          IF NEW.kind=OLD.kind AND NEW.id=OLD.id THEN RETURN NEW; END IF;
        END IF;
        IF current_setting('transaction_isolation') <> 'read committed'
        THEN RAISE EXCEPTION 'news_slug_read_committed_required' USING ERRCODE='25001'; END IF;
        IF EXISTS(SELECT 1 FROM public_content.news_slug_aliases a WHERE a.slug=NEW.id)
        THEN RAISE EXCEPTION 'news_slug_legacy_collision' USING ERRCODE='23505'; END IF;
        RETURN NEW;
      END $legacy_guard$;
      REVOKE ALL ON FUNCTION public_content.guard_news_legacy_id() FROM PUBLIC;
      CREATE TRIGGER guard_news_legacy_id
        BEFORE INSERT OR UPDATE OF kind,id ON public_content.documents
        FOR EACH ROW EXECUTE FUNCTION public_content.guard_news_legacy_id();
      """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
      DO $slug_recovery$
      BEGIN
        IF EXISTS(SELECT 1 FROM public_content.news_slug_aliases)
          OR EXISTS(SELECT 1 FROM public_content.news_slug_current)
        THEN RAISE EXCEPTION 'news_slug_history_requires_forward_recovery'; END IF;
      END $slug_recovery$;
      DROP TRIGGER guard_news_legacy_id ON public_content.documents;
      DROP FUNCTION public_content.guard_news_legacy_id();
      DROP FUNCTION public_content.claim_news_slug(text,text,text,bigint);
      DROP TABLE public_content.news_slug_current,public_content.news_slug_aliases;
      """);
}
