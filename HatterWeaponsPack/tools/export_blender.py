"""Run inside Blender (Scripting tab, or via the Blender MCP) to export the weapon models for the mod.

Writes the .mesh files in src/HatterWeaponsPack/Assets ("HWM1" format, read by MeshLibrary.cs):
  - rocket_launcher.mesh: RPG_Launcher and RPG_Rocket (the rocket in its loaded position)
  - plasma_rifle.mesh: Plasma_Rifle
  - ember_wand.mesh: Ember_Wand and Ember_Tip (the tip crystal, lit only when a fireball is ready)
  - frost_wand.mesh: Frost_Wand, Frost_Tip and Icicle (the projectile, centred on the origin)
  - healing_wand.mesh: Heal_Wand and Heal_Tip
  - objects in world space; objects missing from the open .blend are skipped with a message
  - converted to Unity axes: Blender (x, y, z) -> Unity (-x, z, -y), so the muzzle (Blender -Y) is Unity +Z
  - one sub-mesh per material, with base colour / smoothness / metallic from the Principled BSDF
"""
import bpy, bmesh, struct, os

ASSETS = os.path.join(os.path.expanduser("~"), "Projects", "HatterWeaponsPack", "src", "HatterWeaponsPack", "Assets")
EXPORTS = {
    "rocket_launcher.mesh": ["RPG_Launcher", "RPG_Rocket"],
    "plasma_rifle.mesh": ["Plasma_Rifle"],
    "ember_wand.mesh": ["Ember_Wand", "Ember_Tip"],
    "frost_wand.mesh": ["Frost_Wand", "Frost_Tip", "Icicle"],
    "healing_wand.mesh": ["Heal_Wand", "Heal_Tip"],
}


def to_unity(v):
    return (-v[0], v[2], -v[1])


def export_object(ob):
    deps = bpy.context.evaluated_depsgraph_get()
    eo = ob.evaluated_get(deps)
    me = eo.to_mesh()
    bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm, faces=bm.faces[:]); bm.to_mesh(me); bm.free()
    me.transform(ob.matrix_world)
    corner_normals = [cn.vector.copy() for cn in me.corner_normals] if hasattr(me, "corner_normals") else None
    subs = {}
    for poly in me.polygons:
        s = subs.setdefault(poly.material_index, {"v": [], "i": []})
        base = len(s["v"])
        for li in poly.loop_indices:
            vi = me.loops[li].vertex_index
            n = corner_normals[li] if corner_normals else (me.vertices[vi].normal if poly.use_smooth else poly.normal)
            s["v"].append(to_unity(me.vertices[vi].co) + to_unity(n))
        s["i"] += [base, base + 2, base + 1]          # mirrored axis -> reverse winding
    blob = bytearray()
    name = ob.name.encode(); blob += struct.pack("<i", len(name)) + name
    used = [k for k in sorted(subs) if subs[k]["i"]]
    blob += struct.pack("<i", len(used))
    for k in used:
        m = ob.material_slots[k].material
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        col = tuple(bsdf.inputs["Base Color"].default_value)
        mname = m.name.encode(); blob += struct.pack("<i", len(mname)) + mname
        blob += struct.pack("<4f2f", *col, 1.0 - bsdf.inputs["Roughness"].default_value, bsdf.inputs["Metallic"].default_value)
        v, idx = subs[k]["v"], subs[k]["i"]
        blob += struct.pack("<i", len(v))
        for row in v: blob += struct.pack("<6f", *row)
        blob += struct.pack("<i", len(idx)) + struct.pack("<%di" % len(idx), *idx)
    eo.to_mesh_clear()
    return blob


os.makedirs(ASSETS, exist_ok=True)
for file, names in EXPORTS.items():
    if not all(n in bpy.data.objects for n in names):
        print("Skipped", file, "(objects not in this .blend)")
        continue
    data = bytearray(b"HWM1") + struct.pack("<i", len(names))
    for n in names:
        data += export_object(bpy.data.objects[n])
    out = os.path.join(ASSETS, file)
    with open(out, "wb") as f:
        f.write(data)
    print("Exported", out, len(data), "bytes")
