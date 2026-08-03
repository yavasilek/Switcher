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

function resolveUiModule(installRoot) {
  const candidates = [
    join(installRoot, 'vendor/src/workGraphBacklogUiServer.mjs'),
    join(installRoot, 'src/workGraphBacklogUiServer.mjs'),
  ];
  for (const candidate of candidates) {
    try {
      readFileSync(candidate);
      return candidate;
    } catch {
      // try next
    }
  }
  throw new Error('workGraphBacklogUiServer.mjs не найден — npm install -D @work-graph/cli');
}

const installRoot = resolveInstallRoot();
const { startBacklogUiServer } = await import(pathToFileURL(resolveUiModule(installRoot)).href);

const port = Number(process.env.WORKGRAPH_BACKLOG_UI_PORT ?? config.uiPort ?? 4177);
const { host, port: boundPort } = await startBacklogUiServer({
  hostRoot: projectRoot,
  cwd: projectRoot,
  hostLabel: config.label,
  port,
});

console.log(`Work Graph UI (${config.label ?? projectRoot}): http://${host}:${boundPort}/`);
