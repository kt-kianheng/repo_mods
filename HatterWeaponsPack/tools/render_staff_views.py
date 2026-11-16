"""Run inside Blender (via the Blender MCP with runpy.run_path) after build_staves.py to review a staff.

Opens blender/<STAFF>.blend (default ember_staff), stands the staff upright and renders tools/staff_view_front.png,
staff_view_angle.png, staff_view_side.png (full length, orthographic) and staff_view_head_front.png,
staff_view_head_angle.png (close-ups of the head). The .blend is left untouched.
"""
import bpy, math, os
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
STAFF = globals().get("STAFF", "ember_staff")

bpy.ops.wm.open_mainfile(filepath=os.path.join(ROOT, "blender", STAFF + ".blend"))
s = bpy.context.scene
obs = [o for o in s.objects if o.type == 'MESH']
for o in obs:
    o.rotation_euler = (-math.pi / 2, 0, 0)   # the staff points along -Y: stand it up, head at the top
bpy.context.view_layer.update()
pts = [o.matrix_world @ Vector(c) for o in obs for c in o.bound_box]
zmin, zmax = min(q.z for q in pts), max(q.z for q in pts)
mid = Vector((0, 0, (zmin + zmax) / 2))

cd = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cd)
s.collection.objects.link(cam)
cd.type = 'ORTHO'
s.camera = cam
for loc, energy in (((2, -2.5, 2.5), 40), ((-2.5, 1.5, 1.0), 15), ((0, 2.5, -0.5), 10)):
    ld = bpy.data.lights.new("L", "AREA")
    ld.energy = energy
    ld.size = 2
    lo = bpy.data.objects.new("L", ld)
    lo.location = loc
    lo.rotation_euler = (mid - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    s.collection.objects.link(lo)


def shot(name, target, angle, distance, lift):
    a = math.radians(angle)
    cam.location = target + Vector((math.sin(a) * distance, -math.cos(a) * distance, lift))
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    s.render.filepath = os.path.join(HERE, "staff_view_%s.png" % name)
    bpy.ops.render.render(write_still=True)


cd.ortho_scale = (zmax - zmin) * 1.08
s.render.resolution_x, s.render.resolution_y = 500, 1400
for name, angle in (("front", 0), ("angle", 45), ("side", 90)):
    shot(name, mid, angle, 3, 0)

head = Vector((0, 0, zmax - 0.16))
cd.ortho_scale = 0.38
s.render.resolution_x, s.render.resolution_y = 700, 700
for name, angle in (("head_front", 0), ("head_angle", 60)):
    shot(name, head, angle, 2, 0.35)

bpy.ops.wm.read_homefile(use_empty=True)
print("Staff views rendered for", STAFF)
