#!/usr/bin/env python3
"""볼록이 감시 — run/log.jsonl 을 읽다가 손볼 일이 생겼을 때만 한 줄 출력한다. Monitor 로 띄운다.

로그를 열어 둔 채 기다리지 않고 2초마다 열었다 닫는다. 열어 두면 Windows 쪽에서 파일을 다루다 앱이 죽은 적이 있다.

--once 를 주면 첫 알림을 내고 끝난다. Bash 백그라운드로 띄우면 시간 제한 없이 문제가 생길 때만 깨어난다.
"""
import json
import pathlib
import sys
import time

LOG = pathlib.Path(__file__).resolve().parent.parent / 'run/log.jsonl'
STALE_SEC = 40          # 10초마다 beat 가 찍히므로 이만큼 조용하면 멈춘 것이다
HOLD = 3                # 연속 beat 수: 30초 넘게 이어질 때만 알린다
# (이름, 값 꺼내기, 한계). 재 본 값: CPU 0.05~0.5%, 메모리 100MB, GDI 56, 프레임 3ms 이하
LIMITS = [
    ('CPU', lambda d: d.get('cpu', 0), 3.0, '%'),
    ('메모리', lambda d: d.get('mem', 0), 250, 'MB'),
    ('GDI 객체', lambda d: d.get('gdi', 0), 300, '개'),
    ('프레임 처리', lambda d: d.get('frameMs', 0), 12, 'ms'),
]
ALERTS = {'error', 'fatal', 'load-error', 'devlink-error', 'cmd-error'}
QUIET = {}   # 잦을 수 있는 경고는 이 초마다 한 번만. z-off 는 숨은 창이 활성화되는 순간 늘 한 번 나와서 뺐다 (로그에는 남는다)

streak = {name: 0 for name, *_ in LIMITS}
alarmed = set()
last_said = {}


ONCE = '--once' in sys.argv


def say(text):
    print(text, flush=True)
    if ONCE:
        sys.exit(0)


GAME_MODES = {'Puppet'}   # 게임은 모두 꼭두각시(Puppet)다. 게임 중엔 CPU 를 더 쓴다 (미니건 평균 3.3%·최대 4.7%, 2026-11-11)
GAME_CPU = 8.0


def beat(d):
    for name, get, limit, unit in LIMITS:
        if name == 'CPU' and d.get('mode') in GAME_MODES:
            limit = GAME_CPU
        v = get(d)
        streak[name] = streak[name] + 1 if v >= limit else 0
        if streak[name] >= HOLD and name not in alarmed:
            alarmed.add(name)
            say(f'{name} 높음: {v}{unit} (기준 {limit}{unit}) mode={d.get("mode")} pose={d.get("pose")} fps={d.get("fps")}')
        elif v < limit and name in alarmed:
            alarmed.discard(name)
            say(f'{name} 정상으로 돌아옴: {v}{unit}')


def line(raw):
    try:
        d = json.loads(raw)
    except ValueError:
        return
    ev = d.get('ev')
    if ev == 'beat':
        beat(d)
    elif ev == 'start':
        for k in streak:
            streak[k] = 0
        alarmed.clear()
    elif ev in ALERTS:
        say(raw[:400])
    elif ev in QUIET and time.time() - last_said.get(ev, 0) >= QUIET[ev]:
        last_said[ev] = time.time()
        say(raw[:200])


def main():
    pos = LOG.stat().st_size if LOG.exists() else 0
    tail = b''
    seen = time.time()
    stale = False
    while True:
        time.sleep(2)
        try:
            with LOG.open('rb') as f:
                f.seek(pos)
                data = f.read()
                pos += len(data)
        except OSError:
            continue
        if data:
            seen = time.time()
            if stale:
                stale = False
                say('볼록이 로그 다시 움직임')
            *lines, tail = (tail + data).split(b'\n')
            for raw in lines:
                line(raw.decode('utf-8', 'replace'))
        elif not stale and time.time() - seen > STALE_SEC:
            stale = True
            say(f'볼록이 로그가 {STALE_SEC}초 넘게 멈춤 (앱이 죽었거나 멈췄을 수 있음)')


if __name__ == '__main__':
    main()
