import argparse
import collections
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import UnityPy
from AgentScripts.AfkAddressables import Catalog

ROOT = Path(__file__).resolve().parents[1] / ".artifacts/reference/afk-journey"
UnityPy.config.FALLBACK_UNITY_VERSION = "2021.3.48f1"


def dependency_plan(keys):
    catalog = Catalog(ROOT / "samples/current-catalog-data.bin")
    lookup = {key: i for i, key in enumerate(catalog.keys) if isinstance(key, str)}
    selected, visited = set(), set()
    for key in keys:
        pending = list(catalog.buckets[lookup[key]])
        while pending:
            index = pending.pop()
            if index in visited:
                continue
            visited.add(index)
            location = catalog.location(index)
            if isinstance(location["key"], str) and location["key"].endswith(".bundle"):
                selected.add(location["key"])
            dependency = location["entry"][2]
            if dependency >= 0:
                pending.extend(catalog.buckets[dependency])
    manifest = json.loads((ROOT / "all/bundle-manifest.json").read_text(encoding="utf-8"))
    lookup = {item["bundle"]: item for item in manifest}
    missing = selected - lookup.keys()
    if missing:
        raise FileNotFoundError("Dependencies not downloaded: " + repr(sorted(missing)))
    return [lookup[bundle] for bundle in sorted(selected)]


def load(keys):
    plan = dependency_plan(keys)
    environment = UnityPy.Environment()
    for item in plan:
        environment.load_file(str(ROOT / "all/bundles" / item["bundle"]))
    return environment, plan


def components(go):
    for pair in go.m_Component:
        pointer = pair[1] if isinstance(pair, tuple) else pair.component
        if pointer.path_id:
            yield pointer.read()


def vec(v):
    return {axis: getattr(v, axis) for axis in ("x", "y", "z", "w") if hasattr(v, axis)}


def inspect(keys):
    environment, plan = load(keys)
    roots, meshes, materials, scripts = [], [], [], []
    for reader in environment.objects:
        if reader.type.name == "Transform":
            transform = reader.read()
            if not transform.m_Father.path_id:
                roots.append({"name": transform.m_GameObject.read().m_Name,
                              "position": vec(transform.m_LocalPosition), "children": len(transform.m_Children)})
        elif reader.type.name == "Mesh":
            obj = reader.read()
            meshes.append({"name": obj.m_Name, "vertices": obj.m_VertexData.m_VertexCount})
        elif reader.type.name == "Material":
            obj = reader.read()
            materials.append({"name": obj.m_Name, "textures": [prop for prop, _ in obj.m_SavedProperties.m_TexEnvs]})
        elif reader.type.name == "MonoBehaviour":
            try:
                obj = reader.read()
                scripts.append({"name": obj.m_Name, "class": obj.m_Script.read().m_ClassName,
                                "fields": list(reader.read_typetree())})
            except Exception as error:
                scripts.append({"error": str(error)[:150]})
    report = {"keys": keys, "bundles": len(plan), "bytes": sum(item["bytes"] for item in plan),
              "types": dict(collections.Counter(reader.type.name for reader in environment.objects)),
              "roots": roots, "meshes": meshes, "materials": materials, "scripts": scripts}
    path = ROOT / "scene-inspection"
    path.mkdir(exist_ok=True)
    file = path / (Path(keys[0]).stem + ".json")
    file.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({key: value for key, value in report.items() if key not in ("meshes", "materials", "scripts")}))
    print("Report: " + str(file))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("keys", nargs="+")
    inspect(parser.parse_args().keys)
