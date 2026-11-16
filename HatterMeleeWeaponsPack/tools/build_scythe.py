"""Run inside Blender (Scripting tab, or through the Blender MCP) to build and export the scythe.

  - SC_Scythe: one object. A long (2 m), thin, crooked wooden shaft (snath) along +Z from 0 (the grip end, with a hooked
               knob) to SNATH_LEN, a hand peg (nib) tied on with cord partway up, a cloth-and-cord binding at the head,
               and a long curved iron blade. The blade leaves the head along -Y (Unity +Z) and curls back towards the
               grip; its sharp edge is on the inner (grip) side, where you hook enemies before pulling.

Writes src/HatterMeleeWeaponsPack/Assets/scythe.mesh (HWM1 format), saves blender/scythe.blend and renders
tools/scythe_preview.png. Axes: Blender (x, y, z) -> Unity (-x, z, -y). Prints the blade numbers ScytheBuilder needs.
"""
import bpy, bmesh, math, os, random, struct
from mathutils import Matrix, Vector

ROOT = os.path.join(os.path.expanduser("~"), "Projects", "HatterMeleeWeaponsPack")
ASSETS = os.path.join(ROOT, "src", "HatterMeleeWeaponsPack", "Assets")
PARTS = ["SC_Scythe"]

SNATH_LEN = 2.00        # grip end to the head (was 1.75)
SNATH_R = 0.0165        # shaft radius near the grip (thin)
NIB_Z = 1.20            # hand peg height
BLADE_ROOT_Z = 1.95     # where the blade leaves the shaft
BLADE_LEN = 1.02        # along the blade (outward, -Y) (was 0.82)
BLADE_DROP = 0.30       # how far the tip curls back towards the grip (keeps the old shape)
BLADE_W = 0.11          # widest, near the root
K = SNATH_LEN / 1.75    # stretches the shaft's knots and wobble with its length
BLADE_T = 0.005         # spine thickness

random.seed(7)


def material(name, col, rough, metal=0.0):
    m = bpy.data.materials.new(name)
    if not m.use_nodes: m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*col, 1.0)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    return m


def catmull(points, n):
    """Smooth path through the points, n samples per span."""
    out = []
    pts = [points[0]] + points + [points[-1]]
    for i in range(1, len(pts) - 2):
        p0, p1, p2, p3 = pts[i - 1], pts[i], pts[i + 1], pts[i + 2]
        for k in range(n):
            t = k / n
            t2, t3 = t * t, t * t * t
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    out.append(points[-1])
    return out


def tube(bm, path, radius_at, mi, sides=10, cap_start=True, cap_end=True):
    """A tube along `path` whose radius is radius_at(i, n). Frames are parallel-transported (no twisting)."""
    n = len(path)
    rings = []
    normal = None
    for i, p in enumerate(path):
        t = (path[min(i + 1, n - 1)] - path[max(i - 1, 0)]).normalized()
        if normal is None:
            normal = t.orthogonal().normalized()
        else:
            normal = (normal - t * normal.dot(t)).normalized()
        binormal = t.cross(normal)
        r = radius_at(i, n)
        rings.append([bm.verts.new(p + (normal * math.cos(2 * math.pi * j / sides) + binormal * math.sin(2 * math.pi * j / sides)) * r)
                      for j in range(sides)])
    for i in range(n - 1):
        for j in range(sides):
            bm.faces.new((rings[i][j], rings[i][(j + 1) % sides], rings[i + 1][(j + 1) % sides], rings[i + 1][j])).material_index = mi
    if cap_start: bm.faces.new(list(reversed(rings[0]))).material_index = mi
    if cap_end: bm.faces.new(rings[-1]).material_index = mi


def wraps(bm, centre_path, z0, z1, r, mi, turns):
    """Cord wound round the shaft between z0 and z1 (follows the shaft's wobble)."""
    pts = []
    steps = turns * 12
    for k in range(steps + 1):
        t = k / steps
        z = z0 + (z1 - z0) * t
        c = shaft_point(centre_path, z)
        a = 2 * math.pi * turns * t
        pts.append(c + Vector((math.cos(a) * r, math.sin(a) * r, 0)))
    tube(bm, pts, lambda i, n: 0.0024, mi, sides=6)


def shaft_point(path, z):
    for a, b in zip(path, path[1:]):
        if a.z <= z <= b.z:
            f = (z - a.z) / max(b.z - a.z, 1e-6)
            return a.lerp(b, f)
    return path[-1] if z > path[-1].z else path[0]


def build():
    bpy.ops.wm.read_homefile(use_empty=True, use_factory_startup=True)
    wood = material("Wood Old", (0.25, 0.19, 0.13), 0.88)
    knot = material("Wood Dark", (0.13, 0.095, 0.07), 0.9)
    iron = material("Iron", (0.27, 0.26, 0.25), 0.55, 0.85)
    edge = material("Iron Edge", (0.55, 0.55, 0.56), 0.32, 1.0)
    cord = material("Cord", (0.22, 0.18, 0.13), 0.95)
    mats = [wood, knot, iron, edge, cord]   # 0 wood, 1 knot, 2 iron, 3 edge, 4 cord

    bm = bmesh.new()

    # ---- the snath: crooked shaft from the grip (z 0) to the head ----
    ctrl = [Vector((0.0, 0.0, 0.0))]
    for z in [v * K for v in (0.18, 0.42, 0.66, 0.90, 1.14, 1.38, 1.58)] + [SNATH_LEN]:
        ctrl.append(Vector((random.uniform(-0.016, 0.016), random.uniform(-0.016, 0.016), z)))
    ctrl[-1] = Vector((0.0, 0.0, SNATH_LEN))
    path = catmull(ctrl, 8)
    knots = [(0.31 * K, 0.25), (0.77 * K, 0.30), (1.27 * K, 0.22), (1.49 * K, 0.28)]   # (height, extra radius fraction)

    def snath_r(i, n):
        z = path[i].z
        r = SNATH_R * (1.0 - 0.18 * z / SNATH_LEN) * (1.0 + 0.06 * math.sin(z * 37.0))   # taper and grain wobble
        for kz, extra in knots:
            r *= 1.0 + extra * math.exp(-((z - kz) / 0.02) ** 2)
        return r
    tube(bm, path, snath_r, 0, sides=10)

    # the grip end: a knob hooking off to one side, like a gnarled root
    hook = catmull([Vector((0, 0, 0.02)), Vector((0.01, 0.025, -0.02)), Vector((0.02, 0.07, -0.035)), Vector((0.025, 0.10, -0.03))], 6)
    tube(bm, hook, lambda i, n: SNATH_R * (1.05 - 0.45 * i / n), 1, sides=8)

    # ---- the nib: a short peg for the other hand, tied on with cord ----
    nib_base = shaft_point(path, NIB_Z)
    nib = catmull([nib_base, nib_base + Vector((0.0, -0.06, 0.03)), nib_base + Vector((0.0, -0.11, 0.075)), nib_base + Vector((0.004, -0.125, 0.11))], 6)
    tube(bm, nib, lambda i, n: 0.011 * (1.0 - 0.35 * i / n), 0, sides=8)
    wraps(bm, path, NIB_Z - 0.035, NIB_Z + 0.035, snath_r(0, 1) * 0.92 + 0.003, 4, 5)

    # ---- the head: cloth binding and cord where the blade is fixed ----
    head_c = shaft_point(path, BLADE_ROOT_Z - 0.02)
    res = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=12, radius1=0.022, radius2=0.019, depth=0.13,
                                matrix=Matrix.Translation(head_c))
    for f in {f for v in res["verts"] for f in v.link_faces}: f.material_index = 1
    wraps(bm, path, BLADE_ROOT_Z - 0.08, BLADE_ROOT_Z + 0.03, 0.0235, 4, 7)
    # a short iron spur below the head, on the far side from the blade (like the tang showing through)
    spur_base = head_c + Vector((0, 0.018, -0.04))
    spur = [spur_base, spur_base + Vector((0.0, 0.03, -0.05)), spur_base + Vector((0.0, 0.04, -0.11))]
    tube(bm, catmull(spur, 4), lambda i, n: 0.006 * (1.0 - 0.9 * i / n) + 0.0008, 2, sides=6)

    # ---- the blade: leaves the head along -Y and curls back towards the grip; edge on the inner (lower) side ----
    def spine(t):
        return Vector((0.0, -BLADE_LEN * t, BLADE_ROOT_Z + 0.015 - BLADE_DROP * t * t))
    def tangent(t):
        return Vector((0.0, -BLADE_LEN, -2 * BLADE_DROP * t)).normalized()
    steps = 40
    rows = []
    for k in range(steps + 1):
        t = k / steps
        p = spine(t)
        tan = tangent(t)
        inward = Vector((1, 0, 0)).cross(tan).normalized()      # in the blade's plane, towards the grip side
        if inward.z > 0: inward = -inward
        w = BLADE_W * (1.0 - t) ** 0.75 + 0.004                  # widest at the root, a point at the tip
        th = BLADE_T * (1.0 - 0.6 * t) + 0.0008
        # cross-section: spine (thick, outer) -> bevel -> edge (thin, inner)
        spine_l = p + Vector((-th / 2, 0, 0)); spine_r = p + Vector((th / 2, 0, 0))
        mid_l = p + inward * (w * 0.62) + Vector((-th * 0.35, 0, 0)); mid_r = p + inward * (w * 0.62) + Vector((th * 0.35, 0, 0))
        tip = p + inward * w
        rows.append([bm.verts.new(v) for v in (spine_l, mid_l, tip, mid_r, spine_r)])
    for k in range(steps):
        a, b = rows[k], rows[k + 1]
        quads = [(0, 1, 2), (2, 3, 4)]   # left face, right face (each split into body and edge bands)
        for (i0, i1, i2) in quads:
            bm.faces.new((a[i0], a[i1], b[i1], b[i0])).material_index = 2
            bm.faces.new((a[i1], a[i2], b[i2], b[i1])).material_index = 3
        bm.faces.new((a[4], a[0], b[0], b[4])).material_index = 2   # the spine's back
    bm.faces.new(list(reversed(rows[0]))).material_index = 2
    bm.normal_update()
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])

    me = bpy.data.meshes.new("SC_Scythe"); bm.to_mesh(me); bm.free()
    for m in mats: me.materials.append(m)
    ob = bpy.data.objects.new("SC_Scythe", me); bpy.context.scene.collection.objects.link(ob)
    for p in me.polygons: p.use_smooth = True
    if hasattr(me, "set_sharpness_by_angle"): me.set_sharpness_by_angle(angle=math.radians(50))

    # numbers for ScytheBuilder (in Unity item space: snath along +Y, blade along +Z)
    tip = spine(1.0)
    print("SCYTHE: snath", SNATH_LEN, "blade root z", BLADE_ROOT_Z, "blade tip (unity y,z)", round(tip.z, 3), round(-tip.y, 3))


# ---------------------------------------------------------------- export (same format as the other weapons)
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
        data += export_object(bpy.data.objects[n])
    out = os.path.join(ASSETS, "scythe.mesh")
    with open(out, "wb") as f: f.write(data)
    return out, len(data)


def preview(path):
    sc = bpy.context.scene
    ob = bpy.data.objects["SC_Scythe"]
    show = ob.copy(); show.name = "Preview_Scythe"; sc.collection.objects.link(show)
    # lean it like the reference: grip bottom-left, head top-right, blade curling down from the top
    show.matrix_world = Matrix.Translation((-0.62, 0.0, 0.05)) @ Matrix.Rotation(math.radians(38), 4, 'Y') @ Matrix.Rotation(math.radians(-90), 4, 'Z')
    ob.hide_render = True
    w = bpy.data.worlds.new("World"); sc.world = w
    if not w.use_nodes: w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.53, 0.50, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 0.35
    sun = bpy.data.objects.new("Preview_Sun", bpy.data.lights.new("Preview_Sun", 'SUN')); sc.collection.objects.link(sun)
    sun.data.energy = 3.0; sun.rotation_euler = (math.radians(35), math.radians(-25), math.radians(20))
    cam = bpy.data.objects.new("Preview_Camera", bpy.data.cameras.new("Preview_Camera")); sc.collection.objects.link(cam)
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.65
    cam.location = (0.15, -3.0, 1.08); cam.rotation_euler = (math.radians(90), 0, 0)
    sc.camera = cam
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in engines else 'BLENDER_EEVEE'
    sc.render.resolution_x = sc.render.resolution_y = 1000
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__" or True:
    build()
    out, size = export()
    os.makedirs(os.path.join(ROOT, "blender"), exist_ok=True)
    preview(os.path.join(ROOT, "tools", "scythe_preview.png"))
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "blender", "scythe.blend"))
    print("Exported", out, size, "bytes")
