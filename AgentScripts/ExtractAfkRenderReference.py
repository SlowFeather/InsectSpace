"""Read the existing local AFK bundle archive; export research data under Temp.

Requires UnityPy and the local reference archive. No network,
game process injection, or writes to the original bundle archive.
"""
import argparse
import json
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[1]
REFERENCE = ROOT / ".artifacts/reference/afk-journey"
OUT = ROOT / "client/unity/InsectSpaceClient/Assets/Temp/AFKStudy/RenderSource"
sys.path.insert(0, str(ROOT))
import UnityPy
from AgentScripts.AfkReferenceResources import load
from UnityPy.helpers.MeshHelper import MeshHandler
from UnityPy.helpers import CompressionHelper
from UnityPy.export.ShaderConverter import ShaderSubProgram
from UnityPy.streams import EndianBinaryReader
from PIL import Image, ImageDraw


def export_virtual_texture():
    """Resolve the VT container hashes using the game's separate VT index."""
    index = json.loads((REFERENCE / "all/non-unity/VT/vt.json").read_text(encoding="utf-8"))
    folder = OUT / "VT/mn_01"
    folder.mkdir(parents=True, exist_ok=True)
    pages, metadata, missing = [], [], []
    for record in index["ABInfoLst"]:
        bundle = record["abNameOfHash"]
        if not bundle.startswith("VT/mn_01/"):
            continue
        path = REFERENCE / "all/bundles" / bundle
        if not path.exists():
            missing.append(bundle)
            continue
        env = UnityPy.load(str(path))
        names = dict(zip(map(str, record["abSelfHashes"]), record["assetPaths"]))
        for key, pointer in env.container.items():
            semantic = names[key].replace("\\", "/")
            relative = Path(semantic.split("/mn_01/", 1)[0]).name + "/" + semantic.rsplit("/", 1)[-1]
            target = folder / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            reader = pointer.deref()
            if reader.type.name == "Texture2D":
                obj = reader.read()
                obj.image.save(target)
                x, y, mip = map(int, target.stem.split("_"))
                pages.append(dict(file=relative, source=semantic, bundle=bundle,
                                  path_id=reader.path_id, x=x, y=y, mip=mip,
                                  width=obj.m_Width, height=obj.m_Height))
            elif reader.type.name == "MonoBehaviour":
                data = reader.read_typetree()
                target.with_suffix(".json").write_text(json.dumps(data), encoding="utf-8")
                metadata.append(dict(file=relative, source=semantic, bundle=bundle,
                                     path_id=reader.path_id, pageSize=data.get("pageSize"),
                                     chunks=len(data.get("chunkPositions", []))))
    # Coarse pages provide coverage where no detailed page is present. UnityPy
    # returns top-left PNGs; the VT y coordinate increases from the bottom.
    resolution = 4096
    overview = Image.new("RGB", (resolution, resolution))
    diffuse = sorted((p for p in pages if p["file"].startswith("small_diffuse/")), key=lambda p: -p["mip"])
    for page in diffuse:
        span = 2 ** page["mip"]
        size = resolution * span // 64
        tile = Image.open(folder / page["file"]).convert("RGB").resize((size, size), Image.Resampling.BILINEAR)
        overview.paste(tile, (page["x"] * resolution // 64, resolution - (page["y"] + span) * resolution // 64))
    overview.save(folder / "small-diffuse-overview.png")
    detailed = [p for p in diffuse if p["mip"] == 1]
    sheet = Image.new("RGB", (8 * 192, ((len(detailed) + 7) // 8) * 212), "#202020")
    draw = ImageDraw.Draw(sheet)
    for i, page in enumerate(detailed):
        x, y = (i % 8) * 192, (i // 8) * 212
        tile = Image.open(folder / page["file"]).convert("RGB").resize((192, 192))
        sheet.paste(tile, (x, y))
        draw.text((x + 4, y + 194), Path(page["file"]).stem, fill="white")
    sheet.save(REFERENCE / "vt-review/mip1-contact-sheet.png")
    report = dict(map="mn_01", base_world_page_size=34, overview_resolution=resolution,
                  pages=pages, metadata=metadata, missing_bundles=missing,
                  note="Original VT pages; scene-to-world placement remains under investigation.")
    (folder / "index.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(dict(pages=len(pages), small_diffuse=len(diffuse), detailed=len(detailed),
                         metadata=len(metadata), missing_bundles=missing)))
    return report


def export_meshes():
    env = UnityPy.load(str(REFERENCE / "all/bundles/15548365238117124974.bundle"))
    result = []
    for reader in env.objects:
        if reader.type.name != "Mesh":
            continue
        obj = reader.read()
        if obj.m_Name not in ("mdsc_cliff_chapter00_01_1", "mdsc_cliff_chapter00_01_2"):
            continue
        handler = MeshHandler(obj)
        handler.process()
        def vectors(values, axes):
            return [dict(zip(axes, map(float, value))) for value in values or []]
        data = dict(name=obj.m_Name, vertices=vectors(handler.m_Vertices, "xyz"),
                    normals=vectors(handler.m_Normals, "xyz"), uv=vectors(handler.m_UV0, "xy"),
                    tangents=vectors(handler.m_Tangents, "xyzw"),
                    colors=vectors(handler.m_Colors, "rgba"),
                    submeshes=[dict(indices=[int(i) for tri in sub for i in tri]) for sub in handler.get_triangles()])
        (OUT / (obj.m_Name + ".json")).write_text(json.dumps(data), encoding="utf-8")
        result.append(dict(name=obj.m_Name, vertices=len(data["vertices"]),
                           triangles=sum(len(sub["indices"]) // 3 for sub in data["submeshes"]),
                           bundle="15548365238117124974.bundle", path_id=reader.path_id))
    return result


def export_shader(obj):
    """Unity 2021 shader table entries address separate compressed segments.

    UnityPy's default converter only reads segment zero. Keep all original
    program entries and select each entry's recorded segment before parsing.
    """
    name = obj.m_ParsedForm.m_Name
    folder = OUT / "Shaders" / name.rsplit("/", 1)[-1]
    folder.mkdir(parents=True, exist_ok=True)
    blob = bytes(obj.compressedBlob)
    reports = []
    for platform, offsets, compressed, lengths in zip(obj.platforms, obj.offsets, obj.compressedLengths, obj.decompressedLengths):
        if not isinstance(offsets, list):
            offsets, compressed, lengths = [offsets], [compressed], [lengths]
        segments = [CompressionHelper.decompress_lz4(blob[o:o+c], n) for o, c, n in zip(offsets, compressed, lengths)]
        count = struct.unpack_from("<i", segments[0], 0)[0]
        entries = []
        for index in range(count):
            offset, length, segment = struct.unpack_from("<iii", segments[0], 4 + index * 12)
            assert segment < len(segments) and offset + length <= len(segments[segment])
            source = EndianBinaryReader(segments[segment][offset:offset+length], endian="<")
            try:
                program = ShaderSubProgram(source)
                code = bytes(program.m_ProgramCode)
                extension = "glsl" if b"#version" in code[:256] else "bin"
                file = f"{platform}-{index}.{extension}"
                (folder / file).write_bytes(code)
                entries.append(dict(index=index, type=int(program.m_ProgramType), bytes=len(code), file=file,
                                    keywords=program.m_Keywords, local_keywords=program.m_LocalKeywords))
            except Exception as error:
                entries.append(dict(index=index, error=str(error)))
        reports.append(dict(platform=int(platform), segments=len(segments), count=count, entries=entries))
    (folder / "index.json").write_text(json.dumps(reports, indent=2), encoding="utf-8")
    return dict(name=name, platforms=[dict(platform=r["platform"], segments=r["segments"], count=r["count"],
        errors=sum("error" in x for x in r["entries"])) for r in reports])


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    meshes = export_meshes()
    env, _ = load(["scene/map/mn_01.unity"])
    shaders, environment = [], []
    wanted = {"iGame/Env/World/EnvWorldMountain", "iGame/Env/World/EnvWorldFoliageBillboard",
              "iGame/Env/World/EnvWorldLit", "iGame/Env/EnvGrassGPUInstance"}
    for reader in env.objects:
        if reader.type.name == "Shader":
            obj = reader.read()
            if obj.m_ParsedForm.m_Name in wanted:
                shaders.append(export_shader(obj))
        elif reader.type.name == "MonoBehaviour":
            obj = reader.read()
            kind = obj.m_Script.read().m_ClassName
            if kind in ("EnvironmentInfo", "LightAmbientColorVolumeComponent", "WaterVolumeComponent", "CloudVolumeComponent", "SunOfDayNight", "SkyTime"):
                environment.append(dict(type=kind, name=obj.m_Name, path_id=reader.path_id, data=reader.read_typetree()))
    (OUT / "environment.json").write_text(json.dumps(environment, indent=2), encoding="utf-8")
    report = dict(meshes=meshes, shaders=shaders, environment_components=len(environment))
    (OUT / "extraction-report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--vt-only", action="store_true")
    args = parser.parse_args()
    if args.vt_only:
        export_virtual_texture()
    else:
        main()
