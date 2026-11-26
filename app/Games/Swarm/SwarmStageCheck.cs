using System.Text;

namespace Bolok;

// 버그 떼 2차 자가 검사 (docs/swarm-levels.md 2-1·2-2): 기믹(돌진 말벌·양쪽 줄·강철·모든 보스 돌진·보스 둘·겹친 대열)과 보상(다시 뽑기·자석·고르기).
// SwarmGame.Check() 끝에서 부른다. 헬퍼(SpecOn·SpecDummy·SpecRun)는 SwarmSpecCheck.cs 것을 쓴다
sealed partial class SwarmGame
{
    static int FoeSide(Swarm.Foe f) => f.X < TestField.Left ? 0 : f.X > TestField.Right ? 1 : f.Y < TestField.Top ? 2 : f.Y > TestField.Bottom ? 3 : -1;

    // 1·2단계 판이 2차 전과 같다는 지문: 같은 씨 무기 넷 × 두 단계의 (잡은 수, 나온 수, 버틴 시간 ×10, 받은 피해). 2차 코드를 넣기 전 표(table:1·table:2)와 글자까지 같다고 확인한 값
    public static string Fingerprint()
    {
        var sb = new StringBuilder();
        foreach (int stg in new[] { 1, 2 })
            foreach (var id in new[] { Arm.Id.Twin, Arm.Id.Pistols, Arm.Id.Flame, Arm.Id.Bazooka })
            {
                var r = BotGame(id, 1000, stg, null, true, true, -1, false);   // 가만히 있기 막기는 끈다 (2차 전과 같은 판인지 보는 지문)
                sb.Append($"{r.Kills},{r.Born},{(int)(r.Time * 10)},{r.DamageTaken};");
            }
        return sb.ToString();
    }

    // 많이·약하게 (2026-11-26, docs/swarm-feel.md 2장): 벌레 체력·수·보석·닿는 피해·무기 몇 값이 바뀌어 규칙이 새로 정해졌다. 이 값이 그 규칙의 지문이다 (2번 맛 얹기 뒤로는 바뀌면 안 된다: 맛은 규칙을 안 건드린다).
    // 쌍검 둘(첫째·다섯째)은 앞서 밸런스 패치로도 바뀌었다 (2026-11-24 회전 베기는 걸을 때만, 몸이 정말 움직였을 때만 걸은 것으로 센다)
    const string FingerprintWas = "2315,2368,2559,249;2347,2394,2625,312;2897,2900,3000,185;355,394,731,141;2534,2591,2547,357;3233,3240,3000,250;2534,2587,2548,291;3167,3189,3000,549;";

    static string Stage2Check(SwarmGame game, SceneHost host)
    {
        const double tick = Tick;
        var T = TestField;
        Swarm Quiet(int seed, int stage, Arm.Id id = Arm.Id.Pistols) { var s = SpecOn(seed, id, stage); s.SpecialsOn = false; s.Weapon.Range = 0; s.SafeUntil = 1e9; return s; }   // 무기가 과녁을 안 건드리고, 안 맞는다

        // ── 1·2단계는 그대로: 새 기믹이 하나도 안 나오고, 판 지문이 2차 전과 같다 ──
        Expect(Fingerprint() == FingerprintWas, $"1·2단계 판이 2차 전과 같다 (지문): {Fingerprint()}");
        int stray = 0;
        foreach (int stg in new[] { 1, 2 })
            for (int seed = 0; seed < 3; seed++)
            {
                var g12 = new Swarm(T, 3, new Random(seed), Arm.Id.Pistols, stg) { SafeUntil = 1e9, SpecialsOn = false };
                for (int i = 0; i < 60 * 295; i++)
                {
                    g12.Update(tick);
                    foreach (var f in g12.Foes) if (f.Dart || f.Heavy || f.Extra) stray++;
                    if (g12.RushShape is Swarm.Shape.Dart or Swarm.Shape.Pincer || g12.RushShape2 != null) stray++;
                    g12.Foes.RemoveAll(f => f.Kind != Swarm.Kind.Boss);
                }
                stray += g12.LaneCount + g12.DartsBorn;
            }
        Expect(stray == 0, $"2단계까지 돌진·양쪽 줄·강철·둘째 보스·겹침·띠가 없다: {stray}");
        Expect(new Swarm(T, 3, new Random(1), null, 2).ShapesOpen == 2 && new Swarm(T, 3, new Random(1), null, 3).ShapesOpen == 3 && new Swarm(T, 3, new Random(1), null, 4).ShapesOpen == 4, "고를 대열 가짓수: 2단계까지 2 · 3단계 3 · 4단계부터 4");

        // ── 말벌 돌진 ──
        Expect(Swarm.DartN(3) == 5 && Swarm.DartN(5) == 6 && Swarm.DartN(7) == 7 && Swarm.DartN(10) == 9 && Swarm.DartN(12) == 10 && Swarm.DartN(40) == 10, "돌진 마릿수: 3단계 5 · 5단계 6 · 7단계 7 · 10단계 9 · 12단계부터 10");
        var dart = Quiet(61, 3);
        (dart.X, dart.Y) = (T.Left + 12, T.Top + T.Height / 2.0);
        dart.SafeUntil = 0;
        Expect(dart.Wave(Swarm.Shape.Dart, 40, 0, 0, 0) == 40 - 2 * 5, "돌진은 한 마리가 물결 마릿수 2: 남은 몫을 돌려준다");
        var darts = dart.Foes.ToList();
        double dy0 = darts.Max(f => f.Y) - darts.Min(f => f.Y);
        Expect(darts.Count == 5 && darts.All(f => f.Dart && f.Kind == Swarm.Kind.Wasp && f.GoAt == dart.Clock + Swarm.DartWind && Math.Abs(f.X - (T.Left - 14)) < .01 && f.Vx == 1 && f.Vy == 0 && dart.Shaking(f)), "돌진 말벌 5마리가 변 밖 14px 에 줄지어 떤다");
        Expect(dart.LaneCount == 1 && dart.Lanes[0].Side == 0 && Math.Abs(dart.Lanes[0].B - dart.Lanes[0].A - Swarm.DartBand) < .01 && dart.Lanes[0].A >= 0 && dart.Lanes[0].B <= T.Height && Math.Abs(dy0 - Swarm.DartBand * 4 / 5.0) < 1, $"띠 하나 (너비 180px, 변 안, 간격 180/5): {dart.Lanes[0].B - dart.Lanes[0].A}, 폭 {dy0}");
        var home = darts.Select(f => (f.X, f.Y, f.Hp)).ToList();
        foreach (var f in darts) (f.Hp, f.MaxHp) = (Tough, Tough);
        SpecRun(dart, .95);
        Expect(dart.Hp == Swarm.MaxHp && darts.Select(f => (f.X, f.Y)).SequenceEqual(home.Select(h => (h.X, h.Y))) && dart.LaneCount == 1, "1.0초 동안은 볼록이가 변에 붙어 있어도 안 닿고 자리 그대로");
        var kills0 = (dart.Kills, dart.Gems.Count, dart.Pops.Count);
        bool lined = true, straight = true;
        for (int i = 0; i < 60 * 3 && dart.Foes.Count > 0; i++)
        {
            dart.Update(tick);
            if (dart.Clock < darts[0].GoAt) continue;
            var alive = darts.Where(f => !f.Dead && dart.Foes.Contains(f)).ToList();
            if (alive.Count == 0) break;
            lined &= alive.All(f => Math.Abs(f.X - alive[0].X) < 1e-6);
            straight &= alive.All(f => f.Vx == 1 && f.Vy == 0) && alive.Select(f => f.Y).OrderBy(y => y).SequenceEqual(home.Select(h => h.Y).OrderBy(y => y));
        }
        Expect(lined && straight, "떠난 뒤 줄이 그대로 곧게 (맞아도·볼록이가 맞아도 흐트러지지 않는다)");
        Expect(dart.Hp == Swarm.MaxHp - Swarm.DartTouch, $"돌진은 닿으면 12 (한 번만, 1초 안 맞는 동안 나머지는 지나간다): {dart.Hp}");
        SpecRun(dart, .3);   // 띠는 말벌이 다 지나간 뒤 한 박자 더 있다 (Until = 가로지르는 길 + 54px)
        Expect(dart.Foes.Count == 0 && dart.Kills == kills0.Kills && dart.Gems.Count == kills0.Item2 && dart.LaneCount == 0, $"판 밖 40px 을 벗어나면 조용히 사라진다 (잡은 수·보석 없음), 띠도 사라졌다: 벌레 {dart.Foes.Count}, 잡은 {dart.Kills}, 보석 {dart.Gems.Count}, 띠 {dart.LaneCount}, 시계 {dart.Clock:0.00}");
        var dartTop = Quiet(62, 3);
        (dartTop.X, dartTop.Y) = (T.Left + T.Width / 2.0, T.Top + 26);
        dartTop.Wave(Swarm.Shape.Dart, 40, 2, 0, 0);
        Expect(dartTop.Lanes[0].A >= 0 && dartTop.Lanes[0].B <= T.Width && dartTop.Foes.All(f => f.Vx == 0 && f.Vy == 1 && FoeSide(f) == 2) && Math.Abs(dartTop.Lanes[0].B - dartTop.Lanes[0].A - 180) < .01, "위 변: 아래로 내달리고, 띠는 변 안");
        var dartCorner = Quiet(62, 3);
        (dartCorner.X, dartCorner.Y) = (T.Left + 12, T.Top + 26);
        dartCorner.Wave(Swarm.Shape.Dart, 40, 0, 0, 0);
        Expect(dartCorner.Lanes[0].A >= 0 && dartCorner.Foes.All(f => f.Y >= T.Top), "볼록이가 모서리에 있어도 띠는 변 안으로 민다");
        Expect(Quiet(63, 3).Wave(Swarm.Shape.Dart, 6, 1, 0, 0) == 0 && Quiet(63, 3).Wave(Swarm.Shape.Dart, 6, 1, 0, 0) == 0, "몫이 모자라면 마릿수를 줄이고 남기지 않는다");
        var dartSide1 = Quiet(63, 3);
        dartSide1.Wave(Swarm.Shape.Dart, 30, 1, 0, 0);
        SpecRun(dartSide1, 1.2);
        Expect(dartSide1.Foes.All(f => f.Vx == -1 && f.X < T.Right - 14 && FoeSide(f) != 0), "오른쪽 변에서는 왼쪽으로 내달린다");

        // 한 러시에 두 번까지, 잇따라 안 됨, 다섯째(마지막) 물결은 안 됨 · 10단계 겹침의 빈 변 · 마릿수
        int dartWaves = 0, mixedWaves = 0, mixedChecked = 0, minDartRushes = 0;
        foreach (int stg in new[] { 3, 4, 7, 9, 10 })
            for (int seed = 0; seed < 12; seed++)
            {
                var rs = new Swarm(T, 3, new Random(200 + seed), Arm.Id.Pistols, stg) { SafeUntil = 1e9, SpecialsOn = false };
                var shapes = new List<(int Min, int Wave, Swarm.Shape Shape)>();
                int waveNo = 0, lastMin = -1, mixedBad = 0, bornBefore = 0;
                string badWhy = "";
                rs.OnEvent = e =>
                {
                    if (e != "wave") return;
                    int min = Math.Min(4, (int)(rs.Clock / 60));
                    if (min != lastMin) (lastMin, waveNo) = (min, 0);
                    shapes.Add((min, waveNo++, rs.RushShape));
                    if (rs.RushShape2 is { } b)
                    {
                        mixedWaves++;
                        if (stg < 10 || b == rs.RushShape || b == Swarm.Shape.Dart) mixedBad++;
                        else if (rs.RushShape != Swarm.Shape.Dart && rs.Foes.Count > 8)
                        {
                            var ts = rs.Foes.Where(f => !f.Dart && f.Kind != Swarm.Kind.Boss).Select(f => rs.RingAt(f.X, f.Y)).OrderBy(t => t).ToList();
                            double widest = 0;
                            for (int i = 0; i < ts.Count; i++) widest = Math.Max(widest, (i + 1 < ts.Count ? ts[i + 1] : ts[0] + 1) - ts[i]);
                            mixedChecked++;
                            if (widest < .18) { mixedBad++; badWhy += $" [{rs.RushShape}+{b} 변 {rs.RushSide} 빈틈 {widest:0.00} {rs.Foes.Count}마리]"; }
                        }
                    }
                };
                for (int i = 0; i < 60 * 295 && !rs.Done; i++)
                {
                    rs.Update(tick);
                    Expect(rs.Foes.Count <= Swarm.MaxFoes + 4, $"한 화면 벌레는 {Swarm.MaxFoes + 4} 마리 안: {rs.Foes.Count}");
                    rs.Foes.RemoveAll(f => f.Kind != Swarm.Kind.Boss);
                }
                bornBefore = rs.DartsBorn;
                foreach (var grp in shapes.GroupBy(x => x.Min))
                {
                    var seq = grp.OrderBy(x => x.Wave).ToList();
                    int nDart = seq.Count(x => x.Shape == Swarm.Shape.Dart);
                    dartWaves += nDart;
                    minDartRushes += nDart > 0 ? 1 : 0;
                    Expect(stg >= 3 || nDart == 0, "돌진은 3단계부터");
                    Expect(nDart <= 2 && !seq.Where((x, i) => i > 0 && x.Shape == Swarm.Shape.Dart && seq[i - 1].Shape == Swarm.Shape.Dart).Any() && !seq.Any(x => x.Wave == Swarm.Waves - 1 && x.Shape == Swarm.Shape.Dart) && !seq.Any(x => x.Shape == Swarm.Shape.Dart && grp.Key == 4), $"한 러시에 돌진은 둘까지·잇따라 안 됨·마지막 물결 아님·마무리 아님 ({stg}단계 {grp.Key}분): {string.Join("", seq.Select(x => "WRDP"[(int)x.Shape]))}");
                    if (stg < 4) Expect(!seq.Any(x => x.Shape == Swarm.Shape.Pincer), "양쪽 줄은 4단계부터");
                }
                Expect(mixedBad == 0, $"겹친 물결: 10단계부터, 둘째 모양은 다르고 돌진이 아니며 빈 변이 하나 남는다 ({stg}단계 씨 {seed}){badWhy}");
                Expect(stg >= 3 || bornBefore == 0, "돌진 수");
            }
        Expect(dartWaves >= 6 && mixedWaves >= 6 && mixedChecked >= 3, $"돌진 물결 {dartWaves} · 겹친 물결 {mixedWaves}(빈 변 확인 {mixedChecked}) 번이 나왔다");

        // 봇은 곧게 가는 돌진을 읽는다: 띠 한가운데에 선 채 시작해도 1초 안에 띠 밖으로 나온다 (읽지 못하면 줄줄이 맞는다)
        int bandHits = 0;
        for (int seed = 0; seed < 8; seed++)
        {
            var b = SpecOn(300 + seed, Arm.Id.Rifle, 3);
            (b.SpecialsOn, b.BotThrows) = (false, false);
            b.Weapon.Range = 0;
            b.Wave(Swarm.Shape.Dart, 40, seed % 2 == 0 ? 0 : 1, 0, 0);
            for (int i = 0; i < 60 * 3 && b.State == Swarm.Phase.Play; i++) { b.BotPlay(); b.Update(tick); }
            bandHits += Swarm.MaxHp - b.Hp;
        }
        Expect(bandHits <= 8 * Swarm.DartTouch / 2, $"봇은 돌진 띠를 읽고 피한다 (여덟 판 받은 피해 합 {bandHits})");

        // ── 양쪽 줄 ──
        foreach (int pside in new[] { 0, 1, 2, 3 })
        {
            var pin = Quiet(64, 4);
            pin.Wave(Swarm.Shape.Pincer, 41, pside, 0, 0);
            int a = pin.Foes.Count(f => FoeSide(f) == pside), b2 = pin.Foes.Count(f => FoeSide(f) == (pside ^ 1));
            Expect(pin.Foes.Count == 41 && a + b2 == 41 && Math.Abs(a - b2) <= 1 && pin.Foes.All(f => Math.Sqrt((f.X - pin.X) * (f.X - pin.X) + (f.Y - pin.Y) * (f.Y - pin.Y)) >= 150), $"양쪽 줄은 마주 보는 두 변에만 (수 차이 1 이하, 150px 안 없음): 변 {pside} {a}·{b2}");
        }
        var pinMarks = new Swarm(T, 3, new Random(65), Arm.Id.Rifle, 4) { Clock = 20, SafeUntil = 1e9 };
        game.Begin(pinMarks);
        pinMarks.OnEvent = null;
        pinMarks.CallRush(Swarm.Shape.Pincer);
        game.Track();
        var perSide = new int[4];
        foreach (int ms in game.markSides) perSide[ms]++;
        Expect(pinMarks.Warning && perSide[pinMarks.RushSide] == 3 && perSide[pinMarks.RushSide ^ 1] == 3, $"양쪽 줄 예고: 느낌표가 두 변에 셋씩 ({string.Join("·", perSide)}, 변 {pinMarks.RushSide})");
        game.swarm = null;

        // ── 강철 딱정벌레 ──
        Expect(Math.Abs(Swarm.SteelP(5) - .25) < 1e-9 && Math.Abs(Swarm.SteelP(10) - .40) < 1e-9 && Swarm.SteelP(14) == .5 && Swarm.SteelP(30) == .5, "강철 비율: 5단계 25% · 10단계 40% · 14단계부터 50%");
        int beetles4 = 0, steel4 = 0, steelFaint = 0;
        foreach (int stg in new[] { 4, 5, 12 })
        {
            int beetles = 0, steel = 0;
            for (int seed = 0; seed < 14; seed++)
            {
                var sl = new Swarm(T, 3, new Random(400 + seed), Arm.Id.Pistols, stg) { SafeUntil = 1e9, SpecialsOn = false };
                for (int i = 0; i < 60 * 295; i++)
                {
                    sl.Update(tick);
                    foreach (var f in sl.Foes) if (f.Kind == Swarm.Kind.Beetle) { beetles++; if (f.Heavy) steel++; }
                    sl.Foes.Clear();
                }
            }
            if (stg == 4) (beetles4, steel4) = (beetles, steel);
            else steelFaint += Math.Abs(steel / (double)beetles - Swarm.SteelP(stg)) < .05 ? 1 : 0;
            Expect(stg == 4 || beetles >= 400 && Math.Abs(steel / (double)beetles - Swarm.SteelP(stg)) < .05, $"{stg}단계 딱정벌레 {beetles}마리 중 강철 {steel} ({steel * 100.0 / Math.Max(1, beetles):0.0}%) = SteelP {Swarm.SteelP(stg):P0} ±5%p");
        }
        Expect(beetles4 > 100 && steel4 == 0 && steelFaint == 2, $"4단계까지는 강철이 없다 ({steel4}/{beetles4})");
        Swarm.Foe SteelAt(Swarm s, double dx, bool steel, bool elite = false, int hp = Tough)
        {
            var f = s.Add(Swarm.Kind.Beetle, s.X + dx, s.Y);
            if (steel) s.MakeSteel(f);
            if (elite) s.MakeElite(f);
            (f.Hp, f.MaxHp, f.Speed, f.DashAt) = (hp == Tough ? f.Hp : hp, hp == Tough ? f.MaxHp : hp, 0, 1e9);
            return f;
        }
        var plain = new Swarm(T, 3, new Random(70), Arm.Id.Long, 5) { Spawning = false, SafeUntil = 1e9, SpecialsOn = false };
        var plainFoe = plain.Add(Swarm.Kind.Beetle, plain.X + 40, plain.Y);
        var steelFoe = new Swarm(T, 3, new Random(70), Arm.Id.Long, 5) { Spawning = false, SafeUntil = 1e9, SpecialsOn = false };
        var steelOne = steelFoe.MakeSteel(steelFoe.Add(Swarm.Kind.Beetle, steelFoe.X + 40, steelFoe.Y));
        Expect(steelOne.Hp == (int)Math.Round(plainFoe.Hp * 1.5, MidpointRounding.AwayFromZero) && steelOne.Heavy && !plainFoe.Heavy, $"강철 체력은 ×1.5: {plainFoe.Hp} → {steelOne.Hp}");
        (plainFoe.Hp, plainFoe.MaxHp, plainFoe.Speed, steelOne.Hp, steelOne.MaxHp, steelOne.Speed) = (Tough, Tough, 0, Tough, Tough, 0);
        double px0 = plainFoe.X, sx0 = steelOne.X;
        plain.Update(tick);
        steelFoe.Update(tick);
        double pushPlain = plainFoe.X - px0, pushSteel = steelOne.X - sx0;
        Expect(pushPlain > 20 && Math.Abs(pushSteel / pushPlain - Swarm.SteelKnock) < .02, $"같은 타격에 강철은 밀침 ¼: {pushPlain:0.0} → {pushSteel:0.0}");
        Expect(plainFoe.StunUntil > plain.Clock && steelOne.StunUntil < 0, "강철은 멈칫이 없다 (보통은 있다)");
        foreach (bool elite in new[] { false, true })
        {
            var gem = Quiet(71, 5);
            gem.Weapon.Range = 300;
            var f = SteelAt(gem, 120, true, elite, 1);
            SpecRun(gem, .3);
            Expect(gem.Kills == 1 && gem.Gems.Where(m => m.Item == Swarm.Item.Gem).Sum(m => m.Xp) + gem.Xp == (elite ? 10 : 5), $"강철 딱정벌레를 잡으면 보석 {(elite ? 10 : 5)}: {gem.Gems.Sum(m => m.Xp) + gem.Xp}");
        }
        var stamps = Stamps.Of(host.Sprites);
        var beetleArt = stamps.Row("game/beetle", 2);
        var steelArt = SwarmArt.Steel(beetleArt[0], 0, 2);
        bool differs = false;
        for (int y = 0; y < steelArt.Bmp.Height && !differs; y++)
            for (int x = 0; x < steelArt.Bmp.Width; x++)
                if (steelArt.Bmp.GetPixel(x, y) != beetleArt[0].Bmp.GetPixel(x, y)) { differs = true; break; }
        Expect(differs && steelArt.Bmp.Size == beetleArt[0].Bmp.Size && (steelArt.Dx, steelArt.Dy) == (beetleArt[0].Dx, beetleArt[0].Dy), "강철 그림은 보통 딱정벌레와 색이 다르고 크기·자리는 같다");

        // ── 모든 보스 돌진 ──
        foreach (var (stg, tier, wantLow) in new[] { (5, 1, false), (5, 2, false), (6, 1, true), (6, 2, true), (6, 3, false), (6, 4, false) })
        {
            var bs = Quiet(80 + tier, stg);
            var boss = bs.CallBoss(tier);
            double first = boss.DashAt - bs.Clock;
            bool none = boss.DashAt > 1e8;
            Expect(tier >= 3 ? Math.Abs(first - Swarm.DashEvery) < 1e-6 : wantLow ? Math.Abs(first - Swarm.DashEveryLow) < 1e-6 : none, $"{stg}단계 {tier}번 보스 돌진: 5단계까지 1·2번은 없고, 6단계부터 7초마다, 3·4번은 5초 그대로 ({first:0.0})");
        }
        var dash6 = Quiet(85, 6);
        var b6 = dash6.CallBoss(1);
        b6.Speed = 0;
        SpecRun(dash6, 6.9);
        Expect(!b6.Winding, "6단계 1번 보스는 7초 전에는 안 떤다");
        SpecRun(dash6, .3);
        Expect(dash6.Shaking(b6), "7초 뒤 떤다 (그 뒤 내달린다)");
        SpecRun(dash6, 1.2);
        Expect(Math.Abs(b6.DashAt - 14) < .05, $"다음 돌진은 7초 뒤: {b6.DashAt:0.0}");

        // ── 보스 둘 ──
        (int Max, int[] Sides, double ExtraHp, bool ExtraFlag) Window(int stg, double from, int seed)
        {
            var tw = new Swarm(T, 3, new Random(seed), Arm.Id.Pistols, stg) { Clock = from, SafeUntil = 1e9, SpecialsOn = false };
            tw.Weapon.Range = 0;
            int max = 0;
            int[] sides = Array.Empty<int>();
            double extraHp = 0;
            bool flag = false;
            for (int i = 0; i < 60 * 5; i++)
            {
                tw.Update(tick);
                tw.Foes.RemoveAll(f => f.Kind != Swarm.Kind.Boss);
                int n = tw.Foes.Count;
                if (n > max) { max = n; sides = tw.Foes.Select(FoeSide).OrderBy(x => x).ToArray(); extraHp = tw.Foes.Where(f => f.Extra).Select(f => (double)f.Hp).FirstOrDefault(); flag = tw.Foes.Any(f => f.Extra); }
                foreach (var f in tw.Foes) f.Speed = 0;
            }
            return (max, sides, extraHp, flag);
        }
        var w7 = Window(7, 176, 90);
        Expect(w7.Max == 2 && w7.ExtraFlag && Math.Abs(w7.ExtraHp - Math.Round(400 * Swarm.Tune.Of(7).BossX * Swarm.ExtraHp)) <= 1 && (w7.Sides[0] ^ 1) == w7.Sides[1], $"7단계 3:00 보스 둘: 마주 보는 변, 둘째 체력 ×0.6 ({w7.ExtraHp}): 변 {string.Join(",", w7.Sides)}");
        Expect(Window(7, 116, 91).Max == 1, "7단계 2:00 은 보스 하나");
        Expect(Window(9, 116, 92).Max == 2 && Window(7, 56, 93).Max == 1, "9단계부터 2:00 도 둘, 1:00 은 하나");
        Expect(Window(12, 236, 94).Max == 1 && Window(6, 176, 95).Max == 1, "4:00 여왕은 혼자, 6단계 3:00 은 하나");
        var twin = Quiet(96, 7);
        var mainBoss = twin.CallTwin();
        var extra = twin.Foes.First(f => f.Extra);
        Expect(extra.Kind == Swarm.Kind.Boss && extra.Tier == 1 && mainBoss.Tier == 3 && !mainBoss.Extra, "둘째 보스는 1번 보스 모습, 첫째는 3번");
        twin.Weapon.Range = 300;
        (extra.X, extra.Y, extra.Speed, extra.Hp, extra.DashAt) = (twin.X + 60, twin.Y, 0, 1, 1e9);
        mainBoss.Speed = 0;
        (mainBoss.X, mainBoss.Y) = (T.Left - 100, T.Top - 100);
        double firstSec0 = twin.FirstBossSec;
        SpecRun(twin, .5);
        Expect(!twin.Foes.Contains(extra) && twin.BossesKilled == 0 && twin.FirstBossSec == firstSec0 && !twin.Gems.Any(m => m.Pull) && !twin.Gems.Any(m => m.Item == Swarm.Item.Magnet) && twin.Gems.Any(m => m.Item == Swarm.Item.Heart) && twin.Gems.Where(m => m.Item == Swarm.Item.Gem).Sum(m => m.Xp) == 7, $"둘째 보스를 잡아도 보스 수·첫 보스 초·끌어오기·자석은 그대로, 보석 7 + 하트: {twin.BossesKilled}");
        var bw = new Swarm(T, 3, new Random(97), Arm.Id.Pistols, 7) { Clock = 177.5, SafeUntil = 1e9, SpecialsOn = false };
        bool sawTwin = false;
        for (int i = 0; i < 60 && !sawTwin; i++) { bw.Update(tick); sawTwin |= bw.BossTwin; bw.Foes.Clear(); }
        Expect(sawTwin && !new Swarm(T, 3, new Random(97), Arm.Id.Pistols, 6) { Clock = 177.5 }.BossTwin, "보스 둘 예고(세모 둘)는 7단계 3:00 에만");
        var cleared = new Swarm(T, 3, new Random(98), Arm.Id.Pistols, 7) { Clock = Swarm.WinSec - 1, BossesKilled = Swarm.Bosses };
        Expect(cleared.CallTwin() != null, "보스 둘이 있어도 Cleared 는 보스 넷 그대로");

        // ── 보상: 다시 뽑기 · 자석 · 고르기 ──
        var keys = new HashSet<Keys>();
        bool front = true;
        SwarmGame Rewarded(int maxStage, int stage = 1, bool practice = false)
        {
            var g = new SwarmGame(host) { FakeOpen = f => Session.Fake(f, keys.Contains, () => front) };
            (g.maxStage, g.stage) = (maxStage, Math.Max(stage, maxStage + 1));
            if (practice) g.Command(new[] { "swarm", "level", "3" });
            else g.Command(new[] { "swarm", "weapon", "rifle" });
            return g;
        }
        void F(SwarmGame g, int n) { for (int i = 0; i < n; i++) g.Frame(tick); }
        void Tap(SwarmGame g) { keys.Clear(); F(g, 2); keys.Add(Keys.ShiftKey); F(g, 2); keys.Clear(); F(g, 1); }   // 뗐다 눌렀다 뗀다
        keys.Clear();
        var g0 = Rewarded(0);
        var old0 = g0.swarm!;
        F(g0, 30);
        Tap(g0);
        Expect(g0.swarm == old0 && g0.swarm.Weapon.Which == Arm.Id.Rifle, "깬 단계가 없으면 Shift 는 아무 일도 없다");
        g0.Stop(true);
        keys.Add(Keys.ShiftKey);                    // Shift 를 누른 채 시작
        var g2 = Rewarded(2);
        var first2 = g2.swarm!;
        F(g2, 10);
        Expect(g2.swarm == first2 && !g2.swapUsed, "Shift 를 누른 채 시작해도 안 바뀐다");
        keys.Clear();
        F(g2, 30);                                  // 0.67초 + 0.5초
        Tap(g2);                                    // 1.4초쯤: 바뀐다
        var after2 = g2.swarm!;
        Expect(after2 != first2 && after2.Weapon.Which != Arm.Id.Rifle && after2.Clock < .2 && after2.Stage == 3 && !g2.practice && after2.Magnets == 0, "2단계를 깼으면 시작 1.5초 안에 Shift 한 번: 무기가 다른 새 판 (시계 0·단계 그대로)");
        var weapon2 = after2.Weapon.Which;
        F(g2, 10);
        Tap(g2);
        Expect(g2.swarm == after2 && after2.Weapon.Which == weapon2, "한 판에 한 번뿐 (둘째는 무시)");
        keys.Add(Keys.ShiftKey);
        F(g2, 20);
        keys.Clear();
        Expect(g2.swarm == after2, "꾹 눌러도 한 번");
        g2.Stop(true);
        var g2b = Rewarded(2);
        var late2 = g2b.swarm!;
        F(g2b, 96);                                 // 1.6초
        Tap(g2b);
        Expect(g2b.swarm == late2, "1.6초에는 Shift 가 안 먹는다");
        g2b.Stop(true);
        var g2c = Rewarded(2);
        var back2 = g2c.swarm!;
        F(g2c, 20);
        front = false;
        Tap(g2c);
        front = true;
        Expect(g2c.swarm == back2, "판이 앞 창이 아니면 Shift 를 안 읽는다");
        g2c.Stop(true);
        var g6 = Rewarded(6);
        var ids = new List<Arm.Id> { g6.swarm!.Weapon.Which };
        F(g6, 20);
        for (int i = 0; i < 9; i++)                 // 3초 안에만 먹지만 누를 때마다 시계가 0 이 돼 계속 열린다 (횟수 제한 없음)
        {
            F(g6, 60);                              // 1초씩
            Tap(g6);
            ids.Add(g6.swarm!.Weapon.Which);
        }
        Expect(Enumerable.Range(1, ids.Count - 1).All(i => (int)ids[i] == ((int)ids[i - 1] + 1) % Arm.Kinds) && g6.swarm!.Clock < .2, $"6단계를 깼으면 누를 때마다 다음 무기, 횟수 제한 없음: {string.Join("·", ids.Select(Ko))}");
        var cur6 = g6.swarm!;
        F(g6, 190);                                 // 3.1초
        Tap(g6);
        Expect(g6.swarm == cur6, "고르기도 3.1초에는 안 먹는다");
        g6.Stop(true);
        var g4 = Rewarded(4);
        Expect(g4.swarm!.Magnets == 1, "4단계를 깼으면 자석 하나로 시작");
        F(g4, 20);
        Tap(g4);
        Expect(g4.swarm!.Magnets == 1 && g4.swarm.Clock < .2, "바꾼 판도 자석 하나로 시작");
        g4.Stop(true);
        Expect(Rewarded(3).swarm!.Magnets == 0, "3단계까지는 자석이 없다");
        var gp = Rewarded(2, practice: true);
        var oldP = gp.swarm!;
        F(gp, 20);
        Tap(gp);
        Expect(gp.swarm != oldP && gp.swarm!.Stage == 3 && gp.practice && gp.maxStage == 2 && gp.stageBest.Count == 0, "연습 판에서도 똑같이 되고, 바꿔도 단계·기록은 그대로");
        gp.Stop(true);
        var ga = Rewarded(2);
        ga.Command(new[] { "swarm", "auto" });
        var oldA = ga.swarm!;
        F(ga, 20);
        Tap(ga);
        Expect(ga.swarm == oldA, "자동 조종은 Shift 를 안 읽는다");
        ga.Stop(true);
        var gold = new SwarmGame(host) { FakeOpen = f => Session.Fake(f, keys.Contains, () => front) };
        gold.LoadState("{\"Sec\":120,\"Kills\":300}");
        gold.Command(new[] { "swarm", "weapon", "rifle" });
        var oldO = gold.swarm!;
        F(gold, 20);
        Tap(gold);
        Expect(gold.swarm == oldO && gold.maxStage == 0, "옛 기록으로는 Shift 가 아무 일도 없다");
        gold.Stop(true);
        Expect(GuideReroll.Length == 5 && GuideReroll[3] == "note.swarm_reroll" && GuideChoose[3] == "note.swarm_choose" && GuideReroll[^1] == "note.esc", "안내: 다시 뽑기·고르기 줄이 수류탄 줄 다음, Esc 앞");
        foreach (var (code, _) in L.Languages)
            foreach (var key in new[] { "note.swarm_reroll", "note.swarm_choose" })
                Expect(L.Has(key, code) && L.Raw(key, code).Contains("Shift") && L.Raw(key, code).Length > 8, $"{key} 가 {code} 에 있다");
        var ug = new SwarmGame(host);
        (ug.maxStage, ug.practice) = (1, false);
        var win2 = new Swarm(T, 3, new Random(1), Arm.Id.Pistols, 2) { Clock = Swarm.WinSec };
        win2.Update(tick);
        var win2c = new SwarmGame(host) { maxStage = 1 };
        var win6 = new Swarm(T, 3, new Random(1), Arm.Id.Pistols, 6) { Clock = Swarm.WinSec };
        win6.Update(tick);
        win2c.swarm = win2;
        Expect(win2c.Unlocked(win2) == 2 && (win2c.maxStage = 2) == 2 && win2c.Unlocked(win2) == 0 && (win2c.maxStage = 5) == 5 && win2c.Unlocked(win6) == 6 && (win2c.practice = true) && win2c.Unlocked(win6) == 0, "방금 풀린 보상: 처음 2·4·6단계를 이겼을 때만 (연습·이미 깬 단계는 없음)");

        // ── 다시 칠할 자리: 돌진 띠가 뻗고 지나가는 판 · 양쪽 줄 예고 · 둘째 보스 세모 · 강철 · 시작 보상 표시 ──
        void Repaints(string what, Swarm p, double secs, int maxStage, Action<Swarm>? each = null)
        {
            (game.stage, game.maxStage, game.practice, game.auto) = (p.Stage, maxStage, false, false);
            game.swapUsed = false;
            game.Begin(p);
            p.OnEvent = null;
            game.ses = Session.Fake(T);
            using var ov = host.Overlay(game);
            double until = p.Clock + secs;
            var (bad, first) = SceneKit.Repaint(ov, game, T, () =>
            {
                if (p.Done || p.Clock >= until) return false;
                each?.Invoke(p);
                p.BotPlay();
                p.Update(tick);
                if (p.State == Swarm.Phase.Play) p.Hp = Math.Max(p.Hp, 60);
                game.Track();
                return true;
            }, 60 * 30);
            Expect(bad == 0, $"바뀐 자리만 다시 칠해도 화면이 맞다 ({what}): {bad}번 다름 (처음 {first}프레임)");
            game.swarm = null;
        }
        bool called = false;
        Repaints("돌진 띠 (뻗고·지나가고·사라진다)", new Swarm(T, 3, new Random(110), Arm.Id.Rifle, 3) { Clock = 10, SafeUntil = 1e9 }, 6, 2, p => { if (!called) { called = true; p.CallRush(Swarm.Shape.Dart); } });
        called = false;
        Repaints("양쪽 줄 예고와 물결", new Swarm(T, 3, new Random(111), Arm.Id.Flame, 4) { Clock = 20, SafeUntil = 1e9 }, 7, 3, p => { if (!called) { called = true; p.CallRush(Swarm.Shape.Pincer); } });
        Repaints("보스 둘 예고·돌진", new Swarm(T, 3, new Random(112), Arm.Id.Rifle, 7) { Clock = 175, SafeUntil = 1e9 }, 9, 6);
        called = false;
        Repaints("강철 딱정벌레", new Swarm(T, 3, new Random(113), Arm.Id.Long, 8) { Clock = 100, SafeUntil = 1e9 }, 6, 7, p => { if (!called) { called = true; p.CallSteel(false); p.CallSteel(true); } });
        Repaints("다시 뽑기·고르기가 열린 시작", new Swarm(T, 3, new Random(114), Arm.Id.Rifle, 3), 4, 6);
        Repaints("방금 풀린 보상이 있는 이긴 결과", new Swarm(T, 3, new Random(115), Arm.Id.Long, 4) { SafeUntil = 1e9, Clock = Swarm.WinSec - 3, BossesKilled = 3 }, 8, 3);
        Repaints("10단계 겹친 대열", new Swarm(T, 3, new Random(22), Arm.Id.Rifle, 10) { Clock = 100, SafeUntil = 1e9 }, 12, 9);

        return $"2차: 1·2단계 지문 같음, 돌진({dartWaves}번 나옴)·양쪽 줄·강철·보스 돌진·보스 둘·겹침({mixedWaves}번), 봇이 띠를 피함(피해 {bandHits}), 보상 다시 뽑기·자석·고르기·안내 글 열 언어, 다시 칠하기 맞음";
    }
}
