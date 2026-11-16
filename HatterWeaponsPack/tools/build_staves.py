"""Run inside Blender (Scripting tab, or via the Blender MCP with runpy.run_path) to build the staves.

The staves share one model and differ only in their crystals' colour (the colour of the matching wand's tip). For each
variant in VARIANTS it builds blender/<name>.blend, exports src/HatterWeaponsPack/Assets/<name>.mesh ("HWM1", read by
MeshLibrary) and renders tools/<name>_preview.png and tools/<name>_preview_head.png.

Design: a long, slightly crooked dark-wood staff, thin at the foot and thickening towards the head, with two thinner vines twisting up it, a root-like foot, and a head
where a single thick branch grows out of the shaft and spirals up round a floating crystal core, with little twigs
sprouting off it (plus two small shards).
No metal. Objects (Ember Staff shown; the Frost Staff uses FrostStaff_ and FS):
  - EmberStaff_Body: everything but the core crystal (materials ES Wood, ES Vine, Glow ES Shard)
  - EmberStaff_Tip:  the core crystal (Glow ES Core), lit up by the mod while the staff is ready
The glow materials get their own names per variant: the mod caches materials by name.
Axes as the other weapons: the staff points along Blender -Y (Unity +Z), the grip is at the origin.
Sizes (metres): foot at y = +0.38 (roots to +0.415), the shaft narrows into the branch at y = -0.60, crystal centre at y = -0.75
(13 cm long), the branch closes over it at about y = -0.885.
"""
import bpy, bmesh, math, os, struct
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ASSETS = os.path.join(ROOT, "src", "HatterWeaponsPack", "Assets")

# name: (object prefix, glow material prefix, crystal colour = the wand's tip colour from its .mesh)
VARIANTS = {
    "ember_staff": ("EmberStaff", "ES", (0.9, 0.02, 0.01)),    # Ember Wand: Glow WD Tip
    "frost_staff": ("FrostStaff", "FS", (0.1, 0.85, 1.0)),     # Frost Wand: Glow WD FrostTip
    "lightning_staff": ("LightningStaff", "LS", (1.0, 0.88, 0.1)),   # Lightning Wand: Glow WD LightningTip
}

CRYSTAL_Y = -0.75


# ---------------------------------------------------------------- scene and materials

def clear():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.lights, bpy.data.cameras):
        for block in list(coll):
            if block.users == 0:
                coll.remove(block)


def material(name, color, roughness, metallic=0.0, emission=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    if not m.node_tree:   # older Blender: node trees are opt-in
        m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    if emission > 0:
        for key in ("Emission Color", "Emission"):
            if key in bsdf.inputs:
                bsdf.inputs[key].default_value = (*color, 1.0)
                break
        if "Emission Strength" in bsdf.inputs:
            bsdf.inputs["Emission Strength"].default_value = emission
    return m


# ---------------------------------------------------------------- tubes along paths (tapering, as meshes)

def tube(name, points, radii, mat, sides=10, twist=0.0):
    """A closed tube through the points (Vector list) with a radius per point; capped at both ends."""
    bm = bmesh.new()
    rings = []
    n = len(points)
    prev_normal = None
    for i, p in enumerate(points):
        t = (points[min(i + 1, n - 1)] - points[max(i - 1, 0)]).normalized()
        if prev_normal is None:
            ref = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
            normal = (ref - t * ref.dot(t)).normalized()
        else:
            normal = (prev_normal - t * prev_normal.dot(t)).normalized()   # parallel transport: no flipping
        prev_normal = normal
        binormal = t.cross(normal)
        ring = []
        for s in range(sides):
            a = 2 * math.pi * s / sides + twist * i
            ring.append(bm.verts.new(p + (normal * math.cos(a) + binormal * math.sin(a)) * radii[i]))
        rings.append(ring)
    for i in range(n - 1):
        for s in range(sides):
            a, b = rings[i][s], rings[i][(s + 1) % sides]
            c, d = rings[i + 1][(s + 1) % sides], rings[i + 1][s]
            bm.faces.new((a, b, c, d))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    me = bpy.data.meshes.new(name)
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    for poly in me.polygons:
        poly.use_smooth = True
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    ob.data.materials.append(mat)
    return ob


def smooth_path(ctrl, steps=6):
    """Catmull-Rom through control points."""
    pts = []
    c = [ctrl[0]] + ctrl + [ctrl[-1]]
    for i in range(1, len(c) - 2):
        p0, p1, p2, p3 = c[i - 1], c[i], c[i + 1], c[i + 2]
        for k in range(steps):
            t = k / steps
            t2, t3 = t * t, t * t * t
            pts.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    pts.append(ctrl[-1])
    return pts


def lerp_radii(pts, r0, r1, bulges=()):
    n = len(pts)
    out = []
    for i in range(n):
        t = i / (n - 1)
        r = r0 + (r1 - r0) * t
        for (at, width, amount) in bulges:           # knots: local swellings
            r += amount * math.exp(-((t - at) / width) ** 2)
        out.append(r)
    return out


def crystal(name, centre, length, radius, mat, sides=6, spin=0.0):
    """An elongated bipyramid (a cut gem shape) along Y."""
    bm = bmesh.new()
    top = bm.verts.new(centre + Vector((0, -length * 0.62, 0)))
    bottom = bm.verts.new(centre + Vector((0, length * 0.38, 0)))
    ring_hi, ring_lo = [], []
    for s in range(sides):
        a = 2 * math.pi * s / sides + spin
        off = Vector((math.cos(a) * radius, 0, math.sin(a) * radius))
        ring_hi.append(bm.verts.new(centre + off + Vector((0, -length * 0.16, 0))))
        ring_lo.append(bm.verts.new(centre + off * 0.92 + Vector((0, length * 0.12, 0))))
    for s in range(sides):
        n = (s + 1) % sides
        bm.faces.new((top, ring_hi[n], ring_hi[s]))
        bm.faces.new((ring_hi[s], ring_hi[n], ring_lo[n], ring_lo[s]))
        bm.faces.new((ring_lo[s], ring_lo[n], bottom))
    me = bpy.data.meshes.new(name)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    ob.data.materials.append(mat)
    return ob


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


# ---------------------------------------------------------------- the staff

def build(prefix, mat_prefix, crystal_colour):
    clear()
    wood = material("ES Wood", (0.13, 0.075, 0.04), 0.75)
    vine = material("ES Vine", (0.24, 0.14, 0.075), 0.7)
    core_mat = material("Glow %s Core" % mat_prefix, crystal_colour, 0.3, emission=2.0)
    shard_mat = material("Glow %s Shard" % mat_prefix, crystal_colour, 0.3, emission=1.5)

    parts = []
    V = Vector

    # main shaft: foot to where the head splits, gently crooked, with a couple of knots
    shaft_ctrl = [V((0.000, 0.38, 0.000)), V((0.006, 0.22, -0.004)), V((-0.004, 0.05, 0.003)), V((0.005, -0.12, 0.006)),
                  V((-0.006, -0.30, -0.002)), V((0.004, -0.46, -0.006)), V((0.000, -0.62, 0.000))]
    shaft = smooth_path(shaft_ctrl, 8)
    # thin at the foot, thickening towards the head (a gentle curve, with a couple of knots)
    shaft_radii = []
    for i in range(len(shaft)):
        t = i / (len(shaft) - 1)
        r = 0.0125 + 0.0155 * (t ** 1.3)
        r += 0.003 * math.exp(-((t - 0.33) / 0.04) ** 2) + 0.004 * math.exp(-((t - 0.7) / 0.05) ** 2)
        if t > 0.9:
            r *= 1.0 - 0.1 * ((t - 0.9) / 0.1)      # eases into the spiral branch, so the wood flows on
        shaft_radii.append(r)
    parts.append(tube("Shaft", shaft, shaft_radii, wood, 12))

    # two vines twisting up the shaft, thinning out at both ends
    for k, phase in enumerate((0.0, math.pi)):
        pts = []
        n = 70
        for i in range(n + 1):
            t = i / n
            y = 0.30 - 0.90 * t
            a = phase + t * 2 * math.pi * 1.6
            idx = min(len(shaft) - 1, int(t * (len(shaft) - 1) * 0.9 + 0.08 * (len(shaft) - 1)))
            centre = shaft[idx]
            r = shaft_radii[idx] + 0.002              # hugs the shaft as it thickens
            pts.append(V((centre.x + math.cos(a) * r, y, centre.z + math.sin(a) * r)))
        radii = [0.0035 + 0.0055 * math.sin(math.pi * i / n) for i in range(n + 1)]
        parts.append(tube("Vine %d" % k, pts, radii, vine, 8))

    # foot: three short root-like splits
    for k in range(3):
        a = 2 * math.pi * k / 3 + 0.3
        d = V((math.cos(a), 0, math.sin(a)))
        ctrl = [V((0, 0.33, 0)), V((0, 0.37, 0)) + d * 0.012, V((0, 0.40, 0)) + d * 0.028, V((0, 0.415, 0)) + d * 0.04]
        pts = smooth_path(ctrl, 5)
        parts.append(tube("Root %d" % k, pts, lerp_radii(pts, 0.011, 0.0025), wood, 8))

    # head: a single branch grows out of the shaft top and spirals up round the crystal (about 1.75 turns), widening
    # to cradle it and closing in over its tip, where it ends in a small hook
    turns, a0 = 1.75, 0.4
    y_start, y_end = -0.60, -0.885   # starts inside the shaft's narrowed top

    def smooth(e0, e1, x):
        x = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
        return x * x * (3 - 2 * x)

    def spiral(t):
        # leaves the shaft along its axis, swings out to cradle the crystal, closes in again over its tip
        r = 0.064 * smooth(0.04, 0.34, t) * (1.0 - 0.8 * smooth(0.78, 1.0, t))
        a = a0 + t * 2 * math.pi * turns
        return V((math.cos(a) * r, y_start + (y_end - y_start) * t, math.sin(a) * r)), a

    n = 90
    pts = [spiral(i / n)[0] for i in range(n + 1)]
    # the hook: the tip turns back down a little towards the crystal
    end, end_a = spiral(1.0)
    pts.append(end + V((math.cos(end_a + 0.6) * 0.008, 0.012, math.sin(end_a + 0.6) * 0.008)))
    radii = lerp_radii(pts, 0.0255, 0.007, [(0.12, 0.06, 0.003), (0.5, 0.05, 0.002)])
    parts.append(tube("Spiral Branch", pts, radii, wood, 12))

    # thin little twigs sprouting off the spiral, pointing outwards and up (some with a tiny fork)
    for k, (t, length, fork) in enumerate(((0.16, 0.05, True), (0.30, 0.038, False), (0.44, 0.055, True),
                                           (0.58, 0.035, False), (0.70, 0.045, True), (0.84, 0.03, False))):
        base, ang = spiral(t)
        out = V((math.cos(ang), 0, math.sin(ang)))
        along = V((-math.sin(ang), 0, math.cos(ang))) * (0.35 if k % 2 == 0 else -0.35)
        d = (out * 0.85 + V((0, -0.55, 0)) + along).normalized()        # -Y is up the staff
        bend = V((0, -0.012, 0))
        ctrl = [base, base + d * length * 0.5 + bend * 0.5, base + d * length + bend]
        twig = smooth_path(ctrl, 5)
        parts.append(tube("Twig %d" % k, twig, lerp_radii(twig, 0.0045, 0.0009), vine, 6))
        if fork:
            at = twig[len(twig) // 2]
            fd = (d + along * 1.6 + V((0, -0.3, 0))).normalized()
            small = smooth_path([at, at + fd * length * 0.25, at + fd * length * 0.45 + V((0, -0.004, 0))], 4)
            parts.append(tube("Twig %d fork" % k, small, lerp_radii(small, 0.0028, 0.0007), vine, 6))

    # two small shards floating beside the core, each on the side away from the branch at its height
    for k, (dy, length) in enumerate(((0.030, 0.026), (-0.035, 0.021))):
        y = CRYSTAL_Y + dy
        t = (y - y_start) / (y_end - y_start)
        ang = spiral(t)[1] + math.pi
        c = V((math.cos(ang) * 0.042, y, math.sin(ang) * 0.042))
        parts.append(crystal("Shard %d" % k, c, length, length * 0.27, shard_mat, spin=0.4 + k))

    body = join(parts, prefix + "_Body")
    tip = crystal(prefix + "_Tip", V((0, CRYSTAL_Y, 0)), 0.13, 0.027, core_mat)
    return body, tip


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


def export(objects, path):
    data = bytearray(b"HWM1") + struct.pack("<i", len(objects))
    for ob in objects:
        data += export_object(ob)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(data)
    return len(data)


# ---------------------------------------------------------------- preview

def preview(path, head_path):
    """Two renders: the whole staff, and a close-up of the head."""
    scene = bpy.context.scene
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    try:
        world.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.05, 0.06, 1)
    except Exception:
        pass
    for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
        try:
            scene.render.engine = engine
            break
        except Exception:
            continue
    scene.render.resolution_x, scene.render.resolution_y = 1200, 800

    def shot(cam_loc, target, out):
        for ob in [o for o in scene.objects if o.name.startswith("Preview")]:
            bpy.data.objects.remove(ob, do_unlink=True)
        cd = bpy.data.cameras.new("Preview Camera")
        cam = bpy.data.objects.new("Preview Camera", cd)
        scene.collection.objects.link(cam)
        cd.lens = 50
        cam.location = cam_loc
        cam.rotation_euler = (Vector(target) - Vector(cam_loc)).to_track_quat("-Z", "Y").to_euler()
        scene.camera = cam
        for loc, energy in (((1.0, -1.2, 1.2), 18), ((-1.0, 0.5, 0.6), 7)):
            ld = bpy.data.lights.new("Preview Light", "AREA")
            ld.energy = energy
            ld.size = 1.5
            lo = bpy.data.objects.new("Preview Light", ld)
            lo.location = loc
            lo.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
            scene.collection.objects.link(lo)
        scene.render.filepath = out
        bpy.ops.render.render(write_still=True)

    shot((1.75, -0.35, 0.75), (0, -0.25, 0), path)
    shot((0.30, -0.60, 0.20), (0, -0.76, 0), head_path)
    # keep the .blend free of preview-only objects
    for ob in [o for o in scene.objects if o.name.startswith("Preview")]:
        bpy.data.objects.remove(ob, do_unlink=True)


for name, (prefix, mat_prefix, colour) in VARIANTS.items():
    body, tip = build(prefix, mat_prefix, colour)
    size = export([body, tip], os.path.join(ASSETS, name + ".mesh"))
    preview(os.path.join(HERE, name + "_preview.png"), os.path.join(HERE, name + "_preview_head.png"))
    blend = os.path.join(ROOT, "blender", name + ".blend")
    os.makedirs(os.path.dirname(blend), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=blend)
    print(name, "exported", size, "bytes")
