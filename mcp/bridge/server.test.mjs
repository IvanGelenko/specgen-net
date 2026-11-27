import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, rm, mkdir, readdir, writeFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

const bridge = fileURLToPath(new URL('./server.mjs', import.meta.url));
const request = (id, method, params) => JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n';

function startBridge(env = {}) {
  const child = spawn(process.execPath, [bridge], {
    env: { ...process.env, ...env },
    stdio: ['pipe', 'pipe', 'inherit'],
  });
  let stdout = '';
  child.stdout.setEncoding('utf8').on('data', chunk => { stdout += chunk; });
  const replies = once(child, 'close').then(([code]) => {
    assert.equal(code, 0);
    return stdout.trim().split('\n').filter(Boolean).map(line => JSON.parse(line));
  });
  return { child, replies };
}

test('stdio drains delayed requests in input order after EOF', { timeout: 5000 }, async () => {
  const temp = await mkdtemp(join(tmpdir(), 'plant3d-bridge-test-'));
  const server = createServer((_req, res) => {
    setTimeout(() => res.end(JSON.stringify({ ok: true, result: 'Plant response' })), 100);
  });
  const requested = once(server, 'request');
  try {
    await mkdir(join(temp, 'Plant3dMcp'));
    await writeFile(join(temp, 'Plant3dMcp', 'token'), 'test');
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    const { child, replies } = startBridge({ LOCALAPPDATA: temp, PLANT3D_URL: 'http://127.0.0.1:' + server.address().port });
    child.stdin.write(request(1, 'tools/call', { name: 'ping' }));
    await requested;
    child.stdin.end(request(2, 'ping'));
    const responses = await replies;
    assert.deepEqual(responses.map(reply => reply.id), [1, 2]);
    assert.equal(responses[0].result.isError, false);
  } finally {
    server.closeAllConnections();
    server.close();
    await rm(temp, { recursive: true, force: true });
  }
});

test('advertises tool arguments and write annotations and hides the development probe', { timeout: 5000 }, async () => {
  const { child, replies } = startBridge();
  child.stdin.end(request(1, 'tools/list') + request(2, 'tools/call', { name: 'probe_connector_wrapper' }));
  const [list, probe] = await replies;
  const tools = new Map(list.result.tools.map(tool => [tool.name, tool]));
  assert.equal(tools.get('set_layer').annotations.readOnlyHint, false);
  assert.equal(tools.get('set_layer').inputSchema.properties.dry_run.type, 'boolean');
  assert.equal(tools.get('restore_branch_link').annotations.readOnlyHint, false);
  assert.equal(tools.get('port_conditions').annotations.readOnlyHint, true);
  assert.equal(tools.get('create_joint').inputSchema.properties.s2_first.type, 'boolean');
  assert.equal(tools.has('probe_connector_wrapper'), false);
  assert.match(probe.result.content[0].text, /Unknown tool/);
});

test('rejects outside catalog paths and cleans up temporary copies of invalid catalogs', { timeout: 5000 }, async () => {
  const temp = await mkdtemp(join(tmpdir(), 'plant3d-bridge-test-'));
  try {
    const catalogDir = join(temp, 'catalogs');
    await mkdir(catalogDir);
    const invalid = join(catalogDir, 'invalid.pspc');
    const outside = join(temp, 'outside.pcat');
    await Promise.all([writeFile(invalid, 'invalid database'), writeFile(outside, 'outside catalog directory')]);
    const valid = join(catalogDir, 'valid.pspc');
    const db = new DatabaseSync(valid);
    try {
      db.exec(`
        CREATE TABLE PnPBase (PnPID INTEGER, PnPClassName TEXT);
        CREATE TABLE EngineeringItems (PnPID INTEGER, PartCategory TEXT);
        INSERT INTO PnPBase VALUES (1, 'Pipe');
        INSERT INTO EngineeringItems VALUES (1, 'Pipes');
      `);
    } finally {
      db.close();
    }
    const { child, replies } = startBridge({ TMP: temp, TEMP: temp, TMPDIR: temp, PLANT3D_CATALOG_DIR: catalogDir, LOCALAPPDATA: '' });
    const list = path => ({ name: 'list_catalog_parts', arguments: { path } });
    child.stdin.end(request(1, 'tools/call', list(outside)) + request(2, 'tools/call', list(invalid)) + request(3, 'tools/call', list(valid)));
    const [rejected, failed, accepted] = await replies;
    assert.match(rejected.result.content[0].text, /inside PLANT3D_CATALOG_DIR/);
    assert.equal(failed.result.isError, true);
    assert.equal(accepted.result.isError, false);
    assert.equal(JSON.parse(accepted.result.content[0].text).total, 1);
    assert.deepEqual((await readdir(temp)).sort(), ['catalogs', 'outside.pcat']);
  } finally {
    await rm(temp, { recursive: true, force: true });
  }
});
