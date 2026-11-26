namespace Bolok;

// 클라이밍 콤보·카페 자가 검사 (docs/climb-combo.md 8장): 콤보 시간·단계·보상·끊김 · 같은 씨 = 같은 콤보·카페 · 없앤 것이 없다 · 카페 자리·드나들기·막대·Esc·다시 칠하기·메모리 · 효과가 앞길을 안 가린다
static partial class ClimbCheck
{
    static Climb CafeStand(int d, double bar = .2)
    {
        var t = new Climb(5, ClimbTune.Now with { Gimmicks = false }, 87);
        Shape(t, CafeDirs(d));
        for (int j = t.N - 3; j <= t.N + 12; j++) { t.At(j).Kind = ClimbKind.Leaf; t.At(j).Fog = false; t.At(j).Cafe = 0; }
        t.PutCafe(d);
        t.State = Climb.Phase.Run; t.Bar = bar; t.Steps = 5;
        return t;
    }

    static void Tk(Climb t, int n) { for (int i = 0; i < n; i++) t.Update(Climb.Tick); }

    // ── 콤보 ──
    static string CheckCombo()
    {
        var T = ClimbTune.Now;
        // 시간: 30틱째 키는 이어지고, 31틱째는 키 없이도 그 틱에 끊긴다 (키가 늦게 와도 1부터)
        var t = Fresh(); t.GraceLeft = 1e9;
        var ev = new List<(string, int)>();
        t.OnEvent = e => ev.Add((e, t.Ticks));
        Hop(t);
        Expect(t.Combo == 1 && t.MaxCombo == 1, "첫 칸은 콤보 1");
        Tk(t, 30); Hop(t);
        Expect(t.Combo == 2, "앞 칸에서 30틱(0.5초) 안이면 이어진다");
        Tk(t, 30);
        Expect(t.Combo == 2, "30틱째까지는 안 끊긴다");
        int at = t.Ticks; Tk(t, 1);
        Expect(t.Combo == 0 && ev.Contains(("combo-end", at + 1)), "31틱째에 키가 없어도 그 틱에 0 (사건 combo-end)");
        Hop(t);
        Expect(t.Combo == 1 && t.MaxCombo == 2, "끊긴 뒤 키는 1부터 (가장 긴 콤보는 그대로)");
        var t2 = Fresh(); t2.GraceLeft = 1e9; Hop(t2); Tk(t2, 31); Hop(t2);
        Expect(t2.Combo == 1, "31틱 뒤 키는 새 콤보");
        // 단계 5·15·30, 5마다 막대 .04/.06/.08 (그 칸에서 오른 Gain 위에), 사건 tier·combo5
        var t3 = Fresh(); t3.GraceLeft = 1e9;
        var ev3 = new List<string>();
        t3.OnEvent = e => ev3.Add(e);
        for (int i = 1; i <= 40; i++)
        {
            t3.Bar = .3; Hop(t3);
            int tier = i < 5 ? 0 : i < 15 ? 1 : i < 30 ? 2 : 3;
            double reward = i % 5 == 0 && i >= 5 ? new[] { 0, .04, .06, .08 }[tier] : 0;
            Expect(t3.Combo == i && t3.Tier == tier, $"{i}칸: 콤보 {t3.Combo} 단계 {t3.Tier} (단계 {tier})");
            Expect(Math.Abs(t3.Bar - Math.Min(1, .3 + T.Gain + reward)) < 1e-9, $"{i}칸: 막대 {t3.Bar:0.000} (보상 {reward})");
            Tk(t3, 10);
        }
        Expect(ev3.Count(e => e == "tier") == 3 && ev3.Count(e => e == "combo5") == 8, $"사건 tier 3번·combo5 8번 ({ev3.Count(e => e == "tier")}·{ev3.Count(e => e == "combo5")})");
        Expect(t3.MaxCombo == 40 && t3.Steps == 40 && t3.N == 40, "점수는 오른 칸 수 그대로, 가장 긴 콤보 40");
        var t4 = Fresh(); t4.GraceLeft = 1e9; t4.SetCombo(29); t4.Bar = .95; Hop(t4);
        Expect(t4.Combo == 30 && t4.Bar == 1, "막대는 1 에서 멈춘다 (보상이 넘쳐도)");
        // 끊는 것: 쉼(위) · 홀씨로 살아남 · 떨어짐 · (카페·앞 창 아님은 아래·게임)
        var t5 = Fresh(); t5.GraceLeft = 1e9; for (int i = 0; i < 7; i++) { Hop(t5); Tk(t5, 5); }
        t5.Puff = true; t5.Press(-t5.At(t5.N + 1).Dir);
        Expect(t5.Saves == 1 && t5.Combo == 0 && t5.MaxCombo == 7 && t5.ComboBroken == 7, "홀씨로 살아남으면 콤보가 끊긴다");
        var t6 = Fresh(); t6.GraceLeft = 1e9; for (int i = 0; i < 6; i++) { Hop(t6); Tk(t6, 5); }
        t6.Press(-t6.At(t6.N + 1).Dir);
        Expect(t6.Done && t6.Combo == 0 && t6.MaxCombo == 6, "떨어지면 콤보가 끊긴다");
        // 안 끊는 것: 열매·큰 꽃·시든 잎
        var t7 = Fresh(); t7.GraceLeft = 1e9; Hop(t7); Tk(t7, 5);
        t7.At(t7.N + 1).Kind = ClimbKind.Berry; t7.At(t7.N + 2).Kind = ClimbKind.Flower; t7.At(t7.N + 3).Kind = ClimbKind.Wilt;
        for (int i = 0; i < 3; i++) { Hop(t7); Tk(t7, 5); }
        Expect(t7.Combo == 4 && t7.Berries == 1 && !t7.Done, "열매·큰 꽃·시든 잎을 지나도 콤보가 이어진다");
        // 앞 창이 아니면 (게임): 콤보가 끊긴다
        var host = new SceneHost("adult_sweet", 3);
        var field = new Rectangle(660, 300, 450, 528);
        bool front = true;
        var g = NewGame(host);
        var gt = new Climb(8, ClimbTune.Now with { Gimmicks = false });
        g.Begin(gt, ClimbLook.A2, field, Session.Fake(field, null, () => front), host.Pet.P.Stage);
        g.Frame(1 / 60.0);
        for (int i = 0; i < 6; i++) { gt.Press(gt.At(gt.N + 1).Dir); Tk(gt, 4); }
        Expect(gt.Combo == 6, "게임 속 콤보 6");
        front = false; g.Frame(1 / 60.0);
        Expect(gt.Combo == 0 && gt.MaxCombo == 6, "판이 앞 창이 아니게 되면 콤보가 끊긴다");
        g.Stop(true);
        // 손잡이: 단계 값·폴짝 높이는 6·4·3·2
        Expect(T.ComboTicks == 30 && T.Tier1 == 5 && T.Tier2 == 15 && T.Tier3 == 30 && T.HopArc.SequenceEqual(new[] { 6, 4, 3, 2 }), "콤보 손잡이: 30틱·5/15/30·폴짝 6/4/3/2");
        return "30틱째 이어짐·31틱째 그 틱에 끊김·단계 5/15/30·막대 .04/.06/.08(1 에서 멈춤)·끊는 것(쉼·홀씨·떨어짐·앞 창 아님)·안 끊는 것(열매·꽃·시든 잎)·점수 그대로";
    }

    // 잎 1~n 의 카페 쪽 (카페는 잎이 만들어지고 4칸 뒤에 표시된다: 만든 잎보다 4칸 뒤까지 훑는다)
    static int[] CafeScan(Climb t, int n)
    {
        var r = new int[n + 12];
        for (int i = 1; i <= n + 8; i++) { t.At(i); if (i >= 5) r[i - 4] = t.At(i - 4).Cafe; }
        return r;
    }

    // ── 같은 씨 = 같은 콤보·카페 ──
    static string CheckComboDeterminism()
    {
        int seed = 0;
        for (int s = 1; s <= 200 && seed == 0; s++) { var p = Play(s, ClimbSkill.Easy); if (p.Cafes > 0 && p.MaxCombo >= 8) seed = s; }
        Expect(seed != 0, "느긋한 봇이 카페에 들르는 씨가 있다");
        var log = Record(seed, ClimbSkill.Easy);
        (Climb, List<(string, int)>) Rep(Func<int, double> dt)
        {
            var t = new Climb(seed);
            var ev = new List<(string, int)>();
            t.OnEvent = e => { if (e != "hop") ev.Add((e, t.Ticks)); };
            int n = 0;
            void To(int tick) { while (t.Ticks < tick && !t.Done) { double d = Math.Min(dt(n++), (tick - t.Ticks) * Climb.Tick); t.Update(d); } }
            foreach (var (tick, dir) in log) { To(tick); if (t.Done) break; t.Press(dir); }
            for (int i = 0; i < 60 * 30 && !t.Done; i++) t.Update(Climb.Tick);
            return (t, ev);
        }
        var (a, ea) = Rep(_ => Climb.Tick);
        var rng = new Random(3);
        foreach (var (name, dt) in new (string, Func<int, double>)[] { ("1/60 다시", _ => Climb.Tick), ("1/30", _ => 1.0 / 30), ("들쭉날쭉", _ => rng.NextDouble() * .045 + .004) })
        {
            var (b, eb) = Rep(dt);
            Expect(a.N == b.N && a.Ticks == b.Ticks && a.Why == b.Why && Math.Abs(a.Bar - b.Bar) < 1e-9 && a.MaxCombo == b.MaxCombo && a.Cafes == b.Cafes && Math.Abs(a.CafeSec - b.CafeSec) < 1e-9 && a.TierSteps.SequenceEqual(b.TierSteps), $"{name}: 끝 잎·시간·막대·콤보·카페가 같다");
            Expect(ea.SequenceEqual(eb), $"{name}: 콤보·카페 사건(끝·단계·드나듦)이 같은 틱에 난다 ({ea.Count}개)");
        }
        Expect(a.Cafes > 0 && ea.Any(e => e.Item1 == "cafe-in") && ea.Any(e => e.Item1 == "cafe-out"), "그 판에 카페 드나듦이 있다");
        // 카페 자리는 씨로만: 조작과 무관 (봇이 플레이하는 동안 본 것 = 처음부터 훑은 것)
        var scan = new Climb(seed);
        var want = new Dictionary<int, int>();
        var sc = CafeScan(scan, 700);
        for (int i = 1; i <= 700; i++) if (sc[i] != 0) want[i] = sc[i];
        var play = new Climb(seed); var bot = new ClimbBotState(seed * 31 + 5);
        var got = new Dictionary<int, int>();
        for (int i = 0; i < 60 * 300 && !play.Done && play.N < 600; i++)
        {
            ClimbBot.Play(play, ClimbSkill.Easy, bot); play.Update(Climb.Tick);
            for (int j = play.N + 1; j <= play.N + 15; j++) if (play.At(j).Cafe != 0) got[j] = play.At(j).Cafe;
        }
        foreach (var (leaf, side) in got) Expect(want.TryGetValue(leaf, out int w) && w == side, $"카페 자리가 조작에 따라 달라진다 (잎 {leaf})");
        return $"씨 {seed}: 1/60·1/30·들쭉날쭉 같은 끝·콤보 {a.MaxCombo}·카페 {a.Cafes}번·사건 {ea.Count}개 · 카페 자리는 조작과 무관 ({got.Count}곳)";
    }

    // ── 없앤 것이 정말 없다: 건너뛰기·친구 ──
    static string CheckRemoved()
    {
        Expect(!Enum.GetNames<ClimbKind>().Contains("Curl"), "발판 종류에 건너뛰기가 없다");
        var events = new HashSet<string>();
        int steps = 0;
        for (int seed = 1; seed <= 60; seed++)
        {
            var t = new Climb(seed);
            t.OnEvent = e => events.Add(e);
            var bot = new ClimbBotState(seed + 9);
            for (int i = 0; i < 60 * 200 && !t.Done && t.N < 700; i++) { ClimbBot.Play(t, seed % 2 == 0 ? ClimbSkill.Good : ClimbSkill.Probe, bot); t.Update(Climb.Tick); }
            Expect(t.Steps == t.N, $"점수 = 키로 오른 칸 수 (씨 {seed}: 오른 {t.Steps} 잎 {t.N})");
            steps += t.Steps;
        }
        Expect(!events.Contains("zone") && !events.Contains("curl"), $"사건 zone·curl 이 안 뜬다 ({string.Join(",", events)})");
        // 조작이 막히는 때는 홀씨(0.5초)·카페 드나들기뿐: 무작위로 눌러 안 먹은 때를 센다
        int ignored = 0, tried = 0;
        for (int seed = 1; seed <= 10; seed++)
        {
            var t = new Climb(seed) { Guard = false };
            var rn = new Random(seed);
            for (int step = 0; step < 20000 && !t.Done && t.N < 500; step++)
            {
                if (rn.Next(10) < 3) Tk(t, rn.Next(1, 8));
                int before = t.Presses; bool block = t.SaveLeft > 0 || t.CafePhase is 1 or 3 || t.CafePhase == 2 && false;
                int dir = rn.Next(8) == 0 ? t.At(t.N + 1).Dir * -1 : t.At(t.N + 1).Dir;
                bool insideInner = t.CafePhase == 2 && dir != t.At(t.N + 1).Dir;
                bool visitedSide = t.CafePhase == 0 && t.At(t.N).Cafe != 0 && t.At(t.N).Visited && dir == t.At(t.N).Cafe;
                if (rn.Next(8) == 0) t.Puff = true;
                t.Press(dir); tried++;
                if (t.Presses == before && !t.Done) { ignored++; Expect(block || insideInner || visitedSide || t.State == Climb.Phase.Fall, "키가 안 먹은 까닭이 홀씨·카페 드나들기 말고 있다"); }
                t.Bar = 1; t.WiltLeft = -1;
            }
        }
        return $"종류에 Curl 없음·60판 {steps}칸 점수 = 오른 칸 수·사건 zone/curl 없음·막힌 키는 홀씨·카페 안뿐 ({ignored}/{tried})";
    }

    // ── 카페 자리 (씨 500 × 1000잎) ──
    static string CheckCafePlace()
    {
        var T = ClimbTune.Now;
        int total = 0, minGap = 999, maxGap = 0, firstMin = 999, firstMax = 0, perSeedMin = 999, gapN = 0; long gapSum = 0;
        for (int seed = 1; seed <= 500; seed++)
        {
            var t = new Climb(seed);
            const int N = 1000;
            var dir = new int[N + 8]; var kind = new ClimbKind[N + 8]; var fog = new bool[N + 8]; var cafe = new int[N + 8];
            for (int i = 1; i < N + 8; i++) { ref var l = ref t.At(i); (dir[i], kind[i], fog[i]) = (l.Dir, l.Kind, l.Fog); if (i >= 5) cafe[i - 4] = t.At(i - 4).Cafe; }
            int last = -999, cnt = 0;
            for (int c = 1; c <= N; c++)
            {
                if (cafe[c] == 0) continue;
                cnt++;
                Expect(c >= T.EasyLeaves && kind[c] == ClimbKind.Leaf, $"처음 20잎·기믹 발판엔 카페가 없다 (씨 {seed}, 잎 {c})");
                Expect(cafe[c] == dir[c] && dir[c - 1] == dir[c] && dir[c + 1] == -dir[c] && dir[c + 2] == -dir[c], $"꺾이는 발판 바깥쪽에만 (씨 {seed}, 잎 {c})");
                Expect(cafe[c] != dir[c + 1], $"카페 쪽이 다음 발판 쪽이 아니다 (씨 {seed}, 잎 {c})");
                for (int j = c - 2; j <= c + 2; j++) Expect(kind[j] != ClimbKind.Wilt, $"시든 잎 둘레엔 카페가 없다 (씨 {seed}, 잎 {c})");
                for (int j = c - 1; j <= c + 4; j++) Expect(!fog[j], $"구름 띠 둘레엔 카페가 없다 (씨 {seed}, 잎 {c})");
                for (int j = c - 3; j <= c + 3; j++) Expect(j % T.Mile != 0, $"큰 꽃 앞뒤 3칸엔 카페가 없다 (씨 {seed}, 잎 {c})");
                if (last < 0) { Expect(c >= T.CafeFrom, $"첫 카페는 {T.CafeFrom}잎부터 (씨 {seed}, 잎 {c})"); firstMin = Math.Min(firstMin, c); firstMax = Math.Max(firstMax, c); }
                else { Expect(c - last >= T.CafeGapLo, $"카페 간격 {c - last} (씨 {seed}, 잎 {c}): {T.CafeGapLo} 이상"); minGap = Math.Min(minGap, c - last); maxGap = Math.Max(maxGap, c - last); gapSum += c - last; gapN++; }
                last = c; total++;
            }
            perSeedMin = Math.Min(perSeedMin, cnt);
        }
        Expect(maxGap <= 600 && perSeedMin >= 3 && gapSum / (double)gapN <= 200, $"카페가 드문드문하지 않다 (평균 간격 {gapSum / (double)gapN:0}, 가장 긴 간격 {maxGap}, 한 판 1000잎에 가장 적은 {perSeedMin}개)");
        // 카페 끄기
        Expect(CafeScan(new Climb(3, ClimbTune.Now with { CafeFrom = 99999 }), 1000).All(x => x == 0), "카페 끄기(CafeFrom)");
        Expect(CafeScan(new Climb(3, ClimbTune.Now with { Gimmicks = false }), 1000).All(x => x == 0), "기믹 끄기는 카페도 끈다");
        Expect(CafeScan(new Climb(3), 1000).Any(x => x != 0), "카페가 켜져 있으면 나온다");
        return $"500씨×1000잎 카페 {total}개 (평균 {total / 500.0:0.0}/판): 꺾이는 발판 바깥쪽뿐·카페 쪽 ≠ 다음 발판 쪽·처음 20잎·기믹·시든 잎·구름 띠·큰 꽃 둘레 없음·첫 {firstMin}~{firstMax}·간격 평균 {gapSum / (double)gapN:0} ({minGap}~{maxGap})";
    }

    // ── 카페 그림 자리: 판 안·앞뒤 5잎 발판과 안 겹침 (6판 종류) ──
    static string CheckCafeGeometry()
    {
        var host = new SceneHost("adult_sweet", 3);
        int checks = 0, worstLeft = 999, worstRight = 999;
        foreach (var (look, th, name) in FxKinds)
        {
            using var v = new ClimbView(host.Sprites, "adult_sweet", look, 3, th);
            for (int seed = 1; seed <= 40; seed++)
            {
                var t = new Climb(seed, null, 0, look == ClimbLook.A4);
                int found = 0;
                for (int c = 20; c < 500 && found < 3; c++)
                {
                    t.At(c + 8);                                   // 카페는 잎을 만들고 4칸 뒤에 표시된다
                    if (t.At(c).Cafe == 0) continue;
                    found++;
                    t.N = c; t.At(c + 25);
                    var cb0 = v.CafeBox(t, c, c, look == ClimbLook.A2 ? ClimbView.TargetX(t, c) : 0);
                    for (int n = c - 2; n <= c; n++)
                    {
                        t.N = n;
                        double vx = look == ClimbLook.A2 ? ClimbView.TargetX(t, n) : 0;
                        var cb = v.CafeBox(t, c, n, vx);
                        Expect(cb.Left >= 0 && cb.Right <= v.CW && cb.Top >= 0 && cb.Bottom <= ClimbView.PH, $"{name} 카페 전체가 판 안 (씨 {seed}, 카페 잎 {c}, 서 있는 잎 {n}): {cb} / {v.CW}×{ClimbView.PH}");
                        worstLeft = Math.Min(worstLeft, cb.Left); worstRight = Math.Min(worstRight, v.CW - cb.Right);
                        for (int j = Math.Max(1, c - 5); j <= c + 5; j++)
                        {
                            if (j == c) continue;
                            var sb = v.StepBox(t, j, n, vx);
                            Expect(!cb.IntersectsWith(sb), $"{name} 카페 상자가 발판 {j} 와 겹침 (씨 {seed}, 카페 잎 {c}): 카페 {cb} 발판 {sb}");
                        }
                        checks++;
                    }
                    t.N = 0;
                }
            }
        }
        // 카페 그림이 발판 그림과 같은 점이 30% 아래 (길 한 칸으로 안 읽힌다: 테두리·흰색은 어디나 있으니 뺀다)
        double worst = 0;
        foreach (var (look, th, name) in FxKinds.Where(k => k.Look == ClimbLook.A2))
        {
            var stepCols = new HashSet<uint>();
            if (ClimbSkin.Make(th) is { } sk) { for (int v = 0; v < sk.Variants; v++) for (int st = 0; st < ClimbSkin.Stages; st++) foreach (uint c0 in sk.Step(v, 1, st).P.P) stepCols.Add(c0); }
            else foreach (var lp in ClimbArt.ZoneLeaf.Append(ClimbArt.Wilt)) foreach (uint c0 in ClimbArt.Leaf(lp, 1).P.P) stepCols.Add(c0);
            stepCols.Add(ClimbArt.StemFill); stepCols.Add(ClimbArt.StemHi);
            var cafe = FxPaint.Cafe(th, true);
            int on = 0, same = 0;
            foreach (uint c0 in cafe.P) if (c0 >> 24 != 0 && c0 != ClimbArt.Line && c0 != ClimbArt.White) { on++; if (stepCols.Contains(c0)) same++; }
            double frac = same / (double)on;
            worst = Math.Max(worst, frac);
            Expect(frac < .30, $"{name} 카페가 발판과 같은 색 점 {frac * 100:0}% (30% 아래)");
        }
        return $"6판 종류 {checks}곳: 카페 전체가 판 안(가장자리 여유 {worstLeft}/{worstRight}칸)·앞뒤 5잎 발판 상자와 안 겹침·발판과 같은 색 점 가장 많은 {worst * 100:0}%";
    }

    // ── 카페 드나들기 (규칙) ──
    static string CheckCafeRules()
    {
        var T = ClimbTune.Now;
        foreach (int d in new[] { 1, -1 })
        {
            var t = CafeStand(d, .2);
            int n0 = t.N;
            t.Puff = true; t.SetCombo(7);
            var ev = new List<string>();
            t.OnEvent = e => ev.Add(e);
            Expect(t.At(t.N + 1).Dir == -d && t.At(t.N).Cafe == d, "카페 발판: 카페는 다음 발판 반대쪽");
            t.Press(d);
            Expect(t.CafePhase == 1 && t.N == n0 && !t.Done && t.Wrongs == 0 && t.Puff && t.Cafes == 1 && t.Combo == 0 && t.MaxCombo == 7 && t.At(t.N).Visited, "카페 쪽 키: 들어간다 (N 그대로·안 떨어짐·홀씨 그대로·콤보 끊김)");
            Expect(ev.Contains("cafe-in") && ev.Contains("combo-end"), "사건 cafe-in·combo-end");
            int pr = t.Presses;
            Tk(t, 10); t.Press(-d); t.Press(d);
            Expect(t.CafePhase == 1 && t.Presses == pr && t.N == n0, "걸어 들어가는 0.4초 동안 키는 안 먹는다");
            double bar0 = t.Bar;
            Tk(t, 16);
            Expect(t.CafePhase == 2, $"0.4초 뒤 안에 앉는다 ({t.CafePhase})");
            Expect(Math.Abs(t.Bar - bar0 - .5 * 6 / 60.0) < 1e-6 || t.Bar > bar0, "안에서 막대가 찬다");
            double b1 = t.Bar; Tk(t, 60);
            Expect(Math.Abs(t.Bar - (b1 + T.CafeFill * 1.0)) < 1e-6, $"안에서 초당 +0.5 ({t.Bar:0.000} / {b1 + T.CafeFill:0.000})");
            t.Press(d);
            Expect(t.CafePhase == 2 && t.Presses == pr, "안쪽 키는 아무 일도 없다");
            Tk(t, 60);
            Expect(t.Bar == 1 && !t.Done, "막대는 1 에서 멈춘다");
            Tk(t, 60 * 40);
            Expect(!t.Done && t.CafePhase == 2 && t.Bar == 1 && t.CafeStay > 40, "머무는 한도·벌이 없다 (40초 넘게 쉬어도 그대로)");
            Expect(t.CafeSec > 40 && t.RunClock > 40, "판 시계·쉰 시간은 안에서도 흐른다");
            t.Press(-d);
            Expect(t.CafePhase == 3 && t.N == n0 && ev.Contains("cafe-out"), "길 쪽 키: 걸어 나온다 (같은 발판)");
            Tk(t, 10); int p3 = t.Presses; t.Press(-d); t.Press(d);
            Expect(t.CafePhase == 3 && t.Presses == p3, "걸어 나오는 0.3초 동안 키는 안 먹는다");
            Tk(t, 9);
            Expect(t.CafePhase == 0 && t.N == n0 && t.GraceLeft > .45, "0.3초 뒤 같은 발판에 선다 (막대 0.5초 안 줌)");
            double bg = t.Bar; Tk(t, 25);
            Expect(t.Bar == bg, "나온 뒤 0.5초는 막대가 안 준다");
            Tk(t, 12);
            Expect(t.Bar < bg, "그 뒤에는 다시 준다");
            int pv = t.Presses; t.Press(d);
            Expect(!t.Done && t.N == n0 && t.Presses == pv && t.Wrongs == 0 && t.CafePhase == 0, "다녀온 카페 쪽 키는 아무 일도 없다 (안 떨어진다)");
            t.Press(-d);
            Expect(t.N == n0 + 1 && !t.Done, "다시 길 쪽 키 → 다음 발판");
            Expect(t.Cafes == 1 && t.CafeSec > 40, "카페 수·쉰 시간 기록");
            // 시간·시든 잎·떨어짐은 안에서 안 일어난다: 막대 0 으로 들어가도 안에서 안 죽는다
            var z = CafeStand(d, .01); z.Press(d); Tk(z, 200);
            Expect(!z.Done && z.Bar > .01, "막대가 거의 없어도 안에서는 안 죽고 찬다");
        }
        // 틀린 쪽을 누르면 카페 발판에서는 떨어지는 대신 들어간다 (카페 쪽 = 다음 발판의 반대쪽): 일반 발판에서는 떨어짐
        var w = Fresh(); Hop(w); w.Press(-w.At(2).Dir);
        Expect(w.Done, "카페 없는 발판에서 틀린 쪽은 떨어진다");
        return "들어감(N 그대로·안 떨어짐·홀씨 그대로)·0.4초/0.3초 키 안 먹음·안쪽 키 무시·길 쪽 키로 같은 발판·다시 길 쪽 → 다음·다녀온 뒤 카페 쪽 키 무시·막대 초당 +0.5·1 에서 멈춤·나온 뒤 0.5초·한도 없음·양쪽(±1)";
    }

    // ── 카페: Esc·다시 칠하기·메모리 (게임) ──
    static string CheckCafeGame()
    {
        var host = new SceneHost("adult_sweet", 3);
        int repaints = 0;
        foreach (var (look, th, name) in FxKinds)
        {
            int d = look == ClimbLook.A2 ? -1 : 1;
            var field = look == ClimbLook.A2 ? new Rectangle(660, 300, 450, 528) : new Rectangle(700, 300, 288, 528);
            // Esc: 들어가는 중·앉아 있는 중·나오는 중 모두 바로 끝 (결과 화면·표정 없음)
            foreach (int phase in new[] { 1, 2, 3 })
            {
                host.Pet.Hush();
                var (g, t) = FxBase(host, look, field, th, CafeDirs(d), d);
                Frames(g, 5);
                t.Press(d);
                if (phase >= 2) { Frames(g, 40); Expect(t.CafePhase == 2, $"{name}: 앉았다"); }
                if (phase == 3) { t.Press(-d); Frames(g, 4); Expect(t.CafePhase == 3, $"{name}: 나오는 중"); }
                if (phase == 1) Frames(g, 4);
                Expect(t.CafePhase == phase, $"{name}: 카페 {phase}단계 ({t.CafePhase})");
                g.Stop(true);
                Expect(!g.Running && !host.Pet.Emoting && host.Pet.Mode == Mode.Wander, $"{name}: 카페 {phase}단계 Esc 는 바로 끝, 표정 없음");
            }
            // 앞 창이 아니면 카페 안에서도 멈춘다
            {
                bool front = true;
                var game = NewGame(host);
                var t0 = CafeStand(d, .3);
                game.Begin(t0, look, field, Session.Fake(field, null, () => front), host.Pet.P.Stage, th);
                Frames(game, 3);
                t0.Press(d); Frames(game, 40);
                double b0 = t0.Bar; int tk = t0.Ticks;
                front = false; Frames(game, 60);
                Expect(t0.Ticks == tk && t0.Bar == b0 && t0.CafePhase == 2, $"{name}: 앞 창이 아니면 카페 안도 멈춘다");
                game.Stop(true);
            }
            // 다시 칠하기: 들어가고 나오는 동안 남는 그림 0
            {
                var (g, t) = FxBase(host, look, field, th, CafeDirs(d), d);
                using var ov = host.Overlay(g);
                int frames = 0;
                var (bad, first) = SceneKit.Repaint(ov, g, field, () =>
                {
                    if (frames == 8) t.Press(d);
                    if (frames == 110) t.Press(-d);
                    if (frames == 140) t.Press(-t.At(t.N + 1).Dir);
                    g.Frame(1 / 60.0); return ++frames < 220;
                }, 220, every: 3);
                Expect(bad == 0, $"{name}: 카페 드나들 때 다시 칠하기 모자람 {bad}번 (처음 {first} 프레임)");
                g.Stop(true);
                repaints++;
            }
        }
        // 메모리: 카페를 드나들며 그려도 프레임마다 새로 잡는 바이트 0
        long worst = 0;
        foreach (var (look, th, name) in FxKinds)
        {
            int d = 1;
            var field = look == ClimbLook.A2 ? new Rectangle(660, 300, 450, 528) : new Rectangle(700, 300, 288, 528);
            var (g, t) = FxBase(host, look, field, th, CafeDirs(d), d);
            using var bmp = new Bitmap(1920, 1080); using var gr = Graphics.FromImage(bmp); using var ov = host.Overlay(g);
            var rr = new List<Rectangle>(64);
            int fn = 0;
            void One()
            {
                if (fn % 150 == 5) { t.PutCafe(d); t.Press(d); }
                if (fn % 150 == 100 && t.CafePhase == 2) t.Press(-d);
                t.Bar = Math.Min(t.Bar, .9);
                g.Frame(1 / 60.0); rr.Clear(); g.Dirty(ov, rr); ov.Render(gr); fn++;
            }
            for (int i = 0; i < 400; i++) One();
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 600; i++) One();
            long per = (GC.GetAllocatedBytesForCurrentThread() - b0) / 600;
            worst = Math.Max(worst, per);
            Expect(t.Cafes >= 6, $"{name}: 카페를 여러 번 드나들었다 ({t.Cafes})");
            g.Stop(true);
        }
        Expect(worst == 0, $"카페 드나들며 그리기가 프레임마다 새로 잡는 메모리 {worst}B");
        return $"6판 종류 Esc 3단계·앞 창 아님 멈춤·다시 칠하기 모자람 0({repaints}판)·카페 그리기 {worst}B/프레임";
    }

    // ── 콤보 효과가 앞길을 안 가린다 · 숫자 쪽지가 판·점수·시간 조각 밖 (6판 종류) ──
    static string CheckComboFx()
    {
        var host = new SceneHost("adult_sweet", 3);
        int frames = 0, tags = 0, brokenTags = 0, changed = 0;
        foreach (var (look, th, name) in FxKinds)
        {
            var field = look == ClimbLook.A2 ? new Rectangle(660, 300, 450, 528) : new Rectangle(700, 300, 288, 528);
            foreach (int seed in new[] { 21, 22, 23 })
            {
                var game = NewGame(host);
                game.auto = true; game.skill = ClimbSkill.Probe;
                var t = new Climb(seed, ClimbTune.Now with { Gimmicks = seed != 22 });
                game.Begin(t, look, field, Session.Fake(field), host.Pet.P.Stage, th);
                var v = game.View!;
                var scoreR = v.ScoreRect(field, 3); var pipsR = v.PipsRect(field);
                bool brokeOnce = false;
                for (int i = 0; i < 60 * 80 && !t.Done && t.N < 330; i++)
                {
                    game.Frame(1 / 60.0);
                    bool brk = t.Combo >= 30 && !brokeOnce && t.N > 60 && i % 7 == 0;
                    if (brk) { t.BreakCombo(); brokeOnce = true; game.Frame(1 / 60.0); }
                    if (i % 3 != 0 && !brk) continue;
                    if (t.Tier < 1 && !(t.ComboEndAt > 0 && t.Clock - t.ComboEndAt < .45)) continue;
                    v.NoFx = false; v.ForceCompose();
                    var on = (uint[])v.CellsNow(t, game.Show).Clone();
                    var blocks = (byte[])v.Blocks.Clone();
                    var tag = v.LastTag;
                    v.NoFx = true; v.ForceCompose();
                    var off = v.CellsNow(t, game.Show);
                    v.NoFx = false; v.ForceCompose();
                    for (int k = 0; k < on.Length; k++)
                    {
                        if (on[k] != off[k]) changed++;
                        if (blocks[k] != 0) Expect(on[k] == off[k], $"{name} 씨 {seed} 잎 {t.N} 콤보 {t.Combo}: 효과가 {(blocks[k] == 1 ? "볼록이 몸" : "다음 네 잎 발판")} 칸 ({k % v.CW},{k / v.CW}) 을 덮음");
                    }
                    if (tag.Width > 0 && (t.Tier >= 1 || brokeOnce))
                    {
                        Expect(tag.Left >= 0 && tag.Right <= v.CW && tag.Top >= 0 && tag.Bottom <= ClimbView.PH, $"{name}: 콤보 숫자 쪽지가 판 밖 {tag}");
                        var sc = new Rectangle((scoreR.X - field.X) / 3, (scoreR.Y - field.Y) / 3, scoreR.Width / 3 + 1, scoreR.Height / 3 + 1);
                        var pc = new Rectangle((pipsR.X - field.X) / 3, (pipsR.Y - field.Y) / 3, pipsR.Width / 3 + 1, pipsR.Height / 3 + 1);
                        Expect(!tag.IntersectsWith(sc) && !tag.IntersectsWith(pc), $"{name}: 콤보 숫자 쪽지 {tag} 가 점수 {sc}·시간 조각 {pc} 에 걸림");
                        tags++; if (brk || t.Tier == 0) brokenTags++;
                    }
                    frames++;
                }
            }
        }
        Expect(frames > 300 && tags > 200 && brokenTags > 0 && changed > 1000, $"효과가 실제로 그려졌다 (장면 {frames}·쪽지 {tags}·끊김 쪽지 {brokenTags}·바뀐 칸 {changed})");
        // 초당 8번으로 60칸: 다음 4잎이 판 안 (시선이 따라온 뒤 목표 자리)
        for (int seed = 1; seed <= 20; seed++)
        {
            var t = new Climb(seed) { Guard = false };
            for (int i = 0; i < 60; i++)
            {
                t.Bar = 1; t.WiltLeft = -1; t.Press(t.At(t.N + 1).Dir); Tk(t, i % 2 == 0 ? 7 : 8);
                double view = ClimbView.TargetX(t, t.N);
                for (int k = 1; k <= 4; k++) { double x = ClimbView.WideA2 / 2.0 + t.At(t.N + k).Col * ClimbView.DX - view; Expect(x >= 12 && x <= ClimbView.WideA2 - 12, $"초당 8번: 다음 {k}잎이 판 안 (씨 {seed}, 잎 {t.N}: {x:0})"); }
            }
        }
        return $"6판 종류 {frames}장면 3단 콤보·끊김: 볼록이·다음 네 잎 발판 칸을 안 덮음·쪽지 {tags}번 판·점수·시간 조각 안 걸림 (효과가 바꾼 칸 {changed}) · 초당 8번 60칸 다음 4잎 판 안";
    }
}
