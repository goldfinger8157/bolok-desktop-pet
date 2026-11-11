#!/usr/bin/env python3
"""볼록이 스프라이트 생성기 — 표준 라이브러리만으로 assets/sprites.png·sprites.json 을 만든다.

햄스터를 손으로 찍지 않고 매개변수(체형·볼 크기·자세)로 그린다.
그래서 "형태 × 볼 단계 × 자세" 조합이 전부 같은 캐릭터로 나온다.

    python3 tools/sprites.py [미리보기.png]
"""
import json
import math
import pathlib
import struct
import sys
import zlib

CELL = 40  # 한 칸 크기 (px). 앱은 이걸 정수배로 키워서 그린다

C = dict(
    line=(92, 58, 40),      # 윤곽선 — 검정보다 부드러운 진갈색
    fur=(246, 186, 112),    # 등 털
    shade=(226, 152, 84),   # 그림자
    cream=(255, 243, 224),  # 배·주둥이
    ear=(244, 156, 150),    # 귀 안쪽
    blush=(255, 170, 165),  # 볼터치
    eye=(46, 30, 24),
    white=(255, 255, 255),
    nose=(226, 98, 112),
    paw=(255, 214, 196),
    red=(232, 72, 92),
    seed=(92, 74, 66),
    stripe=(235, 225, 205),
    blue=(120, 190, 240),
    green=(120, 186, 100),
)

# 형태: 체형과 색. 어른 3종은 키운 방식으로 갈린다 (docs/model.md)
FORMS = {
    'baby': dict(rx=8.5, ry=8, ear=2.2, eye=2.6, eyegap=5, fur=(255, 205, 170), shade=(240, 170, 140)),
    'child': dict(rx=10.5, ry=10, ear=3, eye=2.6, eyegap=6),
    'adult_chubby': dict(rx=14, ry=12.5, ear=3.4, eye=2.6, eyegap=7),
    'adult_sweet': dict(rx=12.5, ry=12, ear=3.6, eye=3, eyegap=7, flower=True, fur=(250, 196, 140), blush=2),
    'adult_grumpy': dict(rx=12.5, ry=11.5, ear=3.2, eye=2.3, eyegap=6, fur=(196, 150, 112), shade=(160, 116, 84), brow=True),
}

# 자세: 프레임마다 바뀌는 값만 적는다
POSES = {
    'idle': [{}, {'sq': 1}, {}, {'eye': 'blink'}],
    'walk': [{'turn': 3, 'step': 1}, {'turn': 3, 'dy': -1}, {'turn': 3, 'step': 2}, {'turn': 3, 'dy': -1}],
    'sleep': [{'lie': 1, 'eye': 'shut', 'z': 0}, {'lie': 1, 'eye': 'shut', 'sq': 1, 'z': 1}],
    'eat': [{'mouth': 'chew', 'hold': 1, 'turn': 1}, {'mouth': 'w', 'hold': 1, 'turn': 1, 'puff': 1},
            {'mouth': 'chew', 'hold': 1, 'turn': 1, 'eye': 'blink'}, {'mouth': 'w', 'hold': 1, 'turn': 1, 'puff': 1}],
    'happy': [{'eye': 'happy', 'mouth': 'open', 'heart': 1}, {'eye': 'happy', 'mouth': 'open', 'dy': -3}],
    'sad': [{'eye': 'sad', 'mouth': 'frown', 'sq': 1}, {'eye': 'sad', 'mouth': 'frown', 'sq': 1, 'tear': 1}],
    'hungry': [{'eye': 'sad', 'mouth': 'o', 'belly': 1}, {'eye': 'sad', 'mouth': 'open', 'belly': 1, 'sq': 1}],
    'chomp': [{'eye': 'shut', 'mouth': 'wide', 'dy': -2, 'turn': 3}, {'eye': 'angry', 'mouth': 'chew', 'puff': 1, 'turn': 3},
              {'eye': 'angry', 'mouth': 'w', 'turn': 3}],
    'spit': [{'eye': 'shut', 'mouth': 'w', 'puff': 1, 'sq': 1}, {'eye': 'wide', 'mouth': 'wide', 'dy': -2, 'turn': 3}],
    'held': [{'eye': 'wide', 'mouth': 'o', 'dangle': 1}, {'eye': 'wide', 'mouth': 'o', 'dangle': 2}],
    'fly': [{'eye': 'wide', 'mouth': 'o', 'dangle': 1, 'rot': r} for r in range(4)],  # 던져져서 빙글빙글
    'land': [{'eye': 'shut', 'mouth': 'frown', 'sq': 5}],                             # 튕길 때 찌그러진다
    'dizzy': [{'eye': 'x', 'mouth': 'frown', 'stars': t} for t in range(4)],
    'peek': [{'turn': 3}, {'turn': 3}, {'turn': 3, 'eye': 'blink'}, {'turn': 3}],     # 창 뒤에서 빼꼼
    'shock': [{'eye': 'wide', 'mouth': 'wide', 'dy': -3, 'mark': '!'}, {'eye': 'wide', 'mouth': 'wide', 'mark': '!'}],
    'shy': [{'eye': 'shut', 'mouth': 'w', 'cover': 1}, {'eye': 'shut', 'mouth': 'w', 'cover': 1, 'sq': 1, 'sweat': 1}],
    'angry': [{'eye': 'angry', 'mouth': 'frown', 'puff': 1, 'vein': 1, 'step': 1},
              {'eye': 'angry', 'mouth': 'frown', 'puff': 1, 'vein': 1, 'step': 2, 'dy': -1}],
    'freeze': [{'mouth': 'flat', 'sweat': t} for t in range(3)],                      # 안 보이는 척
    'look': [{'turn': -3, 'mark': '?'}, {'mark': '?'}, {'turn': 3, 'mark': '?'}, {'mark': '?'}],  # 어디 갔지
    # 클릭 반응
    'nuzzle': [{'eye': 'shut', 'turn': -2, 'cover': 0, 'blushy': 1}, {'eye': 'shut', 'turn': 2, 'blushy': 1}],  # 머리 비비기
    'squish': [{'eye': 'wide', 'mouth': 'o', 'puff': 3, 'sq': 2}, {'eye': 'blink', 'mouth': 'o', 'puff': 4, 'sq': 3}],  # 볼 콕
    'giggle': [{'eye': 'happy', 'mouth': 'open', 'dy': -1, 'turn': -1}, {'eye': 'happy', 'mouth': 'open', 'turn': 1}],  # 깔깔
    'flop': [{'eye': 'happy', 'mouth': 'open', 'dangle': 1, 'rot': 2}, {'eye': 'shut', 'mouth': 'w', 'dangle': 2, 'rot': 2}],  # 벌렁
    'love': [{'eye': 'heart', 'mouth': 'open'}, {'eye': 'heart', 'mouth': 'w', 'dy': -1}],
    'wink': [{'eye': 'wink', 'mouth': 'open'}, {'eye': 'wink', 'mouth': 'w'}],
    'hurt': [{'eye': 'x', 'mouth': 'o', 'sq': 2, 'tear': 1}, {'eye': 'shut', 'mouth': 'o', 'sq': 1, 'dy': -1}],  # 총에 맞음
    'pout': [{'eye': 'angry', 'mouth': 'flat', 'puff': 2, 'turn': -3}, {'eye': 'angry', 'mouth': 'flat', 'puff': 2, 'turn': -3, 'dy': -1}],  # 삐짐
    # 혼자 놀기
    'groom': [{'eye': 'shut', 'hands': 0}, {'eye': 'shut', 'hands': 2}, {'eye': 'shut', 'hands': 1}, {'eye': 'shut', 'hands': 3}],  # 세수
    'stretch': [{'sq': -2, 'eye': 'shut', 'mouth': 'open'}, {'sq': -3, 'eye': 'shut', 'mouth': 'wide'},
                {'sq': -3, 'eye': 'shut', 'mouth': 'wide'}, {'sq': 2, 'eye': 'blink'}],  # 기지개와 하품
    'hop': [{}, {'dy': -3}, {'dy': -6, 'eye': 'happy', 'mouth': 'open'}, {'dy': -3}],
    'roll': [{'eye': 'happy', 'mouth': 'open', 'dangle': 1, 'rot': r} for r in range(4)],  # 데굴데굴
    'dig': [{'turn': 2, 'step': 1, 'mouth': 'chew', 'dirt': 0}, {'turn': 2, 'step': 2, 'dy': 1, 'mouth': 'w', 'dirt': 1}],  # 땅파기
    'carry': [{'up': 1, 'mouth': 'w'}, {'up': 1, 'mouth': 'w'}, {'up': 1, 'mouth': 'w'}, {'up': 1, 'mouth': 'w', 'eye': 'blink'}],  # 쟁반 받치기 (벽돌깨기)
    'fix': [{'turn': 2, 'hammer': 0}, {'turn': 2, 'hammer': 1, 'sq': 1}],  # 망치질 (ICBM 구멍 복구)
    'think': [{'turn': -1, 'mark': 'dots'}, {'turn': -1, 'mark': 'dots'}, {'turn': -1, 'eye': 'blink', 'mark': 'dots'},
              {'turn': -1, 'mark': 'dots'}],  # 멍때리기
}
CHEEKS = 4  # 볼 단계: 0(홀쭉) ~ 3(화면 가득)


class Canvas:
    def __init__(self):
        self.p = [[None] * CELL for _ in range(CELL)]

    def put(self, x, y, c, clip=False):
        if 0 <= x < CELL and 0 <= y < CELL and (not clip or self.p[y][x]):
            self.p[y][x] = c

    def ell(self, cx, cy, rx, ry, c, clip=False):
        for y in range(CELL):
            for x in range(CELL):
                if ((x + .5 - cx) / rx) ** 2 + ((y + .5 - cy) / ry) ** 2 < 1:  # 등호를 빼야 끝에 픽셀 한 개가 안 튄다
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


def eyes(g, kind, fx, ey, gap, r, c):
    for x, s in ((fx - gap, -1), (fx + gap, 1)):  # s: 얼굴 바깥쪽 방향
        if kind in ('open', 'wide', 'angry'):
            w = r + (.6 if kind == 'wide' else 0)
            g.ell(x + .5, ey + .5, w, w + .1, c['eye'])
            g.put(x - 1, ey - 1, c['white'])
            g.put(x, ey - 1, c['white'])
            if r >= 2.6:  # 큰 눈엔 반짝임을 하나 더
                g.put(x + 1, ey + 1, c['white'])
            if kind == 'angry':  # 안쪽이 내려간 눈꺼풀로 흘겨본다
                lid = ((-2, -1), (-1, -2), (0, -2), (1, -3), (2, -3))
                for i, j in lid:
                    g.put(x + i * s, ey + j, c['line'])
                    for k in range(1, 4):
                        g.put(x + i * s, ey + j - k, c['fur'])
        elif kind in ('blink', 'shut'):
            g.pix(c['eye'], (x - 2, ey), (x - 1, ey + 1), (x, ey + 1), (x + 1, ey + 1), (x + 2, ey))
        elif kind == 'happy':
            g.pix(c['eye'], (x - 2, ey + 1), (x - 1, ey), (x, ey - 1), (x + 1, ey), (x + 2, ey + 1))
        elif kind == 'heart':
            for dy, row in enumerate(('.#.#.', '#####', '#####', '.###.', '..#..')):
                g.pix(c['red'], *[(x - 2 + i, ey - 2 + dy) for i, b in enumerate(row) if b == '#'])
        elif kind == 'wink':
            if s < 0:
                g.ell(x + .5, ey + .5, r, r + .1, c['eye'])
                g.put(x - 1, ey - 1, c['white'])
                g.put(x, ey - 1, c['white'])
            else:
                g.pix(c['eye'], (x - 2, ey - 2), (x - 1, ey - 1), (x, ey), (x - 1, ey + 1), (x - 2, ey + 2))
        elif kind == 'x':
            for i in range(-2, 3):
                g.put(x + i, ey + i, c['eye'])
                g.put(x + i, ey - i, c['eye'])
        elif kind == 'sad':
            g.ell(x + .5, ey + 1.5, r - .3, r, c['eye'])
            g.put(x - 1, ey, c['white'])
            g.pix(c['line'], (x - 2 * s, ey - 3), (x - s, ey - 3), (x, ey - 4), (x + s, ey - 4))


def nose(g, fx, my, c):
    g.pix(c['nose'], (fx - 1, my - 2), (fx, my - 2), (fx + 1, my - 2), (fx, my - 1))


def mouth(g, kind, fx, my, c):
    if kind == 'w':
        g.pix(c['line'], (fx - 2, my), (fx - 1, my + 1), (fx, my), (fx + 1, my + 1), (fx + 2, my))
    elif kind == 'chew':
        g.pix(c['line'], (fx - 2, my + 1), (fx - 1, my), (fx, my + 1), (fx + 1, my), (fx + 2, my + 1))
    elif kind == 'flat':
        g.pix(c['line'], (fx - 1, my + 1), (fx, my + 1), (fx + 1, my + 1))
    elif kind == 'frown':
        g.pix(c['line'], (fx - 2, my + 1), (fx - 1, my), (fx, my), (fx + 1, my), (fx + 2, my + 1))
    elif kind == 'o':
        g.ell(fx + .5, my + 1.5, 1.8, 1.8, c['line'])
    elif kind in ('open', 'wide'):
        h, w = (2.6, 3.4) if kind == 'open' else (3.8, 4.5)
        g.ell(fx + .5, my + h, w, h, c['line'])
        g.ell(fx + .5, my + h + .5, w - 1, h - 1, c['red'])
        if kind == 'wide':  # 앞니 — 화면을 갉는 도구
            for i in (-2, -1, 1, 2):
                g.pix(c['white'], (fx + i, my + 1), (fx + i, my + 2))


def heart(g, x, y, c):
    g.pix(c, (x, y), (x + 1, y), (x + 3, y), (x + 4, y))
    for i in range(5):
        g.put(x + i, y + 1, c)
    for i in range(1, 4):
        g.put(x + i, y + 2, c)
    g.put(x + 2, y + 3, c)


def hamster(form, cheek, frame):
    F, f = FORMS[form], frame
    c = dict(C, fur=F.get('fur', C['fur']), shade=F.get('shade', C['shade']))
    g = Canvas()
    rx, ry = F['rx'], F['ry']
    ground = 37
    lie = f.get('lie', 0)
    sq = f.get('sq', 0)
    dangle = f.get('dangle', 0)
    if lie:  # 엎드려 자기: 납작하고 넓게
        rx, ry = rx + 2, ry - 2.5
    rx, ry = rx + sq * .5, ry - sq * .5
    cx = 20.5
    cy = ground - ry - 1 + f.get('dy', 0)
    if dangle:  # 들렸을 때는 몸이 아래로 늘어진다
        ry, rx = ry + 1, rx - 1
        cy = 19
    turn = f.get('turn', 0)
    fx = round(20 + turn * (1 if rx > 9 else .7))  # 고개 돌린 쪽으로 얼굴이 옮겨간다
    gap = round(F['eyegap'] - abs(turn) * .4)      # 옆을 보면 눈 사이가 좁아 보인다
    ey = round(cy - ry * .24)
    my = round(cy + ry * .3)

    # 1) 실루엣 — 귀·몸·볼주머니·발
    ear, ex = F['ear'], rx * .62
    ear_x = [cx + s * ex + (fx - 20) * .4 for s in (-1, 1)]
    for x in ear_x:
        g.ell(x, cy - ry * .86, ear, ear, c['fur'])
    g.ell(cx, cy, rx, ry, c['shade'])
    bulge = cheek * 2.2 + f.get('puff', 0) * 1.2  # 먹은 화면이 많을수록 불룩하다
    if bulge:
        for s in (-1, 1):
            g.ell(fx + .5 + s * rx * .72, cy + ry * .22, 2 + bulge * .75, 2.4 + bulge * .6, c['cream'])
    if not lie:
        step = f.get('step', 0)
        for i, s in enumerate((-1, 1)):
            lift = (step == i + 1) * 1
            if dangle:
                lift = -.6 * ((dangle + i) % 2)
            g.ell(cx + s * rx * .5, cy + ry + .5 - lift, 2.8, 1.9, c['paw'])
            if dangle:  # 번쩍 들리면 팔이 벌어진다
                g.ell(cx + s * (rx + .4), cy + 1, 2.2, 1.7, c['paw'])

    # 2) 안쪽 칠하기 — 테두리 한 겹을 그림자로 남기고 털·배를 올린다
    g.ell(cx, cy - .8, rx - 1, ry - .6, c['fur'], True)
    for x in ear_x:
        g.ell(x, cy - ry * .86, ear * .55, ear * .55, c['ear'])
    g.ell(fx + .5, cy + ry * .82, rx * .86, ry * .82, c['cream'], True)
    g.ell(fx + .5, my, rx * .36, ry * .26, c['cream'], True)
    for s in (-1, 1):
        g.ell(fx + .5 + s * (F['eyegap'] + 1.8), cy + ry * .1, 2 + F.get('blush', 0) * .3, 1.2, c['blush'], True)

    # 3) 윤곽선 → 얼굴
    g.outline(c['line'])
    eyes(g, f.get('eye', 'open'), fx, ey, gap, F['eye'], c)
    if F.get('brow'):  # 심술이: 늘 찌푸린 눈썹
        for s in (-1, 1):
            x = fx + s * gap
            g.pix(c['line'], (x - 2 * s, ey - 3), (x - s, ey - 4), (x, ey - 5), (x + s, ey - 5), (x + 2 * s, ey - 6))
    nose(g, fx, my, c)
    mouth(g, f.get('mouth', 'w'), fx, my, c)

    # 4) 소품
    if f.get('hold'):  # 앞발로 씨앗을 쥐고 오물오물
        g.ell(fx + .5, my + 6, 2.4, 3.2, c['line'])
        g.ell(fx + .5, my + 6, 1.5, 2.3, c['seed'])
        g.pix(c['stripe'], (fx, my + 5), (fx, my + 7))
        for s in (-1, 1):
            g.ell(fx + .5 + s * 3.2, my + 6, 1.8, 1.6, c['paw'])
    if f.get('belly'):  # 꼬르륵
        g.pix(c['shade'], (fx - 1, my + 6), (fx, my + 5), (fx + 1, my + 6))
    if f.get('tear'):
        g.pix(c['blue'], (fx - gap - 2, ey + 3), (fx - gap - 2, ey + 4), (fx - gap - 2, ey + 5))
    if f.get('heart'):
        heart(g, 32, 2, c['red'])
    if 'z' in f:
        x, y = 31 + f['z'], 5 - f['z'] * 2
        g.pix(c['blue'], *[(x + i, y) for i in range(5)], (x + 3, y + 1), (x + 2, y + 2), (x + 1, y + 3),
              *[(x + i, y + 4) for i in range(5)])
    if f.get('blushy'):
        for s in (-1, 1):
            g.ell(fx + .5 + s * (gap + 1.5), cy + ry * .14, 3, 1.5, c['blush'])
    if f.get('cover'):  # 앞발로 눈을 가리고 볼이 빨개진다
        for s in (-1, 1):
            g.ell(fx + .5 + s * (gap + 1.5), cy + ry * .14, 3, 1.5, c['blush'])
            g.ell(fx + .5 + s * gap, ey + 1, 3.3, 2.9, c['line'])
            g.ell(fx + .5 + s * gap, ey + 1, 2.4, 2, c['paw'])
    if 'sweat' in f:
        y = ey - 6 + f['sweat'] * 2
        x = fx + gap + 5
        g.pix(c['blue'], (x, y), (x - 1, y + 1), (x, y + 1), (x + 1, y + 1), (x, y + 2))
    if 'hands' in f:  # 앞발로 얼굴을 비빈다
        for s in (-1, 1):
            g.ell(fx + .5 + s * 3, my - f['hands'], 2.4, 2, c['line'])
            g.ell(fx + .5 + s * 3, my - f['hands'], 1.5, 1.2, c['paw'])
    if 'up' in f:  # 앞발을 머리 위로 올려 쟁반을 받친다
        for s in (-1, 1):
            g.ell(fx + .5 + s * rx * .6, cy - ry - f['up'], 2.4, 2, c['line'])
            g.ell(fx + .5 + s * rx * .6, cy - ry - f['up'], 1.5, 1.2, c['paw'])
    if 'hammer' in f:  # 큰 망치: 머리 위로 들었다(0) 옆으로 내리쳤다(1). 자루 두 줄, 머리 7×5 에 테두리
        hx, hy = int(fx + rx), int(cy)
        gray, lite, wood = (150, 150, 160), (210, 210, 218), (150, 100, 60)

        def head(x0, y0, w, h):
            g.pix(c['line'], *[(x0 + dx, y0 + dy) for dx in range(-1, w + 1) for dy in range(-1, h + 1)])
            g.pix(gray, *[(x0 + dx, y0 + dy) for dx in range(w) for dy in range(h)])
            g.pix(lite, *[(x0 + dx, y0) for dx in range(w)])

        if f['hammer'] == 0:
            g.pix(c['line'], *[(hx + dx, hy - i) for dx in (-1, 2) for i in range(-2, 10)])
            g.pix(wood, *[(hx + dx, hy - i) for dx in (0, 1) for i in range(-2, 10)])
            head(hx - 3, hy - 14, 8, 5)
        else:
            hx = min(hx, 26)  # 몸이 큰 형태도 망치 머리가 칸 밖으로 안 나가게
            g.pix(c['line'], *[(hx + i, hy + 1 + i // 2 + dy) for i in range(-2, 8) for dy in (-1, 2)])
            g.pix(wood, *[(hx + i, hy + 1 + i // 2 + dy) for i in range(-2, 8) for dy in (0, 1)])
            head(hx + 7, hy + 1, 5, 8)
            g.pix((250, 185, 40), (hx + 13, hy + 10), (hx + 14, hy + 7), (hx + 10, hy + 11), (hx + 15, hy + 11))
    if 'dirt' in f:  # 튀는 흙
        d = f['dirt']
        g.pix(c['seed'], (4 + d, 34 - d * 3), (7 - d, 31 - d), (35 - d, 33 - d * 2), (32 + d, 30 + d))
    if f.get('mark') == 'dots':
        for x in (31, 34, 37):
            g.pix(c['line'], (x, 8), (x + 1, 8), (x, 9), (x + 1, 9))
    if f.get('vein'):  # 빠직
        red = c['red']
        g.pix(red, (33, 3), (33, 4), (32, 4), (36, 3), (36, 4), (37, 4), (33, 7), (33, 8), (32, 7), (36, 7), (36, 8), (37, 7))
    if f.get('mark') == '!':
        g.pix(c['red'], *[(x, y) for x in (34, 35) for y in range(2, 8)], (34, 9), (35, 9))
    if f.get('mark') == '?':
        g.pix(c['blue'], (33, 3), (34, 2), (35, 2), (36, 2), (37, 3), (37, 4), (36, 5), (35, 6), (35, 7), (35, 9))
    if 'stars' in f:  # 머리 위를 도는 별
        for i in range(3):
            a = (f['stars'] / 4 + i / 3) * 2 * math.pi
            x, y = round(cx + math.cos(a) * (rx - 1)), round(cy - ry - 1 + math.sin(a) * 3)
            for u, v in ((0, 0), (-1, 0), (1, 0), (0, -1), (0, 1)):
                g.put(x + u, y + v, (255, 214, 80))
    if F.get('flower'):  # 애교쟁이: 귀에 꽃
        x, y = round(ear_x[1]), round(cy - ry * .95)
        for a, b in ((-1, 0), (1, 0), (0, -1), (0, 1)):
            g.put(x + a, y + b, (255, 120, 170))
        g.put(x, y, (255, 236, 110))
    for _ in range(f.get('rot', 0)):  # 90도씩 돌리기
        g.p = [[g.p[CELL - 1 - x][y] for x in range(CELL)] for y in range(CELL)]
    return g


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
}


# 총쏘기 게임 그림. 글자는 #=주색, k=검정, g=진회색, l=연회색, w=흰색, r=빨강, y=금색, o=황동, b=갈색 선
PAL = {'k': (18, 18, 22), 'g': (80, 80, 88), 'l': (200, 200, 206), 'w': (255, 255, 255), 'r': (232, 72, 92),
       'y': (250, 185, 40), 'o': (190, 130, 40), 'b': (92, 58, 40), 'd': (150, 150, 160),
       'u': (96, 150, 230), 'v': (110, 190, 110), 'p': (240, 140, 170), 'n': (240, 150, 60), 'm': (170, 110, 220)}
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
}
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
    rows += [('game/flash', [art('..y..|.yyy.|yywyy|.yyy.|..y..', 'r')])]
    rows += [('game/muzzle', [muzzle(a) for a in MUZZLE])]
    for form in FORMS:
        for cheek in range(CHEEKS):
            for pose, frames in POSES.items():
                rows.append((f'{form}/{cheek}/{pose}', [hamster(form, cheek, f) for f in frames]))
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


def contact(rows, scale, path):
    """검수용 한눈 보기: 형태별로 볼 0·3 단계를 자세 순으로 늘어놓는다"""
    picks = [n for n, _ in rows if n.startswith(('egg', 'icon'))]
    cols, grid = 8, []
    d = dict(rows)
    grid.append([g for n in picks for g in d[n]][:cols * 2])
    for form in FORMS:
        for cheek in (0, 3):
            grid.append([g for pose in POSES for g in d[f'{form}/{cheek}/{pose}'][:2]])
    w = max(len(r) for r in grid)
    bg = (236, 240, 244)
    out = [[bg] * (w * CELL * scale) for _ in range(len(grid) * CELL * scale)]
    for r, row in enumerate(grid):
        for i, g in enumerate(row):
            for y in range(CELL):
                for x in range(CELL):
                    if g.p[y][x]:
                        for a in range(scale):
                            for b in range(scale):
                                out[r * CELL * scale + y * scale + a][i * CELL * scale + x * scale + b] = g.p[y][x]
    png(out, path)


def main():
    root = pathlib.Path(__file__).resolve().parent.parent
    (root / 'assets').mkdir(exist_ok=True)
    rows = build()
    cols = max(len(f) for _, f in rows)
    # JSON 을 먼저 쓴다. 떠 있는 앱은 PNG 가 바뀌면 둘 다 다시 읽는다
    (root / 'assets/sprites.json').write_text(json.dumps(
        {'cell': CELL, 'rows': {n: [i, len(f)] for i, (n, f) in enumerate(rows)}}, ensure_ascii=False))
    png(sheet(rows, cols), root / 'assets/sprites.png')
    if len(sys.argv) > 1:
        contact(rows, 3, sys.argv[1])
    print(f'{len(rows)}행 {cols}열')


if __name__ == '__main__':
    main()
