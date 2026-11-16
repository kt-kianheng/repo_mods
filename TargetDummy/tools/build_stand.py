"""Run inside Blender (Scripting tab, or through the Blender MCP) to build and export the target dummy's stand.

Builds the wooden training stand from the reference image: a collar hub on four splayed legs, a post and an arm
crossbar. The Robe itself isn't modelled here: the mod borrows the game's own Robe at runtime and fits the post and
crossbar to it, so the post and crossbar are exported at unit size and stretched in game.

  - TD_Base:     hub, rope bands, centre foot and four legs, 0.63 m tall (fixed size)
  - TD_Post:     pole 1 m tall with its origin at the bottom (scaled to length in game)
  - TD_Crossbar: bar 1 m long along X, centred (scaled to the Robe's arm span in game)

Writes src/TargetDummy/Assets/target_dummy.mesh (HWM1 format, read by MeshLibrary.cs), saves
blender/target_dummy.blend, and renders tools/stand_preview.png. "Preview_*" objects are just for the render.
Axes: Blender (x, y, z) -> Unity (-x, z, -y).
"""
import bpy, bmesh, math, os, struct
from mathutils import Matrix, Vector

ROOT = os.path.join(os.path.expanduser("~"), "Projects", "TargetDummy")
ASSETS = os.path.join(ROOT, "src", "TargetDummy", "Assets")
PARTS = ["TD_Base", "TD_Post", "TD_Crossbar"]


# ---------------------------------------------------------------- scene
def reset():
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    for ob in list(bpy.data.objects): bpy.data.objects.remove(ob, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.lights, bpy.data.cameras):
        for x in list(coll): coll.remove(x)


def material(name, col, rough, metal=0.0):
    m = bpy.data.materials.new(name)
    if not m.use_nodes: m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*col, 1.0)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    return m


def to_object(name, bm, mats, smooth_angle=40):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for m in mats: me.materials.append(m)
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    for p in me.polygons: p.use_smooth = True
    if hasattr(me, "set_sharpness_by_angle"): me.set_sharpness_by_angle(angle=math.radians(smooth_angle))
    return ob


def cylinder(bm, r, z0, z1, seg, side, cap, xf=Matrix()):
    res = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r, radius2=r, depth=z1 - z0,
                                matrix=xf @ Matrix.Translation((0, 0, (z0 + z1) / 2)))
    for f in {f for v in res["verts"] for f in v.link_faces}:
        f.material_index = cap if len(f.verts) > 4 else side


def torus(bm, R, r, z, mi, seg=32, ring=8):
    rows = [[bm.verts.new(((R + r * math.cos(b)) * math.cos(a), (R + r * math.cos(b)) * math.sin(a), z + r * math.sin(b)))
             for b in (2 * math.pi * j / ring for j in range(ring))] for a in (2 * math.pi * i / seg for i in range(seg))]
    for i in range(seg):
        for j in range(ring):
            bm.faces.new((rows[i][j], rows[(i + 1) % seg][j], rows[(i + 1) % seg][(j + 1) % ring], rows[i][(j + 1) % ring])).material_index = mi


def build():
    reset()
    wood = material("Wood", (0.30, 0.15, 0.075), 0.72)
    dark = material("Wood Dark", (0.20, 0.095, 0.045), 0.78)
    end = material("Wood End", (0.42, 0.25, 0.13), 0.85)
    rope = material("Rope", (0.52, 0.38, 0.20), 0.95)

    # base: collar hub with rope bands, a centre foot, and four square legs splayed to the floor
    bm = bmesh.new()
    cylinder(bm, 0.115, 0.44, 0.63, 32, 0, 2)
    cylinder(bm, 0.055, 0.0, 0.44, 20, 0, 2)
    torus(bm, 0.118, 0.012, 0.47, 3)
    torus(bm, 0.118, 0.012, 0.60, 3)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        top = Vector((0.08 * math.cos(a), 0.08 * math.sin(a), 0.52))
        foot = Vector((0.62 * math.cos(a), 0.62 * math.sin(a), -0.05))
        d = foot - top
        rot = d.normalized().to_track_quat('-Z', 'Y').to_matrix().to_4x4()
        res = bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation(top) @ rot @ Matrix.Diagonal((0.065, 0.075, d.length, 1)) @ Matrix.Translation((0, 0, -0.5)))
        for f in {f for v in res["verts"] for f in v.link_faces}: f.material_index = 1
    cut = bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0, 0), plane_no=(0, 0, 1), clear_inner=True)
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
    for f in bm.faces:
        if f.normal.z < -0.99 and all(abs(v.co.z) < 1e-4 for v in f.verts): f.material_index = 2   # sawn leg ends
    base = to_object("TD_Base", bm, [wood, dark, end, rope])
    bev = base.modifiers.new("Bevel", 'BEVEL'); bev.width = 0.008; bev.segments = 2; bev.limit_method = 'ANGLE'

    # post: 1 m, origin at the bottom
    bm = bmesh.new(); cylinder(bm, 0.072, 0.0, 1.0, 24, 0, 1)
    to_object("TD_Post", bm, [wood, end])

    # crossbar: 1 m along X, centred, lightly rounded ends
    bm = bmesh.new(); cylinder(bm, 0.075, -0.5, 0.5, 24, 0, 1, Matrix.Rotation(math.radians(90), 4, 'Y'))
    bar = to_object("TD_Crossbar", bm, [wood, end])
    bev = bar.modifiers.new("Bevel", 'BEVEL'); bev.width = 0.015; bev.segments = 3; bev.limit_method = 'ANGLE'


# ---------------------------------------------------------------- export (same format as HatterWeaponsPack)
def to_unity(v):
    return (-v[0], v[2], -v[1])


def export_object(ob):
    deps = bpy.context.evaluated_depsgraph_get()
    eo = ob.evaluated_get(deps)
    me = eo.to_mesh()
    bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm, faces=bm.faces[:]); bm.to_mesh(me); bm.free()
    me.transform(ob.matrix_world)
    corner = [cn.vector.copy() for cn in me.corner_normals] if hasattr(me, "corner_normals") else None
    subs = {}
    for poly in me.polygons:
        s = subs.setdefault(poly.material_index, {"v": [], "i": []})
        base = len(s["v"])
        for li in poly.loop_indices:
            vi = me.loops[li].vertex_index
            n = corner[li] if corner else (me.vertices[vi].normal if poly.use_smooth else poly.normal)
            s["v"].append(to_unity(me.vertices[vi].co) + to_unity(n))
        s["i"] += [base, base + 2, base + 1]
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


def export():
    os.makedirs(ASSETS, exist_ok=True)
    data = bytearray(b"HWM1") + struct.pack("<i", len(PARTS))
    for n in PARTS:
        ob = bpy.data.objects[n]
        assert ob.matrix_world == Matrix.Identity(4), n + " must stay at the origin, unscaled"
        data += export_object(ob)
    out = os.path.join(ASSETS, "target_dummy.mesh")
    with open(out, "wb") as f: f.write(data)
    return out, len(data)


# ---------------------------------------------------------------- preview render
def preview(path):
    # a laid-out copy of the stand (post to 2.3 m, crossbar at 1.95 m, 1.6 m wide), the parts themselves stay put
    def dup(name, loc, scale):
        src = bpy.data.objects[name]
        ob = src.copy(); ob.name = "Preview_" + name; ob.location = loc; ob.scale = scale
        bpy.context.scene.collection.objects.link(ob)
        return ob
    dup("TD_Post", (0, 0, 0.6), (1, 1, 1.7))
    dup("TD_Crossbar", (0, 0, 1.95), (1.6, 1, 1))
    for n in ("TD_Post", "TD_Crossbar"): bpy.data.objects[n].hide_render = True   # the unit-size originals

    sc = bpy.context.scene
    floor = bpy.data.meshes.new("Preview_Floor"); fbm = bmesh.new()
    bmesh.ops.create_grid(fbm, x_segments=1, y_segments=1, size=6); fbm.to_mesh(floor); fbm.free()
    fl = bpy.data.objects.new("Preview_Floor", floor); sc.collection.objects.link(fl)
    floor.materials.append(material("Preview Floor", (0.32, 0.25, 0.18), 0.9))
    w = bpy.data.worlds.get("World") or bpy.data.worlds.new("World"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.45, 0.34, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 0.6
    sun = bpy.data.objects.new("Preview_Sun", bpy.data.lights.new("Preview_Sun", 'SUN')); sc.collection.objects.link(sun)
    sun.data.energy = 3.5; sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))
    cam = bpy.data.objects.new("Preview_Camera", bpy.data.cameras.new("Preview_Camera")); sc.collection.objects.link(cam)
    cam.location = (2.6, -4.2, 1.6); cam.data.lens = 45
    cam.rotation_euler = (Vector((0, 0, 1.15)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.camera = cam
    sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
    sc.render.resolution_x = sc.render.resolution_y = 900
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__" or True:
    build()
    out, size = export()
    os.makedirs(os.path.join(ROOT, "blender"), exist_ok=True)
    preview(os.path.join(ROOT, "tools", "stand_preview.png"))
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "blender", "target_dummy.blend"))
    print("Exported", out, size, "bytes")
