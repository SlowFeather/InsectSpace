"""Export original water meshes, textures, material values and GPU programs locally."""
import json
import argparse
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[1]
REF = ROOT / ".artifacts/reference/afk-journey"
OUT = ROOT / "client/unity/InsectSpaceClient/Assets/Temp/AFKStudy/Homestead/Source/Water"
sys.path.insert(0, str(ROOT))
from AgentScripts.AfkReferenceResources import load, vec
from UnityPy.helpers.MeshHelper import MeshHandler
from AgentScripts.InspectAfkMap import Buffer
from AgentScripts.ExtractAfkRenderReference import export_shader
from UnityPy.export.Texture2DConverter import parse_image_data


def reflection():
    env, plan = load(["map/prefab/water/watercoast/material/water/mt_water_homestead.mat"])
    folder = OUT / "Reflection"
    folder.mkdir(parents=True, exist_ok=True)
    records = []
    for reader in env.objects:
        if reader.type.name != "Cubemap":
            continue
        obj = reader.read()
        raw = obj.get_image_data()
        if obj.m_ImageCount != 6 or len(raw) != obj.m_CompleteImageSize * 6:
            raise ValueError("Unexpected cubemap face layout: " + obj.m_Name)
        faces = []
        for index, face in enumerate(("PositiveX", "NegativeX", "PositiveY", "NegativeY", "PositiveZ", "NegativeZ")):
            name = obj.m_Name + "-" + face + ".png"
            start = index * obj.m_CompleteImageSize
            image = parse_image_data(raw[start:start + obj.m_CompleteImageSize], obj.m_Width, obj.m_Height,
                                     obj.m_TextureFormat, reader.version, reader.platform,
                                     getattr(obj, "m_PlatformBlob", None), True)
            image.save(folder / name)
            faces.append(dict(face=face, file=name))
        records.append(dict(name=obj.m_Name, size=obj.m_Width, faces=faces))
    if not records:
        raise ValueError("Water material dependencies contained no cubemap")
    (folder / "cubemaps.json").write_text(json.dumps(dict(cubemaps=records,
        source_bundles=[p["bundle"] for p in plan]), indent=2), encoding="utf-8")
    print(json.dumps(records))


def main():
    base = "map/prefab/water/watercoast/"
    keys = [base + address for address in (
        "mesh/hex_lowpoly.mesh", "mesh/water_hexagonmesh.mesh",
        "material/water/mt_water_homestead.mat", "material/shore/mt_shore_default.mat",
        "texture/homestead_01/homestead_01_watertex.png")]
    env, plan = load(keys)
    OUT.mkdir(parents=True, exist_ok=True)
    meshes, materials, textures, shaders = [], [], [], []
    for reader in env.objects:
        if reader.type.name not in ("Mesh", "Material", "Texture2D"):
            continue
        obj = reader.read()
        if reader.type.name == "Mesh":
            h = MeshHandler(obj)
            h.process()
            def vectors(values, axes):
                return [dict(zip(axes, map(float, value))) for value in values or []]
            data = dict(name=obj.m_Name, vertices=vectors(h.m_Vertices,"xyz"),
                        normals=vectors(h.m_Normals,"xyz"), uv=vectors(h.m_UV0,"xy"),
                        uv1=vectors(h.m_UV1,"xy"), tangents=vectors(h.m_Tangents,"xyzw"),
                        colors=vectors(h.m_Colors,"rgba"),
                        submeshes=[dict(indices=[int(i) for tri in sub for i in tri]) for sub in h.get_triangles()])
            (OUT / (obj.m_Name + ".json")).write_text(json.dumps(data), encoding="utf-8")
            meshes.append(dict(name=obj.m_Name, vertices=len(data["vertices"]),
                               bounds=dict(center=vec(obj.m_LocalAABB.m_Center),extent=vec(obj.m_LocalAABB.m_Extent))))
        elif reader.type.name == "Texture2D":
            if obj.m_TextureFormat == 17:
                raw = obj.get_image_data()[:obj.m_Width * obj.m_Height * 8]
                (OUT / (obj.m_Name + ".half.bytes")).write_bytes(raw)
                textures.append(dict(name=obj.m_Name, width=obj.m_Width,height=obj.m_Height,format="RGBAHalf",raw_bytes=len(raw)))
                continue
            try:
                obj.image.save(OUT / (obj.m_Name + ".png"))
                textures.append(dict(name=obj.m_Name, width=obj.m_Width, height=obj.m_Height))
            except ValueError as error:
                textures.append(dict(name=obj.m_Name, format=obj.m_TextureFormat,error=str(error)))
        else:
            shader = obj.m_Shader.read()
            shader_name = shader.m_ParsedForm.m_Name
            if shader_name not in [x["name"] for x in shaders]:
                shaders.append(export_shader(shader))
            materials.append(dict(name=obj.m_Name, shader=shader_name,
                floats={p:float(v) for p,v in obj.m_SavedProperties.m_Floats},
                colors={p:{a:getattr(v,a) for a in "rgba"} for p,v in obj.m_SavedProperties.m_Colors},
                textures={p:dict(name=v.m_Texture.read().m_Name if v.m_Texture.path_id else "",
                    scale=vec(v.m_Scale),offset=vec(v.m_Offset)) for p,v in obj.m_SavedProperties.m_TexEnvs}))
    b = Buffer(REF / "home-map-source/homestead_01.water")
    f = b.fields(b.number(0))
    v = b.target(f[2])
    tiles = []
    for i in range(b.number(v)):
        t = b.fields(b.target(v+4+i*4))
        x,z = struct.unpack_from("<HH",b.data,t[5])
        tiles.append(dict(x=x,z=z,rotation=b.number(t[4],"f") if t[4] else 0,
                          height_layer=b.number(t[0],"B") if t[0] else 0,
                          water_layers=b.number(t[2]) if t[2] else 0,
                          material=b.number(t[3],"H"),template=b.number(t[1]) if t[1] else 0))
    report = dict(source_bundles=[p["bundle"] for p in plan],meshes=meshes,
                  textures=textures,materials=materials,shaders=shaders,tiles=tiles,
                  grid_note="Odd-column offset hex grid: world x=3*x, z=sqrt(3)*(2*z+x%2). Matches terrain holes.")
    (OUT / "water.json").write_text(json.dumps(report,indent=2,default=str),encoding="utf-8")
    print(json.dumps(dict(meshes=meshes,textures=textures,materials=[m['name'] for m in materials],
                         shaders=shaders,tiles=len(tiles)),default=str))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--reflection-only", action="store_true")
    args = parser.parse_args()
    if not args.reflection_only:
        main()
    reflection()
