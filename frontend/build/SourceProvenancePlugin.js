const { execFileSync } = require('child_process');
const path = require('path');

function sourceIdentity() {
  try {
    const git = (...args) => execFileSync('git', args, {
      cwd: path.resolve(__dirname, '../..'),
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'ignore']
    }).trim();
    return {
      productId: 'bookshelf-audiobooks',
      sourceCommit: git('rev-parse', 'HEAD'),
      sourceTree: git('rev-parse', 'HEAD^{tree}'),
      sourceBranch: git('branch', '--show-current'),
      buildDirty: git('status', '--porcelain', '--untracked-files=normal') !== ''
    };
  } catch {
    return { productId: 'bookshelf-audiobooks', sourceCommit: '', sourceTree: '', sourceBranch: '', buildDirty: true };
  }
}

class SourceProvenancePlugin {
  apply(compiler) {
    let identity = null;
    compiler.hooks.beforeCompile.tap('SourceProvenancePlugin', () => { identity = sourceIdentity(); });
    compiler.hooks.thisCompilation.tap('SourceProvenancePlugin', (compilation) => {
      compilation.hooks.processAssets.tap(
        { name: 'SourceProvenancePlugin', stage: compiler.webpack.Compilation.PROCESS_ASSETS_STAGE_ADDITIONAL },
        () => {
          const current = sourceIdentity();
          const receipt = { ...identity, buildDirty: identity.buildDirty || JSON.stringify(current) !== JSON.stringify(identity) };
          compilation.emitAsset('source-provenance.json', new compiler.webpack.sources.RawSource(JSON.stringify(receipt)));
        }
      );
    });
  }
}

module.exports = SourceProvenancePlugin;
