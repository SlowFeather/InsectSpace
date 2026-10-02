import http from 'node:http';
import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const root = fs.realpathSync(path.join(repository, '.artifacts/unity6/WebDevelopment'));
const port = Number(process.argv[2] ?? 8086);
if (!Number.isInteger(port) || port < 1024 || port > 65535) throw new Error('Use a port from 1024 to 65535.');
if (!fs.existsSync(path.join(root, 'index.html'))) throw new Error('Run Invoke-Unity.ps1 -Engine Unity -Action Web first.');
const types = { '.html': 'text/html; charset=utf-8', '.js': 'application/javascript', '.wasm': 'application/wasm',
  '.json': 'application/json', '.css': 'text/css', '.png': 'image/png', '.svg': 'image/svg+xml', '.ico': 'image/x-icon' };
const inside = file => file === root || file.startsWith(root + path.sep);
const server = http.createServer((request, response) => {
  response.setHeader('Cross-Origin-Opener-Policy', 'same-origin');
  response.setHeader('Cross-Origin-Embedder-Policy', 'require-corp');
  response.setHeader('Cache-Control', 'no-store');
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    response.writeHead(405, { Allow: 'GET, HEAD' }); response.end(); return;
  }
  try {
    const pathname = decodeURIComponent(new URL(request.url, 'http://127.0.0.1').pathname);
    const candidate = path.resolve(root, '.' + (pathname === '/' ? '/index.html' : pathname));
    if (!inside(candidate)) { response.writeHead(403); response.end(); return; }
    const file = fs.realpathSync(candidate);
    if (!inside(file) || !fs.statSync(file).isFile()) { response.writeHead(403); response.end(); return; }
    const size = fs.statSync(file).size;
    response.writeHead(200, { 'Content-Type': types[path.extname(file)] ?? 'application/octet-stream', 'Content-Length': size });
    if (request.method === 'HEAD') { response.end(); return; }
    const stream = fs.createReadStream(file);
    stream.on('error', () => response.destroy());
    response.on('close', () => stream.destroy());
    stream.pipe(response);
  } catch (error) {
    response.writeHead(error instanceof URIError ? 400 : 404); response.end();
  }
});
server.listen(port, '127.0.0.1', () => console.log(`LOCAL WEB DEVELOPMENT: http://127.0.0.1:${port} — Ctrl+C to stop`));
