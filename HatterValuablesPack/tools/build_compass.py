"""Run inside Blender (Scripting tab, or via the Blender MCP with runpy.run_path) to build the two compass valuables.

Builds blender/compass.blend, exports src/HatterValuablesPack/Assets/compass.mesh ("HWM1", read by MeshLibrary.cs) and
renders tools/compass_preview.png.

Design (user's request, 13 Nov): a clear glass sphere with a needle floating inside that points in three dimensions,
held in an hourglass-shaped brass frame: a flared base and a matching flared cap joined by three slim posts, with a
little cup above and below the sphere. The compass's emblem stands on the cap: a red eye (Enemy Compass) or a green
dollar sign (Extraction Compass). (The mod can stand the game's own Robe or extraction screen there instead.)
Objects:
  - Compass_Frame:            base, cap, posts and cups (Brass, Brass Dark)
  - Compass_Glass:            the sphere (Glass: see-through; its base colour's alpha is the opacity)
  - Compass_Needle:           a double cone centred on the origin, the red half pointing along +Y (Glow Needle Red, Glow Needle White, Brass; the glow materials are self-lit in game)
  - Compass_EmblemEnemy:      the red eye on a little stand, standing on the origin (Glow Eye, Eye Iris, Eye Pupil, Brass Dark)
  - Compass_EmblemExtraction: the green "$" on a little stand, standing on the origin, facing -Y (Glow Dollar, Brass Dark)
Axes: up is +Z (Unity +Y); exported to Unity as (-x, z, -y). Sizes in metres (about 10 cm tall with an emblem).
"""
import bpy, bmesh, math, os, struct
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ASSETS = os.path.join(ROOT, "src", "HatterValuablesPack", "Assets")

SR = 0.032          # glass sphere radius
BASE_H = 0.010      # base height
GAP = 0.004         # between the base/cap and the sphere
CZ = BASE_H + GAP + SR          # sphere centre height
CAP_Z = CZ + SR + GAP           # underside of the cap
CAP_H = 0.008
TOP_Z = CAP_Z + CAP_H           # where the emblem stands
POST_R = SR + 0.006             # posts' distance from the axis


def clear():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.lights, bpy.data.cameras):
        for block in list(coll):
            if block.users == 0:
                coll.remove(block)


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


def lathe_z(name, profile, mat, sides=64, x=0.0, y=0.0, smooth=True):
    """Surface of revolution round a vertical axis at (x, y); profile = [(r, z)]; r = 0 closes an end."""
    bm = bmesh.new()
    rings = []
    for r, z in profile:
        ring = [bm.verts.new((x + r * math.cos(2 * math.pi * i / sides), y + r * math.sin(2 * math.pi * i / sides), z)) for i in range(sides)]
        rings.append(ring)
    for k in range(len(rings) - 1):
        for i in range(sides):
            j = (i + 1) % sides
            bm.faces.new((rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i]))
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return new_object(name, bm, mat, smooth)


def box(name, centre, size, mat, rot_z=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    m = Matrix.Translation(centre) @ Matrix.Rotation(rot_z, 4, "Z") @ Matrix.Diagonal((*size, 1.0))
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
    return new_object(name, bm, mat)


def prism(name, pts2d, z0, z1, mat):
    """A flat shape (points in XY, counter-clockwise) extruded from z0 to z1."""
    bm = bmesh.new()
    bottom = [bm.verts.new((x, y, z0)) for x, y in pts2d]
    top = [bm.verts.new((x, y, z1)) for x, y in pts2d]
    bm.faces.new(top)
    bm.faces.new(bottom[::-1])
    n = len(pts2d)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((bottom[i], bottom[j], top[j], top[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return new_object(name, bm, mat)


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


def material(name, color, roughness, metallic=0.0, alpha=1.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    if not m.node_tree:
        m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, alpha)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    if alpha < 1.0 and "Alpha" in bsdf.inputs:
        bsdf.inputs["Alpha"].default_value = alpha
        try:
            m.surface_render_method = "BLENDED"
        except Exception:
            pass
    return m


def sphere(name, centre, radius, mat, segs=32, rings=16, scale=(1, 1, 1)):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=radius)
    bmesh.ops.transform(bm, matrix=Matrix.Translation(centre) @ Matrix.Diagonal((*scale, 1.0)), verts=bm.verts)
    return new_object(name, bm, mat, smooth=True)


def cone_between(name, a, b, r0, r1, mat, sides=16):
    a, b = Vector(a), Vector(b)
    d = b - a
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=sides, radius1=r0, radius2=r1, depth=d.length)
    rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=Matrix.Translation((a + b) / 2) @ rot, verts=bm.verts)
    return new_object(name, bm, mat, smooth=True)


def text_mesh(name, body, size, depth, mat):
    cu = bpy.data.curves.new(name, "FONT")
    cu.body = body
    cu.size = size
    cu.extrude = depth / 2
    cu.align_x = "CENTER"
    cu.align_y = "CENTER"
    ob = bpy.data.objects.new(name, cu)
    bpy.context.scene.collection.objects.link(ob)
    deps = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(deps))
    bpy.data.objects.remove(ob, do_unlink=True)
    out = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(out)
    out.data.materials.clear()
    out.data.materials.append(mat)
    return out


def build():
    clear()
    brass = material("Brass", (0.78, 0.56, 0.22), 0.3, 1.0)
    brass_dark = material("Brass Dark", (0.42, 0.29, 0.10), 0.45, 1.0)
    glass = material("Glass", (0.85, 0.93, 1.0), 0.05, 0.0, alpha=0.16)
    needle_n = material("Glow Needle Red", (1.0, 0.05, 0.03), 0.35, 0.0)
    needle_s = material("Glow Needle White", (1.0, 1.0, 1.0), 0.35, 0.0)
    eye = material("Glow Eye", (0.95, 0.08, 0.05), 0.3, 0.0)
    iris = material("Eye Iris", (1.0, 0.75, 0.1), 0.3, 0.0)
    pupil = material("Eye Pupil", (0.02, 0.0, 0.0), 0.3, 0.0)
    dollar = material("Glow Dollar", (0.15, 0.95, 0.35), 0.3, 0.0)

    # frame: flared base and cap (an hourglass), three posts, cups hugging the sphere
    frame = []
    frame.append(lathe_z("Base", [(0.0, 0.0), (0.040, 0.0), (0.041, 0.002), (0.038, 0.004), (0.030, 0.006),
                                  (0.024, BASE_H - 0.002), (0.024, BASE_H), (0.0, BASE_H)], brass))
    frame.append(lathe_z("Base Ring", [(0.0405, 0.0015), (0.0415, 0.0025), (0.0405, 0.0035)], brass_dark, smooth=False))
    frame.append(lathe_z("Cap", [(0.0, CAP_Z), (0.024, CAP_Z), (0.024, CAP_Z + 0.002), (0.030, CAP_Z + 0.004),
                                 (0.038, CAP_Z + 0.006), (0.041, CAP_Z + CAP_H - 0.002), (0.040, TOP_Z), (0.0, TOP_Z)], brass))
    frame.append(lathe_z("Cap Ring", [(0.0405, TOP_Z - 0.0035), (0.0415, TOP_Z - 0.0025), (0.0405, TOP_Z - 0.0015)], brass_dark, smooth=False))
    # cups: shallow rings the sphere rests in
    frame.append(lathe_z("Lower Cup", [(0.012, BASE_H), (0.016, BASE_H + GAP + 0.003), (0.0145, BASE_H + GAP + 0.003), (0.0105, BASE_H)], brass_dark))
    frame.append(lathe_z("Upper Cup", [(0.0105, CAP_Z), (0.0145, CAP_Z - GAP - 0.003), (0.016, CAP_Z - GAP - 0.003), (0.012, CAP_Z)], brass_dark))
    for i in range(3):
        a = math.pi / 2 + i * 2 * math.pi / 3
        x, y = POST_R * math.cos(a), POST_R * math.sin(a)
        frame.append(cone_between("Post", (x, y, 0.005), (x, y, CAP_Z + 0.004), 0.0028, 0.0028, brass, sides=12))
        for z in (BASE_H + 0.004, CAP_Z - 0.004, CZ):
            frame.append(sphere("Post Knob", (x, y, z), 0.0042, brass_dark, segs=12, rings=8))
    frame_ob = join(frame, "Compass_Frame")

    glass_ob = sphere("Compass_Glass", (0, 0, CZ), SR, glass, segs=48, rings=24)

    # needle: a spindle, red half towards +Y, white half towards -Y, a brass bead in the middle
    L, W = SR * 0.8, 0.0042
    needle = [
        cone_between("Needle N", (0, 0, 0), (0, L, 0), W, 0.0, needle_n, sides=12),
        cone_between("Needle S", (0, 0, 0), (0, -L, 0), W, 0.0, needle_s, sides=12),
        sphere("Bead", (0, 0, 0), W * 1.25, brass, segs=16, rings=8),
    ]
    needle_ob = join(needle, "Compass_Needle")

    # emblems, each on a little stand at the origin
    def stand():
        return [lathe_z("Stand", [(0.0, 0.0), (0.012, 0.0), (0.011, 0.002), (0.004, 0.003), (0.003, 0.008), (0.0, 0.008)], brass_dark, sides=24)]
    e = stand()
    ez = 0.008 + 0.010
    e.append(sphere("Eye", (0, 0, ez), 0.010, eye, segs=24, rings=12, scale=(1, 0.9, 1)))
    e.append(sphere("Iris", (0, -0.0072, ez), 0.0052, iris, segs=20, rings=10, scale=(1, 0.35, 1)))
    e.append(sphere("Pupil", (0, -0.0092, ez), 0.004, pupil, segs=16, rings=8, scale=(0.32, 0.25, 1.05)))
    enemy_ob = join(e, "Compass_EmblemEnemy")

    x = stand()
    sign = text_mesh("Dollar", "$", 0.03, 0.004, dollar)
    sign.rotation_euler = (math.pi / 2, 0, 0)          # upright, facing -Y
    bpy.context.view_layer.update()
    bb = [sign.matrix_world @ Vector(c) for c in sign.bound_box]
    sign.location.z += 0.008 - min(v.z for v in bb) - 0.0005
    x.append(sign)
    extraction_ob = join(x, "Compass_EmblemExtraction")
    return frame_ob, glass_ob, needle_ob, enemy_ob, extraction_ob


# ---------------------------------------------------------------- export (same format as the other mods)

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


# ---------------------------------------------------------------- preview

def render(path):
    """The two compasses side by side, each with its emblem on the cap and its needle placed in the sphere."""
    scene = bpy.context.scene
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    try:
        world.node_tree.nodes["Background"].inputs[0].default_value = (0.7, 0.7, 0.69, 1)
        world.node_tree.nodes["Background"].inputs[1].default_value = 0.5
    except Exception:
        pass
    for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
        try:
            scene.render.engine = engine
            break
        except Exception:
            continue
    scene.render.resolution_x, scene.render.resolution_y = 1280, 720
    names = ("Compass_Frame", "Compass_Glass", "Compass_Needle", "Compass_EmblemEnemy", "Compass_EmblemExtraction")
    frame, glass, needle, enemy, extraction = (bpy.data.objects[n] for n in names)
    temp = []
    for dx, emblem, rot in ((-0.055, enemy, (0.5, 0.0, -0.7)), (0.055, extraction, (-0.3, 0.0, 0.9))):
        for src in (frame, glass):
            c = src.copy(); scene.collection.objects.link(c); c.location = (dx, 0, 0); temp.append(c)
        nd = needle.copy(); scene.collection.objects.link(nd); nd.location = (dx, 0, CZ); nd.rotation_euler = rot; temp.append(nd)
        em = emblem.copy(); scene.collection.objects.link(em); em.location = (dx, 0, TOP_Z); temp.append(em)
    for ob in (frame, glass, needle, enemy, extraction):
        ob.hide_render = True
    cd = bpy.data.cameras.new("Preview Camera")
    cam = bpy.data.objects.new("Preview Camera", cd); scene.collection.objects.link(cam); temp.append(cam)
    cd.lens = 50
    cam.location = (0.0, -0.36, 0.13)
    cam.rotation_euler = (Vector((0, 0, 0.055)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    for loc, energy in (((0.3, -0.3, 0.4), 30), ((-0.4, 0.2, 0.3), 10)):
        ld = bpy.data.lights.new("Preview Light", "AREA"); ld.energy = energy; ld.size = 0.5
        lo = bpy.data.objects.new("Preview Light", ld); lo.location = loc
        lo.rotation_euler = (Vector((0, 0, 0.05)) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        scene.collection.objects.link(lo); temp.append(lo)
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    for ob in temp:
        bpy.data.objects.remove(ob, do_unlink=True)
    for ob in (frame, glass, needle, enemy, extraction):
        ob.hide_render = False


objs = build()
size = export(list(objs), os.path.join(ASSETS, "compass.mesh"))
render(os.path.join(HERE, "compass_preview.png"))
blend = os.path.join(ROOT, "blender", "compass.blend")
bpy.ops.wm.save_as_mainfile(filepath=blend)
print("compass exported", size, "bytes")
