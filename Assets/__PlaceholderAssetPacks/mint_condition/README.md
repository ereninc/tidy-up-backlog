# Mint Condition: TCG Card Shop Props

22 stylized props. Flat-shaded, one shared 1024x1024 atlas.
Per-prop triangle counts are in the table below.

## Scale
1 Blender unit = 1 metre. Built to real-world scale.

## Contents
| Prop | Tris | Pivot |
|---|---|---|
| glass_counter | 440 | base_center |
| spinner_rack | 4536 | base_center |
| booster_display | 440 | base_center |
| pack_crimson | 132 | base_center |
| pack_cobalt | 132 | base_center |
| pack_teal | 132 | base_center |
| elite_box | 176 | base_center |
| deck_box | 132 | base_center |
| sleeve_stack | 176 | base_center |
| binder_closed | 88 | base_center |
| binder_open | 528 | base_center |
| slat_wall | 1496 | base_center |
| play_table | 552 | base_center |
| play_stool | 216 | base_center |
| playmat | 132 | base_center |
| register_pos | 220 | base_center |
| gacha_machine | 1952 | base_center |
| graded_slab | 224 | base_center |
| display_easel | 176 | base_center |
| trophy | 836 | base_center |
| display_tower | 1130 | base_center |
| shop_sign | 264 | world_origin |

## Formats
- `pack.glb`: Godot, Three.js, web (Y-up, metres). Recommended.
- `pack.fbx`: Unity/Unreal. In Unity set "Convert Units" on import. In Unreal,
  **disable Generate Lightmap UVs** (this pack uses a single packed UV channel).


## Texturing
One 1024x1024 gradient-ramp atlas, one opaque material + one emissive material.
If props look untextured, ensure `atlas.png` is in the same folder and the material
samples it as Base Color.

## Licence
Base pack: CC0 1.0 (public domain). Use commercially, no attribution required.
