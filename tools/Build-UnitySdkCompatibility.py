"""Build complete, deterministic Unity 6 SDK packages without changing source archives or DLLs."""
import argparse
import gzip
import hashlib
import io
import json
from pathlib import Path
import tarfile

ROOT = Path(__file__).resolve().parents[1]


def digest(data):
    return hashlib.sha256(data).hexdigest()


def read_package(archive, prefix):
    files = {}
    with tarfile.open(archive, 'r:gz') as package:
        for entry in package.getmembers():
            name = entry.name.removeprefix('./')
            if entry.isfile() and name.startswith(prefix):
                files['package/' + name[len(prefix):]] = package.extractfile(entry).read()
    if 'package/package.json' not in files:
        raise ValueError('No complete UPM package in source archive')
    return files


def write_package(destination, files):
    buffer = io.BytesIO()
    with gzip.GzipFile(fileobj=buffer, mode='wb', mtime=0) as compressed:
        with tarfile.open(fileobj=compressed, mode='w') as package:
            for name, data in sorted(files.items()):
                item = tarfile.TarInfo(name)
                item.size = len(data)
                item.mode = 0o644
                package.addfile(item, io.BytesIO(data))
    content = buffer.getvalue()
    if destination.exists() and destination.read_bytes() != content:
        raise ValueError('Versioned SDK release is immutable: ' + str(destination))
    destination.write_bytes(content)
    return digest(content)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--yooasset-source', required=True, type=Path)
    parser.add_argument('--yooasset-commit', required=True)
    args = parser.parse_args()
    lock_path = ROOT / 'vendor/dependencies.lock.json'
    lock = json.loads(lock_path.read_text(encoding='utf-8-sig'))
    original = ROOT / 'vendor/upm/com.framework.unity-1.0.0-alpha.7.tgz'
    expected = next(item['sha256'] for item in lock['files'] if item['path'] == original.relative_to(ROOT).as_posix())
    if digest(original.read_bytes()) != expected:
        raise ValueError('Original GF release checksum mismatch')
    gf = read_package(original, 'package/')
    binary_hashes = {name: digest(data) for name, data in gf.items() if name.endswith('.dll')}
    debugger = 'package/Runtime/UnityFramework/Debugger/DebuggerComponent.cs'
    source = gf[debugger].decode('utf-8-sig')
    if source.count('GUILayout.Window(GetInstanceID(),') != 2:
        raise ValueError('Unexpected debugger source; review this release before adapting it')
    source = source.replace('GUILayout.Window(GetInstanceID(),', 'GUILayout.Window(GetDebuggerWindowId(),')
    anchor = '        private void OnGUI()'
    helper = '''        private int GetDebuggerWindowId()
        {
#if UNITY_6000_6_OR_NEWER
            return GetEntityId().GetHashCode();
#else
            return GetInstanceID();
#endif
        }

'''
    if source.count(anchor) != 1:
        raise ValueError('Unexpected OnGUI method')
    gf[debugger] = source.replace(anchor, helper + anchor).encode('utf-8')
    redirect = 'package/Editor/EditorLogRedirect.cs'
    editor_source = gf[redirect].decode('utf-8-sig')
    if editor_source.count('script.GetInstanceID()') != 1:
        raise ValueError('Unexpected editor log redirect source')
    editor_source = '#if UNITY_6000_6_OR_NEWER\nusing FrameworkObjectId = UnityEngine.EntityId;\n#else\nusing FrameworkObjectId = System.Int32;\n#endif\n' + editor_source
    editor_source = editor_source.replace('HashSet<int>', 'HashSet<FrameworkObjectId>')
    editor_source = editor_source.replace('OnOpenAsset(int instanceId, int line)', 'OnOpenAsset(FrameworkObjectId instanceId, int line)')
    editor_source = editor_source.replace('                    s_WrapperScriptIds.Add(script.GetInstanceID());',
        '#if UNITY_6000_6_OR_NEWER\n                    s_WrapperScriptIds.Add(script.GetEntityId());\n#else\n                    s_WrapperScriptIds.Add(script.GetInstanceID());\n#endif')
    gf[redirect] = editor_source.encode('utf-8')
    version = '1.0.0-alpha.7.insectspace.unity66.2'
    manifest = json.loads(gf['package/package.json'])
    manifest['version'] = version
    gf['package/package.json'] = (json.dumps(manifest, ensure_ascii=False, indent=2) + '\n').encode()
    if binary_hashes != {name: digest(data) for name, data in gf.items() if name.endswith('.dll')}:
        raise ValueError('Protected GF binary changed')
    gf_output = ROOT / f'vendor/upm/com.framework.unity-{version}.tgz'
    gf_hash = write_package(gf_output, gf)
    with tarfile.open(args.yooasset_source, 'r:gz') as archive:
        prefix = next(item.name[:-len('package.json')] for item in archive.getmembers()
                      if item.name.endswith('/Assets/YooAsset/package.json'))
    yoo = read_package(args.yooasset_source, prefix)
    if json.loads(yoo['package/package.json'])['version'] != '3.0.6':
        raise ValueError('Expected the full upstream YooAsset 3.0.6 release')
    yoo_output = ROOT / 'vendor/upm/com.tuyoogame.yooasset-3.0.6.tgz'
    yoo_hash = write_package(yoo_output, yoo)
    releases = [
        {'package': 'com.framework.unity', 'version': version, 'sha256': gf_hash,
         'sourceSha256': expected, 'source': original.relative_to(ROOT).as_posix(),
         'changes': ['Debugger GUI identifiers and editor log redirect callbacks use EntityId on Unity 6.6.'],
         'unchangedDllHashes': binary_hashes},
        {'package': 'com.tuyoogame.yooasset', 'version': '3.0.6', 'sha256': yoo_hash,
         'sourceCommit': args.yooasset_commit, 'sourceArchiveSha256': digest(args.yooasset_source.read_bytes()),
         'source': 'https://github.com/tuyoogame/YooAsset', 'changes': ['Complete unmodified Assets/YooAsset UPM release.']}
    ]
    for output, sha in [(gf_output, gf_hash), (yoo_output, yoo_hash)]:
        relative = output.relative_to(ROOT).as_posix()
        existing = next((item for item in lock['files'] if item['path'] == relative), None)
        if existing and existing['sha256'] != sha:
            raise ValueError('Lock would replace an immutable release')
        if not existing:
            lock['files'].append({'path': relative, 'sha256': sha})
    lock['source'] = 'Pinned original SDK releases, documented compatibility releases, and official platform SDK snapshots'
    lock_path.write_text(json.dumps(lock, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    (ROOT / 'vendor/unity6-sdk-release.json').write_text(json.dumps({
        'release': 'insectspace-unity66-2', 'reviewStatus': 'platform-review-required',
        'packages': releases, 'coreAndServerDlls': 'Unchanged; see dependencies.lock.json'
    }, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('Created complete Unity 6 package releases; original archives and core DLLs unchanged.')


if __name__ == '__main__':
    main()
