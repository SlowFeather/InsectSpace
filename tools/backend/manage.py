"""Native WSL2 process supervision for the explicit local backend topology."""
import argparse
import fcntl
import hashlib
import json
import os
from pathlib import Path
import signal
import shutil
import subprocess
import time
import urllib.request

parser = argparse.ArgumentParser()
parser.add_argument('action', choices=['start', 'stop', 'status'])
parser.add_argument('--root', required=True)
parser.add_argument('--environment', required=True)
args = parser.parse_args()
root = Path(args.root).resolve()
state_dir = root / '.artifacts/validation/backend'
state_dir.mkdir(parents=True, exist_ok=True)
state_file = state_dir / 'wsl-services.json'
lock = (state_dir / 'supervisor.lock').open('w')
fcntl.flock(lock, fcntl.LOCK_EX)
roles = ['gateway', 'identity', 'lobby', 'world', 'battle', 'worker']
publish_dir = root / '.artifacts/backend-linux'
install_dir = Path.home() / '.local/share/insectspace' / hashlib.sha256(str(root).encode()).hexdigest()[:16] / 'backend'
binary = install_dir / 'InsectSpace.BackendHost'

def owned(entry):
    try:
        pid = entry['pid']
        command = Path(f'/proc/{pid}/cmdline').read_bytes().split(b'\0')
        start = Path(f'/proc/{pid}/stat').read_text().split()[21]
        return command[0].decode() == str(binary) and start == entry['start']
    except (FileNotFoundError, ProcessLookupError, PermissionError, IndexError):
        return False

entries = json.loads(state_file.read_text()) if state_file.exists() else []
if args.action == 'status':
    print(json.dumps([dict(role=e['role'], pid=e['pid'], running=owned(e)) for e in entries]))
elif args.action == 'stop':
    for entry in entries:
        if owned(entry):
            os.kill(entry['pid'], signal.SIGTERM)
    deadline = time.monotonic() + 10
    while any(owned(e) for e in entries) and time.monotonic() < deadline:
        time.sleep(.1)
    for entry in entries:
        if owned(entry):
            os.kill(entry['pid'], signal.SIGKILL)
    state_file.unlink(missing_ok=True)
    print('WSL_BACKEND_STOPPED')
else:
    if any(owned(e) for e in entries):
        raise SystemExit('Owned WSL services are already running; stop before starting.')
    if not (publish_dir / 'InsectSpace.BackendHost').is_file():
        raise SystemExit('Publish the Linux host before starting services.')
    # Load the .NET runtime from native ext4 instead of the Windows /mnt mount.
    shutil.copytree(publish_dir, install_dir, dirs_exist_ok=True)
    environment = os.environ.copy()
    for line in Path(args.environment).read_text(encoding='utf-8-sig').splitlines():
        if line and not line.startswith('#'):
            key, value = line.split('=', 1)
            if key.startswith('INSECTSPACE_'):
                environment[key] = value
    environment['INSECTSPACE_BIND_ADDRESS'] = '127.0.0.1'
    environment['DOTNET_ENVIRONMENT'] = 'Development'
    logs = state_dir / 'services'
    logs.mkdir(exist_ok=True)
    binary.chmod(0o700)
    entries = []
    try:
        for role in roles:
            with (logs / f'{role}.out.log').open('w') as out, (logs / f'{role}.err.log').open('w') as err:
                p = subprocess.Popen([str(binary), '--service', role], cwd=install_dir, env=environment, stdin=subprocess.DEVNULL, stdout=out, stderr=err, start_new_session=True)
            entries.append(dict(role=role, pid=p.pid, start=Path(f'/proc/{p.pid}/stat').read_text().split()[21]))
            state_file.write_text(json.dumps(entries, indent=2))
        for index, entry in enumerate(entries):
            deadline = time.monotonic() + 90
            while True:
                try:
                    with urllib.request.urlopen(f'http://127.0.0.1:{8080+index}/healthz', timeout=2) as response:
                        result = json.load(response)
                    if result['mySql'] and result['redis'] and result['processId'] == entry['pid'] and 'Linux' in result['operatingSystem']:
                        break
                except Exception:
                    pass
                if not owned(entry) or time.monotonic() >= deadline:
                    raise RuntimeError('Service ' + entry['role'] + ' failed Linux health check; inspect local service logs.')
                time.sleep(.2)
        print('WSL_BACKEND_READY: six Linux processes, MySQL and Redis healthy')
    except Exception:
        for entry in entries:
            if owned(entry):
                os.kill(entry['pid'], signal.SIGTERM)
        state_file.unlink(missing_ok=True)
        raise
