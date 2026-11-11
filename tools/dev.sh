#!/usr/bin/env bash
# 빌드해서 볼록이를 디버그 모드로 다시 띄운다. 상태는 run/pet.json 으로 이어진다.
# 빌드에 실패하면 떠 있는 앱은 건드리지 않는다 — 실행 중인 exe 가 잠겨 있어 bin-a·bin-b 를 번갈아 쓴다.
set -uo pipefail
cd "$(dirname "$0")/.."
mkdir -p run
cur=$(cat run/slot 2>/dev/null || echo b)
next=$([ "$cur" = a ] && echo b || echo a)
win=$(wslpath -w "$PWD")

dotnet.exe build "$win\\app\\Bolok.csproj" -c Debug -o "$win\\run\\bin-$next" --nologo -v q -clp:ErrorsOnly 2>&1 | tr -d '\r'
[ "${PIPESTATUS[0]}" -eq 0 ] || { echo '빌드 실패: 떠 있는 앱은 그대로 둡니다'; exit 1; }

# YELL 이 게임 중이면 판이 끝날 때까지 기다린다 (최대 10분)
if game=$(python3 tools/playing.py); then
  echo "게임 중($game)이라 끝나길 기다려요"
  for _ in $(seq 1 300); do python3 tools/playing.py >/dev/null || break; sleep 2; done
fi

# grep -q 를 쓰면 일찍 끝나서 pipefail 이 "없음"으로 착각한다
running() { tasklist.exe /FI "IMAGENAME eq Bolok.exe" 2>/dev/null | grep Bolok.exe >/dev/null; }
if running; then
  echo quit >> run/cmd.txt
  for _ in 1 2 3 4 5 6 7 8 9 10; do running || break; sleep .5; done
  running && taskkill.exe /IM Bolok.exe /F >/dev/null 2>&1
fi

echo "$next" > run/slot
starts=$(grep -c '"ev":"start"' run/log.jsonl 2>/dev/null || true)
powershell.exe -NoProfile -Command "Start-Process -FilePath '$win\\run\\bin-$next\\Bolok.exe' -ArgumentList '--debug'"
for _ in $(seq 1 20); do
  [ "$(grep -c '"ev":"start"' run/log.jsonl 2>/dev/null)" -gt "${starts:-0}" ] && { echo "띄웠어요: bin-$next"; exit 0; }
  sleep .5
done
echo '앱이 뜨지 않았어요'; exit 1
