"""Run inside Blender (Scripting tab, or via the Blender MCP with runpy.run_path) to build the A-Bomb valuable.

Builds blender/nuke.blend, exports src/HatterValuablesPack/Assets/nuke.mesh ("HWM1", read by MeshLibrary.cs) and renders
tools/nuke_preview.png (three-quarter view) and tools/nuke_icon.png (256 x 256, the Thunderstore icon).

Design: an original model in the style of a 1940s implosion bomb: a fat egg-shaped body with a seam band near the
nose, a short cone at the back into a square box tail braced by diagonal fins, small lugs and a fuse stub on the
casing, resting on a low steel cradle.
Object: Nuke (materials Bomb Casing, Bomb Tail, Bomb Fittings, Bomb Cradle).
Axes: the bomb lies along Blender +X (nose at +X; Unity -X), the floor is z = 0 and the origin is under the middle of
the whole model. The mod scales it in game (to the piano's size), so the size here only sets the proportions:
body diameter 1.0, total length 2.25.
"""
import bpy, bmesh, math, os, struct
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ASSETS = os.path.join(ROOT, "src", "HatterValuablesPack", "Assets")

R = 0.5            # body radius
LB = 1.55          # body length (rear neck to nose)
CONE = 0.25        # cone from the body's neck to the tail box
TAIL = 0.45        # tail box length
ZC = 0.68          # axis height above the floor
TOTAL = LB + CONE + TAIL
X0 = -TOTAL / 2    # tail end
XT = X0 + TAIL     # tail box front (cone end)
XN = XT + CONE     # body's rear neck
XF = XN + LB       # nose tip


def clear():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.lights, bpy.data.cameras):
        for block in list(coll):
            if block.users == 0:
                coll.remove(block)


def material(name, color, roughness, metallic=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    if not m.node_tree:
        m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    return m


def new_object(name, bm, mat, smooth=False):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = smooth
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.data.materials.append(mat)
    return ob


def lathe(name, profile, mat, sides=48, y=0.0, z=ZC):
    """A surface of revolution round the X axis through (y, z); profile = [(x, r)], r = 0 closes an end."""
    bm = bmesh.new()
    rings = []
    for x, r in profile:
        ring = []
        for i in range(sides):
            a = 2 * math.pi * i / sides
            ring.append(bm.verts.new((x, y + r * math.cos(a), z + r * math.sin(a))))
        rings.append(ring)
    for k in range(len(rings) - 1):
        for i in range(sides):
            j = (i + 1) % sides
            bm.faces.new((rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i]))
    for ring, flip in ((rings[0], True), (rings[-1], False)):
        if ring[0].co.y != ring[1].co.y or ring[0].co.z != ring[1].co.z:
            f = bm.faces.new(ring[::-1] if flip else ring)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return new_object(name, bm, mat, smooth=True)


def box(name, centre, size, mat, rot=None):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    m = Matrix.Diagonal((*size, 1.0))
    if rot is not None:
        m = rot.to_4x4() @ m
    bmesh.ops.transform(bm, matrix=Matrix.Translation(centre) @ m, verts=bm.verts)
    return new_object(name, bm, mat)


def rod(name, a, b, radius, mat, sides=8):
    a, b = Vector(a), Vector(b)
    d = b - a
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=sides, radius1=radius, radius2=radius, depth=d.length)
    rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=Matrix.Translation((a + b) / 2) @ rot, verts=bm.verts)
    return new_object(name, bm, mat)


def body_radius(x):
    """Egg profile: a full round nose, a longer taper towards the rear neck."""
    mid = XN + LB * 0.55                   # widest point, a little ahead of the middle
    if x >= mid:
        u = min(1.0, (x - mid) / (XF - mid))
        return R * max(0.0, 1 - u ** 2.3) ** (1 / 2.3)
    u = min(1.0, (mid - x) / (mid - XN))
    neck = 0.44 * R                        # the rear doesn't close: it flows into the cone
    return neck + (R - neck) * max(0.0, 1 - u ** 2.0) ** 0.5


def join(objects, name):
    bpy.ops.object.select_all(action="DESELECT")
    for ob in objects:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name
    ob.data.name = name
    return ob


def build():
    clear()
    casing = material("Bomb Casing", (0.028, 0.030, 0.024), 0.5, 0.3)
    tail_m = material("Bomb Tail", (0.30, 0.31, 0.29), 0.45, 0.75)
    fit = material("Bomb Fittings", (0.42, 0.42, 0.40), 0.35, 0.85)
    cradle = material("Bomb Cradle", (0.10, 0.10, 0.095), 0.6, 0.6)
    parts = []

    # body: egg profile, closed at the nose, open at the neck (the cone covers it)
    n = 64
    prof = [(XN + (XF - XN) * (1 - math.cos(math.pi * i / n)) / 2, 0) for i in range(n + 1)]
    prof = [(x, body_radius(x)) for x, _ in prof]
    prof[-1] = (XF, 0.0)
    parts.append(lathe("Body", prof, casing))

    # seam band near the nose, and a thinner one round the middle
    for t, w, h, m in ((0.86, 0.012, 0.010, fit), (0.47, 0.004, 0.0025, casing)):
        x = XN + LB * t
        r = body_radius(x) + h
        parts.append(lathe("Seam", [(x - w, r - h - 0.004), (x - w, r), (x + w, r), (x + w, r - h - 0.004)], m, sides=64))

    # cone from the neck back into the tail box, and a short tube inside the box
    rn = body_radius(XN)
    parts.append(lathe("Cone", [(XN + 0.02, rn - 0.005), (XN, rn), (XN - CONE * 0.45, 0.17), (XT, 0.15), (X0 + 0.05, 0.13), (X0 + 0.05, 0.0)], tail_m, sides=32))

    # tail box: four plates forming a square tube one body diameter across, with rim bars at both ends
    t = 0.014
    cx = X0 + TAIL / 2
    for dy, dz, sy, sz in ((0, R, 2 * R, t), (0, -R, 2 * R, t), (R, 0, t, 2 * R), (-R, 0, t, 2 * R)):
        parts.append(box("Tail Plate", (cx, dy, ZC + dz), (TAIL, sy, sz), tail_m))
    for x in (X0 + 0.012, XT - 0.012):
        for dy, dz, sy, sz in ((0, R, 2 * R + 0.03, 0.03), (0, -R, 2 * R + 0.03, 0.03), (R, 0, 0.03, 2 * R), (-R, 0, 0.03, 2 * R)):
            parts.append(box("Tail Rim", (x, dy, ZC + dz), (0.024, sy, sz), fit))
    # rivet line along the middle of each plate
    for dy, dz in ((0, R + t / 2), (0, -R - t / 2), (R + t / 2, 0), (-R - t / 2, 0)):
        for i in range(9):
            x = X0 + 0.04 + i * (TAIL - 0.08) / 8
            parts.append(box("Rivet", (x, dy * 1.0, ZC + dz), (0.012, 0.012 if dy == 0 else 0.006, 0.012 if dz == 0 else 0.006), fit))

    # four diagonal fins from the cone out to the box corners (an X seen from behind), reaching forward past the box
    corner = math.sqrt(2) * R
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        inner, outer = 0.15, corner - 0.01
        rot = Matrix.Rotation(a, 3, "X")
        mid_r = (inner + outer) / 2
        centre = Vector((cx + 0.03, 0, 0)) + rot @ Vector((0, mid_r, 0))
        centre.z += ZC
        parts.append(box("Fin", centre, (TAIL + 0.06, outer - inner, 0.012), tail_m, rot=rot))
        # triangular gusset forward of the box, from the fin's root to the cone
        root = rot @ Vector((0, 0.17, 0))
        tip = rot @ Vector((0, corner * 0.8, 0))
        parts.append(rod("Strut", (XT + 0.17, root.y, ZC + root.z), (XT - 0.02, tip.y, ZC + tip.z), 0.012, tail_m))

    # lugs: two on each side (as in the photo), one on top with a flat cover plate, a fuse stub, a small hook at the nose
    xm = XN + LB * 0.47
    for side in (1, -1):
        for dz in (0.14, -0.14):
            y = side * (math.sqrt(max(0.0, body_radius(xm) ** 2 - dz ** 2)) + 0.012)
            parts.append(box("Lug", (xm, y, ZC + dz), (0.07, 0.03, 0.035), fit))
            parts.append(box("Lug Arm", (xm, y, ZC + dz), (0.20, 0.012, 0.012), fit))
    rt = body_radius(XN + LB * 0.55)
    parts.append(box("Top Cover", (XN + LB * 0.42, 0, ZC + rt - 0.005), (0.20, 0.13, 0.04), fit))
    parts.append(box("Top Cover Lip", (XN + LB * 0.42, 0, ZC + rt + 0.018), (0.16, 0.10, 0.012), fit))
    parts.append(rod("Fuse", (XN + LB * 0.30, 0, ZC + body_radius(XN + LB * 0.30) - 0.01),
                     (XN + LB * 0.30, 0, ZC + body_radius(XN + LB * 0.30) + 0.07), 0.02, fit))
    parts.append(box("Fuse Cap", (XN + LB * 0.30, 0, ZC + body_radius(XN + LB * 0.30) + 0.075), (0.06, 0.035, 0.015), fit))
    xh = XN + LB * 0.93
    parts.append(rod("Nose Hook", (xh, 0.0, ZC + body_radius(xh) - 0.01), (xh + 0.015, 0.0, ZC + body_radius(xh) + 0.04), 0.008, fit))
    xb = XN + LB * 0.55
    parts.append(box("Belly Lug", (xb, 0, ZC - body_radius(xb) - 0.01), (0.16, 0.08, 0.04), fit))

    # cradle: two bottom rails, two saddle rails under the body, posts and X braces between them
    c0, c1 = XN + LB * 0.22, XN + LB * 0.82
    w_bot, w_top = 0.30, 0.22
    z_top = ZC - body_radius((c0 + c1) / 2) * math.cos(math.asin(w_top / R)) - 0.01
    for s in (1, -1):
        parts.append(box("Rail", ((c0 + c1) / 2, s * w_bot, 0.02), (c1 - c0 + 0.08, 0.035, 0.035), cradle))
        parts.append(box("Saddle Rail", ((c0 + c1) / 2, s * w_top, z_top), (c1 - c0, 0.03, 0.03), cradle))
        for x in (c0, c1):
            parts.append(rod("Post", (x, s * w_bot, 0.03), (x, s * w_top, z_top), 0.015, cradle))
        k = 4
        for i in range(k):
            xa = c0 + (c1 - c0) * i / k
            xb2 = c0 + (c1 - c0) * (i + 1) / k
            a, b = (xa, s * w_bot, 0.03), (xb2, s * w_top, z_top)
            if i % 2:
                a, b = (xa, s * w_top, z_top), (xb2, s * w_bot, 0.03)
            parts.append(rod("Brace", a, b, 0.011, cradle))
    for x in (c0, c1):
        parts.append(box("Cross Rail", (x, 0, 0.02), (0.035, 2 * w_bot, 0.035), cradle))
        # saddle: a curved pad hugging the body
        pts = []
        for i in range(9):
            y = -w_top + 2 * w_top * i / 8
            zz = ZC - math.sqrt(max(0.0, body_radius(x) ** 2 - y * y))
            pts.append((y, zz))
        for i in range(8):
            (ya, za), (yb, zb) = pts[i], pts[i + 1]
            parts.append(rod("Saddle", (x, ya, za - 0.01), (x, yb, zb - 0.01), 0.018, cradle, sides=6))
        parts.append(box("Saddle Base", (x, 0, (z_top + pts[4][1]) / 2), (0.03, 2 * w_top, max(0.02, pts[4][1] - z_top)), cradle))

    ob = join(parts, "Nuke")
    for p in ob.data.polygons:   # smooth only the body and cone (big curved surfaces)
        p.use_smooth = ob.material_slots[p.material_index].material.name in ("Bomb Casing",) or p.use_smooth
    return ob


# ---------------------------------------------------------------- export (same format as HatterWeaponsPack)

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


def export(objects, path):
    data = bytearray(b"HWM1") + struct.pack("<i", len(objects))
    for ob in objects:
        data += export_object(ob)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(data)
    return len(data)


# ---------------------------------------------------------------- previews

def render(path, cam_loc, target, res, lens=50):
    scene = bpy.context.scene
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    try:
        world.use_nodes = True
        world.node_tree.nodes["Background"].inputs[0].default_value = (0.75, 0.75, 0.74, 1)
        world.node_tree.nodes["Background"].inputs[1].default_value = 0.6
    except Exception:
        pass
    for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
        try:
            scene.render.engine = engine
            break
        except Exception:
            continue
    scene.render.resolution_x, scene.render.resolution_y = res
    for ob in [o for o in scene.objects if o.name.startswith("Preview")]:
        bpy.data.objects.remove(ob, do_unlink=True)
    cd = bpy.data.cameras.new("Preview Camera")
    cam = bpy.data.objects.new("Preview Camera", cd)
    scene.collection.objects.link(cam)
    cd.lens = lens
    cam.location = cam_loc
    cam.rotation_euler = (Vector(target) - Vector(cam_loc)).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    for loc, energy in (((2.0, -2.5, 3.0), 400), ((-3.0, 1.5, 2.0), 150)):
        ld = bpy.data.lights.new("Preview Light", "AREA")
        ld.energy = energy
        ld.size = 3
        lo = bpy.data.objects.new("Preview Light", ld)
        lo.location = loc
        lo.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        scene.collection.objects.link(lo)
    fd = bpy.data.meshes.new("Preview Floor")
    bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=8); bm.to_mesh(fd); bm.free()
    floor = bpy.data.objects.new("Preview Floor", fd)
    floor.data.materials.append(material("Preview Floor", (0.8, 0.8, 0.78), 0.8))
    scene.collection.objects.link(floor)
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    for ob in [o for o in scene.objects if o.name.startswith("Preview")]:
        bpy.data.objects.remove(ob, do_unlink=True)


nuke = build()
size = export([nuke], os.path.join(ASSETS, "nuke.mesh"))
render(os.path.join(HERE, "nuke_preview.png"), (0.9, -3.6, 1.35), (0.05, 0, 0.6), (1280, 820), lens=40)
render(os.path.join(HERE, "nuke_icon.png"), (1.6, -2.7, 1.9), (0.0, 0, 0.55), (256, 256), lens=38)
blend = os.path.join(ROOT, "blender", "nuke.blend")
os.makedirs(os.path.dirname(blend), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=blend)
print("nuke exported", size, "bytes")
