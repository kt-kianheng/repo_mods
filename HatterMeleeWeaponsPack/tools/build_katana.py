"""Run inside Blender (Scripting tab, or through the Blender MCP) to build and export the katana.

  - KT_Katana: one object, about 1.0 m long, along +Z from 0 (the pommel end) to the tip.
      * tsuka (grip, 0 - 0.265 m): an oval core of cream ray skin, wrapped in black cord (a sleeve with diamond
        openings on both flat sides, where the skin shows through), gold ornaments under the wrap at the middle,
        dark metal pommel cap (kashira) and collar (fuchi)
      * tsuba (guard): a dark iron oval disc
      * habaki (blade collar): brass
      * blade (0.70 m): curved backwards (the tip bends towards the spine), spine along +Y, edge along -Y
        (Unity +Z), a ridge line (shinogi), a bright wavy temper line (hamon) along the edge, and a curved tip
        (kissaki) where the edge sweeps up to meet the spine.

Writes src/HatterMeleeWeaponsPack/Assets/katana.mesh (HWM1 format), saves blender/katana.blend and renders
tools/katana_preview.png. Axes: Blender (x, y, z) -> Unity (-x, z, -y). Prints the numbers the item builder needs.
"""
import bpy, bmesh, math, os, struct
from mathutils import Matrix, Vector

ROOT = os.path.join(os.path.expanduser("~"), "Projects", "HatterMeleeWeaponsPack")
ASSETS = os.path.join(ROOT, "src", "HatterMeleeWeaponsPack", "Assets")
PARTS = ["KT_Katana"]

TSUKA_LEN = 0.265       # pommel end to the guard
TSUKA_A = 0.0130        # grip half-width across the flat sides (x)
TSUKA_B = 0.0175        # grip half-height, edge to spine (y)
TSUBA_Z = TSUKA_LEN     # the guard's grip-side face
TSUBA_T = 0.007
HABAKI_LEN = 0.032
BLADE_ROOT = TSUBA_Z + TSUBA_T               # where the steel starts (inside the habaki)
BLADE_LEN = 0.705                            # nagasa (plus the bit inside the habaki)
BLADE_TIP = BLADE_ROOT + BLADE_LEN
BLADE_W0, BLADE_W1 = 0.032, 0.023            # width at the root and just before the tip
BLADE_T0, BLADE_T1 = 0.0072, 0.0050          # thickness at the spine
SORI = 0.030                                 # curve: how far the tip bends back towards the spine (+Y)
KISSAKI = 0.048                              # length of the curved tip
DIAMONDS = 11                                # openings in the cord wrap, per side


def material(name, col, rough, metal=0.0):
    m = bpy.data.materials.new(name)
    if not m.use_nodes: m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*col, 1.0)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    return m


def spine_y(z):
    """The blade's spine bends back (+Y) as it goes out: none at the root, SORI at the tip."""
    if z <= BLADE_ROOT: return 0.0
    t = (z - BLADE_ROOT) / BLADE_LEN
    return SORI * t * t


def oval_grid(bm, z0, z1, a_at, b_at, mi_at, nz, na, cap0=False, cap1=False):
    """An oval tube from z0 to z1 (half-axes a_at(z), b_at(z)). mi_at(z, angle) gives each face's material, or None
    to leave a hole there."""
    rings = []
    for i in range(nz + 1):
        z = z0 + (z1 - z0) * i / nz
        a, b = a_at(z), b_at(z)
        rings.append([bm.verts.new((a * math.cos(2 * math.pi * j / na), b * math.sin(2 * math.pi * j / na), z)) for j in range(na)])
    for i in range(nz):
        zc = z0 + (z1 - z0) * (i + 0.5) / nz
        for j in range(na):
            mi = mi_at(zc, 2 * math.pi * (j + 0.5) / na)
            if mi is None: continue
            bm.faces.new((rings[i][j], rings[i][(j + 1) % na], rings[i + 1][(j + 1) % na], rings[i + 1][j])).material_index = mi
    if cap0: bm.faces.new(list(reversed(rings[0]))).material_index = mi_at(z0, 0.0) or 0
    if cap1: bm.faces.new(rings[-1]).material_index = mi_at(z1, 0.0) or 0


def blob(bm, centre, radii, mi, seg=10, rings=6):
    res = bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0,
                                    matrix=Matrix.Translation(centre) @ Matrix.Diagonal((*radii, 1.0)))
    for f in {f for v in res["verts"] for f in v.link_faces}: f.material_index = mi


def build():
    bpy.ops.wm.read_homefile(use_empty=True, use_factory_startup=True)
    ito = material("Cord Black", (0.025, 0.024, 0.023), 0.9)
    same = material("Ray Skin", (0.80, 0.77, 0.66), 0.7)
    iron = material("Iron Dark", (0.09, 0.08, 0.07), 0.6, 0.7)
    brass = material("Brass", (0.80, 0.62, 0.28), 0.32, 1.0)
    steel = material("Steel", (0.62, 0.63, 0.65), 0.28, 1.0)
    hamon = material("Steel Hamon", (0.93, 0.93, 0.94), 0.30, 0.55)   # frosty white temper line: less mirror-like than the body
    gold = material("Gold", (0.85, 0.66, 0.25), 0.35, 1.0)
    # the sharp half of the blade, from half its width to the temper line: looks like the steel, but its own material,
    # so the game can make the whole sharp half (it and the hamon) glow when the katana is charged
    edge = material("Steel Edge", (0.62, 0.63, 0.65), 0.28, 1.0)
    mats = [ito, same, iron, brass, steel, hamon, gold, edge]   # 0 cord, 1 skin, 2 iron, 3 brass, 4 steel, 5 hamon, 6 gold, 7 edge
    bm = bmesh.new()

    # ---- tsuka: skin core, cord sleeve with diamond openings, pommel cap and collar ----
    def grip_scale(z):   # very slightly waisted in the middle, wider at the ends
        t = z / TSUKA_LEN
        return 1.0 - 0.05 * math.sin(math.pi * t) + 0.04 * t
    wrap0, wrap1 = 0.014, TSUKA_LEN - 0.016
    oval_grid(bm, 0.008, TSUKA_LEN - 0.010, lambda z: TSUKA_A * grip_scale(z), lambda z: TSUKA_B * grip_scale(z),
              lambda z, ang: 1, 30, 20)
    pitch = (wrap1 - wrap0) / DIAMONDS

    def cord(z, ang):
        # diamonds face the flat sides (angle 0 and pi): the skin shows through
        k = (z - wrap0) / pitch
        dz = abs(k - math.floor(k) - 0.5) * 2.0          # 0 at a diamond's centre, 1 where the cords cross
        for side in (0.0, math.pi):
            da = abs((ang - side + math.pi) % (2 * math.pi) - math.pi) / 0.95
            if dz + da < 0.78: return None
        return 0
    oval_grid(bm, wrap0, wrap1, lambda z: TSUKA_A * grip_scale(z) + 0.0016, lambda z: TSUKA_B * grip_scale(z) + 0.0016,
              cord, 176, 48)
    # menuki: gold ornaments under the wrap, one on each flat side, a little past the middle
    for sx in (1, -1):
        zc = wrap0 + pitch * 6.5
        blob(bm, Vector((sx * TSUKA_A * grip_scale(zc) * 0.98, 0.0, zc)), (0.0022, 0.0075, 0.018), 6)
    # kashira (pommel cap) and fuchi (collar)
    oval_grid(bm, 0.0, wrap0 + 0.002, lambda z: TSUKA_A * 1.06, lambda z: TSUKA_B * 1.06, lambda z, a: 2, 4, 32, cap0=True)
    oval_grid(bm, wrap1 - 0.002, TSUKA_LEN, lambda z: TSUKA_A * grip_scale(z) * 1.13, lambda z: TSUKA_B * grip_scale(z) * 1.13,
              lambda z, a: 2, 4, 32, cap1=True)

    # ---- tsuba: dark iron oval disc ----
    oval_grid(bm, TSUBA_Z, TSUBA_Z + TSUBA_T, lambda z: 0.036, lambda z: 0.040, lambda z, a: 2, 1, 48, cap0=True, cap1=True)

    # ---- habaki: brass collar round the blade's root ----
    hb = bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation((0.0, -BLADE_W0 * 0.5, BLADE_ROOT + HABAKI_LEN / 2))
                               @ Matrix.Diagonal((BLADE_T0 + 0.006, BLADE_W0 + 0.007, HABAKI_LEN, 1.0)))
    for f in {f for v in hb["verts"] for f in v.link_faces}: f.material_index = 3

    # ---- blade: spine at +Y, edge at -Y; section spine | shinogi ridge | hamon line | edge on each side ----
    steps = 70
    rows = []
    for k in range(steps + 1):
        z = BLADE_ROOT + BLADE_LEN * k / steps
        t = k / steps
        y0 = spine_y(z)
        w = BLADE_W0 + (BLADE_W1 - BLADE_W0) * t
        th = BLADE_T0 + (BLADE_T1 - BLADE_T0) * t
        zk = BLADE_TIP - KISSAKI
        if z > zk:   # the tip: the edge sweeps up to meet the spine on a curve
            s = min((z - zk) / KISSAKI, 1.0)
            w *= math.sqrt(max(1.0 - s * s, 0.0)) * 0.97 + 0.0004 * (1 - s)
            th *= 1.0 - 0.75 * s
        w = max(w, 0.0004); th = max(th, 0.0004)
        hf = 0.70 + 0.055 * math.sin(z * 2 * math.pi / 0.037) + 0.02 * math.sin(z * 2 * math.pi / 0.011)   # wavy hamon
        def thick(f):   # half-thickness across the width: full at the ridge (0.3), thin at the edge (1)
            return th * 0.55 * (1.0 if f <= 0.3 else (1.0 - f) / 0.7 + 0.04)
        pts = []
        for f, sx in ((0.0, -1), (0.3, -1), (0.5, -1), (hf, -1), (1.0, 0), (hf, 1), (0.5, 1), (0.3, 1), (0.0, 1)):
            half = th * 0.5 if f == 0.0 else thick(f)
            pts.append(Vector((sx * half, y0 - w * f, z)))
        rows.append([bm.verts.new(p) for p in pts])
    # bands: spine flat to ridge and ridge to half width (steel), half width to hamon (steel edge), hamon to edge (bright),
    # mirrored, and the spine
    bands = [(0, 1, 4), (1, 2, 4), (2, 3, 7), (3, 4, 5), (4, 5, 5), (5, 6, 7), (6, 7, 4), (7, 8, 4), (8, 0, 4)]
    for k in range(steps):
        a, b = rows[k], rows[k + 1]
        for i0, i1, mi in bands:
            bm.faces.new((a[i0], a[i1], b[i1], b[i0])).material_index = mi
    bm.faces.new(list(reversed(rows[0]))).material_index = 4
    bm.faces.new(rows[-1]).material_index = 4

    bm.normal_update()
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new("KT_Katana"); bm.to_mesh(me); bm.free()
    for m in mats: me.materials.append(m)
    ob = bpy.data.objects.new("KT_Katana", me); bpy.context.scene.collection.objects.link(ob)
    for p in me.polygons: p.use_smooth = True
    if hasattr(me, "set_sharpness_by_angle"): me.set_sharpness_by_angle(angle=math.radians(40))

    # numbers for the item builder (Unity item space: grip along +Y, edge towards +Z)
    print("KATANA: grip 0 -", TSUKA_LEN, "| guard", TSUBA_Z, "| blade", round(BLADE_ROOT, 3), "-", round(BLADE_TIP, 3),
          "| tip (unity y, z)", round(BLADE_TIP, 3), round(-spine_y(BLADE_TIP), 3), "| widths", BLADE_W0, BLADE_W1, "| sori", SORI)


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
    out = os.path.join(ASSETS, "katana.mesh")
    with open(out, "wb") as f: f.write(data)
    return out, len(data)


def preview(path):
    sc = bpy.context.scene
    ob = bpy.data.objects["KT_Katana"]
    show = ob.copy(); show.name = "Preview_Katana"; sc.collection.objects.link(show)
    # laid out like the reference photo: grip top-left, tip bottom-right, side view (edge on the outer curve, up-right)
    show.matrix_world = Matrix.Translation((-0.41, 0.0, 0.27)) @ Matrix.Rotation(math.radians(123), 4, 'Y') @ Matrix.Rotation(math.radians(-90), 4, 'Z')
    ob.hide_render = True
    w = bpy.data.worlds.new("World"); sc.world = w
    if not w.use_nodes: w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.12, 0.12, 0.13, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 1.0
    sun = bpy.data.objects.new("Preview_Sun", bpy.data.lights.new("Preview_Sun", 'SUN')); sc.collection.objects.link(sun)
    sun.data.energy = 4.0; sun.rotation_euler = (math.radians(50), math.radians(-20), math.radians(15))
    cam = bpy.data.objects.new("Preview_Camera", bpy.data.cameras.new("Preview_Camera")); sc.collection.objects.link(cam)
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = 1.05
    cam.location = (0.0, -3.0, 0.0); cam.rotation_euler = (math.radians(90), 0, 0)
    sc.camera = cam
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in engines else 'BLENDER_EEVEE'
    sc.render.resolution_x, sc.render.resolution_y = 1600, 1060
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__" or True:
    build()
    out, size = export()
    os.makedirs(os.path.join(ROOT, "blender"), exist_ok=True)
    preview(os.path.join(ROOT, "tools", "katana_preview.png"))
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "blender", "katana.blend"))
    print("Exported", out, size, "bytes")
