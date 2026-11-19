# -*- coding: utf-8 -*-
"""零依赖生成 AttriDel 多尺寸 ICO。
纯 Python 标准库：矢量形状超采样抗锯齿 -> PNG(zlib) -> ICO。
设计概念：蓝色圆角底 + 白色文档 + 绿色对勾徽章（清理完成/干净）。
"""
import math, struct, zlib, os

OUT = r"F:\AI Project\AttriDel\build\AttriDel.ico"

# ---- 形状定义，坐标系 0..256 ----
def in_round_rect(px, py, x0, y0, x1, y1, r):
    if px < x0 or px > x1 or py < y0 or py > y1:
        return False
    if r <= 0:
        return True
    cx = min(max(px, x0 + r), x1 - r)
    cy = min(max(py, y0 + r), y1 - r)
    return (px - cx) ** 2 + (py - cy) ** 2 <= r * r

def in_circle(px, py, cx, cy, r):
    return (px - cx) ** 2 + (py - cy) ** 2 <= r * r

def in_poly(px, py, pts):
    inside = False
    n = len(pts)
    j = n - 1
    for i in range(n):
        xi, yi = pts[i]
        xj, yj = pts[j]
        if ((yi > py) != (yj > py)) and (px < (xj - xi) * (py - yi) / (yj - yi) + xi):
            inside = not inside
        j = i
    return inside

def in_segment(px, py, ax, ay, bx, by, w):
    dx, dy = bx - ax, by - ay
    L2 = dx * dx + dy * dy
    if L2 == 0:
        return (px - ax) ** 2 + (py - ay) ** 2 <= (w / 2) ** 2
    t = ((px - ax) * dx + (py - ay) * dy) / L2
    t = max(0.0, min(1.0, t))
    qx, qy = ax + t * dx, ay + t * dy
    return (px - qx) ** 2 + (py - qy) ** 2 <= (w / 2) ** 2

def lerp(a, b, t):
    return a + (b - a) * t

def bg_color(py):
    # 顶部亮蓝 -> 底部深蓝
    t = max(0.0, min(1.0, (py - 8.0) / 240.0))
    return (lerp(58, 26, t), lerp(123, 76, t), lerp(235, 172, t))

# 各层: (bounds, coverage_func)  coverage 返回 (r,g,b,a) 或 None
LAYERS = []

def layer(bounds, fn):
    LAYERS.append((bounds, fn))

# 1) 圆角蓝底
layer((8, 8, 248, 248), lambda x, y: bg_color(y) + (1.0,)
      if in_round_rect(x, y, 8, 8, 248, 248, 52) else None)

# 2) 顶部柔和高光（alpha 随 y 线性衰减）
def hi(x, y):
    if not in_round_rect(x, y, 8, 8, 248, 248, 52):
        return None
    a = 0.16 * max(0.0, 1.0 - (y - 8.0) / 150.0)
    if a <= 0:
        return None
    return (255, 255, 255, a)
layer((8, 8, 248, 158), hi)

# 3) 文档阴影
layer((66, 56, 178, 204), lambda x, y: (10, 30, 60, 0.22)
      if in_round_rect(x, y, 68, 58, 176, 204, 10) else None)

# 4) 文档白底
layer((64, 52, 172, 196), lambda x, y: (255, 255, 255, 1.0)
      if in_round_rect(x, y, 64, 52, 172, 196, 10) else None)

# 5) 右上角翻折切区（稍暗，限定在文档内）
DOC = (64, 52, 172, 196, 10)
CUT = [(138, 52), (172, 52), (172, 86)]
def cut(x, y):
    if in_round_rect(x, y, *DOC) and in_poly(x, y, CUT):
        return (232, 239, 250, 1.0)
    return None
layer((138, 52, 172, 86), cut)

# 6) 折角三角
FOLD = [(138, 52), (172, 86), (138, 86)]
def fold(x, y):
    if in_round_rect(x, y, *DOC) and in_poly(x, y, FOLD):
        return (188, 209, 238, 1.0)
    return None
layer((138, 52, 172, 86), fold)

# 7) 文档文本线
LINES = [(82, 92, 152), (82, 114, 146), (82, 136, 152), (82, 158, 126)]
def txt(x, y):
    for x0, cy, x1 in LINES:
        if in_round_rect(x, y, x0, cy - 5, x1, cy + 5, 5):
            return (168, 192, 226, 1.0)
    return None
layer((82, 87, 152, 163), txt)

# 8) 徽章阴影
layer((144, 148, 212, 216), lambda x, y: (8, 40, 30, 0.25)
      if in_circle(x, y, 178, 184, 34) else None)

# 9) 绿色对勾徽章（白色描边 + 绿底）
def badge(x, y):
    d2 = (x - 176) ** 2 + (y - 180) ** 2
    if d2 <= 30 ** 2:
        return (36, 191, 120, 1.0)       # 绿底（先判内圈）
    if d2 <= 34 ** 2:
        return (255, 255, 255, 1.0)      # 白描边
    return None
layer((142, 146, 210, 214), badge)

# 10) 白色对勾
def check(x, y):
    if in_segment(x, y, 163, 180, 173, 190, 11) or \
       in_segment(x, y, 173, 190, 192, 168, 11):
        return (255, 255, 255, 1.0)
    return None
layer((158, 162, 198, 196), check)


def render(size, ss):
    W = size * ss
    # premultiplied 累积缓冲
    acc = [0.0] * (W * W * 4)
    scale = 256.0 / W
    for py in range(W):
        y = (py + 0.5) * scale
        for px in range(W):
            x = (px + 0.5) * scale
            cr = cg = cb = ca = 0.0
            for (x0, y0, x1, y1), fn in LAYERS:
                if x < x0 or x > x1 or y < y0 or y > y1:
                    continue
                cov = fn(x, y)
                if cov is None:
                    continue
                r, g, b, a = cov
                if a <= 0:
                    continue
                cr = r * a + cr * (1 - a)
                cg = g * a + cg * (1 - a)
                cb = b * a + cb * (1 - a)
                ca = a + ca * (1 - a)
            idx = (py * W + px) * 4
            acc[idx] = cr
            acc[idx + 1] = cg
            acc[idx + 2] = cb
            acc[idx + 3] = ca

    # 超采样下采样 + 反预乘 -> RGBA 像素
    out = bytearray()
    inv = 1.0 / (ss * ss)
    for y in range(size):
        out.append(0)  # PNG filter: None
        for x in range(size):
            sr = sg = sb = sa = 0.0
            for dy in range(ss):
                for dx in range(ss):
                    idx = ((y * ss + dy) * W + (x * ss + dx)) * 4
                    sr += acc[idx]
                    sg += acc[idx + 1]
                    sb += acc[idx + 2]
                    sa += acc[idx + 3]
            a = sa * inv
            if a <= 0:
                out += b"\x00\x00\x00\x00"
            else:
                out += bytes((int(sr * inv / a + 0.5), int(sg * inv / a + 0.5),
                              int(sb * inv / a + 0.5), int(a * 255 + 0.5)))
    return bytes(out)


def png_chunk(tag, data):
    return (struct.pack(">I", len(data)) + tag + data +
            struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

def make_png(size, raw):
    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n" + png_chunk(b"IHDR", ihdr) +
            png_chunk(b"IDAT", zlib.compress(raw, 9)) + png_chunk(b"IEND", b""))

def main():
    specs = [(16, 8), (24, 6), (32, 6), (48, 4), (64, 4), (128, 3), (256, 2)]
    images = []
    for size, ss in specs:
        raw = render(size, ss)
        images.append((size, make_png(size, raw)))
        print("rendered", size)

    n = len(images)
    header = struct.pack("<HHH", 0, 1, n)
    offset = 6 + 16 * n
    entries = b""
    data = b""
    for size, png in images:
        b = 0 if size == 256 else size
        entries += struct.pack("<BBBBHHII", b, b, 0, 0, 1, 32, len(png), offset)
        offset += len(png)
        data += png
    with open(OUT, "wb") as f:
        f.write(header + entries + data)
    print("ICO ->", OUT, os.path.getsize(OUT), "bytes")

if __name__ == "__main__":
    main()
