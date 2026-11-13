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
    'nowin': ((232, 72, 92), '#########|#########|#.......#|#.#...#.#|#..#.#..#|#...#...#|#..#.#..#|#.#...#.#|#########'),  # 게임에 쓸 창이 없다 (X 표 창)
}


# 총쏘기 게임 그림. 글자는 #=주색, k=검정, g=진회색, l=연회색, w=흰색, r=빨강, y=금색, o=황동, b=갈색 선
PAL = {'k': (18, 18, 22), 'g': (80, 80, 88), 'l': (200, 200, 206), 'w': (255, 255, 255), 'r': (232, 72, 92),
       'y': (250, 185, 40), 'o': (190, 130, 40), 'b': (92, 58, 40), 'd': (150, 150, 160),
       'u': (96, 150, 230), 'v': (110, 190, 110), 'p': (240, 140, 170), 'n': (240, 150, 60), 'm': (170, 110, 220), 'e': (58, 126, 70), 'c': (80, 214, 226)}
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
    'tile': ('wwwwwgwww|llllgllll|lllggllll|lllllglll|llllgllll|dddgddddd', 'k'),  # 금 간 조각 (벽돌깨기 끝 화면: 깬 조각 수 앞에)
    # 대포 게임: 바주카 포탄(오른쪽을 본다), 수류탄
    'rocket': ('g.dddd.|ggddddr|g.dddd.', 'k'),
    'grenade': ('..g..|.vvv.|vvwvv|vvvvv|vvvvv|.vvv.', 'k'),
    # ICBM: 빨간 버튼(안 눌림·눌림), 미사일(오른쪽을 본다. 앱이 돌려 그린다)
    'button_up': ('...rrrrrr...|..rrwwrrrr..|..rrrrrrrr..|..rrrrrrrr..|dddddddddddd|dggggggggggd|dddddddddddd', 'b'),
    'button_down': ('............|............|..rrrrrrrr..|..rrrrrrrr..|dddddddddddd|dggggggggggd|dddddddddddd', 'b'),
    'missile': ('rr..........r..|rrrwwwwwwwwwrr.|.rrwwwwwwwwwrrr|rrrwwwwwwwwwrr.|rr..........r..', 'k'),
    'mg': ('.kkkkkkkkkkkkkkkk.|kggggggggggggggggk|kggggggkkkkkkkkk..|kggkk.kgk.........|kkk...kgk.........|......kkk.........', 'w'),
    # 버그 떼: 벌레가 떨어뜨리는 보석, 칼 (레벨이 오를 때 뜨는 그림)
    'gem': ('..c..|.cwc.|cwccc|ccccu|.ccu.|..u..', 'b'),
    'sword': ('.......ll|......lwl|.....lwl.|o...lwl..|.o.lwl...|..owl....|..boo....|.b...o...|b........', 'b'),
    # 버그 떼: 자석 (주우면 한동안 보석이 다 날아온다). 빨간 말굽에 은빛 끝
    'magnet': ('wl.....wl|ll.....ll|ll.....ll|rr.....rr|rp.....rr|rp.....rr|rrr...rrr|.rrrrrrr.|..rrrrr..', 'b'),
    # 버그 떼: 신발 (주우면 그 판 내내 빨라진다). 파란 운동화에 흰 밑창·흰 코, 오른쪽을 본다
    'shoe': ('.uuu......|.uuuu.....|.uuuwu....|.uuuuwuu..|uuuuuuuuww|wwwwwwwwww', 'b'),
    # 밤에 잘 때 쓰는 수면 모자 (끝에 흰 방울)
    'nightcap': ('........ww|.......uww|......uu..|.....uuu..|....uuuu..|...uuuuu..|..uuuuuu..|wwwwwwwwww', 'b'),
}

# 놀러 온 친구가 두고 가는 선물 (추억 상자). 글자 없이 그림만
ITEMS = {
    'acorn': ('...b...|.bbbbb.|bbbbbbb|.nnnnn.|.nnnnn.|..nnn..|...n...', 'b'),
    'flower': ('.p...p.|ppp.ppp|.ppyppp|..pyp..|.ppppp.|...v...|..vv...|...v...', 'b'),
    'shell': ('...p...|..pwp..|.pwpwp.|pwpwpwp|pwpwpwp|.ppppp.', 'b'),
    'leaf': ('.....vvvvv.|....vvvvev.|...vvvvevv.|..vvvvevv..|..vvvevvv..|..vvevvv...|..vevvv....|..evv......|.e.........|e..........', 'e'),
    'feather': ('.....u.|....uu.|...uuu.|..uuu..|.uuu...|.uu....|u......', 'b'),
    'pebble': ('.dd.dd.|ddddddd|ddddddd|.ddddd.|..ddd..|...d...', 'b'),
    'berry': ('...v...|..vv...|.rrrrr.|rrrrrrr|rrwrrrr|rrrrrrr|.rrrrr.', 'b'),
    'clover': ('...vv.vv.....|..vvvvvvv....|...vvvvv.....|vv..vvv..vv..|vvvv.v.vvvv..|.vvvveevvv...|vvvv.v.vvvv..|vv..vvv..vv..|...vvvvvv....|..vvvvvv.v...|...vv.vv..v..|..........v..|..........v..|..........v..', 'e'),  # 하트 잎 넷이 십자로, 긴 줄기 (YELL: 하트처럼 보였다 → 줄기를 더 길게)
    'sprout': ('.........vv..|.......vvvvv.|......vvvvevv|......vvvevvv|vvv..vvvevvv.|vvvv.vvevvv..|.veev.vevv...|...vvv.v.....|......v......|......v......|.....v.......|.....v.......|......v......', 'e'),  # 새싹: 왼쪽 작은 잎·오른쪽 둥근 큰 잎을 V로 떼고 잎맥, 휜 줄기 (YELL 이 보낸 그림을 참고)
    'sunflower': ('...y.y.y.....|..yyyyyyy....|.yyyobobyy...|yyyobobobyy..|.yybobobyy...|yyyobobobyy..|.yyyobobyy...|..yyyyyyy....|...y.y.y.....|.....v.......|.....v.vv....|......vve....|......v......|......v......', 'b'),  # 해바라기: 노란 꽃잎 고리에 갈색 씨 가운데, 잎 달린 줄기 (YELL 2026-11-13)
    'fallen': ('.....yyyyy.|....yyyyby.|...yoyybyy.|..ooyobyy..|..ooobyyy..|..oobooy...|..obooo....|..boo......|.b.........|b..........', 'b'),  # 낙엽 ①: 잎맥 보이는 둥근 잎, 비스듬히, 아래는 갈색·위는 금색 (YELL 이 보낸 그림). 선물의 초록 'leaf' 와 다른 이름
    'ginkgo': ('...yyy.yyy...|..yyyy.yyyy..|yyyyyy.yyyyyy|yyyyyyyyyyyyy|.yyyyyyyyyyy.|..yyyyyyyyy..|...yyyoyyy...|.....ooo.....|......o......|......o......|......o......|.....o.......', 'o'),  # 낙엽 ②: 은행잎 — 넓고 평평한 부채꼴, 윗변 가운데 좁은 V 홈, 아래로 좁아져 가늘고 긴 꼭지 (YELL 이 보낸 은행잎 그림 참고)
    'maple': ('....n....|...nnn...|.n.nrn.n.|nnnnrnnnn|.nnnrnnn.|..nrrrn..|...nrn...|....r....|....b....', 'b'),  # 낙엽 ③: 단풍잎 — 다섯 갈래, 주황에 빨간 잎맥
    'basket': ('...nnn...|..n...n..|.n.....n.|ooooooooo|onononono|ooooooooo|.ononono.|..ooooo..', 'b'),  # 먹이 바구니 (밥을 줄 때 잠깐 나와 씨앗을 던진다)
}


# 머리에 내려앉은 낙엽: 꼭지가 머리에 꽂힌 느낌보다 누워 내려앉은 느낌 (YELL 2026-11-13). 누운 그림 + 비스듬히 누운 그림 둘,
# 땅의 낙엽 더미는 세운 그림을 90°씩 돌린 것까지 섞어 제각각 흩어 놓는다 (YELL: 다 같은 방향이면 어색, 무작위로)
def _rot_cw(rows):  # 오른쪽으로 90°
    r = rows.split('|')
    h, w = len(r), len(r[0])
    return '|'.join(''.join(r[h - 1 - y][x] for y in range(h)) for x in range(w))


def _shear(rows, k):  # 줄마다 옆으로 밀어 비스듬히 (돌리면 도트가 뭉개져서 기울이기만)
    r = rows.split('|')
    h, w = len(r), len(r[0])
    shifts = [round((y - (h - 1) / 2) * k) for y in range(h)]
    lo = -min(shifts)
    width = w + max(shifts) - min(shifts)
    return '|'.join(('.' * (lo + s) + row).ljust(width, '.') for row, s in zip(r, shifts))


ITEMS['ginkgo_lie'] = ('yyyyy.......|yyyyyyyy....|.yyyyyyyoooo|yyyyyyyy....|yyyyy.......', 'o')  # 누운 은행잎: 부채가 왼쪽, 가운데 홈, 꼭지는 오른쪽으로 납작하게
ITEMS['maple_lie'] = ('.n..n..n...|nnnnnnnnn..|.nrrrrrrnnb|nnnnnnnnn..|.n..n..n...', 'b')  # 누운 단풍잎: 갈래가 위아래로 보이게 납작하게, 꼭지는 오른쪽
ITEMS['fallen_lie'] = ('....yyyyyy...|b.yyyyyyyyyy.|.byoooooooyyy|..yyyyyyyyyy.|....oooooo...', 'b')  # 누운 잎맥 잎: 가로로 길게, 꼭지는 왼쪽
for _n in ('fallen', 'ginkgo', 'maple'):
    _art, _line = ITEMS[_n]
    ITEMS[_n + '_r1'] = (_rot_cw(_art), _line)
    ITEMS[_n + '_r2'] = (_rot_cw(_rot_cw(_art)), _line)
    ITEMS[_n + '_r3'] = (_rot_cw(_rot_cw(_rot_cw(_art))), _line)
    ITEMS[_n + '_lie2'] = (_shear(ITEMS[_n + '_lie'][0], .6), _line)
    ITEMS[_n + '_lie3'] = (_shear(ITEMS[_n + '_lie'][0], -.6), _line)
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


def critter(kind, i):
    """버그 떼: 사방에서 걸어오는 벌레 두 가지, 두 장씩 (i). 머리가 아래를 본다 (버그와 같다)
    wasp = 노란 줄무늬 말벌 (작고 빠르다, 날개가 파닥), beetle = 초록 딱정벌레 (느리고 단단하다, 다리 꼼지락)"""
    g = Canvas()
    if kind == 'wasp':
        cx, cy, rx, ry = 20, 21, 4.5, 6
        gold, dark, wing = (250, 196, 50), (70, 46, 30), (236, 244, 255)
        for s_ in (-1, 1):  # 날개: 올렸다 내렸다
            g.ell(cx + s_ * 6.5, cy - 2 + (2.5 if i else -1.5), 4, 2.6 if i else 3.4, wing)
        g.put(cx, round(cy - ry) - 1, dark)  # 침 (꼬리 = 위)
        g.ell(cx, cy, rx, ry, gold)
        for y in (cy - 3, cy):  # 줄무늬
            for x in range(CELL):
                if g.p[y][x] == gold and abs(x + .5 - cx) < rx:
                    g.put(x, y, dark)
        for s_ in (-1, 1):
            g.ell(cx + s_ * 2, cy + 3.4, 1.2, 1.2, (255, 255, 255))
            g.put(round(cx + s_ * 2 - .5), round(cy + 3.6), (40, 24, 20))
        g.outline(dark)
        return g
    cx, cy, rx, ry = 20, 20, 9, 8
    body, shade, light, leg = (104, 184, 96), (64, 134, 74), (176, 226, 150), (34, 74, 44)
    for s_ in (-1, 1):  # 다리 셋씩, 장마다 앞뒤로
        for n, dy in enumerate((-.55, 0, .55)):
            wig = 1 if (n + i) % 2 else -1
            for t in range(4):
                g.put(round(cx + s_ * (rx * .8 + t)), round(cy + dy * ry + (wig if t >= 2 else 0)), leg)
        for t in range(3):  # 작은 뿔 (머리 쪽 = 아래)
            g.put(round(cx + s_ * (2 + t)), round(cy + ry + t), leg)
    g.ell(cx, cy, rx, ry, shade)
    g.ell(cx, cy - .8, rx - 1.2, ry - 1.4, body, True)
    for x, y in ((-4, -3), (4, -3), (-3.5, 1.5), (3.5, 1.5)):  # 등딱지 점
        g.ell(cx + x, cy + y, 1.5, 1.5, light, True)
    g.pix(shade, *[(cx, cy - ry + 2 + t) for t in range(int(ry * 1.3))])  # 등딱지 가운데 줄
    for s_ in (-1, 1):  # 눈 (아래쪽)
        g.ell(cx + s_ * 3.6, cy + ry * .55, 1.5, 1.5, (255, 255, 255))
        g.put(round(cx + s_ * 3.6 - .5), round(cy + ry * .55 + .3), (30, 40, 30))
    g.outline(leg)
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


def ledge(part):
    """달리기 2층 발판: 풀이 덮인 나무 판, 두께 7. 'l' 왼쪽 끝 · 'm' 가운데 · 'r' 오른쪽 끝을 옆으로 이어 붙인다.
    가운데는 칸 폭(40)을 꽉 채워야 이음매가 안 보인다. 윗줄이 발 닿는 선이라 위로 튀어나온 것은 그리지 않는다"""
    g = Canvas()
    top, n = 16, 3  # 끝 조각 폭
    light, wood, dark = mix(P['green'], P['white'], .35), P['brown'], P['dbrown']

    def col(x, u, cut=0):  # x: 칸 안 자리, u: 무늬 자리, cut: 모서리를 둥글게 깎는 줄 수
        drip = u % 10 in (3, 4)  # 풀이 군데군데 한 줄 더 늘어진다
        rows = [C['line'], light, P['green'], P['dgreen'], P['dgreen'] if drip else wood, dark if u % 20 != 19 else C['line'], C['line']]
        for i in range(cut, 7 - cut):
            g.put(x, top + i, C['line'] if i in (cut, 6 - cut) or cut == n - 1 else rows[i])

    if part == 'm':
        for x in range(CELL):
            col(x, x)
    else:
        for i in range(n):  # 끝은 바깥으로 갈수록 한 줄씩 깎는다
            col(CELL - n + i if part == 'l' else n - 1 - i, 37 + i if part == 'l' else n - 1 - i, n - 1 - i)
    return g


# ── 달리기 기믹 (YELL 2026-11-13): 버섯 점프대, 말벌, 굴러오는 돌, 구덩이, 동굴. 그림 1px = 볼록이 칸 1px (말벌만 2/3) ──
def turn(g, cw=True):
    """칸을 90도 돌린다 (cw: 시계 방향 — 아래를 보던 것이 왼쪽을 본다)"""
    r = Canvas()
    for y in range(CELL):
        for x in range(CELL):
            if g.p[y][x]:
                if cw:
                    r.p[x][CELL - 1 - y] = g.p[y][x]
                else:
                    r.p[CELL - 1 - x][y] = g.p[y][x]
    return r


def run_shroom(squash):
    """버섯 점프대: 보라 갓에 노란 점, 용수철 기둥 (얼굴 없음 — 얼굴 있는 빨간 버섯은 장애물). 밟히면 갓이 내려앉는다.
    눌린 장은 선 장의 상자 안에 들어야 한다 (다시 칠할 자리가 선 장 상자다)"""
    g = Canvas()
    cap, light, spot, coil = (150, 110, 220), (196, 168, 244), (255, 232, 130), (150, 150, 160)
    d = 2 if squash else 0
    g.ell(20, 31 + d, 11, 4.6, cap)
    clip_rows(g, lambda x, y: y < 32 + d)  # 갓 아랫면을 평평하게
    g.ell(20, 28.6 + d, 7, 1.3, light, True)
    for x, y in ((14, 30), (15, 30), (20, 28), (21, 28), (25, 30), (26, 30), (11, 31), (29, 31)):
        g.put(x, y + d, spot, True)
    for i, y in enumerate(range(32 + d, 36)):  # 용수철: 밝은 줄과 어두운 줄이 번갈아
        for x in range(17, 23) if i % 2 == 0 else range(16, 24):
            g.put(x, y, C['cream'] if i % 2 == 0 else coil)
    for x in range(15, 25):
        g.put(x, 35, C['cream'])
    g.outline(C['line'])
    return g


def run_rock(quarter):
    """굴러오는 돌: 동그란 바위. 네 장이 90도씩 돌아 있다 (왼쪽으로 구르니 시계 반대 방향)"""
    g = Canvas()
    base, light, dark, line = (156, 146, 136), (196, 188, 176), (114, 104, 98), (70, 58, 50)
    g.ell(20, 20, 10, 10, base)
    g.ell(16.5, 16.5, 5.5, 5, light, True)
    g.ell(25, 25, 5, 4.5, dark, True)
    g.ell(14.5, 14.5, 1.6, 1.6, (232, 226, 214), True)
    g.pix(line, (22, 12), (23, 13), (23, 14), (24, 15), (25, 15), (13, 23), (14, 24), (15, 24), (16, 25), (20, 27), (21, 27))
    g.pix(dark, (27, 18), (28, 19), (12, 19), (18, 21), (19, 21))
    g.outline(line)
    for _ in range(quarter):
        g = turn(g, cw=False)
    return g


def run_pit(part):
    """구덩이: 화면에 뚫린 구멍처럼 까맣고 그을린 가장자리 (2px 덩어리로 들쭉날쭉). 'l' 왼쪽 끝 · 'm' 가운데(폭 8, 이어 붙인다) · 'r' 오른쪽 끝.
    열 줄: 위 두 줄은 땅 위로 올라온 턱, 아래 여덟 줄은 땅 밑. 턱은 밝은 돌빛이다 — 배경이 없는 게임이라 어두운 창 위에서도 구멍이 보여야 한다"""
    g = Canvas()
    top, hole, soot, chip = 15, (14, 16, 22), (70, 60, 52), (206, 198, 186)

    def col(i, a, b, x0=16):  # 덩어리 한 줄: 위에서 a 번째부터 b 번째까지 (0~4). 맨 위·맨 아래 덩어리는 그을린 가장자리
        for n in range(a, b + 1):
            for y in range(2):
                for x in range(2):
                    g.put(x0 + 2 * i + x, top + 2 * n + y, soot if n in (a, b) else hole)

    if part == 'm':
        for i, (a, b) in enumerate(((0, 4), (1, 4), (0, 3), (0, 4))):
            col(i, a, b)
    else:
        cols = ((2, 2), (1, 3), (0, 4), (1, 4))  # 바깥에서 안으로: 금 하나, 둥근 모서리
        for i, (a, b) in enumerate(cols):
            col(i if part == 'l' else 3 - i, a, b)
        for n in (1, 2, 3):  # 모서리 덩어리는 통째로 그을음
            col(1 if part == 'l' else 2, n, n)
    for x in range(CELL):  # 턱(맨 위 덩어리)을 밝게, 그 아랫줄은 그을음
        for y in range(top, top + 6):
            if g.p[y][x]:
                g.p[y][x] = g.p[y + 1][x] = chip
                if g.p[y + 2][x] == hole:
                    g.p[y + 2][x] = soot
                break
    return g


def run_cave(part, look=0):
    """동굴 천장: 두께 20 의 바위. 윗줄은 올라설 수 있는 평평한 선, 아랫줄(고드름돌 끝)이 머리가 닿는 선이다.
    'l' 입구(이끼 낀 턱이 미리 보인다) · 'm' 가운데(폭 40, 이어 붙인다. look 0 고드름돌 · 1 박쥐 · 2 큰 고드름돌) · 'r' 출구.
    박쥐·고드름돌은 그림일 뿐이고 아랫줄보다 내려오지 않는다"""
    g = Canvas()
    top, deep = 10, 20
    base, light, dark, line = (130, 124, 148), (172, 166, 190), (98, 92, 116), (58, 52, 70)
    moss, dmoss = P['green'], P['dgreen']
    floor = [top + 12] * CELL  # 칸마다 바위가 내려오는 줄 (고드름돌이 없는 곳은 일곱 줄 얕다)

    def spike(c, half, length):
        for x in range(max(0, c - half), min(CELL, c + half + 1)):
            floor[x] = max(floor[x], top + 12 + round(length * (1 - abs(x - c) / (half + 1))))

    if part == 'm':
        for c, half, length in {0: ((6, 2, 4), (15, 3, 7), (25, 2, 3), (33, 3, 7)),
                                1: ((6, 3, 7), (34, 3, 7), (28, 1, 2)),
                                2: ((5, 2, 3), (19, 6, 7), (32, 2, 5), (36, 1, 7))}[look]:
            spike(c, half, length)
        cols = range(CELL)
    else:
        lip = (top + 16, top + 18, top + 19, top + 19, top + 19, top + 17, top + 14, top + 12)  # 입구 턱: 바깥쪽이 끝줄까지 내려온다
        cols = range(32, 40) if part == 'l' else range(0, 8)
        for i, x in enumerate(cols if part == 'l' else reversed(cols)):
            floor[x] = lip[i]
    for i, x in enumerate(cols):
        edge = min(i, len(cols) - 1 - i) if part == 'm' else (i if part == 'l' else len(cols) - 1 - i)
        cut = max(0, 3 - edge) if part != 'm' else 0  # 끝 조각은 윗모서리를 둥글게
        for y in range(top + cut, floor[x] + 1):
            g.put(x, y, base)
    # 무늬: 밝은 윗면, 어두운 얼룩, 금
    for x in cols:
        g.put(x, top + 1, light, True)
        if x % 9 in (2, 3, 4):
            g.put(x, top + 2, light, True)
    for cx, cy, rx, ry in ((6, 17, 4, 2), (21, 15, 5, 2.4), (33, 19, 4, 2), (14, 21, 3, 1.6), (28, 22, 3, 1.5)):
        g.ell(cx + (look * 5 if part == 'm' else 0) % 9, cy, rx, ry, dark, True)
    for x, y in ((11, 14), (12, 15), (12, 16), (25, 18), (26, 19), (37, 15), (36, 16), (2, 20), (3, 21)):
        g.put(x, y, line, True)
    for x in cols:  # 고드름돌 끝은 밝게, 맨 아랫줄과 윗줄·옆은 선
        if floor[x] > top + 13:
            g.put(x, floor[x] - 1, light, True)
    for x in cols:
        for y in range(top, floor[x] + 1):
            if g.p[y][x] and (y == floor[x] or y == top or not g.p[y - 1][x]
                             or (part == 'l' and (x == 32 or not g.p[y][x - 1])) or (part == 'r' and (x == 7 or not g.p[y][x + 1]))
                             or (part == 'm' and ((x > 0 and floor[x - 1] < y) or (x < CELL - 1 and floor[x + 1] < y)))):
                g.put(x, y, line)
    if part == 'm' and look == 1:  # 박쥐: 얕은 곳에 거꾸로 매달려 잔다 (날개를 접고, 노란 눈만 반짝)
        bat, wing, eye = (64, 46, 92), (108, 84, 140), (255, 222, 90)
        rows = ('..b.b..', '.wbbbw.', 'wwbbbww', 'wwbbbww', '.wbebw.'.replace('e', 'b'), '.bebeb.', '.b...b.')
        for j, row in enumerate(rows):
            for i, ch in enumerate(row):
                if ch != '.':
                    g.put(17 + i, top + 12 + j, {'b': bat, 'w': wing, 'e': eye}[ch])
    if part == 'm' and look == 2:  # 큰 고드름돌은 속까지 밝다
        for x in range(15, 24):
            for y in range(top + 12, floor[x] - 1):
                g.put(x, y, light, True)
    if part != 'm':  # 입구·출구 윗면의 이끼, 입구에는 늘어진 덩굴
        for i, x in enumerate(cols):
            if g.p[top + 3][x]:
                y = min(yy for yy in range(top, top + 4) if g.p[yy][x])
                g.put(x, y, dmoss)
                g.put(x, y + 1, moss)
        if part == 'l':
            for x, n in ((33, 7), (35, 4), (37, 2)):
                y0 = min(yy for yy in range(top, top + 6) if g.p[yy][x]) + 2
                for y in range(y0, y0 + n):
                    g.put(x, y, moss if (y - y0) % 3 else dmoss, True)
    return g


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
    rows += [(f'game/{k}', [critter(k, 0), critter(k, 1)]) for k in ('wasp', 'beetle')]
    rows += [('num/+', [art('...|.#.|###|.#.|...', 'b', C['white'])]), ('num/x', [art('...|#.#|.#.|#.#|...', 'b', C['white'])])]
    rows += [(f'item/{k}', [art(*v)]) for k, v in ITEMS.items()]
    rows += [('game/flash', [art('..y..|.yyy.|yywyy|.yyy.|..y..', 'r')])]
    rows += [('game/muzzle', [muzzle(a) for a in MUZZLE])]
    rows += [(f'prop/ledge_{k}', [ledge(k)]) for k in 'lmr']
    rows += [('run/shroom', [run_shroom(0), run_shroom(1)]), ('run/wasp', [turn(critter('wasp', i)) for i in range(2)]),
             ('run/rock', [run_rock(i) for i in range(4)])]
    rows += [(f'run/pit_{k}', [run_pit(k)]) for k in 'lmr']
    rows += [('run/cave_l', [run_cave('l')]), ('run/cave_m', [run_cave('m', i) for i in range(3)]), ('run/cave_r', [run_cave('r')])]
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
