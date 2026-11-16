using System.Text;

namespace Bolok;

// 버그 떼: 화면 없이 그려 보기(--mode-scene swarm), 속도 재기와 무기 표(--mode-bench swarm), 자가 검사(--selfcheck)
sealed partial class SwarmGame
{
    static readonly Rectangle TestField = FieldAt(3);
    static Rectangle FieldAt(int k) => new(960 - 110 * k, 540 - 75 * k, 220 * k, 150 * k); // 1920×1080 모니터 한가운데
    const double Tick = 1 / 60.0;

    // 그 무기가 한창 공격하는 순간인가 (장면을 찍을 때)
    bool Striking(Swarm s) => s.Weapon.Kind switch
    {
        Arm.Sort.Melee => (s.Clock - s.SwingAt) / s.SwingLen is > .4 and < .7,
        Arm.Sort.Bullets => s.Clock - s.FireAt is > .045 and < .068 && s.Shots.Count >= (s.Weapon.Which == Arm.Id.Shotgun ? s.Weapon.Count : 2),
        Arm.Sort.Rocket => s.Clock - s.BoomAt is > .08 and < .2,
        _ => s.FlameOn && flakes.Count > 50,
    };

    // ── 그려 보기 ──
    // 값 없음: 열 장 = 무기 일곱(각자 공격이 한창인 순간, 90초 뒤·레벨 5 이상) + 여왕과 보스 막대·생명 30% 아래 + 자석을 주운 순간(점선 원)과 보스 예고 + 5:00 결과(큰 왕관·반짝이)
    // 값 <무기 이름>: 그 무기 여섯 장 = 시작(머리 위 무기 그림) · 첫 레벨 업(갈래 그림) · 자석을 주운 순간 · 보스 예고 · 갈래가 다 찬 공격 · 끝
    // 값 art: 무기·갈래 그림만 크게 · table: 무기 표만 · check: 버그 떼 자가 검사만
    public static void Scene(string path, int k)
    {
        string? arg = GameModes.SceneArg;
        if (arg is "table" or "check") { Console.OutputEncoding = Encoding.UTF8; L.Use("ko"); Console.WriteLine(arg == "table" ? Table(40) : Check()); return; }
        if (arg == "art") { SwarmArt.Sheet(path, 8); return; }
        var host = new SceneHost(scale: k);
        var game = new SwarmGame(host);
        var field = FieldAt(k);
        bool one = Arm.Find(arg, out var only);
        Swarm s = null!;
        void New(int seed, Arm.Id id, double clock = 0, int levels = 0)
        {
            s = new Swarm(field, k, new Random(seed), id) { Clock = clock };
            for (int i = 0; i < levels; i++) s.LevelUp();
            (s.UpAt, s.LastUp) = (-9, null);
            game.Begin(s);
            s.OnEvent = null;
        }
        if (one) New(5, only);
        using var ov = host.Overlay(game);
        int made = -1;
        SceneKit.Shots(path, ov, Rectangle.Inflate(field, 20, 20), one ? 6 : 10, shot =>
        {
            bool fresh = made != shot;
            made = shot;
            if (one)
            {
                if (fresh && shot == 2) s.Drop(Swarm.Item.Magnet);
                if (fresh && shot == 3) s.SetClock(57.8);
                if (fresh && shot == 4) { for (int i = 0; i < 40; i++) s.LevelUp(); (s.UpAt, s.LastUp) = (-9, null); s.SetClock(200); }
                if (fresh && shot == 5) s.SetClock(298.5);
            }
            else if (fresh)
            {
                if (shot < Arm.Kinds) New(5 + shot, (Arm.Id)shot);
                else if (shot == 7) New(11, Arm.Id.Rifle, 237, 12);
                else if (shot == 8) { New(12, Arm.Id.Shotgun, 57.6, 4); s.Magnets = 2; s.Drop(Swarm.Item.Magnet); }
                else { New(13, Arm.Id.Long, 296, 14); s.BossesKilled = Swarm.Bosses; }
            }
            var (dx, dy) = s.BotSteer();
            s.Steer(dx, dy);
            s.Update(Tick);
            if (s.State == Swarm.Phase.Play) s.Hp = !one && shot == 7 ? Math.Clamp(s.Hp, 12, 26) : Math.Max(s.Hp, 60); // 장면을 찍을 때까지 버티게
            game.Track();
            if (one)
                return shot switch
                {
                    0 => s.Clock >= .8,
                    1 => s.Level > 1 && s.Clock - s.UpAt is > .4 and < 1.2,
                    2 => s.Clock - s.MagnetAt is > .25 and < .45,
                    3 => s.BossWarning && s.Clock - s.BossWarnAt is > .02 and < .12,
                    4 => s.Clock > 212 && game.Striking(s) || s.Clock > 240,
                    _ => Ended(s) && s.EndedFor > 1.2,
                };
            if (shot < Arm.Kinds) return s.Clock >= 90 && s.Level >= 5 && game.Striking(s) || s.Clock >= 150;
            if (shot == 7) return s.Clock >= 244 && Boss(s) is { Tier: Swarm.Bosses } && !HeartHidden(s) || s.Clock >= 260;
            if (shot == 8) return s.Clock - s.MagnetAt is > .2 and < .5 && s.BossWarning && s.Clock - s.BossWarnAt < .15 || s.Clock >= 59.5;
            return Ended(s) && s.EndedFor > 1.2;
        }, 60 * 1800);
    }

    // ── 속도 재기: 같은 씨로 화염방사기(가장 많이 칠하는 무기) 한 판을 자동 조종으로 끝까지, 그리고 무기 표 (--mode-bench swarm) ──
    public static string BenchRun()
    {
        var host = new SceneHost();
        var game = new SwarmGame(host);
        var s = new Swarm(TestField, 3, new Random(5), Arm.Id.Flame);
        game.Begin(s);
        s.OnEvent = null;
        game.ses = Session.Fake(TestField);
        game.ses.Guide("note.swarm_move", "note.swarm_auto", "note.esc");
        using var ov = host.Overlay(game);
        string speed = Bench.Run("swarm", ov, game, s.Field, () => { if (s.Done) return false; var (dx, dy) = s.BotSteer(); s.Steer(dx, dy); return true; }, () => { s.Update(Tick); game.Track(); }, 60 * 330);
        return speed + $" [화염방사기, {s.Time:0}초 버팀]\n" + Table(40);
    }

    // 자동 조종 한 판 (화면 없이 규칙만). 끝난 판을 돌려준다
    static Swarm BotGame(Arm.Id id, int seed, Func<Swarm, bool>? each = null)
    {
        var s = new Swarm(TestField, 3, new Random(seed), id);
        for (int i = 0; i < 60 * (Swarm.WinSec + 10) && s.State == Swarm.Phase.Play; i++)
        {
            var (dx, dy) = s.BotSteer();
            s.Steer(dx, dy);
            s.Update(Tick);
            if (each != null && !each(s)) break;
        }
        return s;
    }

    static readonly string[] KoNames = { "쌍검", "장검", "쌍권총", "산탄총", "돌격소총", "바주카포", "화염방사기" };

    // 무기 표: 무기 7 × 씨 seeds 개를 자동 조종으로 끝까지 (판마다 Swarm·Random 을 따로 쓰므로 나란히 돌려도 된다)
    public static string Table(int seeds)
    {
        var runs = new Swarm[Arm.Kinds * seeds];
        Parallel.For(0, runs.Length, i => runs[i] = BotGame((Arm.Id)(i / seeds), 1000 + i % seeds));
        var sb = new StringBuilder();
        sb.AppendLine($"| 무기 | 5:00 비율 | 1:00 비율 | 평균 초 | 중앙 초 | 평균 레벨 | 잡은 수 | 받은 피해 | 남은 생명 | 잡은 보스 | 첫 보스(초) | 자석 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        double meleeKills = 0, rangedKills = 0, meleeHurt = 0, rangedHurt = 0;
        for (int w = 0; w < Arm.Kinds; w++)
        {
            var mine = runs.Skip(w * seeds).Take(seeds).ToArray();
            var secs = mine.Select(r => r.Time).OrderBy(t => t).ToArray();
            var first = mine.Where(r => r.FirstBossSec >= 0).Select(r => r.FirstBossSec).DefaultIfEmpty(-1).Average();
            double kills = mine.Average(r => r.Kills), hurt = mine.Average(r => r.DamageTaken);
            if (w < 2) (meleeKills, meleeHurt) = (meleeKills + kills / 2, meleeHurt + hurt / 2);
            else (rangedKills, rangedHurt) = (rangedKills + kills / 5, rangedHurt + hurt / 5);
            sb.AppendLine($"| {KoNames[w]} | {mine.Count(r => r.Won) * 100 / seeds}% | {mine.Count(r => r.Time >= 60) * 100 / seeds}% | {secs.Average():0} | {secs[seeds / 2]:0} | {mine.Average(r => r.Level):0.0} | {kills:0} | {hurt:0} | {mine.Average(r => r.Hp):0} | {mine.Average(r => r.BossesKilled):0.0} | {first:0} | {mine.Average(r => r.Magnets):0.0} |");
        }
        sb.Append($"근접/원거리: 잡은 수 {meleeKills / rangedKills:0.00}배, 받은 피해 {meleeHurt / rangedHurt:0.00}배 (씨 {seeds}개씩)");
        return sb.ToString();
    }

    // ── 자가 검사 (화면 없이 규칙만) ──
    static void Expect(bool ok, string what) { if (!ok) throw new Exception("자가 검사 실패: " + what); }

    public static string Check()
    {
        const double tick = Tick;
        void Run(Swarm s, double sec) { for (int i = 0; i < sec * 60 && !s.Done; i++) s.Update(tick); }
        Swarm Empty(int seed, Arm.Id id = Arm.Id.Pistols) => new(TestField, 3, new Random(seed), id) { Spawning = false, ShoeChance = 0, MagnetChance = 0, HeartChance = 0 };
        Swarm.Foe Dummy(Swarm s, Swarm.Kind kind, double dx, double dy, int hp = 9999, int tier = 1) // 제자리에 선 과녁
        {
            var f = s.Add(kind, s.X + dx, s.Y + dy, tier);
            (f.Hp, f.MaxHp, f.Speed, f.DashAt, f.SpawnDue) = (hp, hp, 0, 1e9, 1e9);
            return f;
        }
        var f8 = TestField;
        Expect(SwarmArt.Broken() == null, $"무기 그림 줄 길이·색: {SwarmArt.Broken()}");

        // 벌레는 네 변에서 다 들어와 볼록이에게 걸어오고, 갈수록 많고 빨라진다
        var early = new Swarm(TestField, 3, new Random(1), Arm.Id.Pistols) { SafeUntil = 1e9 };
        var walker = early.Add(Swarm.Kind.Bug);
        double far0 = Math.Abs(walker.X - early.X) + Math.Abs(walker.Y - early.Y);
        Expect(!TestField.Contains((int)walker.X, (int)walker.Y), "벌레는 판 밖에서 나온다");
        Run(early, 1);
        Expect(walker.Dead || Math.Abs(walker.X - early.X) + Math.Abs(walker.Y - early.Y) < far0, "벌레는 볼록이에게 다가온다");
        Run(early, 24);
        Expect(early.FromSide.All(c => c > 0), $"네 변에서 다 들어온다: {string.Join("·", early.FromSide)}");
        var late = new Swarm(TestField, 3, new Random(1), Arm.Id.Pistols) { SafeUntil = 1e9, Clock = 140 };
        Run(late, 25);
        Expect(late.FromSide.Sum() > early.FromSide.Sum() * 2, $"갈수록 많아진다: {early.FromSide.Sum()} → {late.FromSide.Sum()}");
        var lateBug = late.Add(Swarm.Kind.Bug);
        Expect(lateBug.Speed > walker.Speed && lateBug.MaxHp > walker.MaxHp && late.Foes.Any(f => f.Kind != Swarm.Kind.Bug), "갈수록 빠르고 단단해지고 다른 벌레도 섞인다");

        // 무기 뽑기: 씨 200개로 일곱 가지가 다 나오고 한 가지에 몰리지 않는다. 같은 씨면 같은 무기, 정해 주면 그 무기. 다음 판은 지난 판과 다른 무기
        var drawn = new int[Arm.Kinds];
        for (int seed = 0; seed < 200; seed++)
        {
            var a = new Swarm(TestField, 3, new Random(seed)).Weapon.Which;
            drawn[(int)a]++;
            Expect(new Swarm(TestField, 3, new Random(seed)).Weapon.Which == a, "같은 씨면 같은 무기");
            Expect(new Swarm(TestField, 3, new Random(seed), (Arm.Id)(seed % Arm.Kinds)).Weapon.Which == (Arm.Id)(seed % Arm.Kinds), "무기를 정해 주면 그 무기");
        }
        Expect(drawn.All(c => c > 0) && drawn.Max() <= 60, $"무기 일곱 가지가 고르게 뽑힌다: {string.Join("·", drawn)}");
        var again = new Random(3);
        var seenNext = new HashSet<Arm.Id>();
        for (int i = 0; i < 300; i++)
        {
            var last = (Arm.Id)(i % Arm.Kinds);
            var next = PickWeapon(again, last);
            seenNext.Add(next);
            Expect(next != last && (int)next is >= 0 and < Arm.Kinds, "다음 판은 지난 판과 다른 무기");
        }
        Expect(seenNext.Count == Arm.Kinds && Enumerable.Range(0, Arm.Kinds).All(i => Arm.Find(Arm.Name((Arm.Id)i), out var id) && (int)id == i) && !Arm.Find("laser", out _), "무기 이름은 일곱 가지");

        // ── 쌍검: 닿는 거리 안 부채꼴만 벤다, 좌·우 번갈아, 세게 3단계 뒤 넷째 휘두름이 한 바퀴 ──
        var tw = Empty(3, Arm.Id.Twin);
        tw.SafeUntil = 1e9;
        Swarm.Foe a1 = Dummy(tw, Swarm.Kind.Beetle, 30, 0), b1 = Dummy(tw, Swarm.Kind.Beetle, 34, 18), back = Dummy(tw, Swarm.Kind.Beetle, -45, 0), away = Dummy(tw, Swarm.Kind.Beetle, 95, 0);
        tw.Update(tick);
        Expect(tw.SwingAt > 0 && a1.Hp == 9999 - tw.Weapon.Dmg && b1.Hp == a1.Hp, $"쌍검은 부채꼴 안의 벌레를 한꺼번에 벤다: {a1.Hp}·{b1.Hp}");
        Expect(back.Hp == 9999 && away.Hp == 9999 && tw.Hp == Swarm.MaxHp, "등 뒤·먼 벌레는 그대로");
        var hands = new List<int> { tw.SwingHand };
        for (double at = tw.SwingAt; hands.Count < 4;) { tw.Update(tick); if (tw.SwingAt != at) { at = tw.SwingAt; hands.Add(tw.SwingHand); } }
        Expect(hands.SequenceEqual(new[] { 1, -1, 1, -1 }) && !tw.SwingSpin, $"쌍검은 좌·우 손을 번갈아 쓴다: {string.Join(" ", hands)}");
        var spin = Empty(3, Arm.Id.Twin);
        spin.SafeUntil = 1e9;
        for (int i = 0; i < 3; i++) { Expect(!spin.Weapon.Spin, "회전 베기는 세게 3단계부터"); spin.Weapon.Grow(Swarm.Up.Power); }
        Swarm.Foe sf1 = Dummy(spin, Swarm.Kind.Beetle, 25, 0), sb1 = Dummy(spin, Swarm.Kind.Beetle, -26, 0);
        for (int n = 1; n <= 8; n++)
        {
            var (h1, h2) = (sf1.Hp, sb1.Hp);
            for (int was = spin.SwingCount; spin.SwingCount == was;) spin.Update(tick);
            (sf1.X, sb1.X) = (spin.X + 25, spin.X - 26); // 밀려난 과녁을 제자리로
            if (n % 4 == 0) Expect(spin.SwingSpin && spin.SwingArc == Math.PI && h1 - sf1.Hp == spin.Weapon.Dmg * 3 / 2 && h2 - sb1.Hp == h1 - sf1.Hp, $"넷째 휘두름은 한 바퀴 회전 베기 (피해 1.5배): {h1 - sf1.Hp}·{h2 - sb1.Hp}");
            else Expect(!spin.SwingSpin && h1 - sf1.Hp == spin.Weapon.Dmg && h2 == sb1.Hp, $"보통 휘두름은 겨눈 쪽만: {n}번째 {h1 - sf1.Hp}·{h2 - sb1.Hp}");
        }

        // ── 장검: 넓은 부채꼴 안 모두를 한 번에, 멀리 밀어낸다. 등 뒤는 그대로 ──
        var lg = Empty(4, Arm.Id.Long);
        Swarm.Foe l1 = Dummy(lg, Swarm.Kind.Beetle, 40, 0), l2 = Dummy(lg, Swarm.Kind.Beetle, 25, 43), l3 = Dummy(lg, Swarm.Kind.Beetle, 8, 52), l4 = Dummy(lg, Swarm.Kind.Beetle, -50, 0);
        lg.Update(tick);
        Expect(l1.Hp == 9999 - lg.Weapon.Dmg && l2.Hp == l1.Hp && l3.Hp == 9999 && l4.Hp == 9999, $"장검은 140° 안 모두를 한 번에 벤다: {l1.Hp}·{l2.Hp}·{l3.Hp}·{l4.Hp}");
        Expect(l1.X - lg.X >= 40 + lg.Weapon.Knock - .01 && lg.Weapon.Knock >= 20 && l1.StunUntil > lg.Clock && l4.X == lg.X - 50, $"맞은 벌레는 멀리 밀려나 멈칫한다: {l1.X - lg.X:0}");
        var lg2 = Empty(4, Arm.Id.Long);
        var lb = Dummy(lg2, Swarm.Kind.Boss, 0, -60);
        lg2.Update(tick);
        Expect(lb.Hp == 9999 - lg2.Weapon.Dmg && Math.Abs(lg2.Y - 60 - lb.Y - lg2.Weapon.Knock / 4) < .01 && lb.StunUntil < lg2.Clock, $"보스는 밀침 ¼·멈칫 없음: {lg2.Y - 60 - lb.Y:0.0}");

        // ── 쌍권총: 가까운 둘에게 한 발씩 (좌·우로 갈린다), 하나뿐이면 두 발 다 그놈에게 ──
        var gun = Empty(2, Arm.Id.Pistols);
        Dummy(gun, Swarm.Kind.Bug, 300, 0);
        gun.Update(tick);
        Expect(gun.Shots.Count == 0, "멀리 있는 벌레는 쏘지 않는다");
        Swarm.Foe far = Dummy(gun, Swarm.Kind.Beetle, -200, 0), near = Dummy(gun, Swarm.Kind.Beetle, 120, 20);
        gun.Update(tick);
        Expect(gun.Shots.Count == 2 && (gun.Shots[0].Vx > 0) != (gun.Shots[1].Vx > 0) && gun.AimDir == 1, "쌍권총은 가까운 둘을 하나씩 겨눈다");
        Run(gun, .5);
        Expect(near.Hp == 9999 - gun.Weapon.Dmg && far.Hp == near.Hp && gun.Shots.Count == 0, $"총알은 겨눈 벌레만 맞힌다: {near.Hp}·{far.Hp}");
        var solo = Empty(2, Arm.Id.Pistols);
        var lone = Dummy(solo, Swarm.Kind.Beetle, 150, 0);
        solo.Update(tick);
        Expect(solo.Shots.Count == 2 && solo.Shots[0].Vx > 0 && solo.Shots[1].Vx > 0, "하나뿐이면 두 발 다 그쪽으로");
        Run(solo, .5);
        Expect(lone.Hp == 9999 - 2 * solo.Weapon.Dmg, $"하나뿐이면 두 발 다 맞는다: {lone.Hp}");

        // ── 산탄총: 알 다섯이 ±20° 안, 닿는 거리 밖 벌레는 안 맞음 (알은 닿는 거리까지만 날아간다), 코앞이면 다섯 알 다 ──
        var sg = Empty(5, Arm.Id.Shotgun);
        double sgReach = sg.Weapon.Range;
        var sgNear = Dummy(sg, Swarm.Kind.Beetle, sgReach - 40, 0);
        var sgFar = Dummy(sg, Swarm.Kind.Beetle, (sgReach + 40) * Math.Cos(.26), (sgReach + 40) * Math.Sin(.26));
        sg.Update(tick);
        bool spread = true;
        for (int i = 0; i < sg.Shots.Count; i++) spread &= sg.Shots[i].Ammo == Swarm.Ammo.Pellet && Math.Abs(Math.Atan2(sg.Shots[i].Vy, sg.Shots[i].Vx)) <= sg.Weapon.ArcHalf + 1e-6;
        Expect(sg.Shots.Count == 5 && spread && Math.Abs(sg.Weapon.ArcHalf - 20 * Math.PI / 180) < 1e-9, $"산탄총은 알 다섯을 ±20° 안에 뿌린다: {sg.Shots.Count}알");
        Run(sg, .8);
        Expect(sgNear.Hp < 9999 && sgFar.Hp == 9999 && sg.Shots.Count == 0, $"닿는 거리 밖 벌레는 안 맞는다: {sgNear.Hp}·{sgFar.Hp}");
        var sgClose = Empty(5, Arm.Id.Shotgun);
        var point = Dummy(sgClose, Swarm.Kind.Boss, 34, 0);
        sgClose.SafeUntil = 1e9;
        Run(sgClose, .5);
        Expect(point.Hp == 9999 - 5 * sgClose.Weapon.Dmg, $"코앞이면 다섯 알이 다 맞는다: {9999 - point.Hp}");

        // ── 돌격소총: 한 줄 선 둘을 한 발로 (관통 1), 셋째는 안 맞음, 같은 벌레를 한 발로 두 번 치지 않음 ──
        var rf = Empty(6, Arm.Id.Rifle);
        (rf.Weapon.Cool, rf.Weapon.ArcHalf) = (99, 0); // 한 발만, 흔들림 없이
        Swarm.Foe r1 = Dummy(rf, Swarm.Kind.Beetle, 100, 0), r2 = Dummy(rf, Swarm.Kind.Beetle, 140, 0), r3 = Dummy(rf, Swarm.Kind.Beetle, 180, 0);
        Run(rf, 1);
        Expect(rf.Weapon.Pierce == 1 && r1.Hp == 9999 - rf.Weapon.Dmg && r2.Hp == r1.Hp && r3.Hp == 9999 && rf.Shots.Count == 0, $"소총탄은 하나를 뚫고 둘째까지만: {9999 - r1.Hp}·{9999 - r2.Hp}·{9999 - r3.Hp}");
        var rapid = Empty(6, Arm.Id.Rifle);
        var mark = Dummy(rapid, Swarm.Kind.Boss, 150, 0);
        Run(rapid, 2);
        int rounds = (9999 - mark.Hp) / rapid.Weapon.Dmg;
        Expect(rounds >= 2 / rapid.Weapon.Cool - 4 && rounds <= 2 / rapid.Weapon.Cool + 1, $"소총은 끊김 없이 연사한다: 2초에 {rounds}발");

        // ── 바주카포: 한 마리와 다섯 마리 떼가 있으면 떼를 겨눈다. 반지름 안은 다 맞고 밖은 그대로, 볼록이는 안 다친다 ──
        var bz = Empty(7, Arm.Id.Bazooka);
        var single = Dummy(bz, Swarm.Kind.Beetle, -100, 0);
        var pack = new[] { (150, 0), (166, 10), (138, 14), (158, -16), (148, 26) }.Select(p => Dummy(bz, Swarm.Kind.Bug, p.Item1, p.Item2, 10)).ToArray();
        var outside = Dummy(bz, Swarm.Kind.Bug, 245, 0, 10);
        bz.Update(tick);
        Expect(bz.Shots.Count == 1 && bz.Shots[0].Ammo == Swarm.Ammo.Rocket && bz.Shots[0].Vx > 0, "바주카는 벌레가 몰린 쪽을 겨눈다");
        Run(bz, 1);
        Expect(bz.BoomAt > 0 && pack.All(p => p.Dead) && bz.Kills == 5 && !outside.Dead && outside.Hp == 10 && single.Hp == 9999, $"폭발 반지름 안은 다 맞고 밖은 그대로: 잡은 {bz.Kills}");
        var bzClose = Empty(7, Arm.Id.Bazooka);
        var hug = Dummy(bzClose, Swarm.Kind.Beetle, 22, 0);
        Run(bzClose, .3);
        Expect(bzClose.BoomAt > 0 && hug.Hp == 9999 - bzClose.Weapon.Dmg && bzClose.Hp == Swarm.MaxHp && bzClose.DamageTaken == 0, "제 폭발에는 다치지 않는다");

        // ── 화염방사기: 부채꼴 안만 태운다, 불길을 벗어나도 한동안 탄다, 불길 속 벌레가 멈추지 않는다, 겨눔은 천천히 돈다 ──
        var fl = Empty(8, Arm.Id.Flame);
        Swarm.Foe inFire = Dummy(fl, Swarm.Kind.Beetle, 60, 0), side = Dummy(fl, Swarm.Kind.Beetle, 0, 61);
        Run(fl, .5);
        Expect(fl.FlameOn && inFire.Hp <= 9999 - 3 * fl.Weapon.Dmg && side.Hp == 9999 && Math.Abs(fl.AimAngle) < .01, $"불길은 부채꼴 안만 태운다: {9999 - inFire.Hp}·{9999 - side.Hp}");
        fl.Foes.Remove(side);
        inFire.X = fl.X + 500; // 불길 밖으로
        fl.Update(tick);
        int lit = inFire.Hp;
        Run(fl, fl.Weapon.BurnSec + .6);
        int burned = lit - inFire.Hp;
        Expect(!fl.FlameOn && burned >= 3 * fl.Weapon.BurnDmg && burned <= 5 * fl.Weapon.BurnDmg, $"불길을 벗어나도 2초 동안 불붙음 피해: {burned}");
        Run(fl, 1);
        Expect(inFire.Hp == lit - burned, "불은 꺼진다");
        var wade = Empty(8, Arm.Id.Flame);
        wade.SafeUntil = 1e9;
        var walkIn = Dummy(wade, Swarm.Kind.Beetle, 85, 0);
        walkIn.Speed = 36;
        for (int i = 0; i < 60; i++)
        {
            double was = walkIn.X;
            wade.Update(tick);
            Expect(walkIn.X < was - .5, "불길 속 벌레는 멈추지 않고 걸어온다 (밀침·멈칫 없음)");
        }
        Expect(walkIn.Hp < 9999, "걸어오는 동안 탄다");
        var turn = Empty(8, Arm.Id.Flame);
        Dummy(turn, Swarm.Kind.Beetle, -60, 1);
        for (int i = 0; i < 80; i++)
        {
            double was = turn.AimAngle;
            turn.Update(tick);
            Expect(Math.Abs(Math.IEEERemainder(turn.AimAngle - was, 2 * Math.PI)) <= Swarm.FlameTurn * tick + 1e-9, "겨눔은 초당 200° 넘게 돌지 않는다");
            if (i == 20) Expect(Math.Cos(turn.AimAngle) > 0, "0.3초 만에 등 뒤로 돌지 못한다");
        }
        Expect(Math.Cos(turn.AimAngle) < -.99 && turn.AimDir == -1, "1초 남짓이면 등 뒤로 돈다");

        // ── 갈래: 무기마다 레벨을 올리면 세 갈래가 다 차고, 그 뒤엔 생명 +10 만. 값에 끝이 있다 ──
        for (int w = 0; w < Arm.Kinds; w++)
        {
            var grow = Empty(20 + w, (Arm.Id)w);
            var arm = grow.Weapon;
            var (dmg0, cool0) = (arm.Dmg, arm.Cool);
            var seen = new HashSet<Swarm.Up>();
            int steps = arm.Cap(Swarm.Up.Fast) + arm.Cap(Swarm.Up.Wide) + arm.Cap(Swarm.Up.Power);
            for (int i = 0; i < steps; i++)
            {
                int before = arm.Ups.Sum();
                grow.Gems.Add(new Swarm.Gem { X = grow.X, Y = grow.Y, Xp = grow.Need });
                grow.Update(tick);
                seen.Add(grow.LastUp!.Value);
                Expect(arm.Ups.Sum() == before + 1 && arm.Ups.Max() - arm.Ups.Zip(new[] { Swarm.Up.Fast, Swarm.Up.Wide, Swarm.Up.Power }, (n, u) => arm.Full(u) ? int.MaxValue : n).Min() <= 1, $"레벨이 오르면 덜 키운 갈래가 자란다: {KoNames[w]} {string.Join("·", arm.Ups)}");
            }
            Expect(steps is >= 12 and <= 15 && arm.Maxed && seen.Count == 3 && grow.Level == steps + 1, $"세 갈래가 다 찬다: {KoNames[w]} {string.Join("·", arm.Ups)}");
            Expect(arm.Dmg > dmg0 && arm.Cool < cool0 * .75 && arm.Cool > cool0 * .5, $"세지고 빨라진다: {KoNames[w]} 피해 {dmg0}→{arm.Dmg}, 쿨 {cool0:0.00}→{arm.Cool:0.000}");
            var (dmg1, cool1, range1, arc1, count1, pierce1, blast1) = (arm.Dmg, arm.Cool, arm.Range, arm.ArcHalf, arm.Count, arm.Pierce, arm.Blast);
            grow.Hp = 55;
            for (int i = 0; i < 6; i++)
            {
                int hp = grow.Hp;
                grow.LevelUp();
                Expect(grow.LastUp == Swarm.Up.Heal && grow.Hp == Math.Min(Swarm.MaxHp, hp + 10), "갈래가 다 찬 뒤엔 생명 +10 만");
            }
            Expect(!arm.Grow(Swarm.Up.Fast) && !arm.Grow(Swarm.Up.Wide) && !arm.Grow(Swarm.Up.Power) && (dmg1, cool1, range1, arc1, count1, pierce1, blast1) == (arm.Dmg, arm.Cool, arm.Range, arm.ArcHalf, arm.Count, arm.Pierce, arm.Blast), "세지는 데는 끝이 있다");
            Expect((Arm.Id)w switch
            {
                Arm.Id.Twin => arm.Spin && arm.Range > 60,
                Arm.Id.Long => arm.ArcHalf == Math.PI,
                Arm.Id.Pistols => arm.Count == 4 && arm.Pierce == 1,
                Arm.Id.Shotgun => arm.Count == 9,
                Arm.Id.Rifle => arm.Pierce == 4,
                Arm.Id.Bazooka => arm.Blast >= 75,
                _ => arm.Range >= 140 && arm.BurnSec > 3.5,
            }, $"다 큰 {KoNames[w]}의 모양");
        }
        var weak = Empty(30);
        int heals = 0;
        for (int i = 0; i < 12; i++)
        {
            weak.Hp = 40;
            weak.LevelUp();
            if (weak.LastUp == Swarm.Up.Heal) { heals++; Expect(weak.Hp == 60, "생명이 절반 아래면 레벨 업으로 +20"); }
        }
        Expect(heals is >= 2 and <= 10 && !weak.Weapon.Maxed, $"생명이 절반 아래면 둘에 한 번은 생명: 12번에 {heals}번");

        // 보석: 줍는 둘레 안에 들어야 주워진다
        var gems = Empty(4);
        gems.Gems.Add(new Swarm.Gem { X = gems.X + 150, Y = gems.Y, Xp = gems.Need });
        Run(gems, .3);
        Expect(gems.Level == 1 && gems.Gems.Count == 1, "먼 보석은 그대로 있다");
        gems.Steer(1, 0);
        Run(gems, 1);
        Expect(gems.Level == 2 && gems.Gems.Count == 0 && gems.LastUp != null, "보석을 주우면 레벨이 오른다");

        // ── 생명 게이지: 적 종류마다 깎이는 값, 여럿이 닿으면 가장 큰 것 하나, 1초 무적, 0 이면 끝, 하트 +30·최대 100 ──
        foreach (var (kind, tier, want) in new[] { (Swarm.Kind.Wasp, 1, 8), (Swarm.Kind.Bug, 1, 10), (Swarm.Kind.Beetle, 1, 18), (Swarm.Kind.Boss, 1, 30), (Swarm.Kind.Boss, 3, 30), (Swarm.Kind.Boss, 4, 40) })
        {
            var one = Empty(5);
            Dummy(one, kind, 3, 0, 9999, tier);
            one.Update(tick);
            Expect(one.Hp == Swarm.MaxHp - want && one.DamageTaken == want && one.HpFrom == Swarm.MaxHp && one.HpAt == one.Clock, $"{kind}({tier})에 닿으면 생명 -{want}: {one.Hp}");
        }
        var hurt = Empty(5);
        Swarm.Foe t1 = Dummy(hurt, Swarm.Kind.Bug, 3, 0), t2 = Dummy(hurt, Swarm.Kind.Boss, 0, 4), t3 = Dummy(hurt, Swarm.Kind.Beetle, -3, 0);
        hurt.Update(tick);
        Expect(hurt.Hp == 70 && hurt.SafeUntil > hurt.Clock && hurt.State == Swarm.Phase.Play, $"여럿이 닿으면 가장 큰 것 하나만: {hurt.Hp}");
        Expect(new[] { t1, t2, t3 }.All(f => Math.Sqrt((f.X - hurt.X) * (f.X - hurt.X) + (f.Y - hurt.Y) * (f.Y - hurt.Y)) >= Swarm.HurtPush + f.R - 4), "맞으면 둘레 벌레를 밀어낸다");
        Dummy(hurt, Swarm.Kind.Boss, 0, 3);
        Run(hurt, .5);
        Expect(hurt.Hp == 70, "맞은 뒤 1초는 안 맞는다");
        Run(hurt, Swarm.SafeSec);
        Expect(hurt.Hp == 40 && Swarm.SafeSec == 1, $"시간이 지나면 다시 맞는다: {hurt.Hp}");
        hurt.Gems.Add(new Swarm.Gem { X = hurt.X, Y = hurt.Y, Item = Swarm.Item.Heart });
        string heard = "";
        hurt.OnEvent = e => heard += e + " ";
        hurt.Update(tick);
        Expect(hurt.Hp == 40 + Swarm.HeartHp && Swarm.HeartHp == 30 && heard.Contains("heart") && Sounds["heart"] == Sound.Pick, $"하트를 주우면 생명 +30: {hurt.Hp}");
        hurt.Hp = 90;
        hurt.Gems.Add(new Swarm.Gem { X = hurt.X, Y = hurt.Y, Item = Swarm.Item.Heart });
        hurt.Update(tick);
        Expect(hurt.Hp == Swarm.MaxHp, "생명은 최대 100 을 넘지 않는다");
        hurt.Hp = 25;
        Dummy(hurt, Swarm.Kind.Boss, 0, -3);
        Run(hurt, Swarm.SafeSec + .2);
        Expect(hurt.Hp == 0 && hurt.State == Swarm.Phase.Over && !hurt.Done && !hurt.Won, "생명이 다 떨어지면 끝");
        double lasted = hurt.Time;
        Run(hurt, Swarm.EndSec + .1);
        Expect(hurt.Done && hurt.Time == lasted && !hurt.Won, "끝나면 잠깐 보여 주고 닫는다");
        var heartDrop = Empty(5);
        heartDrop.HeartChance = 1;
        Dummy(heartDrop, Swarm.Kind.Beetle, 150, 0, 1);
        Run(heartDrop, 1);
        Expect(heartDrop.Kills == 1 && heartDrop.Gems.All(m => m.Item != Swarm.Item.Heart), "생명이 가득이면 하트는 나오지 않는다");
        heartDrop.Hp = 50;
        Dummy(heartDrop, Swarm.Kind.Beetle, 150, 0, 1);
        Dummy(heartDrop, Swarm.Kind.Beetle, -150, 0, 1);
        Run(heartDrop, 1);
        Expect(heartDrop.Kills == 3 && heartDrop.Gems.Count(m => m.Item == Swarm.Item.Heart) == 1, "생명이 깎였으면 딱정벌레가 하트를 남긴다 (판 위에 하나만)");

        // ── 영구 자석: 주울 때마다 줍는 둘레가 22 씩 넓어진다 (56 → 166). 다섯에서 멈추고, 시간이 지나도 줄지 않는다 ──
        var mag = Empty(8);
        Expect(mag.PickPx == Swarm.PickRange && Swarm.PickRange == 56, "처음 줍는 둘레는 56");
        mag.Gems.Add(new Swarm.Gem { X = mag.X + 70, Y = mag.Y, Xp = 1 });
        mag.Gems.Add(new Swarm.Gem { X = mag.X - 160, Y = mag.Y, Xp = 1 });
        Run(mag, .3);
        Expect(mag.Xp == 0 && mag.Gems.Count == 2, "둘레 밖 보석은 그대로 있다");
        heard = "";
        mag.OnEvent = e => heard += e + " ";
        for (int n = 1; n <= Swarm.MaxMagnets + 1; n++)
        {
            mag.Gems.Add(new Swarm.Gem { X = mag.X, Y = mag.Y, Item = Swarm.Item.Magnet });
            mag.Update(tick);
            int have = Math.Min(n, Swarm.MaxMagnets);
            Expect(mag.Magnets == have && Math.Abs(mag.PickPx - (Swarm.PickRange + Swarm.MagnetStep * have)) < 1e-9 && mag.MagnetAt == mag.Clock, $"자석을 주우면 둘레가 22 씩 넓어진다, 다섯 개까지: {mag.Magnets}개 {mag.PickPx:0}");
            if (n == 1) { Run(mag, .5); Expect(mag.Xp == 1 && mag.Gems.Count == 1 && heard == "magnet " && Sounds["magnet"] == Sound.Pick, $"넓어진 둘레 안의 보석이 끌려온다, 먼 것은 그대로: 보석 {mag.Xp}"); }
        }
        Expect(mag.PickPx == 166 && Swarm.MaxMagnets == 5, $"둘레는 166 에서 멈춘다: {mag.PickPx}");
        Run(mag, 60);
        Expect(mag.Magnets == 5 && mag.PickPx == 166 && mag.Gems.Count == 0 && mag.Xp == 2, "자석은 시간이 지나도 줄지 않는다 (먼 보석도 이제 닿는다)");

        // 자석은 보통 벌레를 잡을 때 가끔: 처음 20초·판 위에 있을 때·주운 뒤 35초는 나오지 않고, 다섯 개를 채우면 더 나오지 않는다
        var drop = Empty(9);
        drop.MagnetChance = 1;
        int MagnetsOut() => drop.Gems.Count(m => m.Item == Swarm.Item.Magnet);
        void Kill() { Dummy(drop, Swarm.Kind.Bug, 200, 0, 1); Run(drop, 1.5); }
        Kill();
        Expect(drop.Kills == 1 && MagnetsOut() == 0, "처음에는 자석이 나오지 않는다");
        drop.Clock = Swarm.MagnetFirst;
        Kill();
        Kill();
        Expect(drop.Kills == 3 && MagnetsOut() == 1 && drop.Magnets == 0, $"자석은 판 위에 하나만 떨어진다: {MagnetsOut()}");
        drop.Steer(1, 0);
        Run(drop, 1.5);
        drop.Steer(0, 0);
        Expect(drop.Magnets == 1 && MagnetsOut() == 0, "자석도 가까이 가면 주워진다");
        drop.Clock += Swarm.MagnetGap - 6;
        Kill();
        Expect(drop.Kills == 4 && MagnetsOut() == 0, "자석을 주운 뒤 35초는 또 나오지 않는다");
        drop.Clock += 6;
        Kill();
        Expect(drop.Kills == 5 && MagnetsOut() + drop.Magnets == 2, "쉬는 시간이 지나면 다시 나온다");
        drop.Gems.RemoveAll(m => m.Item == Swarm.Item.Magnet);
        var capped = Empty(9);
        (capped.MagnetChance, capped.Magnets, capped.Clock) = (1, Swarm.MaxMagnets, 100);
        Dummy(capped, Swarm.Kind.Bug, 200, 0, 1);
        Dummy(capped, Swarm.Kind.Boss, -200, 0, 1, 1);
        Run(capped, 1.5);
        Expect(capped.Kills == 2 && capped.Gems.All(m => m.Item != Swarm.Item.Magnet), "다섯 개를 채우면 자석은 더 나오지 않는다 (보스 것도)");

        // 신발: 밟고 지나가면 줍는 소리가 나고 그 판 내내 걸음이 15% 씩 빨라진다. 세 켤레까지만 쌓인다
        var shod = Empty(10);
        int shoeEvents = 0;
        shod.OnEvent = e => { if (e == "shoe") shoeEvents++; };
        double Walk(int dir) { double from = shod.X; shod.Steer(dir, 0); Run(shod, .5); shod.Steer(0, 0); return Math.Abs(shod.X - from); }
        double plain = Walk(1);
        Expect(Math.Abs(plain - Swarm.MoveSpeed * .5) < .01, $"신발 없이 걷는 빠르기: {plain:0.0}");
        for (int pair = 1; pair <= Swarm.MaxShoes + 1; pair++)
        {
            shod.Gems.Add(new Swarm.Gem { X = shod.X + (pair % 2 == 0 ? 60 : -60), Y = shod.Y, Item = Swarm.Item.Shoe }); // 가는 길에 놓는다
            double before = shod.Speed, walked = Walk(pair % 2 == 0 ? 1 : -1);
            int want = Math.Min(pair, Swarm.MaxShoes);
            Expect(shod.Shoes == want && shod.Gems.Count == 0 && shoeEvents == pair, $"신발은 밟고 지나가면 주워진다, 세 켤레까지: {shod.Shoes}켤레");
            Expect(Math.Abs(shod.Speed / Swarm.MoveSpeed - (1 + Swarm.ShoeBoost * want)) < 1e-9 && walked > before * .5 - .01 && walked < shod.Speed * .5 + .01, $"신발 {want}켤레면 {1 + Swarm.ShoeBoost * want:0.00}배로 걷는다: {shod.Speed / Swarm.MoveSpeed:0.00}배");
            Expect(Math.Abs(Walk(pair % 2 == 0 ? -1 : 1) / plain - (1 + Swarm.ShoeBoost * want)) < 1e-6, "주운 뒤 걸음이 그만큼 빨라진다");
        }
        Expect(Sounds["shoe"] == Sound.Pick && shod.Speed == Swarm.MoveSpeed * (1 + Swarm.ShoeBoost * Swarm.MaxShoes), "신발도 줍는 소리, 빠르기는 세 켤레에서 멈춘다");
        foreach (var (cx, cy) in new[] { (1, 1), (-1, 1), (-1, -1), (1, -1), (1, 0), (0, 1) }) // 가장 빠를 때도 판 밖으로 나가지 않는다
        {
            shod.Steer(cx, cy);
            for (int i = 0; i < 60 * 5; i++)
            {
                shod.Update(tick);
                Expect(shod.X >= f8.Left + 12 && shod.X <= f8.Right - 12 && shod.Y >= f8.Top + 26 && shod.Feet <= f8.Bottom, $"가장 빨라도 판 안에 있다: {shod.X:0}, {shod.Y:0}");
            }
        }
        Expect(shod.X == f8.Right - 12 && shod.Y == f8.Bottom - 22, "구석까지 가서 멈춘다");

        // 신발은 보통 벌레를 잡을 때 가끔: 처음 15초와 하나 떨어진 뒤 30초는 나오지 않고, 신은 것과 떨어져 있는 것을 합쳐 세 켤레까지만
        var loot = Empty(11);
        loot.ShoeChance = 1;
        int ShoesOut() => loot.Gems.Count(m => m.Item == Swarm.Item.Shoe);
        void Bug(int dir) { Dummy(loot, Swarm.Kind.Bug, dir * 200, 0, 1); Run(loot, 1.5); }
        Bug(1);
        Expect(loot.Kills == 1 && ShoesOut() == 0, "처음에는 신발이 나오지 않는다");
        loot.Clock = Swarm.ShoeFirst;
        Bug(1);
        Bug(1);
        Expect(loot.Kills == 3 && ShoesOut() == 1, $"신발이 하나 떨어지면 한동안은 또 나오지 않는다: {ShoesOut()}");
        for (int i = 2; i <= Swarm.MaxShoes + 1; i++)
        {
            loot.Clock += Swarm.ShoeGap;
            Bug(1);
            Expect(ShoesOut() == Math.Min(i, Swarm.MaxShoes) && loot.Shoes == 0, $"신발은 판 위에 세 켤레까지만 떨어진다: {ShoesOut()}");
        }
        loot.Steer(1, 0);
        Run(loot, 1.5);
        loot.Steer(0, 0);
        Expect(loot.Shoes == Swarm.MaxShoes && ShoesOut() == 0, $"떨어진 신발을 밟고 지나가며 다 줍는다: {loot.Shoes}켤레");
        loot.Clock += Swarm.ShoeGap;
        Bug(-1);
        Expect(loot.Kills == Swarm.MaxShoes + 4 && ShoesOut() == 0, "세 켤레를 다 신으면 더 나오지 않는다");

        // ── 보스: 60·120·180·240초에 하나씩 (예고는 2초 전), 체력·피해가 차례로 커진다. 2번은 말벌 여섯, 마지막은 여왕 ──
        var sched = new Swarm(TestField, 3, new Random(3), Arm.Id.Bazooka) { SafeUntil = 1e9 };
        var warns = new List<double>();
        var came = new List<(double At, int Tier, int Hp, int Touch, int Wasps)>();
        sched.OnEvent = e => { if (e == "big") warns.Add(sched.Clock); };
        var known = new HashSet<Swarm.Foe>();
        while (sched.Clock < 245)
        {
            int wasps = sched.Foes.Count(f => f.Kind == Swarm.Kind.Wasp);
            bool warned = sched.BossWarning;
            sched.Update(tick);
            foreach (var f in sched.Foes)
                if (f.Kind == Swarm.Kind.Boss && known.Add(f))
                {
                    came.Add((sched.Clock, f.Tier, f.MaxHp, Swarm.TouchDmg(f), sched.Foes.Count(x => x.Kind == Swarm.Kind.Wasp) - wasps));
                    Expect(warned && !sched.BossWarning, "보스는 예고한 뒤에 들어온다");
                }
        }
        Expect(warns.Count == 4 && warns.Select((t, i) => Math.Abs(t - (60 * (i + 1) - Swarm.BossWarn))).All(d => d < .05), $"보스 예고는 2초 전: {string.Join(" ", warns.Select(t => t.ToString("0.0")))}");
        Expect(came.Count == 4 && came.Select((c, i) => Math.Abs(c.At - 60 * (i + 1)) < .05 && c.Tier == i + 1).All(ok => ok), $"보스는 1분마다 하나씩, 4:00 에 여왕: {string.Join(" ", came.Select(c => $"{c.At:0}초 {c.Tier}번"))}");
        Expect(came.Zip(came.Skip(1), (p, n) => n.Hp > p.Hp).All(ok => ok) && came.Select(c => c.Touch).SequenceEqual(new[] { 30, 30, 30, 40 }) && came[1].Wasps >= 6 && came[0].Wasps < 6, $"보스는 차례로 단단해지고 2번은 말벌 여섯을 데려온다: 체력 {string.Join("·", came.Select(c => c.Hp))}, 말벌 +{came[1].Wasps}");
        var skipped = new Swarm(TestField, 3, new Random(3), Arm.Id.Bazooka) { SafeUntil = 1e9, Clock = 130 };
        Run(skipped, 5);
        Expect(skipped.Foes.All(f => f.Kind != Swarm.Kind.Boss), "중간부터 시작하면 이미 지난 보스는 나오지 않는다");

        // 3번 보스는 돌진: 5초마다 0.6초 멈춰 떨다가 0.5초 동안 그 방향으로 내달린다. 여왕은 6초마다 벌레 넷을 낳는다
        var dash = Empty(12);
        (dash.Weapon.Range, dash.SafeUntil) = (0, 1e9);
        var charger = dash.Add(Swarm.Kind.Boss, dash.X + 300, dash.Y, 3);
        while (!dash.Shaking(charger) && dash.Clock < 10) dash.Update(tick);
        double shakeAt = dash.Clock, shakeX = charger.X;
        Expect(Math.Abs(shakeAt - Swarm.DashEvery) < .05 && shakeX < dash.X + 60, $"3번 보스는 5초마다 멈춰 떤다: {shakeAt:0.00}초");
        while (dash.Shaking(charger)) dash.Update(tick);
        Expect(Math.Abs(dash.Clock - shakeAt - Swarm.DashWind) < .05 && Math.Abs(charger.X - shakeX) < 5, "돌진 예고 0.6초 동안은 제자리");
        Run(dash, Swarm.DashSec + .1);
        Expect(Math.Abs(shakeX - charger.X - Swarm.DashSpeed * Swarm.DashSec) < 12 && charger.Y == dash.Y, $"예고한 방향으로 내달린다: {shakeX - charger.X:0}px");
        var hive = Empty(12);
        (hive.Weapon.Range, hive.SafeUntil) = (0, 1e9);
        var q = hive.Add(Swarm.Kind.Boss, hive.X + 320, hive.Y, Swarm.Bosses);
        Run(hive, Swarm.QueenEvery - .2);
        Expect(hive.Foes.Count == 1 && q.R > charger.R, "여왕은 더 크다");
        Run(hive, .4);
        Expect(hive.Foes.Count == 5 && hive.Foes.Skip(1).All(f => f.Kind == Swarm.Kind.Bug), $"여왕은 6초마다 벌레 넷을 낳는다: {hive.Foes.Count - 1}");

        // 보스가 남기는 것: 보석 15·20·25·30 + 하트, 1·3번은 자석도
        for (int tier = 1; tier <= Swarm.Bosses; tier++)
        {
            var kill = Empty(13);
            kill.Hp = 50;
            Dummy(kill, Swarm.Kind.Boss, 220, 0, 1, tier);
            Run(kill, 1);
            Expect(kill.BossesKilled == 1 && kill.Gems.Count(m => m.Item == Swarm.Item.Heart) == 1 && kill.Gems.Count(m => m.Item == Swarm.Item.Magnet) == tier % 2 && kill.Gems.Any(m => m.Xp == 10 + 5 * tier),
                $"{tier}번 보스가 남기는 것: 보석 {kill.Gems.Max(m => m.Xp)}, 하트 {kill.Gems.Count(m => m.Item == Swarm.Item.Heart)}, 자석 {kill.Gems.Count(m => m.Item == Swarm.Item.Magnet)}");
        }

        // ── 끝: 5:00 이 되면 무조건 끝·승리. 보스 넷을 다 잡았으면 큰 왕관(Clears), 아니면 작은 왕관(Wins) ──
        var winHost = new SceneHost();
        var rec = new SwarmGame(winHost);
        var host = new SceneHost();
        var game = new SwarmGame(host);
        for (int bosses = 3; bosses <= 4; bosses++)
        {
            var win = new Swarm(TestField, 3, new Random(6), Arm.Id.Pistols) { SafeUntil = 1e9, Clock = Swarm.WinSec - 5, BossesKilled = bosses };
            Run(win, 5.1);
            Expect(win.State == Swarm.Phase.Win && win.Foes.Count == 0 && win.Shots.Count == 0 && win.Time == Swarm.WinSec && Swarm.WinSec == 300, "5:00 을 버티면 이긴다");
            Expect(win.Won && win.Cleared == (bosses == 4), "보스 넷을 다 잡아야 큰 왕관");
            Run(win, Swarm.EndSec + .1);
            Expect(win.Done && win.Won && win.Clock < Swarm.WinSec + Swarm.EndSec + 1, "이긴 뒤에도 닫힌다 (판이 5분을 넘지 않는다)");
            (rec.swarm, rec.ses) = (win, Session.Fake(TestField));
            winHost.Pet.StartPuppet();
            rec.Stop(false);
            Expect(!rec.Running && rec.wins == bosses - 2 && rec.clears == bosses - 3 && rec.bestSec == 300 && rec.lastWeapon == Arm.Id.Pistols && rec.best["pistols"] == 300, $"기록: 버틴 판 {rec.wins}, 다 잡은 판 {rec.clears}");
        }

        // ── 기록: 옛 기록도 그대로 읽힌다. 모르는 무기 이름·깨진 JSON 은 버린다 ──
        var old = new SwarmGame(host);
        old.LoadState("{\"Sec\":120,\"Kills\":300}");
        Expect(old.bestSec == 120 && old.bestKills == 300 && old.wins == 0 && old.clears == 0 && old.lastWeapon == null && old.best.Count == 0, "옛 기록 {Sec, Kills} 가 그대로 읽힌다");
        Expect(old.Stats().Single().Played, "옛 기록이 상태창에 나온다");
        var copy = new SwarmGame(host);
        copy.LoadState(rec.SaveState());
        Expect(copy.bestSec == 300 && copy.bestKills == rec.bestKills && copy.wins == 2 && copy.clears == 1 && copy.lastWeapon == Arm.Id.Pistols && copy.best["pistols"] == 300, "저장한 기록을 다시 읽으면 같다");
        copy.LoadState("{\"Sec\":10,\"Kills\":5,\"Wins\":2,\"Clears\":1,\"Last\":\"laser\",\"Best\":{\"flame\":200,\"laser\":50}}");
        Expect(copy.bestSec == 10 && copy.lastWeapon == null && copy.best["flame"] == 200 && copy.best.ContainsKey("laser"), "모르는 무기 이름: 지난 무기는 버리고 무기별 기록은 그냥 둔다");
        copy.LoadState("{\"Sec\":10,\"Kills\":5,\"Last\":\"flame\"}");
        copy.LoadState("{oops");
        copy.LoadState("null");
        Expect(copy.bestSec == 10 && copy.lastWeapon == Arm.Id.Flame, "깨진 기록은 버린다");

        // ── 자동 조종: 무기 일곱 × 씨 둘. 모두 끝나고, 무기마다 1분은 버티고, 수 한도 안 ──
        const int seeds = 2;
        var bots = new Swarm[Arm.Kinds * seeds];
        var over = new string?[bots.Length];
        Parallel.For(0, bots.Length, i => bots[i] = BotGame((Arm.Id)(i / seeds), i % seeds, s =>
        {
            if (TestField.Contains((int)s.X, (int)s.Y) && s.Foes.Count <= Swarm.MaxFoes + 4 && s.Shots.Count <= Swarm.MaxShots && s.Gems.Count <= Swarm.MaxGems && s.Hp <= Swarm.MaxHp && s.Magnets <= Swarm.MaxMagnets) return true;
            over[i] = $"{s.Clock:0}초 벌레 {s.Foes.Count} 탄 {s.Shots.Count} 보석 {s.Gems.Count}";
            return false;
        }));
        var line = new StringBuilder();
        for (int w = 0; w < Arm.Kinds; w++)
        {
            var mine = bots.Skip(w * seeds).Take(seeds).ToArray();
            Expect(over.Skip(w * seeds).Take(seeds).All(o => o == null), $"판 안에서, 벌레·탄·보석 수 한도 ({KoNames[w]}): {over.Skip(w * seeds).FirstOrDefault(o => o != null)}");
            Expect(mine.All(s => s.State != Swarm.Phase.Play && s.Time <= Swarm.WinSec), $"판은 끝난다 ({KoNames[w]})");
            Expect(mine.Max(s => s.Time) >= 60, $"자동 조종이 1분은 버틴다 ({KoNames[w]}): {string.Join("·", mine.Select(s => $"{s.Time:0}초"))}");
            line.Append($"{(w > 0 ? ", " : "")}{KoNames[w]} {mine.Average(s => s.Time):0}초/{mine.Count(s => s.Won)}");
        }
        Expect(bots.Sum(s => s.Magnets) >= Arm.Kinds && bots.Sum(s => s.Shoes) >= Arm.Kinds, $"자동 조종도 자석·신발을 줍는다: 자석 {bots.Sum(s => s.Magnets)}개, 신발 {bots.Sum(s => s.Shoes)}켤레");

        // ── 조작: 판이 앞 창일 때만 방향키로 움직인다. 글자 키(WASD)나 다른 창에서 누른 키로는 안 움직인다. 보스 키로 끝내면 티가 나지 않는다 ──
        var keys = new HashSet<Keys> { Keys.W, Keys.A, Keys.S, Keys.D, Keys.Space };
        bool front = true;
        (game.swarm, game.ses) = (Empty(7), Session.Fake(TestField, keys.Contains, () => front));
        var play = game.swarm;
        host.Pet.StartPuppet();
        void Frames(int n) { for (int i = 0; i < n; i++) game.Frame(tick); }
        var (x0, y0) = (play.X, play.Y);
        Frames(20);
        Expect(play.X == x0 && play.Y == y0, "글자 키로는 안 움직인다");
        keys.UnionWith(new[] { Keys.Left, Keys.Up });
        Frames(20);
        Expect(play.X < x0 - 20 && play.Y < y0 - 20, $"← ↑ 로 움직인다: {play.X - x0:0}, {play.Y - y0:0}");
        (front, x0, y0) = (false, play.X, play.Y);
        Frames(20);
        Expect(play.X == x0 && play.Y == y0 && !play.Moving, "다른 창을 쓰는 동안 누른 방향키로는 안 움직인다");
        game.Stop(true);
        Expect(!game.Running && host.Pet.Mode == Mode.Wander && !host.Pet.Emoting, "보스 키(Esc)로 끝내면 결과 화면·표정 없이 바로 끝난다");

        // ── 디버그 명령: swarm weapon <이름> 으로 그 무기 판을 시작하고, 하는 중이면 그 무기로 새 판 ──
        var cmd = new SwarmGame(host) { FakeOpen = f => Session.Fake(f) };
        Expect(cmd.Command(new[] { "swarm", "weapon", "flame" }) && cmd.Running && cmd.swarm!.Weapon.Which == Arm.Id.Flame && host.Pet.Mode == Mode.Puppet, "swarm weapon flame 으로 화염방사기 판을 시작한다");
        for (int w = 0; w < Arm.Kinds; w++)
        {
            Frames2(cmd, 30);
            cmd.Command(new[] { "swarm", "weapon", Arm.Name((Arm.Id)w) });
            Expect(cmd.Running && cmd.swarm!.Weapon.Which == (Arm.Id)w && cmd.swarm.Clock == 0, $"swarm weapon {Arm.Name((Arm.Id)w)}: 하는 중이면 그 무기로 새 판");
        }
        var kept = cmd.swarm!;
        Expect(cmd.Command(new[] { "swarm", "weapon", "laser" }) && cmd.swarm == kept && !cmd.Command(new[] { "volley" }), "모르는 무기 이름은 무시한다");
        cmd.Command(new[] { "swarm", "at", "100" });
        cmd.Command(new[] { "swarm", "up", "3" });
        cmd.Command(new[] { "swarm", "hp", "40" });
        cmd.Command(new[] { "swarm", "boss", "4" });
        cmd.Command(new[] { "swarm", "item", "magnet" });
        cmd.Command(new[] { "swarm", "auto" });
        Expect(kept.Clock == 100 && kept.Hp == 40 && kept.Level == 4 && kept.Foes.Any(f => f.Tier == Swarm.Bosses) && kept.Gems.Any(m => m.Item == Swarm.Item.Magnet) && cmd.auto, "swarm at·hp·up·boss·item·auto");
        Frames2(cmd, 30);
        cmd.Command(new[] { "swarm", "stop" });
        Expect(!cmd.Running && !cmd.auto && cmd.lastWeapon == null && host.Pet.Mode == Mode.Wander && !host.Pet.Emoting, "swarm stop 은 보스 키와 같다 (자동 조종 판은 기록에 안 남는다)");
        var before2 = (Arm.Id?)null;
        for (int i = 0; i < 20; i++)
        {
            cmd.Command(new[] { "swarm" });
            var now = cmd.swarm!.Weapon.Which;
            Expect(cmd.Running && now != before2, "다음 판은 지난 판과 다른 무기로 시작한다");
            Frames2(cmd, 5);
            cmd.Stop(true);
            Expect(!cmd.Running && cmd.lastWeapon == now && host.Pet.Mode == Mode.Wander, "Esc 는 어느 무기에서든 바로 끝낸다");
            before2 = now;
        }

        // ── 다시 칠할 자리: 무기 일곱 가지 각각 여왕이 오는 구간 20초씩 (보스 예고·막대, 자석 점선 원, 생명 막대 포함), 그리고 5:00 결과 화면까지.
        //    바뀐 자리만 다시 그려도 화면에 지나간 그림이 남지 않는다 ──
        int painted = 0;
        for (int w = 0; w <= Arm.Kinds; w++)
        {
            bool ending = w == Arm.Kinds; // 마지막은 끝 화면 (큰 왕관·반짝이)
            var paint = new Swarm(TestField, 3, new Random(5), ending ? Arm.Id.Flame : (Arm.Id)w) { Clock = ending ? Swarm.WinSec - 12 : 236, Hp = 45 };
            for (int i = 0; i < 12; i++) paint.LevelUp();
            if (ending) paint.BossesKilled = Swarm.Bosses;
            paint.Hp = 20;
            paint.Gems.Add(new Swarm.Gem { X = paint.X + 45, Y = paint.Y + 20, Item = Swarm.Item.Magnet });
            paint.Gems.Add(new Swarm.Gem { X = paint.X - 110, Y = paint.Y - 40, Item = Swarm.Item.Shoe });
            for (int i = 0; i < 12; i++) paint.Gems.Add(new Swarm.Gem { X = TestField.Left + 30 + i * 54, Y = TestField.Top + 60 + i % 3 * 150, Xp = 1 + i % 2 * 2, Item = i == 5 ? Swarm.Item.Heart : Swarm.Item.Gem });
            game.Begin(paint);
            paint.OnEvent = null;
            game.ses = Session.Fake(TestField);
            game.ses.Guide("note.swarm_move", "note.swarm_auto", "note.esc");
            game.ses.GuideUntil = paint.Clock + 2;
            using var ov = host.Overlay(game);
            double until = paint.Clock + 20;
            bool sawQueen = false, sawLow = false;
            var (bad, first) = SceneKit.Repaint(ov, game, TestField, () =>
            {
                if (paint.Done || paint.Clock >= until) return false;
                var (dx, dy) = paint.BotSteer();
                paint.Steer(dx, dy);
                paint.Update(tick);
                if (paint.State == Swarm.Phase.Play) paint.Hp = Math.Max(paint.Hp, 12); // 끝까지 그려 보게
                sawQueen |= Boss(paint) is { Tier: Swarm.Bosses };
                sawLow |= HeartHidden(paint);
                game.Track();
                return true;
            }, 60 * 30);
            Expect(bad == 0, $"바뀐 자리만 다시 칠해도 화면이 맞다 ({(ending ? "끝 화면" : KoNames[w])}): {bad}번 다름 (처음 {first}프레임)");
            Expect(paint.Magnets >= 1 && (ending ? paint.Done && paint.Cleared : sawQueen), $"다시 칠할 자리 검사에 자석·여왕·끝 화면이 들어 있다 ({w}): 자석 {paint.Magnets}개, 여왕 {sawQueen}, 끝 {paint.Done}");
            painted += sawLow ? 1 : 0;
            game.swarm = null;
        }
        Expect(painted >= 1, "다시 칠할 자리 검사에 깜빡이는 생명 하트가 들어 있다");
        return $"버그 떼: 무기 {Arm.Kinds}가지 (판마다 하나, 뽑힌 수 {string.Join("·", drawn)}/200), 자동 조종 평균 초/끝까지 버틴 판({seeds}판씩) — {line}. 생명 {Swarm.MaxHp}, 보스 1분마다 {Swarm.Bosses}번, {Swarm.WinSec:0}초에 끝, 자석 {Swarm.MaxMagnets}개까지 둘레 {Swarm.PickRange:0}→{Swarm.PickRange + Swarm.MagnetStep * Swarm.MaxMagnets:0}";

        static void Frames2(SwarmGame g, int n) { for (int i = 0; i < n; i++) g.Frame(Tick); }
    }
}
