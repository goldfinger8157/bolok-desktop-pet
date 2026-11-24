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
    // 값 spec: 특수기 여섯 장 = 산탄총 둘째 발 · 쌍검 칼 · 쌍권총 난사 · 바주카 새끼 폭탄 · 화염 불 자리 · 번쩍(밝은 손·무기 그림·반짝이)
// 값 art: 무기·갈래 그림과 수류탄·특수기 그림만 크게 · table: 무기 표만 · check: 버그 떼 자가 검사만 · nade: 시작 안내·열한째·열두째만
    // 열한째 = 수류탄: 3개 든 판에서 하나 던져 터진 직후 + 둘째가 공중(그림자) + 옆에 떨어진 수류탄 + 표시의 수 1 / 열두째 = 빈 손: 0개에서 Ctrl (표시·머리 위 회색 수류탄이 깜빡)
    public static void Scene(string path, int k)
    {
        string? arg = GameModes.SceneArg;
        if (arg == "check") { Console.OutputEncoding = Encoding.UTF8; L.Use("ko"); Console.WriteLine(Check()); return; }
        if (arg is { } ta && ta.StartsWith("table")) // table: 단계 1·3·5·10 표 · table:<단계>[:<씨 수>]: 그 단계만
        {
            Console.OutputEncoding = Encoding.UTF8;
            L.Use("ko");
            var part = ta.Split(':');
            if (part.Length > 3 && part[3] == "compare" && int.TryParse(part[1], out int cs) && int.TryParse(part[2], out int cn)) { Console.WriteLine(Compare(cn, cs, !(part.Length > 4 && part[4] == "nocamp"))); return; }
            if (part.Length > 1 && int.TryParse(part[1], out int only1))   // table:<단계>:<씨 수>:off = 수류탄 없이, :nospec = 특수기 없이 (고치기 전 표와 같아야 한다), :idle = 봇이 제자리에 선다 (수류탄은 그대로),
            // :corner·:edge = 제자리 봇이 처음에 모서리·변 가운데(씨 % 4 쪽)로 걸어가 서고, :cornerhold·:edgehold = 거기서 벽 쪽 키를 쥐고, :cornergrab = 가까이 떨어진 보석을 주우러 나갔다 온다, :nocamp = 가만히 있기 막기 없이, 여럿은 , 로 같이.
            // table:<단계>:<씨 수>:compare[:nocamp] = 무기 × (걷는 봇·가운데·모서리·변·모서리 키·변 키) 한 표
            {
                var opts = part.Length > 3 ? part[3].Split(',') : Array.Empty<string>();
                Console.WriteLine(Table(part.Length > 2 && int.TryParse(part[2], out int sd) ? sd : only1 >= 5 ? 80 : 40, only1, !opts.Contains("off"), !opts.Contains("nospec"), CampOf(opts), !opts.Contains("nocamp")));
            }
            else Console.WriteLine(Tables());
            return;
        }
        if (arg is { } tu && tu.StartsWith("tune:")) { Console.OutputEncoding = Encoding.UTF8; L.Use("ko"); Console.WriteLine(Tune(tu.Split(':'))); return; }   // tune:<단계>:<씨 수>:<무기>[,<무기>…][:nospec|:idle|:corner|:edge|:cornergrab|:nocamp] 무기 몇 개만 빨리 (손잡이 맞출 때)
        if (arg == "art") { SwarmArt.Sheet(path, 8); return; }   // 위 둘은 줄 하나라 하나만 크게
        if (arg == "stage") { StageScene(path, k); return; }
        if (arg == "row") { RowScene(path, k); return; }   // 결과 화면 무기 줄 두 장
        if (arg == "stage2") { Stage2Scene(path, k); return; }
        if (arg == "fp") { Console.WriteLine(Fingerprint()); return; }   // 1·2단계 지문 (자가 검사의 FingerprintWas 에 적는다)   // 2차 기믹·보상 아홉 장
        if (arg == "mound") { MoundScene(path, k); return; }   // 흙더미 여섯 장 (SwarmSpecCheck.cs)
        if (arg == "spec") { SpecScene(path, k); return; }   // 특수기 여섯 장 (SwarmSpecCheck.cs)
        if (arg is "bench" or "bench:nospec") { Console.OutputEncoding = Encoding.UTF8; L.Use("ko"); Console.WriteLine(BenchLines(arg == "bench")); return; }   // 속도만 (표 없이). bench:nospec = 특수기를 끈 같은 빌드 (견주는 기준)
        var host = new SceneHost(scale: k);
        var game = new SwarmGame(host);
        var field = FieldAt(k);
        bool one = Arm.Find(arg, out var only), nadeOnly = arg == "nade";
        int[] only3 = { 12, 10, 11 };   // nade: 시작 안내(네 줄이 깃발·머리 위 무기와 안 겹치는지) · 수류탄 · 빈 손
        double nadeAt = -1;   // 수류탄 장면에서 첫 수류탄을 던진 때 (-1 이면 아직)
        bool second = false;
        Swarm s = null!;
        void New(int seed, Arm.Id id, double clock = 0, int levels = 0)
        {
            s = new Swarm(field, k, new Random(seed), id) { Clock = clock };
            for (int i = 0; i < levels; i++) s.LevelUp();
            (s.UpAt, s.LastUp) = (-9, null);
            game.Begin(s);
            s.OnEvent = null;
            game.ses = null;
        }
        if (one) New(5, only);
        using var ov = host.Overlay(game);
        int made = -1;
        SceneKit.Shots(path, ov, Rectangle.Inflate(field, 20, 20), one ? 6 : nadeOnly ? 3 : 12, shot0 =>
        {
            int shot = nadeOnly ? only3[shot0] : shot0;
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
                else if (shot == 9) { New(13, Arm.Id.Long, 296, 14); s.BossesKilled = Swarm.Bosses; }
                else if (shot == 10) // 수류탄: 가만히 선 채 (옆에 떨어진 것을 안 줍게) 벌레 몇 마리 앞으로 하나 던지고, 터진 직후에 둘째가 공중
                {
                    New(14, Arm.Id.Long, 100);
                    (s.Grenades, s.BotThrows, nadeAt, second) = (3, false, -1, false);
                    s.Drop(Swarm.Item.Grenade, -90);
                    foreach (var (bx, by) in new[] { (150, -6), (172, 20), (160, -34), (186, -12), (140, 30), (176, 44), (-70, 80), (-96, 96), (-60, 110) }) s.Add(Swarm.Kind.Bug, s.X + bx, s.Y + by).Speed = 20;
                }
                else if (shot == 12) { New(5, Arm.Id.Long); game.ses = Session.Fake(field); game.ses.Guide(GuideKeys); }   // 시작 0.8초: 안내 네 줄 (nade 에서만)
                else // 빈 손: 0개에서 Ctrl
                {
                    New(15, Arm.Id.Long, 100);
                    (s.Grenades, s.BotThrows) = (0, false);
                }
            }
            if (!one && shot is 10 or 11)
            {
                s.Steer(0, 0);
                if (shot == 10 && nadeAt < 0 && s.Clock >= 100.2) { nadeAt = s.Clock; s.Steer(1, 0); s.Throw(); }
                else if (shot == 10 && !second && nadeAt >= 0 && s.Clock >= nadeAt + .45) { second = true; s.ThrowAt = -9; s.Steer(-1, 1); s.Throw(); }   // 장면에서만 쿨다운을 지운다
                else if (shot == 11 && s.Clock >= 100.2 && s.EmptyAt < 0) s.Throw();
            }
            else s.BotPlay();
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
            if (shot == 10) return s.Clock - s.NadeBoomAt is > .17 and < .23 && s.Nades.Count == 1 || s.Clock >= 104;
            if (shot == 12) return s.Clock >= .8;
            if (shot == 11) return s.Clock - s.EmptyAt is > .02 and < .06 || s.Clock >= 104;
            if (shot == 7) return s.Clock >= 244 && Boss(s) is { Tier: Swarm.Bosses } && !HeartHidden(s) || s.Clock >= 260;
            if (shot == 8) return s.Clock - s.MagnetAt is > .2 and < .5 && s.BossWarning && s.Clock - s.BossWarnAt < .15 || s.Clock >= 59.5;
            return Ended(s) && s.EndedFor > 1.2;
        }, 60 * 1800);
    }

    // 값 stage: 단계 표시 여덟 장 = 시작 깃발(은) · 러시 예고(띠·세모) · 러시(붉은 모서리·한쪽 줄) · 엘리트 셋과 보통 벌레 · 이긴 결과 3종(보라 큰 왕관 7단계·동 작은 왕관 2단계·무지개 10단계) · 진 결과
    static void StageScene(string path, int k)
    {
        var host = new SceneHost(scale: k);
        var game = new SwarmGame(host);
        var field = FieldAt(k);
        Swarm s = null!;
        void New(int seed, Arm.Id id, int stage, int maxStage, double clock = 0)
        {
            s = new Swarm(field, k, new Random(seed), id, stage) { Clock = clock };
            (game.stage, game.maxStage, game.practice, game.auto) = (stage, maxStage, false, false);
            game.Begin(s);
            s.OnEvent = null;
        }
        using var ov = host.Overlay(game);
        int made = -1;
        SceneKit.Shots(path, ov, Rectangle.Inflate(field, 20, 20), 8, shot =>
        {
            bool fresh = made != shot;
            made = shot;
            if (fresh)
                switch (shot)
                {
                    case 0: New(5, Arm.Id.Rifle, 4, 3); break;
                    case 1: New(5, Arm.Id.Rifle, 1, 0, 35); break;
                    case 2: New(5, Arm.Id.Twin, 1, 0, 40); break;
                    case 3:
                        New(5, Arm.Id.Rifle, 5, 4, 100);
                        var shown = new[] { s.CallElite(Swarm.Kind.Bug), s.CallElite(Swarm.Kind.Wasp), s.CallElite(Swarm.Kind.Beetle), s.Add(Swarm.Kind.Bug, s.X + 120, s.Y + 60) };
                        for (int i = 0; i < shown.Length; i++) (shown[i].X, shown[i].Y, shown[i].Speed) = (s.X + 90 + i * 40 - (i == 3 ? 20 : 0), s.Y - 40 + (i == 3 ? 70 : i * 20), 0);
                        break;
                    case 4: New(13, Arm.Id.Long, 7, 6, 296); s.BossesKilled = Swarm.Bosses; break;
                    case 5: New(13, Arm.Id.Twin, 2, 1, 296); s.BossesKilled = 2; break;
                    case 6: New(13, Arm.Id.Shotgun, 10, 9, 296); s.BossesKilled = 3; break;
                    default: New(14, Arm.Id.Pistols, 3, 2, 100); s.Hp = 1; s.Add(Swarm.Kind.Wasp, s.X + 3, s.Y).Speed = 0; break;
                }
            var (dx, dy) = s.BotSteer();
            s.Steer(dx, dy);
            s.Update(Tick);
            if (s.State == Swarm.Phase.Play && shot != 7) s.Hp = Math.Max(s.Hp, 60);
            game.Track();
            return shot switch
            {
                0 => s.Clock >= .6,
                1 => s.Clock >= 36.5 && s.Warning && MarkMask(s) != 0,
                2 => s.Clock >= 41.6,
                3 => s.Clock >= .5,
                _ => Ended(s) && s.EndedFor > (shot == 7 ? 1.2 : 1.3),
            };
        }, 60 * 120);
    }

    // 값 row: 결과 화면 무기 줄 두 장 (docs/swarm-levels.md 2-3, 시안 2번으로 정함). 왼쪽 = 이긴 판 (5단계 산탄총으로 깸: 기록 3 → 5 반짝, 쌍권총·바주카는 못 깸),
    // 오른쪽 = 진 판 (최고 기록 줄이 있는 가장 높은 상자, 바주카포로 졌다: 못 깬 무기가 이번 판 무기)
    static void RowScene(string path, int k)
    {
        var host = new SceneHost(scale: k);
        var game = new SwarmGame(host);
        var field = FieldAt(k);
        Swarm s = null!;
        void New(int shot)
        {
            bool win = shot == 0;
            game.stageBest = new() { ["twin"] = 7, ["long"] = 4, ["shotgun"] = 3, ["rifle"] = 2, ["flame"] = 10 };
            (game.stage, game.maxStage, game.practice, game.auto, game.bestKills) = (5, 10, false, false, win ? 0 : 500);
            s = new Swarm(field, k, new Random(win ? 13 : 14), win ? Arm.Id.Shotgun : Arm.Id.Bazooka, 5);
            if (win) { s.SetClock(296); s.BossesKilled = 3; }
            else { s.SetClock(6); s.Hp = 1; s.Add(Swarm.Kind.Wasp, s.X + 3, s.Y).Speed = 0; }
            game.Begin(s);
            s.OnEvent = null;
        }
        using var ov = host.Overlay(game);
        int made = -1;
        SceneKit.Shots(path, ov, Rectangle.Inflate(field, 20, 20), 2, shot =>
        {
            if (made != shot) { made = shot; New(shot); }
            var (dx, dy) = s.BotSteer();
            s.Steer(dx, dy);
            s.Update(Tick);
            if (s.State == Swarm.Phase.Play && shot == 0) s.Hp = Math.Max(s.Hp, 60);
            game.Track();
            return shot == 0 ? Ended(s) && s.EndedFor is > .75 and < 1.3 && ((int)(s.EndedFor * 10) & 1) == 0 : Ended(s) && s.EndedFor > 1.2;
        }, 60 * 120, 2);
    }

    // 값 stage2: 2차 기믹·보상 아홉 장 (3열 × 3줄) = 돌진 띠가 뻗는 중(떠는 말벌 줄) · 돌진이 지나가는 중 · 양쪽 줄 예고(느낌표 두 변) ·
    //   보통·강철·엘리트 강철 딱정벌레 · 보스 둘 예고(세모 둘) · 겹친 대열 · 다시 뽑기 열린 시작(도는 화살표) · 고르기 열린 시작(점 일곱) · 이긴 결과에 방금 풀린 보상(자석, 4단계)
    static void Stage2Scene(string path, int k)
    {
        var host = new SceneHost(scale: k);
        var game = new SwarmGame(host);
        var field = FieldAt(k);
        Swarm s = null!;
        double mark = 0;
        void New(int seed, Arm.Id id, int stage, int maxStage, double clock = 0, bool spawning = true, double safe = 1e9)
        {
            (game.stage, game.maxStage, game.practice, game.auto, game.swapUsed) = (stage, maxStage, false, false, false);
            s = new Swarm(field, k, new Random(seed), id, stage) { Clock = clock, Spawning = spawning, SafeUntil = safe, Magnets = maxStage >= 4 ? 1 : 0 };
            game.Begin(s);
            s.OnEvent = null;
            game.ses = Session.Fake(field);
        }
        using var ov = host.Overlay(game);
        int made = -1;
        SceneKit.Shots(path, ov, Rectangle.Inflate(field, 20, 20), 9, shot =>
        {
            if (made != shot)
            {
                made = shot;
                switch (shot)
                {
                    case 0: case 1: New(5, Arm.Id.Rifle, 3, 2, 10, false); s.Wave(Swarm.Shape.Dart, 30, 0, 0, 0); mark = s.Clock; break;
                    case 2: New(5, Arm.Id.Rifle, 4, 3, 20); s.CallRush(Swarm.Shape.Pincer); mark = s.Clock; break;
                    case 3:
                        New(5, Arm.Id.Rifle, 5, 4, 100, false);
                        var shown = new[] { s.Add(Swarm.Kind.Beetle, s.X + 80, s.Y - 20), s.CallSteel(false), s.CallSteel(true), s.CallElite(Swarm.Kind.Beetle) };
                        for (int i = 0; i < shown.Length; i++) (shown[i].X, shown[i].Y, shown[i].Speed) = (s.X - 60 + i * 40, s.Y - 30 + (i % 2) * 24, 0);
                        break;
                    case 4: New(9, Arm.Id.Rifle, 7, 6, 175); break;
                    case 5: New(22, Arm.Id.Rifle, 10, 9, 100); break;
                    case 6: New(5, Arm.Id.Shotgun, 3, 2, 0, false, 0); break;
                    case 7: New(5, Arm.Id.Flame, 8, 7, 0, false, 0); break;
                    default: New(13, Arm.Id.Long, 4, 3, 296, false); s.BossesKilled = 3; break;
                }
            }
            if (shot >= 6 && shot < 8) { s.Update(Tick); game.Track(); return s.Clock >= .4; }
            if (shot == 5 && s.RushShape2 == null) { var (ax, ay) = s.BotSteer(); s.Steer(ax, ay); s.Foes.RemoveAll(f => f.Kind != Swarm.Kind.Boss); s.Update(Tick); if (s.State == Swarm.Phase.Play) s.Hp = Math.Max(s.Hp, 60); game.Track(); return s.RushShape2 != null; }   // 새 물결만 보이게 지난 벌레는 치운다
            var (dx, dy) = shot is 0 or 1 or 3 ? (0.0, 0.0) : s.BotSteer();
            s.Steer(dx, dy);
            s.Update(Tick);
            if (s.State == Swarm.Phase.Play) s.Hp = Math.Max(s.Hp, 60);
            game.Track();
            return shot switch
            {
                0 => s.Clock - mark >= .15,
                1 => s.Clock - mark >= 1.55,
                2 => s.Warning && MarkMask(s) != 0 && s.Clock - mark >= .5,
                3 => s.Clock >= 100.2,
                4 => s.BossTwin && s.Clock - s.BossWarnAt > .1,
                5 => s.RushShape2 != null && s.Clock - mark > 0,
                _ => Ended(s) && s.EndedFor is > .75 and < 1.3 && ((int)(s.EndedFor * 5) & 1) == 0,
            };
        }, 60 * 400, 3);
    }

    // ── 속도 재기: 같은 씨로 화염방사기(가장 많이 칠하는 무기) 한 판을 자동 조종으로 끝까지 두 번 — 1단계(옛 방식, 봇이 죽으면 거기까지)와
    //    10단계(생명을 프레임마다 채워 300초를 다 잰다) — 그리고 단계 1·3·5·10 무기 표 (--mode-bench swarm) ──
    public static string BenchRun()
    {
        return BenchLines(true) + "\n" + BenchLines(false) + "\n" + Tables();
    }

    // 화염 1단계 · 화염 10단계 · 쌍권총 10단계(난사) · 바주카 10단계(새끼 폭탄). specials=false 가 같은 빌드의 기준
    static string BenchLines(bool specials) => string.Join("\n", new[] { BenchOne(1, false, Arm.Id.Flame, specials), BenchOne(10, true, Arm.Id.Flame, specials), BenchOne(10, true, Arm.Id.Pistols, specials), BenchOne(10, true, Arm.Id.Bazooka, specials) });

    static string BenchOne(int stage, bool refill, Arm.Id id = Arm.Id.Flame, bool specials = true)
    {
        var host = new SceneHost();
        var game = new SwarmGame(host);
        var s = new Swarm(TestField, 3, new Random(5), id, stage) { SpecialsOn = specials };
        game.Begin(s);
        s.OnEvent = null;
        game.ses = Session.Fake(TestField);
        game.ses.Guide(GuideKeys);
        using var ov = host.Overlay(game);
        string speed = Bench.Run("swarm", ov, game, s.Field, () => { if (s.Done) return false; s.BotPlay(); return true; }, () => { s.Update(Tick); if (refill && s.State == Swarm.Phase.Play) s.Hp = Swarm.MaxHp; game.Track(); }, 60 * 330);
        return speed + $" [{stage}단계 {KoNames[(int)id]}{(refill ? ", 생명 채움" : "")}{(specials ? "" : ", 특수기 없이")}, {s.Time:0}초 버팀, 특수기 {s.Specials}번]";
    }

    // 자동 조종 한 판 (화면 없이 규칙만). 끝난 판을 돌려준다
    static Swarm BotGame(Arm.Id id, int seed, int stage = 1, Func<Swarm, bool>? each = null, bool throws = true, bool specials = true, int camp = -1, bool rule = true)
    {
        var s = new Swarm(TestField, 3, new Random(seed), id, stage) { BotThrows = throws, SpecialsOn = specials, CampOn = rule, BotIdle = camp >= 0, BotCamp = camp == 5 ? 1 : camp is 3 or 4 ? camp - 2 : Math.Max(0, camp), BotHold = camp is 3 or 4, BotGrab = camp == 5, BotCorner = seed & 3, Magnets = stage >= 5 ? 1 : 0 };   // 5단계부터는 4단계를 깬 사람: 자석 하나로 시작
        for (int i = 0; i < 60 * (Swarm.WinSec + 10) && s.State == Swarm.Phase.Play; i++)
        {
            s.BotPlay();
            s.Update(Tick);
            if (each != null && !each(s)) break;
        }
        return s;
    }

    static readonly string[] KoNames = { "쌍검", "장검", "쌍권총", "산탄총", "돌격소총", "바주카포", "화염방사기" };

    // 단계마다 목표 (docs/swarm-levels.md 8장): 5:00 비율 범위, 무기 하나의 폭, 평균 버틴 초, 1:00 비율, 3:00 때 레벨
    static readonly Dictionary<int, (int Lo, int Hi, int WLo, int WHi, int Secs, int One, int Lv)> Goals = new()
    {
        [1] = (35, 50, 25, 60, 230, 95, 13), [3] = (22, 35, 12, 45, 210, 95, 13), [5] = (12, 24, 5, 35, 190, 90, 13), [10] = (3, 10, 0, 20, 150, 85, 13),
    };

    // 단계 1·3·5·10 표 (1·3단계는 씨 40개, 5·10단계는 씨 80개)
    public static string Tables() => string.Join("\n\n", new[] { 1, 3, 5, 10 }.Select(st => Table(st >= 5 ? 80 : 40, st)));

    // 무기 표: 무기 7 × 씨 seeds 개를 자동 조종으로 끝까지 (판마다 Swarm·Random 을 따로 쓰므로 나란히 돌려도 된다)
    // camp: -1 걷는 봇, 0 제자리(가운데), 1 모서리에 가서 서기, 2 변 가운데에 가서 서기 (모서리·변은 씨 % 4)
    static int CampOf(IEnumerable<string> opts) => opts.Contains("cornergrab") ? 5 : opts.Contains("cornerhold") ? 3 : opts.Contains("edgehold") ? 4 : opts.Contains("corner") ? 1 : opts.Contains("edge") ? 2 : opts.Contains("idle") ? 0 : -1;
    static readonly string[] CampNames = { "제자리(idle)", "모서리(corner)", "변 가운데(edge)", "모서리 키 쥠(cornerhold)", "변 키 쥠(edgehold)", "모서리 줍기(cornergrab)" };

    // 무기 × (걷는 봇·가운데·모서리·변 가운데) 5:00 비율과 평균 초를 한 표로 (제자리 막기 손잡이를 맞출 때)
    public static string Compare(int seeds, int stage, bool rule = true)
    {
        var all = new Swarm[6][];
        for (int c = 0; c < 6; c++)
        {
            var runs = all[c] = new Swarm[Arm.Kinds * seeds];
            int camp = c - 1;
            Parallel.For(0, runs.Length, i => runs[i] = BotGame((Arm.Id)(i / seeds), 1000 + i % seeds, stage, null, true, true, camp, rule));
        }
        var sb = new StringBuilder();
        sb.AppendLine($"### {stage}단계 비교 (씨 {seeds}개씩, 5:00 비율 / 평균 초)");
        sb.AppendLine("| 무기 | 걷는 봇 | 가운데 | 모서리 | 변 가운데 | 모서리 키 쥠 | 변 키 쥠 |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        var sum = new double[6, 2];
        for (int w = 0; w < Arm.Kinds; w++)
        {
            sb.Append($"| {KoNames[w]} |");
            for (int c = 0; c < 6; c++)
            {
                var mine = all[c].Skip(w * seeds).Take(seeds).ToArray();
                double win = mine.Count(r => r.Won) * 100.0 / seeds, avg = mine.Average(r => r.Time);
                sum[c, 0] += win / Arm.Kinds; sum[c, 1] += avg / Arm.Kinds;
                sb.Append($" {win:0}% / {avg:0} |");
            }
            sb.AppendLine();
        }
        sb.Append("| 평균 |");
        for (int c = 0; c < 6; c++) sb.Append($" {sum[c, 0]:0.0}% / {sum[c, 1]:0} |");
        sb.AppendLine();
        sb.Append($"땅에서 올라오게 한 벌레 수(판당 평균): 걷는 봇 {all[0].Average(r => r.Burrows):0.00} · 가운데 {all[1].Average(r => r.Burrows):0.0} · 모서리 {all[2].Average(r => r.Burrows):0.0} · 변 {all[3].Average(r => r.Burrows):0.0} · 모서리 키 {all[4].Average(r => r.Burrows):0.0} · 변 키 {all[5].Average(r => r.Burrows):0.0}");
        return sb.ToString();
    }

    public static string Table(int seeds, int stage = 1, bool throws = true, bool specials = true, int camp = -1, bool rule = true)
    {
        bool idle = camp >= 0;
        var runs = new Swarm[Arm.Kinds * seeds];
        Parallel.For(0, runs.Length, i => runs[i] = BotGame((Arm.Id)(i / seeds), 1000 + i % seeds, stage, null, throws, specials, camp, rule));
        var sb = new StringBuilder();
        sb.AppendLine($"### {stage}단계 (씨 {seeds}개씩{(idle ? ", " + CampNames[camp] : "")}{(throws ? "" : ", 수류탄 없이")}{(specials ? "" : ", 특수기 없이")}{(rule ? "" : ", 가만히 있기 막기 없이")})");
        sb.AppendLine("| 무기 | 5:00 비율 | 1:00 비율 | 평균 초 | 중앙 초 | 평균 레벨 | 3:00 레벨 | 잡은 수 | 받은 피해 | 남은 생명 | 잡은 보스 | 첫 보스(초) | 자석 | 던진 수 | 수류탄으로 잡은 수 | 특수기 수 | 특수기로 잡은 수 | 흙더미 수 (판 비율) |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        var win = new double[Arm.Kinds];
        double meleeKills = 0, rangedKills = 0, meleeHurt = 0, rangedHurt = 0, secs = 0, one = 0, lv3 = 0;
        for (int w = 0; w < Arm.Kinds; w++)
        {
            var mine = runs.Skip(w * seeds).Take(seeds).ToArray();
            var sorted = mine.Select(r => r.Time).OrderBy(t => t).ToArray();
            var first = mine.Where(r => r.FirstBossSec >= 0).Select(r => r.FirstBossSec).DefaultIfEmpty(-1).Average();
            double kills = mine.Average(r => r.Kills), hurt = mine.Average(r => r.DamageTaken), lv180 = mine.Average(r => r.Level180 >= 0 ? r.Level180 : r.Level);
            win[w] = mine.Count(r => r.Won) * 100.0 / seeds;
            (secs, one, lv3) = (secs + sorted.Average() / Arm.Kinds, one + mine.Count(r => r.Time >= 60) * 100.0 / seeds / Arm.Kinds, lv3 + lv180 / Arm.Kinds);
            if (w < 2) (meleeKills, meleeHurt) = (meleeKills + kills / 2, meleeHurt + hurt / 2);
            else (rangedKills, rangedHurt) = (rangedKills + kills / 5, rangedHurt + hurt / 5);
            sb.AppendLine($"| {KoNames[w]} | {win[w]:0}% | {mine.Count(r => r.Time >= 60) * 100 / seeds}% | {sorted.Average():0} | {sorted[seeds / 2]:0} | {mine.Average(r => r.Level):0.0} | {lv180:0.0} | {kills:0} | {hurt:0} | {mine.Average(r => r.Hp):0} | {mine.Average(r => r.BossesKilled):0.0} | {first:0} | {mine.Average(r => r.Magnets):0.0} | {mine.Average(r => r.Thrown):0.0} | {mine.Average(r => r.NadeKills):0} | {mine.Average(r => r.Specials):0.0} | {mine.Average(r => r.SpecKills):0} | {mine.Average(r => r.Burrows):0.0} ({mine.Count(r => r.Burrows > 0) * 100 / seeds}%) |");
        }
        double weakest = Math.Min(Math.Min(win[(int)Arm.Id.Pistols], win[(int)Arm.Id.Shotgun]), Math.Min(win[(int)Arm.Id.Bazooka], win[(int)Arm.Id.Flame]));
        double meleeWin = (win[0] + win[1]) / 2, rangedWin = (win[2] + win[4] + win[5]) / 3, midWin = (win[3] + win[6]) / 2, allWin = win.Average();
        sb.AppendLine($"평균: 5:00 {allWin:0.0}%, 1:00 {one:0}%, 버틴 초 {secs:0}, 3:00 레벨 {lv3:0.0}");
        sb.AppendLine($"약한 무기(쌍권총·산탄총·바주카·화염) 가장 낮은 값 {weakest:0}%");
        sb.AppendLine($"근접(쌍검·장검) 5:00 평균 − 원거리(쌍권총·소총·바주카) 평균 = {meleeWin - rangedWin:+0.0;-0.0}%p ({meleeWin:0.0}% − {rangedWin:0.0}%), 중거리(산탄총·화염) {midWin:0.0}% (원거리 대비 {midWin - rangedWin:+0.0;-0.0}%p)");
        sb.Append($"근접/원거리: 잡은 수 {meleeKills / rangedKills:0.00}배, 받은 피해 {meleeHurt / rangedHurt:0.00}배");
        if (Goals.TryGetValue(stage, out var g))
        {
            int gap = stage == 10 ? 15 : 12;
            string Ok(bool ok) => ok ? "O" : "X";
            sb.Append($"\n목표: 5:00 {g.Lo}~{g.Hi}% {Ok(allWin >= g.Lo && allWin <= g.Hi)} · 무기 폭 {g.WLo}~{g.WHi}% {Ok(win.Min() >= g.WLo && win.Max() <= g.WHi)} · 평균 초 {g.Secs}+ {Ok(secs >= g.Secs)} · 1:00 {g.One}%+ {Ok(one >= g.One)} · 3:00 레벨 {g.Lv}+ {Ok(lv3 >= g.Lv)} · 근접−원거리 ±{gap}%p {Ok(Math.Abs(meleeWin - rangedWin) <= gap)}");
        }
        return sb.ToString();
    }

    // 무기 몇 개만 한 줄씩: 5:00 비율·평균 초·특수기 수·특수기로 잡은 수 (손잡이를 맞출 때 7개를 다 돌리지 않으려고)
    static string Tune(string[] part)
    {
        int stage = int.Parse(part[1]), seeds = int.Parse(part[2]);
        var ids = part[3].Split(',').Select(n => Arm.Find(n, out var id) ? id : throw new ArgumentException(n)).ToArray();
        bool specials = !part.Skip(4).Contains("nospec");
        int camp = CampOf(part.Skip(4));
        bool idle = camp >= 0;
        var runs = new Swarm[ids.Length * seeds];
        Parallel.For(0, runs.Length, i => runs[i] = BotGame(ids[i / seeds], 1000 + i % seeds, stage, null, true, specials, camp, !part.Skip(4).Contains("nocamp")));
        var sb = new StringBuilder();
        for (int w = 0; w < ids.Length; w++)
        {
            var mine = runs.Skip(w * seeds).Take(seeds).ToArray();
            var sorted = mine.Select(r => r.Time).OrderBy(t => t).ToArray();
            sb.AppendLine($"{stage}단계 {Arm.Name(ids[w])}{(idle ? "(" + CampNames[camp] + ")" : "")}: 5:00 {mine.Count(r => r.Won) * 100.0 / seeds:0.0}% · 평균 {mine.Average(r => r.Time):0}초 · 중앙 {sorted[seeds / 2]:0} · 90% {sorted[seeds * 9 / 10]:0} · 최대 {sorted[^1]:0} · 잡은 수 {mine.Average(r => r.Kills):0} · 특수기 {mine.Average(r => r.Specials):0.0}번 · 특수기로 잡은 수 {mine.Average(r => r.SpecKills):0} · 흙더미 {mine.Average(r => r.Burrows):0.0}개 (판 {mine.Count(r => r.Burrows > 0) * 100.0 / seeds:0}%)");
        }
        return sb.ToString().TrimEnd();
    }

    // ── 자가 검사 (화면 없이 규칙만) ──
    static void Expect(bool ok, string what) { if (!ok) throw new Exception("자가 검사 실패: " + what); }

    public static string Check()
    {
        const double tick = Tick;
        void Run(Swarm s, double sec) { for (int i = 0; i < sec * 60 && !s.Done; i++) s.Update(tick); }
        Swarm Empty(int seed, Arm.Id id = Arm.Id.Pistols, int stage = 1) => new(TestField, 3, new Random(seed), id, stage) { Spawning = false, ShoeChance = 0, MagnetChance = 0, HeartChance = 0, GrenadeChance = 0, SpecialsOn = false };   // 특수기는 꺼 둔다: 아래 무기 검사가 알·두 발·틱 피해를 세므로 (특수기는 따로 켠 검사에서)
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
            spin.MovedAt = spin.Clock;   // 회전 베기는 걸으며 휘두를 때만 나간다 (걷는 중으로 친다)
            for (int was = spin.SwingCount; spin.SwingCount == was;) spin.Update(tick);
            (sf1.X, sb1.X) = (spin.X + 25, spin.X - 26); // 밀려난 과녁을 제자리로
            if (n % 4 == 0) Expect(spin.SwingSpin && spin.SwingArc == Math.PI && h1 - sf1.Hp == spin.Weapon.Dmg * 3 / 2 && h2 - sb1.Hp == h1 - sf1.Hp, $"넷째 휘두름은 한 바퀴 회전 베기 (피해 1.5배): {h1 - sf1.Hp}·{h2 - sb1.Hp}");
            else Expect(!spin.SwingSpin && h1 - sf1.Hp == spin.Weapon.Dmg && h2 == sb1.Hp, $"보통 휘두름은 겨눈 쪽만: {n}번째 {h1 - sf1.Hp}·{h2 - sb1.Hp}");
        }

        // 가만히 서서는 회전 베기가 안 나간다: 넷째·여덟째… 차례가 와도 보통 휘두름이고, 걷기 시작하면 밀렸던 회전 베기가 바로 나간다 (밀린 건 하나뿐)
        var still = Empty(3, Arm.Id.Twin);
        still.SafeUntil = 1e9;
        for (int i = 0; i < 3; i++) still.Weapon.Grow(Swarm.Up.Power);
        var stillMark = Dummy(still, Swarm.Kind.Beetle, 25, 0);
        void Swing(Swarm t) { stillMark.X = t.X + 25; for (int was = t.SwingCount; t.SwingCount == was;) t.Update(tick); }   // 밀려난 과녁을 제자리로 놓고 한 번 휘두를 때까지
        for (int n = 1; n <= 12; n++)
        {
            Swing(still);
            Expect(!still.SwingSpin && still.SwingArc < Math.PI - .01, $"가만히 서면 {n}번째 휘두름도 회전 베기가 아니다");
        }
        still.MovedAt = still.Clock;
        Swing(still);
        Expect(still.SwingSpin && still.SwingCount == 13, $"걷기 시작하면 밀렸던 회전 베기가 바로 나간다: {still.SwingCount}번째");
        still.MovedAt = still.Clock;
        Swing(still);
        Expect(!still.SwingSpin, "밀렸던 회전 베기는 한 번만 (14번째는 보통)");
        still.MovedAt = -9;   // 걸은 지 한참 됐다
        for (int n = 15; n <= 16; n++) Swing(still);
        Expect(!still.SwingSpin, "걸음을 멈추면 16번째 차례도 안 돈다");
        still.MovedAt = still.Clock;
        Swing(still);
        Expect(still.SwingSpin, "다시 걸으면 17번째에 나간다");

        // 제자리 자동 조종: 가운데든 구석이든 가만히 선 채로는 어느 무기도 5:00 을 못 버틴다 (1단계, 무기마다 씨 6개).
        // 가만히 있기 막기 전 표(씨 120): 가운데 평균 58초·구석 70초 (쌍검 구석 120초·최대 247초), 막기 뒤 가운데 37초·구석 48초 (쌍검 구석 100초)
        double idleAvg, cornerAvg;
        {
            const int idleSeeds = 6;
            var idle = new Swarm[2][];
            for (int c = 0; c < 2; c++)
            {
                var runs = idle[c] = new Swarm[Arm.Kinds * idleSeeds];
                int camp = c == 0 ? 0 : 1;
                Parallel.For(0, runs.Length, i => runs[i] = BotGame((Arm.Id)(i / idleSeeds), 1000 + i % idleSeeds, 1, null, true, true, camp));
                for (int w = 0; w < Arm.Kinds; w++)
                {
                    var mine = runs.Skip(w * idleSeeds).Take(idleSeeds).ToArray();
                    Expect(mine.All(r => !r.Won && r.BotIdle), $"가만히 서 있으면({(c == 0 ? "가운데" : "구석")}) {KoNames[w]}도 5:00 에 못 닿는다: {string.Join(" ", mine.Select(r => (int)r.Time))}초");
                    Expect(mine.All(r => r.Time < 250), $"가만히 서 있으면({(c == 0 ? "가운데" : "구석")}) {KoNames[w]}도 4분을 못 넘긴다: {string.Join(" ", mine.Select(r => (int)r.Time))}초");
                }
            }
            idleAvg = idle[0].Average(r => r.Time);
            cornerAvg = idle[1].Average(r => r.Time);
            Expect(idleAvg < 70, $"가만히 서 있으면(가운데) 평균 70초를 못 넘긴다 (움직이는 봇은 270초쯤): {idleAvg:0}초");
            Expect(cornerAvg < 90, $"가만히 서 있으면(구석) 평균 90초를 못 넘긴다: {cornerAvg:0}초");
            Expect(idle[1].All(r => r.Burrows > 0), "구석에 서 있으면 흙더미가 놓인다");
        }

        // ── 가만히 있기 막기: 오래 서 있으면 땅에서 벌레가 올라온다 (예고 뒤에, 걸으면 처음부터, 벽에 키를 쥐어도 가만히) ──
        {
            Swarm Camper(int seed, Arm.Id id = Arm.Id.Twin) => new(TestField, 3, new Random(seed), id) { Spawning = false, CampOn = true, CampForce = true, ShoeChance = 0, MagnetChance = 0, HeartChance = 0, GrenadeChance = 0, SpecialsOn = false };
            var cs = Camper(11);
            cs.Weapon.Dmg = 0;   // 올라온 벌레를 칼이 바로 베지 않게 (이 검사는 올라오는 때만 본다)
            Run(cs, Swarm.CampAfter - .2);
            Expect(cs.Mounds.Count == 0 && cs.Burrows == 0 && cs.Still > Swarm.CampAfter - .3, $"{Swarm.CampAfter:0}초가 되기 전엔 흙더미가 없다: 선 지 {cs.Still:0.0}초, {cs.Burrows}개");
            Run(cs, .5);
            Expect(cs.Mounds.Count == 1 && cs.Burrows == 1 && cs.Foes.Count == 0, $"{Swarm.CampAfter:0}초를 넘기면 흙더미 하나가 놓인다 (아직 벌레는 없다): {cs.Mounds.Count}개, 벌레 {cs.Foes.Count}");
            var m0 = cs.Mounds[0];
            Expect(Math.Sqrt((m0.X - cs.X) * (m0.X - cs.X) + (m0.Y - cs.Y) * (m0.Y - cs.Y)) >= Swarm.MoundNear - .01 && m0.X > f8.Left && m0.X < f8.Right && m0.Y > f8.Top && m0.Y < f8.Bottom, "흙더미는 볼록이에게서 떨어져 판 안에 놓인다");
            Run(cs, Swarm.MoundSec - .6);
            Expect(cs.Foes.Count == 0 && cs.Mounds.Count == 1, $"흙더미는 0.8초 동안 그림으로만 알린다: 벌레 {cs.Foes.Count}");
            Run(cs, .45);
            var dirt = cs.Foes.Count == 1 ? cs.Foes[0] : null;
            Expect(dirt is { Dirt: true } && cs.Mounds.Count == 0 && Math.Abs(dirt.X - m0.X) < 20 && Math.Abs(dirt.Y - m0.Y) < 20, $"0.8초 뒤 흙더미 자리에서 벌레가 올라온다: 벌레 {cs.Foes.Count}");
            Run(cs, 1.6);
            Expect(cs.Burrows == 2, $"서 있는 동안 계속: 2.2초 뒤 둘째 흙더미: {cs.Burrows}개");

            // 걸으면 처음부터: 오래 걸어(2.5초 넘게 닻을 벗어나) 새 자리에서 다시 센다
            var cw = Camper(12);
            cw.CampPrime(Swarm.CampAfter - 1);
            cw.Steer(1, 0);
            Run(cw, 3.5);
            cw.Steer(0, 0);
            Expect(cw.Still < 1.6 && cw.Burrows == 0, $"오래 걸으면 선 초가 처음부터 다시 센다: {cw.Still:0.0}초, {cw.Burrows}개");
            Run(cw, Swarm.CampAfter - 2.4);
            Expect(cw.Burrows == 0, "걷고 난 뒤에는 처음부터 다시 세어 아직 흙더미가 없다");
            Run(cw, 3.5);
            Expect(cw.Burrows > 0, "다시 오래 서 있으면 또 놓인다");
            // 벽에 키를 쥐고 서 있어도 가만히다 (몸이 안 움직인다)
            var ch = Camper(13);
            ch.Steer(-1, -1);
            Run(ch, 6);
            Run(ch, Swarm.CampAfter + 3);
            Expect(ch.Burrows > 0 && ch.Clock - ch.MovedAt > 8, $"구석에서 키를 쥐고 서 있어도 가만히 서 있는 것: 흙더미 {ch.Burrows}개, 몸이 움직인 지 {ch.Clock - ch.MovedAt:0}초");
            for (int i = 0; i < ch.Mounds.Count; i++) Expect(ch.Mounds[i].X > f8.Left && ch.Mounds[i].X < f8.Right && ch.Mounds[i].Y > f8.Top && ch.Mounds[i].Y < f8.Bottom, "구석에서도 흙더미는 판 안에");
            // 잠깐 나갔다 와도 처음부터는 아니다 (보석 줍기): 쌓인 초가 줄어들 뿐
            var cg = Camper(14);
            cg.CampPrime(8);
            cg.Steer(1, 0);
            Run(cg, .4);
            cg.Steer(-1, 0);
            Run(cg, .4);
            cg.Steer(0, 0);
            Expect(cg.Still is > 5 and < 8, $"잠깐 나갔다 오면 쌓인 초가 줄 뿐 처음부터는 아니다: {cg.Still:0.0}초");
            // 구석에서 여럿 놓여도 모두 볼록이와 떨어져 판 안에
            var cc = Camper(15);
            (cc.X, cc.Y) = (f8.Left + 12, f8.Top + 26);
            cc.CampPrime(Swarm.CampAfter + 28);
            cc.Update(tick);
            Expect(cc.Mounds.Count == Swarm.MoundMax, $"오래 서 있으면 한 번에 {Swarm.MoundMax}개: {cc.Mounds.Count}");
            for (int i = 0; i < cc.Mounds.Count; i++)
            {
                var mq = cc.Mounds[i];
                Expect(Math.Sqrt((mq.X - cc.X) * (mq.X - cc.X) + (mq.Y - cc.Y) * (mq.Y - cc.Y)) >= Swarm.MoundNear - .01 && mq.X >= f8.Left && mq.X <= f8.Right && mq.Y >= f8.Top && mq.Y <= f8.Bottom, $"구석의 흙더미 {i} 도 볼록이와 떨어져 판 안에");
            }
            // 땅에서 올라온 벌레는 잡아도 아무것도 안 남긴다 (보통 벌레는 보석)
            var dg = Empty(16);
            Swarm.Foe bornDirt = Dummy(dg, Swarm.Kind.Bug, 60, 0, 1), plainBug = Dummy(dg, Swarm.Kind.Bug, -60, 0, 1);
            bornDirt.Dirt = true;
            Run(dg, .5);
            Expect(dg.Kills == 2 && dg.Gems.Count == 1, $"땅에서 올라온 벌레는 보석이 안 나온다 (보통 벌레는 나온다): 잡은 수 {dg.Kills}, 보석 {dg.Gems.Count}");
        }
        // 막기 켜짐·꺼짐: 같은 씨는 같은 판 (rng 를 안 써서 안 서 있는 판은 막기를 꺼도 같다)
        {
            var oa = BotGame(Arm.Id.Twin, 2002, 1, null, true, true, 1);
            var ob = BotGame(Arm.Id.Twin, 2002, 1, null, true, true, 1);
            Expect(oa.Time == ob.Time && oa.Burrows == ob.Burrows && oa.Kills == ob.Kills && oa.Burrows > 0, $"같은 씨는 같은 판 (구석에 서는 봇): {oa.Time:0.00}초 {oa.Burrows}개 / {ob.Time:0.00}초 {ob.Burrows}개");
            var walk = new Swarm[Arm.Kinds * 3];
            Parallel.For(0, walk.Length, i => walk[i] = BotGame((Arm.Id)(i / 3), 1000 + i % 3));
            double walkBurrows = walk.Average(r => r.Burrows);
            Expect(walkBurrows <= 3, $"걷는 봇은 흙더미가 거의 안 놓인다 (판당 평균 {walkBurrows:0.0}개, 서 있는 봇은 50개 넘게)");
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

        // 보스가 남기는 것: 보석 15·20·25·30 + 하트, 1·3번은 자석도, 여왕은 수류탄도 (든 것 + 판 위 것이 3 미만일 때)
        for (int tier = 1; tier <= Swarm.Bosses; tier++)
        {
            var kill = Empty(13);
            kill.Hp = 50;
            Dummy(kill, Swarm.Kind.Boss, 220, 0, 1, tier);
            Run(kill, .6);   // 잡은 직후 (보석은 날아오는 중이라 아직 안 주워졌다)
            Expect(kill.BossesKilled == 1 && kill.Gems.Count(m => m.Item == Swarm.Item.Heart) == 1 && kill.Gems.Count(m => m.Item == Swarm.Item.Magnet) == tier % 2 && kill.Gems.Count(m => m.Item == Swarm.Item.Grenade) == (tier == Swarm.Bosses ? 1 : 0) && kill.Gems.Any(m => m.Xp == 10 + 5 * tier),
                $"{tier}번 보스가 남기는 것: 보석 {kill.Gems.Max(m => m.Xp)}, 하트 {kill.Gems.Count(m => m.Item == Swarm.Item.Heart)}, 자석 {kill.Gems.Count(m => m.Item == Swarm.Item.Magnet)}, 수류탄 {kill.Gems.Count(m => m.Item == Swarm.Item.Grenade)}");
        }


        // ══ 단계와 1분 주기 (docs/swarm-levels.md): 2장 값, 1분 주기, 러시 대열, 마무리, 엘리트, 보석 ══

        // 단계 값: 2장 표 (소수 둘째 자리). 100단계에서도 상한 안. 빠르기 상한에서도 말벌은 볼록이보다 느리다
        foreach (var (lv, hp, spawn, boss, speed, elite, rush) in new[] {
            (1, 1.00, 1.00, 1.00, 1.000, 0.0, 2.5), (2, 1.07, 1.06, 1.10, 1.015, .03, 2.6), (3, 1.14, 1.12, 1.20, 1.030, .04, 2.7), (5, 1.28, 1.24, 1.40, 1.060, .06, 2.9),
            (7, 1.42, 1.36, 1.60, 1.090, .08, 3.0), (10, 1.53, 1.45, 1.78, 1.125, .12, 3.0), (15, 1.63, 1.53, 1.98, 1.175, .195, 3.0), (20, 1.73, 1.60, 2.18, 1.225, .20, 3.0) })   // 러시 칸은 손잡이 ① 값 (2.5 에서 단계마다 +0.1, 6단계부터 상한 3.0)
        {
            var tu = Swarm.Tune.Of(lv);
            Expect(Math.Abs(tu.HpX - hp) < .0051 && Math.Abs(tu.SpawnX - spawn) < .0051 && Math.Abs(tu.BossX - boss) < .0051 && Math.Abs(tu.SpeedX - speed) < .00051 && Math.Abs(tu.EliteP - elite) < .00051 && Math.Abs(tu.RushX - rush) < .0051,
                $"{lv}단계 값이 표와 같다: 체력 {tu.HpX:0.000}·수 {tu.SpawnX:0.000}·보스 {tu.BossX:0.000}·빠르기 {tu.SpeedX:0.0000}·엘리트 {tu.EliteP:0.0000}·러시 {tu.RushX:0.00}");
        }
        var cap100 = Swarm.Tune.Of(100);
        Expect(cap100.HpX == 1.8 && cap100.SpawnX == 1.6 && cap100.BossX == 2.5 && cap100.SpeedX == 1.25 && cap100.EliteP == .20 && cap100.RushX == 3.0 && Swarm.Tune.Of(0).HpX == 1, "100단계에서도 상한을 안 넘는다");
        var fastWasp = new Swarm(TestField, 3, new Random(1), Arm.Id.Pistols, 100) { Clock = Swarm.WinSec }.Add(Swarm.Kind.Wasp);
        Expect(fastWasp.Speed < Swarm.MoveSpeed && fastWasp.Speed > 140, $"빠르기 상한에서도 말벌 끝 속도가 볼록이보다 느리다: {fastWasp.Speed:0}");
        Expect(new Swarm(TestField, 3, new Random(1)).Elites == false && new Swarm(TestField, 3, new Random(1), null, 2).Elites, "엘리트는 2단계부터");

        // 1단계가 지금보다 세다: 0초 보통 벌레 체력 12, 1번 보스 400. 단계가 오르면 체력이 따라 오른다
        var st1 = new Swarm(TestField, 3, new Random(1), Arm.Id.Pistols);
        Expect(st1.Add(Swarm.Kind.Bug).MaxHp == 12 && st1.Add(Swarm.Kind.Wasp).MaxHp == 9 && st1.Add(Swarm.Kind.Beetle).MaxHp == 46 && st1.Add(Swarm.Kind.Boss, 0, 0, 1).MaxHp == 400 && st1.Add(Swarm.Kind.Boss, 0, 0, 4).MaxHp == 1900, "1단계 체력: 보통 12·말벌 9·딱정벌레 46, 보스 400…1900");
        var st10 = new Swarm(TestField, 3, new Random(1), Arm.Id.Pistols, 10);
        Expect(st10.Add(Swarm.Kind.Bug).MaxHp == 18 && st10.Add(Swarm.Kind.Boss, 0, 0, 1).MaxHp == 712 && st10.Add(Swarm.Kind.Bug).Speed > st1.Add(Swarm.Kind.Bug).Speed * 1.12, "10단계 체력·보스 체력·걸음이 오른다");

        // 같은 씨 = 같은 판: stage 는 rng 를 쓰지 않는다 (무기 뽑기가 같고, 1단계 두 번은 프레임까지 같다)
        for (int seed = 0; seed < 50; seed++) Expect(new Swarm(TestField, 3, new Random(seed), null, 1).Weapon.Which == new Swarm(TestField, 3, new Random(seed), null, 7).Weapon.Which, "단계가 달라도 같은 씨면 같은 무기");
        var tw1 = new Swarm(TestField, 3, new Random(77), Arm.Id.Rifle);
        var tw2 = new Swarm(TestField, 3, new Random(77), Arm.Id.Rifle, 1);
        for (int i = 0; i < 60 * 25; i++) { foreach (var twin in new[] { tw1, tw2 }) { twin.BotPlay(); twin.Update(tick); } }
        Expect((tw1.X, tw1.Y, tw1.Foes.Count, tw1.Kills, tw1.Born, tw1.Hp, tw1.Gems.Count) == (tw2.X, tw2.Y, tw2.Foes.Count, tw2.Kills, tw2.Born, tw2.Hp, tw2.Gems.Count) && tw1.Born > 30, $"1단계 두 번은 같은 판이다: 벌레 {tw1.Born}마리");

        // 1분 주기: 씨 하나로 300초를 돌려(볼록이 무적, 벌레는 프레임마다 치워 상한·보스가 수를 바꾸지 않게) 구간마다 나온 수를 센다
        var cyc = new Swarm(TestField, 3, new Random(21), Arm.Id.Pistols) { SafeUntil = 1e9 };
        var waveAt = new List<double>();
        var waveShape = new List<Swarm.Shape>();
        var waveSides = new List<int>();
        int rushEvents = 0;
        cyc.OnEvent = e =>
        {
            if (e == "rush") rushEvents++;
            if (e != "wave") return;
            waveAt.Add(cyc.Clock);
            waveShape.Add(cyc.RushShape);
            waveSides.Add(cyc.Foes.Select(f => f.X < TestField.Left ? 0 : f.X > TestField.Right ? 1 : f.Y < TestField.Top ? 2 : 3).Distinct().Count());
        };
        var perSec = new int[301];
        for (int prev = 0; cyc.Clock < Swarm.WinSec - .1 && !cyc.Done;)
        {
            cyc.Update(tick);
            perSec[Math.Min(300, (int)cyc.Clock)] += cyc.Born - prev;
            prev = cyc.Born;
            cyc.Foes.Clear();
        }
        int Sum(int a, int b) { int n = 0; for (int i = a; i <= b; i++) n += perSec[i]; return n; }
        Expect(waveAt.Count == 25 && rushEvents == 5, $"러시 다섯 번, 물결 스물다섯 번: {rushEvents}번 {waveAt.Count}물결");
        for (int i = 0; i < waveAt.Count; i++)
        {
            double want = i < 20 ? 60 * (i / 5) + 40 + 3 * (i % 5) : 240 + 40 + 4 * (i - 20);
            Expect(Math.Abs(waveAt[i] - want) < .05, $"물결은 40·43·46·49·52초(마지막 분은 4초마다)에 한 번씩: {i + 1}번째 {waveAt[i]:0.00} (기대 {want})");
        }
        for (int m = 0; m < 4; m++)
        {
            int rushed = Sum(60 * m + 40, 60 * m + 54), wantN = cyc.RushN(m);
            Expect(Math.Abs(rushed - wantN) <= Math.Max(2, wantN / 10), $"{m}:40 러시 15초 수가 rushN: {rushed} (기대 {wantN})");
            Expect(Sum(60 * m + 55, 60 * m + 58) <= 3, $"숨 고르기 55~58초는 3마리 이하: {Sum(60 * m + 55, 60 * m + 58)}");
            if (m >= 1)
            {
                double calm = Sum(60 * m + 20, 60 * m + 34) / 15.0, warn = Sum(60 * m + 35, 60 * m + 39) / 5.0;
                Expect(warn > calm * .35 && warn < calm * .65, $"예고는 잔잔의 절반쯤: 초당 {calm:0.00} → {warn:0.00}");
            }
        }
        Expect(Enumerable.Range(0, 4).All(m => cyc.RushN(m) == (int)Math.Round(15 * (Swarm.BaseFrom + Swarm.BaseGrow * (40 + 60 * m) / 300) * Swarm.Tune.RushBase, MidpointRounding.AwayFromZero)) && cyc.RushN(3) > cyc.RushN(0), $"1단계 러시는 15 × base1(40+60m) × rushX: {cyc.RushN(0)}·{cyc.RushN(1)}·{cyc.RushN(2)}·{cyc.RushN(3)}");
        Expect(cyc.Born is >= 1600 and <= 2200, $"한 판에 나오는 벌레 수 (계획 1,020 → 잔잔 8.2·러시 2.5, 러시 벌레는 체력 절반이라 수가 많다): {cyc.Born}");
        Expect(waveShape[0] == Swarm.Shape.Wall && waveSides[0] == 1 && waveShape.Skip(20).All(sh => sh == Swarm.Shape.Ring) && waveShape.Take(20).Distinct().Count() == 2, $"첫 러시 첫 물결은 한쪽 줄, 마지막 분은 늘 Ring: {string.Join("", waveShape.Select(sh => sh == Swarm.Shape.Wall ? 'W' : 'R'))}");
        for (int i = 2; i < 20; i++) Expect(!(waveShape[i] == waveShape[i - 1] && waveShape[i] == waveShape[i - 2]), "같은 모양이 세 번 이어지지 않는다");

        // 러시는 spawnX 를 곱하지 않는다: 10단계 0:40 러시는 83, 9단계와 20단계의 러시 수가 같다
        var r10 = new Swarm(TestField, 3, new Random(22), Arm.Id.Pistols, 10) { SafeUntil = 1e9 };
        int rush10 = 0;
        for (int prev = 0; r10.Clock < 56 && !r10.Done;)
        {
            r10.Update(tick);
            if (r10.Clock >= 40 && r10.Clock < 55) rush10 += r10.Born + r10.DartsBorn - prev;   // 돌진 말벌은 물결 마릿수 2 로 친다
            prev = r10.Born + r10.DartsBorn;
            r10.Foes.Clear();
        }
        int rush10Want = (int)Math.Round(15 * (Swarm.BaseFrom + Swarm.BaseGrow * 40 / 300) * 3.0, MidpointRounding.AwayFromZero);   // spawnX(1.45)를 곱하면 이만큼이 아니다
        Expect(Math.Abs(rush10 - rush10Want) <= Math.Max(2, rush10Want / 10) && r10.RushN(0) == rush10Want, $"10단계 0:40 러시는 spawnX 없이 {rush10Want}: {rush10}");
        var r9 = new Swarm(TestField, 3, new Random(1), Arm.Id.Pistols, 30);
        var r20 = new Swarm(TestField, 3, new Random(1), Arm.Id.Pistols, 100);
        Expect(Enumerable.Range(0, 4).All(m => r9.RushN(m) == r20.RushN(m)) && Swarm.Tune.Of(6).RushX == 3.0 && Swarm.Tune.Of(5).RushX < 3.0, "러시가 상한(×3.0)에 닿은 뒤에는 단계가 올라도 수가 같다 (spawnX 를 곱하지 않는다)");

        // 대열: Ring 은 둘레 25%(마무리 33%)를 비운다, Wall 은 한 변에서만 (넘치면 바깥 줄), 모두 볼록이 150px 안에는 없다
        foreach (double holeLen in new[] { Swarm.RingHole, Swarm.FinaleHole })
        {
            var rg = Empty(23);
            rg.Wave(Swarm.Shape.Ring, 100, 0, .30, holeLen, 9999);
            var ts = rg.Foes.Select(f => rg.RingAt(f.X, f.Y)).OrderBy(t => t).ToList();
            double widest = 0, center = 0;
            for (int i = 0; i < ts.Count; i++)
            {
                double next = i + 1 < ts.Count ? ts[i + 1] : ts[0] + 1, gap = next - ts[i];
                if (gap > widest) (widest, center) = (gap, (ts[i] + gap / 2) % 1);
            }
            Expect(rg.Foes.Count == 100 && widest >= holeLen && widest < holeLen + .02 && Math.Abs(center - .30) < .01 && ts.Zip(ts.Skip(1), (a, b) => b - a).Count(g => g > .02) == 1, $"Ring 은 둘레의 {holeLen:P0} 를 비우고 나머지는 고르다: 구멍 {widest:P1} 가운데 {center:0.00}");
        }
        foreach (int wside in new[] { 0, 1, 2, 3 })
        {
            var wl = Empty(23);
            wl.Wave(Swarm.Shape.Wall, 40, wside, 0, 0, 9999);
            int SideOf(Swarm.Foe f) => f.X < TestField.Left ? 0 : f.X > TestField.Right ? 1 : f.Y < TestField.Top ? 2 : f.Y > TestField.Bottom ? 3 : -1;
            var rows = wl.Foes.Select(f => wside < 2 ? Math.Abs(f.X - (wside == 0 ? TestField.Left : TestField.Right)) : Math.Abs(f.Y - (wside == 2 ? TestField.Top : TestField.Bottom))).Select(d => (int)Math.Round(d / 6)).Distinct().Count();
            Expect(wl.Foes.Count == 40 && wl.Foes.All(f => SideOf(f) == wside) && rows == 3 - (wside >= 2 ? 1 : 0) && wl.Foes.Select(f => (f.X, f.Y)).Distinct().Count() == 40, $"Wall 은 한 변에서만, 넘치면 둘째·셋째 줄: 변 {wside}, 줄 {rows}");
        }
        var hug2 = Empty(23);
        hug2.X = TestField.Left + 50;
        hug2.Wave(Swarm.Shape.Wall, 30, 0, 0, 0, 9999);
        hug2.Wave(Swarm.Shape.Ring, 60, 0, .1, .25, 9999);
        Expect(hug2.Foes.Count is > 20 and < 90 && hug2.Foes.All(f => Math.Sqrt((f.X - hug2.X) * (f.X - hug2.X) + (f.Y - hug2.Y) * (f.Y - hug2.Y)) >= 150), $"대열은 볼록이 150px 안쪽 자리를 건너뛴다: {hug2.Foes.Count}마리");
        var cap = Empty(23);
        Expect(cap.Wave(Swarm.Shape.Ring, 60, 0, .1, .25, 40) == 20 && cap.Foes.Count == 40, "상한에 막힌 수는 돌려준다 (다음 물결로 넘긴다)");

        // 마무리: 여왕이 살아 있으면 4:40~5:00 에 rushN(3)×0.7 = 90, 잡았으면 128. 항상 Ring, 구멍은 여왕 반대쪽, 여왕은 새끼를 안 낳는다
        (int Born, List<double> First, bool Brood) Finale(bool alive, int qdx, int qdy)
        {
            var fn = new Swarm(TestField, 3, new Random(24), Arm.Id.Pistols) { SafeUntil = 1e9 };
            fn.SetClock(279.95);
            Swarm.Foe? queen = null;
            if (alive)
            {
                queen = fn.Add(Swarm.Kind.Boss, fn.X + qdx, fn.Y + qdy, Swarm.Bosses);
                (queen.Hp, queen.MaxHp, queen.Speed, queen.DashAt, queen.SpawnDue) = (999999, 999999, 0, 1e9, 280.5);
            }
            int start = fn.Born;
            var first = new List<double>();
            bool brood = false, got = false;
            fn.OnEvent = e => { if (e == "wave" && !got) { got = true; foreach (var f in fn.Foes) if (f != queen) first.Add(fn.RingAt(f.X, f.Y)); } };
            while (fn.Clock < Swarm.WinSec - .15)
            {
                fn.Update(tick);
                if (queen != null) brood |= fn.Foes.Any(f => f != queen && Math.Abs(f.X - queen.X) < 60 && Math.Abs(f.Y - queen.Y) < 60);
                fn.Foes.RemoveAll(f => f != queen);
            }
            return (fn.Born - start, first, brood);
        }
        double GapCenter(List<double> ts)
        {
            ts = ts.OrderBy(t => t).ToList();
            double widest = 0, center = 0;
            for (int i = 0; i < ts.Count; i++)
            {
                double next = i + 1 < ts.Count ? ts[i + 1] : ts[0] + 1, gap = next - ts[i];
                if (gap > widest) (widest, center) = (gap, (ts[i] + gap / 2) % 1);
            }
            return center;
        }
        var finRef = new Swarm(TestField, 3, new Random(1));
        var finAlive = Finale(true, -200, 0);
        var ringProbe = Empty(1);
        double rightMid = ringProbe.RingAt(TestField.Right + 14, ringProbe.Y), bottomMid = ringProbe.RingAt(ringProbe.X, TestField.Bottom + 14);
        double CircDist(double a, double b) { double d = Math.Abs(a - b) % 1; return Math.Min(d, 1 - d); }
        Expect(Math.Abs(finAlive.Born - (int)Math.Round(finRef.RushN(3) * .7, MidpointRounding.AwayFromZero)) <= 1 && !finAlive.Brood && CircDist(GapCenter(finAlive.First), rightMid) < .045, $"여왕이 살아 있으면 마무리는 rushN(3)×0.7 = {finRef.RushN(3) * .7:0}, 구멍은 여왕 반대쪽, 새끼는 안 낳는다: {finAlive.Born}마리, 구멍 {GapCenter(finAlive.First):0.00} (기대 {rightMid:0.00})");
        var finAbove = Finale(true, 0, -150);
        Expect(CircDist(GapCenter(finAbove.First), bottomMid) < .045, $"여왕이 위에 있으면 구멍은 아래: {GapCenter(finAbove.First):0.00} (기대 {bottomMid:0.00})");
        var finFree = Finale(false, 0, 0);
        Expect(Math.Abs(finFree.Born - finRef.RushN(3)) <= 1, $"여왕을 잡았으면 마무리는 rushN(3)×1.0 = {finRef.RushN(3)}: {finFree.Born}");
        var brood2 = Empty(12);
        (brood2.Weapon.Range, brood2.SafeUntil) = (0, 1e9);
        brood2.SetClock(279);
        var q2 = brood2.Add(Swarm.Kind.Boss, brood2.X + 320, brood2.Y, Swarm.Bosses);
        q2.SpawnDue = 280.5;
        Run(brood2, 8);
        Expect(brood2.Foes.Count == 1, "마무리 동안 여왕은 벌레를 낳지 않는다 (4:40 전에는 낳는다)") ;

        // 상한: 20단계 300초 (볼록이 무적·자동 조종) 동안 벌레는 110(+여왕 새끼 4)을 안 넘고, 여왕이 살아 있는 마무리는 90 위로 늘지 않는다
        var big20 = new Swarm(TestField, 3, new Random(25), Arm.Id.Pistols, 20) { SafeUntil = 1e9 };
        big20.Weapon.Dmg = 1;   // 약한 무기로 벌레가 쌓이게 (상한을 시험한다)
        int most = 0, grewPast90 = 0;
        for (int prev = 0; !big20.Done && big20.Clock < Swarm.WinSec;)
        {
            var (cx, cy) = big20.BotSteer();
            big20.Steer(cx, cy);
            big20.Update(tick);
            most = Math.Max(most, big20.Foes.Count);
            if (big20.Clock >= Swarm.FinaleFrom && big20.Foes.Count > prev && big20.Foes.Count > Swarm.FinaleCap && big20.Foes.Any(f => f.Kind == Swarm.Kind.Boss && f.Tier == Swarm.Bosses)) grewPast90++;
            prev = big20.Foes.Count;
        }
        Expect(most <= Swarm.MaxFoes + 4 && most > 100 && grewPast90 == 0, $"20단계 300초 벌레 수 최대 {most}, 여왕이 살아 있는 마무리에 90 위로 는 횟수 {grewPast90}");

        // 엘리트: 1단계에는 없고, 2단계부터 30초 뒤에 나온다 (동시에 6마리까지, 10단계부터 8). 체력 ×2.5·피해 ×1.5·걸음 ×0.9, 잡으면 보석 5(딱정벌레 8)·하트
        var noElite = new Swarm(TestField, 3, new Random(26), Arm.Id.Flame, 1) { SafeUntil = 1e9 };
        var withElite = new Swarm(TestField, 3, new Random(26), Arm.Id.Flame, 6) { SafeUntil = 1e9 };
        int eliteSeen = 0, eliteMax = 0;
        double eliteFirst = 999;
        while (!withElite.Done && withElite.Clock < Swarm.WinSec)
        {
            foreach (var e in new[] { noElite, withElite })
            {
                var (ex, ey) = e.BotSteer();
                e.Steer(ex, ey);
                e.Update(tick);
            }
            eliteSeen += noElite.Foes.Count(f => f.Elite);
            int live = withElite.Foes.Count(f => f.Elite);
            eliteMax = Math.Max(eliteMax, live);
            if (live > 0) eliteFirst = Math.Min(eliteFirst, withElite.Clock);
        }
        Expect(eliteSeen == 0 && eliteMax is > 0 and <= 6 && eliteFirst >= Swarm.EliteFrom && Swarm.EliteMax(10) == 8 && Swarm.EliteMax(9) == 6, $"엘리트: 1단계 {eliteSeen}마리, 6단계 최대 {eliteMax}마리·처음 {eliteFirst:0}초 (30초 전에는 없다)");
        var el = Empty(27, Arm.Id.Pistols, 2);
        el.Clock = 100;
        var norm = el.Add(Swarm.Kind.Bug);
        var elite1 = el.MakeElite(el.Add(Swarm.Kind.Bug));
        Expect(elite1.Elite && !norm.Elite && elite1.MaxHp == (int)Math.Round(norm.MaxHp * 2.5, MidpointRounding.AwayFromZero) && Swarm.TouchDmg(elite1) == 15 && Math.Abs(elite1.Speed - norm.Speed * .9) < 1e-9, $"엘리트는 체력 ×2.5·피해 ×1.5·걸음 ×0.9: 체력 {norm.MaxHp}→{elite1.MaxHp}, 피해 {Swarm.TouchDmg(elite1)}");
        el.Foes.Clear();
        el.Hp = 50;
        el.EliteHeartChance = 1;
        foreach (var (kind, dx, gem) in new[] { (Swarm.Kind.Bug, 150, 5), (Swarm.Kind.Beetle, -150, 8) })
        {
            el.Gems.Clear();
            var e1 = Dummy(el, kind, dx, 0, 1);
            el.MakeElite(e1);
            e1.Hp = 1;
            Run(el, 1);
            Expect(el.Gems.Any(m => m.Item == Swarm.Item.Gem && m.Xp == gem) && el.Gems.Any(m => m.Item == Swarm.Item.Heart), $"엘리트 {kind} 는 보석 {gem} 과 하트를 남긴다");
        }

        // 보석: 값 2 보석이 hpX−1 쯤 (500마리), 상한에서 합쳐 총 값이 안 줄고, 보스를 잡으면 먼 보석이 끌려온다
        foreach (var (lv, want) in new[] { (1, 500), (5, 640) })
        {
            var gv = Empty(28, Arm.Id.Bazooka, lv);
            (gv.Weapon.Blast, gv.Weapon.Dmg) = (5000, 999);
            var spots = new Random(3);
            for (int i = 0; i < 500; i++)
            {
                double a = spots.NextDouble() * 2 * Math.PI, d = 110 + spots.NextDouble() * 170;
                Dummy(gv, Swarm.Kind.Bug, Math.Cos(a) * d, Math.Sin(a) * d * .7, 5);
            }
            Run(gv, 1);
            int total = gv.Gems.Where(m => m.Item == Swarm.Item.Gem).Sum(m => m.Xp) + gv.Xp;
            Expect(gv.Kills == 500 && gv.Gems.Count <= Swarm.MaxGems && Math.Abs(total - want) <= want / 20, $"{lv}단계 보석 500마리: 총 값 {total} (기대 {want}), 보석 {gv.Gems.Count}개");
        }
        var merge = Empty(29);
        for (int i = 0; i < Swarm.MaxGems; i++) merge.Gems.Add(new Swarm.Gem { X = merge.X + 200 + i % 5, Y = merge.Y + 120, Xp = 1 });
        Dummy(merge, Swarm.Kind.Bug, 150, 0, 1);
        Run(merge, .6);
        Expect(merge.Kills == 1 && merge.Gems.Count == Swarm.MaxGems && merge.Gems.Sum(m => m.Xp) + merge.Xp == Swarm.MaxGems + 1 && merge.Gems.Any(m => m.Xp == 2) && merge.Gone.Count >= 0, $"보석 151개째에 총 값이 줄지 않는다 (합쳐진다): {merge.Gems.Sum(m => m.Xp)}");
        var pull = Empty(30);
        pull.Gems.Add(new Swarm.Gem { X = pull.X + 280, Y = pull.Y + 60, Xp = 40 });
        Dummy(pull, Swarm.Kind.Bug, -150, 0, 1);
        Run(pull, 1.5);
        Expect(pull.Gems.Any(m => m.Xp == 40) && pull.Level == 1, "보통 벌레를 잡아서는 먼 보석이 끌려오지 않는다");
        Dummy(pull, Swarm.Kind.Boss, 150, 0, 1, 1);
        Run(pull, 2.5);
        Expect(pull.BossesKilled == 1 && pull.Gems.All(m => m.Xp != 40) && pull.Level > 1, $"보스를 잡으면 먼 보석이 모두 날아온다: 레벨 {pull.Level}");

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

        // ── 단계 기록: 버티면 다음 단계·깬 단계·무기별 최고 단계, 지면·보스 키·연습 판·자동 조종 판은 그대로. 옛 기록은 1단계부터 ──
        Expect(old.stage == 1 && old.maxStage == 0 && old.stageBest.Count == 0 && rec.stage == 2 && rec.maxStage == 1 && rec.stageBest["pistols"] == 1, $"옛 기록은 1단계부터, 이긴 판은 한 단계 올린다: 기록 {rec.stage}단계, 깬 {rec.maxStage}");
        Swarm Finished(int stg, bool win, double clock = Swarm.WinSec - 5)
        {
            var f = new Swarm(TestField, 3, new Random(6), Arm.Id.Pistols, stg) { SafeUntil = win ? 1e9 : 0, Clock = clock };
            if (win) Run(f, 5.1);
            else { f.Hp = 1; Dummy(f, Swarm.Kind.Boss, 3, 0); Run(f, 1.5); }
            return f;
        }
        var adv = new SwarmGame(winHost);
        adv.LoadState("{\"Sec\":10,\"Kills\":5,\"Stage\":3,\"MaxStage\":2,\"StageBest\":{\"pistols\":2}}");
        Expect(adv.stage == 3 && adv.maxStage == 2 && adv.stageBest["pistols"] == 2, "단계 칸을 읽는다");
        void Finish(Swarm f, bool quit = false) { (adv.swarm, adv.ses) = (f, Session.Fake(TestField)); winHost.Pet.StartPuppet(); adv.Stop(quit); }
        Finish(Finished(3, true));
        Expect(adv.stage == 4 && adv.maxStage == 3 && adv.stageBest["pistols"] == 3, $"3단계를 버티면 4단계: {adv.stage}·{adv.maxStage}");
        Finish(Finished(2, true));
        Expect(adv.stage == 4 && adv.maxStage == 3 && adv.stageBest["pistols"] == 3, "지금 단계가 아닌 판을 이겨도 단계는 그대로");
        var lost = Finished(4, false);
        Expect(lost.State != Swarm.Phase.Play && !lost.Won, "진 판");
        Finish(lost);
        Expect(adv.stage == 4 && adv.maxStage == 3, "지면 단계는 그대로 (벌 없음)");
        Finish(new Swarm(TestField, 3, new Random(1), Arm.Id.Pistols, 4), quit: true);
        Expect(adv.stage == 4 && adv.maxStage == 3, "판 도중 보스 키(Esc)로 끝내면 단계는 그대로");
        adv.practice = true;
        Finish(Finished(4, true));
        Expect(adv.stage == 4 && adv.maxStage == 3 && !adv.practice, "디버그로 단계를 정한 판(practice)은 안 남는다");
        adv.auto = true;
        Finish(Finished(4, true));
        Expect(adv.stage == 4 && adv.maxStage == 3 && !adv.auto, "자동 조종 판은 안 남는다");
        Finish(Finished(4, true));
        Expect(adv.stage == 5 && adv.maxStage == 4 && adv.Honor(Finished(1, true)) >= 4, "그 판을 버티면 5단계");
        var escWin = new SwarmGame(winHost);
        escWin.LoadState(adv.SaveState());
        (escWin.swarm, escWin.ses) = (Finished(5, true), Session.Fake(TestField)); winHost.Pet.StartPuppet(); escWin.Stop(true);
        Expect(escWin.stage == 6 && escWin.maxStage == 5, $"5:00 을 버틴 뒤 결과 화면에서 Esc 를 눌러도 단계는 오른다: {escWin.stage}");
        var round = new SwarmGame(winHost);
        round.LoadState(adv.SaveState());
        Expect((round.stage, round.maxStage) == (adv.stage, adv.maxStage) && round.stageBest["pistols"] == 4 && round.bestSec == adv.bestSec, "저장 → 읽기 왕복이 같다 (단계 칸 포함)");
        round.LoadState("{\"Sec\":1,\"Kills\":1,\"Stage\":0,\"MaxStage\":-4}");
        Expect(round.stage == 1 && round.maxStage == 0, "깨진 단계 칸은 1단계·0 으로");
        var onlyStage = new SwarmGame(winHost);
        onlyStage.LoadState("{\"Sec\":0,\"Kills\":0,\"Stage\":6,\"MaxStage\":5}");
        Expect(onlyStage.SaveState() != null && onlyStage.Stats().Single().Played == false, "단계만 있는 기록도 저장된다");

        // ── 글: 새 키 game.swarm_val2 가 열 언어에 다 있고, 옛 키는 없다 ──
        foreach (var (code, _) in L.Languages)
            Expect(L.Has("game.swarm_val2", code) && L.Raw("game.swarm_val2", code).Contains("{2}") && L.Raw("game.swarm_val2", code).Contains("{0}") && L.Raw("game.swarm_val2", code).Contains("{1}"), $"game.swarm_val2 가 {code} 에 있다");
        Expect(!L.Has("game.swarm_val", "en"), "옛 키 game.swarm_val 은 지웠다");

        // ── 자동 조종: 무기 일곱 × 씨 둘. 모두 끝나고, 무기마다 1분은 버티고, 수 한도 안 ──
        const int seeds = 2;
        var bots = new Swarm[Arm.Kinds * seeds];
        var over = new string?[bots.Length];
        Parallel.For(0, bots.Length, i => bots[i] = BotGame((Arm.Id)(i / seeds), i % seeds, 1, s =>
        {
            if (TestField.Contains((int)s.X, (int)s.Y) && s.Foes.Count <= Swarm.MaxFoes + 4 && s.Shots.Count <= Swarm.MaxShots && s.Gems.Count <= Swarm.MaxGems && s.Hp <= Swarm.MaxHp && s.Magnets <= Swarm.MaxMagnets && s.Grenades <= Swarm.MaxGrenades && s.Nades.Count <= Swarm.MaxNades && s.Bombs.Count <= 4 && s.Fires.Count <= Arm.Spec.MaxFires) return true;
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
        for (int w = 0; w < Arm.Kinds; w++)   // 특수기가 있는 무기는 봇 판마다 저절로 나온다 (장검·소총은 없다)
        {
            int total = bots.Skip(w * seeds).Take(seeds).Sum(s => s.Specials), want = (Arm.Id)w == Arm.Id.Bazooka ? 5 : 10;
            Expect(Arm.New((Arm.Id)w).Every > 0 ? total >= want : total == 0, $"자동 조종 판에서 특수기가 나온다 ({KoNames[w]}): {total}번");
        }
        Expect(bots.Sum(s => s.Magnets) >= Arm.Kinds && bots.Sum(s => s.Shoes) >= Arm.Kinds, $"자동 조종도 자석·신발을 줍는다: 자석 {bots.Sum(s => s.Magnets)}개, 신발 {bots.Sum(s => s.Shoes)}켤레");
        Expect(bots.Sum(s => s.Thrown) >= Arm.Kinds && bots.Sum(s => s.NadeKills) > 0, $"자동 조종도 수류탄을 던진다: 던진 수 {bots.Sum(s => s.Thrown)}, 잡은 수 {bots.Sum(s => s.NadeKills)}");

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

        // ══ 수류탄 (docs/swarm-grenade.md 8장) ══
        // 시작 1개·공중 0·저장 글에 없음 / 소리
        var nd0 = Empty(40);
        Expect(nd0.Grenades == 1 && nd0.Nades.Count == 0 && nd0.Thrown == 0 && nd0.NadeKills == 0 && Swarm.StartGrenades == 1 && Swarm.MaxGrenades == 3, "시작하면 수류탄 1개, 공중 0");
        var ndSave = new SwarmGame(host);
        ndSave.LoadState("{\"Sec\":10,\"Kills\":5}");
        Expect(ndSave.SaveState() is { } ndJson && !ndJson.Contains("renade", StringComparison.OrdinalIgnoreCase) && !ndJson.Contains("Thrown"), "저장 글에 수류탄이 없다 (그 판뿐)");
        Expect(Sounds["grenade"] == Sound.Pick && Sounds["throw"] == Sound.Tap && Sounds["noammo"] == Sound.Tap && Sounds["nade"] == Sound.Boom, "수류탄 소리: 줍기·던지기·빈 손·터짐");

        // 방향: 조종 값 쪽 120px (대각선은 85·85), 서 있으면 마지막으로 움직인 쪽, 한 번도 안 움직였으면 Dir 쪽, 벽 앞에서는 판 안으로 잘린다
        foreach (var (ndx, ndy) in new[] { (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1) })
        {
            var ndDir = Empty(41);
            ndDir.Steer(ndx, ndy);
            double ndLen = Math.Sqrt(ndx * ndx + ndy * ndy);
            Expect(ndDir.Throw() && ndDir.Nades.Count == 1 && Math.Abs(ndDir.Nades[0].X1 - ndDir.Nades[0].X0 - ndx / ndLen * 120) < .01 && Math.Abs(ndDir.Nades[0].Y1 - ndDir.Nades[0].Y0 - ndy / ndLen * 120) < .01 && ndDir.Nades[0].Spin == (ndx < 0 ? -1 : 1),
                $"({ndx},{ndy}) 쪽으로 120px 던진다: {ndDir.Nades[0].X1 - ndDir.Nades[0].X0:0.0}, {ndDir.Nades[0].Y1 - ndDir.Nades[0].Y0:0.0}");
        }
        var ndFace = Empty(42);
        ndFace.Steer(0, -1);
        ndFace.Update(tick);
        ndFace.Steer(0, 0);
        ndFace.Update(tick);
        Expect(ndFace.Throw() && Math.Abs(ndFace.Nades[0].X1 - ndFace.Nades[0].X0) < .01 && Math.Abs(ndFace.Nades[0].Y0 - ndFace.Nades[0].Y1 - 120) < .01, "움직이다 멈추면 마지막으로 움직인 쪽 (위)");
        var ndStill = Empty(42);
        Expect(ndStill.Throw() && ndStill.Nades[0].X1 - ndStill.Nades[0].X0 == 120 && ndStill.Nades[0].Y1 == ndStill.Nades[0].Y0, "한 번도 안 움직였으면 오른쪽");
        var ndLeft = Empty(42);
        ndLeft.Dir = -1;
        Expect(ndLeft.Throw() && ndLeft.Nades[0].X1 - ndLeft.Nades[0].X0 == -120 && ndLeft.Nades[0].Spin == -1, "왼쪽을 보게 한 판은 왼쪽 (거꾸로 돈다)");
        var ndWall = Empty(43);
        ndWall.X = TestField.Right - 14;
        ndWall.Steer(1, 0);
        Expect(ndWall.Throw() && ndWall.Nades[0].X1 == TestField.Right - 12 && ndWall.Nades[0].X1 - ndWall.Nades[0].X0 < 120, "벽 앞에서는 떨어질 자리가 판 안으로 잘린다");

        // 수·쿨다운·빈 손
        var ndCool = Empty(44);
        ndCool.Grenades = 3;
        string ndHeard = "";
        ndCool.OnEvent = e => ndHeard += e + " ";
        Expect(ndCool.Throw() && ndCool.Grenades == 2 && ndCool.Thrown == 1 && ndCool.ThrowAt == ndCool.Clock && ndCool.GrenadeAt == ndCool.Clock && ndHeard == "throw ", "던지면 3 → 2");
        Run(ndCool, .3);
        Expect(!ndCool.Throw() && ndCool.Grenades == 2 && ndCool.Thrown == 1 && ndCool.Nades.Count == 1, "0.6초 안의 둘째는 조용히 무시된다 (수 그대로)");
        Run(ndCool, .31);
        Expect(ndCool.Throw() && ndCool.Grenades == 1 && ndCool.Thrown == 2, "0.6초 뒤엔 나간다");
        var ndNone = Empty(45);
        ndNone.Grenades = 0;
        ndHeard = "";
        ndNone.OnEvent = e => ndHeard += e + " ";
        Run(ndNone, .5);
        Expect(!ndNone.Throw() && ndNone.Nades.Count == 0 && ndNone.Thrown == 0 && ndNone.EmptyAt == ndNone.Clock && ndHeard == "noammo ", "0개면 안 나가고 noammo 사건 + EmptyAt");

        // 입력: 누르는 순간만 (꾹 눌러도 한 번), 앞 창일 때만, Ctrl 을 누른 채 시작하면 안 나간다, Shift 로는 아무 일도 없다, 던진 직후 Esc 는 바로 끝
        var ndKeys = new HashSet<Keys> { Keys.ControlKey };
        bool ndFront = true;
        var ndIn = Empty(46);
        game.Begin(ndIn);
        ndIn.OnEvent = null;
        (game.swarm, game.ses) = (ndIn, Session.Fake(TestField, ndKeys.Contains, () => ndFront));
        host.Pet.StartPuppet();
        Frames(20);
        Expect(ndIn.Thrown == 0 && ndIn.Grenades == 1, "Ctrl 을 누른 채 시작해도 안 나간다");
        ndKeys.Clear();
        Frames(3);
        ndKeys.Add(Keys.ControlKey);
        Frames(20);
        Expect(ndIn.Thrown == 1 && ndIn.Grenades == 0 && ndIn.Nades.Count + (ndIn.NadeBoomAt > 0 ? 1 : 0) >= 1, $"Ctrl 을 20프레임 누르고 있어도 한 번: {ndIn.Thrown}");
        ndIn.Grenades = 3;
        Frames(20);
        Expect(ndIn.Thrown == 1 && ndIn.Grenades == 3, "꾹 누르고 있으면 둘째는 안 나간다");
        ndKeys.Clear();
        Frames(45);
        ndKeys.Add(Keys.ControlKey);
        Frames(3);
        Expect(ndIn.Thrown == 2 && ndIn.Grenades == 2, "뗐다 다시 누르면 둘째");
        (ndFront, ndIn.Grenades) = (false, 3);
        ndKeys.Clear();
        Frames(45);
        ndKeys.Add(Keys.ControlKey);
        Frames(20);
        Expect(ndIn.Thrown == 2 && ndIn.Grenades == 3, "판이 앞 창이 아니면 Ctrl 로 안 나간다");
        (ndFront, ndIn.Grenades) = (true, 3);
        ndKeys.Clear();
        Frames(45);
        ndKeys.Add(Keys.ShiftKey);
        Frames(20);
        Expect(ndIn.Thrown == 2 && ndIn.Grenades == 3 && ndIn.Nades.Count == 0, "Shift 로는 아무 일도 없다 (2차 보상 몫)");
        ndKeys.Clear();
        Frames(45);
        ndKeys.Add(Keys.ControlKey);
        Frames(2);
        Expect(ndIn.Thrown == 3 && ndIn.Nades.Count == 1, "공중에 하나 떠 있다");
        game.Stop(true);
        Expect(!game.Running && host.Pet.Mode == Mode.Wander && !host.Pet.Emoting, "던진 직후 Esc 도 바로 끝난다 (남는 것 없음)");

        // 폭발: 0.55초 뒤 떨어진 자리에서 터진다. 반지름(80 + 몸) 안만 70 깎이고 밀침 30·멈칫, 보스는 절반(35)·밀침 ¼·멈칫 없음. 볼록이는 안 다친다
        var ndBoom = Empty(47);
        ndBoom.Weapon.Range = 0;   // 무기가 과녁을 건드리지 않게
        Swarm.Foe ndHit = Dummy(ndBoom, Swarm.Kind.Bug, 150, -40, 100), ndBoss = Dummy(ndBoom, Swarm.Kind.Boss, 120, 30, 5000), ndIn88 = Dummy(ndBoom, Swarm.Kind.Bug, 208, 0, 100), ndOut92 = Dummy(ndBoom, Swarm.Kind.Bug, 120, 92, 100), ndFar = Dummy(ndBoom, Swarm.Kind.Bug, -150, 0, 100);
        double ndLx = ndBoom.X + 120, ndLy = ndBoom.Y;
        double NdDist(Swarm.Foe f, double ax, double ay) => Math.Sqrt((f.X - ax) * (f.X - ax) + (f.Y - ay) * (f.Y - ay));
        Expect(ndBoom.Throw(), "던진다");
        Run(ndBoom, .5);
        Expect(ndBoom.Nades.Count == 1 && ndHit.Hp == 100 && ndBoom.NadeBoomAt < 0 && ndBoom.Pops.Count == 0, "0.55초가 되기 전에는 안 터진다");
        Run(ndBoom, .1);
        Expect(ndBoom.Nades.Count == 0 && ndBoom.NadeBoomAt > 0 && ndBoom.BoomAt < 0 && ndBoom.Pops.Count == 1 && Math.Abs(ndBoom.Pops[0].R - Swarm.NadeBlast) < .01 && Math.Abs(ndBoom.Pops[0].X - ndLx) < .01, "떨어지면 터진다: 터짐 그림, 바주카의 BoomAt 은 그대로");
        Expect(ndHit.Hp == 100 - ndBoom.NadeDamage(false) && ndHit.Hp == 30 && ndIn88.Hp == 30 && ndOut92.Hp == 100 && ndFar.Hp == 100, $"반지름 안만 깎인다: {ndHit.Hp}·{ndIn88.Hp} / 밖 {ndOut92.Hp}·{ndFar.Hp}");
        Expect(Math.Abs(NdDist(ndHit, ndLx, ndLy) - 80) < 1 && ndHit.StunUntil > ndBoom.Clock + .3 && Math.Abs(ndHit.StunUntil - ndBoom.NadeBoomAt - Swarm.NadeStun) < .02, $"보통 벌레는 30px 밀리고 0.4초 멈칫: {NdDist(ndHit, ndLx, ndLy):0.0}");
        Expect(ndBoss.Hp == 5000 - ndBoom.NadeDamage(true) && ndBoom.NadeDamage(true) == 35 && Math.Abs(NdDist(ndBoss, ndLx, ndLy) - 37.5) < .6 && ndBoss.StunUntil < ndBoom.Clock, $"보스는 절반 피해·밀침 ¼·멈칫 없음: {5000 - ndBoss.Hp}, {NdDist(ndBoss, ndLx, ndLy):0.0}");
        Expect(ndBoom.Hp == Swarm.MaxHp && ndBoom.DamageTaken == 0, "볼록이는 안 다친다");

        // 피해 숫자: 0초 70, 300초 220, 단계마다 × HpX, 보스는 × 0.5 × BossX
        var ndNum = Empty(48);
        Expect(ndNum.NadeDamage(false) == 70 && ndNum.NadeDamage(true) == 35, $"0초: {ndNum.NadeDamage(false)}·{ndNum.NadeDamage(true)}");
        ndNum.Clock = 300;
        Expect(ndNum.NadeDamage(false) == 220 && ndNum.NadeDamage(true) == 110, $"300초: {ndNum.NadeDamage(false)}·{ndNum.NadeDamage(true)}");
        foreach (var (ndStage, ndSec) in new[] { (5, 0), (5, 120), (10, 240) })
        {
            var ndStg = Empty(48, Arm.Id.Pistols, ndStage);
            ndStg.Clock = ndSec;
            double ndBase = 70 + .5 * ndSec;
            Expect(ndStg.NadeDamage(false) == (int)Math.Round(ndBase * ndStg.T.HpX, MidpointRounding.AwayFromZero) && ndStg.NadeDamage(true) == (int)Math.Round(ndBase * .5 * ndStg.T.BossX, MidpointRounding.AwayFromZero) && ndStg.NadeDamage(false) > ndBase, $"{ndStage}단계 {ndSec}초: × HpX·× 0.5 × BossX: {ndStg.NadeDamage(false)}·{ndStg.NadeDamage(true)}");
        }

        // 제 발밑에서 터져도 안 다친다 (떨어질 자리로 걸어간다). 잡은 벌레는 보석을 남기고 NadeKills 가 는다
        var ndSelf = Empty(49);
        ndSelf.Weapon.Range = 0;
        var ndMark = Dummy(ndSelf, Swarm.Kind.Bug, 120, 0, 100);
        Expect(ndSelf.Throw(), "던진다");
        ndSelf.Steer(1, 0);
        Run(ndSelf, .56);
        Expect(ndSelf.NadeBoomAt > 0 && NdDist(ndMark, ndSelf.X, ndSelf.Y) < 80 && ndMark.Hp == 30 && ndSelf.Hp == Swarm.MaxHp && ndSelf.DamageTaken == 0, "떨어질 자리로 걸어가 서 있어도 안 다친다");
        var ndKill = Empty(50);
        ndKill.Weapon.Range = 0;
        foreach (var (kx, ky) in new[] { (130, 0), (120, 28), (115, -28) }) Dummy(ndKill, Swarm.Kind.Bug, kx, ky, 5);
        ndKill.Throw();
        Run(ndKill, .6);
        Expect(ndKill.Kills == 3 && ndKill.NadeKills == 3 && ndKill.Gems.Count(m => m.Item == Swarm.Item.Gem) == 3 && ndKill.Pops.Count >= 4, $"잡힌 벌레는 보석을 남기고 NadeKills 가 는다: {ndKill.NadeKills}");

        // 줍기: 1 → 2 → 3, 3개에서 더 주워도 3 (아이템은 사라진다)
        var ndPick = Empty(51);
        ndHeard = "";
        ndPick.OnEvent = e => ndHeard += e + " ";
        for (int n = 1; n <= 4; n++)
        {
            ndPick.Drop(Swarm.Item.Grenade, 0);
            ndPick.Update(tick);
            Expect(ndPick.Grenades == Math.Min(3, 1 + n) && ndPick.Gems.Count == 0 && ndPick.LastItem == Swarm.Item.Grenade && ndPick.ItemAt == ndPick.Clock && ndPick.GrenadeAt == ndPick.Clock, $"수류탄을 주우면 {Math.Min(3, 1 + n)}개: {ndPick.Grenades}");
        }
        Expect(ndHeard == "grenade grenade grenade grenade ", $"줍는 사건: {ndHeard}");

        // 떨어지기 (GrenadeChance = 1): 30초 전엔 안 나온다, 나온 뒤 40초는 안 나온다, 든 것 + 판 위 것이 3이면 안 나온다, 하나 던지면 다시
        var ndDrop = Empty(52);
        ndDrop.GrenadeChance = 1;
        int NdOut() => ndDrop.Gems.Count(m => m.Item == Swarm.Item.Grenade);
        void NdKill() { Dummy(ndDrop, Swarm.Kind.Bug, 200, 0, 1); Run(ndDrop, 1.5); }
        ndDrop.Clock = Swarm.GrenadeFirst - 12;
        NdKill();
        Expect(ndDrop.Kills == 1 && NdOut() == 0, "30초 전에는 수류탄이 안 나온다");
        ndDrop.Clock = Swarm.GrenadeFirst;
        NdKill();
        NdKill();
        Expect(ndDrop.Kills == 3 && NdOut() == 1, $"하나 떨어지면 한동안은 또 안 나온다: {NdOut()}");
        ndDrop.Clock += Swarm.GrenadeGap;
        NdKill();
        Expect(ndDrop.Kills == 4 && NdOut() == 2, $"40초가 지나면 다시: {NdOut()}");
        ndDrop.Clock += Swarm.GrenadeGap;
        NdKill();
        Expect(ndDrop.Kills == 5 && NdOut() == 2, "든 것(1) + 판 위 것(2)이 3이면 안 나온다");
        ndDrop.Throw();
        Run(ndDrop, 1);
        ndDrop.Clock += Swarm.GrenadeGap;
        NdKill();
        Expect(NdOut() == 3 && ndDrop.Grenades == 0, $"하나 던지면 다시 나올 수 있다: 판 위 {NdOut()}, 든 것 {ndDrop.Grenades}");
        var ndFirst = Empty(52);   // 30초까지는 rng 를 더 쓰지 않는다: 같은 씨의 첫 30초는 수류탄 없는 판과 같다
        var ndRef = new Swarm(TestField, 3, new Random(52), Arm.Id.Pistols) { Spawning = false, ShoeChance = 0, MagnetChance = 0, HeartChance = 0 };
        for (int i = 0; i < 60 * 6; i++) { Dummy(ndFirst, Swarm.Kind.Bug, 200, 0, 1); Dummy(ndRef, Swarm.Kind.Bug, 200, 0, 1); Run(ndFirst, 1); Run(ndRef, 1); if (i == 20) break; }
        Expect(ndFirst.Kills == ndRef.Kills && ndFirst.Gems.Count == ndRef.Gems.Count && ndFirst.Clock < Swarm.GrenadeFirst, "처음 30초는 수류탄이 있어도 같은 판 (rng 를 더 쓰지 않는다)");
        var ndBoss2 = Empty(53);
        ndBoss2.Grenades = 3;
        Dummy(ndBoss2, Swarm.Kind.Boss, 220, 0, 1, Swarm.Bosses);
        Run(ndBoss2, .6);
        Expect(ndBoss2.BossesKilled == 1 && ndBoss2.Gems.All(m => m.Item != Swarm.Item.Grenade), "3개를 채웠으면 여왕도 수류탄을 안 준다");
        var ndBoss3 = Empty(53);
        ndBoss3.Drop(Swarm.Item.Grenade, -200);   // 판 위에 이미 둘 (든 1 + 위 2 = 3)
        ndBoss3.Drop(Swarm.Item.Grenade, -200);
        Dummy(ndBoss3, Swarm.Kind.Boss, 220, 0, 1, 4);
        Run(ndBoss3, .6);
        Expect(ndBoss3.BossesKilled == 1 && ndBoss3.Gems.Count(m => m.Item == Swarm.Item.Grenade) == 2, "든 것 + 판 위 것이 3이면 여왕도 안 준다");

        // MaxGems: 보석이 넘쳐도 수류탄은 밀려나지 않는다. 벌레 떼 한가운데서 터뜨려도 보석 150 이하·값 합 그대로
        var ndCap = Empty(54);
        ndCap.Gems.Add(new Swarm.Gem { X = ndCap.X + 200, Y = ndCap.Y + 120, Item = Swarm.Item.Grenade });
        for (int i = 0; i < Swarm.MaxGems; i++) ndCap.Gems.Add(new Swarm.Gem { X = ndCap.X + 200 + i % 5, Y = ndCap.Y + 120, Xp = 1 });
        Dummy(ndCap, Swarm.Kind.Bug, 150, 0, 1);
        Run(ndCap, .6);
        Expect(ndCap.Kills == 1 && ndCap.Gems.Count == Swarm.MaxGems && ndCap.Gems.Any(m => m.Item == Swarm.Item.Grenade) && ndCap.Gems.Where(m => m.Item == Swarm.Item.Gem).Sum(m => m.Xp) + ndCap.Xp == Swarm.MaxGems + 1, "보석 상한에서 수류탄은 밀려나지 않는다 (보석만 합쳐진다)");
        var ndMass = Empty(55);
        (ndMass.Weapon.Range, ndMass.SafeUntil) = (0, 1e9);
        for (int i = 0; i < 120; i++) ndMass.Gems.Add(new Swarm.Gem { X = ndMass.X + 200 + i % 5, Y = ndMass.Y + 120, Xp = 1 });
        for (int i = 0; i < 100; i++) Dummy(ndMass, Swarm.Kind.Bug, 160 + 8.4 * Math.Sqrt(i + .5) * Math.Cos(i * 2.39996), 8.4 * Math.Sqrt(i + .5) * Math.Sin(i * 2.39996), 5);
        ndMass.Throw();
        Run(ndMass, .6);
        Expect(ndMass.Kills >= 40 && ndMass.Gems.Count == Swarm.MaxGems && ndMass.Gems.Sum(m => m.Xp) + ndMass.Xp == 120 + ndMass.Kills, $"벌레 100마리 한가운데에서 터뜨려도 보석 150 이하·값 합 그대로: 잡은 {ndMass.Kills}, 보석 {ndMass.Gems.Count}");

        // 판이 끝나면 공중 수류탄이 없다 (5:00 승리, 생명이 다해 끝, 끝난 뒤엔 못 던진다)
        var ndWin = Empty(56);
        (ndWin.Weapon.Range, ndWin.SafeUntil, ndWin.Clock) = (0, 1e9, Swarm.WinSec - .2);
        ndWin.Throw();
        Run(ndWin, .3);
        Expect(ndWin.State == Swarm.Phase.Win && ndWin.Nades.Count == 0 && ndWin.NadeBoomAt < 0 && ndWin.Thrown == 1, "5:00 이 되면 공중 수류탄도 사라진다 (터지지 않고)");
        var ndOver = Empty(57);
        ndOver.Hp = 1;
        Dummy(ndOver, Swarm.Kind.Boss, 3, 0);
        ndOver.Grenades = 3;
        ndOver.Throw();
        Run(ndOver, .1);
        Expect(ndOver.State == Swarm.Phase.Over && ndOver.Nades.Count == 0 && ndOver.Thrown == 1 && !ndOver.Throw() && ndOver.Thrown == 1, "생명이 다해 끝나도 공중 수류탄은 없고, 끝난 뒤엔 못 던진다");

        // 씨가 같으면 같은 판 (자동 조종이 던지는 판): 자리·잡은 수·던진 수·수류탄으로 잡은 수·든 수가 같다
        int ndThrows = 0;
        foreach (var ndId in new[] { Arm.Id.Rifle, Arm.Id.Flame })
        {
            Swarm ndA = BotGame(ndId, 77), ndB = BotGame(ndId, 77);
            Expect((ndA.X, ndA.Y, ndA.Kills, ndA.Thrown, ndA.NadeKills, ndA.Grenades, ndA.Born, ndA.Gems.Count, ndA.Hp, ndA.Time) == (ndB.X, ndB.Y, ndB.Kills, ndB.Thrown, ndB.NadeKills, ndB.Grenades, ndB.Born, ndB.Gems.Count, ndB.Hp, ndB.Time), $"같은 씨로 두 판이 같다: {ndId}");
            ndThrows += ndA.Thrown;
        }
        Expect(ndThrows > 0, $"자동 조종이 던진다: {ndThrows}");

        // 안내: 네 줄, 새 글이 열 언어에 다 있다
        Expect(GuideKeys.Length == 4 && GuideKeys[2] == "note.swarm_grenade" && GuideKeys[^1] == "note.esc" && GuideKeys[0] == "note.swarm_move", "안내는 네 줄 (이동 · 자동 · Ctrl · Esc)");
        foreach (var (ndCode, _) in L.Languages) Expect(L.Has("note.swarm_grenade", ndCode) && L.Raw("note.swarm_grenade", ndCode).Length > 4, $"note.swarm_grenade 가 {ndCode} 에 있다");

        // 디버그: swarm item grenade 로 하나 떨어뜨린다, swarm grenade 는 0개여도 하나 던진다
        var ndCmd = new SwarmGame(host) { FakeOpen = f => Session.Fake(f) };
        ndCmd.Command(new[] { "swarm", "weapon", "flame" });
        ndCmd.Command(new[] { "swarm", "item", "grenade" });
        Expect(ndCmd.Running && ndCmd.swarm!.Gems.Count(m => m.Item == Swarm.Item.Grenade) == 1, "swarm item grenade");
        ndCmd.swarm!.Grenades = 0;
        ndCmd.Command(new[] { "swarm", "grenade" });
        Expect(ndCmd.swarm.Nades.Count == 1 && ndCmd.swarm.Thrown == 1 && ndCmd.swarm.Grenades == 0, "swarm grenade: 0개여도 하나 날아간다");
        ndCmd.Command(new[] { "swarm", "stop" });
        Expect(!ndCmd.Running && host.Pet.Mode == Mode.Wander, "swarm stop");
        ndCmd.Command(new[] { "swarm", "weapon", "twin" });
        Expect(ndCmd.swarm!.CampOn, "진짜 판은 가만히 있기 막기가 켜져 있다");
        ndCmd.Command(new[] { "swarm", "stop" });

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
        // 단계 명령: swarm level <n> (연습 판), level set <n>, rush [wall|ring], elite [bug|wasp|beetle]
        var lvl = new SwarmGame(host) { FakeOpen = f => Session.Fake(f) };
        Expect(lvl.Command(new[] { "swarm", "level", "5" }) && lvl.Running && lvl.swarm!.Stage == 5 && lvl.practice && lvl.stage == 1, "swarm level 5: 5단계 연습 판으로 시작 (저장된 단계는 그대로)");
        lvl.Command(new[] { "swarm", "weapon", "flame" });
        Expect(lvl.swarm!.Stage == 5 && lvl.swarm.Weapon.Which == Arm.Id.Flame && lvl.practice, "무기를 바꿔도 단계는 그대로");
        lvl.Command(new[] { "swarm", "level", "2" });
        Expect(lvl.swarm!.Stage == 2 && lvl.swarm.Weapon.Which == Arm.Id.Flame && lvl.swarm.Clock == 0, "swarm level 2: 하는 중이면 같은 무기로 새 판");
        Frames2(lvl, 5);
        lvl.Command(new[] { "swarm", "rush", "wall" });
        Expect(lvl.swarm!.Warning && lvl.swarm.RushShape == Swarm.Shape.Wall, "swarm rush wall: 지금 예고가 뜬다");
        Frames2(lvl, 90);
        Expect(!lvl.swarm!.Warning && lvl.swarm.Born >= lvl.swarm.RushN(0) / Swarm.Waves, $"1초 뒤 한 물결이 들어온다: {lvl.swarm.Born}마리");
        lvl.Command(new[] { "swarm", "elite", "wasp" });
        Expect(lvl.swarm!.Foes.Count(f => f.Elite && f.Kind == Swarm.Kind.Wasp) == 1, "swarm elite wasp");
        lvl.Command(new[] { "swarm", "stop" });
        Expect(!lvl.Running && lvl.stage == 1 && !lvl.practice && lvl.maxStage == 0 && lvl.lastWeapon == null, "연습 판은 끝나도 단계·기록이 그대로");
        lvl.Command(new[] { "swarm", "level", "set", "6" });
        Expect(lvl.stage == 6 && lvl.maxStage == 5 && lvl.SaveState() != null, "swarm level set 6: 저장된 단계 6, 깬 단계 5");
        lvl.Command(new[] { "swarm", "level", "4" });
        Expect(lvl.Running && lvl.swarm!.Stage == 4 && lvl.practice, "안 하고 있을 때 swarm level 4 는 새 판을 연다");
        lvl.Command(new[] { "swarm", "stop" });
        lvl.Command(new[] { "swarm" });
        Expect(lvl.Running && lvl.swarm!.Stage == 6 && !lvl.practice, "보통 시작은 저장된 단계로");
        lvl.Command(new[] { "swarm", "stop" });
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
            var paint = new Swarm(TestField, 3, new Random(ending ? 6 : 5), ending ? Arm.Id.Flame : (Arm.Id)w) { Clock = ending ? Swarm.WinSec - 12 : 236, Hp = 45 };
            for (int i = 0; i < 12; i++) paint.LevelUp();
            if (ending) paint.BossesKilled = Swarm.Bosses;
            paint.Hp = 20;
            paint.Gems.Add(new Swarm.Gem { X = paint.X + 45, Y = paint.Y + 20, Item = Swarm.Item.Magnet });
            paint.Gems.Add(new Swarm.Gem { X = paint.X - 110, Y = paint.Y - 40, Item = Swarm.Item.Shoe });
            for (int i = 0; i < 12; i++) paint.Gems.Add(new Swarm.Gem { X = TestField.Left + 30 + i * 54, Y = TestField.Top + 60 + i % 3 * 150, Xp = 1 + i % 2 * 2, Item = i == 5 ? Swarm.Item.Heart : Swarm.Item.Gem });
            game.Begin(paint);
            paint.OnEvent = null;
            game.ses = Session.Fake(TestField);
            game.ses.Guide(GuideKeys);
            game.ses.GuideUntil = paint.Clock + 2;
            using var ov = host.Overlay(game);
            double until = paint.Clock + 20;
            bool sawQueen = false, sawLow = false;
            var (bad, first) = SceneKit.Repaint(ov, game, TestField, () =>
            {
                if (paint.Done || paint.Clock >= until) return false;
                paint.BotPlay();
                paint.Update(tick);
                if (paint.State == Swarm.Phase.Play) paint.Hp = Math.Max(paint.Hp, 12); // 끝까지 그려 보게
                if (paint.Done && !ending) return false; // 큰 피해(여왕 40)로 죽어 닫힌 판은 거기까지 (앱은 그때 판을 닫는다)
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

        // 단계 표시를 다시 칠할 자리: 시작 깃발·엘리트(금 테두리·반짝이) · 예고 띠와 세모·붉은 모서리·한쪽 줄/둘러싸기 · 이긴 결과(깃발 ↑·무지개 왕관)
        void Repaints(string what, Swarm p, double secs, int maxStage = 0, Action<Swarm>? each = null)
        {
            (game.stage, game.maxStage, game.practice, game.auto) = (p.Stage, maxStage, false, false);
            game.Begin(p);
            p.OnEvent = null;
            game.ses = Session.Fake(TestField);
            using var ov2 = host.Overlay(game);
            double until = p.Clock + secs;
            var (bad2, first2) = SceneKit.Repaint(ov2, game, TestField, () =>
            {
                if (p.Done || p.Clock >= until) return false;
                p.BotPlay();
                each?.Invoke(p);   // 프레임마다 (수류탄 검사가 던지고 놓는다)
                p.Update(tick);
                if (p.State == Swarm.Phase.Play) p.Hp = Math.Max(p.Hp, 40);
                if (p.Done) return false;
                game.Track();
                return true;
            }, 60 * 40);
            Expect(bad2 == 0, $"바뀐 자리만 다시 칠해도 화면이 맞다 ({what}): {bad2}번 다름 (처음 {first2}프레임)");
            game.swarm = null;
        }
        var intro = new Swarm(TestField, 3, new Random(31), Arm.Id.Shotgun, 12);
        intro.CallElite(Swarm.Kind.Bug);
        intro.CallElite(Swarm.Kind.Wasp);
        intro.CallElite(Swarm.Kind.Beetle);
        Repaints("시작 깃발·엘리트", intro, 4, 5);
        Repaints("예고·러시 (한쪽 줄·둘러싸기)", new Swarm(TestField, 3, new Random(32), Arm.Id.Rifle, 3) { Clock = 33 }, 28, 10);
        Repaints("마무리 러시", new Swarm(TestField, 3, new Random(33), Arm.Id.Flame, 2) { Clock = 273 }, 24, 7);
        Repaints("이긴 결과 (깃발 ↑·무지개 왕관)", new Swarm(TestField, 3, new Random(34), Arm.Id.Long, 10) { SafeUntil = 1e9, Clock = Swarm.WinSec - 3, BossesKilled = 2 }, 8, 9);
        Repaints("이긴 결과 (큰 왕관·보라)", new Swarm(TestField, 3, new Random(35), Arm.Id.Long, 7) { SafeUntil = 1e9, Clock = Swarm.WinSec - 3, BossesKilled = 4 }, 8, 6);
        // 수류탄: 6초 동안 0.7초마다 던지고 (수를 채운다) 옆에 수류탄 하나, 마지막에 빈 손 한 번. 그리고 5:00 직전에 던진 직후 (판이 끝나며 공중 수류탄이 지워지는 프레임)
        int ndFrame = 0;
        bool ndEmptied = false;
        var ndRep = new Swarm(TestField, 3, new Random(36), Arm.Id.Rifle) { Clock = 100 };
        ndRep.Drop(Swarm.Item.Grenade, -100);
        Repaints("수류탄 (던지기·터짐·줍기·빈 손)", ndRep, 6.3, 0, np =>
        {
            if (ndFrame++ % 42 == 0) { np.Grenades = 3; np.Throw(); }
            if (!ndEmptied && np.Clock >= 105.7) { ndEmptied = true; np.Grenades = 0; np.ThrowAt = -9; np.Throw(); }
        });
        bool ndLast = false;
        Repaints("수류탄 (5:00 직전에 던진 직후)", new Swarm(TestField, 3, new Random(37), Arm.Id.Long, 3) { SafeUntil = 1e9, Clock = Swarm.WinSec - 2 }, 5, 0, np =>
        {
            if (!ndLast && np.Clock >= Swarm.WinSec - .25) { ndLast = true; np.Grenades = 2; np.ThrowAt = -9; np.Throw(); }
        });
        string special = SpecCheck(game, host);
        string stage2 = Stage2Check(game, host);
        return $"버그 떼: 무기 {Arm.Kinds}가지 (판마다 하나, 뽑힌 수 {string.Join("·", drawn)}/200), 자동 조종 평균 초/끝까지 버틴 판({seeds}판씩) — {line}, 제자리 자동 조종은 가운데 평균 {idleAvg:0}초·구석 {cornerAvg:0}초에 쓰러져 5:00 은 없음 (가만히 있으면 땅에서 벌레가 올라온다). 생명 {Swarm.MaxHp}, 보스 1분마다 {Swarm.Bosses}번, {Swarm.WinSec:0}초에 끝, 자석 {Swarm.MaxMagnets}개까지 둘레 {Swarm.PickRange:0}→{Swarm.PickRange + Swarm.MagnetStep * Swarm.MaxMagnets:0}. {special} {stage2}";

        static void Frames2(SwarmGame g, int n) { for (int i = 0; i < n; i++) g.Frame(Tick); }
    }
}
