// 볼록이 상태 모델 — 규칙 전부. 화면과 무관하게 돈다.
// 미리보기가 그대로 쓰고, `node preview/model.js` 로 일주일 시뮬레이션과 자가 검사를 돌린다.
// 시간 단위는 "활동 분": PC 를 쓰는 시간만 센다. 앱이 꺼져 있거나 자리를 비우면 멈춘다.

// 똥·병·훈육은 1차에서 뺐다
const K = {
  fullPerH: 20, moodPerH: 25, energyPerH: 15, // 100 → 0 까지 5시간·4시간·6.7시간
  regenPerH: 200,                             // 낮잠 30분이면 기운이 다 찬다
  feedFull: 35, feedMood: 3,
  petMood: 8,
  playMood: 40, playEnergy: 8, playFull: 5,
  wakeMood: -5,                               // 억지로 깨우면 삐진다
  callAt: 25,                                 // 이 아래로 내려가면 말풍선으로 부른다
  graceMin: 15, missEveryMin: 60,             // 0 에서 15분 방치 → 돌봄 실수, 이후 1시간마다 하나 더 (P1 의 15분 규칙)
  biteEveryMin: 10, maxBites: 12,             // 기분 0 에서 15분 뒤부터 10분마다 한 입
  spitMood: 30, spitMin: 0.05,                // 기분이 풀리면 3초에 한 입씩 뱉는다
  eggMin: 5, babyMin: 65, childMin: 38 * 60,  // 월요일에 부화해서 금요일 오후에 어른 (하루 8시간)
  grumpyBites: 12, grumpyMiss: 8, sweetRatio: 3,
};

function newPet() {
  return {
    stage: 'egg', ageMin: 0, full: 80, mood: 80, energy: 100, asleep: false,
    bites: 0, sulkMin: 0, biteIn: 0, spitIn: 0, zero: { full: 0, mood: 0 },
    miss: 0, feeds: 0, pets: 0, plays: 0, bitesTotal: 0,
  };
}

const clamp = v => Math.max(0, Math.min(100, v));

function adultForm(p) {
  if (p.bitesTotal >= K.grumpyBites || p.miss >= K.grumpyMiss) return 'adult_grumpy';
  return p.pets >= p.feeds * K.sweetRatio ? 'adult_sweet' : 'adult_chubby';
}

// m 활동 분이 지났다. afk 면 자리 비움. 일어난 일을 배열로 돌려준다.
function tick(p, m, afk = false) {
  const ev = [];
  if (p.stage === 'egg') {
    if (afk) return ev;
    p.ageMin += m;
    if (p.ageMin >= K.eggMin) { p.stage = 'baby'; ev.push('hatch'); }
    return ev;
  }
  if (afk && !p.asleep) { p.asleep = true; ev.push('sleep'); }
  if (p.asleep) { // 자는 동안 배고픔·기분·나이는 멈춘다. 주말이 지나도 안 죽는다
    p.energy = clamp(p.energy + K.regenPerH * m / 60);
    if (!afk && p.energy >= 100) { p.asleep = false; ev.push('wake'); }
    return ev;
  }

  p.ageMin += m;
  p.full = clamp(p.full - K.fullPerH * m / 60);
  p.mood = clamp(p.mood - K.moodPerH * m / 60);
  p.energy = clamp(p.energy - K.energyPerH * m / 60);
  if (p.energy <= 0) { p.asleep = true; ev.push('sleep'); }

  for (const k of ['full', 'mood']) {
    p.zero[k] = p[k] > 0 ? 0 : p.zero[k] + m;
    if (p.zero[k] >= K.graceMin) { p.miss++; p.zero[k] -= K.missEveryMin; ev.push('miss'); }
  }

  p.sulkMin = p.mood > 0 ? 0 : p.sulkMin + m;
  if (p.sulkMin >= K.graceMin) {
    p.biteIn -= m;
    if (p.biteIn <= 0 && p.bites < K.maxBites) {
      p.bites++; p.bitesTotal++; p.biteIn = K.biteEveryMin; ev.push('bite');
    }
  } else p.biteIn = 0;

  if (p.bites && p.mood >= K.spitMood) {
    p.spitIn -= m;
    if (p.spitIn <= 0) { p.bites--; p.spitIn = K.spitMin; ev.push('spit'); }
  }

  if (p.stage === 'baby' && p.ageMin >= K.babyMin) { p.stage = 'child'; ev.push('grow'); }
  if (p.stage === 'child' && p.ageMin >= K.childMin) { p.stage = adultForm(p); ev.push('grow'); }
  return ev;
}

// 돌봄. 안 먹히면 false — 미리보기·앱은 도리도리로 보여준다
function act(p, what) {
  if (p.stage === 'egg') return false;
  if (p.asleep && what !== 'wake') return false;
  switch (what) {
    case 'feed':
      if (p.full >= 100) return false;
      p.full = clamp(p.full + K.feedFull); p.mood = clamp(p.mood + K.feedMood); p.feeds++;
      return true;
    case 'pet':
      p.mood = clamp(p.mood + K.petMood); p.pets++;
      return true;
    case 'play':
      if (p.energy < K.playEnergy) return false;
      p.mood = clamp(p.mood + K.playMood); p.energy = clamp(p.energy - K.playEnergy);
      p.full = clamp(p.full - K.playFull); p.plays++;
      return true;
    case 'sleep':
      if (p.energy > 80) return false;
      p.asleep = true;
      return true;
    case 'wake':
      if (!p.asleep) return false;
      p.asleep = false; p.mood = clamp(p.mood + K.wakeMood);
      return true;
  }
  return false;
}

const need = p => p.stage === 'egg' || p.asleep ? null
  : p.full < K.callAt ? 'hungry' : p.mood < K.callAt ? 'bored' : p.energy < K.callAt ? 'sleepy' : null;
const pose = p => p.asleep ? 'sleep' : p.full < K.callAt ? 'hungry' : p.mood < K.callAt ? 'sad' : 'idle';
const cheek = p => Math.ceil(p.bites / (K.maxBites / 3));

// ---- 일주일 시뮬레이션: 9시 출근, 12시 점심 1시간 자리 비움, 18시 퇴근 ----
function week(careAt, care) {
  const p = newPet();
  const log = { minFull: 100, minMood: 100, firstBite: null, sleeps: 0 };
  let active = 0;
  for (let day = 0; day < 5; day++) {
    for (let t = 9 * 60; t < 18 * 60; t++) {
      if (careAt.includes(t)) care(p);
      const ev = tick(p, 1, t >= 12 * 60 && t < 13 * 60);
      if (t < 12 * 60 || t >= 13 * 60) active++;
      if (ev.includes('bite') && log.firstBite === null) log.firstBite = active;
      log.sleeps += ev.filter(e => e === 'sleep').length;
      if (p.stage !== 'egg') { log.minFull = Math.min(log.minFull, p.full); log.minMood = Math.min(log.minMood, p.mood); }
    }
    tick(p, 15 * 60, true); // 퇴근 후 꺼져 있던 시간은 켤 때 "잔 것"으로 친다
  }
  return { p, log };
}

function repeat(p, what, n) { for (let i = 0; i < n; i++) act(p, what); }
const h = (a, m = 0) => a * 60 + m;
const session = (feed, pet, play) => p => { repeat(p, 'feed', feed); repeat(p, 'pet', pet); repeat(p, 'play', play); };
const THREE = [h(9, 30), h(13, 30), h(16)];
const SCENARIOS = [
  { name: '성실', desc: '하루 3번, 밥 2·쓰다듬기 3·놀기 1', times: THREE, care: session(2, 3, 1) },
  { name: '애교', desc: '하루 3번, 밥 2·쓰다듬기 8·놀기 1', times: THREE, care: session(2, 8, 1) },
  { name: '바쁜 주', desc: '하루 1번, 밥 2·쓰다듬기 3·놀기 1', times: [h(9, 30)], care: session(2, 3, 1) },
  { name: '방치', desc: '일주일 내내 안 돌봄', times: [], care: () => {} },
];

function selfCheck() {
  const assert = require('assert');
  const [good, sweet, once, none] = SCENARIOS.map(s => {
    const r = week(s.times, s.care);
    row(s.name, r);
    return r;
  });
  function row(name, r) { console.log(
    name.padEnd(8), r.p.stage.padEnd(13), `실수 ${r.p.miss}`, `누적 ${r.p.bitesTotal}입`, `남은 ${r.p.bites}입`,
    `최저 포만 ${r.log.minFull.toFixed(0)}`, `기분 ${r.log.minMood.toFixed(0)}`,
    `첫 입 ${r.log.firstBite === null ? '-' : (r.log.firstBite / 60).toFixed(1) + '시간'}`); }

  // 하루 3번 돌보면 무탈하게 어른이 된다
  assert.equal(good.p.miss, 0); assert.equal(good.p.bitesTotal, 0); assert.equal(good.p.stage, 'adult_chubby');

  // 쓰다듬기를 듬뿍 하면 애교쟁이
  assert.equal(sweet.p.stage, 'adult_sweet');

  // 하루 한 번만 봐주면 화면을 먹기 시작한다
  assert.ok(once.p.bitesTotal > 0);

  // 전혀 안 돌보면 심술이가 되고 화면을 최대치까지 먹는다
  assert.equal(none.p.stage, 'adult_grumpy'); assert.equal(none.p.bites, K.maxBites);

  // 돌봐주면 먹은 화면을 전부 뱉는다
  const back = none.p;
  tick(back, 0); // 출근: 자리에 돌아오면 깬다
  repeat(back, 'pet', 2); act(back, 'play');
  for (let i = 0; i < 60 && back.bites; i++) tick(back, K.spitMin);
  assert.equal(back.bites, 0);

  // 자리를 비운 동안은 나빠지지 않는다 (주말·회의)
  const away = newPet(); tick(away, K.eggMin);
  const before = [away.full, away.mood, away.ageMin];
  for (let i = 0; i < 60 * 48; i++) tick(away, 1, true);
  assert.deepEqual([away.full, away.mood, away.ageMin], before);
  tick(away, 0); act(away, 'wake');
  assert.equal(away.asleep, false);

  // 알은 돌볼 수 없고, 잠든 동안은 깨우기 말고는 안 먹힌다
  const e = newPet(); assert.equal(act(e, 'feed'), false);
  const s = newPet(); tick(s, K.eggMin); s.energy = 50; act(s, 'sleep');
  assert.equal(act(s, 'pet'), false); assert.equal(act(s, 'wake'), true);

  console.log('자가 검사 통과');
}

if (typeof module !== 'undefined') module.exports = { K, newPet, tick, act, week, SCENARIOS };
if (typeof require !== 'undefined' && require.main === module) selfCheck();
