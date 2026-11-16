"""Run inside Blender (Scripting tab, or via the Blender MCP with runpy.run_path) to build the Orb Cannon.

Builds blender/orb_cannon.blend, exports src/HatterWeaponsPack/Assets/orb_cannon.mesh ("HWM1", read by MeshLibrary)
and renders tools/orb_cannon_preview.png (unloaded) and tools/orb_cannon_preview_loaded.png (traces lit pink).

Design: an original shoulder-fired recoilless-style tube in the spirit of the PF-98: a thick dark-gray tube with a
flared rear vent, a slightly wider central sleeve, a heavy muzzle collar, an optic box on top, a pistol grip with a
trigger, a front grip and a shoulder pad underneath. Circuit-board traces run along the tube: four main traces
(two per side), each unbroken from the rear flare to the muzzle collar with a few 45-degree jogs, and four short
thin branches ending in round pads. The traces are their own material ("OC Trace"), which the mod swaps for a pink
glow while the cannon is loaded.

Objects (the mod scales the tube to the game's small soul orb at runtime, so the parts are split by where they sit):
  - OC_Tube:   tube, sleeve, flare, collar, bore and the traces (materials OC Tube, OC Sleeve, OC Metal, OC Bore, OC Trace)
  - OC_Top:    the optic box and its mount (OC Optic, OC Lens)
  - OC_Bottom: pistol grip, trigger, front grip, shoulder pad (OC Grip)
Axes as the other weapons: the muzzle points along Blender -Y (Unity +Z), the grip is near the origin.
Sizes (metres): tube outer radius R = 0.12 (nominal; the mod rescales it to the small orb's radius + 2 cm), rear vent
mouth at y = +0.56, muzzle face at y = -0.70.
"""
import bpy, bmesh, math, os, struct
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ASSETS = os.path.join(ROOT, "src", "HatterWeaponsPack", "Assets")

R = 0.12            # tube outer radius (nominal)
RS = R + 0.012      # sleeve radius
WALL = 0.012
SLEEVE = (0.13, -0.20)          # sleeve from y to y
TUBE = (0.45, -0.62)            # plain tube between the flare and the collar
MUZZLE = -0.70
REAR = 0.56
SIDES = 64

TRACE_W, BRANCH_W, TRACE_H = 0.007, 0.0035, 0.0018
PAD_R = 0.0065


# ---------------------------------------------------------------- scene and materials

def clear():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.lights, bpy.data.cameras):
        for block in list(coll):
            if block.users == 0:
                coll.remove(block)


def material(name, color, roughness, metallic=0.0, emission=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    if not m.node_tree:
        m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    for key in ("Emission Color", "Emission"):
        if key in bsdf.inputs:
            bsdf.inputs[key].default_value = (*color, 1.0)
            break
    if "Emission Strength" in bsdf.inputs:
        bsdf.inputs["Emission Strength"].default_value = emission
    return m


def new_object(name, bm, mats):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    for m in mats:
        ob.data.materials.append(m)
    return ob


# ---------------------------------------------------------------- lathes (revolved round the Y axis)

def lathe(bm, profile, mat_index, sides=SIDES):
    """Revolves a (radius, y) polyline round Y. Each segment gets its own vertices (crisp edges between segments,
    smooth round the circumference). Going towards -y with growing radius on the outside gives outward normals."""
    for k in range(len(profile) - 1):
        (r0, y0), (r1, y1) = profile[k], profile[k + 1]
        ring0, ring1 = [], []
        for j in range(sides):
            a = 2 * math.pi * j / sides
            c, s = math.cos(a), math.sin(a)
            ring0.append(bm.verts.new((r0 * c, y0, r0 * s)))
            ring1.append(bm.verts.new((r1 * c, y1, r1 * s)))
        for j in range(sides):
            jn = (j + 1) % sides
            f = bm.faces.new((ring0[j], ring0[jn], ring1[jn], ring1[j]))
            f.material_index = mat_index
            f.smooth = abs(r1 - r0) < 1e-6 or abs(y1 - y0) > 1e-6   # round surfaces smooth, flat rings too (one normal)


# ---------------------------------------------------------------- circuit traces on the tube

def surface_radius(y):
    """The tube's surface radius at y (on the sleeve it's wider; a short ramp climbs the step)."""
    top, bottom = SLEEVE
    ramp = 0.004
    if bottom <= y <= top:
        return RS
    if top < y < top + ramp:
        return RS - (RS - R) * (y - top) / ramp
    if bottom - ramp < y < bottom:
        return RS - (RS - R) * (bottom - y) / ramp
    return R


def trace_path(theta0_deg, jogs, y_start=0.44, y_end=-0.61):
    """Control points (y, theta in radians) from y_start to y_end: straight runs along the tube, and at each jog
    a 45-degree diagonal (the arc length round the tube equals the distance along it)."""
    pts = [(y_start, math.radians(theta0_deg))]
    th = math.radians(theta0_deg)
    for y, dth_deg in jogs:
        dth = math.radians(dth_deg)
        pts.append((y, th))
        th += dth
        pts.append((y - R * abs(dth), th))
    pts.append((y_end, th))
    return pts


def theta_at(pts, y):
    for (ya, ta), (yb, tb) in zip(pts, pts[1:]):
        if ya >= y >= yb:
            return ta if abs(ya - yb) < 1e-9 else ta + (tb - ta) * (ya - y) / (ya - yb)
    return pts[-1][1]


def surface_point(y, th, lift=0.0):
    r = surface_radius(y) + lift
    return Vector((r * math.cos(th), y, r * math.sin(th)))


def resample(pts, step=0.006):
    out = [pts[0]]
    for (ya, ta), (yb, tb) in zip(pts, pts[1:]):
        length = math.hypot(ya - yb, R * (tb - ta))
        n = max(1, int(math.ceil(length / step)))
        for i in range(1, n + 1):
            t = i / n
            out.append((ya + (yb - ya) * t, ta + (tb - ta) * t))
    return out


def strip(bm, pts, width, mat_index):
    """A raised flat-topped strip along the surface (a box section), closed at both ends."""
    samples = resample(pts)
    rings = []
    for i, (y, th) in enumerate(samples):
        p = surface_point(y, th)
        prev_p = surface_point(*samples[max(0, i - 1)])
        next_p = surface_point(*samples[min(len(samples) - 1, i + 1)])
        t = (next_p - prev_p)
        if t.length < 1e-9:
            t = Vector((0, -1, 0))
        t.normalize()
        n = Vector((math.cos(th), 0, math.sin(th)))
        side = t.cross(n).normalized() * (width / 2)
        base = p - n * 0.0006
        top = p + n * TRACE_H
        rings.append([bm.verts.new(base - side), bm.verts.new(base + side), bm.verts.new(top + side), bm.verts.new(top - side)])
    for a, b in zip(rings, rings[1:]):
        for k in range(4):
            kn = (k + 1) % 4
            f = bm.faces.new((a[k], b[k], b[kn], a[kn]))   # outward-facing
            f.material_index = mat_index
            f.smooth = False
    for ring, flip in ((rings[0], False), (rings[-1], True)):
        f = bm.faces.new(list(reversed(ring)) if flip else ring)
        f.material_index = mat_index
    return samples[-1]


def pad(bm, y, th, mat_index, sides=14):
    n = Vector((math.cos(th), 0, math.sin(th)))
    u = Vector((0, 1, 0))
    v = n.cross(u).normalized()
    centre = surface_point(y, th)
    bottom, top = [], []
    for j in range(sides):
        a = 2 * math.pi * j / sides
        off = (u * math.cos(a) + v * math.sin(a)) * PAD_R
        bottom.append(bm.verts.new(centre + off - n * 0.0006))
        top.append(bm.verts.new(centre + off + n * TRACE_H))
    for j in range(sides):
        jn = (j + 1) % sides
        f = bm.faces.new((bottom[j], bottom[jn], top[jn], top[j]))
        f.material_index = mat_index
    f = bm.faces.new(top)
    f.material_index = mat_index


# four main traces, two per side (theta 0 = the cannon's right side, 90 = top), and four branches
TRACES = [
    (28, [(0.30, 14), (-0.02, -14), (-0.40, 10)]),
    (152, [(0.36, -12), (0.05, 12), (-0.35, -14)]),
    (212, [(0.25, 12), (-0.10, -12)]),
    (332, [(0.38, -10), (0.00, 12), (-0.45, -12)]),
]
# (trace, y where it leaves the trace, turn in degrees, straight run in metres)
BRANCHES = [
    (0, 0.20, 12, 0.05),
    (3, -0.25, -12, 0.04),
    (1, -0.15, 12, 0.05),
    (2, 0.35, -10, 0.04),
]


# ---------------------------------------------------------------- boxes

def box(bm, centre, size, mat_index, rot_x_deg=0.0):
    m = Matrix.Translation(centre) @ Matrix.Rotation(math.radians(rot_x_deg), 4, "X") @ Matrix.Diagonal((*size, 1.0))
    res = bmesh.ops.create_cube(bm, size=1.0, matrix=m)
    for v in res["verts"]:
        for f in v.link_faces:
            f.material_index = mat_index
            f.smooth = False


def cylinder_y(bm, centre, radius, length, mat_index, sides=24):
    """A short cylinder along Y (the optic's lens)."""
    y0, y1 = centre[1] + length / 2, centre[1] - length / 2
    prof = [(0.0, y0), (radius, y0), (radius, y1), (0.0, y1)]
    start = len(bm.verts)
    for k in range(len(prof) - 1):
        (r0, ya), (r1, yb) = prof[k], prof[k + 1]
        ring0, ring1 = [], []
        for j in range(sides):
            a = 2 * math.pi * j / sides
            c, s = math.cos(a), math.sin(a)
            ring0.append(bm.verts.new((centre[0] + r0 * c, ya, centre[2] + r0 * s)))
            ring1.append(bm.verts.new((centre[0] + r1 * c, yb, centre[2] + r1 * s)))
        for j in range(sides):
            jn = (j + 1) % sides
            f = bm.faces.new((ring0[j], ring0[jn], ring1[jn], ring1[j]))
            f.material_index = mat_index
            f.smooth = abs(yb - ya) > 1e-6


# ---------------------------------------------------------------- the cannon

def build():
    clear()
    m_tube = material("OC Tube", (0.11, 0.115, 0.125), 0.5, 0.35)
    m_sleeve = material("OC Sleeve", (0.085, 0.088, 0.095), 0.55, 0.35)
    m_metal = material("OC Metal", (0.05, 0.05, 0.055), 0.35, 0.7)
    m_bore = material("OC Bore", (0.012, 0.012, 0.014), 0.9, 0.0)
    m_trace = material("OC Trace", (0.008, 0.008, 0.009), 0.3, 0.2)
    m_optic = material("OC Optic", (0.04, 0.04, 0.045), 0.6, 0.3)
    m_lens = material("OC Lens", (0.25, 0.03, 0.14), 0.1, 0.0)
    m_grip = material("OC Grip", (0.03, 0.03, 0.032), 0.85, 0.0)

    # the tube and everything round it
    bm = bmesh.new()
    lathe(bm, [(R, TUBE[0]), (R, TUBE[1])], 0)                                     # tube
    lathe(bm, [(R, SLEEVE[0]), (RS, SLEEVE[0]), (RS, SLEEVE[1]), (R, SLEEVE[1])], 1)  # sleeve
    lathe(bm, [(R, TUBE[1]), (R + 0.018, TUBE[1]), (R + 0.018, MUZZLE), (R - WALL, MUZZLE)], 2)   # muzzle collar
    lathe(bm, [(R - WALL, MUZZLE), (R - WALL, TUBE[0])], 3)                           # bore (normals inward)
    lathe(bm, [(R - WALL, TUBE[0]), (R + 0.025, REAR), (R + 0.035, REAR), (R, TUBE[0])], 2)        # rear flare
    # thin bands at the sleeve's ends and behind the collar
    for y in (SLEEVE[0] + 0.012, SLEEVE[1] - 0.012, TUBE[1] + 0.03):
        lathe(bm, [(R, y + 0.004), (R + 0.004, y + 0.004), (R + 0.004, y - 0.004), (R, y - 0.004)], 2)

    paths = [trace_path(th, jogs) for th, jogs in TRACES]
    for p in paths:
        strip(bm, p, TRACE_W, 4)
    for ti, yb, turn, run in BRANCHES:
        th0 = theta_at(paths[ti], yb)
        dth = math.radians(turn)
        y1 = yb - R * abs(dth)
        th1 = th0 + dth
        bp = [(yb, th0), (y1, th1), (y1 - run, th1)]
        strip(bm, bp, BRANCH_W, 4)
        pad(bm, y1 - run - PAD_R * 0.6, th1, 4)
    tube = new_object("OC_Tube", bm, [m_tube, m_sleeve, m_metal, m_bore, m_trace])

    # on top: the optic box on a mount
    bm = bmesh.new()
    box(bm, Vector((0, -0.035, RS + 0.009)), (0.034, 0.08, 0.02), 0)
    box(bm, Vector((0, -0.035, RS + 0.045)), (0.05, 0.15, 0.055), 0)
    box(bm, Vector((0, 0.045, RS + 0.05)), (0.04, 0.012, 0.04), 0)      # rear eyepiece hood
    cylinder_y(bm, (0, -0.11, RS + 0.045), 0.018, 0.012, 1)
    top = new_object("OC_Top", bm, [m_optic, m_lens])

    # underneath: pistol grip and trigger, front grip, shoulder pad
    bm = bmesh.new()
    box(bm, Vector((0, 0.045, -(RS + 0.06))), (0.032, 0.045, 0.12), 0, rot_x_deg=-14)    # pistol grip, raked back
    box(bm, Vector((0, 0.03, -(RS + 0.008))), (0.04, 0.09, 0.016), 0)                    # grip mount
    box(bm, Vector((0, -0.005, -(RS + 0.024))), (0.007, 0.012, 0.03), 0, rot_x_deg=10)   # trigger
    box(bm, Vector((0, -0.016, -(RS + 0.046))), (0.008, 0.05, 0.007), 0)                 # trigger guard
    box(bm, Vector((0, -0.34, -(R + 0.05))), (0.03, 0.036, 0.09), 0, rot_x_deg=6)        # front grip
    box(bm, Vector((0, -0.34, -(R + 0.006))), (0.036, 0.05, 0.016), 0)                   # its mount
    box(bm, Vector((0, 0.31, -(R + 0.014))), (0.05, 0.15, 0.03), 0)                      # shoulder pad
    bottom = new_object("OC_Bottom", bm, [m_grip])
    return [tube, top, bottom], m_trace


# ---------------------------------------------------------------- export (same format as tools/export_blender.py)

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

def setup_render():
    scene = bpy.context.scene
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    if not world.node_tree:
        world.use_nodes = True
    try:
        world.node_tree.nodes["Background"].inputs[0].default_value = (0.32, 0.33, 0.35, 1)
        world.node_tree.nodes["Background"].inputs[1].default_value = 0.35
    except Exception:
        pass
    for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
        try:
            scene.render.engine = engine
            break
        except Exception:
            continue
    scene.render.resolution_x, scene.render.resolution_y = 1400, 800
    return scene


def shot(scene, cam_loc, target, out):
    for ob in [o for o in scene.objects if o.name.startswith("Preview")]:
        bpy.data.objects.remove(ob, do_unlink=True)
    cd = bpy.data.cameras.new("Preview Camera")
    cam = bpy.data.objects.new("Preview Camera", cd)
    scene.collection.objects.link(cam)
    cd.lens = 50
    cam.location = cam_loc
    cam.rotation_euler = (Vector(target) - Vector(cam_loc)).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    for loc, energy in (((1.6, -0.6, 1.4), 45), ((-1.4, 0.8, 0.6), 18), ((0.4, 1.5, -0.5), 10)):
        ld = bpy.data.lights.new("Preview Light", "AREA")
        ld.energy = energy
        ld.size = 2.0
        lo = bpy.data.objects.new("Preview Light", ld)
        lo.location = loc
        lo.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        scene.collection.objects.link(lo)
    scene.render.filepath = out
    bpy.ops.render.render(write_still=True)


def previews(trace_mat):
    scene = setup_render()
    view = ((2.3, -0.55, 0.75), (0, -0.07, -0.03))
    shot(scene, *view, os.path.join(HERE, "orb_cannon_preview.png"))
    bsdf = trace_mat.node_tree.nodes.get("Principled BSDF")
    old = tuple(bsdf.inputs["Base Color"].default_value)
    bsdf.inputs["Base Color"].default_value = (1.0, 0.25, 0.65, 1)
    for key in ("Emission Color", "Emission"):
        if key in bsdf.inputs:
            bsdf.inputs[key].default_value = (1.0, 0.25, 0.65, 1)
            break
    if "Emission Strength" in bsdf.inputs:
        bsdf.inputs["Emission Strength"].default_value = 2.5
    shot(scene, *view, os.path.join(HERE, "orb_cannon_preview_loaded.png"))
    shot(scene, (-2.1, -1.0, 0.55), (0, -0.07, -0.03), os.path.join(HERE, "orb_cannon_preview_left.png"))
    bsdf.inputs["Base Color"].default_value = old
    if "Emission Strength" in bsdf.inputs:
        bsdf.inputs["Emission Strength"].default_value = 0.0
    for ob in [o for o in scene.objects if o.name.startswith("Preview")]:
        bpy.data.objects.remove(ob, do_unlink=True)


objects, trace = build()
size = export(objects, os.path.join(ASSETS, "orb_cannon.mesh"))
previews(trace)
blend = os.path.join(ROOT, "blender", "orb_cannon.blend")
os.makedirs(os.path.dirname(blend), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=blend)
print("orb_cannon exported", size, "bytes")
