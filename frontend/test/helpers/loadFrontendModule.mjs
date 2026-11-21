import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { transformSync } from '@babel/core';

const require = createRequire(import.meta.url);
const sourceRoot = fileURLToPath(new URL('../../src/', import.meta.url));

// Execute production components and selectors with explicitly supplied UI/store
// boundaries. Utilities and selector dependencies still run their real code.
export function loadFrontendModule(relativePath, replacements = {}) {
  const modules = new Map();

  function load(path) {
    const filename = path.endsWith('.js') ? path : `${path}.js`;
    if (modules.has(filename)) {
      return modules.get(filename).exports;
    }

    const module = { exports: {} };
    modules.set(filename, module);
    const { code } = transformSync(readFileSync(filename, 'utf8'), {
      filename,
      configFile: false,
      babelrc: false,
      presets: [
        ['@babel/preset-env', { targets: { node: 'current' } }],
        '@babel/preset-react'
      ]
    });

    const localRequire = (specifier) => {
      if (Object.hasOwn(replacements, specifier)) {
        return replacements[specifier];
      }
      if (specifier.endsWith('.css')) {
        return {};
      }
      if (specifier.startsWith('.')) {
        return load(resolve(dirname(filename), specifier));
      }
      if (/^(Utilities|Store\/Selectors)\//.test(specifier)) {
        return load(resolve(sourceRoot, specifier));
      }
      return require(specifier);
    };

    Function('require', 'module', 'exports', code)(localRequire, module, module.exports);
    return module.exports;
  }

  return Array.isArray(relativePath)
    ? relativePath.map((path) => load(resolve(sourceRoot, path)))
    : load(resolve(sourceRoot, relativePath));
}
