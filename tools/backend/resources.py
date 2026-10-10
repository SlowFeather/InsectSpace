"""Loopback-only WSL host for existing, immutable YooAsset releases."""
import argparse
import fcntl
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request

parser = argparse.ArgumentParser()
parser.add_argument('action', choices=['start', 'stop', 'test', 'serve'])
parser.add_argument('--root', required=True)
parser.add_argument('--source')
parser.add_argument('--version', default='foundation-001')
parser.add_argument('--core-directory')
parser.add_argument('--world-directory')
parser.add_argument('--port', type=int, default=18088)
args = parser.parse_args()
root = Path(args.root).resolve()
state_dir = root / '.artifacts/validation/backend-resources'
if args.port != 18088:
    state_dir = state_dir / ('port-' + str(args.port))
state_dir.mkdir(parents=True, exist_ok=True)
state_file = state_dir / 'host.json'
script = Path(__file__).resolve()

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def owned(state):
    try:
        command = Path(f"/proc/{state['pid']}/cmdline").read_bytes().split(b'\0')
        start = Path(f"/proc/{state['pid']}/stat").read_text().split()[21]
        return str(script).encode() in command and b'serve' in command and start == state['start']
    except (OSError, KeyError, IndexError):
        return False

if args.action == 'serve':
    release = Path(args.source).resolve()
    manifest = json.loads((release / 'files.json').read_text())
    request_lock = threading.Lock()
    requests = state_dir / 'requests.jsonl'
    requests.write_text('')
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass  # Do not log requests or query strings.

        def do_GET(self):
            self.send_file(False)

        def do_HEAD(self):
            self.send_file(True)

        def send_file(self, head):
            path = urllib.parse.urlsplit(self.path).path
            if path == '/healthz':
                data = json.dumps(dict(role='yooasset-local-host', processId=os.getpid(), operatingSystem='Linux', files=len(manifest))).encode()
                content_type = 'application/json'
            else:
                key = urllib.parse.unquote(path).lstrip('/')
                if key not in manifest or path.startswith('//'):
                    self.send_error(404)
                    return
                file = release / key
                if not file.is_file() or file.is_symlink() or digest(file) != manifest[key]['sha256']:
                    self.send_error(503)
                    return
                data = file.read_bytes()
                content_type = 'application/octet-stream'
            self.send_response(200)
            self.send_header('Content-Type', content_type)
            self.send_header('Content-Length', str(len(data)))
            self.send_header('Cache-Control', 'no-store' if path.endswith('.version') or path == '/healthz' else 'public, max-age=3600')
            self.send_header('Access-Control-Allow-Origin', '*')
            self.send_header('X-Content-Type-Options', 'nosniff')
            self.end_headers()
            if not head:
                self.wfile.write(data)
                if path != '/healthz':
                    # Only successful published keys; never headers or query strings.
                    with request_lock, requests.open('a') as evidence:
                        evidence.write(json.dumps(dict(path='/' + key, bytes=len(data), sha256=manifest[key]['sha256'], at=time.time())) + '\n')
    ThreadingHTTPServer(('127.0.0.1', args.port), Handler).serve_forever()
    sys.exit(0)

lock = (state_dir / 'host.lock').open('w')
fcntl.flock(lock, fcntl.LOCK_EX)
state = json.loads(state_file.read_text()) if state_file.exists() else {}
if args.action == 'stop':
    if owned(state):
        os.kill(state['pid'], signal.SIGTERM)
        deadline = time.monotonic() + 5
        while owned(state) and time.monotonic() < deadline:
            time.sleep(.1)
        if owned(state):
            os.kill(state['pid'], signal.SIGKILL)
    state_file.unlink(missing_ok=True)
    print('WSL_RESOURCE_HOST_STOPPED')
elif args.action == 'start':
    if owned(state):
        raise SystemExit('Resource host already running; stop it before changing releases.')
    source = Path(args.source).resolve()
    files = {}
    directories = {}
    versions = {}
    for package in ('Core', 'WorldCommon'):
        override = args.core_directory if package == 'Core' else args.world_directory
        directory = Path(override).resolve() if override else source / package / args.version
        if not directory.is_dir():
            raise SystemExit('Missing published package: ' + package)
        version_file = directory / f'{package}.version'
        if not version_file.is_file():
            raise SystemExit('Missing published version pointer: ' + package)
        version = version_file.read_text(encoding='utf-8-sig').strip()
        if not version or len(version) > 100 or any(c not in 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._' for c in version):
            raise SystemExit('Invalid published package version.')
        for name in (f'{package}.version', f'{package}_{version}.bytes', f'{package}_{version}.hash'):
            if not (directory / name).is_file():
                raise SystemExit('Missing package metadata: ' + name)
        if not override and version != args.version:
            raise SystemExit('Published version pointer mismatch.')
        directories[package] = directory
        versions[package] = version
        for file in directory.iterdir():
            if file.is_file() and not file.is_symlink() and file.suffix in ('.version', '.bytes', '.hash', '.bundle', '.rawfile'):
                files[package + '/' + file.name] = dict(sha256=digest(file), size=file.stat().st_size)
    release_id = hashlib.sha256(json.dumps(files, sort_keys=True).encode()).hexdigest()
    release = Path.home() / '.local/share/insectspace' / hashlib.sha256(str(root).encode()).hexdigest()[:16] / 'resources' / release_id
    release.mkdir(parents=True, exist_ok=True)
    for key in files:
        package, name = key.split('/')
        dest = release / key
        dest.parent.mkdir(exist_ok=True)
        if dest.exists():
            if dest.is_symlink() or digest(dest) != files[key]['sha256']:
                raise SystemExit('An immutable staged release has changed; inspect it before restarting.')
        else:
            temporary = dest.with_name(dest.name + '.staging')
            shutil.copyfile(directories[package] / name, temporary)
            temporary.replace(dest)
        dest.chmod(0o400)
    (release / 'files.json').write_text(json.dumps(files, indent=2))
    with (state_dir / 'host.log').open('w') as log:
        proc = subprocess.Popen([sys.executable, str(script), 'serve', '--root', str(root), '--source', str(release), '--port', str(args.port)], stdin=subprocess.DEVNULL, stdout=log, stderr=log, start_new_session=True, cwd=release)
    state = dict(pid=proc.pid, start=Path(f'/proc/{proc.pid}/stat').read_text().split()[21], port=args.port, release=str(release), releaseId=release_id, version=versions['Core'], versions=versions, source=str(source))
    state_file.write_text(json.dumps(state, indent=2))
    try:
        for attempt in range(50):
            try:
                health = json.load(urllib.request.urlopen(f'http://127.0.0.1:{args.port}/healthz', timeout=2))
                if health['processId'] == proc.pid:
                    break
            except (OSError, ValueError):
                pass
            if not owned(state):
                raise RuntimeError('Resource host exited; inspect local host.log.')
            time.sleep(.1)
        else:
            raise RuntimeError('Resource host did not become healthy.')
    except Exception:
        if owned(state):
            os.kill(proc.pid, signal.SIGTERM)
        state_file.unlink(missing_ok=True)
        raise
    print('WSL_RESOURCE_HOST_READY files=' + str(len(files)))
else:
    if not owned(state):
        raise SystemExit('Start the resource host before testing.')
    files = json.loads((Path(state['release']) / 'files.json').read_text())
    base = f"http://127.0.0.1:{state['port']}"
    for key, entry in files.items():
        data = urllib.request.urlopen(base + '/' + key + '?validation=1', timeout=5).read()
        if len(data) != entry['size'] or hashlib.sha256(data).hexdigest() != entry['sha256']:
            raise SystemExit('Remote bytes do not match the published release.')
    for path in ('/', '/Core/', '/Core/../../wsl.env', '/%2e%2e/%2e%2e/etc/passwd', '/files.json', '/Core/not-found.bundle'):
        try:
            urllib.request.urlopen(base + path, timeout=5)
            raise SystemExit('Unexpectedly exposed path: ' + path)
        except urllib.error.HTTPError as error:
            if error.code != 404:
                raise
    try:
        urllib.request.urlopen(urllib.request.Request(base + '/Core/Core.version', data=b'test', method='POST'), timeout=5)
        raise SystemExit('Host unexpectedly accepted a write.')
    except urllib.error.HTTPError as error:
        if error.code != 501:
            raise
    result = dict(passed=True, files=len(files), releaseId=state['releaseId'], version=state['version'], traversalRejected=True, writesRejected=True, executedAt=time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()))
    (state_dir / 'test-result.json').write_text(json.dumps(result, indent=2))
    print('WSL_RESOURCE_HOST_PASS files=' + str(len(files)))
