using System.Diagnostics;

namespace Bolok;

// 포트리스: 화면 없이 그려 보기(--mode-scene fortress), 속도 재기(--mode-bench fortress), 자가 검사(--selfcheck)
sealed partial class FortressGame
{
    // ── 그려 보기: 시작(안내·차례 화살표·조준경), 힘 모으기, 날아가는 포탄, 불덩이와 새 구멍,
    //    다음 내 차례(힘 막대 위 지난 힘 세모, 지금 조준경 옆에 흐린 지난 각도 조준경), 여러 번 터진 뒤 ──
    // 덧붙이는 값: top = 섬이 화면 위쪽에 붙어 있을 때 (바람 상자가 발 아래로 간다) · edge = 그 섬이 판 왼쪽 끝에 있을 때 (조준경 둘레 아래로)
    //             guns = 새 대포 네 가지가 쏘는 모습 (SceneGuns)
    //             check = 그림 대신 포트리스 자가 검사만 돌려 결과를 찍는다 (고치는 동안 빨리 볼 때)
    public static void Scene(string path, int k)
    {
        if (GameModes.SceneArg == "guns") { SceneGuns(path, k); return; }
        if (GameModes.SceneArg == "check") { Console.WriteLine(Check()); return; } // 그림 없이 포트리스 자가 검사만 (전체 --selfcheck 는 몇 분 걸린다)
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
        SceneKit.Shots(path, ov, field, 6, shot =>
        {
            bool mine = w.MyTurn && w.Stage == Fortress.Phase.Turn;
            if (shot == 1 && mine && w.Clock > 1.2) w.StartCharge();
            if (shot == 2 && mine && w.ChargeFrom >= 0 && w.Power > .68) w.ReleaseCharge();
            if (shot == 4 && mine && w.Clock - w.TurnAt > .5) { w.Aim = w.LastAim + .5; w.StartCharge(); } // 지난번보다 조금 낮게 겨누고 다시 힘을 모은다
            if (shot >= 5 && mine && w.Clock - w.TurnAt > .5) { var (a, p) = w.BotAim(seat); w.Fire(w.Me, a, p); }
            w.Update(1 / 60.0);
            game.Sync();
            game.Track();
            return shot switch
            {
                0 => w.Clock >= 1,
                1 => w.Power > .55,
                2 => w.Shells.Count > 0 && w.Shells[0].Vy > -150 * k / 3.0,                             // 꼭대기를 넘어 내려오기 시작
                3 => w.Blasts.Count > 0 && w.Clock - w.Blasts[^1].At >= BlastArt.Sec * .3,              // 불덩이가 한창
                4 => mine && w.ChargeFrom >= 0 && w.Power > .4,                                         // 지난 힘 세모까지 아직 덜 찼다
                _ => w.Done || w.Booms >= 14 && w.Blasts.Count > 0 && w.Clock - w.Blasts[^1].At >= .1,  // 여러 번 터진 뒤 (작은 폭발이 한창일 때)
            };
        });
    }

    // ── 새 대포 네 가지 (--mode-scene fortress 그림.png guns): 튕김탄(한 번 튕겨 다시 나는 중) · 집속탄(막 넷으로 갈라짐) · 드릴탄(땅속을 파고드는 중) · 밀치기탄(라이벌이 밀려 날아감) ──
    // 장마다 새 판이고 볼록이가 그 대포를 쓴다 (라이벌은 다른 새 대포: 머리 위 대포 그림 넷이 다 나온다). 라이벌 가까이에 떨어지는 각도와 힘은 미리 날려 보고 고른다
    static void SceneGuns(string path, int k)
    {
        var host = new SceneHost(scale: k);
        var game = new FortressGame(host) { showMe = true };
        int fw = Math.Min(1920, 1280 * k / 3), fh = Math.Min(1040, 720 * k / 3);
        var field = new Rectangle(0, 0, fw, fh);
        var lands = new[] { new Rectangle((int)(fw * .06), (int)(fh * .52), (int)(fw * .36), (int)(fh * .38)), new Rectangle((int)(fw * .58), (int)(fh * .46), (int)(fw * .36), (int)(fh * .44)) };
        string[] mine = { "bounce", "cluster", "drill", "push" }, theirs = { "drill", "push", "bounce", "cluster" };
        double sf = k / 3.0;
        Fortress New(int n)
        {
            var w = new Fortress(field, lands, k, new Random(9), mine[n], theirs[n]) { Wind = 0 };
            for (int i = 0; i < 60 * GuideSec + 20; i++) w.Update(1 / 60.0); // 안내 말풍선이 걷힌 뒤에 쏜다
            return w;
        }
        bool Seen(Fortress w) => w.Shells.All(s => s.Y > field.Top + 30 * sf && s.X > field.Left && s.X < field.Right); // 포탄이 다 그림 안에 있다
        bool Now(Fortress w, int n) => n switch
        {
            0 => w.Shells.Count == 1 && w.Shells[0].Bounces == 1 && w.Shells[0].Fuse < Fortress.BounceFuse - .2 && Seen(w),
            1 => w.Shells.Count == 4 && w.Shells[0].Vy > 200 * sf && Seen(w),
            2 => w.Shells.Count == 1 && w.Shells[0].DrillLeft >= 0 && w.Shells[0].DrillLeft < w.Shells[0].Drill * .35,
            _ => w.Blasts.Count > 0 && w.Cpu.Air && w.Clock - w.Blasts[^1].At >= .22,
        };
        (double A, double P) Find(int n)
        {
            var (best, at) = (double.MaxValue, (-1.0, .6));
            for (double deg = -80; deg <= -20; deg += 3)
                for (double p = .35; p <= 1; p += .05)
                {
                    var w = New(n);
                    bool seen = false;
                    double near = double.MaxValue;
                    w.OnEvent = e => { if (e == "boom") near = Math.Min(near, Math.Sqrt(Math.Pow(w.Blasts[^1].X - w.Cpu.X, 2) + Math.Pow(w.Blasts[^1].Y - w.CenterY(w.Cpu), 2))); };
                    w.Fire(w.Me, deg * Math.PI / 180, p);
                    for (int i = 0, after = 30; i < 600 && (w.Stage == Fortress.Phase.Flying || after-- > 0); i++) { w.Update(1 / 60.0); seen |= Now(w, n); }
                    if (seen && near < best) (best, at) = (near, (deg * Math.PI / 180, p));
                }
            return at;
        }
        var was = L.Code;
        L.Use("ko");
        Fortress? play = null;
        int cur = -1, frames = 0;
        using var ov = host.Overlay(game);
        SceneKit.Shots(path, ov, field, mine.Length, shot =>
        {
            if (cur != shot)
            {
                var (a, p) = Find(cur = shot);
                play = New(shot);
                game.Open(play, Session.Fake(field), events: false);
                play.Aim = a;
                play.Fire(play.Me, a, p);
                frames = 0;
            }
            play!.Update(1 / 60.0);
            game.Sync();
            game.Track();
            return Now(play, shot) || ++frames > 600; // 못 찾았으면 그 장은 쏜 뒤 모습으로

        });
        L.Use(was);
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
        Expect(Fortress.Guns.Select(g => g.Hp).SequenceEqual(new[] { 200, 300, 400, 350, 250, 350, 300 }) && Fortress.GunOf("multi").Shells == 9 && Fortress.GunOf(null).Name == "normal",
            "멀티캐논 9발/200, 왕대포/300, 일반대포/400, 튕김탄/350, 집속탄/250, 드릴탄/350, 밀치기탄/300");
        Expect(GunRows.Length == Fortress.Guns.Length && GunRows.All(r => r.Length == GunH && r.All(line => line.Length == GunW)) && GunRows.Select(r => string.Concat(r)).Distinct().Count() == GunRows.Length,
            "대포마다 제 그림이 있다 (12×10, 서로 다르다)");
        string guns = NewGunChecks(field, a.R, bwin.R);
        // 라이벌 대포: 정해 주지 않으면 내 것이 아닌 둘 중 아무거나 (같은 대포로 맞붙지 않는다), 정해 주면 그대로 (같은 것이어도)
        foreach (var gun in Fortress.Guns)
        {
            var seen = new HashSet<string>();
            for (int seed = 0; seed < 60; seed++)
            {
                var t = new Fortress(field, new[] { a.R, bwin.R }, 3, new Random(seed), gun.Name);
                Expect(t.Me.Gun == gun && t.Cpu.Gun != gun && t.Cpu.Hp == t.Cpu.Gun.Hp, $"라이벌은 나와 다른 대포를 쓴다 ({gun.Name}, 씨 {seed}): {t.Cpu.Gun.Name}");
                seen.Add(t.Cpu.Gun.Name);
            }
            Expect(seen.Count == Fortress.Guns.Length - 1, $"라이벌 대포는 나머지 둘이 다 나온다 ({gun.Name}): {string.Join(" ", seen)}");
            foreach (var other in Fortress.Guns)
                Expect(new Fortress(field, new[] { a.R, bwin.R }, 3, new Random(1), gun.Name, other.Name).Cpu.Gun == other, $"라이벌 대포를 정해 주면 그대로 ({gun.Name} · {other.Name})");
        }
        Expect(new Fortress(field, new[] { a.R, bwin.R }, 3, new Random(1)).Cpu.Gun.Name != "normal", "대포를 안 고르면 나는 일반대포, 라이벌은 다른 것");
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

        // 지난 발: 내가 쏜 각도와 힘이 다음 내 차례까지 남는다. 라이벌이 쏜 것은 적지 않고, 새 판은 비어 있다
        var memo = Make();
        Expect(!memo.HasLast && !GhostOn(memo) && Mark(memo, memo.Me) is >= 90 and < 1000, "첫 발 전에는 지난 발 표시가 없다 (차례 화살표만)");
        memo.Fire(memo.Me, -1.25, .62);
        Expect(memo.HasLast && memo.LastAim == -1.25 && memo.LastPower == .62 && !GhostOn(memo), "쏘면 그 각도와 힘을 적어 둔다 (포탄이 나는 동안에는 안 보인다)");
        int cpuFired = 0;
        memo.OnEvent = e => { if (e == "fire") cpuFired++; };
        for (int i = 0; i < 60 * 8 && memo.MyTurn; i++) memo.Update(1 / 60.0);
        Expect(!memo.MyTurn && !GhostOn(memo) && Mark(memo, memo.Me) == 0, "라이벌 차례에는 지난 발 표시가 없다");
        for (int i = 0; i < 60 * 12 && !memo.MyTurn && !memo.Done; i++) memo.Update(1 / 60.0);
        Expect(cpuFired == 1 && memo.MyTurn && memo.Stage == Fortress.Phase.Turn, $"라이벌이 한 번 쏘고 다시 내 차례: {cpuFired}번, {memo.Stage}");
        Expect(memo.HasLast && memo.LastAim == -1.25 && memo.LastPower == .62, $"라이벌이 쏴도 지난 발은 내 것 그대로: {memo.LastAim:F2} · {memo.LastPower:F2}");
        Expect(GhostOn(memo) && Mark(memo, memo.Me) == 1000 && LastTip(memo) == (int)(20 * 3 * .62), "다음 내 차례에는 누르기 전부터 빈 힘 막대와 지난 힘 세모, 흐린 지난 각도 조준경이 보인다");
        double walkedFrom = memo.Me.X;
        int ghostFrom = AimAt(memo, memo.Me, memo.LastAim).X;
        for (int i = 0; i < 30; i++) { memo.Walk(memo.Me, 1, 1 / 60.0); memo.Update(1 / 60.0); }
        double walked = memo.Me.X - walkedFrom;
        Expect(walked > 20 && Math.Abs(AimAt(memo, memo.Me, memo.LastAim).X - ghostFrom - walked) <= 1 && memo.LastAim == -1.25, $"걸으면 지난 각도 조준경도 볼록이를 따라온다 (각도는 그대로): {walked:F0}px");
        memo.Fire(memo.Me, -.7, 1.4); // 디버그 명령으로는 1 넘게도 쏠 수 있다: 적는 것은 그대로, 세모는 막대 끝에
        Expect(memo.LastAim == -.7 && memo.LastPower == 1.4 && LastTip(memo) == 20 * 3, "다시 쏘면 새 값으로 바뀐다");
        Expect(!Make().HasLast, "새 판은 지난 발이 없다");

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
               $"깎인 넓이는 원과 {worstArea:P0} 안, {guns}, {play}, {hud}";
    }

    // 새 대포 넷 (YELL 2026-11-13): 튕김탄은 두 번 튕기고, 집속탄은 꼭대기에서 넷으로 갈라지고, 드릴탄은 땅속으로 파고들어 터지고, 밀치기탄은 조금 다치게 하고 멀리 민다.
    // 라이벌은 어느 대포로든 몇 발 안에 맞히고, 포탄이 판 밖으로 나가도 탈이 없다
    static string NewGunChecks(Rectangle field, Rectangle landA, Rectangle landB)
    {
        Fortress Make(string mine, string cpu = "normal", int seed = 1) => new(field, new[] { landA, landB }, 3, new Random(seed), mine, cpu) { Wind = 0 };
        void Tick(Fortress w, double sec) { for (double t = 0; t < sec; t += 1 / 60.0) w.Update(1 / 60.0); }
        static double Speed(Fortress.Shell s) => Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy);
        const double deg = Math.PI / 180;

        // 튕김탄: 제 섬 위에 떨어뜨리면 두 번 튕기고(튕길 때마다 느려진다) 세 번째로 닿을 때 터진다
        var hop = Make("bounce");
        var hopSpeeds = new List<double>();
        double hopFirst = -1, hopBoom = -1, hopWas = 0, hopKeep = 0, hopUp = double.MinValue;
        hop.OnEvent = e =>
        {
            if (e == "bounce")
            {
                hopSpeeds.Add(Speed(hop.Shells[0]));
                (hopKeep, hopUp) = (Math.Max(hopKeep, hopSpeeds[^1] / hopWas), Math.Max(hopUp, hop.Shells[0].Vy));
                if (hopFirst < 0) hopFirst = hop.Clock;
            }
            if (e == "boom") hopBoom = hop.Clock;
        };
        hop.Fire(hop.Me, -60 * deg, .35);
        double hopV0 = Speed(hop.Shells[0]);
        for (int i = 0; i < 60 * 6; i++)
        {
            if (hop.Shells.Count == 1) hopWas = Speed(hop.Shells[0]);
            hop.Update(1 / 60.0);
        }
        Expect(hopSpeeds.Count == 2 && hop.Booms == 1 && hop.Stage != Fortress.Phase.Flying, $"튕김탄은 두 번 튕기고 터진다: {hopSpeeds.Count}번 튕기고 {hop.Booms}번 터짐");
        Expect(hopKeep is > .5 and < .68 && hopSpeeds[1] < hopSpeeds[0] * .8 && hopUp < -50, $"튕길 때마다 느려지고 위로 튄다: {hopV0:F0} → {hopSpeeds[0]:F0} → {hopSpeeds[1]:F0} (남는 빠르기 {hopKeep:F2}배)");

        Expect(hop.Blasts.Count == 0 && hopBoom - hopFirst is > .1 and <= Fortress.BounceFuse + .05, $"처음 튕기고 {Fortress.BounceFuse}초 안에 터진다: {hopBoom - hopFirst:F2}초");
        // 가파른 벽에 쏘면 되돌아온다 (겉면이 보는 쪽으로 튄다)
        var wall = Make("bounce");
        double wallVx = 0;
        wall.OnEvent = e => { if (e == "bounce" && wallVx == 0) wallVx = wall.Shells[0].Vx; };
        (wall.Me.X, wall.Me.Y) = (landB.Left - 200, landB.Top + 150); // 오른쪽 섬 왼쪽 벽 앞 허공에서 (곧 떨어지지만 쏘는 데는 상관없다)
        wall.Fire(wall.Me, 0, .4);
        Tick(wall, .6);
        Expect(wallVx < -100, $"벽에 맞으면 되튄다: 오른쪽으로 쏜 탄이 {wallVx:F0}");

        // 집속탄: 올라가다 내려가기 시작하는 순간 넷으로 갈라져 옆으로 벌어지고, 작은 폭발 넷
        var burst = Make("cluster");
        double riseVy = 0, splitVy = double.NaN, spread = 0;
        int kids = 0;
        var pops = new List<Fortress.Blast>();
        burst.OnEvent = e =>
        {
            if (e == "split") (kids, splitVy, spread) = (burst.Shells.Count(s => s.Look == 2 && s.Split == 0), riseVy, burst.Shells.Max(s => s.Vx) - burst.Shells.Min(s => s.Vx));
            if (e == "boom") pops.Add(burst.Blasts[^1]);
        };
        burst.Fire(burst.Me, -70 * deg, .5);
        Expect(burst.Shells.Count == 1 && burst.Shells[0].Split == 4, "집속탄은 한 발로 날아간다");
        for (int i = 0; i < 60 * 8 && burst.Stage == Fortress.Phase.Flying; i++)
        {
            if (burst.Shells.Count == 1 && burst.Shells[0].Split > 0) riseVy = burst.Shells[0].Vy;
            burst.Update(1 / 60.0);
        }
        Expect(kids == 4 && splitVy is < 0 and > -40 && spread > 200, $"집속탄은 꼭대기에서 넷으로 갈라진다: {kids}개, 갈라지기 직전 세로 빠르기 {splitVy:F0}, 벌어짐 {spread:F0}");
        Expect(pops.Count == 4 && pops.All(p => p.R < 40) && pops.Max(p => p.X) - pops.Min(p => p.X) > 60, $"새끼 탄 넷이 넓게 떨어져 작게 터진다: {pops.Count}번, 폭 {(pops.Count > 0 ? pops.Max(p => p.X) - pops.Min(p => p.X) : 0):F0}px");

        // 드릴탄: 땅에 닿으면 날던 쪽으로 정해진 길이만큼 파고든 뒤 땅속에서 터진다. 지나간 자리는 굴이 되고, 굴은 폭발로 세지 않는다
        var bore = Make("drill");
        double drillLen = Fortress.GunOf("drill").Drill;
        (double X, double Y)? entry = null;
        bore.Fire(bore.Me, -70 * deg, .5);
        for (int i = 0; i < 60 * 8 && bore.Stage == Fortress.Phase.Flying; i++)
        {
            bore.Update(1 / 60.0);
            if (entry == null && bore.Shells.Count == 1 && bore.Shells[0].DrillLeft >= 0) entry = (bore.Shells[0].X + bore.Shells[0].Vx / Fortress.DrillSpeed * (bore.Shells[0].DrillLeft - drillLen), bore.Shells[0].Y + bore.Shells[0].Vy / Fortress.DrillSpeed * (bore.Shells[0].DrillLeft - drillLen));
        }
        var deep = bore.Blasts.LastOrDefault(b => !b.Quiet);
        Expect(entry != null && deep != null && bore.Booms == 1 && bore.Carves >= 5, $"드릴탄은 굴을 파고 한 번 터진다: 폭발 {bore.Booms}번, 깎인 자리 {bore.Carves}곳");
        double drilled = Math.Sqrt((deep!.X - entry!.Value.X) * (deep.X - entry.Value.X) + (deep.Y - entry.Value.Y) * (deep.Y - entry.Value.Y));
        Expect(Math.Abs(drilled - drillLen) < 3 && deep.Y > landA.Top + drillLen * .7 && Math.Abs(entry.Value.Y - landA.Top) < 12, $"땅속 {drillLen}px 에서 터진다: 들어간 자리 ({entry.Value.X:F0},{entry.Value.Y:F0}) → 터진 자리 ({deep.X:F0},{deep.Y:F0}), {drilled:F0}px");
        Expect(!bore.Solid((entry.Value.X + deep.X) / 2, (entry.Value.Y + deep.Y) / 2) && !bore.Solid(entry.Value.X + (deep.X - entry.Value.X) * .2, entry.Value.Y + (deep.Y - entry.Value.Y) * .2), "지나간 자리는 굴이 된다");
        var flat = Make("normal");
        flat.Fire(flat.Me, -70 * deg, .5);
        Tick(flat, 5);
        Expect(flat.Booms == 1 && flat.Carves == 1, "보통 포탄은 굴을 파지 않는다");

        // 밀치기탄: 조금 다치게 하고 멀리 민다. 구멍은 작다. 멀리서 터지면 거의 안 밀린다 (어디서 맞든 이기는 것이 아니다)
        var pushGun = Fortress.GunOf("push");
        (double Moved, int Lost, bool Out) Shove(double dx, Fortress.Gun gun)
        {
            var t = Make("normal");
            var (x0, hp) = (t.Cpu.X, t.Cpu.Hp);
            t.Explode(t.Cpu.X + dx, t.Cpu.Y - 2, gun.Radius, gun.Damage, gun.Knock, gun.Crater);
            Tick(t, 4);
            return (Math.Abs(t.Cpu.X - x0), hp - t.Cpu.Hp, t.Cpu.Out);
        }
        var (pushNear, plain, pushFar, pushEdge) = (Shove(25, pushGun), Shove(25, Fortress.GunOf("normal")), Shove(62, pushGun), Shove(-25, pushGun));
        Expect(pushNear.Moved > 200 && pushNear.Moved > plain.Moved * 2 && !pushNear.Out, $"밀치기탄은 멀리 민다: {pushNear.Moved:F0}px (일반대포 {plain.Moved:F0}px)");
        Expect(pushNear.Lost is > 0 and <= 20 && pushNear.Lost * 2 < plain.Lost, $"밀치기탄은 조금만 다치게 한다: {pushNear.Lost} (일반대포 {plain.Lost})");
        Expect(pushFar.Moved < 80 && pushFar.Lost <= 4 && !pushFar.Out, $"멀리서 터지면 거의 안 밀린다: {pushFar.Moved:F0}px, 피해 {pushFar.Lost}");
        Expect(pushEdge.Out, "섬 끝 쪽으로 가까이서 밀리면 물에 빠진다");
        var dent = Make("normal");
        var (dentX, dentY) = (dent.Cpu.X - 200, dent.Cpu.Y);
        dent.Explode(dentX, dentY, pushGun.Radius, pushGun.Damage, pushGun.Knock, pushGun.Crater);
        Expect(!dent.Solid(dentX, dentY + 8) && dent.Solid(dentX + 38, dentY + 8) && dent.Solid(dentX, dentY + 38), "밀치기탄 구멍은 작다 (폭발 반지름의 0.4배)");

        // 라이벌: 어느 대포로든 빗나감 없이 계산하면 가까이 떨어지고, 실제로(흔들려) 쏴도 몇 발 안에 맞힌다
        int worstShots = 0;
        string worstGun = "", shotsBy = "";
        foreach (var gun in Fortress.Guns)
        {
            int gunWorst = 0;
            int near = 0;
            for (int seed = 0; seed < 4; seed++)
            {
                var ai = Make("normal", gun.Name, seed);
                ai.Wind = (seed - 1.5) / 2;
                var (ang, pow) = ai.Plan(miss: 0);
                ai.MyTurn = false;
                double best = double.MaxValue;
                int hp = ai.Me.Hp;
                ai.OnEvent = e => { if (e == "boom") best = Math.Min(best, Math.Sqrt(Math.Pow(ai.Blasts[^1].X - ai.Me.X, 2) + Math.Pow(ai.Blasts[^1].Y - ai.CenterY(ai.Me), 2))); };
                ai.Fire(ai.Cpu, ang, pow);
                for (int i = 0; i < 60 * 10 && ai.Stage == Fortress.Phase.Flying; i++) ai.Update(1 / 60.0);
                if (best < 80 || ai.Me.Hp < hp) near++;
            }
            Expect(near >= 3, $"라이벌 계산은 {gun.Name} 로도 볼록이 근처에 떨어진다: {near}/4");
            for (int seed = 0; seed < 5; seed++)
            {
                var t = Make("normal", gun.Name, seed + 20);

                int shots = 0, hp = t.Me.Hp;
                t.OnEvent = e => { if (e == "fire") shots++; };
                for (int i = 0; i < 60 * 240 && t.Me.Hp == hp && !t.Me.Out && shots <= 12; i++)
                {
                    if (t.MyTurn && t.Stage == Fortress.Phase.Turn) (t.MyTurn, t.Wind) = (false, 0); // 볼록이는 가만히 있고 라이벌만 거듭 쏜다
                    t.Update(1 / 60.0);
                }
                Expect(t.Me.Hp < hp || t.Me.Out, $"라이벌이 {gun.Name} 로 볼록이를 맞힌다 (씨 {seed + 20}): {shots}발을 쏘고도 못 맞힘");
                if (shots > worstShots) (worstShots, worstGun) = (shots, gun.Name);
                gunWorst = Math.Max(gunWorst, shots);
            }
            shotsBy += $"{(shotsBy == "" ? "" : " · ")}{gun.KoLabel} {gunWorst}";
        }
        Expect(worstShots <= 8, $"라이벌은 어느 대포로든 8발 안에 맞힌다: {worstGun} {worstShots}발");

        // 대포 이름: 디버그 명령(worms <이름>)·하위 메뉴가 이름으로 찾고, 어느 언어에도 제 이름이 있다
        string lang = L.Code;
        foreach (var gun in Fortress.Guns)
        {
            Expect(Fortress.GunOf(gun.Name) == gun, $"이름으로 대포를 찾는다: {gun.Name}");
            foreach (var (code, _) in L.Languages)
            {
                L.Use(code);
                Expect(gun.Label.Length > 0 && !gun.Label.StartsWith("gun."), $"{gun.Name} 의 {code} 이름이 있다: {gun.Label}");
            }
        }
        L.Use(lang);


        // 포탄이 판 밖으로 나가도 탈이 없다: 어느 대포든 사방으로(센 바람에도) 쏘면 차례가 넘어간다
        int flown = 0;
        foreach (var gun in Fortress.Guns)
            foreach (double ang in new[] { -178.0, -135, -90, -45, -2, 20, 90, 160 })
                foreach (double wind in new[] { -1.0, 1 })
                {
                    var t = Make(gun.Name);
                    t.Wind = wind;
                    t.Fire(t.Me, ang * deg, 1);
                    for (int i = 0; i < 60 * 20 && t.Stage == Fortress.Phase.Flying; i++) t.Update(1 / 60.0);
                    Expect(t.Stage != Fortress.Phase.Flying && t.Shells.Count == 0, $"{gun.Name} 를 {ang}° 로 쏴도 끝난다 (바람 {wind})");
                    flown++;
                }
        return $"새 대포: 튕김탄 2번 튕김({hopV0:F0}→{hopSpeeds[0]:F0}→{hopSpeeds[1]:F0}px/초), 집속탄 꼭대기에서 4개(폭 {pops.Max(p => p.X) - pops.Min(p => p.X):F0}px), 드릴탄 땅속 {drilled:F0}px, " +
               $"밀치기탄 {pushNear.Moved:F0}px 밀고 피해 {pushNear.Lost}(일반대포 {plain.Moved:F0}px·{plain.Lost}), 라이벌이 맞히기까지 가장 오래 걸린 발 수 ({shotsBy}), 판 밖으로 쏜 {flown}발 탈 없음";
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
        // 대포 그림: 체력 막대 왼쪽에 따로 있고, 체력 막대·숫자·힘 막대·지난 힘 세모(막대 왼쪽 끝에 있을 때까지)·제 몸과 안 겹친다
        foreach (var h in new[] { me, w.Cpu })
        {
            int e = Math.Max(1, k / 2), x = (int)h.X, y = game.BarY(w, h);
            var gun = game.GunBox(w, h);
            var bars = Rectangle.FromLTRB(x - 10 * k - e, y - 6 * k - e, x + 12 * k + 17 * GameHud.Small(k), y + 2 * k + e);  // 힘 막대 윗변부터 체력 막대·숫자까지
            var tip = Rectangle.FromLTRB(x - 13 * k, y - 10 * k, x + 13 * k + e, y - 6 * k);                                // 지난 힘 세모가 올 수 있는 자리
            Expect(gun.Right <= x - 10 * k - e && gun.Width >= 9 * k && gun.Height >= 7 * k && !gun.IntersectsWith(bars) && !gun.IntersectsWith(tip), $"대포 그림은 막대 옆에 따로 있다 ({what}): {gun} · {bars} · {tip}");
            Expect(gun.Bottom <= (int)(h.Y - (h == me ? game.meHead : game.cpuHead)), $"대포 그림이 머리를 가리지 않는다 ({what}): {gun}");
            Expect(!r.IntersectsWith(gun) || h != me, $"바람 상자가 내 대포 그림과 안 겹친다 ({what}): {r} · {gun}");
        }
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
        Expect(play.LastAim == play.Aim && play.LastPower is > .4 and < .55, $"놓은 각도와 힘이 지난 발로 남는다: {play.LastPower:F2}");
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
        Expect(!cmd.HasLast, "새 판을 열면 지난 발이 없다");
        Expect(game.Command(new[] { "fortress", "fire", "-60", "0.5" }) && cmd.Stage == Fortress.Phase.Flying && cmd.Shells.Count == 1, "worms fire 로 쏜다");
        Expect(Math.Abs(cmd.LastAim + Math.PI / 3) < 1e-9 && cmd.LastPower == .5, "worms fire 로 쏜 각도와 힘도 지난 발로 남는다");
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
        using var ov = host.Overlay(game);
        int ghosts = 0, tips = 0, holes = 0, bad, first; // 지난 각도 조준경·지난 힘 세모가 보인 프레임 (둘째 내 차례부터: 걷고 뛰는 동안에도 따라다닌다)
        int bounces = 0, splits = 0, digs = 0;           // 새 대포가 한 일 (튕김·갈라짐·굴): 그 그림들도 다시 칠할 자리에 다 들어 있어야 한다
        foreach (var (mine, theirs, seed) in new[] { ("giant", "multi", 5), ("drill", "cluster", 5), ("bounce", "push", 7), ("cluster", "drill", 8), ("push", "bounce", 9) })
        {
            var paint = new Fortress(small, new[] { new Rectangle(60, 300, 340, 240), new Rectangle(560, 270, 340, 270) }, 3, new Random(seed), mine, theirs) { Wind = .4 };
            game.Open(paint, Session.Fake(small), events: false);
            paint.OnEvent = e => { if (e == "bounce") bounces++; else if (e == "split") splits++; };
            int n = 0;
            bool mainRun = mine == "giant";
            (bad, first) = SceneKit.Repaint(ov, game, small, () =>
            {
                if (paint.Done || paint.Booms >= 12 && paint.Stage == Fortress.Phase.Turn || ++n > 60 * 80) return false;
                if (mainRun && GhostOn(paint)) ghosts++;
                if (mainRun && paint.HasLast && Mark(paint, paint.Me) >= 1000) tips++;
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
            digs += paint.Carves - paint.Booms;
            Expect(bad == 0 && paint.Booms >= (mainRun ? 2 : 1), $"바뀐 자리만 다시 칠해도 화면이 맞다 ({mine} · {theirs}): {bad}번 다름 (처음 {first}프레임), 폭발 {paint.Booms}번");
            if (mainRun) holes = game.tiles.Count(t => t != null);
        }
        Expect(ghosts > 60 && tips > 60, $"그 판에 지난 발 표시(흐린 조준경·힘 세모)도 나왔다: {ghosts}·{tips}프레임");
        Expect(bounces >= 2 && splits >= 2 && digs >= 4, $"그 판들에 새 대포의 튕김·갈라짐·굴도 나왔다: {bounces}·{splits}·{digs}");

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
        return $"조작은 마우스·←→·↑ 만, 지난 발(각도·힘)은 내 것만 다음 차례까지 남아 {ghosts}프레임 보임, 구멍 그림 조각 {holes}장";
    }
}
