import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { parse } from '@babel/parser';

const sourceRoot = fileURLToPath(new URL('../src/', import.meta.url));

test('function components use parameter defaults instead of removed React defaultProps', () => {
  const offenders = [];
  function visit(directory) {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (entry.isDirectory()) {
        visit(path);
      } else if (/\.(js|tsx|ts)$/.test(entry.name) && !entry.name.endsWith('.d.ts')) {
        const source = readFileSync(path, 'utf8');
        if (!/\.defaultProps\s*=/.test(source)) continue;
        const { program } = parse(source, { sourceType: 'unambiguous', plugins: ['jsx', 'typescript', 'exportDefaultFrom'] });
        const classes = new Set(program.body.flatMap((node) => {
          const declaration = node.type === 'ExportNamedDeclaration' || node.type === 'ExportDefaultDeclaration' ? node.declaration : node;
          return declaration?.type === 'ClassDeclaration' && declaration.id ? [declaration.id.name] : [];
        }));
        for (const statement of program.body) {
          const assignment = statement.type === 'ExpressionStatement' && statement.expression;
          const target = assignment?.type === 'AssignmentExpression' && assignment.left;
          if (target?.type === 'MemberExpression' && target.property.name === 'defaultProps' && target.object.type === 'Identifier' && !classes.has(target.object.name)) {
            offenders.push(`${path.slice(sourceRoot.length)}: ${target.object.name}`);
          }
        }
      }
    }
  }
  visit(sourceRoot);
  assert.deepEqual(offenders, []);
});
