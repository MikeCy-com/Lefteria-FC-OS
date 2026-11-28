import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';

const methods = new Set(['get', 'post', 'put', 'patch', 'delete', 'head', 'options', 'trace']);
const supportedHost = 'srv1649602.hstgr.cloud';
const publicApiPaths = ['/api/public/site', '/api/public/news', '/api/public/news/{id}', '/api/public/sections', '/api/public/website/pages'];
const publicMediaPath = '/api/uploads/{folder}/{fileName}';
const publicMediaFolders = ['players', 'staff', 'news', 'partners', 'sponsors', 'gallery', 'opponent',
  'groups', 'academy', 'products', 'venues', 'club', 'teams', 'events', 'reels', 'standings'];
export const publicMediaExpression = '^/api/uploads/(?:' + publicMediaFolders.join('|') + ')/[^/]+$';
export function reviewedPublicMedia(document) {
  const operations = document?.paths?.[publicMediaPath];
  for (const method of ['get', 'head']) {
    const operation = operations?.[method];
    if (!operation || (operation.security !== undefined &&
      (!Array.isArray(operation.security) || operation.security.length !== 0)))
      throw new Error('Reviewed public media must remain anonymous.');
    for (const key of ['folder', 'fileName']) {
      if (![...(operations.parameters ?? []), ...(operation.parameters ?? [])]
        .some(value => value?.in === 'path' && value.name === key && value.required === true))
        throw new Error('Reviewed public media requires path parameters.');
    }
  }
  return publicMediaExpression;
}

const entryOperations = [
  ['post', '/api/admin/auth/login', []], ['post', '/api/admin/auth/logout', []],
  ['get', '/api/admin/auth/setup', []], ['post', '/api/admin/auth/setup', []],
  ['get', '/api/admin/academy/office/{collection}', [{ WebAdmin: [] }]],
  ['post', '/api/admin/academy/office/registrations', [{ WebAdmin: [] }]],
  ['post', '/api/admin/academy/office/registrations/review', [{ WebAdmin: [] }]],
  // Exact reviewed enrollment POST; the API and SQL independently check authorization.
  ['post', '/api/admin/academy/office/registrations/enroll', [{ WebAdmin: [] }]],
  ['post', '/api/admin/academy/office/orders', [{ WebAdmin: [] }]],
  ['post', '/api/admin/academy/office/orders/status', [{ WebAdmin: [] }]],
  ['get', '/api/admin/auth/me', [{ WebAdmin: [] }]],
  ['get', '/api/admin/club', [{ WebAdmin: [] }]], ['put', '/api/admin/club', [{ WebAdmin: [] }]],
  // Reviewed admin announcement operations only. Caddy still defers every
  // session, permission and organization check to the secured ASP.NET API.
  ['get','/api/admin/communications/announcement-drafts',[{ WebAdmin: [] }]],
  ['post','/api/admin/communications/announcement-drafts',[{ WebAdmin: [] }]],
  ['post','/api/admin/communications/announcement-drafts/{id}/publish',[{ WebAdmin: [] }]],
  ['post','/api/admin/communications/announcement-drafts/{id}/media',[{ WebAdmin: [] }]],
  ['delete','/api/admin/communications/announcement-drafts/{id}/media/{mediaId}',[{ WebAdmin: [] }]],
  ['get','/api/admin/communications/app-announcements',[{ WebAdmin: [] }]],
  ['get','/api/admin/communications/app-announcements/{id}/readers',[{ WebAdmin: [] }]],
  ['delete','/api/admin/communications/app-announcements/{id}',[{ WebAdmin: [] }]],
  ['post', '/api/mobile/auth/request-otp', []], ['post', '/api/mobile/auth/verify-otp', []],
  ['post', '/api/mobile/auth/register-fan', []],
  ['post', '/api/mobile/auth/exchange-legacy', [{ LegacyMobile: [] }]],
  ['get', '/api/uploads/avatars/{fileName}', []], ['head', '/api/uploads/avatars/{fileName}', []]
];

// A fixed reviewed snapshot, not automatic publication of all future APIs.
// This explicit, reviewed WebAdmin-only inventory includes the new sponsorship APIs;
// edge list, so even healthy APIs failed with an edge 404. Every entry's exact
// method, path, mandatory parameters and WebAdmin security is rechecked
// against the immutable contract; new routes must be explicitly reviewed.
export const reviewedAdminEntries = [
  ['get','/api/admin/academy/drills'],
  ['post','/api/admin/academy/drills'],
  ['get','/api/admin/academy/drills/proposals'],
  ['post','/api/admin/academy/drills/proposals'],
  ['post','/api/admin/academy/drills/proposals/linked'],
  ['post','/api/admin/academy/drills/proposals/{drillId}/apply-revision'],
  ['put','/api/admin/academy/drills/{drillId}'],
  ['get','/api/admin/academy/training'],
  ['get','/api/admin/academy/training/plans'],
  ['post','/api/admin/academy/training/plans'],
  ['get','/api/admin/academy/training/plans/approvals'],
  ['get','/api/admin/academy/training/plans/assigned-groups'],
  ['post','/api/admin/academy/training/plans/{planId}/submit'],
  ['get','/api/admin/academy/training/plans/publications'],
  ['put','/api/admin/academy/training/plans/{planId}'],
  ['post','/api/admin/academy/training/plans/{planId}/approve'],
  ['post','/api/admin/academy/training/plans/{planId}/cancel'],
  ['get','/api/admin/academy/training/plans/{planId}/publication-preview'],
  ['get','/api/admin/academy/training/plans/{planId}/drills'],
  ['put','/api/admin/academy/training/plans/{planId}/drills'],
  ['get','/api/admin/academy/training/plans/{planId}/delivery'],
  ['put','/api/admin/academy/training/plans/{planId}/delivery'],
  ['post','/api/admin/academy/training/plans/{planId}/drills/approve'],
  ['post','/api/admin/academy/training/plans/{planId}/publish'],
  ['get','/api/admin/academy/training/{eventId}/attendance'],
  ['post','/api/admin/academy/training/{eventId}/attendance'],
  ['get','/api/admin/club/players/{playerId}/documents'],
  ['post','/api/admin/club/players/{playerId}/documents'],
  ['get','/api/admin/club/players/{playerId}/evaluations'],
  ['post','/api/admin/club/players/{playerId}/evaluations'],
  ['get','/api/admin/crm/academy/leads'],
  ['post','/api/admin/crm/academy/leads/{leadId}/status'],
  ['get','/api/admin/finance/payabl/invoices/multi.pdf'],
  ['get','/api/admin/finance/payabl/invoices/{invoiceId}.pdf'],
  ['get','/api/admin/finance/payabl/payment-requests'],
  ['post','/api/admin/finance/payabl/payment-requests'],
  ['get','/api/admin/finance/payabl/status'],
  ['get','/api/admin/finance/payabl/training-fees'],
  ['post','/api/admin/finance/payabl/training-fees'],
  ['post','/api/admin/finance/payabl/training-fees/{feeId}/cash'],
  ['post','/api/admin/finance/payabl/training-fees/{feeId}/sms'],
  ['get','/api/admin/football/academy-group-profiles/{groupId}'],
  ['put','/api/admin/football/academy-group-profiles/{groupId}'],
  ['put','/api/admin/football/academy-group-profiles/{groupId}/visibility'],
  ['put','/api/admin/football/assignments'],
  ['get','/api/admin/football/audiences'],
  ['put','/api/admin/football/coach-assignments'],
  ['get','/api/admin/football/coaches'],
  ['get','/api/admin/football/first-team-profiles/{teamId}'],
  ['put','/api/admin/football/first-team-profiles/{teamId}'],
  ['post','/api/admin/football/groups'],
  ['post','/api/admin/football/groups/with-profile'],
  ['get','/api/admin/football/players'],
  ['get','/api/admin/football/players/{playerId}/birthday-preferences'],
  ['put','/api/admin/football/players/{playerId}/birthday-preferences'],
  ['post','/api/admin/hub/ai/education-briefs'],
  ['get','/api/admin/hub/editorial'],
  ['post','/api/admin/hub/editorial'],
  ['get','/api/admin/hub/editorial/{id}'],
  ['post','/api/admin/hub/editorial/{id}/final-approve'],
  ['post','/api/admin/hub/editorial/{id}/images/quarantine'],
  ['get','/api/admin/hub/editorial/{id}/images/{mediaId}/preview'],
  ['post','/api/admin/hub/editorial/{id}/images/{mediaId}/reject'],
  ['post','/api/admin/hub/editorial/{id}/publish'],
  ['post','/api/admin/hub/editorial/{id}/return-for-revision'],
  ['post','/api/admin/hub/editorial/{id}/revise'],
  ['post','/api/admin/hub/editorial/{id}/submit-review'],
  ['post','/api/admin/hub/editorial/{id}/text-review'],
  ['post','/api/admin/hub/editorial/{id}/visual-approve'],
  ['get','/api/admin/hub/player-quizzes'],
  ['post','/api/admin/hub/player-quizzes'],
  ['get','/api/admin/hub/player-quizzes/{id}'],
  ['post','/api/admin/hub/player-quizzes/{id}/approve'],
  ['post','/api/admin/hub/player-quizzes/{id}/publish'],
  ['post','/api/admin/hub/player-quizzes/{id}/withdraw'],
  ['get','/api/admin/pos/cashier/finance-handoff'],
  ['get','/api/admin/pos/cashier/receipts/{receiptId}'],
  ['get','/api/admin/pos/cashier/registers'],
  // Read-only shift-scoped stock: API + private SQL reauthorize the named cashier.
  // NO grants to tender, receipt writer, card capture or arbitrary stock updates.
  ['get','/api/admin/pos/cashier/registers/{registerId}/sellable-stock'],
  ['post','/api/admin/pos/cashier/registers/{registerId}/sales/prepare'],
  ['post','/api/admin/pos/cashier/registers/{registerId}/shifts'],
  ['post','/api/admin/pos/cashier/returns/{returnId}/refund-requests'],
  ['post','/api/admin/pos/cashier/returns/{returnId}/review'],
  ['post','/api/admin/pos/cashier/sales/{saleId}/cancel'],
  ['post','/api/admin/pos/cashier/sales/{saleId}/returns'],
  ['post','/api/admin/pos/cashier/sales/{saleId}/tender-cash'],
  ['post','/api/admin/pos/cashier/shifts/{shiftId}/close'],
  ['get','/api/admin/pos/cashier/shifts/{shiftId}/reconciliation'],
  ['get','/api/admin/pos/catalog'],
  ['post','/api/admin/pos/categories'],
  ['put','/api/admin/pos/categories/{id}'],
  ['post','/api/admin/pos/cities'],
  ['get','/api/admin/pos/locations'],
  ['post','/api/admin/pos/locations'],
  ['put','/api/admin/pos/locations/{locationId}/products/{productId}'],
  ['post','/api/admin/pos/locations/{locationId}/stock'],
  ['post','/api/admin/pos/products'],
  ['put','/api/admin/pos/products/{id}'],
  ['get','/api/admin/trials/settings'],
  // Native CMS: only these exact per-organization draft and approval operations.
  ['get','/api/admin/website/pages'],
  ['post','/api/admin/website/pages'],
  ['put','/api/admin/website/pages/{slug}'],
  ['post','/api/admin/website/pages/{slug}/publication'],
  // Exact sponsor/partner draft and manager-publication APIs. Never wildcard.
  ['get','/api/admin/website/sponsorships'],
  ['post','/api/admin/website/sponsorships/draft'],
  // Native creation can only produce an unpublished, owner-scoped draft.
  ['post','/api/admin/website/sponsorships/native'],
  ['post','/api/admin/website/sponsorships/publication'],
  ['get','/api/admin/website/news'],
  ['get','/api/admin/website/news-drafts'],
  ['post','/api/admin/website/news-drafts'],
  ['put','/api/admin/website/news-drafts/{id}'],
  ['get','/api/admin/website/news/{id}'],
  ['put','/api/admin/website/news/{id}'],
];

// These private endpoints are intentionally excluded from the public OpenAPI
// inventory. Each one has an explicit WebAdmin authorization guard in source,
// and the preview routing review test pins that source policy. No wildcards.
export const reviewedHiddenAdminEntries = [
  ['get','/api/admin/hub/fan-subscriptions/overview'],
  ['get','/api/admin/hub/classroom-curriculum'],
  ['put','/api/admin/hub/classroom-curriculum/{contentId}'],
  ['get','/api/admin/hub/players/{playerId}/learning-position'],
  ['put','/api/admin/hub/players/{playerId}/learning-position'],
  ['get','/api/admin/football/territories'],
  ['post','/api/admin/football/territories'],
  ['get','/api/admin/football/territories/access'],
  ['put','/api/admin/football/territories/access'],
  ['get','/api/admin/football/territories/metrics'],
  ['get','/api/admin/football/group-players'],
  ['post','/api/admin/football/players/search'],
  ['put','/api/admin/football/groups/{groupId}/name'],
  // Read-only named Academy site inventory evidence; no stock mutation route.
  ['get','/api/admin/pos/sites/{locationId}/stock-reconciliation'],
  ['get','/api/admin/access/users'],
  ['post','/api/admin/access/users'],
  ['post','/api/admin/access/users/status'],
  ['post','/api/admin/access/users/role'],
  ['post','/api/admin/staff/coaches'],
  ['get','/api/admin/academy/intake'],
  // Explicit private review routes; API and SQL independently require Academy Super Admin.
  ['get','/api/admin/academy/office/guardians'],
  ['post','/api/admin/academy/office/guardians/verify'],
  ['post','/api/admin/academy/office/guardians/revoke'],
  ['post','/api/admin/auth/profile/name'],
  ['post','/api/admin/auth/profile/password'],
  // Future self-service contact address is still an admin-cookie operation;
  // it does not enable auth/recovery email use.
  ['get','/api/admin/auth/profile/contact-email'],
  ['post','/api/admin/auth/profile/contact-email']
];

function entryPattern(path,operation) {
  if(!path.startsWith('/api/') || path.includes('//') || path.endsWith('/'))
    throw new Error('Unsafe preview entry route');
  const params=[...(operation?.parameters??[])];
  return path.split('/').slice(1).map(segment=>{
    const variable=/^\{([A-Za-z_][A-Za-z0-9_]*)\}$/.exec(segment);
    const pdf=/^\{([A-Za-z_][A-Za-z0-9_]*)\}[.]pdf$/.exec(segment);
    if(variable||pdf){
      const parameter=(variable??pdf)[1];
      if(operation&&!params.some(p=>p?.in==='path'&&p.name===parameter&&p.required===true))
        throw new Error('Reviewed entry route requires a declared path parameter');
      return pdf?'[0-9a-fA-F-]{36}[.]pdf':'[^/]+';
    }
    if(!/^[A-Za-z0-9_-]+(?:[.]pdf)?$/.test(segment))
      throw new Error('Unsafe preview route segment');
    return segment.replaceAll('.', '[.]');
  }).join('/');
}


export const publicWebExpression = '^/(?:$|__data[.]json$|(?:team|about|contact|privacy|terms|fixtures|calendar|venues)(?:/__data[.]json)?$|(?:news|academy|match|player|staff|events|partners|sponsors|seasons|pages)(?:/[^/]+)?(?:/__data[.]json)?$|(?:_app|club|mobile)/(?:[A-Za-z0-9_-][A-Za-z0-9_.-]*/)*[A-Za-z0-9_-][A-Za-z0-9_.-]*$|favicon[.]ico$|robots[.]txt$)';
export const publicApiExpression = '^/api/public/(?:site|sections|news(?:/[^/]+)?|website/pages)$';

export function reviewedPublicReads(document) {
  // This curated list never opens a newly added anonymous API automatically.
  for (const path of publicApiPaths) {
    const operation = document?.paths?.[path]?.get;
    if (!operation || (operation.security !== undefined &&
        (!Array.isArray(operation.security) || operation.security.length !== 0))) {
      throw new Error('Reviewed public reads must remain explicitly anonymous.');
    }
  }
  const parameter = document.paths['/api/public/news/{id}'].get.parameters;
  if (!parameter?.some(value => value.in === 'path' && value.name === 'id' && value.required === true)) {
    throw new Error('The public article path parameter must remain required.');
  }
  return publicApiPaths;
}

export function protectedOperations(document) {
  if (!document || typeof document !== 'object' || Array.isArray(document) ||
      !document.paths || typeof document.paths !== 'object' || Array.isArray(document.paths) ||
      document.security !== undefined) throw new Error('Explicit operation security is required.');
  const operations = [];
  for (const [path, item] of Object.entries(document.paths)) {
    if (!item || typeof item !== 'object' || Array.isArray(item)) throw new Error('Invalid path item.');
    for (const [method, operation] of Object.entries(item)) {
      if (!methods.has(method)) continue;
      if (!operation || typeof operation !== 'object' || Array.isArray(operation)) throw new Error('Invalid operation.');
      const security = operation.security;
      const mentionsMobile = Array.isArray(security) && security.some(value =>
        value && typeof value === 'object' && Object.hasOwn(value, 'Mobile'));
      if (!mentionsMobile) continue;
      if (security.length !== 1 || Object.keys(security[0]).length !== 1 ||
          !Array.isArray(security[0].Mobile) || security[0].Mobile.length !== 0) {
        throw new Error('Ambiguous Mobile operation security.');
      }
      if (!path.startsWith('/api/mobile/') || path.endsWith('/') || path.includes('//')) {
        throw new Error('Unexpected Mobile operation path.');
      }
      const declaredParameters = [...(item.parameters ?? []), ...(operation.parameters ?? [])];
      const segments = path.split('/').slice(1).map(segment => {
        const parameter = /^\{([a-zA-Z_][a-zA-Z0-9_]*)\}$/.exec(segment);
        if (parameter) {
          if (!declaredParameters.some(value => value?.in === 'path' && value.name === parameter[1] && value.required === true)) {
            throw new Error('A path parameter must be explicitly required.');
          }
          return '[^/]+';
        }
        // Only two reviewed private invoice downloads use a literal .pdf
        // suffix. Never allow arbitrary dotted paths in the Mobile router.
        if (method === 'get' && path === '/api/mobile/finance/invoices/multi.pdf' &&
            segment === 'multi.pdf') return 'multi[.]pdf';
        if (method === 'get' && path === '/api/mobile/finance/invoices/{invoiceId}.pdf' &&
            segment === '{invoiceId}.pdf') {
          if (!declaredParameters.some(value => value?.in === 'path' &&
              value.name === 'invoiceId' && value.required === true))
            throw new Error('A path parameter must be explicitly required.');
          return '[0-9a-fA-F-]{36}[.]pdf';
        }
        if (!/^[a-zA-Z0-9_-]+$/.test(segment)) throw new Error('Unsafe Mobile operation path.');
        return segment;
      });
      operations.push({ method: method.toUpperCase(), path, expression: '/' + segments.join('/') });
    }
  }
  if (operations.length === 0) throw new Error('No explicit Mobile operations were found.');
  return operations.sort((left, right) => left.method.localeCompare(right.method) || left.path.localeCompare(right.path));
}

// Explicitly reviewed internal Mobile GET. Never expose a generic path prefix.
export const reviewedHiddenMobileEntries = [
  ['get','/api/mobile/hub/player-classrooms']
];

export function bearerRouteGroups(document) {
  const groups = new Map();
  for (const operation of protectedOperations(document)) {
    const group = groups.get(operation.method) ?? [];
    group.push(operation.expression);
    groups.set(operation.method, group);
  }
  for(const [method,path] of reviewedHiddenMobileEntries){
    if(document?.paths?.[path]?.[method])
      throw new Error('Promote newly documented private Mobile route to contract review.');
    const expressions=groups.get(method.toUpperCase())??[];
    expressions.push('/'+entryPattern(path));
    groups.set(method.toUpperCase(),expressions);
  }
  return [...groups].map(([method, expressions]) => ({ method, expression: `^(?:${expressions.join('|')})$` }));
}

export function entryRouteGroups(document) {
  const groups = new Map();
  const reviewed = [
    ...entryOperations,
    ...reviewedAdminEntries.map(([method,path])=>[method,path,[{WebAdmin:[]}]])
  ];
  const authorized = new Set();
  for (const [method,path,security] of reviewed) {
    const operation = document?.paths?.[path]?.[method];
    if (!operation || JSON.stringify(operation.security ?? []) !== JSON.stringify(security)) {
      throw new Error('Reviewed application entry security changed.');
    }
    const expression = entryPattern(path, {
      parameters:[...(document.paths[path]?.parameters ?? []),...(operation.parameters ?? [])]
    });
    authorized.add(method+'|'+path);
    const paths = groups.get(method.toUpperCase()) ?? [];
    paths.push('/'+expression); groups.set(method.toUpperCase(), paths);
  }
  // Any newly published admin contract operation is blocked until explicitly
  // reviewed. Public login/bootstrap operations remain in entryOperations.
  for(const [path,item] of Object.entries(document?.paths??{})){
    if(!path.startsWith('/api/admin/'))continue;
    for(const [method,operation] of Object.entries(item)){
      if(!methods.has(method))continue;
      if(!authorized.has(method+'|'+path))
        throw new Error('Unreviewed admin API requires explicit preview route approval.');
      if(path.includes('//') || operation?.security===undefined &&
        !entryOperations.some(([m,p,security])=>m===method&&p===path&&security.length===0))
        throw new Error('Admin preview route security is not explicit.');
    }
  }
  for(const [method,path] of reviewedHiddenAdminEntries){
    if(document?.paths?.[path]?.[method])
      throw new Error('Promote newly documented private route to approved contract list.');
    const expression=entryPattern(path);
    const paths=groups.get(method.toUpperCase())??[];
    paths.push('/'+expression); groups.set(method.toUpperCase(),paths);
  }
  return [...groups].map(([method, paths]) => ({
    method, expression:`^(?:${paths.join('|')})$`
  }));
}

export function generateEdge(document, hostname, contractDigest) {
  if (hostname !== supportedHost) throw new Error('The preview hostname must be explicitly reviewed.');
  if (!/^[a-f0-9]{64}$/.test(contractDigest)) throw new Error('A contract SHA-256 is required.');
  const groups = bearerRouteGroups(document);
  const entries = entryRouteGroups(document);
  reviewedPublicReads(document);
  reviewedPublicMedia(document);
  const matchers = groups.map(({ method, expression }) => `  @private_${method.toLowerCase()} {
    method ${method}
    path_regexp private_${method.toLowerCase()} ${expression}
  }`).join('\n');
  const entryMatchers = entries.map(({ method, expression }) => `  @entry_${method.toLowerCase()} {
    method ${method}
    path_regexp entry_${method.toLowerCase()} ${expression}
  }`).join('\n');
  const entryHandles = entries.map(({ method }) => `    handle @entry_${method.toLowerCase()} {
      # The API enforces its own cookie/JWT, throttling and request guards.
      reverse_proxy api:8080
    }`).join('\n');
  const handles = groups.map(({ method }) => `    handle @private_${method.toLowerCase()} {
      # A match only selects the API; its JWT and current-scope checks still apply.
      reverse_proxy api:8080
    }`).join('\n');
  const responseHeaders = (indent) => [
    'header {',
    '  X-Frame-Options DENY',
    '  X-Content-Type-Options nosniff',
    '  Referrer-Policy no-referrer',
    '  X-Robots-Tag "noindex, nofollow, noarchive"',
    '  Cache-Control "no-store"',
    '  -Server',
    '}'
  ].map(line => indent + line).join('\n');
  return `# Generated from reviewed application contracts; application authentication stays authoritative.
# OpenAPI SHA-256: ${contractDigest}
# https://caddyserver.com/docs/caddyfile/matchers
# https://caddyserver.com/docs/caddyfile/directives/request_header
{
  auto_https off
  admin 127.0.0.1:2019
  servers {
    max_header_size 32KB
  }
}

https://${hostname}:8443 {
  tls /certificates/current/fullchain.pem /certificates/current/privkey.pem
  encode zstd gzip
${responseHeaders('  ')}

  # Authentication errors run outside the normal deferred-header chain.
  # Keep its challenge header and original status; expose no error details.
  handle_errors {
${responseHeaders('    ')}
    respond "" {err.status_code}
  }

${matchers}
${entryMatchers}
  @cached_basic header_regexp cached_basic Authorization (?i)^Basic[[:blank:]]+
  @public_web {
    method GET HEAD
    path_regexp public_web ${publicWebExpression}
  }
  @public_api {
    method GET
    path_regexp public_api ${publicApiExpression}
  }
  @public_media {
    method GET HEAD
    path_regexp public_media ${publicMediaExpression}
  }
  @legacy_upload {
    method GET HEAD
    path /uploads/* /api/uploads/*
  }
  @api path /api/*

  route {
    # Remove old browser-cached preview credentials. Preserve application Bearer
    # headers and cookies on the approved API operations below.
    request_header @cached_basic -Authorization
    handle @public_web {
      reverse_proxy web:3000 {
        header_up -Authorization
        header_up -Cookie
      }
    }
    handle @public_api {
      reverse_proxy api:8080 {
        header_up -Authorization
        header_up -Cookie
      }
    }
${entryHandles}
    handle @public_media {
      # Public image folder allowlist; API validates filenames, extensions and disk paths.
      reverse_proxy api:8080 {
        header_up -Authorization
        header_up -Cookie
      }
    }
    handle @legacy_upload {
      # Private restored uploads are not published. Missing image references
      # return a plain404 so browsers can use the public crest fallback.
      respond "" 404
    }
${handles}
    handle @api {
      # Unlisted APIs and diagnostic endpoints are not public preview routes.
      respond "" 404
    }
    handle {
      # Every website destination reaches the application, including its own
      # admin/member login and ordinary404 pages. No browser password dialog.
      reverse_proxy web:3000 {
        header_up -Authorization
      }
    }
  }
}
`;
}

async function main(args) {
  const options = new Map();
  let check = false;
  for (let index = 0; index < args.length; index++) {
    const key = args[index];
    if (key === '--check' && !check) { check = true; continue; }
    if (!['--contract', '--host', '--output'].includes(key) || options.has(key) || !args[index + 1]) {
      throw new Error('Usage: generate-edge.mjs --contract <openapi.json> --host <reviewed-hostname> --output <Caddyfile> [--check]');
    }
    options.set(key, args[++index]);
  }
  if (options.size !== 3) throw new Error('The contract, hostname and new output path are required.');
  const source = await readFile(options.get('--contract'));
  if (source.length > 4 * 1024 * 1024) throw new Error('The API contract exceeds its size bound.');
  const document = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(source));
  const digest = createHash('sha256').update(source).digest('hex');
  const output = generateEdge(document, options.get('--host'), digest);
  if (check) {
    if (await readFile(options.get('--output'), 'utf8') !== output) throw new Error('The generated edge is stale.');
  } else await writeFile(options.get('--output'), output, { flag: 'wx', mode: 0o600 });
  console.log(`${check ? 'Verified' : 'Generated'} preview edge for ${protectedOperations(document).length} explicitly protected operations.`);
}

if (process.argv[1] && pathToFileURL(process.argv[1]).href === import.meta.url) {
  main(process.argv.slice(2)).catch(() => {
    // File-system and input errors may contain deployment paths; emit no input.
    console.error('Preview edge generation refused. Check explicit arguments, contract and a new output path.');
    process.exitCode = 1;
  });
}
