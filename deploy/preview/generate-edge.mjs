import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';

const methods = new Set(['get', 'post', 'put', 'patch', 'delete', 'head', 'options', 'trace']);
const supportedHost = 'srv1649602.hstgr.cloud';

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

export function generateEdge(document, hostname, contractDigest) {
  if (hostname !== supportedHost) throw new Error('The preview hostname must be explicitly reviewed.');
  if (!/^[a-f0-9]{64}$/.test(contractDigest)) throw new Error('A contract SHA-256 is required.');
  const groups = bearerRouteGroups(document);
  const matchers = groups.map(({ method, expression }) => `  @private_${method.toLowerCase()} {
    method ${method}
    path_regexp private_${method.toLowerCase()} ${expression}
    header_regexp bearer_${method.toLowerCase()} Authorization (?i)^Bearer[[:blank:]]+[^[:blank:]]+$
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
  return `# Generated from explicit Mobile operation security; do not broaden by hand.
# OpenAPI SHA-256: ${contractDigest}
# https://caddyserver.com/docs/caddyfile/matchers
# https://caddyserver.com/docs/caddyfile/directives/basic_auth
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
  @api path /api/*

  route {
${handles}
    handle {
      # New, anonymous and non-Mobile routes remain behind the preview gate.
      # In particular, a Bearer header cannot bypass OTP or legacy exchange.
      basic_auth bcrypt "Lefteria OS preview" {
        preview {env.PREVIEW_GATE_HASH}
      }
      reverse_proxy @api api:8080 {
        header_up -Authorization
      }
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
