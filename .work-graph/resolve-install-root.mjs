import { execFileSync } from 'node:child_process';
import { createRequire } from 'node:module';
import { dirname, join, resolve } from 'node:path';

function primaryCheckoutRoot(projectRoot) {
  try {
    const commonGitDir = execFileSync(
      'git',
      ['rev-parse', '--path-format=absolute', '--git-common-dir'],
      { cwd: projectRoot, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] },
    ).trim();
    return dirname(resolve(projectRoot, commonGitDir));
  } catch {
    return null;
  }
}

function uniqueRoots(roots) {
  const seen = new Set();
  return roots.filter((root) => {
    if (!root) return false;
    const normalized = resolve(root);
    const key = process.platform === 'win32' ? normalized.toLowerCase() : normalized;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

export function resolveWorkGraphInstallRoot(projectRoot) {
  const dependencyRoots = uniqueRoots([
    process.env.WORKGRAPH_DEPENDENCY_ROOT,
    projectRoot,
    primaryCheckoutRoot(projectRoot),
  ]);

  for (const dependencyRoot of dependencyRoots) {
    try {
      const require = createRequire(join(dependencyRoot, 'package.json'));
      return dirname(require.resolve('@work-graph/cli/package.json'));
    } catch {
      // Try the next checkout. Codex worktrees normally reuse the primary checkout install.
    }
  }

  throw new Error(
    'Work Graph dependencies not found. Run npm install in this worktree or in the primary checkout.',
  );
}
