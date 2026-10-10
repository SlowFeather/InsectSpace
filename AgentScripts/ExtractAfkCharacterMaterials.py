import json
from pathlib import Path

from AgentScripts.AfkReferenceResources import load, vec


def export():
    key = "character/hero/pbsc_char_hero_faye_show.prefab"
    environment, plan = load([key])
    folder = Path(__file__).resolve().parents[1] / "client/unity/InsectSpaceClient/Assets/Temp/AFKStudy/Models/Faye"
    names = {"pbsc_char_hero_faye_show", "pbsc_char_hero_faye_skin_show", "pbsc_char_hero_faye_weapon_show"}
    materials, textures = [], {}
    for reader in environment.objects:
        if reader.type.name != "Material":
            continue
        material = reader.read()
        if material.m_Name not in names:
            continue
        bindings = []
        for prop, binding in material.m_SavedProperties.m_TexEnvs:
            texture_name = ""
            if binding.m_Texture.path_id:
                texture = binding.m_Texture.read()
                texture_name = texture.m_Name
                if texture_name not in textures:
                    texture.image.save(folder / (texture_name + ".png"))
                    textures[texture_name] = {"name": texture_name, "sRGB": texture.m_ColorSpace == 1}
            bindings.append({"property": prop, "id": texture_name, "scale": vec(binding.m_Scale), "offset": vec(binding.m_Offset)})
        materials.append({"id": material.m_Name, "name": material.m_Name, "shader": "iGame/Char",
                          "floats": [{"property": prop, "value": float(value)} for prop, value in material.m_SavedProperties.m_Floats],
                          "colors": [{"property": prop, "value": {axis: getattr(value, axis) for axis in "rgba"}}
                                     for prop, value in material.m_SavedProperties.m_Colors], "textures": bindings})
    if {material["name"] for material in materials} != names:
        raise ValueError("Missing original opaque character materials")
    report = {"key": key, "source_bundles": [item["bundle"] for item in plan], "materials": materials, "textures": list(textures.values())}
    (folder / "materials.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({"materials": len(materials), "textures": list(textures.values())}))


if __name__ == "__main__":
    export()
