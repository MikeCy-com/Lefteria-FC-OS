import { mkdir, writeFile } from 'node:fs/promises';
import openapiTS, { astToString } from 'openapi-typescript';

const schema = new URL('../../contracts/openapi.json', import.meta.url);
const output = new URL('../../frontend/src/lib/api/', import.meta.url);
const ast = await openapiTS(schema, { alphabetize: true });
await mkdir(output, { recursive: true });
await writeFile(new URL('schema.d.ts', output),
  '// Generated from contracts/openapi.json. Run npm run api:generate; do not edit.\n' + astToString(ast));
console.log('Generated TypeScript API contract from contracts/openapi.json.');
