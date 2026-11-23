#!/usr/bin/env python3
"""방석·집 그림 뽑기 — 시안 PNG(배율 3, run/home/*.png)에서 assets/home.json 을 만든다. 표준 라이브러리만 쓴다.

    python3 tools/home_art.py                  # run/home/*.png → assets/home.json
    python3 tools/home_art.py --diff 그림.png cushions|houses   # 앱이 그린 --home-look 그림과 시안을 칸마다 견준다
    python3 tools/home_art.py --src 폴더       # 시안이 다른 폴더에 있을 때

시안 다섯 장의 3×3 덩어리는 한 칸도 안 어긋난다 → 3으로 나누면 그대로 배율 1 도트다.
같은 빈 물건이 밝은 띠에 한 번, 어두운 띠에 한 번 있다 → 두 띠에서 다 바탕색인 칸만 투명으로 둔다.
추출 결과는 assets/home.json 에 커밋하고, 그 뒤로는 그 파일이 원본이다 (그림은 거기서 고친다).
front·seat·sleep·top 같은 겹·자리 값은 자동으로 못 가른다 → 아래 META 표에 손으로 적는다.

시안 띠 (행 5개): 빈 것(밝음) / 햄스터 어린이 / 고양이 어른 / 빈 것(어두움) / 시바 어른(어두움)
"""
import json
import pathlib
import struct
import sys
import zlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
BG_L = (0x93, 0xA0, 0xAC)  # 밝은 띠 바탕
BG_D = (0x2A, 0x2E, 0x36)  # 어두운 띠 바탕
OUTLINE = (0x5C, 0x3A, 0x28)

# 시안 파일 → 띠 첫 y, 행 높이(띠 사이), 띠 높이. 물건 번호는 왼쪽부터 0
BANDS = {
    'cushions-1': (104, 150, 146),
    'cushions-2': (104, 150, 146),
    'cushions-volume': (104, 150, 146),
    'houses-1': (104, 222, 218),
    'houses-2': (104, 222, 218),
}
# (id, 종류, 시안 파일, 그 파일에서 몇 번째 물건) — 방석 6·8 은 볼륨판 6′·8′ 을 쓴다 (cushions-2 의 6·8 은 버린다)
ITEMS = [
    ('round', 'cushion', 'cushions-1', 0), ('tassel', 'cushion', 'cushions-1', 1), ('donut', 'cushion', 'cushions-1', 2),
    ('basket', 'cushion', 'cushions-1', 3), ('futon', 'cushion', 'cushions-1', 4),
    ('leaf', 'cushion', 'cushions-volume', 1), ('cloud', 'cushion', 'cushions-2', 1), ('heart', 'cushion', 'cushions-volume', 3),
    ('egg', 'cushion', 'cushions-2', 3), ('flower', 'cushion', 'cushions-2', 4),
    ('kennel', 'house', 'houses-1', 0), ('box', 'house', 'houses-1', 1), ('mushroom', 'house', 'houses-1', 2),
    ('teepee', 'house', 'houses-1', 3), ('igloo', 'house', 'houses-1', 4),
    ('rounddoor', 'house', 'houses-2', 0), ('wicker', 'house', 'houses-2', 1), ('cattree', 'house', 'houses-2', 2),
    ('strawberry', 'house', 'houses-2', 3), ('teapot', 'house', 'houses-2', 4),
]
LETTERS = 'abcdefghijklmnpqrstuvwxyzABCDEFGHIJKLMNPQRSTUVWXYZ'  # 'o' 는 윤곽선 전용


# ── PNG 읽기·쓰기 (8비트 RGB/RGBA, 인터레이스 없음) ──
def read_png(path):
    d = pathlib.Path(path).read_bytes()
    pos, idat = 8, b''
    while pos < len(d):
        n, t = struct.unpack('>I4s', d[pos:pos + 8])
        body = d[pos + 8:pos + 8 + n]
        pos += 12 + n
        if t == b'IHDR':
            w, h, depth, ctype = struct.unpack('>IIBB', body[:10])
            assert depth == 8 and ctype in (2, 6), 'PNG 는 8비트 RGB/RGBA 만'
        elif t == b'IDAT':
            idat += body
    raw = zlib.decompress(idat)
    bpp = 4 if ctype == 6 else 3
    stride = w * bpp
    rows, prev, p = [], bytearray(stride), 0
    for _ in range(h):
        f = raw[p]
        line = bytearray(raw[p + 1:p + 1 + stride])
        p += 1 + stride
        for i in range(stride):
            a = line[i - bpp] if i >= bpp else 0
            b = prev[i]
            c = prev[i - bpp] if i >= bpp else 0
            if f == 1:
                line[i] = (line[i] + a) & 255
            elif f == 2:
                line[i] = (line[i] + b) & 255
            elif f == 3:
                line[i] = (line[i] + ((a + b) >> 1)) & 255
            elif f == 4:
                pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                line[i] = (line[i] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        rows.append(line)
        prev = line
    return Img(w, h, bpp, rows)


class Img:
    def __init__(self, w, h, bpp, rows):
        self.w, self.h, self.bpp, self.rows = w, h, bpp, rows

    def px(self, x, y):
        i = x * self.bpp
        return tuple(self.rows[y][i:i + 3])


def write_png(path, w, h, pix):
    """pix[y][x] = (r,g,b)"""
    raw = b''.join(b'\x00' + b''.join(bytes(p) for p in row) for row in pix)

    def chunk(t, body):
        c = struct.pack('>I', len(body)) + t + body
        return c + struct.pack('>I', zlib.crc32(t + body) & 0xffffffff)
    pathlib.Path(path).write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0))
                                   + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))


def hexc(c):
    return '#%02X%02X%02X' % c


# ── 시안에서 물건 잘라 내기 ──
def find_runs(img, y0, h, bg):
    """띠 안에서 바탕색이 아닌 열을 가로로 묶는다 (빈 열 14px 넘으면 다음 물건). 왼쪽 위 글자 줄(x<100, y<띠+24)과 왼쪽 바깥 여백(x<18)은 뺀다"""
    on = []
    for x in range(img.w):
        hit = False
        if x >= 18:
            for y in range(y0 + (24 if x < 100 else 0), y0 + h):
                if img.px(x, y) != bg:
                    hit = True
                    break
        on.append(hit)
    runs, x = [], 0
    while x < img.w:
        if not on[x]:
            x += 1
            continue
        s, gap, e = x, 0, x
        while x < img.w and (on[x] or gap < 14):
            if on[x]:
                e, gap = x, 0
            else:
                gap += 1
            x += 1
        runs.append((s, e + 1))
    return runs


def bbox_y(img, x0, x1, y0, h, bg):
    ys = [y for y in range(y0, y0 + h) if any(img.px(x, y) != bg for x in range(x0, x1) if x >= 100 or y >= y0 + 24)]
    return min(ys), max(ys) + 1


class Cut:
    """시안 한 장에서 잘라 낸 물건 하나: 시안 좌표 원점 + 가로·세로 칸 수 + 칸 색 격자 (None 이면 투명)"""

    def __init__(self, ident, kind, fname, x0, y0, cw, ch, cells, odd):
        self.id, self.kind, self.fname, self.x0, self.y0, self.cw, self.ch, self.cells, self.odd = ident, kind, fname, x0, y0, cw, ch, cells, odd


def cut_items(src):
    imgs = {f: read_png(src / (f + '.png')) for f in BANDS}
    runs = {}
    for f, (y0, step, h) in BANDS.items():
        runs[f] = find_runs(imgs[f], y0, h, BG_L)
        rd = find_runs(imgs[f], y0 + step * 3, h, BG_D)
        assert runs[f] == rd, f'{f}: 밝은 띠와 어두운 띠의 물건 자리가 다르다 {runs[f]} {rd}'
    out = []
    for ident, kind, f, n in ITEMS:
        img = imgs[f]
        y0, step, h = BANDS[f]
        xa, xb = runs[f][n]
        ya, yb = bbox_y(img, xa, xb, y0, h, BG_L)
        yd, ye = bbox_y(img, xa, xb, y0 + step * 3, h, BG_D)
        assert (ya, yb) == (yd - step * 3, ye - step * 3), f'{ident}: 밝은·어두운 띠의 높이가 다르다'
        assert (xb - xa) % 3 == 0 and (yb - ya) % 3 == 0, f'{ident}: 3 의 배수가 아니다 {xb - xa}x{yb - ya}'
        cw, ch = (xb - xa) // 3, (yb - ya) // 3
        cells, odd = [], 0
        for cy in range(ch):
            row = []
            for cx in range(cw):
                if xa + cx * 3 < 100 and ya + cy * 3 < y0 + 24:  # 「빈 집」「어두운 바탕」 글자 자리 (물건이 글자 줄까지 올라와도 글자는 뺀다)
                    row.append(None)
                    continue
                blk = {img.px(xa + cx * 3 + i, ya + cy * 3 + j) for i in range(3) for j in range(3)}
                dk = {img.px(xa + cx * 3 + i, step * 3 + ya + cy * 3 + j) for i in range(3) for j in range(3)}
                if len(blk) != 1 or len(dk) != 1:
                    odd += 1  # 3×3 이 한 색이 아니다
                l, d = next(iter(blk)), next(iter(dk))
                if l == BG_L and d == BG_D:
                    row.append(None)
                elif l == d:
                    row.append(l)
                else:
                    odd += 1  # 두 띠가 다르다: 반투명이거나 글자가 섞였다
                    row.append(l)
            cells.append(row)
        out.append(Cut(ident, kind, f, xa, ya, cw, ch, cells, odd))
    return out

# ── 손으로 적는 값: 시안 둘째·셋째·다섯째 줄(펫이 있는 줄)을 앱 펫 그림과 맞대어 잰 것 (2026-11-23) ──
# seat: 펫 칸(40×40)의 아래 가운데가 놓이는 물건 칸 자리 (x, y). x 는 「몸통 네모의 가운데」가 닿을 자리 (시안이 몸통 가운데로 맞췄다:
#       어린이 20.5, 어른 22.0 → 펫 칸을 floor(x − 몸통 가운데 + .5) 만큼 옮긴다), y 는 펫 칸 아래 줄. 집은 빼꼼(idle) 때 자리
# sleep: 잘 때 seat 보다 더 내려가는 칸 (시안은 +3, 동그란 문은 −3 — 둥근 구멍 위쪽에서 잔다)
# front_cols: 앞 겹이 시작하는 줄을 열마다 (그 열의 물건 칸을 거기부터 아래까지가 펫을 덮는다). 시안 세 마리가 가려진 자리에서 뽑은 값
# door_to: 집 문 안(어두운 색 덩어리) 아래로 문턱 위쪽 몇 줄까지 펫이 보이나 (마지막 줄). door_mirror: 둥근 구멍은 위쪽 줄을 아래로 거울처럼 접어 닫는다
# top: 캣타워 어른이 올라가 자는 자리 (x, y)
META = {
    'round': {'seat': (23, 16), 'front_cols': [10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 9, 9, 10, 10, 10, 10, 10, 10, 10, 11, 11, 11, 11, 11, 11, 11, 11, 10, 10, 10, 10, 10, 10, 10, 9, 9, 9, 10, 10, 10, 10, 10, 10, 10, 10, 10]},
    'tassel': {'seat': (26, 16), 'front_cols': [11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 10, 10, 10, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 10, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11, 11]},
    'donut': {'seat': (26, 25), 'front_cols': [14, 14, 14, 14, 14, 14, 14, 14, 14, 13, 13, 15, 16, 17, 18, 18, 19, 19, 19, 19, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 19, 19, 19, 19, 19, 18, 17, 16, 15, 13, 13, 14, 14, 14, 14, 14, 14, 14, 14, 14]},
    'basket': {'seat': (23, 22), 'front_cols': [12, 12, 12, 12, 12, 12, 12, 11, 12, 12, 12, 12, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 12, 12, 12, 12, 11, 12, 12, 12, 12, 12, 12, 12]},
    'futon': {'seat': (25, 17)},
    'leaf': {'seat': (27, 17), 'front_cols': [14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 12, 12, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13]},
    'cloud': {'seat': (27, 18), 'front_cols': [12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 11, 11, 12, 12, 12, 12, 12, 12, 12, 13, 13, 13, 13, 13, 13, 13, 13, 12, 12, 12, 12, 12, 12, 12, 11, 11, 11, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12, 12]},
    'heart': {'seat': (23, 26), 'front_cols': [20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 19, 19, 19, 19, 19, 19, 19, 20, 20, 21, 21, 21, 22, 22, 21, 21, 21, 20, 20, 19, 19, 19, 19, 19, 19, 19, 19, 20, 20, 20, 20, 20, 20, 20, 20, 20]},
    'egg': {'seat': (26, 14), 'front_cols': [8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 7, 7, 8, 8, 8, 8, 8, 8, 8, 9, 9, 9, 9, 9, 9, 9, 9, 8, 8, 8, 8, 8, 8, 8, 7, 7, 7, 8, 8, 8, 8, 8, 8, 8, 8, 8]},
    'flower': {'seat': (24, 20), 'front_cols': [17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 16, 16, 16, 16, 16, 16, 15, 16, 16, 16, 16, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17]},
    'kennel': {'seat': (32, 62), 'sleep': 3, 'door_to': 61},
    'box': {'seat': (28, 55), 'sleep': 3, 'door_to': 54},
    'mushroom': {'seat': (31, 56), 'sleep': 3, 'door_to': 55},
    'teepee': {'seat': (30, 66), 'sleep': 3, 'door_to': 65},
    'igloo': {'seat': (31, 49), 'sleep': 3, 'door_to': 48},
    'rounddoor': {'seat': (24, 62), 'sleep': -3, 'door_mirror': 53},
    'wicker': {'seat': (28, 58), 'sleep': 3, 'door_to': 57},
    'cattree': {'seat': (18, 58), 'sleep': 3, 'top': (18, 31), 'door_mirror': 47, 'front_cols': [24, 24, 24, 24, 24, 23, 23, 24, 25, 25, 25, 25, 25, 25, 26, 26, 26, 26, 26, 26, 26, 26, 25, 25, 25, 25, 25, 25, 25, 23, 23, 23, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24]},
    'strawberry': {'seat': (28, 61), 'sleep': 3, 'door_to': 60},
    'teapot': {'seat': (29, 52), 'sleep': 3, 'door_to': 51},
}


# 앞 겹 그림: 펫이 있을 때만 물건 위에 덧그리는 그림 (이불 요 세트 — 시안이 펫 줄에서 이불을 턱까지 끌어올려 다시 그렸다).
# 시안 세 줄(어린이·고양이·시바)에서 셋이 같은 색인 칸 중 펫 그림이 아닌 칸을 뽑은 것 (2026-11-23). x, y: 물건 칸 좌표의 왼쪽 위, pal: 이 그림만의 글자 → 색, rows: 글자 그림 (. 은 덧그리지 않는다)
# 이 칸들이 앞 겹이다 (펫을 가린다) — front_cols 는 쓰지 않는다
COVER = {
    'futon': {'x': 0, 'y': 6, 'pal': {"a": "#8CC8AE", "b": "#5C3A28", "c": "#FFF6E4", "d": "#FFE08A", "e": "#B8E4D0", "f": "#FFFFFF", "g": "#E4E8EE", "h": "#D8F2E6", "i": "#FFD8DE", "j": "#EDAFBC"},
      'rows': [
        '..............bbbbbbb.b.b..bbbbbbbbbb.............',
        '............bffffffffffffffffffffffff.b...........',
        '...........ffggggggggggggggggggggggggff...........',
        '.....ii...fggeeecccccdddddeeeeecccccdggf...ii.....',
        '....ibb...gcccccdddddeeeeecccccdddddeeeg...bbi....',
        '...ibff...ecccccdddddeeeeecccccdddddeeee...ffbi...',
        '....fgg.........dddddeeeeecccccd...........ggf....',
        '.bibgdeeeeecccccdddddeeeeecccccdddddeeeeeccccgbib.',
        '.bbfeecccccdddddeeeeecccccdddddeeeeecccccdddddfbb.',
        'bibgeecccccdddddeeeeecccccdddddeeeeecccccdddddgbib',
        'bjbeeecccccdddddeeeeecccccdddddeeeeecccccdddddebjb',
        'bbaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaabb',
        'bbaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaabb',
        '.baahaahaahaahaahaahaahaahaahaahaahaahaahaahaahab.',
        '.baaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaab.',
        '..bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb..'
      ]},
}


# ── 문 안·앞 겹 ──
def is_dark(c):
    return max(c) < 0x60 and c != OUTLINE  # 문 안 어두운 갈색(#4A3328)·이글루 남색(#3A4658)·그 사이 음영


def door_cells(cut, m):
    """집 문 안: 어두운 색으로 이어진 가장 큰 덩어리 + 문턱 위쪽 줄(door_to) 또는 둥근 구멍 아랫부분(door_mirror)"""
    dark = {(x, y) for y in range(cut.ch) for x in range(cut.cw) if cut.cells[y][x] and is_dark(cut.cells[y][x])}
    seen, best = set(), []
    for p in sorted(dark):
        if p in seen:
            continue
        stack, comp = [p], []
        seen.add(p)
        while stack:
            a = stack.pop()
            comp.append(a)
            for d in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                q = (a[0] + d[0], a[1] + d[1])
                if q in dark and q not in seen:
                    seen.add(q)
                    stack.append(q)
        if len(comp) > len(best):
            best = comp
    door = set(best)
    top, last = min(y for _, y in door), max(y for _, y in door)
    if 'door_mirror' in m:  # 둥근 구멍: 위쪽 줄을 맨 아래 줄(door_mirror)에 맞춰 접어 아래를 닫는다
        for y in range(last + 1, m['door_mirror'] + 1):
            src = top + (m['door_mirror'] - y)
            door |= {(x, y) for (x, yy) in list(door) if yy == src}
    elif 'door_to' in m:  # 문턱: 마지막 줄의 가로 폭 그대로 아래로 (펫 몸 바닥이 문턱 위에 걸친다)
        xs = [x for x, y in door if y == last]
        for y in range(last + 1, m['door_to'] + 1):
            door |= {(x, y) for x in range(min(xs), max(xs) + 1) if cut.cells[y][x]}
    return door


def front_cells(cut, m):
    """앞 겹: 열마다 front_cols[x] 줄부터 아래의 물건 칸"""
    cols = m.get('front_cols')
    if not cols:
        return set()
    assert len(cols) == cut.cw, f'{cut.id}: front_cols 길이 {len(cols)} != {cut.cw}'
    return {(x, y) for x in range(cut.cw) for y in range(cols[x], cut.ch) if cut.cells[y][x]}


def cover_cells(cover):
    return {(cover['x'] + x, cover['y'] + y) for y, row in enumerate(cover['rows']) for x, ch in enumerate(row) if ch != '.'}


def runs(cells):
    """칸 모음 → [[y, x0, x1], ...] (줄마다 이어진 구간)"""
    out = []
    for y in sorted({y for _, y in cells}):
        xs = sorted(x for x, yy in cells if yy == y)
        a = b = xs[0]
        for x in xs[1:]:
            if x == b + 1:
                b = x
            else:
                out.append([y, a, b])
                a = b = x
        out.append([y, a, b])
    return out


# ── assets/home.json ──
def build(cuts):
    items = {}
    for c in cuts:
        m = META[c.id]
        cnt = {}
        for row in c.cells:
            for p in row:
                if p:
                    cnt[p] = cnt.get(p, 0) + 1
        pal, letters = {}, iter(LETTERS)
        if OUTLINE in cnt:
            pal['o'] = hexc(OUTLINE)
        for col in sorted(cnt, key=lambda k: (-cnt[k], k)):
            if col != OUTLINE:
                pal[next(letters)] = hexc(col)
        back = {v: k for k, v in pal.items()}
        it = {'kind': c.kind, 'w': c.cw, 'h': c.ch, 'pal': pal,
              'rows': [''.join(back[hexc(p)] if p else '.' for p in row) for row in c.cells],
              'seat': list(m['seat'])}
        door = door_cells(c, m) if c.kind == 'house' else set()
        if door:
            it['door'] = runs(door)
        cover = COVER.get(c.id)
        fr = (cover_cells(cover) if cover else front_cells(c, m)) - door  # 문 안은 앞 겹이 아니다 (캣타워: 구멍 안 펫은 보이고, 상자 위 방석 앞 겹만 가린다)
        if fr:
            it['front'] = runs(fr)
        if c.kind == 'house':
            it['sleep'] = m['sleep']
        if 'top' in m:
            it['top'] = list(m['top'])
        if cover:  # 앞 겹 그림: 색은 물건 색표에 합친다 (없는 색은 새 글자)
            back2 = dict(back)
            for ch, col in cover['pal'].items():
                if col not in back2:
                    back2[col] = next(letters)
                    pal[back2[col]] = col
            it['cover'] = {'x': cover['x'], 'y': cover['y'], 'rows': [''.join(back2[cover['pal'][ch]] if ch != '.' else '.' for ch in row) for row in cover['rows']]}
        items[c.id] = it
    return items


def fmt(items):
    """보기 좋게: 물건 하나는 머리 줄·색표·문·앞 겹·그림 줄이 한 줄씩"""
    about = ('방석·집 그림. tools/home_art.py 가 시안(run/home/*.png)에서 뽑은 것이고, 그 뒤로는 이 파일이 원본이다. '
             'rows: 칸 그림 (글자 한 칸 = pal 색 하나, . 은 투명, o 는 윤곽선). door: 집 문 안(펫이 보이는 자리), 줄마다 [줄, 처음 칸, 끝 칸]. '
             'front: 펫을 덮는 앞 겹 (같은 꼴). seat: 펫 칸 아래 가운데가 놓이는 칸 자리 [x, y] (x 는 펫 몸통 네모의 가운데가 닿을 자리, 집은 빼꼼 때 y). '
             'sleep: 잘 때 더 내려가는 칸. top: 캣타워 어른이 올라가 자는 자리')
    out = ['{', ' "about": ' + json.dumps(about, ensure_ascii=False) + ',', ' "items": {']
    names = list(items)
    for n, name in enumerate(names):
        it = items[name]
        out.append(f'  "{name}": {{')
        out.append(f'   "kind": "{it["kind"]}", "w": {it["w"]}, "h": {it["h"]}, "seat": {json.dumps(it["seat"])},'
                   + (f' "sleep": {it["sleep"]},' if 'sleep' in it else '') + (f' "top": {json.dumps(it["top"])},' if 'top' in it else ''))
        out.append('   "pal": ' + json.dumps(it['pal'], ensure_ascii=False) + ',')
        for key in ('door', 'front'):
            if key in it:
                out.append(f'   "{key}": ' + json.dumps(it[key], separators=(',', ':')) + ',')
        if 'cover' in it:
            out.append('   "cover": {"x": %d, "y": %d, "rows": [' % (it['cover']['x'], it['cover']['y']))
            out.append(',\n'.join('    "%s"' % r for r in it['cover']['rows']))
            out.append('   ]},')
        out.append('   "rows": [')
        for i, r in enumerate(it['rows']):
            out.append(f'    "{r}"' + (',' if i < len(it['rows']) - 1 else ''))
        out.append('   ]')
        out.append('  }' + (',' if n < len(names) - 1 else ''))
    out += [' }', '}']
    return '\n'.join(out) + '\n'


def main(argv):
    src = ROOT / 'run/home'
    if '--src' in argv:
        src = pathlib.Path(argv[argv.index('--src') + 1])
    if '--diff' in argv:
        rest = [a for a in argv if a != '--src' and a != str(src)]
        return diff(rest[rest.index('--diff') + 1:], src)
    cuts = cut_items(src)
    for c in cuts:
        if c.odd:
            print(f'경고: {c.id} 에 어긋난 3×3 덩어리·두 띠가 다른 칸 {c.odd}개')
    items = build(cuts)
    out = ROOT / 'assets/home.json'
    out.write_text(fmt(items), encoding='utf-8')
    json.loads(out.read_text(encoding='utf-8'))  # 깨지지 않았나
    print(f'{out}: {len(items)}개, {out.stat().st_size // 1024}KB')
    for c in cuts:
        it = items[c.id]
        print(f'  {c.id:10} {c.cw}x{c.ch} 색 {len(it["pal"])}'
              + (f' 문 {sum(b - a + 1 for _, a, b in it["door"])}칸' if 'door' in it else '')
              + (f' 앞 겹 {sum(b - a + 1 for _, a, b in it["front"])}칸' if 'front' in it else ''))
    return 0


def diff(argv, src):
    """--diff 그림.png cushions|houses [비교그림 앞이름]: 앱이 그린 --home-look 그림을 시안과 칸마다 견준다.
    빈 줄(0·3)은 물건 칸 전체가 같아야 하고(다르면 종료 코드 1), 펫 줄(1·2·4)은 눈으로 볼 참고 숫자 (펫 그림이 시안 뒤로 바뀌었을 수 있다).
    앞이름을 주면 시안 | 앱 을 나란히 놓은 비교 그림을 앞이름-1.png, 앞이름-2.png 로 쓴다 (물건 다섯씩)"""
    look = read_png(argv[0])
    house = argv[1] == 'houses'
    tw, th, bottom = (80, 88, 4) if house else (64, 64, 4)  # app/Home/HomeLook.cs 의 Tile 과 같게
    cuts = [c for c in cut_items(src) if (c.kind == 'house') == house]
    imgs = {f: read_png(src / (f + '.png')) for f in BANDS}
    bad_empty = 0
    wins = []  # 비교 그림용 (c, 시안 창, 앱 창)
    print(f'{"물건":10} {"빈(밝)":>6} {"어린이":>6} {"고양이":>6} {"빈(어둠)":>7} {"시바":>6}   (다른 칸 수)')
    for i, c in enumerate(cuts):
        y0, step, h = BANDS[c.fname]
        mock = imgs[c.fname]
        ax0 = i * tw * 3 + (tw - c.cw) // 2 * 3  # 앱 그림에서 물건 왼쪽 위 (줄 0)
        ay0 = (th - bottom - c.ch) * 3
        counts = []
        # 비교 창 (칸): 가로 물건 폭 ± 8, 세로 물건 위 top 칸부터 아래 4칸
        top = 12 if house else 30
        for r in range(5):
            n = 0
            for cy in range(-top, c.ch + 4):
                for cx in range(-8, c.cw + 8):
                    mx, my = c.x0 + cx * 3, c.y0 + r * step + cy * 3
                    px, py = ax0 + cx * 3, r * th * 3 + ay0 + cy * 3
                    if mx < 18 or mx + 3 > mock.w or my < y0 + r * step or my + 3 > y0 + r * step + h:
                        continue
                    if my < y0 + r * step + 24 and mx < 190:
                        continue  # 시안의 글자 줄 (「햄스터 어린이 · 빼꼼」 같은)
                    if px < 0 or px + 3 > look.w or py < r * th * 3 or py + 3 > (r + 1) * th * 3:
                        continue
                    if mock.px(mx, my) != look.px(px, py):
                        n += 1
            counts.append(n)
            if r in (0, 3):
                bad_empty += n
        print(f'{c.id:10} ' + ' '.join(f'{n:>7}' for n in counts))
        wins.append((c, ax0, ay0, top))
    print('빈 줄 어긋난 칸 합계:', bad_empty)
    if len(argv) > 2:
        pad = 8
        for part in range(0, len(wins), 5):
            group = wins[part:part + 5]
            ww = [(c.cw + 16) * 3 for c, *_ in group]
            wh = max((c.ch + 4 + top) * 3 for c, _, _, top in group)
            W = sum(w * 2 + pad for w in ww) + pad
            H = 5 * (wh + pad) + pad
            pix = [[(0xF2, 0xF3, 0xF5)] * W for _ in range(H)]
            x = pad
            for (c, ax0, ay0, top), w in zip(group, ww):
                y0, step, h = BANDS[c.fname]
                mock = imgs[c.fname]
                for r in range(5):
                    oy = pad + r * (wh + pad)
                    for yy in range((c.ch + 4 + top) * 3):
                        for xx in range(w):
                            mx, my = c.x0 + (xx - 8 * 3), c.y0 + r * step + (yy - top * 3)
                            if 18 <= mx < mock.w and y0 + r * step <= my < y0 + r * step + h and not (my < y0 + r * step + 24 and mx < 190):
                                pix[oy + yy][x + xx] = mock.px(mx, my)
                            px, py = ax0 + (xx - 8 * 3), r * th * 3 + ay0 + (yy - top * 3)
                            if 0 <= px < look.w and r * th * 3 <= py < (r + 1) * th * 3:
                                pix[oy + yy][x + w + xx] = look.px(px, py)
                x += w * 2 + pad
            path = f'{argv[2]}-{part // 5 + 1}.png'
            write_png(path, W, H, pix)
            print('비교 그림:', path)
    return 1 if bad_empty else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
