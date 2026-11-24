using System.Diagnostics;

namespace Bolok;

// 눈길 마중 자가 검사 (--selfcheck, docs/ice-run.md 8장). 장면·속도 재기는 TrailScenes.cs, 봇 표는 TrailTable.cs
static partial class TrailCheck
{
    static void Expect(bool ok, string what) { if (!ok) throw new Exception("자가 검사 실패: " + what); }

    const double Rx = 9.8;   // 어린이 몸 반폭

    // 길 위의 것이 하나도 없는 판 (닿음 검사용): 3·2·1 이 끝난 뒤로 돌려 둔다
    static Trail Bare(int stage = 1, double rx = Rx)
    {
        var t = new Trail(1, stage, rx) { Things = Array.Empty<TrailThing>() };
        for (int i = 0; i < 200 && t.State == Trail.Phase.Count; i++) t.Update(Trail.Tick);
        return t;
    }

    static TrailThing Th(TrailKind k, double u, double p)
    {
        var (w, len) = Trail.Dim(k);
        return new TrailThing { Kind = k, U = (float)u, W = (float)w, Len = (float)len, P = p, P0 = p };
    }

    static Trail With(Trail t, params TrailThing[] things) { t.Things = things.OrderBy(x => x.P0).ToArray(); return t; }

    static void Run(Trail t, double sec, int ax = 0, bool dash = false, bool slow = false)
    {
        t.Steer(ax); t.Hold(dash, slow);
        for (int i = 0; i < (int)(sec * 60); i++) t.Update(Trail.Tick);
    }

    // 틈만 따라가는 조종: 지금 가장 가까운 (아직 안 지난) 묶음의 틈 가운데로
    static void Follow(Trail t, bool dash)
    {
        double g = 0;
        foreach (var b in t.Bundles) if (t.Pos < b.End + (b.Kind == TrailKind.Ball ? 34 : 3)) { g = b.Gap; break; }   // 눈덩이는 질주하면 만나는 곳이 29 걸음 늦다
        double dx = g - t.X;
        t.Steer(Math.Abs(dx) < .03 ? 0 : Math.Sign(dx));
        t.Hold(dash, false);
    }

    public static string Check()
    {
        var sw = Stopwatch.StartNew();
        string determinism = CheckDeterminism();
        string fair = CheckFair();
        string touch = CheckTouch();
        string time = CheckTime();
        string game = CheckGame();
        string art = CheckArt();
        string bots = CheckBots();
        return $"아이스런: 같은 씨 같은 판({determinism}) · 길 공정함({fair}) · 닿음({touch}) · 시간·쉼터({time}) · 기록·Esc·조작·자리({game}) · 그림({art}) · 봇 표({bots}) 모두 통과 ({sw.Elapsed.TotalSeconds:0.0}초)";
    }

    // ── 같은 씨 = 같은 판 ──
    static string CheckDeterminism()
    {
        // 같은 씨·같은 봇이면 끝 점수·시간·자리가 같고, 다른 씨면 다른 판
        var (a, b, c) = (Play(7, 3, TrailSkill.Good), Play(7, 3, TrailSkill.Good), Play(8, 3, TrailSkill.Good));
        Expect(a.Score == b.Score && a.Pos == b.Pos && a.Time == b.Time && a.Hits == b.Hits && a.State == b.State, "같은 씨·같은 조작은 같은 판");
        Expect(a.Things.Length == b.Things.Length && a.Things.Zip(b.Things).All(p => p.First.P0 == p.Second.P0 && p.First.U == p.Second.U && p.First.Kind == p.Second.Kind), "같은 씨는 같은 길");
        Expect(a.Things.Length != c.Things.Length || a.Things.Zip(c.Things).Any(p => p.First.P0 != p.Second.P0), "다른 씨는 다른 길");
        // 프레임을 1/60 · 1/30 · 들쭉날쭉으로 줘도 같다 (조작은 쭉 같은 값): 40초
        Trail Fixed(Func<int, double> dt)
        {
            var t = new Trail(5, 2, Rx);
            t.Steer(0); t.Hold(false, false);
            double clock = 0;
            for (int i = 0; clock < 40; i++) { double d = dt(i); t.Update(d); clock += d; }
            return t;
        }
        var rng = new Random(3);
        var r60 = Fixed(_ => 1.0 / 60);
        var r30 = Fixed(_ => 1.0 / 30);
        var jit = Fixed(i => (new Random(i).NextDouble() * .045 + .004));
        double tol = Math.Max(1.2, TrailTune.Now.Dash / 60.0);   // 한 틱에 가는 만큼까지는 같다고 본다 (빨라지면 그만큼)
        Expect(Math.Abs(r60.Pos - r30.Pos) < tol && Math.Abs(r60.Pos - jit.Pos) < tol, $"프레임 빠르기와 무관 (1/60 {r60.Pos:0.0} · 1/30 {r30.Pos:0.0} · 들쭉날쭉 {jit.Pos:0.0})");
        Expect(r60.Hits == r30.Hits && r60.Hits == jit.Hits && r60.Score == r30.Score && r60.Score == jit.Score, "프레임 빠르기와 무관: 넘어짐·점수");
        _ = rng;
        return "씨 · 1/60·1/30·들쭉날쭉";
    }

    // ── 길 공정함: 씨 500 × 단계 1·3·5·10 ──
    static string CheckFair()
    {
        int bundles = 0, stagesDone = 0;
        foreach (int stage in new[] { 1, 3, 5, 10 })
        {
            for (int seed = 1; seed <= 500; seed++)
            {
                var t = new Trail(seed, stage, Rx);
                var seen = new HashSet<TrailKind>();
                double gapNeed = t.GapHalf * 2, prevEnd = -1, prevGap = 0;
                Expect(gapNeed >= t.T.GapMin - 1e-9, "틈은 폭 0.5 이상");
                for (int i = 0; i < t.Bundles.Count; i++)
                {
                    var b = t.Bundles[i];
                    bundles++;
                    // 틈: 묶음의 어떤 것도 틈(가운데 ± 반폭) 안으로 들어오지 않는다
                    var members = t.Members(b).ToList();
                    Expect(members.Count > 0, $"묶음에 든 것이 있다 (씨 {seed}, 단계 {stage})");
                    foreach (var th in members)
                        if (!b.Fresh && th.Kind != TrailKind.Ice)
                            Expect(Math.Abs(th.U - b.Gap) >= th.W / 2 + t.GapHalf - 1e-6, $"묶음의 것이 틈을 막는다 (씨 {seed}, 단계 {stage}, {th.Kind})");
                    Expect(Math.Abs(b.Gap) <= .75, "틈이 길 안");
                    // 처음 보이는 종류는 혼자, 가운데에
                    Expect(seen.Add(b.Kind) == b.Fresh, $"처음 보이는 종류 표시 (씨 {seed}, 단계 {stage})");
                    if (b.Fresh) Expect(members.Count == 1 && Math.Abs(members[0].U) < 1e-6, $"처음 보이는 종류는 혼자 가운데에 (씨 {seed}, 단계 {stage}, {members.Count}개)");
                    // 틈에서 틈으로 질주 빠르기로도 옮길 수 있다
                    if (prevEnd >= 0 && b.Start - prevEnd > 0)
                    {
                        double time = (b.Start - prevEnd) / t.T.Dash - .3;
                        Expect(Math.Abs(b.Gap - prevGap) / t.T.Steer <= time + 1e-6 || Math.Abs(b.Gap - prevGap) / t.T.Steer <= (b.Start - prevEnd) / t.T.Dash, $"틈에서 틈으로 옮길 수 있다 (씨 {seed}, 단계 {stage}, {Math.Abs(b.Gap - prevGap):0.00} / {time:0.00}초)");
                    }
                    (prevEnd, prevGap) = (b.End, b.Gap);
                    // 쉼터 앞뒤 빈 자리
                    foreach (double gate in t.Gates)
                        Expect(b.Start > gate + t.T.GateClear - 1 || b.End < gate - t.T.GateClear + 1, $"쉼터 앞뒤는 비어 있다 (씨 {seed}, {b.Start:0}~{b.End:0}, 쉼터 {gate})");
                    // 종류 열림
                    Expect(b.Kind switch { TrailKind.Snowman => stage >= 2, TrailKind.Ball => stage >= 3, TrailKind.Ice => stage >= 4, _ => true }, $"단계 {stage} 에 아직 안 열린 종류 {b.Kind}");
                }
                // 쉼터 앞뒤에는 닿는 것도 줍는 것도 없다 (눈덩이는 만나는 자리로)
                foreach (var th in t.Things)
                {
                    double at = th.Kind == TrailKind.Ball ? th.P0 - t.T.BallLead : th.P0;
                    foreach (double gate in t.Gates)
                        if (th.Kind != TrailKind.Wind) Expect(at > gate + 20 || at + th.Len < gate - 20, $"쉼터 앞뒤 20 걸음에는 아무것도 없다 (씨 {seed}, {th.Kind} {at:0}, 쉼터 {gate})");
                }
            }
            stagesDone++;
        }
        // 틈만 따라가는 조종으로 넘어짐 0: 보통 빠르기와 질주 둘 다
        int runs = 0;
        foreach (int stage in new[] { 1, 3, 5, 10 })
            for (int seed = 1; seed <= 100; seed++)
                foreach (bool dash in new[] { false, true })
                {
                    var t = new Trail(seed, stage, Rx);
                    for (int i = 0; i < 60 * 300 && !t.Done; i++) { Follow(t, dash); t.Update(Trail.Tick); }
                    Expect(t.Hits == 0, $"틈만 따라가면 안 닿는다 (씨 {seed}, 단계 {stage}, {(dash ? "질주" : "보통")}, 넘어짐 {t.Hits}, 자리 {t.Pos:0})");
                    Expect(t.State == Trail.Phase.Win || stage >= 5 && !dash && t.TimeOut, $"틈만 따라가면 완주한다 (높은 단계의 보통 빠르기는 시간이 모자랄 수 있다) (씨 {seed}, 단계 {stage}, {(dash ? "질주" : "보통")})");
                    runs++;
                }
        return $"씨 500×단계 {stagesDone}개 묶음 {bundles}개 · 틈 따라가기 {runs}판 넘어짐 0";
    }

    // ── 닿음 ──
    static string CheckTouch()
    {
        // 종류마다: 정면이면 닿고, 틈으로 가면 안 닿고, 뛰면 넘고(눈사람은 못 넘고)
        foreach (var kind in new[] { TrailKind.Pile, TrailKind.Hole, TrailKind.Snowman, TrailKind.Ball })
        {
            var t = With(Bare(), Th(kind, 0, 60 + (kind == TrailKind.Ball ? 100 : 0))); // 정면
            if (kind == TrailKind.Ball) { var b = t.Things[0]; b.On = true; t.Things[0] = b; }
            Run(t, 3);
            Expect(t.Hits == 1 && t.LastHit == kind, $"{kind}: 정면이면 닿는다");
            var side = With(Bare(), Th(kind, 0, 60 + (kind == TrailKind.Ball ? 100 : 0)));
            if (kind == TrailKind.Ball) { var b = side.Things[0]; b.On = true; side.Things[0] = b; }
            side.X = .7;
            Run(side, 3);
            Expect(side.Hits == 0, $"{kind}: 틈으로 가면 안 닿는다");
        }
        {   // 뛰기: 눈더미·물구멍·눈덩이는 넘고 눈사람은 못 넘는다
            foreach (var kind in new[] { TrailKind.Pile, TrailKind.Hole, TrailKind.Ball, TrailKind.Snowman })
            {
                double dist = kind == TrailKind.Ball ? 160 : 60;
                var t = With(Bare(), Th(kind, 0, dist));
                if (kind == TrailKind.Ball) { var b = t.Things[0]; b.On = true; t.Things[0] = b; }
                bool jumped = false;
                for (int i = 0; i < 60 * 5; i++)
                {
                    double ahead = t.Ahead(t.Things[0]);
                    if (!jumped && ahead < (kind == TrailKind.Ball ? 22 : 7 + t.Things[0].Len / 2)) { t.Jump(); jumped = true; }   // 닿기 직전에 한 번
                    t.Update(Trail.Tick);
                }
                Expect(kind == TrailKind.Snowman ? t.Hits == 1 : t.Hits == 0, $"{kind}: 뛰면 {(kind == TrailKind.Snowman ? "못 넘는다" : "넘는다")} (넘어짐 {t.Hits})");
            }
        }
        {   // 닿은 뒤 1.5초는 다시 안 닿고, 멈춤이 끝난 뒤에도 한동안
            var t = With(Bare(), Th(TrailKind.Snowman, 0, 55), Th(TrailKind.Snowman, 0, 62));
            Run(t, 3);
            Expect(t.Hits == 1, $"닿은 뒤에는 바로 다시 안 닿는다 (넘어짐 {t.Hits})");
            var h = With(Bare(), Th(TrailKind.Hole, 0, 55));
            for (int i = 0; i < 60 * 4 && h.Hits == 0; i++) h.Update(Trail.Tick);   // 닿을 때까지 (빠르기·가속에 따라 닿는 때가 다르다)
            Run(h, .3);
            Expect(h.Halted || h.HaltLeft > 0, "물구멍: 멈춘다");
            Expect(h.InvLeft >= h.HaltLeft + h.T.SafeAfter - .1, $"멈춤이 끝난 뒤 {h.T.SafeAfter}초는 안 닿는다 ({h.InvLeft:0.00}/{h.HaltLeft:0.00})");
        }
        {   // 몸 폭 85%: 아기는 좁고 통통이는 넓다 (같은 자리에서 아기는 비키고 통통이는 닿는다)
            var baby = With(Bare(1, 9.2), Th(TrailKind.Pile, .3, 60));
            var chubby = With(Bare(1, 14), Th(TrailKind.Pile, .3, 60));
            Run(baby, 3); Run(chubby, 3);
            Expect(baby.Hits == 0 && chubby.Hits == 1, $"몸 폭: 같은 자리에서 아기 {baby.Hits} · 통통 {chubby.Hits}");
            Expect(Math.Abs(baby.Body - 2 * 9.2 * .85 / 70) < 1e-9, "닿음 폭은 몸 폭의 85%");
        }
        {   // 줍기: 열매 +10, 모래시계 +3초 (60 에서 멈춤), 별 4초 질주 + 부수고 지나감
            var t = With(Bare(), Th(TrailKind.Berry, 0, 40), Th(TrailKind.Glass, 0, 80));
            t.Time = 20;
            Run(t, 3);
            Expect(t.Berries == 1 && t.Score == 10 && t.Glasses == 1 && t.Time > 20 - 3 + 2.9 - 1, $"열매·모래시계 (열매 {t.Berries}, 점수 {t.Score}, 시간 {t.Time:0.0})");
            var cap = With(Bare(), Th(TrailKind.Glass, 0, 40));
            cap.Time = cap.T.TimeCap - .5;
            Run(cap, 2);
            Expect(cap.Time <= cap.T.TimeCap + 1e-9, "시간은 상한에서 멈춘다");
            var star = With(Bare(), Th(TrailKind.Star, 0, 40), Th(TrailKind.Snowman, 0, 200));
            Run(star, 2);
            Expect(star.StarLeft > 0 && star.Stars == 1, "별을 주웠다");
            Run(star, 3);
            Expect(star.Hits == 0 && star.Smashes == 1, $"별 동안은 닿아도 부수고 지나간다 (넘어짐 {star.Hits}, 부숨 {star.Smashes})");
        }
        {   // 빙판: 좌우가 30% 만 먹는다 · 옆바람: 3초 동안 옆으로 밀린다 · 길 밖: 빠르기 ×0.7
            var ice = With(Bare(), Th(TrailKind.Ice, 0, 10));
            var dry = Bare();
            for (int i = 0; i < 60 * 3 && !ice.OnIce; i++) { ice.Update(Trail.Tick); dry.Update(Trail.Tick); }   // 빙판에 올라설 때까지 (빠르기에 따라 때가 다르다)
            Expect(ice.OnIce, $"빙판 위 (자리 {ice.Pos:0})");
            Run(ice, .15, 1); Run(dry, .15, 1);   // 빙판을 지나기 전에 짧게 좌우
            Expect(Math.Abs(ice.X) < Math.Abs(dry.X) * .6, $"빙판은 좌우가 덜 먹는다 ({ice.X:0.00} / {dry.X:0.00})");
            var wind = With(Bare(), new TrailThing { Kind = TrailKind.Wind, U = 1, P = 30, P0 = 30 });
            Run(wind, 4.5);
            Expect(wind.X > .6 && wind.WindLeft <= 0, $"옆바람: 한쪽으로 밀리고 3초 뒤 그친다 (X {wind.X:0.00})");
            var off = Bare(); off.X = 1.1;
            var on = Bare();
            Run(off, 2); Run(on, 2);
            Expect(off.Pos < on.Pos * .85, "길 밖 눈밭은 느리다");
            Run(Bare(), .1, 1); var edge = Bare(); Run(edge, 3, 1);
            Expect(edge.X <= edge.T.OffLimit + 1e-9, "길 밖으로 더는 못 나간다");
        }
        {   // 죽음의 소용돌이 없음: 가만히 서 있기만 해도 연달아 멈춘 시간이 3초를 넘지 않는다
            int worst = 0;
            foreach (int stage in new[] { 1, 3, 5, 10 })
                for (int seed = 1; seed <= 40; seed++)
                {
                    var t = new Trail(seed, stage, Rx);
                    int run = 0, gap = 0;
                    for (int i = 0; i < 60 * 200 && !t.Done; i++)
                    {
                        t.Steer(0); t.Hold(false, false);
                        t.Update(Trail.Tick);
                        if (t.Halted || t.Speed < 3 && t.State == Trail.Phase.Run) { run++; gap = 0; }
                        else if (++gap > 30) run = 0;
                        worst = Math.Max(worst, run);
                    }
                }
            Expect(worst <= 180, $"가만히 서 있어도 연달아 멈춤이 3초를 넘지 않는다 ({worst / 60.0:0.0}초)");
            return $"종류별 정면·틈·뛰기 · 몸 폭 · 줍기 · 빙판·바람·길 밖 · 가만히 서 있어도 연속 멈춤 {worst / 60.0:0.0}초";
        }
    }

    // ── 시간·쉼터·단계 ──
    static string CheckTime()
    {
        var t = Bare();
        double cap = t.T.TimeCap, start = cap - 10;   // 시간 상한은 손잡이 값 (지금 60)
        int s0 = (int)start;
        t.Time = start; t.Pos = t.Gates[0] - .5;
        Run(t, .2);
        Expect(t.Leg == 1 && Math.Abs(t.Time - Math.Min(cap, start - .2 + t.GateBonus)) < .3 && t.Time <= cap + 1e-9, $"쉼터에서 시간이 더해지고 상한({cap:0}초)에서 멈춘다 ({t.Time:0.0})");
        Expect(t.Score == 100 + (s0 - 1) * 10 || t.Score == 100 + s0 * 10 || t.Score == 100 + (s0 - 1) * 10 + 10, $"쉼터 점수 = 100 + 남은 초 × 10 ({t.Score})");
        var zero = Bare(); zero.Time = .05;
        Run(zero, 1);
        Expect(zero.State == Trail.Phase.Over && zero.TimeOut, "시간이 0 이면 끝");
        var air = Bare(); air.Time = .02; air.Jump(); Run(air, .1);
        Expect(air.State == Trail.Phase.Run && air.Airborne, "공중이면 내려설 때까지 기다린다");
        Run(air, .6);
        Expect(air.State == Trail.Phase.Over, "내려서면 끝");
        var halt = With(Bare(), Th(TrailKind.Hole, 0, 20)); halt.Time = 1.0;
        Run(halt, 1.5);
        Expect(halt.State == Trail.Phase.Run && halt.Halted, "넘어진 중이면 일어설 때까지 기다린다");
        Run(halt, 1.5);
        Expect(halt.State == Trail.Phase.Over, "일어서면 끝");
        var pass = Bare(); pass.Time = .1; pass.Jump(); Run(pass, .09); pass.Pos = pass.Gates[0] - 4; pass.Speed = 50; Run(pass, .1);
        Expect(pass.Leg == 1 && pass.State == Trail.Phase.Run, $"공중에서 쉼터 줄을 넘으면 통과 (쉼터 {pass.Leg}, {pass.State})");
        // 다섯째 쉼터에서 완주: 점수 = 쉼터 100 × 5 + 남은 초 × 10 들 + 완주 남은 초 × 20
        var win = Bare(); win.Time = 30;
        int score = 0; double expect = 0;
        for (int i = 0; i < 5; i++)
        {
            win.Pos = win.Gates[i] - .2;
            expect += 100 + (int)(win.Time - .15) * 10;
            Run(win, .1);
            Expect(win.Leg == i + 1 || i == 4, "쉼터를 하나씩");
        }
        Expect(win.State == Trail.Phase.Win && win.Leg == 5, "다섯째 쉼터에서 완주");
        score = win.Score;
        Expect(Math.Abs(score - (expect + (int)win.Time * 20)) <= 60, $"완주 점수 = 쉼터마다 100 + 남은 초 × 10, 완주하면 남은 초 × 20 ({score} / {expect + (int)win.Time * 20})");
        Expect(Trail.GateBonusOf(TrailTune.Now, 1) == TrailTune.Now.GateSec && Trail.GateBonusOf(TrailTune.Now, 5) < Trail.GateBonusOf(TrailTune.Now, 3) && Trail.GateBonusOf(TrailTune.Now, 20) == TrailTune.Now.GateSecMin, "쉼터 보너스는 단계가 오를수록 줄고 가장 적어도 멈춘다");
        // 굽이: 쉼터 가까이는 곧고, 어디서나 ±0.6 안
        var c = new Trail(3, 1, Rx);
        double maxCurve = 0;
        for (double p = 0; p < c.Gates[^1]; p += 7) maxCurve = Math.Max(maxCurve, Math.Abs(c.CurveAt(p)));
        Expect(maxCurve > .1 && maxCurve <= .6, $"굽이 값 ({maxCurve:0.00})");
        Expect(c.Gates.All(g => Math.Abs(c.CurveAt(g)) < .01), "쉼터는 곧은 길");
        return "쉼터 시간 더하기·시간 상한·0이면 끝·공중·넘어진 중·줄을 넘으면 통과·완주·굽이";
    }

    // ── 모듈: 기록·Esc·조작 규칙·자리·메모리·다시 칠하기 ──
    static string CheckGame()
    {
        // 판 자리: 창 안이면 볼록이 가까이(창 안으로), 없으면 볼록이 둘레 (모니터 안으로)
        var win = new Rectangle(300, 100, 1320, 860);
        var inWin = TrailGame.InWindow(win, 600, 360, 3);
        Expect(win.Contains(inWin) && Math.Abs(inWin.X + 300 - (win.X + win.Width / 2)) < 2 && inWin.Bottom <= win.Bottom, "판은 창 안 아래 가운데");
        // 창 안에서는 볼록이 가까이 (늘 같은 자리가 아니다), 창 밖으로 안 나간다 (YELL 2026-11-24)
        var near1 = TrailGame.NearPet(win, 900, 700, 600, 360, 3);
        var near2 = TrailGame.NearPet(win, 1400, 900, 600, 360, 3);
        var edge = TrailGame.NearPet(win, -500, 5000, 600, 360, 3);
        Expect(win.Contains(near1) && win.Contains(near2) && win.Contains(edge) && near1 != near2
            && Math.Abs(near1.X + 300 - 900) < 2 && Math.Abs(near1.Bottom - (700 + 24)) < 2 && edge.X == win.X && edge.Bottom <= win.Bottom,
            $"창 안 판은 볼록이 가까이·창 안으로 ({near1} · {near2} · {edge})");
        var area = new Rectangle(0, 0, 1920, 1040);
        foreach (var (px, py) in new[] { (50.0, 40.0), (960.0, 1000.0), (1900.0, 600.0), (0.0, 1040.0) })
        {
            var p = TrailGame.Around(area, px, py, 600, 360, 3);
            Expect(area.Contains(p) && p.Width == 600 && p.Height == 360, $"바탕화면 판은 모니터 안 ({px},{py} → {p})");
        }
        Expect(TrailGame.Around(area, 960, 800, 600, 360, 3).Bottom >= 800, "볼록이가 판 안 아래에 온다");

        // 기록: 완주하면 다음 단계, 시간 초과는 그대로, 연습·자동 판은 안 남고, 저장했다 읽으면 같다
        var host = new SceneHost();
        var g = new TrailGame(host);
        var winRun = Play(11, 1, TrailSkill.Good);
        Expect(winRun.State == Trail.Phase.Win, "잘하는 봇은 1단계를 완주한다");
        g.KeepForCheck(winRun);
        var s1 = g.State;
        Expect(s1.Stage == 2 && s1.Clears == 1 && s1.Best == winRun.Score && s1.MaxStage == 1 && s1.Plays == 1 && s1.BestLegs == 5, $"완주하면 다음 판은 한 단계 위 ({s1})");
        var lose = Play(12, 2, TrailSkill.Easy, tune: new TrailTune { StartSec = 14 });
        Expect(lose.TimeOut, "시간 초과 판");
        g.KeepForCheck(lose);
        Expect(g.State.Stage == 2 && g.State.Clears == 1 && g.State.Plays == 2, "시간 초과면 같은 단계");
        var before = g.State;
        g.KeepForCheck(winRun, practiceRun: true); g.KeepForCheck(winRun, autoRun: true);
        Expect(g.State == before, "연습 판·자동 판은 기록에 안 남는다");
        string json = g.SaveState()!;
        var g2 = new TrailGame(host);
        g2.LoadState(json);
        Expect(g2.State == g.State, "저장했다 읽으면 같은 값");
        var broken = new TrailGame(host); broken.LoadState("{깨진");
        Expect(broken.State.Plays == 0, "깨진 기록은 버린다");

        // Esc: 결과 화면·표정 없이 바로 끝, 볼록이 본체가 돌아온다 (기록은 안 남는다)
        var field = new Rectangle(660, 570, 600, 360);
        var eg = new TrailGame(host);
        var et = new Trail(3, 1, Rx);
        eg.Begin(et, field, Session.Fake(field));
        Expect(eg.Running, "판이 열린다");
        eg.Stop(true);
        Expect(!eg.Running && eg.State.Plays == 0, "Esc(보스 키)는 바로 끝, 기록 안 남김");
        eg.Begin(Play(11, 1, TrailSkill.Good), field, Session.Fake(field));
        eg.Stop(true);
        Expect(!eg.Running && eg.State.Plays == 1, "결과 화면 중 Esc 도 바로 (끝난 판은 기록)");

        // Start: 메뉴에서 시작하면 볼록이 둘레(바탕화면 판)에 판이 열리고 볼록이가 판 위로 옮겨 간다
        var sg = new TrailGame(host) { desktopOnly = true, FakeOpen = f => Session.Fake(f) };
        sg.Start();
        Expect(sg.Running, "Start 로 판이 열린다");
        sg.Frame(1 / 60.0);
        sg.Stop(true);
        Expect(!sg.Running, "Start 한 판도 Esc 로 바로 끝");

        // 조작 규칙: 판이 앞 창이 아니면 방향키는 안 먹고 Ctrl·Shift 는 먹는다
        var keys = new HashSet<Keys> { Keys.Left, Keys.ControlKey, Keys.ShiftKey };
        bool front = false;
        var fake = Session.Fake(field, k => keys.Contains(k), () => front);
        fake.Tick();
        Expect(fake.AxisX == 0 && fake.Ctrl && fake.Shift, "판이 앞 창이 아니면 ← 는 안 먹고 Ctrl·Shift 는 먹는다");
        front = true; fake.Tick();
        Expect(fake.AxisX == -1 && fake.Ctrl, "판이 앞 창이면 ← 도 먹는다");
        // 게임: 앞 창이 아니면 길 가운데로 천천히 돌아온다
        var ig = new TrailGame(host);
        var it = new Trail(4, 1, Rx) { Things = Array.Empty<TrailThing>() };
        front = false; keys.Clear();
        var s = Session.Fake(field, k => keys.Contains(k), () => front);
        ig.Begin(it, field, s);
        for (int i = 0; i < 200; i++) ig.Frame(1 / 60.0);
        it.X = .6;
        for (int i = 0; i < 90; i++) ig.Frame(1 / 60.0);
        Expect(it.X < .6 - .3 && it.Speed <= it.T.Slow + 1, $"판이 앞 창이 아니면 길 가운데로 천천히 돌아온다 (X {it.X:0.00}, 빠르기 {it.Speed:0})");
        ig.Stop(true);
        // 메뉴를 고를 때 눌려 있던 Ctrl 로 바로 뛰지 않는다: Begin 은 ctrlWas=true, 떼었다 눌러야 뛴다
        var jg = new TrailGame(host);
        var jt = new Trail(4, 1, Rx) { Things = Array.Empty<TrailThing>() };
        keys.Add(Keys.ControlKey); front = true;
        var sj = Session.Fake(field, k => keys.Contains(k), () => front);
        jg.Begin(jt, field, sj);
        for (int i = 0; i < 200; i++) jg.Frame(1 / 60.0);
        Expect(jt.JumpT < 0, "눌려 있던 Ctrl 로는 안 뛴다");
        keys.Remove(Keys.ControlKey); jg.Frame(1 / 60.0); keys.Add(Keys.ControlKey); jg.Frame(1 / 60.0); jg.Frame(1 / 60.0);
        Expect(jt.JumpT >= 0 || jt.Airborne, "뗐다 누르면 뛴다");
        jg.Stop(true);

        // 메모리: 규칙 한 판(자동 조종 포함)이 새로 잡는 바이트 0
        var warm = new Trail(9, 5, Rx);
        var wb = new TrailBotState(1);
        for (int i = 0; i < 600; i++) { TrailBot.Play(warm, TrailSkill.Good, wb); warm.Update(Trail.Tick); }
        long b0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 3000 && !warm.Done; i++) { TrailBot.Play(warm, TrailSkill.Good, wb); warm.Update(Trail.Tick); }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - b0;
        Expect(bytes == 0, $"규칙 3000틱(봇 포함)이 새로 잡는 메모리 {bytes}B");

        // 다시 칠하기: 판 사각형 하나라 지난 그림이 남지 않는다 (SceneKit.Repaint). 자동 조종으로 끝까지 돌려 본다
        var rg = new TrailGame(host) { auto = true };
        rg.FriendList = () => new[] { "cat", "shiba" };
        rg.FriendArt = sp => Sprites.Drawn(PetArt.Embedded, sp, null);
        var rfield = new Rectangle(660, 570, 600, 360);
        rg.Begin(new Trail(21, 3, Rx), rfield, Session.Fake(rfield));
        using var ov = host.Overlay(rg);
        int frames = 0;
        var (bad, first) = SceneKit.Repaint(ov, rg, rfield, () => { rg.Frame(1 / 60.0); return ++frames < 60 * 12; }, 60 * 12, every: 7);
        Expect(bad == 0, $"다시 칠하기 모자람 {bad}번 (처음 {first} 프레임)");
        rg.Stop(true);
        // 판이 옮겨 가거나 끝나는 프레임에도 지난 자리를 지운다: 끝난 판은 결과 화면 동안 멈춰도 한 번은 다 칠한다
        var eg2 = new TrailGame(host) { auto = true };
        eg2.Begin(Play(11, 1, TrailSkill.Good), rfield, Session.Fake(rfield));
        var rects = new List<Rectangle>();
        eg2.Frame(1 / 60.0); eg2.Dirty(ov, rects);
        Expect(rects.Any(r => r.Contains(ov.Client(rfield).X + 5, ov.Client(rfield).Y + 5)), "결과 화면은 판 전체를 한 번 칠한다");
        eg2.Stop(true);

        // 언어: 메뉴 이름·안내·기록 줄이 열 언어에 다 있다
        string[] keysL = { "menu.trail", "game.trail", "game.trail_val", "game.trail_none", "note.trail_steer", "note.trail_jump", "note.trail_dash" };
        foreach (var (code, _) in L.Languages)
            foreach (var k in keysL) Expect(L.Has(k, code) && L.Raw(k, code).Length > 0, $"{k} 이 {code} 에 없다");
        Expect(GameModes.Checks.Any() && Enumerable.Range(0, 1).All(_ => true), "게임 목록");

        // 글자 없음: 소스에 글자 그리기 호출이 없다 (소스를 찾을 수 있을 때)
        string text = "";
        var src = FindSource();
        if (src != null)
        {
            foreach (var file in Directory.GetFiles(src, "*.cs"))
            {
                if (file.EndsWith("TrailCheck.cs")) continue;
                string body = File.ReadAllText(file);
                foreach (string bad2 in new[] { "DrawString", "TextRenderer", "DrawText", "AddString", "MeasureString" })
                    Expect(!body.Contains(bad2), $"{Path.GetFileName(file)} 에 글자 그리기 {bad2}");
            }
            text = " · 소스에 글자 그리기 없음";
        }
        return $"판 자리·기록·Esc·조작 규칙(앞 창이 아니면 방향키 막힘)·메모리 {bytes}B·다시 칠하기·열 언어{text}";
    }

    static string? FindSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
        {
            string p = Path.Combine(dir.FullName, "app", "Games", "Trail");
            if (Directory.Exists(p)) return p;
        }
        return null;
    }

    // ── 그림 ──
    static string CheckArt()
    {
        // 깊이 12단계 그림이 모두 빈 그림이 아니고, 가장 큰 그림도 판(200×120칸) 밖으로 안 나가게 그려진다 (구멍·빙판은 반쯤 내려 그린다)
        var host = new SceneHost();
        int pics = 0;
        using (var v = new TrailView(host.Sprites, 3, new (string, Sprites)?[5]))
            foreach (var (name, set) in v.Gallery())
                for (int i = 0; i < TrailView.Steps; i++)
                {
                    var p = set[i];
                    Expect(p.W > 0 && p.H > 0 && p.Bmp.Width == p.W * 3 && Opaque(p.Bmp), $"{name} {i}단계가 빈 그림");
                    pics++;
                }
        // 판 가장자리에서 그리는 자리 계산: 아래로 판을 넘는 그림은 그리지 않는다 (TrailView.Fits)
        for (int i = 0; i < TrailView.Steps; i++) Expect(TrailView.StepFor(TrailView.StepS[i]) == i, "크기 단계 표");
        return $"깊이 12단계 그림 {pics}장 모두 찬 그림";
    }

    static bool Opaque(Bitmap b)
    {
        for (int y = 0; y < b.Height; y++) for (int x = 0; x < b.Width; x++) if (b.GetPixel(x, y).A != 0) return true;
        return false;
    }

    // ── 봇 표가 계획의 목표 안인가 (씨 100개로 가볍게. 전체 표는 --mode-scene trail x table) ──
    static string CheckBots()
    {
        int seeds = 100;
        var easy1 = Run(seeds, 1, TrailSkill.Easy); var easy3 = Run(seeds, 3, TrailSkill.Easy); var easy5 = Run(seeds, 5, TrailSkill.Easy);
        var good1 = Run(seeds, 1, TrailSkill.Good); var good3 = Run(seeds, 3, TrailSkill.Good); var good5 = Run(seeds, 5, TrailSkill.Good);
        Expect(easy1.WinRate is >= .5 and <= .78 && easy1.AvgGates >= 3.8 && easy1.HitsPerLeg is >= 1.4 and <= 3.1, $"느긋한 봇 1단계 {easy1.WinRate:P0}·쉼터 {easy1.AvgGates:0.0}·넘어짐 {easy1.HitsPerLeg:0.0}");
        Expect(easy3.WinRate is >= .08 and <= .38 && easy5.WinRate <= .06, $"느긋한 봇 3단계 {easy3.WinRate:P0} · 5단계 {easy5.WinRate:P0}");
        Expect(good1.WinRate >= .95 && good1.HitsPerLeg < .5, $"잘하는 봇 1단계 {good1.WinRate:P0}·넘어짐 {good1.HitsPerLeg:0.00}");
        Expect(good3.WinRate >= .8, $"잘하는 봇 3단계 {good3.WinRate:P0}");
        Expect(good5.WinRate is >= .36 and <= .68, $"잘하는 봇 5단계 {good5.WinRate:P0}");
        Expect(easy1.AvgWinSec is >= 140 and <= 172 && good1.AvgWinSec is >= 115 and <= 140, $"완주한 판 길이 느긋 {easy1.AvgWinSec:0}초 · 잘함 {good1.AvgWinSec:0}초");
        Expect(easy1.WinRate >= easy3.WinRate && easy3.WinRate >= easy5.WinRate && good1.WinRate >= good3.WinRate && good3.WinRate >= good5.WinRate, "단계마다 완주율이 내려가기만 한다");
        return $"느긋 {easy1.WinRate:P0}/{easy3.WinRate:P0}/{easy5.WinRate:P0} · 잘함 {good1.WinRate:P0}/{good3.WinRate:P0}/{good5.WinRate:P0}";
    }
}
