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

# 개발 폴더(run\bin-*)에서 뜬 볼록이만 다룬다. 받은 판(배포용 exe)은 저장도 따로라 절대 끄지 않는다
pids() {
  powershell.exe -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='Bolok.exe'\" | ForEach-Object { \$_.ProcessId.ToString() + ' ' + [int](\$_.ExecutablePath -like '*\\run\\bin-*') }" 2>/dev/null | tr -d '\r'
}
dev_pids() { pids | awk '$2 == 1 { print $1 }'; }
other_pids() { pids | awk '$2 == 0 { print $1 }'; }
if [ -n "$(other_pids)" ]; then
  echo '받은 판 볼록이가 떠 있어요 (한 번에 하나만 떠요). 그걸 종료한 뒤 다시 해 주세요'; exit 1
fi
if [ -n "$(dev_pids)" ]; then
  echo quit >> run/cmd.txt
  for _ in 1 2 3 4 5 6 7 8 9 10; do [ -z "$(dev_pids)" ] && break; sleep .5; done
  for p in $(dev_pids); do taskkill.exe /PID "$p" /F >/dev/null 2>&1; done
fi

echo "$next" > run/slot
starts=$(grep -c '"ev":"start"' run/log.jsonl 2>/dev/null || true)
powershell.exe -NoProfile -Command "Start-Process -FilePath '$win\\run\\bin-$next\\Bolok.exe' -ArgumentList '--debug'"
for _ in $(seq 1 20); do
  [ "$(grep -c '"ev":"start"' run/log.jsonl 2>/dev/null)" -gt "${starts:-0}" ] && { echo "띄웠어요: bin-$next"; exit 0; }
  sleep .5
done
echo '앱이 뜨지 않았어요'; exit 1
