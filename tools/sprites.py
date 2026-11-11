#!/usr/bin/env python3
"""볼록이 스프라이트 생성기 — 표준 라이브러리만으로 assets/sprites.png·sprites.json 을 만든다.

햄스터를 손으로 찍지 않고 매개변수(체형·볼 크기·자세)로 그린다.
그래서 "형태 × 볼 단계 × 자세" 조합이 전부 같은 캐릭터로 나온다.

    python3 tools/sprites.py [미리보기.png]
"""
import json
import math
import os
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

# 캐릭터(종): 햄스터 몸에 귀·꼬리·얼굴·무늬·색을 얹는다. 실존 동물만, 유명 캐릭터와 겹치는 조합은 피한다
#   (흰 토끼는 빼고 — 미피 판례, 곰은 리락쿠마 대신 반달가슴곰, 양은 검은 얼굴 없이 — 숀더쉽, 사슴은 흰 점 없이 — 밤비,
#    고슴도치는 파랑 없이 — 소닉, 고양이는 흰 몸+리본 없이 — 헬로키티, 펭귄은 푸른 회색 — 뽀로로·턱스, 옷·소품은 안 입힌다)
SPECIES = {
    'hamster': dict(name='햄스터'),
    'cat': dict(name='고양이', fur=(240, 168, 96), shade=(214, 134, 70), ears='pointy', tail='thin', marks=('tabby',), whisker=True),
    'shiba': dict(name='시바견', fur=(226, 150, 82), shade=(196, 120, 60), ears='pointy', tail='curl', face='cheeks', nose='dark'),
    'fox': dict(name='여우', fur=(234, 120, 58), shade=(204, 92, 40), ears='fox', ear_k=1.25, tail='fluffy', face='cheeks', nose='dark'),
    'moonbear': dict(name='반달가슴곰', fur=(92, 72, 60), shade=(70, 54, 46), cream=(236, 222, 200), ear=(150, 120, 104), ears='small',
                     belly=False, marks=('crescent',), face='snout', snout=(200, 168, 130), nose='dark', blush_k=.6),
    'penguin': dict(name='펭귄', fur=(84, 100, 130), shade=(62, 76, 104), cream=(250, 250, 252), paw=(244, 160, 60), ears='none',
                    wings='flipper', face='bigbelly', beak='small'),
    'owl': dict(name='부엉이', fur=(150, 110, 82), shade=(120, 86, 62), cream=(236, 220, 192), paw=(240, 180, 70), ears='tuft',
                wings='wing', marks=('disc',), beak='hook'),
    'squirrel': dict(name='다람쥐', fur=(198, 118, 62), shade=(168, 92, 46), ears='pointy', ear_k=.75, tail='bushy', nose='dark'),
    'otter': dict(name='수달', fur=(132, 96, 70), shade=(106, 76, 56), cream=(238, 220, 192), ears='small', ear_k=.8, face='snout',
                  snout=(236, 218, 190), nose='dark', whisker=True),
    'duck': dict(name='오리', fur=(255, 214, 80), shade=(236, 186, 50), cream=(255, 240, 170), paw=(250, 150, 50), ears='none',
                 wings='small', beak='bill', marks=('tuft',)),
    'frog': dict(name='개구리', fur=(120, 196, 96), shade=(88, 160, 70), cream=(226, 244, 200), paw=(140, 206, 110), ears='none',
                 face='frog', nose='none', blush=(255, 160, 150)),
    'hedgehog': dict(name='고슴도치', fur=(232, 210, 178), shade=(204, 180, 146), ears='small', ear_k=.7, back='spikes',
                     spike=(122, 92, 70), nose='dark'),
    'raccoon': dict(name='너구리', fur=(160, 160, 164), shade=(128, 128, 134), cream=(240, 240, 236), ear=(90, 90, 96), ears='pointy',
                    ear_k=.8, tail='ring', marks=('mask',), nose='dark'),
    'dragon': dict(name='아기 용', fur=(104, 190, 150), shade=(76, 158, 120), cream=(244, 236, 170), paw=(124, 204, 166), ears='none',
                   horns='dragon', wings='dragon', tail='dragon', marks=('plates',)),
    'sheep': dict(name='양', fur=(250, 246, 236), shade=(226, 220, 206), cream=(236, 212, 180), ear=(236, 200, 170), ears='side',
                  back='wool', face='wool'),
    'deer': dict(name='사슴', fur=(206, 146, 90), shade=(176, 118, 70), ears='side', horns='antler', face='snout', snout=(244, 226, 200),
                 nose='dark'),
    'goat': dict(name='염소', fur=(224, 214, 196), shade=(196, 184, 164), cream=(250, 246, 238), ear=(230, 196, 180), ears='side',
                 horns='goat', marks=('beard',), nose='dark'),
    'beaver': dict(name='비버', fur=(150, 100, 62), shade=(122, 78, 46), cream=(232, 206, 168), ears='small', ear_k=.7, tail='flat',
                   face='snout', snout=(222, 196, 160), nose='dark', teeth=True),
    'quokka': dict(name='쿼카', fur=(168, 146, 126), shade=(140, 120, 102), cream=(240, 228, 212), ears='small', ear_k=.85, nose='dark',
                   smile=True),
    'capybara': dict(name='카피바라', fur=(170, 120, 78), shade=(140, 96, 60), cream=(196, 150, 104), ears='small', ear_k=.6,
                     face='capy', snout=(150, 106, 70), nose='nostrils', wide=1.12, belly=False, eye_k=.75, eye_up=2),
    'koala': dict(name='코알라', fur=(170, 172, 182), shade=(140, 142, 154), cream=(236, 236, 240), ear=(246, 246, 250), ears='big',
                  nose='big'),
    # 실제 동물 더
    'panda': dict(name='판다', fur=(246, 246, 242), shade=(214, 214, 210), cream=(255, 255, 252), ear=(60, 56, 60), ear_out=(52, 48, 54),
                  paw=(66, 62, 68), ears='round', ear_k=.9, marks=('eyepatch',), nose='dark', belly=False),
    'redpanda': dict(name='레서판다', fur=(196, 88, 46), shade=(160, 66, 34), cream=(252, 244, 232), ear=(252, 244, 232), paw=(64, 38, 30),
                     ears='pointy', ear_k=.85, face='cheeks', marks=('brows',), tail='ring', nose='dark', belly=False),  # 배는 털색 그대로, 다리는 짙게 (여우와 다르게)
    'alpaca': dict(name='알파카', fur=(222, 186, 146), shade=(190, 152, 112), cream=(250, 240, 222), ear=(200, 150, 120), ears='long',
                   ear_k=.8, marks=('topknot',), face='snout', snout=(250, 240, 222), nose='dark'),  # 크림색 긴 귀는 토끼(미피)처럼 보인다 → 갈색 + 큰 털뭉치
    'pig': dict(name='돼지', fur=(246, 232, 214), shade=(222, 204, 182), cream=(252, 242, 230), ear=(236, 182, 168), ears='pointy',
                ear_k=.8, tail='curl', nose='pig', marks=('patches',)),  # 밝은 핑크는 페파피그와 겹친다
    'octopus': dict(name='문어', fur=(236, 130, 150), shade=(206, 100, 124), cream=(252, 196, 206), ears='none', body='octo',
                    marks=('spots',), nose='none'),
    # 판타지·옛이야기 (유명 디자인과 겹치지 않게: 슬라임은 뾰족 머리 없는 민트 덩어리, 유령은 연보라에 작은 팔,
    #  유니콘은 크림색 몸에 한 가지 색 갈기, 버섯은 갈색 갓에 크림 점)
    'slime': dict(name='슬라임', fur=(150, 226, 196), shade=(110, 196, 166), cream=(196, 244, 224), ears='none', body='blob',
                  marks=('shine',), nose='none', belly=False, mouth_in=(70, 150, 120)),  # 파란 물방울·빨간 혀는 드래곤퀘스트
    'ghost': dict(name='유령', fur=(196, 214, 240), shade=(166, 186, 222), cream=(226, 236, 250), ears='none', body='ghost',
                  nose='none', belly=False, blush=(236, 170, 204), no_cover=True),  # 물결 밑단·흰색은 팩맨·부우와 겹친다 → 연기 꼬리
    'golem': dict(name='꼬마 골렘', fur=(164, 166, 158), shade=(130, 132, 124), cream=(196, 198, 190), paw=(140, 142, 134), ears='small',
                  ear_k=.6, marks=('cracks', 'moss'), nose='none', wide=1.1),
    'unicorn': dict(name='유니콘', fur=(214, 222, 238), shade=(184, 194, 218), cream=(240, 244, 252), ear=(196, 186, 230), ears='pointy',
                    ear_k=.75, horns='unicorn', marks=('mane',), mane=(112, 100, 184), nose='dark'),  # 흰 몸·무지개 갈기는 마이 리틀 포니
    'mushroom': dict(name='버섯 요정', fur=(250, 238, 214), shade=(226, 210, 180), cream=(255, 248, 232), ears='none', marks=('cap',),
                     cap=(160, 104, 64), nose='none'),
    'imp': dict(name='꼬마 악마', fur=(196, 120, 210), shade=(164, 92, 180), cream=(232, 190, 236), ears='pointy', ear_k=.7,
                horns='imp', wings='imp', tail='spade', nose='none'),
    'gumiho': dict(name='구미호', fur=(206, 208, 218), shade=(170, 172, 186), cream=(248, 248, 252), ear=(236, 190, 210), ears='fox',
                   ear_k=1.15, tail='multi', face='cheeks', nose='dark'),  # 금빛 여우·여우불·한복은 나인테일·아리 → 은회색, 꼬리는 겹친 다발
    'dokkaebi': dict(name='꼬마 도깨비', fur=(120, 176, 200), shade=(92, 148, 176), cream=(206, 230, 238), ears='small', ear_k=.7,
                     horns='one', marks=('hair',), nose='dark'),
}


def mix(a, b, t):
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


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


def ear_tri(cx, cy, rx, ry, s, h, fx):
    """뾰족 귀 삼각형: 머리 위 양옆, 끝이 바깥으로 살짝"""
    base_in, base_out = (cx + s * rx * .18 + (fx - 20) * .3, cy - ry * .78), (cx + s * rx * .86 + (fx - 20) * .3, cy - ry * .42)
    tip = (cx + s * rx * .62 + (fx - 20) * .3, cy - ry - h * .9)
    return [base_in, base_out, tip]


def behind(g, S, c, cx, cy, rx, ry, fx, lie):
    """몸 뒤에 그리는 것: 꼬리, 가시·털뭉치, 뿔, 날개"""
    tail, dark = S.get('tail'), mix(c['shade'], C['line'], .35)
    if tail == 'thin':  # 고양이: 오른쪽 뒤로 휘어 올라간다
        for i in range(9):
            a = -.6 + i * .2
            g.ell(cx + rx * .75 + math.cos(a) * 4 + i * .3, cy + ry * .55 - i * 1.3, 1.5, 1.5, c['shade'])
    elif tail == 'fluffy':  # 여우: 크고 끝이 하얗다
        g.ell(cx + rx * .95, cy + ry * .1, 4.2, ry * .8, c['shade'])
        g.ell(cx + rx * .95 + 1, cy - ry * .55, 3, 2.6, c['cream'])
    elif tail == 'curl':  # 시바: 등 위로 말린 꼬리
        g.ell(cx + rx * .8, cy - ry * .45, 3.6, 3.4, c['shade'])
        g.ell(cx + rx * .8, cy - ry * .45, 1.6, 1.5, c['cream'])
    elif tail == 'bushy':  # 다람쥐: 머리보다 높이 솟는 큰 꼬리
        g.ell(cx + rx * .75, cy - ry * .45, 5.5, ry * 1.05, c['shade'])
        g.ell(cx + rx * .75 + 1.5, cy - ry * 1.15, 3.6, 3.2, c['shade'])
    elif tail == 'ring':  # 너구리: 줄무늬 꼬리
        g.ell(cx + rx * .95, cy + ry * .05, 3.4, ry * .78, c['shade'])
        for i in range(-2, 3):
            y = round(cy + ry * .05 + i * ry * .3)
            g.pix(dark, *[(round(cx + rx * .95) + dx, y) for dx in range(-3, 4)])
    elif tail == 'flat':  # 비버: 납작한 꼬리가 바닥에
        g.ell(cx + rx * .7, cy + ry + .2, 5.5, 2.2, (110, 80, 60))
    elif tail == 'dragon':
        g.poly([(cx + rx * .5, cy + ry * .3), (cx + rx * .6, cy + ry * .95), (cx + rx + 6, cy + ry * .1)], c['shade'])
    elif tail == 'multi':  # 구미호: 겹친 꼬리 다발 (부채처럼 펼치지 않는다)
        for i, (dx, dy) in enumerate(((.7, -.55), (.95, -.15), (.85, .25))):
            g.ell(cx + rx * dx + i * .6, cy + ry * dy, 3.6, ry * .55, c['shade'])
            g.ell(cx + rx * dx + i * .6 + .8, cy + ry * dy - ry * .42, 2.2, 1.8, c['cream'])
    elif tail == 'spade':  # 꼬마 악마: 가는 꼬리 끝에 하트
        for i in range(7):
            g.ell(cx + rx * .7 + i * .9, cy + ry * .55 - i * .9, .9, .9, c['shade'])
        heart(g, round(cx + rx * .7 + 5), round(cy + ry * .55 - 9), c['shade'])
    back = S.get('back')
    if back == 'spikes':  # 고슴도치: 위쪽 둘레로 가시
        for k in range(11):
            a = math.pi + k * math.pi / 10
            bx, by = cx + math.cos(a) * rx * .95, cy + math.sin(a) * ry * .95
            tx, ty = cx + math.cos(a) * (rx + 4), cy + math.sin(a) * (ry + 4)
            nx, ny = -math.sin(a) * 2, math.cos(a) * 2
            g.poly([(bx + nx, by + ny), (bx - nx, by - ny), (tx, ty)], S['spike'])
    elif back == 'wool' and not lie:  # 양: 몸 둘레로 몽글몽글
        for k in range(12):
            a = 2 * math.pi * k / 12
            g.ell(cx + math.cos(a) * rx * .92, cy + math.sin(a) * ry * .88, 3.3, 3.1, c['fur'])
    horns = S.get('horns')
    for s in (-1, 1):
        if horns == 'dragon':
            g.poly([(cx + s * rx * .22, cy - ry * .8), (cx + s * rx * .5, cy - ry * .72), (cx + s * rx * .4, cy - ry - 4)], (244, 220, 140))
        elif horns == 'goat':
            g.poly([(cx + s * rx * .18, cy - ry * .8), (cx + s * rx * .42, cy - ry * .78), (cx + s * rx * .55, cy - ry - 4.5)], (210, 196, 170))
        elif horns == 'imp':
            g.poly([(cx + s * rx * .25, cy - ry * .82), (cx + s * rx * .48, cy - ry * .74), (cx + s * rx * .5, cy - ry - 3)], (90, 60, 110))
        elif horns == 'antler':  # 가지 친 뿔
            bx, by = cx + s * rx * .32, cy - ry * .85
            g.pix((176, 140, 100), *[(bx + s * i * .3, by - i) for i in range(7)],
                  *[(bx + s * (1 + i), by - 3 - i * .6) for i in range(4)], *[(bx + s * (2 + i * .2), by - 6 - i) for i in range(2)])
    if horns == 'unicorn':
        g.poly([(cx - 1.6, cy - ry * .82), (cx + 2.6, cy - ry * .82), (cx + 1, cy - ry - 5)], (240, 206, 110))
    elif horns == 'one':  # 도깨비: 가운데 뿔 하나
        g.poly([(cx - 1.4, cy - ry * .84), (cx + 2.4, cy - ry * .84), (cx + .5, cy - ry - 4)], (246, 236, 210))
    wings = S.get('wings')
    for s in (-1, 1):
        if wings == 'flipper':
            g.ell(cx + s * rx * .98, cy + ry * .15, 2.2, ry * .5, c['shade'])
        elif wings == 'wing':
            g.ell(cx + s * rx * .9, cy + ry * .15, 2.8, ry * .58, mix(c['shade'], C['line'], .2))
        elif wings == 'small':
            g.ell(cx + s * rx * .95, cy + ry * .1, 2.4, ry * .38, c['shade'])
        elif wings == 'dragon':
            g.poly([(cx + s * rx * .5, cy - ry * .3), (cx + s * (rx + 5.5), cy - ry * .95), (cx + s * (rx + 4), cy + ry * .15)], (150, 214, 186))
        elif wings == 'imp':  # 작은 박쥐 날개
            g.poly([(cx + s * rx * .55, cy - ry * .25), (cx + s * (rx + 4.5), cy - ry * .75), (cx + s * (rx + 2.5), cy + ry * .05)], (120, 80, 140))


def marks(g, S, c, cx, cy, rx, ry, fx, ey, my, gap, before_outline):
    """무늬: 윤곽선 전(몸 안쪽 색칠)과 후(선·작은 점)"""
    dark = mix(c['shade'], C['line'], .45)
    for m in S.get('marks', ()):
        if before_outline:
            if m == 'crescent':  # 반달가슴곰: 가슴의 흰 반달
                g.pix(c['cream'], *[(fx + i, round(cy + ry * .42 + abs(i) * .45)) for i in range(-5, 6)],
                      *[(fx + i, round(cy + ry * .42 + abs(i) * .45) + 1) for i in range(-4, 5)])
            elif m == 'disc':  # 부엉이: 눈 둘레 얼굴판
                for s in (-1, 1):
                    g.ell(fx + .5 + s * gap, ey + .5, 4, 3.8, c['cream'], True)
            elif m == 'mask':  # 너구리: 눈 가면
                g.ell(fx + .5, ey + .7, gap + 4, 2.8, (70, 70, 78), True)
                g.ell(fx + .5, ey - 3.2, gap + 2, 1.1, c['cream'], True)
            elif m == 'plates':  # 아기 용: 배 비늘
                for i in range(3):
                    y = round(cy + ry * (.38 + i * .2))
                    g.pix(mix(c['cream'], C['line'], .25), *[(fx + x, y) for x in range(-3, 5)])
            elif m == 'eyepatch':  # 판다: 비스듬한 눈 무늬
                for s in (-1, 1):
                    g.ell(fx + .5 + s * gap, ey + 1, 3.2, 3.8, (66, 62, 68), True)
            elif m == 'brows':  # 레서판다: 흰 눈썹 점
                for s in (-1, 1):
                    g.ell(fx + .5 + s * gap, ey - 3.5, 1.8, 1.1, c['cream'], True)
            elif m == 'patches':  # 얼룩 돼지
                g.ell(cx - rx * .55, cy - ry * .45, 3.4, 2.6, (150, 104, 76), True)
                g.ell(cx + rx * .62, cy + ry * .35, 3, 2.4, (150, 104, 76), True)
            elif m == 'spots':  # 문어: 연한 점
                for x, y in ((-.55, -.55), (.5, -.6), (-.7, .1), (.72, .05)):
                    g.ell(cx + x * rx, cy + y * ry, 1.3, 1.3, c['cream'], True)
            elif m == 'cracks':  # 골렘: 갈라진 금
                d = mix(c['shade'], C['line'], .5)
                g.pix(d, (cx - rx * .6, cy - ry * .2), (cx - rx * .5, cy - ry * .1), (cx - rx * .55, cy), (cx - rx * .45, cy + ry * .1),
                      (cx + rx * .55, cy + ry * .35), (cx + rx * .62, cy + ry * .45), (cx + rx * .5, cy + ry * .5))
            elif m == 'cap':  # 버섯 요정: 머리 위 큰 갓과 크림 점
                g.ell(fx + .5, cy - ry * .55, rx * 1.25, ry * .62, S['cap'])
                g.ell(fx + .5, cy - ry * .32, rx * 1.1, 1.2, mix(S['cap'], C['line'], .3))
                for x, y in ((-.55, -.8), (.15, -1.05), (.6, -.7)):
                    g.ell(fx + .5 + x * rx, cy + y * ry * .78, 1.4, 1.1, c['cream'])
            elif m == 'topknot':  # 알파카: 정수리 털뭉치 (귀 사이를 덮는 뭉게구름)
                for x, y in ((-3.5, .82), (0, .98), (3.5, .82), (-1.8, .7), (1.8, .7)):
                    g.ell(fx + .5 + x, cy - ry * y, 3, 2.7, c['cream'])
            elif m == 'moss':  # 골렘: 이끼와 작은 꽃
                g.ell(fx + .5, cy - ry * .88, rx * .55, 2, (120, 176, 90))
                g.pix((250, 236, 120), (fx + 2, cy - ry - 2))
                g.pix((246, 160, 190), (fx + 1, cy - ry - 2), (fx + 3, cy - ry - 2), (fx + 2, cy - ry - 3), (fx + 2, cy - ry - 1))
            elif m == 'mane':  # 유니콘: 한 가지 색 갈기 (무지개는 피한다)
                g.ell(fx - 1, cy - ry * .78, rx * .42, 2.6, S['mane'])
                g.ell(cx + rx * .78, cy - ry * .2, 2, ry * .45, S['mane'])
            elif m == 'hair':  # 도깨비: 부스스한 머리털
                g.poly([(fx - 5, cy - ry * .7), (fx - 3, cy - ry - 2), (fx - 1, cy - ry * .75), (fx + 1, cy - ry - 3), (fx + 3, cy - ry * .75),
                        (fx + 5, cy - ry - 1), (fx + 6, cy - ry * .6)], (60, 70, 90))
            elif m == 'tuft':  # 오리: 정수리 깃
                g.pix(c['fur'], (fx, cy - ry - 1), (fx + 1, cy - ry - 2), (fx + 1, cy - ry - 1), (fx + 2, cy - ry - 3))
        else:
            if m == 'tabby':  # 고양이: 이마 줄무늬
                for x, h in ((-2, 2), (0, 3), (2, 2)):
                    g.pix(dark, *[(fx + x, round(ey - 4 - i)) for i in range(h)])
            elif m == 'shine':  # 슬라임: 반짝이는 윗면
                g.pix(c['white'], (cx - rx * .55, cy - ry * .55), (cx - rx * .55 + 1, cy - ry * .55), (cx - rx * .55, cy - ry * .55 + 1),
                      (cx - rx * .3, cy - ry * .72))
            elif m == 'beard':  # 염소 수염
                g.pix(c['line'], (fx, my + 3), (fx + 1, my + 3), (fx, my + 4), (fx + 1, my + 4), (fx, my + 5))
    if not before_outline and S.get('whisker'):
        for s in (-1, 1):
            g.pix(c['line'], (fx + s * (gap + 3), my - 1), (fx + s * (gap + 5), my - 2), (fx + s * (gap + 3), my + 1), (fx + s * (gap + 5), my + 1))


def nose_draw(g, kind, fx, my, c):
    if kind == 'pink':
        nose(g, fx, my, c)
    elif kind == 'dark':
        g.pix(c['line'], (fx - 1, my - 2), (fx, my - 2), (fx + 1, my - 2), (fx + 2, my - 2), (fx, my - 1), (fx + 1, my - 1))
    elif kind == 'big':  # 코알라: 크고 까만 코
        g.ell(fx + .5, my - 2.2, 2.2, 2.4, c['line'])
        g.put(fx - 1, my - 3, (120, 110, 110))
    elif kind == 'pig':  # 돼지: 큰 코에 콧구멍 둘
        g.ell(fx + .5, my - 1.5, 3.2, 2.2, c['line'])
        g.ell(fx + .5, my - 1.5, 2.3, 1.4, c['ear'])
        g.pix(c['line'], (fx - 1, my - 2), (fx + 2, my - 2))
    elif kind == 'nostrils':  # 카피바라: 콧구멍 둘
        g.pix(c['line'], (fx - 1, my - 2), (fx + 2, my - 2), (fx - 1, my - 1), (fx + 2, my - 1))


def beak_draw(g, kind, fx, my, c):
    orange, deep = (246, 160, 60), (206, 112, 40)
    if kind == 'small':  # 펭귄: 작은 세모 부리
        g.pix(c['line'], *[(fx + x, my - 3) for x in range(-2, 4)], (fx - 1, my - 1), (fx + 2, my - 1), (fx, my), (fx + 1, my))
        g.pix(orange, *[(fx + x, my - 2) for x in range(-1, 3)], (fx, my - 1), (fx + 1, my - 1))
    elif kind == 'hook':  # 부엉이: 아래로 굽은 부리
        g.pix(c['line'], (fx - 1, my - 3), (fx + 2, my - 3), (fx - 1, my - 2), (fx + 2, my - 2), (fx, my), (fx + 1, my))
        g.pix(orange, (fx, my - 3), (fx + 1, my - 3), (fx, my - 2), (fx + 1, my - 2), (fx, my - 1), (fx + 1, my - 1))
    elif kind == 'bill':  # 오리: 넓적한 부리
        g.ell(fx + .5, my - .8, 4.6, 2.4, c['line'])
        g.ell(fx + .5, my - .8, 3.6, 1.5, orange)
        g.pix(deep, (fx - 1, my - 1), (fx + 2, my - 1))


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
    return pet('hamster', form, cheek, frame)


def pet(species, form, cheek, frame):
    F, f, S = FORMS[form], frame, SPECIES[species]
    if species == 'hamster':
        c = dict(C, fur=F.get('fur', C['fur']), shade=F.get('shade', C['shade']))
    else:  # 다른 종: 아기는 밝게, 심술이는 어둡게, 애교쟁이는 살짝 따뜻하게
        c = dict(C, **{k: S[k] for k in ('fur', 'shade', 'cream', 'ear', 'paw', 'blush') if k in S})
        t = {'baby': (C['white'], .25), 'adult_grumpy': (C['line'], .18), 'adult_sweet': ((255, 200, 160), .12)}.get(form)
        if t:
            c['fur'], c['shade'] = mix(c['fur'], t[0], t[1]), mix(c['shade'], t[0], t[1])
    g = Canvas()
    rx, ry = F['rx'] * S.get('wide', 1), F['ry'] / S.get('wide', 1) ** .5
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
    body = S.get('body')
    if body == 'ghost' and not lie:  # 유령은 살짝 떠 있다
        cy -= 2
    turn = f.get('turn', 0)
    fx = round(20 + turn * (1 if rx > 9 else .7))  # 고개 돌린 쪽으로 얼굴이 옮겨간다
    gap = round(F['eyegap'] - abs(turn) * .4)      # 옆을 보면 눈 사이가 좁아 보인다
    ey = round(cy - ry * .24) - S.get('eye_up', 0)
    my = round(cy + ry * .3)

    # 1) 실루엣 — 꼬리·등(가시·털뭉치)·귀·뿔·날개 → 몸·볼주머니·발
    ear, ex = F['ear'] * S.get('ear_k', 1), rx * .62
    ears = S.get('ears', 'round')
    ear_x = [cx + s * ex + (fx - 20) * .4 for s in (-1, 1)]
    behind(g, S, c, cx, cy, rx, ry, fx, lie)
    if ears in ('round', 'small', 'big'):
        k = {'round': 1, 'small': .75, 'big': 1.7}[ears]
        if ears == 'big':
            ear_x = [cx + s * rx * .82 for s in (-1, 1)]
        for x in ear_x:
            g.ell(x, cy - ry * (.7 if ears == 'big' else .86), ear * k, ear * k, c['fur'])
    elif ears in ('pointy', 'fox', 'tuft'):
        h = ear * (2.1 if ears == 'fox' else 1.7 if ears == 'pointy' else 1.1)
        for s in (-1, 1):
            g.poly(ear_tri(cx, cy, rx, ry, s, h, fx), c['fur'])
    elif ears == 'side':
        for s in (-1, 1):
            g.ell(cx + s * (rx + .6), cy - ry * .4, ear * 1.1, ear * .55, c['fur'])
    elif ears == 'long':  # 알파카: 길쭉하게 선 귀
        for s in (-1, 1):
            g.ell(cx + s * rx * .48, cy - ry - 1.5, 1.7, ear * 1.3, c['fur'])
    if 'ear_out' in S and ears in ('round', 'small', 'big'):  # 판다: 귀 바깥이 까맣다
        for x in ear_x:
            r = ear * {'round': 1, 'small': .75, 'big': 1.7}[ears]
            g.ell(x, cy - ry * .86, r, r, S['ear_out'])
    g.ell(cx, cy, rx, ry, c['shade'])
    if body == 'blob' and not lie:  # 슬라임: 바닥이 퍼진 덩어리
        g.ell(cx, cy + ry * .5, rx * 1.12, ry * .55, c['shade'])
    elif body == 'ghost' and not lie:  # 유령: 아래가 한쪽으로 가늘어지는 연기 꼬리 (물결 밑단은 팩맨 유령)
        for i, (x, y, r) in enumerate(((.15, .85, rx * .72), (.55, 1.05, 3.2), (.85, 1.18, 2.1), (1.05, 1.15, 1.3))):
            g.ell(cx + x * rx, cy + y * ry, r, r * (.7 if i == 0 else 1), c['shade'])
    bulge = cheek * 2.2 + f.get('puff', 0) * 1.2  # 먹은 화면이 많을수록 불룩하다
    if bulge:
        for s in (-1, 1):
            g.ell(fx + .5 + s * rx * .72, cy + ry * .22, 2 + bulge * .75, 2.4 + bulge * .6, c['cream'])
    if body == 'octo' and not lie:  # 문어: 발 대신 다리 다섯
        step = f.get('step', 0)
        for i in range(-2, 3):
            lift = 1 if step and (i + step) % 2 == 0 else 0
            g.ell(cx + i * rx * .4, cy + ry - lift, 1.9, 2.6, c['shade'])
    elif body in ('blob', 'ghost') and not lie:
        if dangle:  # 들리면 작은 팔
            for s in (-1, 1):
                g.ell(cx + s * (rx + .2), cy + 1, 2, 1.6, c['shade'])
    elif body == 'ghost':
        pass
    elif not lie:
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
    if body == 'blob' and not lie:
        g.ell(cx, cy + ry * .45, rx * 1.05, ry * .45, c['fur'], True)
    elif body == 'ghost' and not lie:
        g.ell(cx + rx * .15, cy + ry * .75, rx * .65, ry * .45, c['fur'], True)
    if ears in ('round', 'small', 'big'):
        for x in ear_x:
            r = ear * {'round': .55, 'small': .4, 'big': 1.1}[ears]
            g.ell(x, cy - ry * (.7 if ears == 'big' else .86), r, r, c['ear'])
    elif ears == 'long':
        for s in (-1, 1):
            g.ell(cx + s * rx * .48, cy - ry - 1.5, .8, ear * .9, c['ear'], True)
    elif ears in ('pointy', 'fox'):
        for s in (-1, 1):
            tri = ear_tri(cx, cy, rx, ry, s, ear * (2.1 if ears == 'fox' else 1.7), fx)
            mid = (sum(p[0] for p in tri) / 3, sum(p[1] for p in tri) / 3)
            g.poly([(mid[0] + (x - mid[0]) * .5, mid[1] + (y - mid[1]) * .5 + .5) for x, y in tri], c['ear'], True)
            if ears == 'fox':  # 여우 귀 끝은 짙다
                g.poly([tri[2], (tri[2][0] + (tri[0][0] - tri[2][0]) * .3, tri[2][1] + (tri[0][1] - tri[2][1]) * .3),
                        (tri[2][0] + (tri[1][0] - tri[2][0]) * .3, tri[2][1] + (tri[1][1] - tri[2][1]) * .3)], (70, 44, 34), True)
    face = S.get('face')
    if face == 'bigbelly':  # 펭귄: 얼굴부터 배까지 하얗다
        g.ell(fx + .5, cy + ry * .25, rx * .78, ry * .8, c['cream'], True)
    elif face == 'wool':  # 양: 털 속에 얼굴만 (검지 않게)
        g.ell(fx + .5, cy + ry * .05, rx * .6, ry * .62, c['cream'], True)
    elif S.get('belly', True):
        g.ell(fx + .5, cy + ry * .82, rx * .86, ry * .82, c['cream'], True)
    if face in ('snout', 'capy'):
        g.ell(fx + .5, my - .5, rx * (.5 if face == 'capy' else .42), ry * (.36 if face == 'capy' else .3), S['snout'], True)
    elif face == 'cheeks':  # 시바·여우: 볼과 주둥이가 하얗다
        g.ell(fx + .5, my + .5, rx * .62, ry * .34, c['cream'], True)
    elif face not in ('bigbelly', 'wool', 'frog'):
        g.ell(fx + .5, my, rx * .36, ry * .26, c['cream'], True)
    marks(g, S, c, cx, cy, rx, ry, fx, ey, my, gap, before_outline=True)
    for s in (-1, 1):
        g.ell(fx + .5 + s * (F['eyegap'] + 1.8), cy + ry * .1, (2 + F.get('blush', 0) * .3) * S.get('blush_k', 1), 1.2, c['blush'], True)

    # 3) 윤곽선 → 얼굴
    g.outline(c['line'])
    marks(g, S, c, cx, cy, rx, ry, fx, ey, my, gap, before_outline=False)
    if face == 'frog':  # 개구리 눈은 머리 위 혹에
        ey = round(cy - ry * .78)
    eyes(g, f.get('eye', 'open'), fx, ey, gap, F['eye'] * S.get('eye_k', 1), c)
    if F.get('brow'):  # 심술이: 늘 찌푸린 눈썹
        for s in (-1, 1):
            x = fx + s * gap
            g.pix(c['line'], (x - 2 * s, ey - 3), (x - s, ey - 4), (x, ey - 5), (x + s, ey - 5), (x + 2 * s, ey - 6))
    beak = S.get('beak')
    mk = f.get('mouth', 'w')
    if beak:
        if mk in ('open', 'wide', 'o'):
            mouth(g, mk, fx, my, c)
        beak_draw(g, beak, fx, my, c)
    else:
        nose_kind = S.get('nose', 'pink')
        if nose_kind != 'none':
            nose_draw(g, nose_kind, fx, my, c)
        if S.get('smile') and mk == 'w':  # 쿼카: 늘 활짝 웃는다
            g.pix(c['line'], (fx - 3, my), (fx - 2, my + 1), (fx - 1, my + 2), (fx, my + 2), (fx + 1, my + 2), (fx + 2, my + 2), (fx + 3, my + 1), (fx + 4, my))
            g.pix(c['red'], (fx - 1, my + 1), (fx, my + 1), (fx + 1, my + 1), (fx + 2, my + 1))
        elif face == 'frog' and mk == 'w':  # 개구리: 넓게 웃는 입
            g.pix(c['line'], *[(fx + i, my + (1 if abs(i) < 3 else 0)) for i in range(-4, 5)])
        else:
            mouth(g, mk, fx, my, dict(c, red=S['mouth_in']) if 'mouth_in' in S else c)
        if S.get('teeth') and mk in ('w', 'chew', 'flat'):  # 비버 앞니
            g.pix(c['line'], (fx - 1, my + 1), (fx + 2, my + 1), (fx - 1, my + 2), (fx + 2, my + 2), (fx, my + 3), (fx + 1, my + 3))
            g.pix(c['white'], (fx, my + 1), (fx + 1, my + 1), (fx, my + 2), (fx + 1, my + 2))

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
    if f.get('cover'):  # 앞발로 눈을 가리고 볼이 빨개진다 (유령은 가리지 않는다 — 부우의 특징)
        for s in (-1, 1):
            g.ell(fx + .5 + s * (gap + 1.5), cy + ry * .14, 3, 1.5, c['blush'])
            if not S.get('no_cover'):
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
    # 설정 창 도감(0번), ICBM 구멍에서 빼꼼하는 떠난 친구(0번 + 눈 감은 1번으로 깜빡)
    rows += [(f'thumb/{sp}', [pet(sp, 'child', 0, POSES['idle'][0]), pet(sp, 'child', 0, POSES['idle'][3])]) for sp in SPECIES]
    return rows


def pet_rows(sp):
    """캐릭터 하나의 그림: 형태 × 볼 단계 × 자세"""
    return [(f'{form}/{cheek}/{pose}', [pet(sp, form, cheek, f) for f in frames])
            for form in FORMS for cheek in range(CHEEKS) for pose, frames in POSES.items()]


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
            grid.append([pet('hamster', form, cheek, f) for pose in POSES for f in POSES[pose][:2]])
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


def species_sheet(scale, path, poses=('idle', 'walk', 'happy', 'sleep', 'fix', 'held', 'angry', 'love'), forms=('child', 'adult_chubby')):
    """검수용: 캐릭터마다 한 줄, 자세 몇 개씩"""
    bg = (236, 240, 244)
    only = os.environ.get('PETS', '').split(',') if os.environ.get('PETS') else list(SPECIES)
    grid = [[pet(sp, form, 1, POSES[p][0]) for form in forms for p in poses] for sp in only]
    w = max(len(r) for r in grid)
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


def lzw(data, min_size):
    """GIF 용 LZW 압축 (가변 길이 코드, 4096 에서 다시 시작)"""
    clear, end = 1 << min_size, (1 << min_size) + 1
    size, nxt = min_size + 1, end + 1
    table = {(i,): i for i in range(clear)}
    out, bits, nbits = bytearray(), 0, 0

    def put(code):
        nonlocal bits, nbits
        bits |= code << nbits
        nbits += size
        while nbits >= 8:
            out.append(bits & 255)
            bits >>= 8
            nbits -= 8

    put(clear)
    w = (data[0],)
    for k in data[1:]:
        wk = w + (k,)
        if wk in table:
            w = wk
            continue
        put(table[w])
        if nxt < 4096:
            table[wk] = nxt
            nxt += 1
            if nxt > (1 << size) and size < 12:  # 해독기는 한 칸 늦게 표를 채우니 하나 넘었을 때 늘린다
                size += 1
        else:
            put(clear)
            table, size, nxt = {(i,): i for i in range(clear)}, min_size + 1, end + 1
        w = (k,)
    put(table[w])
    put(end)
    if nbits:
        out.append(bits & 255)
    return bytes(out)


def gif(frames, path, delay_ms):
    """움직이는 GIF (배경 투명, 무한 반복). frames: 같은 크기의 [줄][칸] = (r,g,b) 또는 None"""
    h, w = len(frames[0]), len(frames[0][0])
    count = {}
    for fr in frames:
        for row in fr:
            for c in row:
                if c:
                    count[c] = count.get(c, 0) + 1
    colors = sorted(count, key=count.get, reverse=True)[:255]  # 0 번은 투명
    index = {c: i + 1 for i, c in enumerate(colors)}

    def near(c):
        if c not in index:
            index[c] = min(range(len(colors)), key=lambda i: sum((a - b) ** 2 for a, b in zip(colors[i], c))) + 1
        return index[c]

    pal = b''.join(bytes(c) for c in [(0, 0, 0)] + colors) + b'\0' * 3 * (255 - len(colors))
    data = bytearray(b'GIF89a' + struct.pack('<HHBBB', w, h, 0xF7, 0, 0) + pal)
    data += b'\x21\xff\x0bNETSCAPE2.0\x03\x01\x00\x00\x00'  # 무한 반복
    for fr in frames:
        data += b'\x21\xf9\x04' + bytes([(2 << 2) | 1]) + struct.pack('<H', delay_ms // 10) + b'\x00\x00'  # 다음 장 전에 지우기, 0 번 투명
        data += b'\x2c' + struct.pack('<HHHHB', 0, 0, w, h, 0) + b'\x08'
        packed = lzw([near(c) if c else 0 for row in fr for c in row], 8)
        for i in range(0, len(packed), 255):
            data += bytes([len(packed[i:i + 255])]) + packed[i:i + 255]
        data += b'\x00'
    pathlib.Path(path).write_bytes(bytes(data + b'\x3b'))


def parade(path, cols=9, scale=2):
    """README 용: 모든 캐릭터가 걷다가 웃고 윙크한다 (캐릭터마다 박자를 어긋나게)"""
    seq = [('walk', i) for i in range(4)] * 2 + [('happy', 0), ('happy', 1), ('wink', 0), ('idle', 3), ('love', 0), ('love', 1)]
    species = list(SPECIES)
    rows = (len(species) + cols - 1) // cols
    W, H = cols * CELL * scale, rows * CELL * scale
    cache, frames = {}, []
    for t in range(len(seq)):
        fr = [[None] * W for _ in range(H)]
        for n, sp in enumerate(species):
            pose, f = seq[(t + n * 5) % len(seq)]
            key = (sp, pose, f)
            if key not in cache:
                cache[key] = pet(sp, 'child', 1, POSES[pose][f % len(POSES[pose])])
            row, in_row = n // cols, min(cols, len(species) - n // cols * cols)
            g, oy = cache[key], row * CELL * scale
            ox = (n % cols) * CELL * scale + (cols - in_row) * CELL * scale // 2  # 마지막 줄은 가운데로
            for y in range(CELL):
                for x in range(CELL):
                    if g.p[y][x]:
                        for a in range(scale):
                            for b in range(scale):
                                fr[oy + y * scale + a][ox + x * scale + b] = g.p[y][x]
        frames.append(fr)
    gif(frames, path, 160)


def write(rows, png_path, json_path):
    cols = max(len(f) for _, f in rows)
    # JSON 을 먼저 쓴다. 떠 있는 앱은 PNG 가 바뀌면 둘 다 다시 읽는다
    meta = {'cell': CELL, 'rows': {n: [i, len(f)] for i, (n, f) in enumerate(rows)}}
    if any(n.startswith('thumb/') for n, _ in rows):  # 공통 그림에는 캐릭터 이름표도 (설정 창이 쓴다)
        meta['species'] = {k: v['name'] for k, v in SPECIES.items()}
    json_path.write_text(json.dumps(meta, ensure_ascii=False))
    png(sheet(rows, cols), png_path)
    return cols


def manifest(root):
    """캐릭터 그림 목록 (크기·sha256). exe 에 들어가고, 앱은 저장소에서 받은 그림이 이것과 맞을 때만 쓴다"""
    import hashlib
    pets = root / 'assets/pets'
    out = {}
    for sp in SPECIES:
        e = {}
        for ext in ('png', 'json'):
            b = (pets / f'{sp}.{ext}').read_bytes()
            e[ext] = hashlib.sha256(b).hexdigest()
            e[ext + '_size'] = len(b)
        out[sp] = e
    (pets / 'manifest.json').write_text(json.dumps(out, indent=1, sort_keys=True) + '\n')


def main():
    """python3 tools/sprites.py                  공통 그림 + 캐릭터 전부
       python3 tools/sprites.py --manifest       캐릭터 그림 목록(크기·sha256)만 다시
       python3 tools/sprites.py --pets fox,cat   공통 그림 + 고른 캐릭터만
       python3 tools/sprites.py --look 미리.png   캐릭터 검수용 그림만 (assets 는 안 건드린다)
       python3 tools/sprites.py --gif 행진.gif    README 용 움직이는 그림"""
    root = pathlib.Path(__file__).resolve().parent.parent
    args = sys.argv[1:]
    if args[:1] == ['--look']:
        species_sheet(3, args[1])
        return
    if args[:1] == ['--gif']:
        parade(args[1])
        return
    if args[:1] == ['--manifest']:
        manifest(root)
        return
    pets = list(SPECIES) if args[:1] != ['--pets'] else args[1].split(',')
    (root / 'assets/pets').mkdir(parents=True, exist_ok=True)
    for sp in pets:  # 캐릭터 그림을 먼저, 공통 그림은 마지막에 (떠 있는 앱은 공통 PNG 가 바뀌면 다시 읽는다)
        write(pet_rows(sp), root / f'assets/pets/{sp}.png', root / f'assets/pets/{sp}.json')
    manifest(root)
    rows = build()
    cols = write(rows, root / 'assets/sprites.png', root / 'assets/sprites.json')
    if args[:1] and args[0] not in ('--pets', '--look'):
        contact(rows, 3, args[0])
    print(f'공통 {len(rows)}행 {cols}열, 캐릭터 {len(pets)}종')


if __name__ == '__main__':
    main()
