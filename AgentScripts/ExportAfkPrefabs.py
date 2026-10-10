import argparse
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from UnityPy.helpers.MeshHelper import MeshHandler
from UnityPy.classes import PPtr
from AgentScripts.AfkReferenceResources import ROOT, components, load, vec

OUT = Path(__file__).resolve().parents[1] / "client/unity/InsectSpaceClient/Assets/Temp/AFKStudy/Environment/Source"


def identity(reader):
    return Path(reader.assets_file.name).name + "_" + str(reader.path_id)


def export(keys, output=None, streamed_meshes=False):
    global OUT
    if output is not None:
        OUT = Path(output)
    environment, plan = load(keys)
    OUT.mkdir(parents=True, exist_ok=True)
    textures, meshes, materials, shaders, nodes = {}, {}, {}, {}, []
    diagnostics = []
    streamed = {}
    if streamed_meshes:
        addresses = set()
        for reader in environment.objects:
            if reader.type.name != "MonoBehaviour":
                continue
            data = reader.read_typetree()
            addresses.update(info["meshAddress"] for info in data.get("renderInfoList", [])
                             if info.get("meshAddress"))
        if addresses:
            mesh_environment, mesh_plan = load(sorted(addresses))
            plan = list({item["bundle"]: item for item in plan + mesh_plan}.values())
            for address in addresses:
                candidates = [reader for reader in mesh_environment.objects
                              if reader.type.name == "Mesh" and reader.read().m_Name == Path(address).stem]
                if len(candidates) != 1:
                    raise ValueError("Expected one streamed mesh for " + address)
                streamed[address] = candidates[0]

    def texture(pointer):
        if not pointer.path_id:
            return ""
        reader = pointer.deref()
        key = identity(reader)
        if key not in textures:
            obj = reader.read()
            if not obj.m_Width or not obj.m_Height:
                return ""
            try:
                obj.image.save(OUT / (key + ".png"))
            except ValueError:
                # Preserve HDR data even when UnityPy's PNG converter cannot
                # represent negative or above-one half-float channel values.
                if obj.m_TextureFormat != 17:
                    raise
                import struct
                from PIL import Image
                raw = obj.get_image_data()
                (OUT / (key + ".half.bytes")).write_bytes(raw)
                pixels = bytes(int(max(0, min(1, v[0])) * 255) for v in struct.iter_unpack('<e', raw))
                Image.frombytes('RGBA', (obj.m_Width,obj.m_Height), pixels).transpose(Image.Transpose.FLIP_TOP_BOTTOM).save(OUT / (key + ".png"))
                diagnostics.append({"kind": "hdr_preview_clamped", "texture": key, "raw_preserved": True})
            textures[key] = {"id": key, "name": obj.m_Name, "width": obj.m_Width, "height": obj.m_Height,
                             "sRGB": obj.m_ColorSpace == 1}
        return key

    def mesh(pointer):
        if not pointer.path_id:
            return ""
        reader = pointer.deref()
        return mesh_reader(reader)

    def mesh_reader(reader):
        key = identity(reader)
        if key not in meshes:
            obj = reader.read()
            handler = MeshHandler(obj)
            handler.process()
            if not handler.m_Vertices:
                return ""
            def vectors(values, dimensions):
                return [{axis: float(value[index]) for index, axis in enumerate("xyzw"[:dimensions])} for value in values]
            colors = handler.m_Colors or []
            data = {"name": obj.m_Name, "vertices": vectors(handler.m_Vertices, 3),
                    "normals": vectors(handler.m_Normals or [], 3), "uv": vectors(handler.m_UV0 or [], 2),
                    "tangents": vectors(handler.m_Tangents or [], 4),
                    "colors": [{axis: float(value[index]) for index, axis in enumerate("rgba")} for value in colors],
                    "submeshes": [{"indices": [int(i) for triangle in sub for i in triangle]} for sub in handler.get_triangles()]}
            (OUT / (key + ".json")).write_text(json.dumps(data), encoding="utf-8")
            meshes[key] = {"id": key, "name": obj.m_Name, "vertices": len(handler.m_Vertices),
                           "triangles": sum(len(sub["indices"]) // 3 for sub in data["submeshes"])}
        return key

    def lod_mesh(values, assetsfile):
        """Return the first non-empty mesh from a custom render LOD list."""
        for value in values or []:
            if not value or not value.get("m_PathID", value.get("path_id", 0)):
                continue
            try:
                candidate = PPtr(**value, assetsfile=assetsfile)
                result = mesh(candidate)
                if result:
                    return result
            except Exception as error:
                diagnostics.append({"kind": "mesh_lod", "error": str(error)[:240]})
        return ""

    def material(pointer):
        if not pointer.path_id:
            diagnostics.append({"kind": "empty_material_slot"})
            return ""
        reader = pointer.deref()
        key = identity(reader)
        if key not in materials:
            obj = reader.read()
            shader = obj.m_Shader.read()
            shader_name = getattr(shader, "m_Name", "") or getattr(getattr(shader, "m_ParsedForm", None), "m_Name", "")
            shader_id = identity(obj.m_Shader.deref())
            if shader_id not in shaders:
                try:
                    shader_text = shader.export()
                    (OUT / (shader_id + ".shader.txt")).write_text(shader_text, encoding="utf-8")
                    shader_error = ""
                except Exception as error:
                    shader_error = str(error)[:240]
                shaders[shader_id] = {"id": shader_id, "name": shader_name, "exported": not shader_error, "error": shader_error}
            refs = [{"property": prop, "id": texture(ref.m_Texture), "scale": vec(ref.m_Scale),
                     "offset": vec(ref.m_Offset)} for prop, ref in obj.m_SavedProperties.m_TexEnvs]
            colors = [{"property": prop, "value": {axis: getattr(value, axis) for axis in "rgba"}}
                      for prop, value in obj.m_SavedProperties.m_Colors]
            floats = [{"property": prop, "value": float(value)} for prop, value in obj.m_SavedProperties.m_Floats]
            materials[key] = {"id": key, "name": obj.m_Name, "shader": shader_name, "shader_id": shader_id,
                              "textures": refs, "colors": colors, "floats": floats}
        return key

    for reader in environment.objects:
        if reader.type.name != "Transform":
            continue
        transform = reader.read()
        go = transform.m_GameObject.read()
        node = {"id": identity(reader), "parent": identity(transform.m_Father.deref()) if transform.m_Father.path_id else "",
                "name": go.m_Name, "active": bool(go.m_IsActive), "position": vec(transform.m_LocalPosition),
                "rotation": vec(transform.m_LocalRotation), "scale": vec(transform.m_LocalScale),
                "mesh": "", "materials": [], "enabled": True}
        for component in components(go):
            kind = type(component).__name__
            if kind == "MeshFilter":
                node["mesh"] = mesh(component.m_Mesh)
            elif kind == "MeshRenderer":
                node["materials"] = [material(pointer) for pointer in component.m_Materials]
                node["enabled"] = bool(component.m_Enabled)
            elif kind == "MonoBehaviour" and component.m_Script.read().m_ClassName == "ForegroundRenderInfoSet":
                data = component.object_reader.read_typetree()
                def pointer(value):
                    return PPtr(**value, assetsfile=component.object_reader.assets_file)
                for index, info in enumerate(data["renderInfoList"]):
                    address = info.get("meshAddress", "")
                    mesh_id = mesh_reader(streamed[address]) if address in streamed else lod_mesh(info.get("meshLod"), component.object_reader.assets_file)
                    child = {"id": node["id"] + "_render_" + str(index), "parent": node["id"],
                             "name": go.m_Name + "_render_" + str(index), "active": True,
                             "position": {"x": 0, "y": 0, "z": 0}, "rotation": {"x": 0, "y": 0, "z": 0, "w": 1},
                             "scale": {"x": 1, "y": 1, "z": 1},
                             "matrix": [info["matrix"]["e" + str(row) + str(column)] for row in range(4) for column in range(4)],
                             "mesh": mesh_id, "mesh_address": address,
                             "streamed_lod0": address in streamed,
                             "materials": [material(pointer(info["mat"]))],
                             "enabled": True, "shadow": bool(info["shadowCasting"])}
                    nodes.append(child)
        nodes.append(node)
    report = {"keys": keys, "source_bundles": [item["bundle"] for item in plan], "nodes": nodes,
              "meshes": list(meshes.values()), "materials": list(materials.values()), "textures": list(textures.values()),
              "shaders": list(shaders.values()), "diagnostics": diagnostics}
    (OUT / "prefabs.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({"keys": len(keys), "nodes": len(nodes), "meshes": len(meshes), "materials": len(materials),
                      "textures": len(textures), "triangles": sum(item["triangles"] for item in meshes.values())}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("keys", nargs="*")
    parser.add_argument("--selection", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--streamed-meshes", action="store_true")
    args = parser.parse_args()
    export(json.loads(args.selection.read_text(encoding="utf-8")) if args.selection else args.keys, args.output, args.streamed_meshes)
