import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';

const methods = new Set(['get', 'post', 'put', 'patch', 'delete', 'head', 'options', 'trace']);
const supportedHost = 'srv1649602.hstgr.cloud';
const publicApiPaths = ['/api/public/site', '/api/public/news', '/api/public/news/{id}', '/api/public/sections'];
const entryOperations = [
  ['post', '/api/admin/auth/login', []], ['post', '/api/admin/auth/logout', []],
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
export const publicWebExpression = '^/(?:$|__data[.]json$|(?:team|about|contact|privacy|terms|fixtures|calendar|venues)(?:/__data[.]json)?$|(?:news|academy|match|player|staff|events|partners|sponsors|seasons)(?:/[^/]+)?(?:/__data[.]json)?$|(?:_app|club|mobile)/(?:[A-Za-z0-9_-][A-Za-z0-9_.-]*/)*[A-Za-z0-9_-][A-Za-z0-9_.-]*$|favicon[.]ico$|robots[.]txt$)';
export const publicApiExpression = '^/api/public/(?:site|sections|news(?:/[^/]+)?)$';

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
        if (!/^[a-zA-Z0-9_-]+$/.test(segment)) throw new Error('Unsafe Mobile operation path.');
        return segment;
      });
      operations.push({ method: method.toUpperCase(), path, expression: '/' + segments.join('/') });
    }
  }
  if (operations.length === 0) throw new Error('No explicit Mobile operations were found.');
  return operations.sort((left, right) => left.method.localeCompare(right.method) || left.path.localeCompare(right.path));
}

export function bearerRouteGroups(document) {
  const groups = new Map();
  for (const operation of protectedOperations(document)) {
    const group = groups.get(operation.method) ?? [];
    group.push(operation.expression);
    groups.set(operation.method, group);
  }
  return [...groups].map(([method, expressions]) => ({ method, expression: `^(?:${expressions.join('|')})$` }));
}

export function entryRouteGroups(document) {
  const groups = new Map();
  for (const [method, path, security] of entryOperations) {
    const operation = document?.paths?.[path]?.[method];
    if (!operation || JSON.stringify(operation.security ?? []) !== JSON.stringify(security)) {
      throw new Error('Reviewed application entry security changed.');
    }
    const expression = path.replace(/\{(?:fileName|id)\}/g, '[^/]+');
    for (const parameter of ['fileName','id']) {
      if (path.includes('{'+parameter+'}') &&
          ![...(document.paths[path]?.parameters ?? []),...(operation.parameters ?? [])]
            .some(value => value?.name === parameter && value.in === 'path' && value.required === true)) {
        throw new Error('Reviewed entry route requires a declared path parameter.');
      }
    }
    const paths = groups.get(method.toUpperCase()) ?? [];
    paths.push(expression); groups.set(method.toUpperCase(), paths);
  }
  return [...groups].map(([method, paths]) => ({ method, expression: `^(?:${paths.join('|')})$` }));
}

export function generateEdge(document, hostname, contractDigest) {
  if (hostname !== supportedHost) throw new Error('The preview hostname must be explicitly reviewed.');
  if (!/^[a-f0-9]{64}$/.test(contractDigest)) throw new Error('A contract SHA-256 is required.');
  const groups = bearerRouteGroups(document);
  const entries = entryRouteGroups(document);
  reviewedPublicReads(document);
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
