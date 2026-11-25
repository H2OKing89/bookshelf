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

// Record the same backend-compatible identity for production Vite artifacts.
function SourceProvenancePlugin() {
  let identity = null;
  return {
    name: 'bookshelf-source-provenance',
    apply: 'build',
    buildStart() {
      identity = sourceIdentity();
    },
    generateBundle() {
      const current = sourceIdentity();
      const receipt = { ...identity, buildDirty: identity.buildDirty || JSON.stringify(current) !== JSON.stringify(identity) };
      this.emitFile({ type: 'asset', fileName: 'source-provenance.json', source: JSON.stringify(receipt) });
    }
  };
}

module.exports = SourceProvenancePlugin;
