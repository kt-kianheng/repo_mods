"""Run inside Blender (Scripting tab, or through the Blender MCP) to build and export the nunchuck.

  - NC_Handle: one 50 cm handle (both ends of the nunchuck use it). Brown octagonal wood, 51 -> 42 mm across, two dark
               grip bands, a silver cap and an eye ring at the chain end. Lies along +Z from 0 (the grip end) to
               0.50 m (top of the cap); the eye ring sits above that (RING_CENTRE; the chain hangs from RING_TOP).
  - NC_Link:   one silver chain link (oval, 4 cm inner length, 6 mm wire), centred on the origin, long axis +Z.
               The game strings ten of them, 4 cm apart, as separate physics pieces: the 40 cm chain.

Writes src/HatterMeleeWeaponsPack/Assets/nunchuck.mesh (HWM1 format), saves blender/nunchuck.blend and renders
tools/nunchuck_preview.png ("Preview_*" objects are only for the render). Axes: Blender (x, y, z) -> Unity (-x, z, -y).
"""
import bpy, bmesh, math, os, struct
from mathutils import Matrix, Vector

ROOT = os.path.join(os.path.expanduser("~"), "Projects", "HatterMeleeWeaponsPack")
ASSETS = os.path.join(ROOT, "src", "HatterMeleeWeaponsPack", "Assets")
PARTS = ["NC_Handle", "NC_Link"]

HANDLE_LEN = 0.50       # grip end to the top of the cap
THICK = 1.5             # handle thickness, relative to the first version (34 -> 28 mm across)
LINK_INNER = 0.040      # inner length = link pitch
WIRE_R = 0.003          # 6 mm wire
LINK_WIDTH = 0.0227     # centre-line width
RING_R, RING_TUBE = 0.0063 * THICK, 0.0021 * THICK
RING_CENTRE = HANDLE_LEN + 0.005 * THICK + RING_R - RING_TUBE * 0.5
RING_TOP = RING_CENTRE + RING_R - RING_TUBE * 0.5   # where the chain hangs from (keep NunchuckBuilder.RingTop equal)


def reset():
    bpy.ops.wm.read_homefile(use_empty=True, use_factory_startup=True)


def material(name, col, rough, metal=0.0):
    m = bpy.data.materials.new(name)
    if not m.use_nodes: m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*col, 1.0)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    return m


def to_object(name, bm, mats, smooth_angle=35):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for m in mats: me.materials.append(m)
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    for p in me.polygons: p.use_smooth = True
    if hasattr(me, "set_sharpness_by_angle"): me.set_sharpness_by_angle(angle=math.radians(smooth_angle))
    return ob


def ring(bm, center, R, r, axis, mi, seg=24, sides=8):
    """Torus of centre-line radius R and tube radius r around `axis` ('X', 'Y' or 'Z')."""
    rot = {"Z": Matrix.Identity(3), "X": Matrix.Rotation(math.radians(90), 3, 'Y'), "Y": Matrix.Rotation(math.radians(90), 3, 'X')}[axis]
    rows = []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        row = []
        for j in range(sides):
            b = 2 * math.pi * j / sides
            p = Vector(((R + r * math.cos(b)) * math.cos(a), (R + r * math.cos(b)) * math.sin(a), r * math.sin(b)))
            row.append(bm.verts.new(Vector(center) + rot @ p))
        rows.append(row)
    for i in range(seg):
        for j in range(sides):
            bm.faces.new((rows[i][j], rows[(i + 1) % seg][j], rows[(i + 1) % seg][(j + 1) % sides], rows[i][(j + 1) % sides])).material_index = mi


def frustum(bm, z0, z1, r0, r1, seg, mi_side, mi_cap, cap_bottom=True, cap_top=True, rot_offset=0.0):
    res = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r0, radius2=r1, depth=z1 - z0,
                                matrix=Matrix.Translation((0, 0, (z0 + z1) / 2)) @ Matrix.Rotation(rot_offset, 4, 'Z'))
    for f in {f for v in res["verts"] for f in v.link_faces}:
        f.material_index = mi_cap if len(f.verts) > 4 else mi_side
    return res


def build():
    reset()
    wood = material("Wood", (0.36, 0.18, 0.08), 0.6)
    dark = material("Wood Dark", (0.17, 0.08, 0.035), 0.7)
    steel = material("Steel", (0.78, 0.79, 0.81), 0.28, 1.0)

    # ---- handle: octagonal shaft, tapered (51 mm -> 42 mm across at THICK 1.5) ----
    bm = bmesh.new()
    oct_r = lambda across: across / 2 / math.cos(math.pi / 8)   # octagon circumradius from its width across flats
    L, k = HANDLE_LEN, THICK
    shaft_top = L - 0.015 * k
    w_bottom, w_top = 0.034 * k, 0.028 * k
    width_at = lambda z: w_bottom + (w_top - w_bottom) * z / shaft_top
    frustum(bm, 0.0, shaft_top, oct_r(w_bottom), oct_r(w_top), 8, 0, 0, rot_offset=math.pi / 8)
    # grip bands (a hair proud of the flats) near each end
    for z in (0.045, L - 0.09):
        across = width_at(z + 0.004 * k) + 0.0012 * k
        frustum(bm, z, z + 0.008 * k, oct_r(across), oct_r(across), 8, 1, 1, rot_offset=math.pi / 8)
    # silver cap at the chain end, and the eye ring the chain hangs from
    frustum(bm, shaft_top - 0.002 * k, L, 0.0152 * k, 0.0132 * k, 24, 2, 2)
    frustum(bm, L - 0.002 * k, L + 0.005 * k, 0.0050 * k, 0.0042 * k, 12, 2, 2)
    ring(bm, (0, 0, RING_CENTRE), RING_R, RING_TUBE, "Y", 2)
    handle = to_object("NC_Handle", bm, [wood, dark, steel])
    bev = handle.modifiers.new("Bevel", 'BEVEL'); bev.width = 0.0012; bev.segments = 2; bev.limit_method = 'ANGLE'

    # ---- chain link: a stadium-shaped loop of round wire, long axis +Z, width along X ----
    bm = bmesh.new()
    half_straight = (LINK_INNER + 2 * WIRE_R - (LINK_WIDTH)) / 2 + 0.0   # centre-line straight half-length
    rc = LINK_WIDTH / 2                                                    # centre-line end radius
    path = []
    n_arc, n_str = 10, 3
    for k in range(n_arc + 1):                                             # top arc
        a = math.pi * k / n_arc
        path.append(Vector((rc * math.cos(a), 0, half_straight + rc * math.sin(a))))
    for k in range(1, n_str):                                              # left straight
        path.append(Vector((-rc, 0, half_straight - 2 * half_straight * k / n_str)))
    for k in range(n_arc + 1):                                             # bottom arc
        a = math.pi + math.pi * k / n_arc
        path.append(Vector((rc * math.cos(a), 0, -half_straight + rc * math.sin(a))))
    for k in range(1, n_str):                                              # right straight
        path.append(Vector((rc, 0, -half_straight + 2 * half_straight * k / n_str)))
    sides = 8
    rows = []
    n = len(path)
    for i, p in enumerate(path):
        t = (path[(i + 1) % n] - path[i - 1]).normalized()
        nrm = t.cross(Vector((0, 1, 0))).normalized()
        bin_ = Vector((0, 1, 0))
        rows.append([bm.verts.new(p + (nrm * math.cos(2 * math.pi * j / sides) + bin_ * math.sin(2 * math.pi * j / sides)) * WIRE_R) for j in range(sides)])
    for i in range(n):
        for j in range(sides):
            bm.faces.new((rows[i][j], rows[(i + 1) % n][j], rows[(i + 1) % n][(j + 1) % sides], rows[i][(j + 1) % sides]))
    to_object("NC_Link", bm, [steel], smooth_angle=80)


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
            nn = corner[li] if corner else (me.vertices[vi].normal if poly.use_smooth else poly.normal)
            s["v"].append(to_unity(me.vertices[vi].co) + to_unity(nn))
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
    out = os.path.join(ASSETS, "nunchuck.mesh")
    with open(out, "wb") as f: f.write(data)
    return out, len(data)


# ---------------------------------------------------------------- preview render
def preview(path):
    sc = bpy.context.scene
    handle, link = bpy.data.objects["NC_Handle"], bpy.data.objects["NC_Link"]

    def dup(src, mat):
        ob = src.copy(); ob.name = "Preview_" + src.name; ob.matrix_world = mat
        sc.collection.objects.link(ob)
        return ob

    # handle A lying on a slant, the chain draping down in an arc, handle B lying on the floor
    a_base = Vector((-0.40, 0.0, 0.03))
    a_dir = Vector((0.55, 0.0, 0.83)).normalized()
    a_rot = a_dir.to_track_quat('Z', 'Y').to_matrix().to_4x4()
    dup(handle, Matrix.Translation(a_base) @ a_rot)
    start = a_base + a_dir * RING_TOP
    pts = []
    for i in range(11):
        t = i / 10
        pts.append(start + Vector((0.27 * t, 0.0, -0.52 * t + 0.12 * math.sin(math.pi * t))))
    # ten links, 3 cm apart, each heading for the next point on the arc
    p = start
    d = (pts[1] - pts[0]).normalized()
    for k in range(10):
        tgt = pts[min(k + 1, 10)]
        d = (tgt - p).normalized() if (tgt - p).length > 1e-4 else d
        c = p + d * (LINK_INNER / 2)
        roll = Matrix.Rotation(math.radians(90 * (k % 2)), 4, 'Z')
        dup(link, Matrix.Translation(c) @ d.to_track_quat('Z', 'Y').to_matrix().to_4x4() @ roll)
        p = p + d * LINK_INNER
    b_end = p
    b_dir = Vector((0.9, 0.25, -0.03)).normalized()
    dup(handle, Matrix.Translation(b_end + b_dir * RING_TOP) @ (-b_dir).to_track_quat('Z', 'Y').to_matrix().to_4x4())

    for n in PARTS: bpy.data.objects[n].hide_render = True
    floor = bpy.data.meshes.new("Preview_Floor"); fbm = bmesh.new()
    bmesh.ops.create_grid(fbm, x_segments=1, y_segments=1, size=3); fbm.to_mesh(floor); fbm.free()
    fl = bpy.data.objects.new("Preview_Floor", floor); sc.collection.objects.link(fl)
    floor.materials.append(material("Preview Floor", (0.30, 0.27, 0.24), 0.9))
    w = bpy.data.worlds.new("World"); sc.world = w
    if not w.use_nodes: w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.42, 0.40, 0.38, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 0.7
    sun = bpy.data.objects.new("Preview_Sun", bpy.data.lights.new("Preview_Sun", 'SUN')); sc.collection.objects.link(sun)
    sun.data.energy = 3.0; sun.rotation_euler = (math.radians(45), math.radians(15), math.radians(-30))
    cam = bpy.data.objects.new("Preview_Camera", bpy.data.cameras.new("Preview_Camera")); sc.collection.objects.link(cam)
    cam.location = (0.20, -1.65, 0.70); cam.data.lens = 50
    cam.rotation_euler = (Vector((0.08, 0.0, 0.28)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.camera = cam
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in engines else 'BLENDER_EEVEE'
    sc.render.resolution_x = sc.render.resolution_y = 900
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__" or True:
    build()
    out, size = export()
    os.makedirs(os.path.join(ROOT, "blender"), exist_ok=True)
    preview(os.path.join(ROOT, "tools", "nunchuck_preview.png"))
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "blender", "nunchuck.blend"))
    print("Exported", out, size, "bytes; RING_TOP =", round(RING_TOP, 4), "LINK_INNER =", LINK_INNER)
