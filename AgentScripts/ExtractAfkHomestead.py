"""Recover local homestead instances and baked terrain from the reference archive."""
import argparse
import collections
import json
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[1]
REF = ROOT / ".artifacts/reference/afk-journey"
OUT = ROOT / "client/unity/InsectSpaceClient/Assets/Temp/AFKStudy/Homestead/Source"
sys.path.insert(0, str(ROOT))
from AgentScripts.InspectAfkMap import Buffer
from AgentScripts.AfkReferenceResources import load, components, vec
from AgentScripts.ExportAfkPrefabs import export
import UnityPy
from PIL import Image, ImageDraw


def definitions(name, field):
    b = Buffer(REF / "map-definitions" / name)
    v = b.target(b.fields(b.number(0))[field])
    result = {}
    for i in range(b.number(v)):
        f = b.fields(b.target(v + 4 + i * 4))
        p = b.target(f[1]) if len(f) > 1 and f[1] else None
        address = b.data[p + 4:p + 4 + b.number(p)].decode() if p else ""
        result[b.number(f[0], "H") if f[0] else 0] = address
    return result


def indexed_instances(mapname, kind, field, defs):
    folder = REF / mapname
    path = folder / ("Render" + kind + "Buffer.mapdata")
    if not path.exists():
        return []
    data = path.read_bytes()
    b = Buffer(folder / (mapname + ".indexinfo"))
    v = b.target(b.fields(b.number(0))[0])
    result = []
    for i in range(b.number(v)):
        fields = b.fields(b.target(v + 4 + i * 4))
        if len(fields) <= field or not fields[field]:
            continue
        q = b.target(fields[field])
        for j in range(b.number(q)):
            identifier, start, length = struct.unpack_from("<H2xII", b.data, q + 4 + j * 12)
            for k in range(start, start + length):
                matrix = struct.unpack_from("<16f", data, k * 64)
                result.append(dict(id=identifier, key=defs.get(identifier, ""), matrix=matrix, kind=kind))
    return result


def layout():
    fg = definitions("map_foreground_hd.def", 1)
    terrain = definitions("map_terrain_hd.def", 0)
    grass = fg
    records = indexed_instances("homestead_01", "Foreground", 3, grass)
    data = (REF / "homestead_01/DrawMeshRendererDataExpandArray.mapdata").read_bytes()
    for p in range(0, len(data), 100):
        identifier = struct.unpack_from("<H", data, p)[0]
        records.append(dict(id=identifier, key=fg.get(identifier, ""),
                            matrix=struct.unpack_from("<16f", data, p + 4), kind="Renderer"))
    data = (REF / "homestead_01/DrawMeshFodMatrixArray.mapdata").read_bytes()
    ids = (REF / "homestead_01/DrawMeshFodIdArray.mapdata").read_bytes()
    for i in range(len(ids) // 2):
        identifier = struct.unpack_from("<H", ids, i * 2)[0]
        records.append(dict(id=identifier, key=fg.get(identifier, ""),
                            matrix=struct.unpack_from("<16f", data, i * 64), kind="Fod"))
    records += indexed_instances("homestead_01", "Terrain", 1, terrain)
    records += indexed_instances("homestead_01", "Grass", 2, grass)
    unique = {}
    for item in records:
        unique.setdefault((item["id"], tuple(round(v, 4) for v in item["matrix"])), item)
    records = list(unique.values())
    # Recover the complete map: its east sector uses the green broadleaf cliffs.
    # Restricting this to the westfall sector silently selected the autumn biome.
    selected = records
    catalog = json.loads((REF / "all/bundle-manifest.json").read_text())
    from AgentScripts.AfkAddressables import Catalog
    available = set(x for x in Catalog(REF / "samples/current-catalog-data.bin").keys if isinstance(x, str))
    for item in selected:
        if item["key"] not in available and item["key"].endswith(".prefab"):
            candidate = item["key"].replace(".prefab", "_hd.prefab")
            if candidate in available:
                item["key"] = candidate
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "layout.json").write_text(json.dumps(dict(map="homestead_01", instances=selected), indent=2))
    keys = sorted(set(x["key"] for x in selected if x["key"] in available))
    (OUT / "selection.json").write_text(json.dumps(keys, indent=2))
    print(json.dumps(dict(total=len(records), selected=len(selected), keys=len(keys),
                         kinds=dict(collections.Counter(x["kind"] for x in selected)),
                         missing=sorted(set(x["key"] for x in selected if x["key"] not in available)))))


def virtual_texture(mapname):
    index = json.loads((REF / "all/non-unity/VT/vt.json").read_text())
    folder = OUT / "VT" / mapname
    folder.mkdir(parents=True, exist_ok=True)
    pages, metadata, missing = [], [], []
    for record in index["ABInfoLst"]:
        bundle = record["abNameOfHash"]
        if not bundle.startswith(("VT/" + mapname + "/", "LDRes/VT/" + mapname + "/")):
            continue
        path = REF / "all/bundles" / bundle
        if not path.exists():
            missing.append(bundle)
            continue
        env = UnityPy.load(str(path))
        names = dict(zip(map(str, record["abSelfHashes"]), record["assetPaths"]))
        for key, pointer in env.container.items():
            semantic = names[key].replace("\\", "/")
            relative = Path(semantic.split("/" + mapname + "/", 1)[0]).name + "/" + semantic.rsplit("/", 1)[-1]
            target = folder / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            reader = pointer.deref()
            if reader.type.name == "Texture2D":
                obj = reader.read()
                obj.image.save(target)
                coordinates = target.stem.split("_")
                if len(coordinates) != 3 or not all(v.isdigit() for v in coordinates):
                    continue
                x, y, mip = map(int, coordinates)
                pages.append(dict(file=relative, x=x, y=y, mip=mip, width=obj.m_Width, height=obj.m_Height))
            elif reader.type.name == "MonoBehaviour":
                data = reader.read_typetree()
                target.with_suffix(".json").write_text(json.dumps(data, indent=2))
                metadata.append(data)
    for kind in ("small_diffuse", "diffuse", "shadow", "normal"):
        tiles = sorted((p for p in pages if p["file"].startswith(kind + "/")), key=lambda p: -p["mip"])
        if not tiles:
            continue
        size, count = 8192, 64
        overview = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        for page in tiles:
            span = 2 ** page["mip"]
            tile = Image.open(folder / page["file"]).convert("RGBA").resize((size * span // count,) * 2, Image.Resampling.BILINEAR)
            overview.paste(tile, (page["x"] * size // count, size - (page["y"] + span) * size // count))
        overview.save(folder / (kind + "-overview.png"))
        crop = overview.crop((0, size - 16 * size // count, 12 * size // count, size))
        crop.save(folder / (kind + "-world.png"))
        crop.resize((1152, 1536)).save(REF / (mapname + "-" + kind + ".png"))
    (folder / "index.json").write_text(json.dumps(dict(map=mapname, pages=pages, metadata=metadata, missing=missing), indent=2))
    print(json.dumps(dict(map=mapname, pages=len(pages), missingCount=len(missing),
                         missingExamples=missing[:5], channels=dict(collections.Counter(p["file"].split("/")[0] for p in pages)))))


def environment():
    env, plan = load(["scene/map/hraid/homestead_01.unity"])
    result = []
    for reader in env.objects:
        if reader.type.name == "MonoBehaviour":
            obj = reader.read()
            kind = obj.m_Script.read().m_ClassName
            result.append(dict(type=kind, data=reader.read_typetree()))
            if kind == "ColorLookup":
                texture = obj.texture.m_Value.read()
                texture.image.save(OUT / "ColorLookup.png")
                print("Recovered LUT:", texture.m_Name, texture.m_Width, texture.m_Height)
        elif reader.type.name == "Light":
            result.append(dict(type="Light", data=reader.read_typetree()))
        elif reader.type.name == "RenderSettings":
            result.append(dict(type="RenderSettings", data=reader.read_typetree()))
    (OUT / "environment.json").write_text(json.dumps(result, indent=2))
    print("Environment components:", len(result))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=["layout", "vt", "prefabs", "environment"])
    parser.add_argument("--map", default="homestead_01")
    args = parser.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    if args.mode == "layout":
        layout()
    elif args.mode == "vt":
        virtual_texture(args.map)
    elif args.mode == "prefabs":
        export(json.loads((OUT / "selection.json").read_text()), OUT / "Prefabs")
    else:
        environment()
