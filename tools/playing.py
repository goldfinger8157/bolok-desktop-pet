#!/usr/bin/env python3
"""YELL 이 게임 중이면 게임 이름을 찍고 0 으로 끝난다. dev.sh 가 판을 끊지 않으려고 쓴다.
run/log.jsonl 에서 마지막 앱 시작 이후의 게임 시작·끝만 본다 (2026-11-03 다시 띄우다 벽돌깨기 판을 끊었다).
게임은 모두 모듈(app/Games/<게임>/)이라 이름을 여기 적지 않는다: mode-start / mode-end 의 id 로 안다."""
import json
import pathlib
import sys

LOG = pathlib.Path(__file__).resolve().parent.parent / 'run/log.jsonl'

try:
    lines = LOG.read_bytes().decode('utf-8', 'replace').splitlines()[-5000:]
except OSError:
    sys.exit(1)
active = set()
for raw in lines:
    try:
        rec = json.loads(raw)
    except ValueError:
        continue
    ev = rec.get('ev')
    if ev == 'start':
        active.clear()
    elif ev == 'mode-start':
        active.add(rec.get('id', '게임'))
    elif ev == 'mode-end':
        active.discard(rec.get('id', '게임'))
if active:
    print(' '.join(sorted(active)))
    sys.exit(0)
sys.exit(1)
