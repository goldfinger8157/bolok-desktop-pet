#!/usr/bin/env python3
"""볼록이 스프라이트 생성기 — 표준 라이브러리만으로 assets/sprites.png·sprites.json 을 만든다.

알·아이콘·이모티콘·게임·소품 같은 공통 그림만 만든다. 캐릭터(34종 × 형태 × 볼 × 자세)는 그림 파일 없이
앱이 assets/pets/art.json 데이터로 그린다 (app/PetArt.cs). 색은 같은 art.json 의 colors 를 쓴다.

    python3 tools/sprites.py
"""
import json
import pathlib
import struct
import zlib

CELL = 40  # 한 칸 크기 (px). 앱은 이걸 정수배로 키워서 그린다

# 캐릭터 그림 데이터(색·형태·자세·종)는 assets/pets/art.json 한 곳에 있다. 앱(app/PetArt.cs)도 같은 파일로 그린다
ART = json.loads((pathlib.Path(__file__).resolve().parent.parent / 'assets/pets/art.json').read_text(encoding='utf-8'))


def _rgb(v):
    return tuple(int(v[i:i + 2], 16) for i in (1, 3, 5))


def _data(v):
    """art.json 값 → 생성기 값: '#RRGGBB' 는 (r,g,b), 객체·목록은 안쪽까지"""
    if isinstance(v, str) and v.startswith('#'):
        return _rgb(v)
    if isinstance(v, dict):
        return {k: _data(x) for k, x in v.items()}
    if isinstance(v, list):
        return [_data(x) for x in v]
    return v


C = _data(ART['colors'])  # 기본 색: 윤곽선·털·그림자·배·귀 안쪽·볼터치·눈 …




def mix(a, b, t):
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


assert CELL == ART['cell'], 'art.json 의 cell 과 생성기 CELL 이 달라요'


class Canvas:
    def __init__(self):
        self.p = [[None] * CELL for _ in range(CELL)]

    def put(self, x, y, c, clip=False):
        if 0 <= x < CELL and 0 <= y < CELL and (not clip or self.p[y][x]):
            self.p[y][x] = c

    def ell(self, cx, cy, rx, ry, c, clip=False):
        if rx <= 0 or ry <= 0:
            return
        for y in range(max(0, int(cy - ry) - 1), min(CELL, int(cy + ry) + 2)):  # 원이 걸친 범위만 (전체를 돌면 21종 만드는 데 7분)
            for x in range(max(0, int(cx - rx) - 1), min(CELL, int(cx + rx) + 2)):
                if ((x + .5 - cx) / rx) ** 2 + ((y + .5 - cy) / ry) ** 2 < 1:  # 등호를 빼야 끝에 픽셀 한 개가 안 튄다
                    self.put(x, y, c, clip)

    def poly(self, pts, c, clip=False):
        """다각형 채우기 (픽셀 가운데가 안에 들면)"""
        xs, ys = [p[0] for p in pts], [p[1] for p in pts]
        for y in range(max(0, int(min(ys)) - 1), min(CELL, int(max(ys)) + 2)):
            for x in range(max(0, int(min(xs)) - 1), min(CELL, int(max(xs)) + 2)):
                px, py, inside = x + .5, y + .5, False
                for i in range(len(pts)):
                    (x1, y1), (x2, y2) = pts[i], pts[i - 1]
                    if (y1 > py) != (y2 > py) and px < (x2 - x1) * (py - y1) / (y2 - y1) + x1:
                        inside = not inside
                if inside:
                    self.put(x, y, c, clip)

    def pix(self, c, *pts):
        for x, y in pts:
            self.put(round(x), round(y), c)

    def outline(self, c):
        edge = [(x, y) for y in range(CELL) for x in range(CELL) if not self.p[y][x] and any(
            0 <= x + a < CELL and 0 <= y + b < CELL and self.p[y + b][x + a]
            for a, b in ((1, 0), (-1, 0), (0, 1), (0, -1)))]
        for x, y in edge:
            self.p[y][x] = c


def egg(wobble, crack):
    g = Canvas()
    cx, cy = 20.5 + wobble, 25
    g.ell(cx, cy, 10, 12, C['cream'])
    for x, y, r in ((-4, -6, 2.8), (4, -2, 2.3), (-2, 6, 3), (6, 6, 1.9)):
        g.ell(cx + x, cy + y, r, r, C['fur'], True)
    g.outline(C['line'])
    x0 = 20 + wobble
    zig = [(-9, 0), (-8, -1), (-7, -2), (-6, -1), (-5, 0), (-4, 1), (-3, 0), (-2, -1), (-1, -2),
           (0, -1), (1, 0), (2, 1), (3, 0), (4, -1), (5, -2), (6, -1), (7, 0), (8, 1), (9, 0)]
    g.pix(C['line'], *[(x0 + x, 24 + y) for x, y in zig[:crack * 2]])
    return g


def icons():
    h = Canvas()  # 하트
    h.ell(15.5, 16, 5.5, 5.5, C['red'])
    h.ell(24.5, 16, 5.5, 5.5, C['red'])
    for y in range(17, 30):
        half = 10 - (y - 17) * .82
        for x in range(CELL):
            if abs(x + .5 - 20) < half:
                h.put(x, y, C['red'])
    h.outline(C['line'])
    h.ell(14.5, 14, 1.6, 1.6, C['white'], True)
    s = Canvas()  # 해바라기씨
    s.ell(20, 22, 6.5, 10, C['seed'])
    s.outline(C['line'])
    for dx in (-3, 2):
        for y in range(15, 30):
            s.put(20 + dx, y, C['stripe'], True)
    return {'heart': h, 'seed': s}


# 머리 위에 잠깐 뜨는 그림. 글자 대신이라 어느 나라 말을 쓰든 통한다
EMOTES = {
    'note': ((96, 120, 220), '...##..|...###.|...#.#.|...#...|.###...|####...|.##....'),
    'bang': ((232, 72, 92), '###|###|###|###|...|###'),
    'ask': ((96, 150, 230), '.###.|##.##|...##|..##.|..#..|.....|..#..'),
    'no': ((232, 72, 92), '##.##|.###.|..#..|.###.|##.##'),
    'star': ((250, 185, 40), '...#...|...#...|#######|.#####.|..###..|.##.##.|.#...#.'),
    'tear': ((110, 180, 240), '..#..|..#..|.###.|#####|#####|.###.'),
    'sweat': ((110, 180, 240), '.#.|.#.|###|###|.#.'),
    'zzz': ((110, 170, 235), '#####|...#.|..#..|.#...|#####'),
    'spark': ((245, 170, 40), '...#...|.#.#.#.|..###..|###.###|..###..|.#.#.#.|...#...'),
    'dots': ((92, 58, 40), '#.#.#'),
    'vein': ((232, 72, 92), '##.##|#...#|.....|#...#|##.##'),
    'heart': ((232, 72, 92), '.##.##.|#######|#######|.#####.|..###..|...#...'),
    'seed': ((92, 74, 66), '..#..|.###.|#####|#.#.#|#.#.#|.###.|..#..'),
    'sun': ((245, 170, 40), '#..#..#|.#...#.|..###..|######.|..###..|.#...#.|#..#..#'),   # 아침 인사
    'moon': ((240, 196, 70), '..###..|.##....|##.....|##.....|##.....|.##....|..###..'),  # 밤 인사
    'mail': ((96, 150, 230), '#######|##...##|#.#.#.#|#..#..#|#######'),                   # 엽서가 왔다
}


# 총쏘기 게임 그림. 글자는 #=주색, k=검정, g=진회색, l=연회색, w=흰색, r=빨강, y=금색, o=황동, b=갈색 선
PAL = {'k': (18, 18, 22), 'g': (80, 80, 88), 'l': (200, 200, 206), 'w': (255, 255, 255), 'r': (232, 72, 92),
       'y': (250, 185, 40), 'o': (190, 130, 40), 'b': (92, 58, 40), 'd': (150, 150, 160),
       'u': (96, 150, 230), 'v': (110, 190, 110), 'p': (240, 140, 170), 'n': (240, 150, 60), 'm': (170, 110, 220), 'e': (58, 126, 70)}
GAME = {
    'aim': ('....rrr....|..rr...rr..|.r.......r.|.r...r...r.|r....r....r|r..rr.rr..r|r....r....r|.r...r...r.|.r.......r.|..rr...rr..|....rrr....', 'w'),
    'hole0': ('.....l.....|.....l.....|...ggg.....|..gkkkg.l..|lgkkkkkg...|..gkkkkgll.|...gkkg....|....ggl....|......l....', None),
    'hole1': ('..l........|...l..g....|....gggg...|...gkkkkg..|...gkkkkgll|...gkkkg...|..l.ggg....|.l.....l...|........l..', None),
    'hole2': ('.........l.|....ggg.l..|..lgkkkg...|...gkkkkg..|...gkkkkg..|....gkkg...|...l.gg....|..l....l...', None),
    'bullet': ('.y.|yyy|yyy|yyy|ooo|ooo', 'b'),
    'bulletOff': ('.d.|ddd|ddd|ddd|ggg|ggg', 'b'),
    'exit': ('.rrrrrrrrrrrr.|rrrrrrrrrrrrrr|rrwwrrrrrrwwrr|rrrwwrrrrwwrrr|rrrrwwrrwwrrrr|rrrrrwwwwrrrrr|rrrrrrwwrrrrrr|rrrrrwwwwrrrrr|rrrrwwrrwwrrrr|rrrwwrrrrwwrrr|rrwwrrrrrrwwrr|rrrrrrrrrrrrrr|.rrrrrrrrrrrr.', 'b'),
    'star': ('...y...|...y...|yyyyyyy|.yyyyy.|..yyy..|.yy.yy.|.y...y.', 'b'),
    'crown': ('y...y...y|yy.yyy.yy|yyyyyyyyy|yyyyyyyyy', 'b'),
    'hit': ('rr...rr|.rr.rr.|..rrr..|.rr.rr.|rr...rr', 'w'),
    'spark': ('..y..|.yyy.|yyyyy|.yyy.|..y..', None),
    'pistol': ('kkkkkkkkkk|kggggggggk|kggkkkkkk.|kgk.k.....|kgk.......|kkk.......', 'w'),
    'shotgun': ('kkkkkkkkkkkkkkkkkk|kggggggggggggggggk|kggggggkoooooook..|kggkk..kkkkkkkk...|kkk...............', 'w'),
    'shell': ('.r.|rrr|rrr|rrr|ooo|ooo', 'b'),
    'shellOff': ('.d.|ddd|ddd|ddd|ggg|ggg', 'b'),
    'ball': ('.ldd.|lwldg|dlddg|ddggg|.ggg.', 'k'),  # 쇠공 (벽돌깨기)
    'fireball': ('.yyn.|ywynn|yynnr|nnnrr|.rrr.', 'b'),  # 불공 아이템을 먹은 쇠공
    # 벽돌깨기 아이템 캡슐: 글자 없이 그림으로 (공 둘, 양쪽 화살표, 큰 공, 불꽃, 모래시계, 하트)
    'item_split': ('.uuuuuuuuu.|uuuuuuuuuuu|uuwwuuuwwuu|uuwwuuuwwuu|uuuuuuuuuuu|.uuuuuuuuu.', 'b'),
    'item_wide': ('.vvvvvvvvv.|vvvvvvvvvvv|vwvvvvvvvwv|wwwwwwwwwww|vwvvvvvvvwv|vvvvvvvvvvv|.vvvvvvvvv.', 'b'),
    'item_slow': ('.yyyyyyyyy.|yyybbbbbyyy|yyyybbbyyyy|yyyyybyyyyy|yyyybbbyyyy|yyybbbbbyyy|.yyyyyyyyy.', 'b'),
    'item_giant': ('.mmmmmmmmm.|mmmmwwwmmmm|mmmwwwwwmmm|mmmwwwwwmmm|mmmwwwwwmmm|mmmmwwwmmmm|.mmmmmmmmm.', 'b'),
    'item_fire': ('.rrrrrrrrr.|rrrrryrrrrr|rrrryyrrrrr|rrryyyyrrrr|rrryywyyrrr|rrrryyyrrrr|.rrrrrrrrr.', 'b'),
    'item_life': ('.ppppppppp.|ppwwpppwwpp|pwwwwpwwwwp|pwwwwwwwwwp|ppwwwwwwwpp|ppppwwwpppp|.ppppppppp.', 'b'),
    'plate': ('wwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwww|llllllllllllllllllllllllllllllllll|.dddddddddddddddddddddddddddddddd.', 'g'),  # 쟁반: 윗면이 17줄
    # 대포 게임: 바주카 포탄(오른쪽을 본다), 수류탄
    'rocket': ('g.dddd.|ggddddr|g.dddd.', 'k'),
    'grenade': ('..g..|.vvv.|vvwvv|vvvvv|vvvvv|.vvv.', 'k'),
    # ICBM: 빨간 버튼(안 눌림·눌림), 미사일(오른쪽을 본다. 앱이 돌려 그린다)
    'button_up': ('...rrrrrr...|..rrwwrrrr..|..rrrrrrrr..|..rrrrrrrr..|dddddddddddd|dggggggggggd|dddddddddddd', 'b'),
    'button_down': ('............|............|..rrrrrrrr..|..rrrrrrrr..|dddddddddddd|dggggggggggd|dddddddddddd', 'b'),
    'missile': ('rr..........r..|rrrwwwwwwwwwrr.|.rrwwwwwwwwwrrr|rrrwwwwwwwwwrr.|rr..........r..', 'k'),
    'mg': ('.kkkkkkkkkkkkkkkk.|kggggggggggggggggk|kggggggkkkkkkkkk..|kggkk.kgk.........|kkk...kgk.........|......kkk.........', 'w'),
    # 밤에 잘 때 쓰는 수면 모자 (끝에 흰 방울)
    'nightcap': ('........ww|.......uww|......uu..|.....uuu..|....uuuu..|...uuuuu..|..uuuuuu..|wwwwwwwwww', 'b'),
}

# 놀러 온 친구가 두고 가는 선물 (추억 상자). 글자 없이 그림만
ITEMS = {
    'acorn': ('...b...|.bbbbb.|bbbbbbb|.nnnnn.|.nnnnn.|..nnn..|...n...', 'b'),
    'flower': ('.p...p.|ppp.ppp|.ppyppp|..pyp..|.ppppp.|...v...|..vv...|...v...', 'b'),
    'shell': ('...p...|..pwp..|.pwpwp.|pwpwpwp|pwpwpwp|.ppppp.', 'b'),
    'leaf': ('.....vv|....vvv|...vvv.|..vvv..|.vvv...|.v.....|v......', 'b'),
    'feather': ('.....u.|....uu.|...uuu.|..uuu..|.uuu...|.uu....|u......', 'b'),
    'pebble': ('.dd.dd.|ddddddd|ddddddd|.ddddd.|..ddd..|...d...', 'b'),
    'berry': ('...v...|..vv...|.rrrrr.|rrrrrrr|rrwrrrr|rrrrrrr|.rrrrr.', 'b'),
    'clover': ('...vv.vv.....|..vvvvvvv....|...vvvvv.....|vv..vvv..vv..|vvvv.v.vvvv..|.vvvveevvv...|vvvv.v.vvvv..|vv..vvv..vv..|...vvvvvv....|..vvvvvv.v...|...vv.vv..v..|..........v..|..........v..|..........v..', 'e'),  # 하트 잎 넷이 십자로, 긴 줄기 (YELL: 하트처럼 보였다 → 줄기를 더 길게)
    'sprout': ('.........vv..|.......vvvvv.|......vvvvevv|......vvvevvv|vvv..vvvevvv.|vvvv.vvevvv..|.veev.vevv...|...vvv.v.....|......v......|......v......|.....v.......|.....v.......|......v......', 'e'),  # 새싹: 왼쪽 작은 잎·오른쪽 둥근 큰 잎을 V로 떼고 잎맥, 휜 줄기 (YELL 이 보낸 그림을 참고)
    'basket': ('...nnn...|..n...n..|.n.....n.|ooooooooo|onononono|ooooooooo|.ononono.|..ooooo..', 'b'),  # 먹이 바구니 (밥을 줄 때 잠깐 나와 씨앗을 던진다)
}
def bug(i, boss=False):
    """버그 습격: 편대로 날아드는 「화면 버그」. 동글한 보라 딱정벌레, 다리 꼼지락 두 장 (i).
    보스는 칸을 꽉 채우고 눈썹이 성났다. 머리가 아래(볼록이 쪽)를 본다"""
    g = Canvas()
    body, shade, belly, leg = (160, 112, 224), (118, 80, 186), (204, 176, 246), (70, 46, 110)
    if boss:
        body, shade, belly, leg = (196, 84, 150), (150, 56, 116), (240, 160, 200), (90, 30, 70)
    cx, cy, rx, ry = (20, 16, 15, 13) if boss else (20, 22, 7, 6)  # 보스는 더듬이 끝이 칸 안에 들게
    k = 2 if boss else 1
    for s_ in (-1, 1):  # 다리 셋씩, 장마다 앞뒤로
        for n, dy in enumerate((-.5, 0, .5)):
            wig = (1 if (n + i) % 2 else -1) * k
            x0, y0 = cx + s_ * rx * .8, cy + dy * ry
            for t in range(3 * k):
                g.put(round(x0 + s_ * t), round(y0 + (wig if t >= 2 * k else 0)), leg)
    for s_ in (-1, 1):  # 더듬이 (머리 쪽 = 아래)
        for t in range(3 * k):
            g.put(round(cx + s_ * (2 + t) * k / 1.5), round(cy + ry + t), leg)
        g.ell(cx + s_ * (2 + 3 * k) * k / 1.5, cy + ry + 3 * k, 1.2 * k, 1.2 * k, (250, 220, 90))
    g.ell(cx, cy, rx, ry, shade)
    g.ell(cx, cy - .6, rx - 1, ry - 1.2, body, True)
    g.ell(cx, cy - ry * .45, rx * .55, ry * .3, belly, True)  # 등 무늬
    g.pix(shade, *[(cx, cy - ry + 2 + t) for t in range(int(ry * 1.2))])  # 날개 가운데 줄
    ex, ey, er = rx * .42, cy + ry * .45, (2.2 if boss else 1.3)
    for s_ in (-1, 1):  # 눈 (아래쪽)
        g.ell(cx + s_ * ex, ey, er, er, (255, 255, 255))
        g.ell(cx + s_ * ex, ey + .4 * k, er * .55, er * .55, (40, 24, 50))
        if boss:  # 성난 눈썹
            g.pix(leg, *[(cx + s_ * (ex - 2 + t), ey - 3 - (t if s_ < 0 else 3 - t) * .5) for t in range(4)])
    g.outline((60, 34, 80))
    return g


def nuke(i):
    """ICBM 버섯구름 여덟 장: 섬광 → 불덩이 → 기둥이 솟고 갓이 퍼짐 → 연기. 바닥 가운데가 (20, 38)"""
    g = Canvas()
    W, Y, O, R, D, S, L = (255, 255, 240), (255, 226, 90), (250, 150, 50), (214, 70, 50), (120, 60, 60), (120, 116, 120), (176, 172, 176)

    def stem(top, half, col):
        for y in range(top, 38):
            for x in range(20 - half, 20 + half + 1):
                g.put(x, y, col)

    if i == 0:
        g.ell(20, 33, 7, 5, W)
    elif i == 1:
        g.ell(20, 31, 10, 7, O); g.ell(20, 31, 7, 5, Y); g.ell(20, 31, 3, 2, W)
    elif i == 2:
        g.ell(20, 36, 9, 2, R); stem(28, 2, O); g.ell(20, 25, 9, 7, R); g.ell(20, 25, 6, 5, O); g.ell(20, 25, 3, 2, Y)
    elif i == 3:
        g.ell(20, 36, 11, 2, R); stem(20, 2, O); g.ell(20, 17, 12, 6, R); g.ell(20, 16, 9, 4, O); g.ell(20, 15, 5, 2, Y)
    elif i == 4:
        g.ell(20, 36, 12, 2, D); stem(16, 3, R); stem(18, 1, O); g.ell(20, 12, 15, 7, R); g.ell(20, 11, 11, 5, O); g.ell(20, 10, 6, 2, Y)
        g.ell(20, 21, 6, 2, R)
    elif i == 5:
        g.ell(20, 36, 12, 2, S); stem(15, 3, D); g.ell(20, 11, 16, 8, D); g.ell(20, 10, 12, 5, R); g.ell(20, 9, 6, 2, O); g.ell(20, 21, 7, 2, D)
    elif i == 6:
        g.ell(20, 36, 10, 2, S); stem(16, 2, S); g.ell(20, 10, 16, 8, S); g.ell(18, 9, 9, 4, L); g.ell(20, 21, 6, 2, S)
    else:
        for x, y, r in ((12, 9, 4), (22, 6, 5), (29, 11, 4), (19, 14, 3), (20, 24, 2), (20, 31, 2), (14, 36, 3), (26, 36, 3)):
            g.ell(x, y, r, r * .7, L if (x + y) % 2 else S)
    if i < 7:
        g.outline((70, 40, 40) if i < 6 else (80, 78, 82))
    return g


def boom(i):
    """조준경 폭발 다섯 장: 하얀 섬광 → 불꽃 → 연기"""
    g = Canvas()
    r = (5, 9, 13, 16, 18)[i]
    rings = [((255, 255, 240), (255, 230, 90)), ((255, 220, 70), (250, 140, 40)), ((250, 140, 40), (210, 60, 40)),
             ((120, 110, 110), (210, 60, 40)), ((150, 145, 145), (110, 105, 105))][i]
    g.ell(20, 20, r, r, rings[1])
    g.ell(20, 20, r * .65, r * .65, rings[0])
    if i >= 3:  # 가운데부터 걷히는 연기
        for y in range(CELL):
            for x in range(CELL):
                if ((x + .5 - 20) ** 2 + (y + .5 - 20) ** 2) < (r * .5) ** 2:
                    g.p[y][x] = None
    if i <= 2:  # 사방으로 튀는 불티
        for a in range(8):
            dx, dy = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)][a]
            for t in range(2):
                g.put(20 + dx * (r + 2 + t * 2), 20 + dy * (r + 2 + t * 2), (255, 220, 70))
    return g


# ── 게임 소품: 귀여운 것들. 40칸에 그려 앱이 2~6배로 키운다 ──
P = dict(red=(232, 72, 92), cream=(255, 243, 224), green=(110, 190, 90), dgreen=(70, 140, 60), yellow=(255, 214, 80),
         brown=(150, 100, 60), dbrown=(110, 70, 40), pink=(255, 170, 200), orange=(250, 150, 60), white=(255, 255, 255),
         sky=(170, 210, 250), pot=(205, 120, 80), cheese=(255, 210, 90), cookie=(215, 160, 95), choc=(100, 60, 40))


def face(g, cx, cy, gap=3):
    for s in (-1, 1):
        g.ell(cx + s * gap, cy, 1.1, 1.3, C['eye'])
        g.put(int(cx + s * gap - .5), int(cy - 1), C['white'])
        g.ell(cx + s * (gap + 2.5), cy + 2, 1.6, .9, C['blush'], True)
    g.pix(C['line'], (cx - 1, cy + 2), (cx, cy + 3), (cx + 1, cy + 2))


def clip_rows(g, keep):
    for y in range(CELL):
        for x in range(CELL):
            if g.p[y][x] and not keep(x, y):
                g.p[y][x] = None


def prop_mushroom():
    g = Canvas()
    g.ell(20.5, 18, 15, 11, P['red'])
    clip_rows(g, lambda x, y: y < 22)  # 갓 아랫면을 평평하게
    g.ell(20.5, 29, 6, 8, P['cream'])  # 기둥은 갓 다음에 그려야 빨갛게 안 남는다
    g.ell(20.5, 21.5, 12, 1.6, (230, 200, 180), True)
    for x, y, r in ((13, 15, 2.2), (27, 13, 2.6), (20.5, 9.5, 2), (31, 19, 1.6), (10, 20, 1.5)):
        g.ell(x, y, r, r, P['white'], True)
    g.outline(C['line'])
    face(g, 20.5, 29, 2.5)
    return g


def prop_sunflower():
    g = Canvas()
    import math as m
    for x in range(19, 22):
        for y in range(18, 38):
            g.put(x, y, P['green'])
    g.ell(14, 29, 5, 2.2, P['green'])
    g.ell(27, 33, 5, 2.2, P['green'])
    for i in range(12):
        a = i / 12 * 2 * m.pi
        g.ell(20.5 + m.cos(a) * 8.5, 13 + m.sin(a) * 8.5, 3.4, 3.4, P['yellow'])
    g.ell(20.5, 13, 6.5, 6.5, P['brown'])
    g.outline(C['line'])
    for x, y in ((17, 10), (23, 11), (20, 16), (24, 15), (16, 15)):
        g.put(x, y, P['dbrown'])
    face(g, 20.5, 13, 2.5)
    return g


def prop_cactus():
    g = Canvas()
    for y in range(28, 38):
        half = 8 - (y - 28) * .3
        for x in range(CELL):
            if abs(x + .5 - 20.5) < half:
                g.put(x, y, P['pot'])
    for x in range(11, 31):
        for y in range(27, 30):
            g.put(x, y, (225, 140, 100))
    g.ell(20.5, 18, 7, 11, P['green'])
    g.ell(11.5, 19, 3, 5.5, P['green'])
    g.ell(29.5, 15, 3, 5.5, P['green'])
    g.ell(20.5, 6.5, 2.5, 2, P['pink'])
    g.outline(C['line'])
    for x, y in ((17, 12), (24, 10), (16, 22), (25, 21), (11, 17), (30, 13)):
        g.put(x, y, (200, 240, 180))
    face(g, 20.5, 18, 2.5)
    return g


def prop_cloud():
    g = Canvas()
    for x, y, rx, ry in ((13, 23, 8, 7), (22, 18, 10, 9), (30, 23, 7, 6), (20.5, 25, 14, 5)):
        g.ell(x, y, rx, ry, P['white'])
    for x in range(8, 34):
        if g.p[28][x]:
            g.put(x, 28, P['sky'])
    g.outline((120, 150, 190))
    face(g, 20.5, 21, 3)
    return g


def prop_acorn():
    g = Canvas()
    g.ell(20.5, 25, 8, 10, (200, 140, 80))
    g.ell(20.5, 16, 10.5, 6, P['dbrown'])
    clip_rows(g, lambda x, y: True)
    for x in range(19, 22):
        for y in range(7, 11):
            g.put(x, y, P['dbrown'])
    g.outline(C['line'])
    for x in range(12, 30, 3):
        g.put(x, 15, P['brown'])
        g.put(x + 1, 18, P['brown'])
    face(g, 20.5, 26, 2.5)
    return g


def prop_carrot():
    g = Canvas()
    for y in range(13, 37):
        half = 7 - (y - 13) * .28
        for x in range(CELL):
            if abs(x + .5 - 20.5) < half:
                g.put(x, y, P['orange'])
    for x, y, rx, ry in ((16, 9, 2.5, 5), (20.5, 7, 2.5, 6), (25, 9, 2.5, 5)):
        g.ell(x, y, rx, ry, P['green'])
    g.outline(C['line'])
    for y in (19, 25, 30):
        g.pix((220, 110, 40), (18, y), (19, y))
    face(g, 20.5, 19, 2.5)
    return g


def prop_strawberry():
    g = Canvas()
    g.ell(20.5, 22, 11, 11, P['red'])
    g.ell(20.5, 28, 7, 8, P['red'])
    for x, y, rx, ry in ((15, 12, 4, 2), (26, 12, 4, 2), (20.5, 11, 2, 4)):
        g.ell(x, y, rx, ry, P['green'])
    g.outline(C['line'])
    for x, y in ((14, 18), (27, 18), (17, 27), (24, 28), (20, 31), (12, 23), (29, 24)):
        g.put(x, y, P['yellow'])
    face(g, 20.5, 21, 3)
    return g


def prop_cupcake():
    g = Canvas()
    for y in range(24, 37):
        half = 10 - (y - 24) * .25
        for x in range(CELL):
            if abs(x + .5 - 20.5) < half:
                g.put(x, y, (150, 200, 240) if (x // 3) % 2 else (120, 175, 225))
    g.ell(20.5, 21, 12.5, 6, P['pink'])
    g.ell(20.5, 16, 9, 5.5, P['pink'])
    g.ell(20.5, 11.5, 5, 4, P['pink'])
    g.ell(20.5, 6.5, 2.6, 2.6, P['red'])
    g.outline(C['line'])
    for x, y, c in ((14, 19, P['yellow']), (24, 16, (120, 200, 255)), (18, 13, P['white']), (27, 21, P['green']), (21, 20, P['yellow'])):
        g.put(x, y, c)
    face(g, 20.5, 29, 3)
    return g


def prop_cheese():
    g = Canvas()
    for y in range(14, 35):
        x0 = 34 - (y - 14) * 27 / 20
        for x in range(CELL):
            if x0 <= x <= 34:
                g.put(x, y, P['cheese'])
    for y in range(31, 35):
        for x in range(7, 35):
            g.put(x, y, (240, 185, 70))
    g.outline(C['line'])
    for x, y, r in ((27, 24, 2), (31, 18, 1.4), (17, 29, 1.6)):
        g.ell(x, y, r, r, (235, 175, 60), True)
    face(g, 25, 28, 2.5)
    return g


def prop_apple():
    g = Canvas()
    g.ell(15.5, 23, 9, 10, P['red'])
    g.ell(25.5, 23, 9, 10, P['red'])
    for x in range(19, 22):
        for y in range(7, 13):
            g.put(x, y, P['dbrown'])
    g.ell(25, 10, 4, 2, P['green'])
    g.outline(C['line'])
    g.ell(13, 18, 1.6, 2.6, P['white'], True)
    face(g, 20.5, 23, 3)
    return g


def prop_cookie():
    g = Canvas()
    g.ell(20.5, 22, 13, 12, P['cookie'])
    clip_rows(g, lambda x, y: (x + .5 - 32) ** 2 + (y + .5 - 12) ** 2 > 22)  # 한 입 베어 먹은 자리
    g.outline(C['line'])
    for x, y in ((13, 17), (24, 26), (16, 29), (27, 20), (11, 24), (20, 13)):
        g.ell(x, y, 1.3, 1.3, P['choc'], True)
    face(g, 19, 22, 3)
    return g


PROPS = {n[5:]: f for n, f in globals().items() if n.startswith('prop_')}


def gun(rows, pivot=(10, 21)):
    """총: 손잡이(pivot) 가 칸의 (10, 21) 에 오게 놓는다. 앱은 이 점을 축으로 돌린다"""
    g = Canvas()
    rows = rows.split('|')
    gx = next(i for i, c in enumerate(rows[-1]) if c != '.')
    x0, y0 = pivot[0] - gx - 1, pivot[1] - len(rows) + 1
    for y, row in enumerate(rows):
        for x, c in enumerate(row):
            if c != '.':
                g.put(x0 + x, y0 + y, PAL[c])
    g.outline(PAL['w'])
    return g


GUNS = {
    'gun_minigun': '.......kkkkk.....................|.......kgggk.....................|kkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkk|kgggggggggggkdddddddddddddddddddk|kgggggggggggkkkkkkkkkkkkkkkkkkkkk|kgggggggggggkdddddddddddddddddddk|kgggggggggggkkkkkkkkkkkkkkkkkkkkk|kgggggggggggkdddddddddddddddddddk|kkkkkgggkkkkkkkkkkkkkkkkkkkkkkkkk|...kgggk.........................|..kgggk..........................|..kkkk...........................',  # 미니건: 총신 셋
    'gun_pistol': 'kkkkkkkkkkkkkkkkkkkkk|kllllllllllllllllllllk|kggggggggggggggggggggk|kkkkgggkkkkkkkkkkkkkk.|...kgggk..............|..kgggk...............|..kggk................|..kkk.................',
    'gun_shotgun': '..........................|kkkkkkkkkkkkkkkkkkkkkkkkkk|kllllllllllllllllllllllllk|kggggggggggggggggggggggggk|kkkkkgggkkkkoooooooookkkk.|...kgggk...kooooooooook...|..kgggk....kkkkkkkkkkk....|..kggk....................|..kkk.....................',
    'gun_mg': '.......kkkk.......................|kkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkk.|kllllllllllllllllllllllllllllllllk|kgggggggggggggggggggggggggggggggk.|kkkkkgggkkkkgggggkkkkkkkkkkkkkkk..|...kgggk...kgggk..................|..kgggk....kgggk..................|..kggk......kkk...................|..kkk.............................',
}


# 총구 불꽃: 오른쪽으로 뻗는 원뿔. 왼쪽 가운데가 총구 끝에 붙는다 (칸의 x 0, y 20)
MUZZLE = [
    '.....r.......|...y.yy..r...|..yyyyyyyy...|.yywwwwwyyyr.|wwwwwwwwwwyyr|.yywwwwwyyyr.|..yyyyyyyy...|...y.yy..r...|.....r.......',
    '...r.........|....yy.y.....|..yyywyyyr...|.ywwwwwwyy...|wwwwwwwwwyyyr|.ywwwwwwyy...|..yyywyyyr...|....yy.y.....|...r.........',
]


def muzzle(rows):
    g = Canvas()
    rows = rows.split('|')
    y0 = 20 - len(rows) // 2
    for y, row in enumerate(rows):
        for x, c in enumerate(row):
            if c != '.':
                g.put(x, y0 + y, PAL[c])
    return g


DIGITS = ['###|#.#|#.#|#.#|###', '.#.|##.|.#.|.#.|###', '###|..#|###|#..|###', '###|..#|###|..#|###', '#.#|#.#|###|..#|..#',
          '###|#..|###|..#|###', '###|#..|###|#.#|###', '###|..#|.#.|.#.|.#.', '###|#.#|###|#.#|###', '###|#.#|###|..#|###']


def art(rows, outline=None, main=None):
    g = Canvas()
    rows = rows.split('|')
    x0, y0 = (CELL - len(rows[0])) // 2, (CELL - len(rows)) // 2
    for y, row in enumerate(rows):
        for x, c in enumerate(row):
            if c != '.':
                g.put(x0 + x, y0 + y, main if c == '#' else PAL[c])
    if outline:
        g.outline(PAL[outline])
    return g


def emote(color, art):
    """작은 흰 말풍선 안의 그림. 꼬리 끝은 (20, 26) — 앱이 이 점을 머리 위에 맞춘다"""
    g = Canvas()
    left, top, right, bottom = 15, 13, 25, 23  # 그림 7칸에 여백 2칸씩. 테두리까지 13칸
    for y in range(top, bottom + 1):
        for x in range(left, right + 1):
            if (x in (left, right)) and (y in (top, bottom)):
                continue  # 모서리를 한 칸 다듬는다
            g.put(x, y, C['white'])
    for i, half in enumerate((1, 0)):
        for x in range(20 - half, 20 + half + 1):
            g.put(x, bottom + 1 + i, C['white'])
    g.outline(C['line'])
    rows = art.split('|')
    x0, y0 = (left + right + 1 - len(rows[0])) // 2, (top + bottom + 1 - len(rows)) // 2
    for y, row in enumerate(rows):
        for x, c in enumerate(row):
            if c == '#':
                g.put(x0 + x, y0 + y, color)
    return g


def png(pixels, path):
    h, w = len(pixels), len(pixels[0])
    raw = b''.join(b'\0' + b''.join(bytes(p) if len(p) == 4 else bytes(p) + b'\xff' for p in row) for row in pixels)

    def chunk(t, d):
        return struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d))
    data = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
    data += chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b'')
    pathlib.Path(path).write_bytes(data)


def build():
    """(이름, [프레임 Canvas]) 행 목록"""
    rows = [('egg/idle', [egg(w, 0) for w in (0, 1, 0, -1)]),
            ('egg/hatch', [egg(w, n) for w, n in ((1, 2), (-1, 4), (1, 6), (0, 8))])]
    rows += [(f'icon/{k}', [v]) for k, v in icons().items()]
    rows += [(f'emo/{k}', [emote(*v)]) for k, v in EMOTES.items()]
    rows += [(f'game/{k}', [art(*v)]) for k, v in GAME.items()]
    rows += [(f'num/{i}', [art(d, 'b', C['white'])]) for i, d in enumerate(DIGITS)]
    rows += [('game/boom', [boom(i) for i in range(4)]), ('game/boom2', [boom(4)])]
    rows += [(f'prop/{k}', [f()]) for k, f in PROPS.items()]
    rows += [(f'game/{k}', [gun(v)]) for k, v in GUNS.items()]
    rows += [('game/nuke', [nuke(i) for i in range(8)])]
    rows += [('game/bug', [bug(0), bug(1)]), ('game/bugboss', [bug(0, True), bug(1, True)])]
    rows += [('num/+', [art('...|.#.|###|.#.|...', 'b', C['white'])]), ('num/x', [art('...|#.#|.#.|#.#|...', 'b', C['white'])])]
    rows += [(f'item/{k}', [art(*v)]) for k, v in ITEMS.items()]
    rows += [('game/flash', [art('..y..|.yyy.|yywyy|.yyy.|..y..', 'r')])]
    rows += [('game/muzzle', [muzzle(a) for a in MUZZLE])]
    return rows


def sheet(rows, cols):
    clear = (0, 0, 0, 0)
    px = [[clear] * (cols * CELL) for _ in range(len(rows) * CELL)]
    for r, (_, frames) in enumerate(rows):
        for i, g in enumerate(frames):
            for y in range(CELL):
                for x in range(CELL):
                    if g.p[y][x]:
                        px[r * CELL + y][i * CELL + x] = g.p[y][x]
    return px


def write(rows, png_path, json_path):
    cols = max(len(f) for _, f in rows)
    # JSON 을 먼저 쓴다. 떠 있는 앱은 PNG 가 바뀌면 둘 다 다시 읽는다
    meta = {'cell': CELL, 'rows': {n: [i, len(f)] for i, (n, f) in enumerate(rows)}}
    json_path.write_text(json.dumps(meta, ensure_ascii=False))
    png(sheet(rows, cols), png_path)
    return cols


def main():
    """python3 tools/sprites.py    공통 그림(알·아이콘·이모티콘·게임·소품)을 assets/sprites.png·json 으로.
    캐릭터 34종은 그림 파일 없이 앱이 assets/pets/art.json 으로 그린다 (app/PetArt.cs). README 행진 그림은 Bolok.exe --art-gif"""
    root = pathlib.Path(__file__).resolve().parent.parent
    rows = build()
    cols = write(rows, root / 'assets/sprites.png', root / 'assets/sprites.json')
    print(f'공통 {len(rows)}행 {cols}열')


if __name__ == '__main__':
    main()
