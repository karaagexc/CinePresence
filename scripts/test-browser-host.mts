// Test the packaged native executable without publishing any activity.
// Bind the app pipe ourselves; fail if another server already owns it.
import net from 'node:net';
import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import assert from 'node:assert/strict';
import path from 'node:path';
const exe = path.resolve(process.argv[2] ?? 'artifacts/CinePresence-0.2.3-win-x64/CinePresence.BrowserHost.exe');
const suffix = createHash('sha256').update(process.env.USERDOMAIN + '\\' + process.env.USERNAME).digest('hex').slice(0, 24).toUpperCase();
const pipeName = '\\\\.\\pipe\\CinePresence.Browser.' + suffix;
function frame(value: unknown) { const body = Buffer.from(JSON.stringify(value)); const header = Buffer.alloc(4); header.writeInt32LE(body.length); return Buffer.concat([header, body]); }
const received: {items?: {title: string}[]; disconnect?: boolean}[] = [];
const server = net.createServer(socket => {
  let buffer = Buffer.alloc(0);
  socket.on('data', data => {
    buffer = Buffer.concat([buffer, data]);
    if (buffer.length < 4 || buffer.length < 4 + buffer.readInt32LE()) return;
    received.push(JSON.parse(buffer.subarray(4, 4 + buffer.readInt32LE()).toString('utf8')));
    socket.end(frame({ connected: true, message: 'Local test server' }));
  });
  socket.on('error', () => {});
});
(async () => {
  await new Promise<void>((resolve, reject) => { server.once('error', reject); server.listen(pipeName, resolve); });
  const child = spawn(exe, ['chrome-extension://ndikeejjjaangmgeohkglafbldikbnag/'], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
  const chunks: Buffer[] = []; let error = '';
  child.stdout.on('data', x => chunks.push(x)); child.stderr.on('data', x => error += x);
  const timeout = setTimeout(() => child.kill(), 10000);
  const packet = { version: 1, clientId: '90e2420b-7df2-466a-87df-4eddf7c9ee11', browser: 'msedge', items: [{ id: '1', title: 'Amélie · 你好' }] };
  const input = frame(packet); child.stdin.write(input.subarray(0, 2)); child.stdin.end(input.subarray(2));
  const code = await new Promise((resolve, reject) => { child.once('error', reject); child.once('exit', resolve); });
  clearTimeout(timeout); assert.equal(code, 0, error);
  const output = Buffer.concat(chunks); assert.equal(JSON.parse(output.subarray(4).toString('utf8')).connected, true);
  assert.equal(received[0].items![0].title, 'Amélie · 你好'); assert.equal(received[1].disconnect, true);
  assert.equal(error, '');
  console.log('Packaged browser host: UTF-8 framing, fragmented input, local pipe, reply and disconnect passed. No presence published.');
})().catch(error => { console.error(error.message); process.exitCode = 1; }).finally(() => server.close());
