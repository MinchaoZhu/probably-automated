#!/usr/bin/env python3
"""桌面学徒对讲机的像素图（48x48，和游戏物品同为 100 PPU）。

用法：python3 art/intercom.py  → 写出 src/AutoMod/ModFiles/Textures/Items/intercom.png
     python3 art/intercom.py --preview out.png  → 另存一张放大 6 倍、桌面底色的预览

仿照店里桌面道具（录音机、终端）的画法：
- 3/4 俯视的斜投影：顶面是向右上斜的平行四边形，下面一条窄正面，右边露出侧面；
- 光从左上来，顶面最亮、正面次之、侧面最暗，整体低对比、偏灰；
- 颜色全部取自游戏物品图集的调色板，轮廓统一用 #292234；
- 底下一圈半透明阴影，让它“放”在桌上。
"""
import os
import struct
import sys
import zlib

W = H = 48
C = {
    'O': (0x29, 0x22, 0x34),  # 轮廓
    'k': (0x37, 0x35, 0x41),  # 最暗
    'd': (0x44, 0x46, 0x4C),  # 侧面
    'm': (0x5F, 0x68, 0x68),  # 正面
    'b': (0x88, 0x94, 0x8A),  # 顶面
    'l': (0xA8, 0xAE, 0xA5),  # 顶面受光
    'h': (0xC4, 0xC4, 0xB0),  # 亮边
    'g': (0x53, 0x58, 0x5B),  # 网格槽
    's': (0x27, 0x56, 0x54),  # 屏幕底
    'S': (0x67, 0xAA, 0xA5),  # 屏幕字
    'T': (0x8A, 0xBF, 0xBB),  # 屏幕高光
    'a': (0x9B, 0x4A, 0x3D),  # 通话键暗部
    'A': (0xCC, 0x77, 0x55),  # 通话键
    'Y': (0xD5, 0x9F, 0x75),  # 通话键高光
    'G': (0x84, 0xBA, 0x51),  # 绿灯
    'R': (0xA2, 0x40, 0x43),  # 红灯
    'r': (0x64, 0x34, 0x3B),  # 红灯（暗）
}
SHADOW = (0x29, 0x22, 0x34, 80)

px = [[None] * W for _ in range(H)]


def put(x, y, c):
    if 0 <= x < W and 0 <= y < H:
        px[y][x] = c


def get(x, y):
    return px[y][x] if 0 <= x < W and 0 <= y < H else None


def poly(pts, c):
    """按像素中心填充多边形。"""
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
    for y in range(int(min(ys)), int(max(ys)) + 1):
        for x in range(int(min(xs)), int(max(xs)) + 1):
            cx, cy = x + 0.5, y + 0.5
            inside = False
            j = len(pts) - 1
            for i in range(len(pts)):
                xi, yi = pts[i]; xj, yj = pts[j]
                if (yi > cy) != (yj > cy) and cx < (xj - xi) * (cy - yi) / (yj - yi) + xi:
                    inside = not inside
                j = i
            if inside:
                put(x, y, c)


# ---------- 几何 ----------
# 对称的 3/4 俯视（和打印机、投影仪、桌上的终端一样）：顶面是上窄下宽的梯形，下面一条正面。
# 顶面前沿 y=FY、x 从 FL 到 FR（含）；往后 DEP 行，每后退 3 行左右各收 1 像素。
FL, FR, FY, DEP, FRONT_H = 5, 42, 30, 13, 7
BOT = FY + FRONT_H                 # 正面底边（含）
inset = lambda v: (v + 1) // 3     # 后退 v 行时左右收进的像素


def top(u, v):
    """顶面局部坐标 → 像素：u 是离前沿左端的列，v 从前沿 0 往后。宽度随 v 收窄，u 按比例压缩。"""
    w0 = FR - FL
    w = w0 - 2 * inset(v)
    return FL + inset(v) + round(u * w / w0), FY - v


def tput(u, v, c):
    put(*top(u, v), c)


def trect(u0, v0, u1, v1, c):
    for v in range(v0, v1 + 1):
        for u in range(u0, u1 + 1):
            tput(u, v, c)


# 阴影：正面下方和右侧各露出一点
for y in range(BOT + 1, BOT + 3):
    for x in range(FL + 1 + (y - BOT), FR + 2 + (y - BOT)):
        put(x, y, 'SH')
for y in range(FY + 2, BOT + 1):
    put(FR + 1, y, 'SH')

# 顶面：后部受光、前部稍暗；左缘亮、右缘暗
for v in range(DEP):
    y = FY - v
    x0, x1 = FL + inset(v), FR - inset(v)
    for x in range(x0, x1 + 1):
        put(x, y, 'l' if v >= DEP - 5 else 'b')
    put(x0, y, 'h'); put(x0 + 1, y, 'l')
    put(x1, y, 'd'); put(x1 - 1, y, 'm')
for x in range(FL + inset(DEP - 1), FR - inset(DEP - 1) + 1):
    put(x, FY - DEP + 1, 'h')                       # 后沿亮边
# 正面：顶部一条暗折线，左端受光、右端暗，底部更暗
for y in range(FY + 1, BOT + 1):
    for x in range(FL, FR + 1):
        put(x, y, 'm')
    put(FL, y, 'b'); put(FR, y, 'd')
for x in range(FL, FR + 1):
    put(x, FY + 1, 'k')
    put(x, BOT, 'd')
for x in range(FL + 1, FR):
    put(x, FY, 'l')                                 # 顶面前沿的转折高光

# ---------- 顶面上的部件 ----------
# 喇叭网格（左）
trect(3, 2, 16, 9, 'k')
for v in range(3, 9, 2):
    trect(4, v, 15, v, 'g')
trect(3, 9, 16, 9, 'O')
# 小屏幕（右后）
trect(20, 6, 34, 9, 'O')
trect(21, 7, 33, 8, 's')
trect(22, 7, 27, 7, 'S'); trect(29, 7, 31, 7, 'S')
tput(33, 8, 'T')
# 通话键（右前）：下沿暗、左上高光
trect(20, 1, 27, 4, 'O')
trect(21, 2, 26, 3, 'A')
trect(21, 2, 26, 2, 'a')
tput(21, 3, 'Y'); tput(22, 3, 'Y')
# 指示灯：绿 = 在线，红 = 需要注意
trect(29, 3, 30, 4, 'O'); tput(29, 4, 'G'); tput(30, 4, 'G')
trect(32, 3, 33, 4, 'O'); tput(32, 4, 'r'); tput(33, 4, 'r')

# 正面：左边一排散热孔，右边一颗螺丝
for x in range(FL + 3, FL + 15, 2):
    put(x, FY + 3, 'k'); put(x, FY + 4, 'k')
put(FR - 3, FY + 3, 'k'); put(FR - 3, FY + 4, 'b')

# ---------- 轮廓：实体像素挨着透明（或阴影）的一圈描成轮廓色，再削掉尖角 ----------
solid = lambda c: c not in (None, 'SH')
edge = [(x, y) for y in range(H) for x in range(W)
        if solid(get(x, y)) and any(not solid(get(x + dx, y + dy)) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))]
for x, y in edge:
    put(x, y, 'O')
for y in range(H):
    for x in range(W):
        if get(x, y) == 'O' and sum(solid(get(x + dx, y + dy)) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))) <= 1:
            put(x, y, None)

# ---------- 天线（后右角，最后画，自带轮廓） ----------
ax, ay = FR - inset(DEP) - 6, FY - DEP + 1      # 底座位置（顶面后沿上）
for x in range(ax - 1, ax + 3):
    put(x, ay, 'O')
put(ax, ay - 1, 'O'); put(ax + 1, ay - 1, 'O')
for y in range(ay - 9, ay - 1):
    put(ax, y, 'O'); put(ax + 1, y, 'm')
put(ax + 2, ay - 9, None)
put(ax, ay - 10, 'R'); put(ax + 1, ay - 10, 'R')
put(ax, ay - 11, 'O'); put(ax + 1, ay - 11, 'O')
put(ax - 1, ay - 10, 'O'); put(ax + 2, ay - 10, 'O')


def rgba(c):
    if c is None:
        return (0, 0, 0, 0)
    if c == 'SH':
        return SHADOW
    return C[c] + (255,)


def write_png(path, scale=1, bg=None):
    raw = bytearray()
    for row in px:
        line = bytearray()
        for c in row:
            r, g, b, a = rgba(c)
            if bg is not None:
                r, g, b = (round(v * a / 255 + w * (255 - a) / 255) for v, w in zip((r, g, b), bg))
                a = 255
            line += bytes((r, g, b, a)) * scale
        for _ in range(scale):
            raw.append(0); raw += line

    def chunk(tag, data):
        return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF)

    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', W * scale, H * scale, 8, 6, 0, 0, 0)) \
        + chunk(b'IDAT', zlib.compress(bytes(raw), 9)) + chunk(b'IEND', b'')
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with open(path, 'wb') as f:
        f.write(png)


if __name__ == '__main__':
    if len(sys.argv) == 3 and sys.argv[1] == '--preview':
        write_png(sys.argv[2], scale=6, bg=(0xA0, 0xA8, 0x9C))  # 桌面底色
        print(sys.argv[2])
    else:
        out = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'AutoMod', 'ModFiles', 'Textures', 'Items', 'intercom.png'))
        write_png(out)
        print(out)
