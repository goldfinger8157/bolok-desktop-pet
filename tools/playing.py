#!/usr/bin/env python3
"""YELL 이 게임 중이면 게임 이름을 찍고 0 으로 끝난다. dev.sh 가 판을 끊지 않으려고 쓴다.
run/log.jsonl 에서 마지막 앱 시작 이후의 게임 시작·끝만 본다 (2026-11-03 다시 띄우다 벽돌깨기 판을 끊었다)."""
import json
import pathlib
import sys

LOG = pathlib.Path(__file__).resolve().parent.parent / 'run/log.jsonl'
START = {'game': '총쏘기', 'run-start': '달리기', 'break-start': '벽돌깨기', 'worms-start': '포트리스', 'gun-start': '미니건', 'icbm-start': 'ICBM', 'squad-start': '볼록이 부대'}
END = {'game-end': '총쏘기', 'run-end': '달리기', 'run-quit': '달리기', 'break-end': '벽돌깨기', 'break-quit': '벽돌깨기', 'worms-end': '포트리스', 'gun-end': '미니건', 'icbm-end': 'ICBM', 'squad-end': '볼록이 부대'}

try:
    lines = LOG.read_bytes().decode('utf-8', 'replace').splitlines()[-5000:]
except OSError:
    sys.exit(1)
active = set()
for raw in lines:
    try:
        ev = json.loads(raw).get('ev')
    except ValueError:
        continue
    if ev == 'start':
        active.clear()
    elif ev in START:
        active.add(START[ev])
    elif ev in END:
        active.discard(END[ev])
if active:
    print(' '.join(sorted(active)))
    sys.exit(0)
sys.exit(1)
