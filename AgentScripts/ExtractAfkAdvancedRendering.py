"""Export study-only environment inputs and all six character reflection faces."""
import json
from pathlib import Path

from AgentScripts.AfkReferenceResources import load
from UnityPy.export.Texture2DConverter import parse_image_data

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "client/unity/InsectSpaceClient/Assets/Temp/AFKStudy/Advanced"


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    environment, plan = load(["scene/map/hraid/homestead_01.unity",
                              "character/hero/pbsc_char_hero_faye_show.prefab"])
    textures, cubes = [], []
    for reader in environment.objects:
        if reader.type.name == "MonoBehaviour":
            obj = reader.read()
            if obj.m_Script.read().m_ClassName != "EnvironmentInfo":
                continue
            for name, pointer in (("CloudShadow", obj.cloud.CloudTex), ("FogNoise", obj.fog.FastNoiseTex)):
                texture = pointer.read()
                texture.image.save(OUT / (name + ".png"))
                textures.append(dict(binding=name, source=texture.m_Name, width=texture.m_Width,
                                     height=texture.m_Height, sRGB=texture.m_ColorSpace == 1))
        elif reader.type.name == "Cubemap":
            obj = reader.read()
            if obj.m_Name not in ("hdr_10", "hdr_34"):
                continue
            raw = obj.get_image_data()
            if obj.m_ImageCount != 6 or len(raw) != obj.m_CompleteImageSize * 6:
                raise ValueError("Unexpected cubemap layout: " + obj.m_Name)
            faces = []
            for i, face in enumerate(("PositiveX", "NegativeX", "PositiveY", "NegativeY", "PositiveZ", "NegativeZ")):
                data = raw[i * obj.m_CompleteImageSize:(i + 1) * obj.m_CompleteImageSize]
                name = obj.m_Name + "-" + face + ".png"
                binary = obj.m_Name + "-" + face + ".etc1.bytes"
                (OUT / binary).write_bytes(data)
                parse_image_data(data, obj.m_Width, obj.m_Height, obj.m_TextureFormat,
                                 reader.version, reader.platform, getattr(obj, "m_PlatformBlob", None), True).save(OUT / name)
                faces.append(dict(face=face, file=name, raw=binary))
            if obj.m_TextureFormat != 34:
                raise ValueError("Expected ETC_RGB4 environment cube: " + obj.m_Name)
            cubes.append(dict(name=obj.m_Name, size=obj.m_Width, format=obj.m_TextureFormat,
                              formatName="ETC_RGB4", sRGB=obj.m_ColorSpace == 1,
                              mipCount=obj.m_MipCount, faceBytes=obj.m_CompleteImageSize, faces=faces))
    report = dict(textures=textures, cubemaps=cubes, source_bundles=[p["bundle"] for p in plan])
    if len(textures) != 2 or len(cubes) != 2:
        raise ValueError("Missing atmosphere/character reference inputs")
    (OUT / "inputs.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report))


if __name__ == "__main__":
    main()
