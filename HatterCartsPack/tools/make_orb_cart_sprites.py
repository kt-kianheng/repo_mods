#!/usr/bin/env python3
"""Procedurally generates the Orb Cart sprites.

Outputs (256x256 RGBA PNGs):
  src/HatterCartsPack/Assets/orb_cart_icon.png - inventory / shop icon (transparent background)
  src/HatterCartsPack/Assets/orb_cart_emblem.png - decal applied to the cart's outer walls
  thunderstore/icon.png          - Thunderstore package icon (opaque)
Drawn at 4x and downsampled for clean anti-aliasing.
"""
import math, os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
S = 4
N = 256 * S
FONT = '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'

ORB_CORE = (255, 236, 255)
ORB_MID = (214, 92, 255)
ORB_EDGE = (110, 30, 190)
GLOW = (200, 80, 255)
METAL_D = (38, 41, 48)
METAL_M = (66, 71, 82)
METAL_L = (104, 111, 126)
ACCENT = (150, 70, 230)
SCREEN_BG = (8, 22, 14)
SCREEN_FG = (90, 255, 140)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def radial_orb(size, core=ORB_CORE, mid=ORB_MID, edge=ORB_EDGE):
    """A glowing sphere with a hot off-centre core and a specular highlight."""
    img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    px = img.load()
    r = size / 2
    hx, hy = r * 0.78, r * 0.72  # hot spot (upper-left)
    for y in range(size):
        for x in range(size):
            dx, dy = x + 0.5 - r, y + 0.5 - r
            d = math.hypot(dx, dy) / r
            if d > 1:
                continue
            h = math.hypot(x + 0.5 - hx, y + 0.5 - hy) / (r * 1.25)
            t = min(1.0, h)
            col = lerp(core, mid, min(1, t * 1.6)) if t < 0.62 else lerp(mid, edge, min(1, (t - 0.62) / 0.6))
            # rim darkening + soft edge alpha
            rim = max(0.0, (d - 0.82) / 0.18)
            col = lerp(col, edge, rim * 0.6)
            a = 255 if d < 0.97 else int(255 * (1 - d) / 0.03)
            px[x, y] = col + (a,)
    d = ImageDraw.Draw(img)
    s = size
    d.ellipse([s * 0.24, s * 0.18, s * 0.46, s * 0.36], fill=(255, 255, 255, 170))
    d.ellipse([s * 0.30, s * 0.22, s * 0.38, s * 0.29], fill=(255, 255, 255, 235))
    return img


def glow(size, color, radius, strength=1.0):
    g = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(g)
    c = size / 2
    d.ellipse([c - radius, c - radius, c + radius, c + radius], fill=color + (int(200 * strength),))
    return g.filter(ImageFilter.GaussianBlur(radius * 0.55))


def paste_center(base, img, cx, cy):
    base.alpha_composite(img, (int(cx - img.width / 2), int(cy - img.height / 2)))


def rounded(d, box, r, **kw):
    d.rounded_rectangle(box, radius=r, **kw)


def draw_cart(img, ox, oy, w, fill_pct=0.42, with_text=True):
    """Front-three-quarter cart: square basket, closed twin hatch on top, handle + % screen on the face."""
    d = ImageDraw.Draw(img)
    h = w * 0.62
    depth = w * 0.22
    x0, y0 = ox - w / 2, oy - h / 2 + depth * 0.5
    x1, y1 = x0 + w, y0 + h
    # shadow
    sh = Image.new('RGBA', img.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).ellipse([x0 + w * 0.02, y1 + h * 0.06, x1 + w * 0.08, y1 + h * 0.26], fill=(0, 0, 0, 120))
    img.alpha_composite(sh.filter(ImageFilter.GaussianBlur(w * 0.04)))
    # top face (parallelogram) with hatch
    top = [(x0, y0), (x0 + depth, y0 - depth), (x1 + depth, y0 - depth), (x1, y0)]
    d.polygon(top, fill=METAL_L)
    # hatch doors (two panels split down the middle)
    inset = w * 0.07
    hx0, hx1 = x0 + inset + depth * 0.15, x1 - inset + depth * 0.15
    hy0, hy1 = y0 - depth * 0.18, y0 - depth * 0.82
    mid = (hx0 + hx1) / 2 + depth * 0.0
    for a, b in ((hx0, mid - w * 0.01), (mid + w * 0.01, hx1)):
        poly = [(a, hy0), (a + depth * 0.64, hy1), (b + depth * 0.64, hy1), (b, hy0)]
        d.polygon(poly, fill=(30, 32, 38))
        d.line(poly + [poly[0]], fill=ACCENT, width=max(2, int(w * 0.012)))
    # side face
    side = [(x1, y0), (x1 + depth, y0 - depth), (x1 + depth, y1 - depth), (x1, y1)]
    d.polygon(side, fill=METAL_D)
    # emblem on side
    ex, ey = x1 + depth * 0.5, (y0 + y1) / 2 - depth * 0.5
    er = min(depth, h) * 0.32
    d.ellipse([ex - er * 0.8, ey - er, ex + er * 0.8, ey + er], fill=ACCENT)
    # front face
    rounded(d, [x0, y0, x1, y1], w * 0.035, fill=METAL_M)
    # front frame lines
    lw = max(2, int(w * 0.014))
    d.line([(x0 + w * 0.05, y0 + h * 0.12), (x1 - w * 0.05, y0 + h * 0.12)], fill=METAL_L, width=lw)
    d.line([(x0 + w * 0.05, y1 - h * 0.12), (x1 - w * 0.05, y1 - h * 0.12)], fill=METAL_D, width=lw)
    # purple accent stripe
    d.rectangle([x0, y1 - h * 0.30, x1, y1 - h * 0.22], fill=ACCENT)
    # screen on the face
    sx0, sx1 = x0 + w * 0.22, x1 - w * 0.22
    sy0, sy1 = y0 + h * 0.20, y0 + h * 0.62
    rounded(d, [sx0 - w * 0.02, sy0 - w * 0.02, sx1 + w * 0.02, sy1 + w * 0.02], w * 0.03, fill=(20, 20, 24))
    rounded(d, [sx0, sy0, sx1, sy1], w * 0.02, fill=SCREEN_BG)
    # fill bar along the bottom of the screen
    bx0, bx1, by0, by1 = sx0 + w * 0.03, sx1 - w * 0.03, sy1 - h * 0.11, sy1 - h * 0.05
    d.rectangle([bx0, by0, bx1, by1], fill=(20, 60, 36))
    d.rectangle([bx0, by0, bx0 + (bx1 - bx0) * fill_pct, by1], fill=SCREEN_FG)
    if with_text:
        fs = int(h * 0.22)
        f = ImageFont.truetype(FONT, fs)
        txt = '%d%%' % round(fill_pct * 100)
        tw = d.textlength(txt, font=f)
        d.text(((sx0 + sx1) / 2 - tw / 2, sy0 + h * 0.035), txt, font=f, fill=SCREEN_FG)
    # handle bar in front of the face
    hy = y0 - h * 0.06
    d.line([(x0 + w * 0.08, hy), (x1 - w * 0.08, hy)], fill=(200, 60, 60), width=int(w * 0.045))
    d.line([(x0 + w * 0.08, hy), (x0 + w * 0.08, y0 + h * 0.05)], fill=(150, 40, 40), width=int(w * 0.03))
    d.line([(x1 - w * 0.08, hy), (x1 - w * 0.08, y0 + h * 0.05)], fill=(150, 40, 40), width=int(w * 0.03))
    # hover thrusters
    for tx in (x0 + w * 0.18, x1 - w * 0.18):
        d.ellipse([tx - w * 0.07, y1 - h * 0.02, tx + w * 0.07, y1 + h * 0.08], fill=(30, 30, 36))
    tg = Image.new('RGBA', img.size, (0, 0, 0, 0))
    td = ImageDraw.Draw(tg)
    for tx in (x0 + w * 0.18, x1 - w * 0.18):
        td.ellipse([tx - w * 0.06, y1 + h * 0.06, tx + w * 0.06, y1 + h * 0.14], fill=(120, 200, 255, 200))
    img.alpha_composite(tg.filter(ImageFilter.GaussianBlur(w * 0.02)))
    return (x0 + x1) / 2 + depth * 0.5, y0 - depth * 0.5  # hatch centre


def make_icon(path, background=False):
    img = Image.new('RGBA', (N, N), (0, 0, 0, 0))
    if background:
        bg = Image.new('RGBA', (N, N), (0, 0, 0, 255))
        px = bg.load()
        for y in range(N):
            for x in range(N):
                t = math.hypot(x - N * 0.5, y - N * 0.42) / (N * 0.75)
                px[x, y] = lerp((54, 24, 84), (10, 8, 16), min(1, t)) + (255,)
        img = bg
    hcx, hcy = draw_cart(img, N * 0.47, N * 0.66, N * 0.66)
    # orb dropping into the hatch, with motion trail
    orb_r = int(N * 0.15)
    cx, cy = hcx, N * 0.20
    trail = Image.new('RGBA', img.size, (0, 0, 0, 0))
    ImageDraw.Draw(trail).polygon([(cx - orb_r * 0.55, cy), (cx + orb_r * 0.55, cy), (hcx + orb_r * 0.15, hcy - N * 0.02), (hcx - orb_r * 0.15, hcy - N * 0.02)], fill=GLOW + (90,))
    img.alpha_composite(trail.filter(ImageFilter.GaussianBlur(N * 0.015)))
    paste_center(img, glow(orb_r * 6, GLOW, orb_r * 1.15, 1.0), cx, cy)
    paste_center(img, radial_orb(orb_r * 2), cx, cy)
    img = img.resize((256, 256), Image.LANCZOS)
    img.save(path)
    return path


def make_emblem(path):
    img = Image.new('RGBA', (N, N), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # dark rounded plate with purple bevel (opaque - the decal material may not support alpha)
    rounded(d, [0, 0, N - 1, N - 1], N * 0.12, fill=(24, 18, 34, 255))
    rounded(d, [N * 0.04, N * 0.04, N * 0.96, N * 0.96], N * 0.10, outline=ACCENT, width=int(N * 0.025))
    # hazard-ish chevrons along the bottom
    for i in range(6):
        x = N * 0.10 + i * N * 0.14
        d.polygon([(x, N * 0.86), (x + N * 0.07, N * 0.78), (x + N * 0.11, N * 0.78), (x + N * 0.04, N * 0.86)], fill=(90, 50, 140))
    # orb
    r = int(N * 0.25)
    paste_center(img, glow(r * 6, GLOW, r * 1.1, 0.9), N / 2, N * 0.40)
    paste_center(img, radial_orb(r * 2), N / 2, N * 0.40)
    # downward arrow into a slot
    d = ImageDraw.Draw(img)
    d.rectangle([N * 0.30, N * 0.70, N * 0.70, N * 0.735], fill=(10, 8, 14))
    f = ImageFont.truetype(FONT, int(N * 0.11))
    for txt, y in (('ORB', N * 0.035),):
        tw = d.textlength(txt, font=f)
        d.text((N / 2 - tw / 2, y + N * 0.02), txt, font=f, fill=(235, 210, 255))
    # opaque backdrop for any transparent corners
    final = Image.new('RGBA', (N, N), (24, 18, 34, 255))
    final.alpha_composite(img)
    final.resize((256, 256), Image.LANCZOS).save(path)
    return path



def make_orb_badge(path, size=512, frac=0.40, margin=0.03):
    """Transparent overlay (same size as the game's baked item icons) with an orb in the top-right corner.
    The mod composites this onto the vanilla Pocket C.A.R.T. icon at runtime."""
    n = size * S
    img = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    r = int(n * frac / 2)
    cx, cy = n - r - n * margin, r + n * margin
    paste_center(img, glow(int(r * 3.4), GLOW, r * 1.05, 1.0), cx, cy)
    # dark outline so the orb reads against the cart at inventory size
    ring = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    ImageDraw.Draw(ring).ellipse([cx - r * 1.07, cy - r * 1.07, cx + r * 1.07, cy + r * 1.07], fill=(20, 6, 34, 230))
    img.alpha_composite(ring)
    paste_center(img, radial_orb(r * 2), cx, cy)
    img.resize((size, size), Image.LANCZOS).save(path)
    return path


def composite_preview(base_icon, badge, out):
    base = Image.open(base_icon).convert('RGBA')
    over = Image.open(badge).convert('RGBA').resize(base.size, Image.LANCZOS)
    base.alpha_composite(over)
    base.save(out)
    return out


# ---- In-game enemy orb, translated to 2D -------------------------------------------------------
# Values taken from the game's materials:
#   Enemy Valuable Outer : BaseColor (1, 0, .66, a .31), FresnelColor (1, 0, .32), FresnelPower 4, Scale 5, Smoothness .8
#   Enemy Valuable Inner : Color (.16, 0, .035) with BW mottled core texture, ColorOverlay (1, 0, .44)
#   Specks               : Color (1, 0, .27), emissive
GAME_SHELL = (255, 0, 168)
GAME_FRESNEL = (255, 0, 82)
GAME_CORE_DARK = (41, 0, 9)
GAME_CORE_PINK = (255, 0, 112)
GAME_SPECK = (255, 40, 110)


def _value_noise(n, cells, seed):
    import random
    rnd = random.Random(seed)
    g = [[rnd.random() for _ in range(cells + 2)] for _ in range(cells + 2)]
    out = Image.new('L', (n, n))
    px = out.load()
    for y in range(n):
        fy = y / n * cells; iy = int(fy); ty = fy - iy; ty = ty * ty * (3 - 2 * ty)
        for x in range(n):
            fx = x / n * cells; ix = int(fx); tx = fx - ix; tx = tx * tx * (3 - 2 * tx)
            a = g[iy][ix] * (1 - tx) + g[iy][ix + 1] * tx
            b = g[iy + 1][ix] * (1 - tx) + g[iy + 1][ix + 1] * tx
            px[x, y] = int(255 * (a * (1 - ty) + b * ty))
    return out


def game_orb(size, seed=7):
    """Returns an RGBA orb (size x size) styled after the in-game enemy orb."""
    import random
    n = size
    r = n / 2
    img = Image.new('RGBA', (n, n), (0, 0, 0, 0))

    # 1) mottled core: fractal value noise, thresholded into blotches like the BW core texture
    noise = Image.blend(_value_noise(n, 5, seed), _value_noise(n, 11, seed + 1), 0.45)
    noise = Image.blend(noise, _value_noise(n, 23, seed + 2), 0.25)
    npx = noise.load()
    core_r = r * 0.80
    px = img.load()
    for y in range(n):
        for x in range(n):
            dx, dy = (x + 0.5 - r) / r, (y + 0.5 - r) / r
            d = math.hypot(dx, dy)
            if d > 1:
                continue
            # spherical lookup so the blotches wrap around the ball
            z = math.sqrt(max(0.0, 1 - d * d))
            sx = int((dx / (1.6 - z * 0.6) * 0.5 + 0.5) * (n - 1))
            sy = int((dy / (1.6 - z * 0.6) * 0.5 + 0.5) * (n - 1))
            v = npx[min(n - 1, max(0, sx)), min(n - 1, max(0, sy))] / 255
            blot = max(0.0, min(1.0, (v - 0.48) / 0.10))          # hard-ish edges like the texture
            core = lerp(GAME_CORE_DARK, GAME_CORE_PINK, blot * 0.85)
            # how much of the core shows through the shell (core is smaller than the shell)
            in_core = max(0.0, min(1.0, (core_r / r - d) / 0.12))
            # shell body tint (translucent magenta) over the core
            body = lerp(lerp(GAME_CORE_DARK, GAME_SHELL, 0.35), core, in_core * 0.75)
            # fresnel rim: (1 - n.v)^power, strong and bright near the edge
            fres = min(1.0, 5.0 * (1 - z) ** 4 * 6)
            col = lerp(body, GAME_FRESNEL, min(1.0, fres))
            # hot inner edge of the rim
            col = lerp(col, (255, 150, 200), max(0.0, (d - 0.90) / 0.10) * 0.55)
            a = 255 if d < 0.985 else int(255 * (1 - d) / 0.015)
            px[x, y] = col + (a,)

    # 2) glowing specks inside the shell
    rnd = random.Random(seed + 9)
    specks = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    sd = ImageDraw.Draw(specks)
    for i in range(int(14 + n / 40)):
        ang = rnd.random() * math.tau
        rad = math.sqrt(rnd.random()) * r * 0.78
        cx, cy = r + math.cos(ang) * rad, r + math.sin(ang) * rad
        sr = n * (0.012 + rnd.random() * (0.03 if i % 4 == 0 else 0.012))
        sd.ellipse([cx - sr, cy - sr, cx + sr, cy + sr], fill=GAME_SPECK + (230,))
    glowy = specks.filter(ImageFilter.GaussianBlur(n * 0.012))
    img.alpha_composite(glowy)
    img.alpha_composite(specks)

    # 3) glossy highlights (smoothness .8): a sharp key highlight + a soft reflection band
    hl = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    hd = ImageDraw.Draw(hl)
    hd.ellipse([n * 0.22, n * 0.15, n * 0.47, n * 0.34], fill=(255, 220, 240, 120))
    hd.ellipse([n * 0.28, n * 0.19, n * 0.38, n * 0.27], fill=(255, 255, 255, 235))
    hd.ellipse([n * 0.64, n * 0.70, n * 0.74, n * 0.76], fill=(255, 190, 220, 90))
    img.alpha_composite(hl.filter(ImageFilter.GaussianBlur(n * 0.006)))

    # keep everything inside the sphere silhouette
    mask = Image.new('L', (n, n), 0)
    ImageDraw.Draw(mask).ellipse([0, 0, n - 1, n - 1], fill=255)
    out = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    out.paste(img, (0, 0), Image.composite(img.getchannel('A'), Image.new('L', (n, n), 0), mask))
    return out


def make_orb_badge_game(path, size=512, frac=0.38, margin=0.035):
    n = size * 2
    img = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    r = int(n * frac / 2)
    cx, cy = n - r - n * margin, r + n * margin
    paste_center(img, glow(int(r * 3.6), GAME_FRESNEL, r * 1.12, 0.95), cx, cy)
    paste_center(img, game_orb(r * 2), cx, cy)
    img.resize((size, size), Image.LANCZOS).save(path)
    return path


# ---- Hatch drawn onto the Pocket C.A.R.T. icon -------------------------------------------------
# Corners of the basket opening in the game's 512x512 baked Pocket C.A.R.T. icon (left, back, right, front).
ICON_OPENING = [(76, 150), (225, 79), (401, 105), (269, 215)]
# The screen bracket sits in front of the opening's right corner; the hatch is never drawn over it.
ICON_BRACKET = [(318, 172), (338, 140), (352, 124), (398, 100), (470, 92), (512, 92), (512, 330), (318, 330)]

HATCH_METAL = (118, 121, 128)
HATCH_METAL_D = (72, 74, 80)
HATCH_PURPLE = (92, 40, 98)
HATCH_YELLOW = (236, 178, 20)


def _perspective_coeffs(dst, src):
    """Coefficients for Image.transform(PERSPECTIVE) mapping output quad dst onto source quad src."""
    import numpy as np
    A, B = [], []
    for (x, y), (u, v) in zip(dst, src):
        A.append([x, y, 1, 0, 0, 0, -u * x, -u * y]); B.append(u)
        A.append([0, 0, 0, x, y, 1, -v * x, -v * y]); B.append(v)
    return np.linalg.solve(np.array(A, float), np.array(B, float)).tolist()


def _door_texture(w, h, seed, mirror=False):
    """One hatch door, flat: u = hinge (0) -> seam (w), v = along the seam."""
    img = Image.new('RGBA', (w, h), HATCH_METAL + (255,))
    d = ImageDraw.Draw(img)
    grime = _value_noise(max(w, h), 9, seed).resize((w, h))
    gp = grime.load(); px = img.load()
    for y in range(h):
        for x in range(w):
            g = gp[x, y] / 255
            px[x, y] = lerp(HATCH_METAL_D, HATCH_METAL, 0.45 + 0.55 * g) + (255,)
    b = int(w * 0.06)
    d.rectangle([0, 0, w - 1, h - 1], outline=(46, 47, 52), width=b)                  # frame
    d.rectangle([b * 2, b * 2, w - 1 - b * 2, h - 1 - b * 2], outline=(150, 152, 158), width=max(2, b // 3))  # bevel
    # purple panel then hazard stripe towards the seam, as on the Hauler doors
    p0, p1 = int(w * 0.52), int(w * 0.80)
    d.rectangle([p0, b * 2, p1, h - 1 - b * 2], fill=HATCH_PURPLE)
    s0 = int(w * 0.84)
    stripe = Image.new('RGBA', (w - s0 - b, h - 4 * b), HATCH_YELLOW + (255,))
    sd = ImageDraw.Draw(stripe)
    step = max(8, stripe.width)
    for k in range(-stripe.width, stripe.height + stripe.width, step):
        sd.polygon([(0, k), (stripe.width, k - stripe.width * 0.8), (stripe.width, k - stripe.width * 0.8 + step / 2), (0, k + step / 2)], fill=(20, 20, 22))
    img.paste(stripe, (s0, b * 2))
    # rivets near the hinge
    for k in range(5):
        cy = b * 2 + (h - 4 * b) * (k + 0.5) / 5
        d.ellipse([b * 2.4, cy - b * 0.7, b * 3.8, cy + b * 0.7], fill=(160, 162, 168))
    return img.transpose(Image.FLIP_LEFT_RIGHT) if mirror else img


def make_hatch_overlay(path, size=512):
    """Transparent 512x512 layer: the closed Hauler-style hatch covering the Pocket C.A.R.T. icon's basket."""
    S2 = 2
    n = size * S2
    A, Bk, C, D = [(x * S2, y * S2) for x, y in ICON_OPENING]
    mid = lambda p, q, t=0.5: (p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t)
    mDC, mAB = mid(D, C), mid(A, Bk)
    layer = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    W, H = 400, 520
    # Left door: hinge on edge A-D, seam on mAB-mDC.  Right door: hinge on edge Bk-C, seam on mAB-mDC.
    doors = [
        (_door_texture(W, H, 3), [A, mAB, mDC, D], 1.00),
        (_door_texture(W, H, 5, mirror=True), [mAB, Bk, C, mDC], 0.86),
    ]
    for tex, quad, light in doors:
        # source rect corners in the same order: (hinge,far) (seam,far) (seam,near) (hinge,near)
        if quad[0] == A:
            src = [(0, 0), (W, 0), (W, H), (0, H)]
        else:
            src = [(0, 0), (W, 0), (W, H), (0, H)]
        coeffs = _perspective_coeffs(quad, src)
        warped = tex.transform((n, n), Image.PERSPECTIVE, coeffs, Image.BICUBIC)
        mask = Image.new('L', (n, n), 0)
        ImageDraw.Draw(mask).polygon(quad, fill=255)
        if light != 1.0:
            r, g, b, a = warped.split()
            warped = Image.merge('RGBA', [ch.point(lambda v, L=light: int(v * L)) for ch in (r, g, b)] + [a])
        layer.paste(warped, (0, 0), mask)
    d = ImageDraw.Draw(layer)
    d.line([mAB, mDC], fill=(14, 14, 16, 255), width=3 * S2)                         # seam
    d.line([A, Bk, C, D, A], fill=(28, 29, 33, 255), width=2 * S2, joint='curve')    # edge where hatch meets rim
    # keep the screen bracket in front
    cut = Image.new('L', (n, n), 255)
    ImageDraw.Draw(cut).polygon([(x * S2, y * S2) for x, y in ICON_BRACKET], fill=0)
    layer.putalpha(Image.composite(layer.getchannel('A'), Image.new('L', (n, n), 0), cut))
    layer.resize((size, size), Image.LANCZOS).save(path)
    return path


if __name__ == '__main__':
    assets = os.path.join(ROOT, 'src', 'HatterCartsPack', 'Assets')
    ts = os.path.join(ROOT, 'thunderstore')
    os.makedirs(assets, exist_ok=True)
    os.makedirs(ts, exist_ok=True)
    print(make_icon(os.path.join(assets, 'orb_cart_icon.png')))
    print(make_emblem(os.path.join(assets, 'orb_cart_emblem.png')))
    print(make_store_icon(os.path.join(ts, 'icon.png')))
    print(make_hatch_overlay(os.path.join(assets, 'orb_cart_hatch_overlay.png')))


def _additive(base, layer):
    """Screen-blend an RGBA light layer onto base (keeps base alpha)."""
    import numpy as np
    b = np.asarray(base, dtype=np.float32) / 255
    l = np.asarray(layer, dtype=np.float32) / 255
    la = l[..., 3:4]
    rgb = 1 - (1 - b[..., :3]) * (1 - l[..., :3] * la)
    out = np.concatenate([rgb, b[..., 3:4]], axis=-1)
    return Image.fromarray((np.clip(out, 0, 1) * 255).astype('uint8'), 'RGBA')


def screenshot_orb(size, seed=7):
    """The in-game orb as it looks in a R.E.P.O. screenshot: low-res render look, bloom, a hot core
    and a little sensor grain."""
    import random
    import numpy as np
    big = game_orb(size * 2, seed)
    # brighten the emissive parts (specks + rim) the way the game's bloom pushes them towards white-pink
    arr = np.asarray(big, dtype=np.float32)
    lum = arr[..., 0] * 0.6 + arr[..., 2] * 0.4
    hot = np.clip((lum - 170) / 85, 0, 1)[..., None]
    arr[..., :3] = arr[..., :3] * (1 - hot * 0.35) + np.array([255, 190, 225]) * hot * 0.35
    big = Image.fromarray(arr.clip(0, 255).astype('uint8'), 'RGBA')
    # R.E.P.O. renders at a low internal resolution: soften then pixelate slightly
    orb = big.resize((size, size), Image.LANCZOS)
    px = max(8, size // 5)
    orb = orb.resize((px, px), Image.BILINEAR).resize((size, size), Image.NEAREST)
    orb = orb.filter(ImageFilter.GaussianBlur(size * 0.004))
    # grain
    rnd = np.random.default_rng(seed)
    a = np.asarray(orb, dtype=np.float32)
    a[..., :3] += rnd.normal(0, 7, a[..., :3].shape)
    return Image.fromarray(a.clip(0, 255).astype('uint8'), 'RGBA')


def make_store_icon(path):
    """Thunderstore icon: the drawn Orb C.A.R.T. with an in-game-looking orb dropping into its hatch."""
    img = Image.new('RGBA', (N, N), (0, 0, 0, 255))
    px = img.load()
    for y in range(N):
        for x in range(N):
            t = math.hypot(x - N * 0.5, y - N * 0.42) / (N * 0.75)
            px[x, y] = lerp((54, 18, 52), (10, 8, 14), min(1, t)) + (255,)
    hcx, hcy = draw_cart(img, N * 0.47, N * 0.66, N * 0.66)

    orb_r = int(N * 0.15)
    cx, cy = hcx, N * 0.20
    # pink light from the orb spilling onto the hatch and the top of the cart
    spill = Image.new('RGBA', (N, N), (0, 0, 0, 0))
    ImageDraw.Draw(spill).ellipse([hcx - N * 0.30, hcy - N * 0.14, hcx + N * 0.30, hcy + N * 0.14], fill=GAME_FRESNEL + (150,))
    img = _additive(img, spill.filter(ImageFilter.GaussianBlur(N * 0.06)))
    # faint motion streak
    trail = Image.new('RGBA', (N, N), (0, 0, 0, 0))
    ImageDraw.Draw(trail).polygon([(cx - orb_r * 0.5, cy), (cx + orb_r * 0.5, cy), (hcx + orb_r * 0.12, hcy - N * 0.03), (hcx - orb_r * 0.12, hcy - N * 0.03)], fill=GAME_FRESNEL + (70,))
    img = _additive(img, trail.filter(ImageFilter.GaussianBlur(N * 0.02)))
    # bloom: wide soft halo + tight bright halo
    halo = Image.new('RGBA', (N, N), (0, 0, 0, 0))
    hd = ImageDraw.Draw(halo)
    hd.ellipse([cx - orb_r * 2.1, cy - orb_r * 2.1, cx + orb_r * 2.1, cy + orb_r * 2.1], fill=GAME_FRESNEL + (120,))
    img = _additive(img, halo.filter(ImageFilter.GaussianBlur(orb_r * 0.9)))
    halo2 = Image.new('RGBA', (N, N), (0, 0, 0, 0))
    ImageDraw.Draw(halo2).ellipse([cx - orb_r * 1.15, cy - orb_r * 1.15, cx + orb_r * 1.15, cy + orb_r * 1.15], fill=(255, 90, 160, 200))
    img = _additive(img, halo2.filter(ImageFilter.GaussianBlur(orb_r * 0.25)))
    paste_center(img, screenshot_orb(orb_r * 2), cx, cy)
    # bloom bleeding back over the orb's edge, like a real capture
    edge = Image.new('RGBA', (N, N), (0, 0, 0, 0))
    ImageDraw.Draw(edge).ellipse([cx - orb_r, cy - orb_r, cx + orb_r, cy + orb_r], outline=(255, 120, 180, 160), width=int(orb_r * 0.10))
    img = _additive(img, edge.filter(ImageFilter.GaussianBlur(orb_r * 0.12)))
    img = img.resize((256, 256), Image.LANCZOS)
    img.save(path)
    return path


def make_store_icon_ingame(path, pocket_icon):
    """Thunderstore icon from in-game art: the baked Pocket C.A.R.T. icon (from the player's install) with the
    hatch overlay, plus the screenshot-style orb dropping into the hatch."""
    hatch_png = os.path.join(ROOT, 'src', 'HatterCartsPack', 'Assets', 'orb_cart_hatch_overlay.png')
    cart = Image.open(pocket_icon).convert('RGBA')
    cart.alpha_composite(Image.open(hatch_png).convert('RGBA'))

    img = Image.new('RGBA', (N, N), (0, 0, 0, 255))
    px = img.load()
    for y in range(N):
        for x in range(N):
            t = math.hypot(x - N * 0.5, y - N * 0.40) / (N * 0.75)
            px[x, y] = lerp((54, 18, 52), (10, 8, 14), min(1, t)) + (255,)

    # cart: scaled so it fills the lower ~70% of the icon
    cs = int(N * 0.90)
    ox, oy = int((N - cs) / 2 + N * 0.01), int(N * 0.20)
    shadow = Image.new('RGBA', (N, N), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).ellipse([ox + cs * 0.10, oy + cs * 0.80, ox + cs * 0.80, oy + cs * 0.97], fill=(0, 0, 0, 150))
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(N * 0.03)))
    img.alpha_composite(cart.resize((cs, cs), Image.LANCZOS), (ox, oy))

    # hatch centre in icon space (centre of the basket opening quad)
    qx = sum(p[0] for p in ICON_OPENING) / 4
    qy = sum(p[1] for p in ICON_OPENING) / 4
    hcx, hcy = ox + qx / 512 * cs, oy + qy / 512 * cs

    orb_r = int(N * 0.12)
    cx, cy = hcx - N * 0.01, N * 0.15
    spill = Image.new('RGBA', (N, N), (0, 0, 0, 0))
    ImageDraw.Draw(spill).ellipse([hcx - N * 0.24, hcy - N * 0.10, hcx + N * 0.24, hcy + N * 0.10], fill=GAME_FRESNEL + (140,))
    img = _additive(img, spill.filter(ImageFilter.GaussianBlur(N * 0.05)))
    halo = Image.new('RGBA', (N, N), (0, 0, 0, 0))
    ImageDraw.Draw(halo).ellipse([cx - orb_r * 2.1, cy - orb_r * 2.1, cx + orb_r * 2.1, cy + orb_r * 2.1], fill=GAME_FRESNEL + (120,))
    img = _additive(img, halo.filter(ImageFilter.GaussianBlur(orb_r * 0.9)))
    halo2 = Image.new('RGBA', (N, N), (0, 0, 0, 0))
    ImageDraw.Draw(halo2).ellipse([cx - orb_r * 1.15, cy - orb_r * 1.15, cx + orb_r * 1.15, cy + orb_r * 1.15], fill=(255, 90, 160, 200))
    img = _additive(img, halo2.filter(ImageFilter.GaussianBlur(orb_r * 0.25)))
    paste_center(img, screenshot_orb(orb_r * 2), cx, cy)
    edge = Image.new('RGBA', (N, N), (0, 0, 0, 0))
    ImageDraw.Draw(edge).ellipse([cx - orb_r, cy - orb_r, cx + orb_r, cy + orb_r], outline=(255, 120, 180, 160), width=int(orb_r * 0.10))
    img = _additive(img, edge.filter(ImageFilter.GaussianBlur(orb_r * 0.12)))
    img.resize((256, 256), Image.LANCZOS).save(path)
    return path
