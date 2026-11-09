#!/usr/bin/env python3
"""学徒对讲机的像素图（32x32 = 2x2 格，和游戏物品同为 100 PPU）。

用法：python3 art/intercom.py  → 写出 src/AutoMod/ModFiles/Textures/Items/intercom.png
     python3 art/intercom.py --preview out.png  → 另存一张放大 10 倍、带格线的预览

竖放的石墨机身，浅色墨水屏，扬声器格栅，红绿两个物理键，右侧一个侧键。
游戏按每格中心像素是否不透明决定占格，四格的中心都必须落在机身上，天线不能画出图外。
"""
import os
import struct
import sys
import zlib

C = {
    'O': (0x29, 0x22, 0x34),  # 轮廓
    'k': (0x37, 0x35, 0x41),  # 天线暗面
    'm': (0x5F, 0x68, 0x68),  # 屏上的字
    'n': (0x1E, 0x1B, 0x25),  # 屏幕黑边
    'x': (0x4A, 0x4E, 0x58),  # 石墨亮边
    'z': (0x2D, 0x2F, 0x38),  # 石墨暗
    'P': (0xD8, 0xDA, 0xD0),  # 墨水屏
    'K': (0x3C, 0x3F, 0x48),  # 石墨机身
    'E': (0x5A, 0x5E, 0x6A),  # 机身受光边
    'R': (0xB0, 0x4A, 0x3E),  # 红键
    'r': (0x7A, 0x33, 0x2E),  # 红键暗部
    'G': (0x5E, 0x9C, 0x4E),  # 绿键
    'g': (0x3E, 0x6B, 0x36),  # 绿键暗部
    'L': (0xB6, 0xF0, 0x5A),  # 指示灯
}
W = H = 32
px = [[None] * W for _ in range(H)]


def put(x, y, c):
    if 0 <= x < W and 0 <= y < H:
        px[y][x] = c


def rect(x0, y0, x1, y1, c):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            put(x, y, c)


# 机身和侧键
rect(6, 5, 25, 30, 'K')
for x, y in ((6, 5), (25, 5), (6, 30), (25, 30)):
    put(x, y, None)
rect(26, 9, 27, 12, 'x')
# 光从左上来
rect(7, 6, 7, 29, 'E'); rect(7, 6, 24, 6, 'E')
rect(24, 7, 24, 29, 'z'); rect(8, 29, 24, 29, 'z')
# 墨水屏
rect(9, 8, 22, 16, 'n'); rect(10, 9, 21, 15, 'P')
rect(11, 10, 18, 10, 'm'); rect(11, 12, 16, 12, 'm'); rect(11, 14, 19, 14, 'm')
# 扬声器格栅
for y in (18, 20, 22):
    rect(10, y, 21, y, 'z'); rect(10, y + 1, 21, y + 1, 'x')
# 两个物理键和指示灯
rect(10, 25, 13, 27, 'R'); rect(10, 27, 13, 27, 'r')
rect(18, 25, 21, 27, 'G'); rect(18, 27, 21, 27, 'g')
put(15, 26, 'L'); put(16, 26, 'L')
# 轮廓
solid = [[v is not None for v in row] for row in px]
for y in range(H):
    for x in range(W):
        if solid[y][x] and any(not (0 <= x + a < W and 0 <= y + b < H and solid[y + b][x + a]) for a, b in ((1, 0), (-1, 0), (0, 1), (0, -1))):
            px[y][x] = 'O'
# 天线
rect(21, 2, 22, 4, 'O'); put(22, 3, 'k'); put(22, 4, 'k')
rect(21, 1, 22, 2, 'R')

bad = [(x, y) for x, y in ((8, 7), (24, 7), (8, 23), (24, 23)) if px[y][x] is None]
if bad:
    raise SystemExit(f'这些格子中心透明，游戏会当成空格：{bad}')


def png(path, rgba, w, h):
    raw = b''.join(b'\0' + bytes(v for p in rgba[y * w:(y + 1) * w] for v in p) for y in range(h))

    def chunk(tag, data):
        return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data))
    with open(path, 'wb') as f:
        f.write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(raw)) + chunk(b'IEND', b''))


if len(sys.argv) == 3 and sys.argv[1] == '--preview':
    s, bg, line = 10, (0xA0, 0xA8, 0x9C, 255), (0x7A, 0x33, 0x2E, 255)
    out = []
    for Y in range(H * s):
        for X in range(W * s):
            v = px[Y // s][X // s]
            on_line = X % (16 * s) == 0 or Y % (16 * s) == 0
            out.append(line if on_line else C[v] + (255,) if v else bg)
    png(sys.argv[2], out, W * s, H * s)
else:
    here = os.path.dirname(os.path.abspath(__file__))
    png(os.path.join(here, '..', 'src', 'AutoMod', 'ModFiles', 'Textures', 'Items', 'intercom.png'),
        [C[v] + (255,) if v else (0, 0, 0, 0) for row in px for v in row], W, H)
