<script lang="ts">
  import PublicShell from './PublicShell.svelte';
  import type { PublicPageData, PublicOrganization } from './types';

  let { data, eyebrow, title, description, status, primaryHref, primaryLabel,
    secondaryHref = '/contact', secondaryLabel = 'ΕΠΙΚΟΙΝΩΝΙΑ', organization = 'lefteria_fc' }: {
      data: PublicPageData;
      eyebrow: string;
      title: string;
      description: string;
      status: string;
      primaryHref: string;
      primaryLabel: string;
      secondaryHref?: string;
      secondaryLabel?: string;
      organization?: PublicOrganization;
    } = $props();
</script>

<PublicShell site={data.site} branding={data.branding} {organization} navigationGroups={data.navigationGroups}>
  <main class="source-journey" data-testid="public-journey-page">
    <nav class="journey-breadcrumbs" aria-label="Διαδρομή">
      <a href="/">ΑΡΧΙΚΗ</a><span aria-hidden="true">/</span><span aria-current="page">{title}</span>
    </nav>
    <section aria-labelledby="journey-title" class="journey-content">
      <p class="journey-eyebrow">{eyebrow}</p>
      <h1 id="journey-title">{title}</h1>
      <p class="journey-description">{description}</p>
      <div class="journey-notice" role="status" data-testid="journey-capability-status">
        <strong>ΕΝΗΜΕΡΩΣΗ ΥΠΗΡΕΣΙΑΣ</strong>
        <p>{status}</p>
      </div>
      <div class="journey-actions">
        <a class="journey-primary" href={primaryHref} data-testid="journey-primary">{primaryLabel}<span aria-hidden="true">→</span></a>
        <a class="journey-secondary" href={secondaryHref} data-testid="journey-secondary">{secondaryLabel}<span aria-hidden="true">↗</span></a>
      </div>
    </section>
  </main>
</PublicShell>

<style>
  .source-journey { min-height:calc(100svh - 210px);padding:clamp(28px,4.5vw,70px);background:radial-gradient(ellipse at 90% 25%,rgb(245 166 35 / 9%),transparent 38%),#080808;color:white; }
  .journey-breadcrumbs { display:flex;flex-wrap:wrap;align-items:center;gap:12px;color:#b7b7b7;font:600 12px 'Fira Sans',sans-serif; }
  .journey-breadcrumbs a { color:#f5a623;text-decoration:none; }
  .journey-content { margin:clamp(46px,7vh,100px) 0 70px;max-width:900px; }
  .journey-eyebrow { margin-bottom:12px;color:#f5a623;font-size:13px;letter-spacing:.09em;font-weight:700; }
  .journey-content h1 { margin:0 0 24px;font-family:'Fira Sans Extra Condensed',sans-serif;font-size:clamp(46px,5vw,82px);line-height:1.04;overflow-wrap:anywhere; }
  .journey-description { max-width:680px;font-size:clamp(15px,1.15vw,18px);line-height:1.8;color:#dedede; }
  .journey-notice { max-width:700px;padding:24px 26px;margin:32px 0;border-left:3px solid #f5a623;background:linear-gradient(110deg,#1f1a10,#111 75%); }
  .journey-notice strong { display:block;color:#ffcd70;font-size:12px;letter-spacing:.08em; }
  .journey-notice p { margin:9px 0 0;line-height:1.7;font-size:14px;color:#ddd; }
  .journey-actions { display:flex;gap:12px;flex-wrap:wrap; }
  .journey-actions a { min-height:48px;display:inline-flex;align-items:center;justify-content:center;gap:20px;padding:14px 22px;text-decoration:none;font:700 15px 'Fira Sans Extra Condensed',sans-serif; }
  .journey-primary { background:#f5a623;color:#080808; }
  .journey-primary:hover { background:#ffd36d; }
  .journey-secondary { border:1px solid #bbb;color:white; }
  .journey-secondary:hover { background:white;color:#080808; }
  .journey-actions a:focus-visible,.journey-breadcrumbs a:focus-visible { outline:3px solid #ffca46;outline-offset:4px; }
  @media (max-width:600px) {
    .source-journey { padding:24px 20px;min-height:70svh; }
    .journey-content { margin-top:52px; }
    .journey-actions a { width:100%;justify-content:space-between; }
    .journey-notice { padding:20px; }
  }
</style>
