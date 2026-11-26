using System.Diagnostics;

namespace Bolok;

// 클라이밍 자가 검사 (--selfcheck, docs/climb.md 8장). 장면·속도 재기는 ClimbScenes.cs, 봇 표는 ClimbTable.cs
static partial class ClimbCheck
{
    static void Expect(bool ok, string what) { if (!ok) throw new Exception("자가 검사 실패: " + what); }

    public static string Check()
    {
        var sw = Stopwatch.StartNew();
        string determinism = CheckDeterminism();
        string fair = CheckFair();
        string seen = CheckSeen();
        string bar = CheckBar();
        string fall = CheckFall();
        string game = CheckGame();
        string place = CheckPlace();
        string art = CheckArt();
        string draw = CheckDraw();
        string bots = CheckBots();
        string theme = CheckTheme();
        string combo = CheckCombo();
        string comboDet = CheckComboDeterminism();
        string removed = CheckRemoved();
        string cafePlace = CheckCafePlace();
        string cafeGeo = CheckCafeGeometry();
        string cafeRules = CheckCafeRules();
        string cafeGame = CheckCafeGame();
        string comboFx = CheckComboFx();
        return $"클라이밍: 같은 씨 같은 판({determinism}) · 덩굴 공정함({fair}) · 늘 보인다({seen}) · 막대({bar}) · 떨어짐({fall}) · 조작·기록·Esc({game}) · 판 자리({place}) · 그림({art}) · 그리기({draw}) · 봇 표({bots}) · 테마({theme}) · 콤보({combo}) · 콤보 같은 씨({comboDet}) · 없앤 것({removed}) · 카페 자리({cafePlace}) · 카페 그림 자리({cafeGeo}) · 카페 드나들기({cafeRules}) · 카페 게임({cafeGame}) · 콤보 효과({comboFx}) 모두 통과 ({sw.Elapsed.TotalSeconds:0.0}초)";
    }

    // ── 같은 씨 = 같은 판 ──
    // 조작 기록 (틱, 쪽) 을 다른 프레임 빠르기로 되풀이해도 같다
    static List<(int Tick, int Dir)> Record(int seed, ClimbSkill skill, ClimbTune? tune = null)
    {
        var t = new Climb(seed, tune);
        var bot = new ClimbBotState(seed * 31 + 5);
        var log = new List<(int, int)>();
        for (int i = 0; i < 60 * 300 && !t.Done; i++)
        {
            int before = t.Presses;
            ClimbBot.Play(t, skill, bot);
            if (t.Presses != before) log.Add((t.Ticks, t.LastPress));
            t.Update(Climb.Tick);
        }
        return log;
    }

    static Climb Replay(int seed, List<(int Tick, int Dir)> log, Func<int, double> dt, ClimbTune? tune = null)
    {
        var t = new Climb(seed, tune);
        int n = 0;
        void To(int tick) { while (t.Ticks < tick && !t.Done) { double d = Math.Min(dt(n++), (tick - t.Ticks) * Climb.Tick); t.Update(d); } }
        foreach (var (tick, dir) in log) { To(tick); if (t.Done) break; t.Press(dir); }
        for (int i = 0; i < 60 * 30 && !t.Done; i++) t.Update(Climb.Tick);   // 마지막 키 뒤 (시간으로 끝나기까지)
        return t;
    }

    static string CheckDeterminism()
    {
        var log = Record(7, ClimbSkill.Easy);
        Expect(log.Count > 20, "기록이 있다");
        var a = Replay(7, log, _ => Climb.Tick);
        var b = Replay(7, log, _ => Climb.Tick);
        var c = Replay(8, log, _ => Climb.Tick);
        Expect(a.N == b.N && a.Ticks == b.Ticks && a.Bar == b.Bar && a.Why == b.Why && a.Berries == b.Berries, "같은 씨·같은 조작은 같은 판");
        Expect(a.N != c.N || a.Ticks != c.Ticks || a.Why != c.Why || a.Bar != c.Bar, "다른 씨는 다른 판");
        var r30 = Replay(7, log, _ => 1.0 / 30);
        var rng = new Random(3);
        var jit = Replay(7, log, _ => rng.NextDouble() * .045 + .004);
        foreach (var (name, r) in new[] { ("1/30", r30), ("들쭉날쭉", jit) })
            Expect(r.N == a.N && r.Ticks == a.Ticks && r.Why == a.Why && Math.Abs(r.Bar - a.Bar) < 1e-9 && r.Berries == a.Berries && r.Saves == a.Saves, $"프레임 빠르기와 무관 ({name}: {r.N}잎 {r.Ticks}틱 · 1/60: {a.N}잎 {a.Ticks}틱)");
        // 같은 판 두 번: 잎도 같다
        var x = new Climb(11); var y = new Climb(11); var z = new Climb(12);
        bool same = true, diff = false;
        var xd = new int[300];
        for (int i = 0; i < 300; i++) { xd[i] = x.At(i).Dir; same &= x.At(i).Dir == y.At(i).Dir && x.At(i).Kind == y.At(i).Kind && x.At(i).Fog == y.At(i).Fog; diff |= x.At(i).Dir != z.At(i).Dir; }
        Expect(same && diff, "같은 씨는 같은 덩굴, 다른 씨는 다른 덩굴");
        // 기믹을 끄고 켜도 방향은 같다 (표 견줌이 같은 덩굴 위에서)
        var off = new Climb(11, ClimbTune.Now with { Gimmicks = false });
        for (int i = 1; i < 300; i++) Expect(off.At(i).Dir == xd[i], "기믹 끄기는 방향을 안 바꾼다");
        return $"씨 · 1/60·1/30·들쭉날쭉 ({a.N}잎)";
    }

    // ── 덩굴 공정함: 씨 500 × 1,000잎 ──
    static string CheckFair()
    {
        var T = ClimbTune.Now;
        var flips = new double[4]; var counts = new double[4];
        int[] lo = { 1, 30, 150, 300 }, hi = { 29, 149, 299, 9999 };
        int longest = 0, zigMax = 0, kinds = 0;
        for (int seed = 1; seed <= 500; seed++)
        {
            var t = new Climb(seed);
            const int N = 1000;
            var dir = new int[N + 1]; var kind = new ClimbKind[N + 1]; var fog = new bool[N + 1];
            for (int i = 1; i <= N; i++) { ref var l = ref t.At(i); (dir[i], kind[i], fog[i]) = (l.Dir, l.Kind, l.Fog); }
            // 줄 길이
            int run = 1, zig = 0, curZig = 0, start = 1;
            for (int i = 2; i <= N + 1; i++)
            {
                if (i <= N && dir[i] == dir[i - 1]) { run++; continue; }
                if (i > N) break;   // 마지막 줄은 N 에서 잘렸을 수 있다 (더 이어질 줄을 한 칸으로 세지 않는다)
                longest = Math.Max(longest, run);
                Expect(run <= T.MaxRun, $"가장 긴 줄 {run} (씨 {seed}, 잎 {start})");
                if (start < T.EasyLeaves) Expect(run >= 2, $"처음 20잎은 L≥2 (씨 {seed}, 잎 {start})");
                curZig = run == 1 ? curZig + 1 : 0;
                zig = Math.Max(zig, curZig);
                start = i; run = 1;
            }
            Expect(zig <= T.MaxZig, $"번갈아 꺾기 {zig}번 (씨 {seed})");
            zigMax = Math.Max(zigMax, zig);
            for (int i = 1; i < N; i++)
                for (int z = 0; z < 4; z++)
                    if (i >= lo[z] && i <= hi[z]) { counts[z]++; if (dir[i + 1] != dir[i]) flips[z]++; }
            // 기믹 자리
            int last = -99;
            for (int i = 1; i <= N; i++)
            {
                Expect((kind[i] == ClimbKind.Flower) == (i % T.Mile == 0), $"큰 꽃잎은 {T.Mile}잎마다 (잎 {i})");
                if (fog[i]) Expect(i >= T.CloudFrom, $"구름 띠는 {T.CloudFrom}잎부터 (잎 {i})");
                if (kind[i] == ClimbKind.Leaf) continue;
                kinds++;
                Expect(i - last >= 3, $"기믹 잎 사이 3칸 이상 (씨 {seed}, 잎 {last}·{i})");
                last = i;
                int from = kind[i] switch { ClimbKind.Berry => T.BerryFrom, ClimbKind.Puff => T.PuffFrom, ClimbKind.Wilt => T.WiltFrom, _ => 0 };
                Expect(i >= from, $"{kind[i]} 는 {from}잎부터 (잎 {i})");
            }
            // 구름 띠 길이 8~12
            for (int i = 1; i <= N; i++)
            {
                if (!fog[i] || fog[i - 1]) continue;
                int len = 0;
                while (i + len <= N && fog[i + len]) len++;
                if (i + len > N) break;
                Expect(len >= T.CloudLenLo && len <= T.CloudLenHi, $"구름 띠 길이 {len} (씨 {seed}, 잎 {i})");
            }
        }
        double[] want = { .28, .35, .45, .57 };   // 0~29 · 30~149 · 150~299 · 300~ (300~599 는 40 → 55% 로 올라 평균 49%, 600~ 은 55%)
        var got = Enumerable.Range(0, 4).Select(z => flips[z] / counts[z]).ToArray();
        for (int z = 0; z < 4; z++) Expect(Math.Abs(got[z] - want[z]) <= .05, $"구간 {z} 꺾임 비율 {got[z] * 100:0.0}% (표 {want[z] * 100:0}% ±5%p)");
        return $"500씨×1000잎: 가장 긴 줄 {longest}·번갈아 {zigMax}·꺾임 {string.Join("/", got.Select(x => (x * 100).ToString("0")))}%·기믹 {kinds}개";
    }

    // ── 늘 보인다: 시선이 다 따라온 뒤 다음 4잎이 판 안 (구름 띠 안에서도 다음 3잎) ──
    static string CheckSeen()
    {
        int checkedLeaves = 0;
        double minX = 999, maxX = -999, maxPet = 0;
        for (int seed = 1; seed <= 200; seed++)
        {
            var t = new Climb(seed);
            for (int n = 0; n < 400; n++)
            {
                t.N = n;
                t.At(n + 20);
                double view = ClimbView.TargetX(t, n), own = t.At(n).Col * ClimbView.DX;
                maxPet = Math.Max(maxPet, Math.Abs(own - view));
                for (int k = 1; k <= 4; k++)
                {
                    double x = ClimbView.WideA2 / 2.0 + t.At(n + k).Col * ClimbView.DX - view;
                    (minX, maxX) = (Math.Min(minX, x), Math.Max(maxX, x));
                    Expect(x >= 12 && x <= ClimbView.WideA2 - 12, $"다음 {k}잎이 판 안 (씨 {seed}, 잎 {n}: x {x:0})");
                }
                for (int k = 1; k < t.T.CloudHide; k++) Expect(!t.Hidden(n + k), "다음 3잎은 구름에도 안 가린다");
                checkedLeaves++;
            }
        }
        Expect(maxPet <= 50 + 1e-9, $"볼록이는 판 가운데 ±50칸 안 ({maxPet:0})");
        // 볼록이 몸(40칸)·표시 자리: 볼록이는 판 안 가로 25~125칸
        return $"{checkedLeaves}잎 · 다음 4잎의 가로 {minX:0}~{maxX:0}칸 (판 150)";
    }

    // ── 막대 ──
    static Climb Fresh(int seed = 1, ClimbTune? tune = null) => new(seed, tune ?? ClimbTune.Now with { Gimmicks = false });
    static void Run(Climb t, double sec) { for (int i = 0; i < (int)Math.Round(sec * 60); i++) t.Update(Climb.Tick); }
    static void Hop(Climb t) => t.Press(t.At(t.N + 1).Dir);

    static string CheckBar()
    {
        var T = ClimbTune.Now;
        var t = Fresh();
        Run(t, 5);
        Expect(t.State == Climb.Phase.Wait && t.Bar == 1, "첫 키 전에는 막대가 안 준다 (가득 찬 채 기다린다)");
        Hop(t);
        Expect(t.State == Climb.Phase.Run && t.N == 1, "첫 키가 판을 시작하고 한 칸 오른다");
        Run(t, 1);
        double want = 1 - T.Gain * t.Pace(1) * 1;
        Expect(Math.Abs(t.Bar - want) < 1e-6, $"Pace 대로 준다 ({t.Bar:0.0000} / {want:0.0000})");
        Expect(Math.Abs(t.Pace(0) - 1.4) < 1e-9 && Math.Abs(t.Pace(10000) - T.PaceMax) < 1e-9 && t.Pace(200) > t.Pace(100) && T.PaceMax == 6.8, "Pace 는 1.4 에서 올라 6.8 에서 멈춘다");
        double b0 = t.Bar; Hop(t);
        Expect(Math.Abs(t.Bar - Math.Min(1, b0 + T.Gain)) < 1e-9, "한 칸 오르면 Gain 만큼 찬다");
        var t2 = Fresh(); Hop(t2);
        Expect(t2.Bar == 1, "1 에서 멈춘다");
        for (int i = 0; i < 20; i++) { Run(t2, .1); Hop(t2); Expect(t2.Bar <= 1, "1 을 안 넘는다"); }
        // 0 이면 끝
        var t3 = Fresh(); Hop(t3);
        double sec = 0;
        while (!t3.Done && sec < 30) { Run(t3, .05); sec += .05; }
        Expect(t3.State == Climb.Phase.Fall && t3.Why == Climb.Cause.Time && sec is > 4 and < 8, $"막대가 0 이면 떨어진다 ({sec:0.0}초, {t3.Why})");
        Run(t3, T.FallSec + .1);
        Expect(t3.State == Climb.Phase.Over, "떨어짐 0.9초 뒤 결과");
        // 열매·이정표
        var t4 = Fresh(); Hop(t4); t4.At(2).Kind = ClimbKind.Berry; t4.Bar = .3; Hop(t4);
        Expect(Math.Abs(t4.Bar - (.3 + T.Gain + T.BerryBar)) < 1e-9 && t4.Berries == 1, "열매는 막대 +0.2");
        var t5 = Fresh(); Hop(t5); t5.At(2).Kind = ClimbKind.Flower; t5.Bar = .2; Hop(t5);
        Expect(t5.Bar == 1, "이정표(큰 꽃잎)에서 막대가 가득");
        // 멈춤: 홀씨로 돌아오는 동안
        var t6 = Fresh(); Hop(t6); t6.Puff = true; t6.Bar = .5;
        t6.Press(-t6.At(2).Dir);
        Expect(t6.Saves == 1 && !t6.Puff && !t6.Done && t6.N == 1, "틀린 쪽을 밟아도 홀씨가 있으면 한 번 산다");
        Run(t6, T.SaveSec - .05);
        Expect(t6.Bar == .5, "홀씨로 돌아오는 동안 막대가 안 준다");
        int n6 = t6.N; Hop(t6);
        Expect(t6.N == n6, "홀씨로 돌아오는 동안 조작은 안 먹는다");
        Run(t6, .1); Hop(t6);
        Expect(t6.N == n6 + 1, "돌아온 뒤에는 다시 오른다");
        // 홀씨는 막대 0 을 못 살린다
        var t8 = Fresh(); Hop(t8); t8.Puff = true; t8.Bar = .002;
        Run(t8, .2);
        Expect(t8.Why == Climb.Cause.Time && t8.Saves == 0, "막대 0 은 홀씨로 못 산다");
        // 검사용 봇(0.10초)은 600잎을 시간으로 안 죽는다
        for (int seed = 1; seed <= 20; seed++)
        {
            var p = Play(seed, ClimbSkill.Probe, maxLeaves: 600);
            Expect(p.N >= 600 && !p.Done, $"검사용 봇은 600잎까지 산다 (씨 {seed}: {p.N}잎 {p.Why})");
        }
        return "첫 키 전 · Pace · 1에서 멈춤 · 0 이면 끝 · 열매·이정표 · 홀씨 멈춤 · 검사용 봇 600잎";
    }

    // ── 떨어짐 ──
    static string CheckFall()
    {
        var T = ClimbTune.Now;
        var t = Fresh(); Hop(t);
        t.Press(-t.At(2).Dir);
        Expect(t.State == Climb.Phase.Fall && t.Why == Climb.Cause.Wrong && t.N == 1 && t.Wrongs == 1, "틀린 쪽을 밟으면 끝");
        int n = t.N; Hop(t);
        Expect(t.N == n, "끝난 판은 조작을 안 받는다");
        var t2 = Fresh(); Hop(t2); t2.Puff = true;
        t2.Press(-t2.At(2).Dir); Run(t2, T.SaveSec + .1);
        t2.Press(-t2.At(2).Dir);
        Expect(t2.Why == Climb.Cause.Wrong && t2.Saves == 1, "홀씨는 한 번뿐");
        var t3 = Fresh(); Hop(t3); t3.At(2).Kind = ClimbKind.Wilt; t3.Bar = 1; Hop(t3);
        Run(t3, T.WiltSec - .1);
        Expect(!t3.Done, "시든 잎은 0.9초는 버틴다");
        Run(t3, .2);
        Expect(t3.Why == Climb.Cause.Wilt, "시든 잎은 0.9초 뒤 떨어진다");
        var t4 = Fresh(); Hop(t4); t4.At(2).Kind = ClimbKind.Wilt; t4.Bar = 1; Hop(t4); Run(t4, .5); Hop(t4); Run(t4, 1.5);
        Expect(!t4.Done, "시든 잎에서 서둘러 내리면 안 떨어진다");
        var t5 = Fresh(); Hop(t5); t5.Puff = true; t5.At(2).Kind = ClimbKind.Wilt; Hop(t5); Run(t5, 1);
        Expect(t5.Why == Climb.Cause.Wilt && t5.Puff, "시든 잎은 홀씨로 못 산다 (서둘러야 한다)");
        // 열매·홀씨 줍기
        var t6 = Fresh(); Hop(t6); t6.At(2).Kind = ClimbKind.Puff; Hop(t6);
        Expect(t6.Puff && t6.At(2).Taken, "홀씨를 줍는다");
        t6.At(3).Kind = ClimbKind.Puff; Hop(t6);
        Expect(t6.Puff && !t6.At(3).Taken, "홀씨를 지닌 채로는 새로 안 줍는다 (잎에 남는다)");
        return "틀림 → 끝 · 홀씨 한 번 · 시든 잎 0.9초 · 홀씨 줍기";
    }

    // ── 조작·기록·Esc·판 (게임) ──
    static string CheckGame()
    {
        var host = new SceneHost("adult_sweet", 3);
        var field = new Rectangle(660, 300, 450, 528);
        var keys = new HashSet<Keys>();
        bool front = true;
        (ClimbGame, Climb, Session) Open(int seed, ClimbLook look = ClimbLook.A2, int start = 0)
        {
            var g = new ClimbGame(host);
            var t = new Climb(seed, ClimbTune.Now with { Gimmicks = false }, start);
            var p = look == ClimbLook.A2 ? field : new Rectangle(field.X, field.Y, ClimbView.WideA4 * 3, field.Height);
            var s = Session.Fake(p, k => keys.Contains(k), () => front);
            g.Begin(t, look, p, s, host.Pet.P.Stage);
            return (g, t, s);
        }
        Keys KeyOf(int dir) => dir < 0 ? Keys.Left : Keys.Right;

        // 쥐고 있어도 한 칸
        keys.Clear(); front = true;
        var (g1, t1, s1) = Open(2);
        g1.Frame(1 / 60.0);
        keys.Add(KeyOf(t1.At(1).Dir));
        for (int i = 0; i < 30; i++) g1.Frame(1 / 60.0);
        Expect(t1.N == 1, $"쥐고 있어도 한 칸 ({t1.N})");
        keys.Clear(); g1.Frame(1 / 60.0);
        keys.Add(KeyOf(t1.At(2).Dir));
        g1.Frame(1 / 60.0);
        Expect(t1.N == 2, "뗐다 누르면 다시 한 칸");
        g1.Stop(true);
        // 번갈아 두드려도 하나도 안 잃는다 (키 상태로는 못 보는 짧은 누름: KeyDown·KeyUp 이벤트)
        keys.Clear();
        var (g2, t2, s2) = Open(3);
        g2.Frame(1 / 60.0);
        int taps = 0;
        for (int i = 0; i < 12 && !t2.Done; i++)
        {
            int dir = t2.At(t2.N + 1).Dir;
            s2.KeyEvent(KeyOf(dir), true); s2.KeyEvent(KeyOf(dir), false);
            g2.Frame(1 / 60.0);
            taps++;
        }
        Expect(t2.N == taps && !t2.Done, $"빠르게 번갈아 두드려도 하나도 안 잃는다 ({t2.N}/{taps})");
        g2.Stop(true);
        // ← 를 쥔 채 → 를 두드려도 ← 가 다시 안 눌린다
        keys.Clear();
        var (g3, t3, _) = Open(4);
        g3.Frame(1 / 60.0);
        var hold = KeyOf(t3.At(1).Dir); var tap = hold == Keys.Left ? Keys.Right : Keys.Left;
        keys.Add(hold); g3.Frame(1 / 60.0);
        keys.Add(tap); g3.Frame(1 / 60.0); keys.Remove(tap); g3.Frame(1 / 60.0); g3.Frame(1 / 60.0);
        Expect(t3.Presses == 2, $"한 키를 쥔 채 다른 키를 두드렸다 떼도 쥔 키는 다시 안 눌린다 (눌림 {t3.Presses}번)");
        g3.Stop(true);
        // 같은 프레임에 두 키 → 둘 다 버린다 (오르지도 떨어지지도 않는다)
        keys.Clear();
        var (g4, t4, _) = Open(5);
        g4.Frame(1 / 60.0);
        keys.Add(Keys.Left); keys.Add(Keys.Right); g4.Frame(1 / 60.0); g4.Frame(1 / 60.0);
        Expect(t4.N == 0 && !t4.Done && t4.State == Climb.Phase.Wait, "같은 프레임 두 키는 둘 다 버린다");
        g4.Stop(true);
        // 메뉴를 고를 때 눌려 있던 키로 첫 칸을 안 오른다
        keys.Clear(); keys.Add(KeyOf(new Climb(6).At(1).Dir));
        var (g5, t5, _) = Open(6);
        for (int i = 0; i < 10; i++) g5.Frame(1 / 60.0);
        Expect(t5.N == 0 && t5.State == Climb.Phase.Wait, "메뉴 때 눌려 있던 키로 안 오른다");
        keys.Clear(); g5.Frame(1 / 60.0); keys.Add(KeyOf(t5.At(1).Dir)); g5.Frame(1 / 60.0);
        Expect(t5.N == 1, "뗐다 누르면 오른다");
        g5.Stop(true);
        // 판이 앞 창이 아니면: 안 먹고 막대가 멈추고, 돌아오면 0.5초는 안 준다
        keys.Clear(); front = true;
        var (g6, t6, _) = Open(7);
        g6.Frame(1 / 60.0);
        keys.Add(KeyOf(t6.At(1).Dir)); g6.Frame(1 / 60.0); keys.Clear(); g6.Frame(1 / 60.0);
        Expect(t6.State == Climb.Phase.Run && t6.N == 1, "시작");
        double bar0 = t6.Bar; int tick0 = t6.Ticks;
        front = false;
        keys.Add(KeyOf(t6.At(2).Dir));
        for (int i = 0; i < 120; i++) g6.Frame(1 / 60.0);
        Expect(t6.N == 1 && t6.Bar == bar0 && t6.Ticks == tick0, "다른 창을 누르면 멈춘다 (키도 안 먹고 막대도 안 준다)");
        Expect(g6.Show.Paused, "멈춘 동안 볼록이는 두리번 (Paused)");
        keys.Clear(); front = true;
        g6.Frame(1 / 60.0); g6.Frame(1 / 60.0);
        Expect(!g6.Show.Paused && t6.GraceLeft > .45, "판을 다시 누르면 0.5초 뒤부터 막대가 준다");
        g6.Stop(true);

        // 끝난 뒤: 2.5초 뒤 닫힘, 클릭하면 바로 (0.6초 뒤부터)
        var (g7, t7, s7) = Open(8);
        g7.Frame(1 / 60.0); t7.Press(t7.At(1).Dir); t7.Press(-t7.At(2).Dir);
        for (int i = 0; i < 60 * 60 && g7.Running; i++) g7.Frame(1 / 60.0);
        Expect(!g7.Running, "결과 화면은 2.5초 뒤 닫힌다");
        var (g8, t8, s8) = Open(9);
        g8.Frame(1 / 60.0); t8.Press(t8.At(1).Dir); t8.Press(-t8.At(2).Dir);
        for (int i = 0; i < 60 * 3 / 2 + 12 && t8.EndedFor < 1.0 || t8.State != Climb.Phase.Over; i++) g8.Frame(1 / 60.0);
        s8.Press(MouseButtons.Left, true); g8.Frame(1 / 60.0);
        Expect(!g8.Running, "결과 화면은 판을 누르면 바로 닫힌다");

        // 기록: 최고·판 수·마지막·열매, 연습·자동은 안 남고, 저장했다 읽으면 같고, 깨진 JSON 은 버린다
        var rg = new ClimbGame(host);
        var a = Play(11, ClimbSkill.Good); var b = Play(12, ClimbSkill.Easy);
        rg.KeepForCheck(a);
        var st = rg.State;
        Expect(st.Best == a.N && st.Plays == 1 && st.Last == a.N && st.Berries == a.Berries && st.MaxCombo == a.MaxCombo && st.Cafes == a.Cafes, $"기록: 최고·판 수·마지막·가장 긴 콤보·카페 ({st})");
        rg.KeepForCheck(b);
        Expect(rg.State.Best == Math.Max(a.N, b.N) && rg.State.Plays == 2 && rg.State.Last == b.N, "더 낮은 판은 최고를 안 낮춘다");
        var before = rg.State;
        rg.KeepForCheck(a, practiceRun: true); rg.KeepForCheck(a, autoRun: true);
        Expect(rg.State == before, "연습 판·자동 판은 기록에 안 남는다");
        string json = rg.SaveState()!;
        var rg2 = new ClimbGame(host); rg2.LoadState(json);
        Expect(rg2.State == rg.State, "저장했다 읽으면 같은 값");
        var broken = new ClimbGame(host); broken.LoadState("{깨진");
        Expect(broken.State.Plays == 0 && broken.SaveState() == null, "깨진 기록은 버린다");
        var neg = new ClimbGame(host); neg.LoadState("{\"Best\":-5,\"Plays\":-1,\"Last\":0,\"Berries\":0,\"MaxCombo\":-3,\"Cafes\":-1}");
        Expect(neg.State.Best == 0 && neg.State.Plays == 0 && neg.State.MaxCombo == 0 && neg.State.Cafes == 0, "음수 기록은 0 으로");
        var old = new ClimbGame(host); old.LoadState("{\"Best\":210,\"Plays\":14,\"Last\":88,\"Berries\":120,\"Friends\":2}");   // 옛 저장: Friends 는 버려지고 최고·판 수는 그대로
        Expect(old.State == (210, 14, 88, 120, 0, 0), $"옛 저장(Friends 있음)을 읽어도 최고·판 수가 그대로 ({old.State})");
        Expect(old.SaveState()!.Contains("MaxCombo") && !old.SaveState()!.Contains("Friends"), "새로 저장하면 Friends 없이 MaxCombo·Cafes");
        Expect(rg.Stats().Single().Played && rg.Stats().Single().Value.Contains(rg.State.Best.ToString("N0", L.Culture)), "설정 → 상태 줄");
        // 왕관 잎 = 지난 최고 자리
        var cg = new ClimbGame(host); cg.LoadState(json);
        var ct = new Climb(1) { Best = cg.State.Best };
        cg.Begin(ct, ClimbLook.A2, field, Session.Fake(field), host.Pet.P.Stage);
        Expect(cg.Show.BestLeaf == cg.State.Best, "작은 왕관은 지난 최고 잎에");
        cg.Stop(true);

        // Esc: 결과 화면·표정 없이 바로 끝, 본체가 돌아온다, 기록은 안 남는다. 떨어지는 중·결과 화면 중에도 바로
        host.Pet.Hush();   // 앞 시험에서 제대로 끝낸 판의 표정을 지운다
        var eg = new ClimbGame(host);
        var (e1, et1, _) = Open(13);
        e1.Frame(1 / 60.0); et1.Press(et1.At(1).Dir);
        Expect(e1.Running, "판이 열린다");
        e1.Stop(true);
        Expect(!e1.Running && !host.Pet.Emoting && host.Pet.Mode == Mode.Wander, "Esc(보스 키)는 바로 끝, 표정 없음, 본체가 돌아온다");
        var (e2, et2, _) = Open(14);
        e2.Frame(1 / 60.0); et2.Press(et2.At(1).Dir); et2.Press(-et2.At(2).Dir);
        e2.Frame(1 / 60.0); e2.Stop(true);
        Expect(!e2.Running, "떨어지는 중 Esc 도 바로");
        var (e3, et3, _) = Open(15);
        e3.Frame(1 / 60.0); et3.Press(et3.At(1).Dir); et3.Press(-et3.At(2).Dir);
        while (et3.State != Climb.Phase.Over) e3.Frame(1 / 60.0);
        e3.Stop(true);
        Expect(!e3.Running && !host.Pet.Emoting, "결과 화면 중 Esc 도 바로, 표정 없음");
        _ = eg;
        var xg = new ClimbGame(host); // 기록 확인: Esc 는 끝난 판이 아니면 안 남긴다
        var xt = new Climb(16); xg.Begin(xt, ClimbLook.A2, field, Session.Fake(field), host.Pet.P.Stage); xt.Press(xt.At(1).Dir); xg.Stop(true);
        Expect(xg.State.Plays == 0, "Esc 로 끝낸 도중 판은 기록 안 남김");

        // Start: 메뉴에서 시작하면 볼록이 둘레에 판이 열리고 본체가 판 위로 간다
        var sg = new ClimbGame(host) { desktopOnly = true, FakeOpen = f => Session.Fake(f, null, () => true) };
        sg.Start();
        Expect(sg.Running && sg.Placed.Look == ClimbLook.A2 && sg.Placed.Win == IntPtr.Zero, "Start 로 바탕화면 판이 열린다");
        sg.Frame(1 / 60.0);
        sg.Stop(true);
        Expect(!sg.Running, "Start 한 판도 Esc 로 바로 끝");

        // 디버그 명령
        var dg = new ClimbGame(host) { desktopOnly = true, FakeOpen = f => Session.Fake(f, null, () => true) };
        Expect(dg.Command(new[] { "climb", "seed", "5" }) && dg.Command(new[] { "climb", "at", "120" }) && dg.Command(new[] { "climb", "best", "300" }), "디버그 명령: seed·at·best");
        dg.Command(new[] { "climb" });
        Expect(dg.Running, "디버그 climb 으로 시작");
        var dt = dg.Ses; _ = dt;
        dg.Command(new[] { "climb", "bar", "40" }); dg.Command(new[] { "climb", "put", "berry" }); dg.Command(new[] { "climb", "combo", "7" }); dg.Command(new[] { "climb", "put", "cafe" });
        dg.Frame(1 / 60.0);
        dg.Command(new[] { "climb", "stop" });
        Expect(!dg.Running && dg.State.Plays == 0, "climb stop: 바로 끝, 기록 안 남김 (연습 판)");
        Expect(!new ClimbGame(host).Command(new[] { "trail" }) && new ClimbGame(host).Command(new[] { "climb", "put", "berry" }), "다른 게임 명령은 안 받는다");

        // 언어: 메뉴 이름·안내·기록 줄이 열 언어에 다 있다
        string[] langKeys = { "menu.climb", "game.climb", "game.climb_val", "game.climb_none", "note.climb_hop" };
        foreach (var (code, _) in L.Languages)
            foreach (var k in langKeys) Expect(L.Has(k, code) && L.Raw(k, code).Length > 0, $"{k} 이 {code} 에 없다");

        // 메모리: 규칙 한 판(봇 포함)이 새로 잡는 바이트 0
        var warm = new Climb(9);
        var wb = new ClimbBotState(1);
        for (int i = 0; i < 300; i++) { ClimbBot.Play(warm, ClimbSkill.Probe, wb); warm.Update(Climb.Tick); }
        long b0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 3600 && !warm.Done; i++) { ClimbBot.Play(warm, ClimbSkill.Probe, wb); warm.Update(Climb.Tick); }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - b0;
        Expect(bytes == 0 && warm.N > 300, $"규칙 3600틱(봇 포함, {warm.N}잎)이 새로 잡는 메모리 {bytes}B");
        var scan = new Climb(10);
        long b1 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i <= 1000; i++) _ = scan.At(i);
        Expect(GC.GetAllocatedBytesForCurrentThread() - b1 == 0 && Climb.Ring == 64, "잎 1,000개를 만드는 데 메모리 0B, 고리 배열은 64칸");

        // 글자 없음: 소스에 글자 그리기 호출이 없다 (소스를 찾을 수 있을 때)
        string text = "";
        if (FindSource() is { } src)
        {
            foreach (var file in Directory.GetFiles(src, "*.cs"))
            {
                string name = Path.GetFileName(file);
                if (name is "ClimbCheck.cs" or "ClimbScenes.cs" or "ClimbDesk.cs" or "ClimbCheckCombo.cs" or "ClimbScenesFx.cs") continue;
                string body = File.ReadAllText(file);
                foreach (string gone in new[] { "Curl", "Lift", "Friend", "Perch", "Bird", "SkipOpen", "CarrierPaint" })   // 없앤 것(건너뛰기·친구)이 소스에 안 남았다
                    Expect(!body.Contains(gone), $"{name} 에 없앤 것 {gone} 이 남았다");
                foreach (string bad in new[] { "DrawString", "TextRenderer", "DrawText", "AddString", "MeasureString" })
                    Expect(!body.Contains(bad), $"{name} 에 글자 그리기 {bad}");
                foreach (string bad in new[] { "무한", "Infinite", "Stairs" }) Expect(!body.Contains(bad), $"{name} 에 {bad}");
            }
            text = " · 소스에 글자 그리기 없음";
        }
        return $"쥐고 있어도 한 칸·번갈아 두드려도 안 잃음·두 키 무시·메뉴 키·앞 창 아님 멈춤·결과 닫기·기록·Esc·명령·열 언어·메모리 {bytes}B{text}";
    }

    static string? FindSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
        {
            string p = Path.Combine(dir.FullName, "app", "Games", "Climb");
            if (Directory.Exists(p)) return p;
        }
        return null;
    }

    // ── 판 자리: A4 고르기와 A2 로 물러나기, 아이스런 자리는 그대로 ──
    static string CheckPlace()
    {
        var scr = new[] { new Rectangle(0, 0, 1920, 1040) };
        var area = scr[0];
        const int cell = 120, f = 3;
        Win W(int h, int x, int y, int w, int hh, bool top = false) => new(new IntPtr(h), new Rectangle(x, y, w, hh), top);
        var win = W(1, 200, 100, 1200, 820);   // 오른쪽 테두리 x=1400, 높이 820 ≥ 528
        double px = 1390, py = 880;
        var p1 = ClimbPlace.Plan(new[] { win }, px, py, cell, f, scr, area, edge: true);
        Expect(p1.Look == ClimbLook.A4 && p1.Win == win.H && p1.Side == 1 && p1.Panel.Width == 96 * f && p1.Panel.Height == 176 * f
            && p1.Panel.X + p1.Panel.Width / 2 == 1400 && area.Contains(p1.Panel) && Rectangle.Intersect(win.R, p1.Panel).Height == p1.Panel.Height, $"창 오른쪽 테두리 가까이 → A4, 줄기가 테두리 위 ({p1})");
        var p2 = ClimbPlace.Plan(new[] { win }, 210, py, cell, f, scr, area, edge: true);
        Expect(p2.Look == ClimbLook.A4 && p2.Side == -1 && p2.Panel.X + p2.Panel.Width / 2 == 200, $"왼쪽 테두리 가까이면 왼쪽 ({p2})");
        // 안 맞으면 A2: 이번 판이 「가끔」에 안 뽑혔다 · 볼록이가 멀다 · 테두리가 짧다 · 화면 끝이라 바깥 자리가 없다 · 다른 창이 가린다 · 맨 위 고정 창
        var plain = ClimbPlace.Plan(new[] { win }, px, py, cell, f, scr, area, edge: false);
        Expect(plain.Look == ClimbLook.A2 && plain.Win == win.H, $"뽑히지 않으면 A2 ({plain.Why})");
        var far = ClimbPlace.Plan(new[] { win }, 800, py, cell, f, scr, area, edge: true);
        Expect(far.Look == ClimbLook.A2 && far.Win == win.H && far.Why == "no-edge-window", $"볼록이가 테두리에서 멀면 A2 ({far.Why})");
        var shortWin = W(2, 200, 400, 1200, 450);
        Expect(ClimbPlace.Plan(new[] { shortWin }, px, 800, cell, f, scr, area, true).Look == ClimbLook.A2, "테두리가 판 높이보다 짧으면 A2");
        var maxi = W(3, 0, 0, 1920, 1040);
        Expect(ClimbPlace.Plan(new[] { maxi }, 1900, 900, cell, f, scr, area, true).Look == ClimbLook.A2, "최대화 창(바깥 자리 없음)은 A2");
        var edgeWin = W(4, 600, 100, 1320, 820);   // 오른쪽 테두리가 화면 끝
        Expect(ClimbPlace.Plan(new[] { edgeWin }, 1905, py, cell, f, scr, area, true).Look == ClimbLook.A2, "화면 끝에 붙은 테두리는 A2");
        var cover = W(5, 1300, 300, 300, 400);   // 위 창이 판을 가린다
        Expect(ClimbPlace.Plan(new[] { cover, win }, px, py, cell, f, scr, area, true).Look == ClimbLook.A2, "다른 창이 테두리를 가리면 A2");
        Expect(ClimbPlace.Plan(new[] { W(6, 200, 100, 1200, 820, true) }, px, py, cell, f, scr, area, true).Look == ClimbLook.A2, "항상 위 창은 안 쓴다");
        // 두 모니터: 판은 창이 걸친 모니터 안에서만
        var two = new[] { new Rectangle(0, 0, 1920, 1040), new Rectangle(1920, 0, 1280, 1000) };
        var left = W(7, 1000, 100, 920, 820);   // 오른쪽 테두리가 첫 모니터 끝 (바깥 자리는 다른 모니터)
        Expect(ClimbPlace.Plan(new[] { left }, 1915, py, cell, f, two, area, true).Look == ClimbLook.A2, "모니터 끝 테두리는 A2 (판이 두 모니터에 걸치지 않는다)");
        var second = W(8, 2000, 100, 1100, 820);
        var p3 = ClimbPlace.Plan(new[] { second }, 3100 - 10, py, cell, f, two, new Rectangle(1920, 0, 1280, 1000), true);
        Expect(p3.Look == ClimbLook.A2 || two[1].Contains(p3.Panel), "둘째 모니터에서도 판이 그 모니터 안");
        // A2: 넓은 창이면 그 창 안 볼록이 가까이, 아니면 바탕화면 볼록이 둘레 (모니터 안)
        var big = W(9, 100, 50, 1700, 960);
        var a2w = ClimbPlace.Plan(new[] { big }, 900, 900, cell, f, scr, area, false);
        Expect(a2w.Look == ClimbLook.A2 && a2w.Win == big.H && big.R.Contains(a2w.Panel) && Math.Abs(a2w.Panel.X + 225 - 900) < 3, $"넓은 창 안 A2 는 볼록이 가까이 ({a2w.Panel})");
        // 발이 판 위에서 Foot 줄에 오게 (창 아래·모니터 아래에 걸리지 않을 때)
        Expect(Math.Abs(a2w.Panel.Y + ClimbView.Foot * f - 900) <= 6 * f + 1 || a2w.Panel.Bottom >= big.R.Bottom - 6 * f - 1, $"판을 열 때 볼록이 발 자리가 그대로 ({a2w.Panel.Y + ClimbView.Foot * f} / 900)");
        var desk = ClimbPlace.Plan(Array.Empty<Win>(), 960, 1000, cell, f, scr, area, true);
        Expect(desk.Look == ClimbLook.A2 && desk.Win == IntPtr.Zero && area.Contains(desk.Panel) && desk.Panel.Width == 450 && desk.Panel.Height == 528, $"창이 없으면 바탕화면 판 ({desk.Why})");
        foreach (var (qx, qy) in new[] { (30.0, 40.0), (1900.0, 1030.0), (0.0, 1040.0) })
            Expect(area.Contains(ClimbPlace.Plan(Array.Empty<Win>(), qx, qy, cell, f, scr, area, false).Panel), "바탕화면 판은 늘 모니터 안");
        var small = W(10, 600, 300, 500, 500);
        Expect(ClimbPlace.Plan(new[] { small }, 800, 700, cell, f, scr, area, false).Win == IntPtr.Zero, "판보다 좁은 창이면 바탕화면 판");
        // 「가끔」: 씨로 정한다 (세 판에 한 번쯤, 같은 씨면 같은 결정)
        int yes = Enumerable.Range(1, 3000).Count(ClimbPlace.Coin);
        Expect(yes is > 850 and < 1150 && ClimbPlace.Coin(77) == ClimbPlace.Coin(77), $"A4 는 세 판에 한 번쯤 ({yes}/3000)");
        // 창 테두리 판 계산 자체
        Expect(ClimbPlace.EdgePanel(win.R, 1, 0, 288, 528, area) is { } ep && ep.Y == 100 && ep.X == 1256, "EdgePanel: 창 안으로 눌러 넣는다");
        Expect(ClimbPlace.EdgePanel(win.R, 1, 5000, 288, 528, area) is { } ep2 && ep2.Bottom == win.R.Bottom, "EdgePanel: 아래로도 창 안");

        // 게임: Start 가 이 자리를 쓰고, 창이 움직이면 판이 따라가고, 닫히면 그 자리에 둔다
        var host = new SceneHost("adult_sweet", f);
        host.Pet.X = 1390; host.Pet.Y = 880;
        var cur = win.R;
        bool alive = true;
        var g = new ClimbGame(host)
        {
            lookMode = 1,
            FakeOpen = pn => Session.Fake(pn, null, () => true),
            FakeWindows = () => new List<Win> { new(win.H, cur, false) },
            FakeProbe = h => new WinState(alive, false, cur),
            FakeScreens = scr,
        };
        g.Start();
        Expect(g.Running && g.Placed.Look == ClimbLook.A4 && g.Placed.Win == win.H, $"Start: 창 테두리 가까이면 A4 ({g.Placed.Look})");
        var p0 = g.Placed.Panel;
        cur = new Rectangle(cur.X - 120, cur.Y + 20, cur.Width, cur.Height - 40);
        g.Frame(1 / 60.0);
        var expect = ClimbPlace.EdgePanel(cur, 1, cur.Y + (p0.Y - win.R.Y), p0.Width, p0.Height, area)!.Value;
        Expect(g.Placed.Panel == expect && g.Placed.Panel.X + g.Placed.Panel.Width / 2 == cur.Right && g.Placed.Panel != p0, $"창이 움직이면 판이 테두리를 따라간다 ({g.Placed.Panel})");
        var moved = g.Placed.Panel;
        alive = false;
        cur = cur with { X = cur.X + 200 };
        g.Frame(1 / 60.0);
        Expect(g.Placed.Win == IntPtr.Zero && g.Placed.Panel == moved, "창이 닫히면 그 자리에 두고 바탕화면 판처럼");
        g.Stop(true);
        var gp = new ClimbGame(host) { lookMode = 2, FakeOpen = pn => Session.Fake(pn, null, () => true), FakeWindows = () => new List<Win> { new(win.H, win.R, false) }, FakeScreens = scr };
        gp.Start();
        Expect(gp.Placed.Look == ClimbLook.A2, "climb look a2 는 A4 가 맞아도 A2");
        gp.Stop(true);

        // 아이스런 자리는 그대로: Kit/PanelPlace 가 옛 TrailGame 식과 같은 값을 낸다 (200개 무작위)
        var rnd = new Random(5);
        for (int i = 0; i < 200; i++)
        {
            var ins = new Rectangle(rnd.Next(0, 400), rnd.Next(0, 300), rnd.Next(600, 1500), rnd.Next(400, 800));
            double x = rnd.Next(-200, 2000), y = rnd.Next(-200, 1300);
            int w = 200 * 3, h = 120 * 3, k = rnd.Next(2, 5);
            var oldNear = new Rectangle((int)Math.Clamp(x - w / 2.0, ins.Left, Math.Max(ins.Left, ins.Right - w)), (int)Math.Clamp(y + 8 * k - h, ins.Top, Math.Max(ins.Top, ins.Bottom - h - 6 * k)), w, h);
            Expect(PanelPlace.NearPet(ins, x, y, w, h, k) == oldNear, "아이스런 창 안 자리는 전과 같다");
            var oldAround = new Rectangle((int)Math.Clamp(x - w / 2.0, area.Left, area.Right - w), (int)Math.Clamp(y + 8 * k - h, area.Top, area.Bottom - h), w, h);
            Expect(PanelPlace.Around(area, x, y, w, h, k) == oldAround, "아이스런 바탕화면 자리는 전과 같다");
        }
        return $"A4 고르기(창 테두리 가까이·세 판에 한 번쯤 {yes}/3000)·A2 로 물러나기 9가지·창 따라가기·닫히면 그 자리·아이스런 자리 200개 같음";
    }

    // ── 그림 ──
    static string CheckArt()
    {
        var host = new SceneHost("adult_sweet", 3);
        int pics = 0;
        foreach (var look in new[] { ClimbLook.A2, ClimbLook.A4 })
            using (var v = new ClimbView(host.Sprites, "adult_sweet", look, 3))
            {
                foreach (var (name, px) in v.Gallery()) { Expect(px.W > 0 && px.H > 0 && px.P.Any(c => c >> 24 != 0), $"{name} 이 빈 그림"); pics++; }
                Expect(v.Board.Width == v.CW * 3 && v.Board.Height == ClimbView.PH * 3, "큰 그림 크기");
            }
        // 덩굴 마디 네 가지가 이웃 잎과 이어진다: 토막 시작(마디)과 끝(다음 마디)이 속 그림 안
        using (var v = new ClimbView(host.Sprites, "adult_sweet", ClimbLook.A2, 3))
        {
            int[] dxs = { 22, -8, 8, -22 };
            for (int k = 0; k < 4; k++)
            {
                var sg = ClimbArt.StemSeg(dxs[k], -ClimbView.DY, 1);
                Expect(sg.In.On(-sg.Ox, -sg.Oy) && sg.In.On(dxs[k] - sg.Ox, -ClimbView.DY - sg.Oy) || sg.In.On(dxs[k] - sg.Ox, -ClimbView.DY - sg.Oy + 1) || sg.In.On(dxs[k] - sg.Ox - Math.Sign(dxs[k]), -ClimbView.DY - sg.Oy + 1), $"덩굴 마디 {dxs[k]} 이 끊긴 줄기");
            }
        }
        return $"그림 {pics}장 모두 찬 그림 · 덩굴 마디 넷 이어짐";
    }

    // ── 그리기: 다시 칠하기·프레임 메모리 ──
    static readonly (ClimbLook, ClimbTheme)[] DrawKinds = { (ClimbLook.A2, ClimbTheme.Vine), (ClimbLook.A4, ClimbTheme.Vine), (ClimbLook.A2, ClimbTheme.Desk), (ClimbLook.A2, ClimbTheme.Sweet), (ClimbLook.A2, ClimbTheme.Balloon), (ClimbLook.A2, ClimbTheme.Block) };

    static string CheckDraw()
    {
        var host = new SceneHost("adult_sweet", 3);
        long worstBytes = 0;
        int repainted = 0;
        foreach (var (look, th) in DrawKinds)   // 덩굴 두 모양(A2·A4) + 덩굴 말고 넷 (A2)
        {
            var field = look == ClimbLook.A2 ? new Rectangle(660, 300, 450, 528) : new Rectangle(700, 300, 288, 528);
            var g = NewGame(host);
            g.auto = true; g.skill = ClimbSkill.Good;
            var t = new Climb(21);
            g.Begin(t, look, field, Session.Fake(field), host.Pet.P.Stage, th);
            using var ov = host.Overlay(g);
            int frames = 0;
            // 다시 칠하기: 멈춘 동안 일부만 칠하는 길(막대·점수)을 포함해 지난 그림이 안 남는다
            var (bad, first) = SceneKit.Repaint(ov, g, field, () => { g.Frame(1 / 60.0); return ++frames < 60 * 25; }, 60 * 25, every: 7);
            Expect(bad == 0, $"{look}/{th} 다시 칠하기 모자람 {bad}번 (처음 {first} 프레임)");
            g.Stop(true);
            repainted++;
            // 첫 키를 기다리는 동안 (판이 멈춘다): 안 바뀌면 다시 칠하지 않고, 바뀌면 다 칠한다
            var wg = NewGame(host);
            var wt = new Climb(22);
            wg.Begin(wt, look, field, Session.Fake(field, null, () => true), host.Pet.P.Stage, th);
            wt.Steps = 1;
            var rects = new List<Rectangle>();
            using var ovw = host.Overlay(wg);
            using (var wb = new Bitmap(ov.Width, ov.Height))
            using (var wgr = Graphics.FromImage(wb))
                for (int i = 0; i < 60; i++) { wg.Frame(1 / 60.0); rects.Clear(); wg.Dirty(ovw, rects); ovw.Render(wgr); }
            wg.Frame(1 / 60.0); rects.Clear(); wg.Dirty(ovw, rects);
            Expect(rects.Count == 0, $"{look}/{th} 기다리는 동안 판이 멈춰 있다 (칠할 자리 {rects.Count})");
            wt.Press(wt.At(1).Dir); wg.Frame(1 / 60.0); rects.Clear(); wg.Dirty(ovw, rects);
            Expect(rects.Any(r => r.Contains(ovw.Client(field).X + 5, ovw.Client(field).Y + 5)), $"{look} 오르는 프레임은 판 전체를 칠한다");
            wg.Stop(true);
            // 프레임 메모리: 규칙·칠할 자리·그리기가 새로 잡는 바이트 0 (자동 조종 포함)
            var bg = NewGame(host);
            bg.auto = true; bg.skill = ClimbSkill.Probe;
            var bt = new Climb(31);
            bg.Begin(bt, look, field, Session.Fake(field), host.Pet.P.Stage, th);
            using var bmp = new Bitmap(ov.Width, ov.Height);
            using var gr = Graphics.FromImage(bmp);
            using var ov2 = host.Overlay(bg);
            var rr = new List<Rectangle>(64);
            for (int i = 0; i < 1000; i++) { bg.Frame(1 / 60.0); rr.Clear(); bg.Dirty(ov2, rr); ov2.Render(gr); }
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            int n = 0;
            for (; n < 1200 && !bt.Done; n++) { bg.Frame(1 / 60.0); rr.Clear(); bg.Dirty(ov2, rr); ov2.Render(gr); }
            long per = (GC.GetAllocatedBytesForCurrentThread() - b0) / Math.Max(1, n);
            worstBytes = Math.Max(worstBytes, per);
            bg.Stop(true);
        }
        Expect(worstBytes == 0, $"그리기가 프레임마다 새로 잡는 메모리 {worstBytes}B");
        // 판이 창을 따라 옮겨 가는 프레임에도 지난 자리를 지운다
        {
            var field = new Rectangle(400, 120, 288, 528);
            var cur = new Rectangle(0, 60, 544, 700);
            var scr = new[] { new Rectangle(0, 0, 1920, 1080) };
            var pg = new ClimbGame(host) { lookMode = 1, FakeOpen = pn => Session.Fake(pn, null, () => true), FakeWindows = () => new List<Win> { new(new IntPtr(1), cur, false) }, FakeProbe = h => new WinState(true, false, cur), FakeScreens = scr };
            host.Pet.X = 540; host.Pet.Y = 700;
            pg.Start();
            Expect(pg.Running && pg.Placed.Look == ClimbLook.A4, "창 테두리 판 시험이 열린다");
            using var ov = host.Overlay(pg);
            int frames = 0;
            var (bad, first) = SceneKit.Repaint(ov, pg, pg.Placed.Panel, () =>
            {
                if (frames == 30) cur = cur with { X = cur.X + 90, Y = cur.Y + 25 };
                if (frames == 60) cur = cur with { X = cur.X + 120 };
                pg.Frame(1 / 60.0); return ++frames < 120;
            }, 120, every: 1);
            Expect(bad == 0, $"창을 따라 옮겨 가는 판 다시 칠하기 모자람 {bad}번 (처음 {first})");
            pg.Stop(true);
        }
        return $"두 모양·다섯 테마 다시 칠하기 모자람 0·기다리는 동안 멈춤·창 따라가며 지움·프레임 메모리 {worstBytes}B";
    }

    // ── 봇 표: docs/climb-combo.md 2-2 의 새 목표 (씨 500) ──
    static string CheckBots()
    {
        var easy = Run(DefaultSeeds, ClimbSkill.Easy);
        var good = Run(DefaultSeeds, ClimbSkill.Good);
        BotTargets(easy, good);
        return $"느긋한 평균 {easy.Avg:0}잎·{easy.MedianSec:0}초 틀림+시든 잎 {easy.Pct(easy.Wrong + easy.Wilt):0}% / 잘하는 평균 {good.Avg:0}잎·{good.MedianSec:0}초 틀림 {good.Pct(good.Wrong):0}% (씨 {DefaultSeeds})";
    }

    static void BotTargets(Tally easy, Tally good)
    {
        Expect(easy.Avg is >= 90 and <= 125, $"느긋한 봇 평균 {easy.Avg:0} (90~125)");
        Expect(easy.MedianLeaves is >= 80 and <= 110, $"느긋한 봇 가운데 값 {easy.MedianLeaves:0} (80~110)");
        Expect(easy.MedianSec is >= 30 and <= 45, $"느긋한 봇 한 판 {easy.MedianSec:0.0}초 (30~45)");
        Expect(easy.Past(20) >= .85, $"느긋한 봇 20잎 넘김 {easy.Past(20) * 100:0}% (85% 이상)");
        Expect(easy.Pct(easy.Wrong + easy.Wilt) >= 75 && easy.Pct(easy.Time) <= 25, $"느긋한 봇 틀림+시든 잎 {easy.Pct(easy.Wrong + easy.Wilt):0}%·시간 {easy.Pct(easy.Time):0}% (틀림+시든 잎 ≥ 75·시간 ≤ 25)");
        Expect(easy.Pct(easy.Wrong) >= 70, $"느긋한 봇 틀린 쪽으로 끝남 {easy.Pct(easy.Wrong):0}% (≥ 70)");
        Expect(easy.SecP95 <= 120, $"느긋한 봇 95%가 120초 안 ({easy.SecP95:0}초)");
        Expect(good.Avg is >= 400 and <= 540, $"잘하는 봇 평균 {good.Avg:0} (400~540)");
        Expect(good.MedianLeaves is >= 400 and <= 520, $"잘하는 봇 가운데 값 {good.MedianLeaves:0} (400~520)");
        Expect(good.MedianSec is >= 75 and <= 100, $"잘하는 봇 한 판 {good.MedianSec:0.0}초 (75~100)");
        Expect(good.Past(20) >= .99, $"잘하는 봇 20잎 넘김 {good.Past(20) * 100:0.0}% (99% 이상)");
        Expect(good.Pct(good.Wrong) >= 70, $"잘하는 봇 틀린 쪽으로 끝남 {good.Pct(good.Wrong):0}% (≥ 70)");
        Expect(good.Pct(good.Time) is >= 10 and <= 28, $"잘하는 봇 시간으로 끝남 {good.Pct(good.Time):0}% (10~28)");
        Expect(good.SecP95 <= 210, $"잘하는 봇 95%가 210초 안 ({good.SecP95:0}초)");
        Expect(easy.TierShare(1) is >= .20 and <= .55 && easy.TierShare(3) < .05, $"느긋한 봇 콤보 단계 몫 1단 {easy.TierShare(1) * 100:0}%·3단 {easy.TierShare(3) * 100:0}% (1단 20~55·3단 5 아래)");
        Expect(good.TierShare(3) >= .55, $"잘하는 봇 3단 몫 {good.TierShare(3) * 100:0}% (55% 이상)");
    }
}
