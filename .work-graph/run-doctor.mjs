#!/usr/bin/env node
import { spawnSync } from 'node:child_process';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { resolveWorkGraphInstallRoot } from './resolve-install-root.mjs';

const projectRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const installRoot = resolveWorkGraphInstallRoot(projectRoot);
const cliPath = join(installRoot, 'bin', 'work-graph.mjs');
const result = spawnSync(process.execPath, [cliPath, 'doctor', projectRoot], {
  cwd: projectRoot,
  env: {
    ...process.env,
    WG_PROJECT_ROOT: projectRoot,
    WORKGRAPH_ROOT: projectRoot,
  },
  stdio: 'inherit',
});

if (result.error) throw result.error;
process.exitCode = result.status ?? 1;
