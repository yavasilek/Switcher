#!/usr/bin/env node
import { readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { resolveWorkGraphInstallRoot } from './resolve-install-root.mjs';

const projectRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const config = JSON.parse(readFileSync(join(projectRoot, '.work-graph/config.json'), 'utf8'));

process.env.WG_PROJECT_ROOT = projectRoot;
process.env.WORKGRAPH_ROOT = projectRoot;

function resolveInstallRoot() {
  if (process.env.WORKGRAPH_ENGINE_ROOT) {
    return resolve(process.env.WORKGRAPH_ENGINE_ROOT);
  }
  if (config.engineRoot) {
    console.warn('[work-graph] config.engineRoot устарел — используйте npm packages');
    return resolve(config.engineRoot);
  }
  return resolveWorkGraphInstallRoot(projectRoot);
}

function resolveMcpModule(installRoot) {
  const candidates = [
    join(installRoot, 'vendor/packages/workgraph-mcp/src/index.mjs'),
    join(installRoot, 'packages/workgraph-mcp/src/index.mjs'),
  ];
  for (const candidate of candidates) {
    try {
      readFileSync(candidate);
      return candidate;
    } catch {
      // try next
    }
  }
  throw new Error('workgraph-mcp entry не найден — npm install -D @work-graph/mcp');
}

const installRoot = resolveInstallRoot();
await import(pathToFileURL(resolveMcpModule(installRoot)).href);
