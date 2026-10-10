"""Stage the downloaded CC-BY Valerya GLB as an OBJ using the standard library.
--mixamo preserves the source T-pose for automatic rigging. The default is a static
relaxed-arm study. --unity-albedos copies textures into the existing Unity study.
Staged conversion manifests never replace final rig/animation attribution.
"""
import hashlib
import json
import math
import pathlib
import struct
import sys
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
SOURCE = ROOT / '.artifacts/reference/valerya/source/valerya.glb'
ASSET_DIR = ROOT / 'client/unity/InsectSpaceClient/Assets/InsectSpace/Rendering/HeroStudy/Valerya'
DEST = ROOT / '.artifacts/reference/valerya/static-study'
MIXAMO = '--mixamo' in sys.argv
if MIXAMO:
    DEST = ROOT / '.artifacts/reference/valerya/mixamo-upload'
data = SOURCE.read_bytes()
assert data[:4] == b'glTF' and struct.unpack_from('<I', data, 4)[0] == 2
size = struct.unpack_from('<I', data, 12)[0]
doc = json.loads(data[20:20 + size])
binary = data[28 + size:]
assert not doc.get('skins') and not doc.get('animations'), 'Recheck conversion for a rigged source.'
DEST.mkdir(parents=True, exist_ok=True)

def read_accessor(index):
    a = doc['accessors'][index]
    assert 'sparse' not in a
    view = doc['bufferViews'][a['bufferView']]
    kind = {5126: 'f', 5125: 'I', 5123: 'H', 5121: 'B'}[a['componentType']]
    count = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3}[a['type']]
    fmt = '<' + kind * count
    stride = view.get('byteStride', struct.calcsize(fmt))
    offset = view.get('byteOffset', 0) + a.get('byteOffset', 0)
    return [struct.unpack_from(fmt, binary, offset + i * stride) for i in range(a['count'])]

def smooth(a, b, x):
    t = min(1, max(0, (x-a)/(b-a)))
    return t*t*(3-2*t)

def pose(v, part):
    if MIXAMO:
        return v
    x, y, z = v
    if part not in ('body', 'th3', 'th1') or y < 1.31:
        return v
    sign = 1 if x > 0 else -1
    weight = smooth(.185, .305, abs(x)) * smooth(1.31, 1.40, y)
    # A restrained relaxed-arm pose, baked for this art review only.
    angle = -sign * math.radians(65 if part != 'th1' else 38) * weight
    dx, dy = x-sign*.205, y-1.49
    return (sign*.205+math.cos(angle)*dx-math.sin(angle)*dy,
            1.49+math.sin(angle)*dx+math.cos(angle)*dy, z)

materials = []
texture_files = []
for i, material in enumerate(doc['materials']):
    tex = material['pbrMetallicRoughness']['baseColorTexture']['index']
    img = doc['images'][doc['textures'][tex]['source']]
    view = doc['bufferViews'][img['bufferView']]
    ext = '.png' if img['mimeType'] == 'image/png' else '.jpg'
    filename = 'albedo_%02d%s' % (i, ext)
    start = view.get('byteOffset', 0)
    (DEST / filename).write_bytes(binary[start:start+view['byteLength']])
    texture_files.append(filename)
    if '--unity-albedos' in sys.argv:
        ASSET_DIR.mkdir(parents=True, exist_ok=True)
        (ASSET_DIR / filename).write_bytes(binary[start:start+view['byteLength']])
    materials.append('newmtl Surface_%d\nKd 1 1 1\nmap_Kd %s\n' % (i, filename))
(DEST / 'Valerya.mtl').write_text('\n'.join(materials), encoding='utf-8')

pose_name = 'unrigged T-pose for Mixamo' if MIXAMO else 'relaxed-arm static study'
lines = ['# Valerya by agra_aoe / AoG.01, CC BY 4.0; '+pose_name, 'mtllib Valerya.mtl']
offset = 1
triangles = 0
for node in doc['nodes']:
    assert not any(k in node for k in ('matrix', 'translation', 'rotation', 'scale'))
    part = node['name']
    for prim in doc['meshes'][node['mesh']]['primitives']:
        assert prim.get('mode', 4) == 4
        points = [pose(p, part) for p in read_accessor(prim['attributes']['POSITION'])]
        uv = read_accessor(prim['attributes']['TEXCOORD_0'])
        indices = [v[0] for v in read_accessor(prim['indices'])]
        lines += ['o '+part, 'usemtl Surface_%d' % prim['material'], 's 1']
        lines += ['v %.7f %.7f %.7f' % p for p in points]
        lines += ['vt %.7f %.7f' % (u, 1-v) for u, v in uv]
        for k in range(0, len(indices), 3):
            face = [offset + i for i in indices[k:k+3]]
            lines.append('f '+' '.join('%d/%d' % (i, i) for i in face))
        triangles += len(indices)//3
        offset += len(points)
obj_name = 'Valerya_TPose.obj' if MIXAMO else 'Valerya_Relaxed.obj'
(DEST / obj_name).write_text('\n'.join(lines)+'\n', encoding='utf-8')
info = {'title': 'Valerya - Fantasy Queen | Stylized 3D Character', 'author': 'agra_aoe (AoG.01)',
    'source': 'https://sketchfab.com/3d-models/valerya-fantasy-queen-stylized-3d-character-271fa806eeac44d5a0c03101d7ce2096',
    'license': 'CC BY 4.0', 'license_url': 'https://creativecommons.org/licenses/by/4.0/',
    'download_date': '2026-10-02', 'source_sha256': hashlib.sha256(data).hexdigest(),
    'triangles': triangles, 'original_vertices': offset-1, 'source_has_rig': False,
    'modifications': 'GLB to OBJ; '+pose_name+'; extracted original albedo textures.',
    'scope': 'Intermediate conversion only; final Unity FBX provenance lives in the Unity Valerya/ATTRIBUTION.json.',
    'limitations': 'This OBJ has no skeletal animation. Author discloses AI-assisted workflow.'}
(DEST / 'ATTRIBUTION.json').write_text(json.dumps(info, indent=2)+'\n', encoding='utf-8')
print(json.dumps(info, indent=2))
if MIXAMO:
    archive = DEST.parent / 'Valerya_Mixamo.zip'
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
        for name in [obj_name, 'Valerya.mtl'] + texture_files:
            z.write(DEST / name, name)
    print(archive)
