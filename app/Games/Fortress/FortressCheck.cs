using System.Diagnostics;

namespace Bolok;

// 포트리스: 화면 없이 그려 보기(--mode-scene fortress), 속도 재기(--mode-bench fortress), 자가 검사(--selfcheck)
sealed partial class FortressGame
{
    // ── 그려 보기: 시작(안내·차례 화살표·조준경), 힘 모으기, 날아가는 포탄, 불덩이와 새 구멍, 여러 번 터진 뒤 ──
    // 덧붙이는 값: top = 섬이 화면 위쪽에 붙어 있을 때 (바람 상자가 발 아래로 간다) · edge = 그 섬이 판 왼쪽 끝에 있을 때 (조준경 둘레 아래로)
    public static void Scene(string path, int k)
    {
        var host = new SceneHost(scale: k);
        var game = new FortressGame(host) { showMe = true };
        int fw = Math.Min(1920, 1280 * k / 3), fh = Math.Min(1040, 720 * k / 3);
        var field = new Rectangle(0, 0, fw, fh);
        Rectangle Land(double x, double y, double w, double h) => new((int)(fw * x), (int)(fh * y), (int)(fw * w), (int)(fh * h));
        var was = L.Code;
        L.Use("ko");
        double up = GameModes.SceneArg is "top" or "edge" ? .34 : 0, right = GameModes.SceneArg == "top" ? .12 : 0;
        var w = new Fortress(field, new[] { Land(.06 + right, .52 - up, .36 - right, .38), Land(.58, .46 - up + .1 * Math.Sign(up), .36, .44) }, k, new Random(9), "giant", "multi") { Wind = .6 };
        game.Open(w, Session.Fake(field), events: false);
        L.Use(was);
        using var ov = host.Overlay(game);
        var seat = new Random(4);
        w.Aim = -.9;
        SceneKit.Shots(path, ov, field, 5, shot =>
        {
            bool mine = w.MyTurn && w.Stage == Fortress.Phase.Turn;
            if (shot == 1 && mine && w.Clock > 1.2) w.StartCharge();
            if (shot == 2 && mine && w.ChargeFrom >= 0 && w.Power > .68) w.ReleaseCharge();
            if (shot >= 3 && mine && w.Clock - w.TurnAt > .5) { var (a, p) = w.BotAim(seat); w.Fire(w.Me, a, p); }
            w.Update(1 / 60.0);
            game.Sync();
            game.Track();
            return shot switch
            {
                0 => w.Clock >= 1,
                1 => w.Power > .55,
                2 => w.Shells.Count > 0 && w.Shells[0].Vy > -150 * k / 3.0,                             // 꼭대기를 넘어 내려오기 시작
                3 => w.Blasts.Count > 0 && w.Clock - w.Blasts[^1].At >= BlastArt.Sec * .3,              // 불덩이가 한창
                _ => w.Done || w.Booms >= 14 && w.Blasts.Count > 0 && w.Clock - w.Blasts[^1].At >= .1,  // 여러 번 터진 뒤 (작은 폭발이 한창일 때)
            };
        });
    }

    // ── 속도 재기 (--mode-bench fortress): 같은 씨의 한 판을 양쪽 다 자동으로 끝까지 ──
    static readonly Rectangle TestField = new(0, 0, 1920, 1040);
    static readonly Rectangle[] TestLands = { new(150, 500, 600, 400), new(1150, 450, 650, 450) };

    // 볼록이 차례가 오고 0.5초 뒤 어림해서 쏜다 (끝났으면 false)
    static bool Bot(Fortress w, Random seat)
    {
        if (w.Done) return false;
        if (w.MyTurn && w.Stage == Fortress.Phase.Turn && w.Clock - w.TurnAt > .5) { var (a, p) = w.BotAim(seat); w.Fire(w.Me, a, p); }
        return true;
    }

    static (SceneHost, FortressGame, Fortress) BenchGame()
    {
        var host = new SceneHost();
        var game = new FortressGame(host);
        var w = new Fortress(TestField, TestLands, 3, new Random(9), "normal", "normal") { Wind = 0 };
        game.Open(w, Session.Fake(TestField), events: false);
        return (host, game, w);
    }

    public static string BenchRun()
    {
        var (host, game, w) = BenchGame();
        var seat = new Random(4);
        using var ov = host.Overlay(game);
        string whole = Bench.Run("fortress", ov, game, w.Field, () => Bot(w, seat), () => { w.Update(1 / 60.0); game.Sync(); game.Track(); }, 60 * 900);
        return whole + $" · 폭발 {w.Booms}번, {w.Clock:0}초에 {(w.Stage == Fortress.Phase.Won ? "볼록이" : "라이벌")} 승\n" + BenchClip();
    }

    // 실제 창처럼: 바뀐 자리(지난 프레임·이번 프레임, 64px 칸으로 모아)만 잘라서 그릴 때 걸리는 시간. 규칙이 가장 오래 걸린 프레임도 잰다
    static string BenchClip()
    {
        var (host, game, w) = BenchGame();
        var seat = new Random(4);
        using var ov = host.Overlay(game);
        using var bmp = new Bitmap(ov.Width, ov.Height);
        using var g = Graphics.FromImage(bmp);
        using var clip = new Region();
        List<Rectangle> last = new(256), now = new(256);
        var cells = new HashSet<(int, int)>();
        var area = new Rectangle(0, 0, ov.Width, ov.Height);
        long tUpdate = 0, tDirty = 0, tDraw = 0, worstUpdate = 0, bytes = 0, dirtyCells = 0, rects = 0;
        int frames = 0;
        long worstDraw = 0;
        double worstAt = 0;
        void Step(bool measure)
        {
            long a0 = GC.GetAllocatedBytesForCurrentThread(), t0 = Stopwatch.GetTimestamp();
            w.Update(1 / 60.0);
            game.Sync();
            game.Track();
            long t1 = Stopwatch.GetTimestamp();
            now.Clear();
            game.Dirty(ov, now);
            long a1 = GC.GetAllocatedBytesForCurrentThread(), t1b = Stopwatch.GetTimestamp();
            cells.Clear();
            clip.MakeEmpty();
            foreach (var list in new[] { last, now })
                foreach (var r0 in list)
                {
                    var r = Rectangle.Intersect(r0, area);
                    if (r.IsEmpty) continue;
                    for (int y = r.Top / 64; y <= (r.Bottom - 1) / 64; y++)
                        for (int x = r.Left / 64; x <= (r.Right - 1) / 64; x++)
                            if (cells.Add((x, y))) clip.Union(new Rectangle(x * 64, y * 64, 64, 64));
                }
            (last, now) = (now, last);
            g.Clip = clip;
            long a2 = GC.GetAllocatedBytesForCurrentThread(), t2 = Stopwatch.GetTimestamp();
            ov.Render(g);
            long t3 = Stopwatch.GetTimestamp();
            if (!measure) return;
            (tUpdate, tDirty, tDraw, worstUpdate) = (tUpdate + t1 - t0, tDirty + t1b - t1, tDraw + t3 - t2, Math.Max(worstUpdate, t1 - t0));
            rects += last.Count;
            if (t3 - t2 > worstDraw) (worstDraw, worstAt) = (t3 - t2, w.Clock);
            bytes += a1 - a0 + GC.GetAllocatedBytesForCurrentThread() - a2; // 잘라 낼 자리를 만드는 데 쓴 것은 뺀다
            dirtyCells += cells.Count;
            frames++;
        }
        for (int i = 0; i < 30 && Bot(w, seat); i++) Step(false);
        while (frames < 60 * 900 && Bot(w, seat)) Step(true);
        double Us(long ticks) => ticks * 1e6 / Stopwatch.Frequency / Math.Max(1, frames);
        return $"fortress (바뀐 자리만 그리기): {frames}프레임, 규칙 {Us(tUpdate):0.0}µs (가장 느린 프레임 {worstUpdate * 1e3 / Stopwatch.Frequency:0.00}ms), 칠할 자리 {Us(tDirty):0.0}µs ({(double)rects / Math.Max(1, frames):0.0}곳), 그리기 {Us(tDraw):0.0}µs (가장 느린 프레임 {worstDraw * 1e3 / Stopwatch.Frequency:0.00}ms, {worstAt:0.0}초), " +
               $"메모리 {bytes / Math.Max(1, frames)}B/프레임, 다시 칠한 넓이 {dirtyCells * 64.0 * 64 / Math.Max(1, frames) / 1000:0.0}천px²/프레임";
    }

    // ── 자가 검사 (화면 없이): 섬 고르기, 규칙, 라이벌, 조작, 기록, 다시 칠할 자리 ──
    static void Expect(bool ok, string what) { if (!ok) throw new Exception("자가 검사 실패: " + what); }

    public static string Check()
    {
        var field = TestField;
        var screens = new[] { field, new Rectangle(1920, 0, 1920, 1040) };
        var a = new Win(new IntPtr(1), TestLands[0], false);
        var bwin = new Win(new IntPtr(2), TestLands[1], false);
        var covered = new Win(new IntPtr(3), new Rectangle(700, 650, 300, 200), false);
        var full = new Win(new IntPtr(4), new Rectangle(0, 0, 1920, 1040), false);
        var isl = WindowPick.Islands(a, new[] { a, bwin, covered, full }, field, screens, 120, 4);
        Expect(isl.Field == field && isl.Wins.Count == 2 && isl.Wins.Contains(a.R) && isl.Wins.Contains(bwin.R), "섬: 가린 창·최대화 창은 빼고 창모드 창들");
        Expect(WindowPick.Islands(full, new[] { full }, field, screens, 120, 4).Wins.Count == 0, "창모드 창이 없으면 섬도 없다");

        Fortress Make(int seed = 1, string gun = "normal") => new(field, new[] { a.R, bwin.R }, 3, new Random(seed), gun, "normal") { Wind = 0 };
        void Tick(Fortress w, double sec) { for (double t = 0; t < sec; t += 1 / 60.0) w.Update(1 / 60.0); }
        var w = Make();
        Tick(w, .5);
        Expect(!w.Me.Air && !w.Cpu.Air && Math.Abs(w.Me.Y - a.R.Top) < Fortress.Cell && Math.Abs(w.Cpu.Y - bwin.R.Top) < Fortress.Cell && w.Me.X < w.Cpu.X,
            $"볼록이는 왼쪽 섬, 라이벌은 오른쪽 섬 위에 선다: 나 ({w.Me.X:F0},{w.Me.Y:F0},{w.Me.Air},{w.Me.Hp}) 라이벌 ({w.Cpu.X:F0},{w.Cpu.Y:F0},{w.Cpu.Air},{w.Cpu.Hp}) {w.Stage} {w.MyTurn}");

        // 폭발: 땅이 깎이고, 가까우면 다치고 날아간다. 받침이 없어지면 떨어진다
        var boom = Make();
        var (holeX, holeY) = (boom.Me.X + 30, boom.Me.Y + 10);
        boom.Explode(boom.Me.X + 30, boom.Me.Y);
        Expect(!boom.Solid(holeX, holeY) && boom.Me.Hp < boom.Me.Gun.Hp && boom.Me.Air && boom.Me.Vx < 0 && boom.Cpu.Hp == boom.Cpu.Gun.Hp, "폭발: 땅이 깎이고 가까운 쪽만 다쳐 날아간다");
        Expect(boom.Blasts.Count == 1 && boom.Booms == 1, "터진 자리가 목록에 남는다");
        Tick(boom, Fortress.BlastSec + .1);
        Expect(boom.Blasts.Count == 0 && boom.Booms == 1 && !boom.Solid(holeX, holeY), $"터진 지 {Fortress.BlastSec}초가 지나면 목록에서 지운다 (깎인 땅은 그대로)");

        // 깎인 모양은 들쭉날쭉하다 (동그라미가 아니다): 넓이는 원과 비슷하고, 원 안에 남은 칸과 원 밖에 깎인 칸이 다 있다. 같은 자리는 늘 같은 모양
        int rag = 0;
        double worstArea = 0;
        foreach (var (bx, by) in new[] { (450.0, 700.0), (400.5, 650.0), (1475.0, 680.0), (1400.0, 700.5) })
        {
            const double r = 85;
            var (c1, c2) = (Make(), Make(7));
            c1.Explode(bx, by, r);
            c2.Explode(bx, by, r);
            int carved = 0, kept = 0, beyond = 0;
            bool same = true;
            for (double y = by - r * 1.4; y <= by + r * 1.4; y += Fortress.Cell)
                for (double x = bx - r * 1.4; x <= bx + r * 1.4; x += Fortress.Cell)
                {
                    bool gone = !c1.Solid(x, y);
                    double gx = Math.Floor(x / Fortress.Cell) * Fortress.Cell + Fortress.Cell / 2.0 - bx, gy = Math.Floor(y / Fortress.Cell) * Fortress.Cell + Fortress.Cell / 2.0 - by, d = Math.Sqrt(gx * gx + gy * gy);
                    same &= gone == !c2.Solid(x, y);
                    if (gone) carved++;
                    if (!gone && d < r - Fortress.Cell) kept++;
                    if (gone && d > r + Fortress.Cell) beyond++;
                    Expect(gone || d >= r * .65, "반지름의 0.65배 안은 늘 깎인다");
                    Expect(!gone || d <= r * 1.35, "반지름의 1.35배 밖은 안 깎인다");
                }
            double area = carved * Fortress.Cell * Fortress.Cell / (Math.PI * r * r);
            worstArea = Math.Max(worstArea, Math.Abs(area - 1));
            Expect(same, "같은 자리에서 터지면 같은 모양으로 깎인다");
            Expect(area is > .9 and < 1.12, $"깎인 넓이는 원과 비슷하다: {area:P0}");
            if (kept > 20 && beyond > 20) rag++;
        }
        Expect(rag >= 3, $"깎인 자리는 동그랗지 않다: 넷 중 {rag}");

        // 피해와 날아가기는 거리로만 따진다 (깎인 모양과 무관): 같은 거리면 어느 쪽이든 똑같이 다치고 반대로 날아간다
        var fair = Make();
        (fair.Cpu.X, fair.Cpu.Y) = (fair.Me.X + 70, fair.Me.Y);
        fair.Explode(fair.Me.X + 35, fair.CenterY(fair.Me), 60);
        Expect(fair.Me.Hp == fair.Cpu.Hp && fair.Me.Hp < fair.Me.Gun.Hp && Math.Abs(fair.Me.Vx + fair.Cpu.Vx) < 1e-6 && fair.Me.Vx < 0 && fair.Me.Vy == fair.Cpu.Vy,
            $"같은 거리면 같은 피해: 나 {fair.Me.Hp} 라이벌 {fair.Cpu.Hp}");

        var drop = Make();
        double y0 = drop.Me.Y;
        drop.Explode(drop.Me.X, drop.Me.Y + 25); // 발밑 껍질까지 깎이게 (가장 얕게 깎이는 쪽도 반지름의 0.65배 = 32px)
        Tick(drop, 1.5);
        Expect(!drop.Me.Air && drop.Me.Y > y0 + 20, "받침이 깎이면 떨어져 아래에 선다");
        // 섬 끝에서 걸어 나가면 퐁당
        var walk = Make();
        for (int i = 0; i < 60 * 20 && !walk.Me.Dead; i++) { walk.Walk(walk.Me, -1, 1 / 60.0); walk.Update(1 / 60.0); }
        Expect(walk.Me.Out && walk.Me.Dead, "섬 밖으로 걸어 나가면 물에 빠진다");

        // 섬은 창 전체: 발밑을 끝까지 파면 뚫려서 떨어지고 퐁당
        Expect(w.Lands.Contains(a.R) && w.Lands.Contains(bwin.R), "섬은 창 높이 그대로");
        var dig = Make();
        for (int depth = 25; depth < a.R.Height + 60; depth += 50) dig.Explode(dig.Me.X, dig.Me.Y + depth);
        Tick(dig, 3);
        Expect(dig.Me.Out, $"바닥을 다 파면 떨어져 퐁당 ({dig.Me.Y:F0})");

        // 대포마다 체력이 다르고, 바람은 모든 포탄을 민다
        foreach (var gun in Fortress.Guns)
        {
            var t = Make(gun: gun.Name);
            Expect(t.Me.Hp == gun.Hp, $"{gun.Label} 체력 {gun.Hp}");
        }
        Expect(Fortress.Guns.Select(g => g.Hp).SequenceEqual(new[] { 200, 300, 400 }) && Fortress.GunOf("multi").Shells == 9 && Fortress.GunOf(null).Name == "normal", "멀티캐논 9발/200, 왕대포/300, 일반대포/400");
        (double X, double R) Land(Fortress x, double wind)
        {
            x.Wind = wind;
            (double X, double R)? at = null;
            x.OnEvent = e => { if (e == "boom") at ??= (x.Blasts[^1].X, x.Blasts[^1].R); };
            x.Fire(x.Me, -Math.PI / 2, .5);
            for (int i = 0; i < 60 * 8 && at == null; i++) x.Update(1 / 60.0);
            return at ?? (double.NaN, 0);
        }
        foreach (var gun in new[] { "normal", "giant" })
        {
            var calm = Make(gun: gun); double still = Land(calm, 0).X;
            var windy = Make(gun: gun); double pushed = Land(windy, 1).X;
            Expect(Math.Abs(still - calm.Me.X) < 20 && pushed > still + 40, $"바람이 {gun} 포탄을 민다: {still:F0} → {pushed:F0}");
        }
        double bigR = Land(Make(gun: "giant"), 0).R, smallR = Land(Make(gun: "normal"), 0).R;
        Expect(bigR > smallR * 1.5, "왕대포 구멍이 더 크다");
        var multi = Make(gun: "multi");
        int pops = 0;
        double popR = 0;
        multi.OnEvent = e => { if (e == "boom") { pops++; popR = Math.Max(popR, multi.Blasts[^1].R); } };
        multi.Fire(multi.Me, -75 * Math.PI / 180, .45); // 제 섬 위로 높이 (섬 사이로 빠지면 물에 떨어져 안 터진다)
        Expect(multi.Shells.Count == 9 && multi.Shells.Select(x => Math.Round(x.Vx)).Distinct().Count() == 9, "멀티캐논은 9발을 부채꼴로");
        for (int i = 0; i < 60 * 8 && multi.Stage == Fortress.Phase.Flying; i++) multi.Update(1 / 60.0);
        Expect(pops >= 7 && popR < smallR, $"멀티캐논은 작은 폭발 여럿 ({pops})");

        // 차례: 내가 쏘고 가라앉으면 라이벌, 라이벌도 쏘면 다시 나. 시간이 다 되면 넘어간다
        var turn = Make();
        turn.Fire(turn.Me, -Math.PI / 2, .3);
        for (int i = 0; i < 60 * 8 && turn.MyTurn; i++) turn.Update(1 / 60.0);
        Expect(!turn.MyTurn && turn.Stage == Fortress.Phase.Turn, "내가 쏘고 나면 라이벌 차례");
        // 라이벌은 생각하는 동안 계산을 프레임마다 나눠서 한다: 한 번에 계산한 것과 같은 각도·힘이 나온다
        var twin = Make();
        twin.Fire(twin.Me, -Math.PI / 2, .3);
        for (int i = 0; i < 60 * 8 && twin.MyTurn; i++) twin.Update(1 / 60.0);
        var (planA, planP) = twin.Plan();
        double shotV = 0;
        turn.OnEvent = e => { if (e == "fire") shotV = Math.Sqrt(turn.Shells[0].Vx * turn.Shells[0].Vx + turn.Shells[0].Vy * turn.Shells[0].Vy); };
        for (int i = 0; i < 60 * 12 && !turn.MyTurn && !turn.Done; i++) turn.Update(1 / 60.0);
        Expect(turn.MyTurn || turn.Stage is Fortress.Phase.Won or Fortress.Phase.Lost, "라이벌이 쏘고 나면 다시 내 차례");
        Expect(turn.CpuAim == planA && Math.Abs(shotV - planP * Fortress.MaxShot) < 1e-6, $"라이벌 계산은 나눠서 해도 같다: {turn.CpuAim:F4} · {planA:F4}, 힘 {shotV / Fortress.MaxShot:F3} · {planP:F3}");
        var idle = Make();
        Tick(idle, Fortress.TurnSec + 1.5);
        Expect(!idle.MyTurn, "시간이 다 되면 차례가 넘어간다");

        // 라이벌은 볼록이 근처를 노린다 (빗나감 없이 계산하면 가까이 떨어진다)
        int near = 0;
        for (int seed = 0; seed < 6; seed++)
        {
            var ai = Make(seed);
            ai.Wind = (seed - 3) / 3.0;
            var (ang, pow) = ai.Plan(miss: 0);
            ai.MyTurn = false;
            double? at = null;
            ai.OnEvent = e => { if (e == "boom") at = Math.Abs(ai.Blasts[^1].X - ai.Me.X); };
            ai.Fire(ai.Cpu, ang, pow);
            for (int i = 0; i < 60 * 8 && at == null; i++) ai.Update(1 / 60.0);
            if (at < 70) near++;
        }
        Expect(near >= 5, $"라이벌 계산은 볼록이 근처에 떨어진다: {near}/6");
        // 실제로는 흔들려 쏜다: 첫 발은 대부분 빗나간다
        int firstNear = 0;
        for (int seed = 0; seed < 12; seed++)
        {
            var ai = Make(seed);
            ai.Wind = (seed % 5 - 2) / 2.0;
            var (ang, pow) = ai.Plan();
            ai.MyTurn = false;
            double? at = null;
            ai.OnEvent = e => { if (e == "boom") at ??= Math.Abs(ai.Blasts[^1].X - ai.Me.X); };
            ai.Fire(ai.Cpu, ang, pow);
            for (int i = 0; i < 60 * 8 && at == null; i++) ai.Update(1 / 60.0);
            if (at < 70) firstNear++;
        }
        Expect(firstNear <= 5, $"라이벌 첫 발은 대부분 빗나간다: {firstNear}/12 이 70px 안");

        // 라이벌이 다 다치면 이긴다
        var win = Make();
        for (int i = 0; i < 20 && !win.Cpu.Dead; i++) win.Explode(win.Cpu.X, win.CenterY(win.Cpu)); // 쓰러질 때까지
        win.Fire(win.Me, -Math.PI / 2, .2);
        Tick(win, 6);
        Expect(win.Stage == Fortress.Phase.Won, "라이벌이 쓰러지면 이긴다");

        // 둘 다 계산대로 쏘는 판: 끝까지 간다. 터진 자리 목록은 쌓이지 않는다
        var duel = Make(9);
        var seat = new Random(4);
        int mostBlasts = 0;
        for (int i = 0; i < 60 * 900 && Bot(duel, seat); i++)
        {
            duel.Update(1 / 60.0);
            mostBlasts = Math.Max(mostBlasts, duel.Blasts.Count);
        }
        Expect(duel.Done, $"한 판이 끝난다: 나 {duel.Me.Hp}, 라이벌 {duel.Cpu.Hp}, {duel.Clock:F0}초");
        Expect(duel.Booms >= 8 && mostBlasts <= 9, $"터진 자리 목록은 쌓이지 않는다: {duel.Booms}번 터지는 동안 가장 많을 때 {mostBlasts}개");

        string play = ModuleChecks(field, a.R, bwin.R);
        string hud = HudChecks();
        return $"포트리스 라이벌: 계산만 하면 6번 중 {near}번 70px 안, 실제 첫 발은 12번 중 {firstNear}번, 시험 대결 {duel.Clock:F0}초에 {(duel.Stage == Fortress.Phase.Won ? "볼록이" : "라이벌")} 승, " +
               $"깎인 넓이는 원과 {worstArea:P0} 안, {play}, {hud}";
    }

    // 바람 상자 자리: 어느 모습·배율·언어에서도, 볼록이가 왼쪽 섬이든 오른쪽 섬이든, 섬이 낮든 높든 판 끝에 붙었든
    // 볼록이 가까이에 있고 볼록이·체력 막대(힘 막대·차례 화살표 자리까지)·조준경·안내 말풍선·판 끝과 겹치지 않는다. 걷고 뛰는 동안에는 제자리에 있다가 멈추면 한 번 옮겨 간다
    static string HudChecks()
    {
        var field = TestField;
        var cases = new (string Name, bool High, int Left, int Mode)[] { ("낮은 섬", false, 400, 0), ("높은 섬", true, 400, 1), ("판 끝의 높은 섬", true, 0, 2) };
        string was = L.Code;
        int looked = 0, strolls = 0;
        try
        {
            foreach (string form in Showcase.Forms)
                for (int k = 2; k <= 6; k++)
                    foreach (var c in cases)
                        for (int side = 0; side < 2; side++)
                            foreach (var (code, _) in L.Languages)
                            {
                                if (code != "ko" && (form != Showcase.Forms[0] || side != 0)) continue; // 말풍선 크기만 언어를 탄다: 다른 언어는 첫 모습으로만
                                L.Use(code);
                                int top = c.High ? 40 * k + 4 : 620; // 높은 섬: 창 위에 볼록이 한 칸만 남는다 (WindowPick 이 섬으로 받아 주는 가장 높은 창)
                                var lands = new[] { new Rectangle(c.Left, top, 700, 1000 - top), new Rectangle(field.Right - c.Left - 700, top, 700, 1000 - top) };
                                var host = new SceneHost(form, k);
                                var game = new FortressGame(host);
                                var w = new Fortress(field, lands, k, new Random(3), "normal", "normal") { Wind = 0 };
                                if (side == 1) (w.Me.X, w.Me.Y, w.Cpu.X, w.Cpu.Y) = (w.Cpu.X, w.Cpu.Y, w.Me.X, w.Me.Y); // 볼록이가 오른쪽 섬에
                                game.Open(w, Session.Fake(field), events: false);
                                using var ov = host.Overlay(game);
                                string what = $"{c.Name}, {form} ×{k}, {(side == 0 ? "왼쪽" : "오른쪽")}, {code}";
                                Expect(game.hudMode == c.Mode, $"바람 상자 자리 고르기 ({what}): {game.hudMode}");
                                LookHud(game, ov, w, what);
                                looked++;
                                if (code != "ko" || k is not (2 or 3 or 6)) { game.DropTiles(); game.DropGuide(); continue; }
                                // 걷는 동안·뛰어 있는 동안에는 제자리, 멈춰 서면 한 번 스르륵 옮겨 가서 다시 가만히 있는다
                                void Step() { w.Update(1 / 60.0); game.Sync(); game.Track(); }
                                var (r0, x0, dir) = (game.HudRect(w), w.Me.X, side == 0 ? 1 : -1);
                                for (int i = 0; i < 90; i++)
                                {
                                    w.Walk(w.Me, dir, 1 / 60.0);
                                    Step();
                                    Expect(game.HudRect(w) == r0, $"걷는 동안 바람 상자는 제자리 ({what})");
                                }
                                w.Jump(w.Me);
                                for (int i = 0; i < 240 && w.Me.Air; i++) { Step(); Expect(game.HudRect(w) == r0, $"뛰어 있는 동안 바람 상자는 제자리 ({what})"); }
                                int moves = 0, turns = 0, way = 0, still = 0;
                                var prev = r0;
                                for (int i = 0; i < 150; i++)
                                {
                                    Step();
                                    var r = game.HudRect(w);
                                    int dx = Math.Sign(r.X - prev.X);
                                    if (r != prev) { moves++; still = 0; } else still++;
                                    if (dx != 0 && way != 0 && dx != way) turns++;
                                    if (dx != 0) way = dx;
                                    prev = r;
                                }
                                Expect(Math.Abs(w.Me.X - x0) > 20 * k && moves is >= 3 and <= 45 && turns == 0 && still >= 60 && way == dir,
                                    $"멈춰 서면 바람 상자가 한 번 옮겨 간다 ({what}): {moves}프레임 동안, 되돌아간 적 {turns}번, 그 뒤 {still}프레임 가만히");
                                Expect(prev == game.HudAt(w, game.hudMode) && game.hudMode == c.Mode, $"옮겨 간 자리도 같은 쪽 ({what}): {game.hudMode}");
                                LookHud(game, ov, w, what + ", 걸은 뒤");
                                strolls++;
                                game.DropTiles();
                                game.DropGuide();
                            }
        }
        finally { L.Use(was); }
        return $"바람 상자 자리 {looked}가지(모습 {Showcase.Forms.Length} × 배율 2~6 × 섬 {cases.Length} × 좌우, 언어 {L.Languages.Length})에서 안 겹침, 걷고 뛰는 {strolls}판 동안 제자리";
    }

    static void LookHud(FortressGame game, Overlay ov, Fortress w, string what)
    {
        int k = w.K, back = Back(w);
        var (me, r) = (w.Me, game.HudRect(w));
        var pet = Fx.PetRect(me.X, me.Y, 40 * k);
        Expect(Rectangle.Inflate(w.Field, -k, -k).Contains(r), $"바람 상자는 판 안에 있다 ({what}): {r}");
        Expect(!r.IntersectsWith(pet), $"바람 상자가 볼록이를 가리지 않는다 ({what}): {r} · {pet}");
        Expect(!r.IntersectsWith(game.LabelRect(w, me)), $"바람 상자가 체력 막대·힘 막대와 안 겹친다 ({what}): {r} · {game.LabelRect(w, me)}");
        Expect(!r.IntersectsWith(game.GuideRect(ov, w)), $"바람 상자가 안내 말풍선과 안 겹친다 ({what}): {r} · {game.GuideRect(ov, w)}");
        int gap = Math.Max(Math.Max(pet.Left - r.Right, r.Left - pet.Right), Math.Max(pet.Top - r.Bottom, r.Top - pet.Bottom));
        Expect(gap <= 40 * k, $"바람 상자는 볼록이 가까이에 있다 ({what}): {gap}px");
        double keep = w.Aim;
        for (int deg = 0; deg < 360; deg += 2) // 조준경이 도는 둘레 (0° 오른쪽, 90° 아래)
        {
            double a = deg * Math.PI / 180;
            // 발 아래 뒤쪽으로 뻗은 상자: 뒤쪽 아래나 제 발밑(60° 넘게 아래)을 겨눌 때만 조준경이 그 위를 지난다 (조준경이 위에 그려진다)
            if (game.hudMode == 1 && Math.Sin(a) > 0 && (Math.Cos(a) * back > 0 || deg is > 60 and < 120)) continue;
            w.Aim = a;
            var (ax, ay) = AimAt(w);
            Expect(!r.IntersectsWith(Rectangle.Inflate(game.aim.Box(ax, ay), 1, 1)), $"바람 상자가 조준경과 안 겹친다 ({what}, {deg}°): {r}");
        }
        w.Aim = keep;
    }

    // 모듈: 조작(마우스·방향키만, 판이 앞 창일 때만), 시작·끝내기, 기록, 하위 메뉴, 디버그 명령, 다시 칠할 자리
    static string ModuleChecks(Rectangle field, Rectangle landA, Rectangle landB)
    {
        var keys = new HashSet<Keys> { Keys.A, Keys.D, Keys.W, Keys.Space, Keys.Tab };
        bool front = true;
        var mouse = new Point(900, 300);
        var host = new SceneHost();
        var game = new FortressGame(host);
        Fortress New(int seed = 4) => new(field, new[] { landA, landB }, 3, new Random(seed), "normal", "normal") { Wind = 0 };
        var play = New();
        var ses = Session.Fake(field, keys.Contains, () => front, () => mouse);
        game.Open(play, ses, events: false);
        host.Pet.StartPuppet();
        void Frames(int n) { for (int i = 0; i < n && game.Running; i++) game.Frame(1 / 60.0); }
        Frames(30);
        double x0 = play.Me.X;
        Frames(20);
        Expect(play.Me.X == x0 && !play.Me.Air && play.Stage == Fortress.Phase.Turn, "글자 키·Space 로는 걷지도 뛰지도 쏘지도 않는다");
        Expect(Math.Abs(play.Aim - Math.Atan2(mouse.Y - play.CenterY(play.Me), mouse.X - play.Me.X)) < 1e-9 && host.Pet.X == play.Me.X && host.Pet.Y == play.Me.Y, "마우스로 겨누고, 볼록이 본체가 내 햄스터 자리에 있다");
        keys.Add(Keys.Right);
        Frames(20);
        Expect(play.Me.X > x0 + 20 && host.Pet.Pose == "walk", $"→ 로 걷는다: {play.Me.X - x0:0}");
        (front, x0) = (false, play.Me.X);
        Frames(20);
        Expect(play.Me.X == x0, "다른 창을 쓰는 동안 누른 → 로는 안 걷는다");
        front = true;
        keys.Remove(Keys.Right);
        keys.Add(Keys.Left);
        Frames(10);
        Expect(play.Me.X < x0 - 5, "← 로 걷는다");
        keys.Remove(Keys.Left);
        keys.Add(Keys.Up);
        Frames(2);
        Expect(play.Me.Air && play.Me.Vy < 0, "↑ 로 뛴다");
        Frames(90);
        Expect(!play.Me.Air, "누르고 있어도 한 번만 뛴다");
        keys.Remove(Keys.Up);
        front = false;
        keys.Add(Keys.Up);
        Frames(5);
        Expect(!play.Me.Air, "다른 창을 쓰는 동안 누른 ↑ 로는 안 뛴다");
        keys.Remove(Keys.Up);
        front = true;
        // 왼쪽 버튼: 누르고 있으면 힘이 차고 놓으면 쏜다
        ses.Press(MouseButtons.Left, true);
        Frames(30);
        Expect(play.ChargeFrom >= 0 && play.Power is > .3 and < .6 && play.Stage == Fortress.Phase.Turn && host.Pet.Pose == "angry", $"왼쪽 버튼을 누르고 있으면 힘이 찬다: {play.Power:F2}");
        x0 = play.Me.X;
        keys.Add(Keys.Right);
        Frames(5);
        keys.Remove(Keys.Right);
        Expect(play.Me.X == x0, "힘을 모으는 동안은 걷지 않는다");
        ses.Press(MouseButtons.Left, false);
        Frames(1);
        Expect(play.Stage == Fortress.Phase.Flying && play.Shells.Count == 1 && play.ChargeFrom < 0, "놓으면 쏜다");
        ses.Press(MouseButtons.Right, true);
        ses.Press(MouseButtons.Right, false);
        Frames(2);
        Expect(play.Shells.Count <= 1, "오른쪽 버튼은 쓰지 않는다");

        // 하는 중에 또 시작해도 판은 그대로. 보스 키(Stop(true))는 표정 없이 바로 끝나고 그릴 것이 남지 않는다
        game.Start();
        game.Choices![0].Start();
        Expect(ReferenceEquals(game.game, play) && game.Choices.Count == Fortress.Guns.Length, "하는 중에 또 골라도 판은 그대로다 (하위 메뉴: 대포 셋)");
        Expect(game.Command(new[] { "worms", "stop" }) && !game.Running && host.Pet.Mode == Mode.Wander && !host.Pet.Emoting, "보스 키·worms stop 으로 끝내면 표정 없이 바로 끝난다");
        var none = new List<Rectangle>();
        using (var ov0 = host.Overlay(game)) game.Dirty(ov0, none);
        Expect(none.Count == 0 && game.tiles.Length == 0, "끝나면 그릴 것이 남지 않는다");
        Expect(!game.Command(new[] { "raid" }) && !game.Command(Array.Empty<string>()), "남의 명령은 받지 않는다");

        // 디버그 명령: worms fire <각도> <힘> (내 차례일 때만), fortress 로 써도 같다
        var cmd = New();
        game.Open(cmd, Session.Fake(field), events: false);
        host.Pet.StartPuppet();
        Expect(game.Command(new[] { "fortress", "fire", "-60", "0.5" }) && cmd.Stage == Fortress.Phase.Flying && cmd.Shells.Count == 1, "worms fire 로 쏜다");
        game.Stop(true);

        // 기록: 이기면 한 번 더해지고, 옛 저장 칸(WormsWins)도 넘어온다
        Expect(game.SaveState() == null && !game.Stats().Single().Played, "아직 못 이겼으면 기록이 없다");
        game.LoadState(GameModes.WithLegacy(new SaveData { WormsWins = 2 }).GetValueOrDefault(game.Id));
        Expect(game.wins == 2, "옛 기록(WormsWins)이 넘어온다");
        var won = New();
        game.Open(won, Session.Fake(field), events: false);
        host.Pet.StartPuppet();
        for (int i = 0; i < 20 && !won.Cpu.Dead; i++) won.Explode(won.Cpu.X, won.CenterY(won.Cpu));
        won.Fire(won.Me, -Math.PI / 2, .2);
        Frames(60 * 12);
        Expect(!game.Running && game.wins == 3 && game.SaveState() == "{\"Wins\":3}" && game.Stats().Single().Played && host.Pet.Emoting, "이기면 기록이 하나 늘고 기뻐한다");
        host.Pet.Hush();

        // 다시 칠할 자리: 걷고 뛰고 서로 쏘는 판, 그리고 이기는 판. 바뀐 자리만 다시 그려도 화면에 지나간 그림이 남지 않는다
        var small = new Rectangle(0, 0, 960, 600);
        var paint = new Fortress(small, new[] { new Rectangle(60, 300, 340, 240), new Rectangle(560, 270, 340, 270) }, 3, new Random(5), "giant", "multi") { Wind = .4 };
        game.Open(paint, Session.Fake(small), events: false);
        using var ov = host.Overlay(game);
        int n = 0;
        var (bad, first) = SceneKit.Repaint(ov, game, small, () =>
        {
            if (paint.Done || paint.Booms >= 12 && paint.Stage == Fortress.Phase.Turn || ++n > 60 * 80) return false;
            if (paint.MyTurn && paint.Stage == Fortress.Phase.Turn)
            {
                double t = paint.Clock - paint.TurnAt;
                paint.Aim = -1 + Math.Sin(paint.Clock * 3) * .5; // 조준경이 움직인다
                if (t < .4) paint.Walk(paint.Me, 1, 1 / 60.0);
                else if (t < .45) paint.Jump(paint.Me);
                else if (t > 1.3 && paint.ChargeFrom < 0) paint.StartCharge();
                else if (paint.ChargeFrom >= 0 && paint.Power > .5) paint.ReleaseCharge();
            }
            paint.Update(1 / 60.0);
            game.Sync();
            game.Track();
            return true;
        }, 60 * 90, every: 7);
        Expect(bad == 0 && paint.Booms >= 2, $"바뀐 자리만 다시 칠해도 화면이 맞다: {bad}번 다름 (처음 {first}프레임), 폭발 {paint.Booms}번");
        int holes = game.tiles.Count(t => t != null);
        var end = new Fortress(small, new[] { new Rectangle(60, 300, 340, 240), new Rectangle(560, 270, 340, 270) }, 3, new Random(6), "normal", "normal");
        game.Open(end, Session.Fake(small), events: false);
        for (int i = 0; i < 20 && !end.Cpu.Dead; i++) end.Explode(end.Cpu.X, end.CenterY(end.Cpu));
        end.Fire(end.Me, -Math.PI / 2, .2);
        (bad, first) = SceneKit.Repaint(ov, game, small, () =>
        {
            if (end.Done) return false;
            end.Update(1 / 60.0);
            game.Sync();
            game.Track();
            return true;
        }, 60 * 30, every: 5);
        Expect(bad == 0 && end.Stage == Fortress.Phase.Won, $"이기는 장면도 바뀐 자리만 다시 칠해 맞다: {bad}번 다름 (처음 {first}프레임), {end.Stage}");
        game.game = null;
        game.DropTiles();
        game.DropGuide();
        return $"조작은 마우스·←→·↑ 만, 구멍 그림 조각 {holes}장";
    }
}
