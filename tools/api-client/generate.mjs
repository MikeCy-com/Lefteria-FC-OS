import { mkdir, writeFile } from 'node:fs/promises';
import openapiTS, { astToString } from 'openapi-typescript';
import ts from 'typescript';

const schema = new URL('../../contracts/openapi.json', import.meta.url);
const output = new URL('../../frontend/src/lib/api/', import.meta.url);
const ast = await openapiTS(schema, {
  alphabetize: true,
  transform(schemaObject) {
    // Multipart binary values are actual browser files, never JSON strings.
    if (schemaObject.format === 'binary') {
      const blob = ts.factory.createTypeReferenceNode('Blob');
      return schemaObject.nullable || Array.isArray(schemaObject.type) && schemaObject.type.includes('null')
        ? ts.factory.createUnionTypeNode([blob, ts.factory.createLiteralTypeNode(ts.factory.createNull())])
        : blob;
    }
  }
});
await mkdir(output, { recursive: true });
await writeFile(new URL('schema.d.ts', output),
  '// Generated from contracts/openapi.json. Run npm run api:generate; do not edit.\n' + astToString(ast));
console.log('Generated TypeScript API contract from contracts/openapi.json.');
