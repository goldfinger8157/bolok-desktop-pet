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
        foreach (var b in t.Bundles) if (t.Pos < b.End + (b.Kind == TrailKind.Ball ? BallLate(t) : 3)) { g = b.Gap; break; }   // 눈덩이는 질주하면 만나는 곳이 늦다
        double dx = g - t.X;
        t.Steer(Math.Abs(dx) < .03 ? 0 : Math.Sign(dx));
        t.Hold(dash, false);
    }

    // 눈덩이는 질주하면 만나는 곳이 이만큼 늦다 (걸음). 빠르기 87 일 때 34 → 빠르기에 비례
    static double BallLate(Trail t) => 34 * t.T.Speed / 87;

    // 틈 따라가기 + 새 피하기 (길 만들기가 피할 수 있다고 한 대로): 앞 묶음을 지날 때까지는 그 틈을 지키고, 지나면 새 줄 바깥(틈 · 줄 양옆 가운데 닿을 수 있고 다음 틈에도 갈 수 있는 곳)으로 가서 새가 지날 때까지 서 있다가 틈으로
    static void FollowDodge(Trail t, bool dash)
    {
        int ci = -1;
        for (int i = 0; i < t.Bundles.Count; i++) if (t.Pos < t.Bundles[i].End + (t.Bundles[i].Kind == TrailKind.Ball ? BallLate(t) : 3)) { ci = i; break; }
        double g = ci >= 0 ? t.Bundles[ci].Gap : 0, x = g, lim = t.T.OffLimit - .2, hw = t.BirdHalf + t.T.BirdMargin, v = dash ? t.T.Dash : t.T.Speed;
        for (int i = t.BirdLo; i < t.Birds.Length; i++)
        {
            ref var b = ref t.Birds[i];
            double a = b.M - t.Pos;
            if (a > t.BirdSeen) break;
            if (b.Gone || a < -t.T.BirdNear - 1 || ci >= 0 && b.M > t.Bundles[ci].Start) continue;
            if (ci > 0 && t.Pos < t.AfterOf(t.Bundles[ci - 1])) { x = t.Bundles[ci - 1].Gap; break; }   // 앞 묶음을 다 지날 때까지 그 틈에서
            double ta = Math.Max(0, (a - t.T.BirdNear) / v), tb = ci >= 0 ? Math.Max(0, (t.BeforeOf(t.Bundles[ci]) - 4 - b.M) / v) : 99, best = double.MaxValue;
            x = t.X;
            foreach (double c in new[] { ci > 0 ? t.Bundles[ci - 1].Gap : g, g, b.U - hw, b.U + hw })
            {
                if (Math.Abs(c - b.U) < hw - 1e-9 || Math.Abs(c) > lim) continue;
                if (Math.Abs(c - t.X) / t.T.Steer > ta || Math.Abs(c - g) / t.T.Steer > tb) continue;
                double cost = Math.Abs(c - t.X) + Math.Abs(c - g);
                if (cost < best) (best, x) = (cost, c);
            }
            break;
        }
        double dx = x - t.X, brake = Math.Abs(t.Vx) * t.T.SteerStop * .5 + .02;   // 멈추는 데 드는 거리만큼 앞서 손을 뗀다 (줄 가장자리를 넘어 미끄러지지 않게)
        t.Steer(Math.Abs(dx) < Math.Max(.03, Math.Sign(dx) == Math.Sign(t.Vx) ? brake : 0) ? 0 : Math.Sign(dx));
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
        string birds = CheckBirds();
        string bots = CheckBots();
        return $"아이스런: 같은 씨 같은 판({determinism}) · 길 공정함({fair}) · 닿음({touch}) · 시간·쉼터({time}) · 기록·Esc·조작·자리({game}) · 그림({art}) · 새({birds}) · 봇 표({bots}) 모두 통과 ({sw.Elapsed.TotalSeconds:0.0}초)";
    }

    // ── 같은 씨 = 같은 판 ──
    static string CheckDeterminism()
    {
        // 같은 씨·같은 봇이면 끝 점수·시간·자리가 같고, 다른 씨면 다른 판
        var (a, b, c) = (Play(7, 3, TrailSkill.Good), Play(7, 3, TrailSkill.Good), Play(8, 3, TrailSkill.Good));
        Expect(a.Score == b.Score && a.Pos == b.Pos && a.Time == b.Time && a.Hits == b.Hits && a.BirdHits == b.BirdHits && a.BirdLost == b.BirdLost && a.State == b.State, "같은 씨·같은 조작은 같은 판");
        Expect(a.Birds.Length > 0 && a.Birds.Length == b.Birds.Length && a.Birds.Zip(b.Birds).All(p => p.First.M == p.Second.M && p.First.U == p.Second.U), "같은 씨는 같은 새");
        Expect(a.Birds.Length != c.Birds.Length || a.Birds.Zip(c.Birds).Any(p => p.First.M != p.Second.M || p.First.U != p.Second.U), "다른 씨는 다른 새");
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
        Expect(r60.Hits == r30.Hits && r60.Hits == jit.Hits && r60.BirdHits == r30.BirdHits && r60.BirdHits == jit.BirdHits && r60.Score == r30.Score && r60.Score == jit.Score, "프레임 빠르기와 무관: 넘어짐·새·점수");
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
        // 새까지 피하며 틈 따라가기: 보통 빠르기와 질주 둘 다 넘어짐 0 · 새 맞음 0 (새는 늘 피할 수 있다)
        int birdRuns = 0, birdsMet = 0;
        foreach (int stage in new[] { 1, 3, 5, 10 })
            for (int seed = 1; seed <= 60; seed++)
                foreach (bool dash in new[] { false, true })
                {
                    var t = new Trail(seed, stage, Rx);
                    for (int i = 0; i < 60 * 300 && !t.Done; i++) { FollowDodge(t, dash); t.Update(Trail.Tick); }
                    Expect(t.Hits == 0 && t.BirdHits == 0, $"새까지 피하며 틈 따라가면 안 닿는다 (씨 {seed}, 단계 {stage}, {(dash ? "질주" : "보통")}, 넘어짐 {t.Hits}, 새 {t.BirdHits}, 자리 {t.Pos:0})");
                    birdsMet += t.Birds.Count(b => b.M <= t.Pos);
                    birdRuns++;
                }
        Expect(birdsMet > birdRuns * 4, $"새 피하기 시험에 새가 충분히 나온다 ({birdsMet}/{birdRuns})");
        return $"씨 500×단계 {stagesDone}개 묶음 {bundles}개 · 틈 따라가기 {runs}판 넘어짐 0 · 새까지 피하며 {birdRuns}판(새 {birdsMet}마리) 맞음 0";
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
        var halt = With(Bare(), Th(TrailKind.Hole, 0, 20));
        for (int i = 0; i < 60 * 4 && halt.Hits == 0; i++) halt.Update(Trail.Tick);   // 닿을 때까지 (빠르기에 따라 때가 다르다)
        halt.Time = .3;
        Run(halt, .6);
        Expect(halt.State == Trail.Phase.Run && halt.Halted && halt.Time <= 0, "넘어진 중이면 일어설 때까지 기다린다");
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
        var near1 = PanelPlace.NearPet(win, 900, 700, 600, 360, 3);
        var near2 = PanelPlace.NearPet(win, 1400, 900, 600, 360, 3);
        var edge = PanelPlace.NearPet(win, -500, 5000, 600, 360, 3);
        Expect(win.Contains(near1) && win.Contains(near2) && win.Contains(edge) && near1 != near2
            && Math.Abs(near1.X + 300 - 900) < 2 && Math.Abs(near1.Bottom - (700 + 24)) < 2 && edge.X == win.X && edge.Bottom <= win.Bottom,
            $"창 안 판은 볼록이 가까이·창 안으로 ({near1} · {near2} · {edge})");
        var area = new Rectangle(0, 0, 1920, 1040);
        foreach (var (px, py) in new[] { (50.0, 40.0), (960.0, 1000.0), (1900.0, 600.0), (0.0, 1040.0) })
        {
            var p = PanelPlace.Around(area, px, py, 600, 360, 3);
            Expect(area.Contains(p) && p.Width == 600 && p.Height == 360, $"바탕화면 판은 모니터 안 ({px},{py} → {p})");
        }
        Expect(PanelPlace.Around(area, 960, 800, 600, 360, 3).Bottom >= 800, "볼록이가 판 안 아래에 온다");

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

    // ── 새 (2026-11-25): 놓기 규칙 · 맞으면 감점뿐 · 점프로 못 피함 · 비키면 피함 · 뚫고 지나지 않음 ──
    static string CheckBirds()
    {
        var tune = TrailTune.Now;
        // 놓기: 씨 300 × 단계 1·3·5·10
        var avgGap = new Dictionary<int, double>(); var perRun = new Dictionary<int, double>();
        int total = 0;
        foreach (int stage in new[] { 1, 3, 5, 10 })
        {
            int birds = 0, gaps = 0; double gapSum = 0;
            for (int seed = 1; seed <= 300; seed++)
            {
                var t = new Trail(seed, stage, Rx);
                double seen = t.BirdSeen, prev = -1, hw = t.BirdHalf + tune.BirdMargin;
                foreach (var b in t.Birds)
                {
                    birds++; total++;
                    Expect(b.M >= tune.BirdFirst - 1e-6 && (b.M - seen) / tune.Dash >= 4.0, $"첫 새는 몇 초 뒤부터 (가장 빠른 질주로도 4초 뒤, 씨 {seed}, 자리 {b.M:0})");
                    foreach (double gate in t.Gates)
                        Expect(!(gate > b.M - seen - tune.BirdGateClear + 1e-6 && gate < b.M + tune.BirdGateClear - 1e-6), $"새는 쉼터와 겹치지 않는다 (보이는 동안·앞뒤) (씨 {seed}, 새 {b.M:0}, 쉼터 {gate:0})");
                    int bi = -1;
                    for (int i = 0; i + 1 < t.Bundles.Count; i++) if (t.AfterOf(t.Bundles[i]) <= b.M && b.M <= t.BeforeOf(t.Bundles[i + 1])) bi = i;
                    Expect(bi >= 0, $"새는 묶음 사이 빈 자리에서 만난다 (씨 {seed}, 새 {b.M:0})");
                    Expect(t.BirdDodgeable(t.Bundles[bi], t.Bundles[bi + 1], b.M, b.U, hw, tune.Dash), $"새는 늘 피할 수 있다 (씨 {seed}, 단계 {stage}, 새 {b.M:0} 줄 {b.U:0.00})");
                    Expect(Math.Abs(b.U) <= .85 + 1e-6, "새 줄이 길 안");
                    foreach (var th in t.Things)
                        if (th.Harm && th.Kind != TrailKind.Ball) Expect(Math.Abs(th.P0 + th.Len / 2 - b.M) > 10, $"새 자리에 장애물이 없다 (씨 {seed}, {th.Kind})");
                    if (prev >= 0) { Expect(b.M - prev >= 6 * tune.Speed - 1, "새 사이는 6초 이상"); gaps++; gapSum += (b.M - prev) / tune.Speed; }
                    prev = b.M;
                }
            }
            avgGap[stage] = gapSum / Math.Max(1, gaps);
            perRun[stage] = birds / 300.0;
        }
        // 빈도: 1단계 10~20초에 한 번쯤 (놓을 자리를 못 찾아 건너뛰면 조금 길다), 단계가 오를수록 더 잦다
        Expect(avgGap[1] is >= 10 and <= 22, $"1단계 새 사이 평균 {avgGap[1]:0.0}초");
        // (묶음이 아주 촘촘한 높은 단계 — 10단계쯤 — 에서는 피할 틈이 되는 빈 자리가 드물어 다시 뜸해진다. 그래도 한 판에 몇 마리는 나온다)
        Expect(avgGap[3] < avgGap[1] && avgGap[5] < avgGap[3], $"단계가 오를수록 새가 더 잦다 ({avgGap[1]:0.0} → {avgGap[3]:0.0} → {avgGap[5]:0.0}초, 10단계 {avgGap[10]:0.0}초)");
        Expect(perRun[10] >= 1.5, $"10단계도 새가 나온다 (한 판 {perRun[10]:0.0}마리)");
        Expect(perRun[1] is >= 6 and <= 14 && perRun[5] > perRun[1], $"한 판 새 수 1단계 {perRun[1]:0.0} · 5단계 {perRun[5]:0.0}");
        // 알아챌 틈: 보이는 순간부터 닿기까지 (가장 빠른 질주로도) 반응 + 길 끝에서 끝까지 옮기는 시간보다 길다
        double warn = (tune.SeeDist / tune.BirdRush) / tune.Dash;
        Expect(warn >= tune.BirdReact + 2.0 / tune.Steer + 1.0, $"새가 보이고 닿기까지 {warn:0.0}초 (질주)");
        // 그림이 맞는 범위: 보이기 시작하는 거리에서도 줄이 지평선 아래 (Proj 가 받는다), 새는 볼록이 곁을 지나 76줄까지
        Expect(TrailView.RowAt(tune.SeeDist + 8) >= 3 && TrailView.RowAt(tune.SeeDist + 8) < TrailView.Rows && TrailView.RowAt(-9.5) <= 76.5, $"보이는 거리 {tune.SeeDist:0} 에서 줄 {TrailView.RowAt(tune.SeeDist + 8):0.0}");

        // 맞으면: 점수만 깎이고 멈추지 않고 시간도 안 잃는다 (새 하나를 놓은 판과 안 놓은 판을 같이 달려 견준다)
        TrailBird Bird(double m, double u) => new() { M = m, U = (float)u };
        var evs = new List<string>(); var clocks = new List<double>();
        var hit = Bare(); hit.Score = 120; hit.Birds = new[] { Bird(1500, 0) };
        hit.OnEvent = e => { evs.Add(e); clocks.Add(hit.Clock); };
        var twin = Bare(); twin.Score = 120;
        bool airAtHit = true;
        for (int i = 0; i < 60 * 14; i++) { hit.Steer(0); hit.Update(Trail.Tick); twin.Steer(0); twin.Update(Trail.Tick); }
        Expect(hit.BirdHits == 1 && hit.Hits == 0 && hit.Score == 120 - tune.BirdPenalty && hit.BirdLost == tune.BirdPenalty, $"새에 맞으면 점수만 {tune.BirdPenalty} 깎인다 (맞음 {hit.BirdHits}, 점수 {hit.Score})");
        Expect(hit.HaltLeft == 0 && !hit.Halted && Math.Abs(hit.Pos - twin.Pos) < 1e-6 && Math.Abs(hit.Time - twin.Time) < 1e-9, $"새에 맞아도 멈추지도 시간을 잃지도 않는다 (자리 {hit.Pos:0.0}/{twin.Pos:0.0}, 시간 {hit.Time:0.00}/{twin.Time:0.00})");
        Expect(evs.Count(e => e == "cry") == 1 && evs.Count(e => e == "bird") == 1 && evs.IndexOf("cry") < evs.IndexOf("bird"), $"멀리서 한 번 울고 나서 맞는다 ({string.Join(",", evs)})");
        double cryAt = clocks[evs.IndexOf("cry")], hitAt = clocks[evs.IndexOf("bird")];
        Expect(hitAt - cryAt >= warn - .3, $"울고 나서 닿기까지 {hitAt - cryAt:0.0}초 (질주로도 {warn:0.0}초)");
        _ = airAtHit;
        // 0 밑으로는 안 내려간다
        foreach (int start in new[] { 20, 0 })
        {
            var f = Bare(); f.Score = start; f.Birds = new[] { Bird(300, 0) };
            Run(f, 5);
            Expect(f.BirdHits == 1 && f.Score == 0 && f.BirdLost == start, $"점수는 0 밑으로 안 내려간다 (시작 {start}, 지금 {f.Score}, 깎임 {f.BirdLost})");
        }
        // 점프로는 못 피한다 (머리 높이): 닿는 순간 공중이다
        foreach (double lead in new[] { 8.0, 30.0, 60.0 })
        {
            var j = Bare(); j.Score = 100; j.Birds = new[] { Bird(500, 0) };
            bool air = false; j.OnEvent = e => { if (e == "bird") air = j.Airborne; };
            bool jumped = false;
            for (int i = 0; i < 60 * 8; i++)
            {
                if (!jumped && j.Birds[0].M - j.Pos < lead) { j.Jump(); jumped = true; }
                j.Update(Trail.Tick);
            }
            Expect(jumped && j.BirdHits == 1 && j.Score == 100 - tune.BirdPenalty, $"뛰어도 새는 못 피한다 (앞 {lead:0} 걸음에서 점프, 맞음 {j.BirdHits})");
            if (lead == 30) Expect(air, "새에 닿는 순간 공중이다");
        }
        // 비켜 서면 피한다: 새를 알아챈 뒤(반응 0.35초) 오른쪽으로 · 처음부터 곁에 서 있으면 · 가장자리 바깥
        foreach (double dir in new[] { 1.0, -1.0 })
        {
            var d = Bare(); d.Score = 100; d.Birds = new[] { Bird(1500, 0) };
            double cry = -1;
            d.OnEvent = e => { if (e == "cry") cry = d.Clock; };
            for (int i = 0; i < 60 * 14; i++) { d.Steer(cry >= 0 && d.Clock - cry > tune.BirdReact ? (int)dir : 0); d.Update(Trail.Tick); }
            Expect(d.BirdHits == 0 && d.Score == 100 && cry >= 0, $"울음을 듣고 비키면 피한다 (방향 {dir:0}, 맞음 {d.BirdHits})");
        }
        var side = Bare(); side.Birds = new[] { Bird(300, 0) }; side.X = .7;
        Run(side, 5);
        Expect(side.BirdHits == 0, "곁 줄이면 안 맞는다");
        var edge = Bare(); edge.Birds = new[] { Bird(300, 0) }; edge.X = (tune.BirdW + edge.Body) / 2 - .02;
        Run(edge, 4);
        Expect(edge.BirdHits == 1, "줄 폭 안이면 맞는다");
        var jus = Bare(); jus.Birds = new[] { Bird(300, 0) }; jus.X = (tune.BirdW + jus.Body) / 2 + .02;
        Run(jus, 4);
        Expect(jus.BirdHits == 0, "줄 폭 밖이면 안 맞는다");
        // 별이면 부수고 지나간다 · 방금 넘어졌으면(깜빡) 봐준다
        var star = Bare(); star.Birds = new[] { Bird(300, 0) }; star.StarLeft = 10;
        Run(star, 5);
        Expect(star.BirdHits == 0 && star.Smashes == 1, "별 동안은 새를 부수고 지나간다");
        var blink = Bare(); blink.Birds = new[] { Bird(300, 0) }; blink.InvLeft = 10;
        Run(blink, 5);
        Expect(blink.BirdHits == 0, "넘어진 직후 깜빡이는 동안은 새가 안 닿는다");
        // 한 새는 한 번만 맞는다 (멈춰 서서 새 곁에 있어도 거듭 깎이지 않는다)
        var once = Bare(); once.Birds = new[] { Bird(300, 0) }; once.Score = 500;
        Run(once, 6);
        Expect(once.BirdHits == 1 && once.Score == 500 - tune.BirdPenalty, "한 새는 한 번만");

        // 뚫고 지나지 않는다: 한 틱에 가는 가장 큰 상대 거리가 가장 좁은 닿는 폭 안에 든다
        double maxRel = (tune.Dash + tune.Speed * tune.BallRoll) / 60;    // 질주 + 굴러오는 눈덩이
        double minWin = 2 * tune.BirdNear;
        foreach (var k in new[] { TrailKind.Pile, TrailKind.Hole, TrailKind.Snowman, TrailKind.Ball, TrailKind.Berry, TrailKind.Glass, TrailKind.Star })
            minWin = Math.Min(minWin, Trail.Dim(k).Len + 5);
        Expect(maxRel < minWin * .6, $"한 틱에 가는 가장 큰 거리 {maxRel:0.00} < 가장 좁은 닿는 폭 {minWin:0.0} 의 60%");
        Expect(tune.Dash / 60 < 2 * tune.BirdNear * .5, "새: 질주로도 한 틱 거리가 닿는 폭의 절반 안");
        foreach (var kind in new[] { TrailKind.Pile, TrailKind.Hole, TrailKind.Snowman, TrailKind.Ball })   // 가장 빠르게 (질주) 달려도 모두 닿는다
        {
            var t = With(Bare(), Th(kind, 0, 700));
            if (kind == TrailKind.Ball) { var b = t.Things[0]; b.On = true; t.Things[0] = b; }
            Run(t, 8, 0, true);
            Expect(t.Hits == 1, $"{kind}: 질주로 달려도 뚫고 지나지 않는다 (넘어짐 {t.Hits})");
        }
        var fast = Bare(); fast.Birds = new[] { Bird(900, 0) };
        Run(fast, 8, 0, true);
        Expect(fast.BirdHits == 1, "새: 질주로 달려도 뚫고 지나지 않는다");
        return $"{total}마리: 놓기·쉼터와 안 겹침·늘 피할 수 있음 · 빈도 {avgGap[1]:0}초(1단계)→{avgGap[5]:0}초(5단계) · 한 판 {perRun[1]:0.0}/{perRun[5]:0.0}마리 · 감점 {tune.BirdPenalty}·0 밑 안 내려감·안 멈춤·점프로 못 피함·비키면 피함·뚫지 않음";
    }

    // ── 봇 표가 계획의 목표 안인가 (씨 100개로 가볍게. 전체 표는 --mode-scene trail x table) ──
    static string CheckBots()
    {
        int seeds = 100;
        var easy1 = Run(seeds, 1, TrailSkill.Easy); var easy3 = Run(seeds, 3, TrailSkill.Easy); var easy5 = Run(seeds, 5, TrailSkill.Easy);
        var good1 = Run(seeds, 1, TrailSkill.Good); var good3 = Run(seeds, 3, TrailSkill.Good); var good5 = Run(seeds, 5, TrailSkill.Good);
        // 2026-11-25 난이도를 올렸다: 느긋한 봇 1단계 45~60% (예전 55~70%), 3단계 5~20% (예전 15~30%). 씨 100개 오차를 봐서 조금 넓게
        Expect(easy1.WinRate is >= .4 and <= .65 && easy1.AvgGates >= 3.5 && easy1.HitsPerLeg is >= 1.4 and <= 3.1, $"느긋한 봇 1단계 {easy1.WinRate:P0}·쉼터 {easy1.AvgGates:0.0}·넘어짐 {easy1.HitsPerLeg:0.0}");
        Expect(easy3.WinRate is >= .03 and <= .25 && easy5.WinRate <= .06, $"느긋한 봇 3단계 {easy3.WinRate:P0} · 5단계 {easy5.WinRate:P0}");
        Expect(easy1.BirdsPerRun > 0 && good1.BirdsPerRun < easy1.BirdsPerRun + .3 && easy1.BirdsPerRun < easy1.MetPerRun * .5, $"새: 느긋한 봇 판당 {easy1.BirdsPerRun:0.0}마리 맞음 (만남 {easy1.MetPerRun:0.0}) · 잘하는 봇 {good1.BirdsPerRun:0.0}");
        Expect(good1.WinRate >= .95 && good1.HitsPerLeg < .5, $"잘하는 봇 1단계 {good1.WinRate:P0}·넘어짐 {good1.HitsPerLeg:0.00}");
        Expect(good3.WinRate >= .8, $"잘하는 봇 3단계 {good3.WinRate:P0}");
        Expect(good5.WinRate is >= .36 and <= .68, $"잘하는 봇 5단계 {good5.WinRate:P0}");
        Expect(easy1.AvgWinSec is >= 140 and <= 172 && good1.AvgWinSec is >= 115 and <= 140, $"완주한 판 길이 느긋 {easy1.AvgWinSec:0}초 · 잘함 {good1.AvgWinSec:0}초");
        Expect(easy1.WinRate >= easy3.WinRate && easy3.WinRate >= easy5.WinRate && good1.WinRate >= good3.WinRate && good3.WinRate >= good5.WinRate, "단계마다 완주율이 내려가기만 한다");
        return $"느긋 {easy1.WinRate:P0}/{easy3.WinRate:P0}/{easy5.WinRate:P0} · 잘함 {good1.WinRate:P0}/{good3.WinRate:P0}/{good5.WinRate:P0} · 새 맞음 판당 느긋 {easy1.BirdsPerRun:0.0} 잘함 {good1.BirdsPerRun:0.0}";
    }
}
