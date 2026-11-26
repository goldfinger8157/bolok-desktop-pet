using System.Text;

namespace Bolok;

// 클라이밍: 장면 그려 보기(--mode-scene climb[@배율] 그림.png [값]), 속도 재기(--mode-bench climb)
//   값 없음 · panel: 모양 둘(A2·A4) × 배경 둘(문서 창 안·바탕화면) × 장면 다섯(오르는 중·100잎 친구·떨어짐·결과·새 기록 결과). 그림.png = 모두 이은 한 장, 그림-A2-window.png 처럼 따로도
//   값 check: 자가 검사만 · 값 table[:씨 수[:full[:손잡이]]]: 봇 표 · 값 art: 만든 그림 모음
//   값 theme[:덩굴|desk|sweet|balloon|block]: 테마마다 theme-<이름>-window.png (오르는 중·친구·떨어짐·결과) · -dark.png · -moments.png (서 있는 발판 셋·시간 다 돼 떨어짐·결과)
//   값 skip[:테마]: 건너뛰기(덩굴손) 장면 띠 skip-<이름>.png (한 칸 폴짝 → 5잎 → 내려섬, 위아래 두 줄)
//   값 replay:씨:테마:처음:끝[:한 줄 장 수[:지난 최고[:틀리게 한 번 누를 잎]]]: 그 씨의 판을 바르게 올라가며 잎 처음~끝마다 한 장씩 (친구·건너뛰기·구름 띠·시든 잎 그대로, 어른 볼록이) 그림.png 한 장에 잇고, 사건·앞 발판 보임 비율을 적는다. 버그 재현용
static partial class ClimbCheck
{
    static readonly string[] Friends = { "fox", "cat", "shiba", "redpanda", "corgi" };

    static ClimbGame NewGame(SceneHost host)
    {
        var g = new ClimbGame(host);
        g.FriendList = () => Friends;
        g.FriendArt = sp => Sprites.Drawn(PetArt.Embedded, sp, null);
        return g;
    }

    public static void Scene(string path, int k)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string? arg = GameModes.SceneArg;
        L.Use("ko");
        if (arg == "check") { Console.WriteLine(Check()); return; }
        if (arg == "themecheck") { try { Console.WriteLine(CheckTheme()); } catch (Exception e) { Console.WriteLine(e.Message); Environment.ExitCode = 1; } return; }   // 테마·건너뛰기·친구 검사만
        if (arg is { } ta && ta.StartsWith("table"))
        {
            var p = ta.Split(':');
            Console.WriteLine(Table(p.Length > 1 && int.TryParse(p[1], out int n) ? n : DefaultSeeds, p.Length > 2 && p[2] == "full", p.Length > 3 ? p[3] : null));
            return;
        }
        if (arg == "mem")
        {
            foreach (var look in new[] { ClimbLook.A2, ClimbLook.A4 })
            {
                var host = new SceneHost("adult_sweet", 3);
                var field = look == ClimbLook.A2 ? new Rectangle(660, 300, 450, 528) : new Rectangle(700, 300, 288, 528);
                var bg = NewGame(host); bg.auto = true; bg.skill = ClimbSkill.Probe;
                var bt = new Climb(31);
                bg.Begin(bt, look, field, Session.Fake(field), host.Pet.P.Stage);
                using var bmp = new Bitmap(1920, 1080); using var gr = Graphics.FromImage(bmp); using var ov2 = host.Overlay(bg);
                var rr = new List<Rectangle>(64);
                for (int i = 0; i < 30; i++) { bg.Frame(1 / 60.0); rr.Clear(); bg.Dirty(ov2, rr); ov2.Render(gr); }
                long a = 0, b = 0, c = 0; int n = 0;
                for (; n < 3500 && !bt.Done; n++)
                {
                    long m0 = GC.GetAllocatedBytesForCurrentThread(); bg.Frame(1 / 60.0);
                    long m1 = GC.GetAllocatedBytesForCurrentThread(); rr.Clear(); bg.Dirty(ov2, rr);
                    long m2 = GC.GetAllocatedBytesForCurrentThread(); ov2.Render(gr);
                    long m3 = GC.GetAllocatedBytesForCurrentThread();
                    (a, b, c) = (a + m1 - m0, b + m2 - m1, c + m3 - m2);
                }
                Console.WriteLine($"{look}: Frame {a / n}B Dirty {b / n}B Render {c / n}B ({n}프레임)");
            }
            return;
        }
        if (arg == "art") { ArtSheet(path, Math.Min(Math.Max(k, 1), 4)); return; }
        int f = Math.Min(Math.Max(k, 2), 4);
        string dir = Path.GetDirectoryName(path) ?? "", stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
        if (arg is { } ta2 && (ta2 == "theme" || ta2.StartsWith("theme:"))) { ThemeScenes(dir, f, ta2.Length > 6 ? ta2[6..] : null); return; }
        if (arg is { } sa && (sa == "skip" || sa.StartsWith("skip:"))) { SkipScenes(dir, f, sa.Length > 5 ? sa[5..] : null); return; }
        if (arg is { } ra && ra.StartsWith("replay:")) { ReplayScenes(path, f, ra[7..]); return; }
        var rows = new List<Bitmap>();
        foreach (var look in new[] { ClimbLook.A2, ClimbLook.A4 })
            foreach (var bg in new[] { "window", "desktop" })
            {
                using var row = Row(look, bg, f, false);
                row.Save(Path.Combine(dir, $"{stem}-{look}-{bg}{ext}"));
                rows.Add((Bitmap)row.Clone());
            }
        foreach (var look in new[] { ClimbLook.A2, ClimbLook.A4 })
        {
            using var dark = Row(look, "window", f, true);
            dark.Save(Path.Combine(dir, $"{stem}-{look}-window-dark{ext}"));
        }
        int w = rows.Max(r => r.Width), h = rows.Sum(r => r.Height);
        using var all = new Bitmap(w, h);
        using (var g = Graphics.FromImage(all))
        {
            g.Clear(Color.White);
            int y = 0;
            foreach (var r in rows) { g.DrawImageUnscaled(r, 0, y); y += r.Height; r.Dispose(); }
        }
        all.Save(path);
    }

    enum Shot { Mid, Friend, Fall, Result, NewBest, Stand1, Stand2, Stand3, FallTime, ResultTime, Save, Weak }

    // 시안과 같은 덩굴 모양 (잎 −1 ~ +8 의 방향): 시안 A 장면의 지그재그
    static readonly int[] MidDirs = { -1, -1, 1, 1, 1, -1, -1, -1, -1, 1 };
    static readonly int[] FriendDirs = { 1, -1, -1, 1, 1, 1, -1, -1, -1, 1 };

    static void Shape(Climb t, int[] dirsFrom)   // dirsFrom[0] = 잎 N−1 의 방향, [1] = N, … [9] = N+8
    {
        for (int i = 0; i < dirsFrom.Length; i++) t.At(t.N - 1 + i).Dir = (sbyte)dirsFrom[i];
        t.At(t.N - 1).Col = t.At(t.N).Col - dirsFrom[1];
        for (int j = t.N + 1; j <= t.N + 8; j++) t.At(j).Col = t.At(j - 1).Col + t.At(j).Dir;
    }

    static (ClimbGame, Climb) Make(SceneHost host, Shot shot, ClimbLook look, Rectangle panel, ClimbTheme theme = ClimbTheme.Vine, bool fog = false)
    {
        var game = NewGame(host);
        game.LoadState("{\"Best\":91,\"Plays\":9,\"Last\":60,\"Berries\":40,\"Friends\":2}");
        int start = shot switch { Shot.Friend => 100, Shot.NewBest => 124, _ => 87 };
        var t = new Climb(5, null, start) { Best = 91 };
        Shape(t, shot == Shot.Friend ? FriendDirs : MidDirs);
        for (int j = t.N - 3; j <= t.N + 12; j++) { t.At(j).Kind = ClimbKind.Leaf; t.At(j).Fog = false; }
        if (shot != Shot.Friend) { t.At(t.N + 2).Kind = ClimbKind.Wilt; t.At(t.N + 3).Kind = ClimbKind.Berry; t.At(t.N + 5).Kind = ClimbKind.Puff; t.At(t.N + 6).Kind = ClimbKind.Curl; }
        else { t.At(t.N).Kind = ClimbKind.Flower; t.At(t.N + 3).Kind = ClimbKind.Berry; }
        t.State = Climb.Phase.Run;
        if (fog) for (int j = t.N + 7; j <= t.N + 10; j++) t.At(j).Fog = true;
        t.Bar = shot switch { Shot.Friend => 1, Shot.Stand1 => .8, Shot.Stand2 => .3, Shot.Stand3 => .1, Shot.FallTime or Shot.ResultTime => .02, _ => .62 };
        t.GraceLeft = shot is Shot.FallTime or Shot.ResultTime ? 0 : 99;
        t.Steps = 5;   // 시작 안내 말풍선이 안 뜨게
        if (shot == Shot.Mid) t.Puff = false;
        if (shot == Shot.Save) t.Puff = true;
        if (shot == Shot.Weak) t.At(t.N + 1).Kind = ClimbKind.Wilt;
        var ses = Session.Fake(panel, null, () => true);
        game.Begin(t, look, panel, ses, host.Pet.P.Stage, theme);
        void Run(int frames) { for (int i = 0; i < frames; i++) game.Frame(1 / 60.0); }
        switch (shot)
        {
            case Shot.Mid or Shot.Stand1 or Shot.Stand2 or Shot.Stand3: Run(30); break;
            case Shot.Save: Run(20); t.Press(-t.At(t.N + 1).Dir); Run(14); break;                                       // 홀씨로 둥실 돌아오는 중
            case Shot.Weak: Run(10); t.Press(t.At(t.N + 1).Dir); Run(36); break;                                      // 약한 발판 위에서 버티는 중
            case Shot.FallTime: Run(10); while (!t.Done) game.Frame(1 / 60.0); while (t.SinceFall < .45) game.Frame(1 / 60.0); break;   // 시간이 다 돼 무너지며 떨어지는 중
            case Shot.ResultTime: Run(10); while (!t.Done) game.Frame(1 / 60.0); while (t.State != Climb.Phase.Over || t.EndedFor < .3) game.Frame(1 / 60.0); break;
            case Shot.Friend: t.Clock = 3; t.MileAt = 2.9; t.Fire("zone"); Run(6); break;
            case Shot.Fall:
                Run(20);
                t.Press(-t.At(t.N + 1).Dir);
                while (t.SinceFall < .34) game.Frame(1 / 60.0);
                break;
            default:
                Run(10);
                t.Press(-t.At(t.N + 1).Dir);
                while (t.State != Climb.Phase.Over || t.EndedFor < .3) game.Frame(1 / 60.0);
                break;
        }
        return (game, t);
    }

    // 한 줄: 장면 다섯 (배경에 판 자리를 맞춘다)
    static Bitmap Row(ClimbLook look, string bg, int f, bool dark, Shot[]? shotList = null, ClimbTheme theme = ClimbTheme.Vine, bool fog = false)
    {
        var win = new Rectangle(160, 40, 1560, 1000);
        int pw = ClimbPlace.Width(look) * f, ph = ClimbView.PH * f;
        Rectangle panel = bg == "window"
            ? look == ClimbLook.A2 ? new Rectangle(win.Right - 80 - pw, win.Bottom - 60 - ph, pw, ph) : new Rectangle(win.Right - pw / 2, win.Bottom - 50 - ph, pw, ph)
            : new Rectangle(800, 300, pw, ph);
        var view = Rectangle.Inflate(panel, 40, 40);
        var shots = shotList ?? new[] { Shot.Mid, Shot.Friend, Shot.Fall, Shot.Result, Shot.NewBest };
        var bmp = new Bitmap((view.Width + 12) * shots.Length + 12, view.Height + 24);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(244, 241, 236));
        using var frame = new Bitmap(1920, 1080);
        using var fg = Graphics.FromImage(frame);
        for (int i = 0; i < shots.Length; i++)
        {
            var host = new SceneHost("adult_sweet", f);
            var (game, _) = Make(host, shots[i], look, panel, theme, fog);
            using var ov = host.Overlay(game);
            ClimbDesk.Wallpaper(fg, new Rectangle(0, 0, 1920, 1080));
            if (bg == "window") ClimbDesk.DocWindow(fg, win, dark);
            ov.Render(fg);
            g.DrawImage(frame, new Rectangle(12 + i * (view.Width + 12), 12, view.Width, view.Height), view, GraphicsUnit.Pixel);
            game.Stop(true);
        }
        return bmp;
    }

    static readonly (ClimbTheme T, string Name)[] ThemeNames = { (ClimbTheme.Vine, "vine"), (ClimbTheme.Desk, "desk"), (ClimbTheme.Sweet, "sweet"), (ClimbTheme.Balloon, "balloon"), (ClimbTheme.Block, "block") };

    // 테마마다 장면: window (밝은 창: 오르는 중·친구·떨어짐·결과) · dark (어두운 창) · moments (서 있는 발판 싱싱·조짐·곧·시간 다 돼 떨어짐·결과)
    static void ThemeScenes(string dir, int f, string? only)
    {
        foreach (var (th, name) in ThemeNames)
        {
            if (only != null && only != name) continue;
            var four = new[] { Shot.Mid, Shot.Friend, Shot.Fall, Shot.Result };
            using (var a = Row(ClimbLook.A2, "window", f, false, four, th, true)) a.Save(Path.Combine(dir, $"theme-{name}-window.png"));
            using (var b = Row(ClimbLook.A2, "window", f, true, four, th, true)) b.Save(Path.Combine(dir, $"theme-{name}-dark.png"));
            using (var c = Row(ClimbLook.A2, "window", f, false, new[] { Shot.Stand1, Shot.Stand2, Shot.Stand3, Shot.FallTime, Shot.ResultTime }, th, true)) c.Save(Path.Combine(dir, $"theme-{name}-moments.png"));
            Console.WriteLine($"{name}: theme-{name}-window.png · -dark.png · -moments.png");
        }
    }

    // 한 판을 바르게 올라가며 잎 처음~끝마다 한 장 (그 씨의 친구·기믹 그대로, 검사용 어른 모습). 잎 번호를 덧쓴다
    static void ReplayScenes(string path, int f, string spec)
    {
        var p = spec.Split(':');
        int seed = int.Parse(p[0]);
        var th = ThemeNames.First(x => x.Name.Equals(p.Length > 1 ? p[1] : "vine", StringComparison.OrdinalIgnoreCase)).T;
        int from = p.Length > 2 ? int.Parse(p[2]) : 95, to = p.Length > 3 ? int.Parse(p[3]) : 115;
        var win = new Rectangle(160, 40, 1560, 1000);
        int pw = ClimbPlace.Width(ClimbLook.A2) * f, ph = ClimbView.PH * f;
        var panel = new Rectangle(win.Right - 80 - pw, win.Bottom - 60 - ph, pw, ph);
        var view = Rectangle.Inflate(panel, 16, 16);
        var host = new SceneHost("adult_grumpy", f);
        var game = NewGame(host);
        var t = new Climb(seed) { Best = p.Length > 5 ? int.Parse(p[5]) : 0 };
        game.Begin(t, ClimbLook.A2, panel, Session.Fake(panel, null, () => true), host.Pet.P.Stage, th);
        using var ov = host.Overlay(game);
        if (game.View!.Skin != null) game.View.ProbeOn();
        var prev = t.OnEvent;
        t.OnEvent = e => { prev?.Invoke(e); if (e != "hop") Console.WriteLine($"  사건 {e} 잎 {t.N}"); };
        int cols = p.Length > 4 ? int.Parse(p[4]) : 7, n = to - from + 1, rows = (n + cols - 1) / cols;
        using var strip = new Bitmap(cols * (view.Width + 6) + 6, rows * (view.Height + 6) + 6);
        using var sg = Graphics.FromImage(strip);
        sg.Clear(Color.FromArgb(244, 241, 236));
        using var frame = new Bitmap(1920, 1080);
        using var fg = Graphics.FromImage(frame);
        using var font = new Font("Arial", 11, FontStyle.Bold);
        for (int i = 0; i < 30; i++) game.Frame(1 / 60.0);
        int shot = 0;
        void Snap()
        {
            ClimbDesk.Wallpaper(fg, new Rectangle(0, 0, 1920, 1080));
            ClimbDesk.DocWindow(fg, win, false);
            ov.Render(fg);
            int x = 6 + shot % cols * (view.Width + 6), y = 6 + shot / cols * (view.Height + 6);
            sg.DrawImage(frame, new Rectangle(x, y, view.Width, view.Height), view, GraphicsUnit.Pixel);
            string kinds = string.Concat(Enumerable.Range(t.N + 1, 9).Select(j => t.At(j).Kind switch { ClimbKind.Flower => 'F', ClimbKind.Curl => 'C', ClimbKind.Wilt => 'W', ClimbKind.Berry => 'b', ClimbKind.Puff => 'p', _ => '.' }));
            sg.DrawString($"{t.N} {kinds}", font, Brushes.Black, x + 4, y + 2);
            shot++;
            if (game.View!.Skin != null) Console.WriteLine($"{t.N}: 앞 발판 (판 안/보임) " + string.Join("  ", Enumerable.Range(t.N + 1, 6).Select(j => { var (a, b) = game.View!.StepSeen(t, j); return $"{j}:{a:0.00}/{b:0.00}"; })));
        }
        while (t.N < to && !t.Done)
        {
            t.Bar = 1; t.GraceLeft = 99; t.Steps = Math.Max(t.Steps, 5);
            if (p.Length > 6 && t.N == int.Parse(p[6]) && t.Puff) t.Press(-t.At(t.N + 1).Dir);   // 틀리게 한 번 눌러 홀씨로 살아나 본다
            for (int i = 0; i < 90 && t.SaveLeft > 0 || i < 1; i++) game.Frame(1 / 60.0);
            t.Press(t.At(t.N + 1).Dir);
            var rr = new List<Rectangle>(8);
            for (int i = 0; i < 24 || t.LiftLeft > 0 || t.SaveLeft > 0; i++) { game.Frame(1 / 60.0); rr.Clear(); game.Dirty(ov, rr); ov.Render(fg); }   // 올라서 가만히 설 때까지 (프레임마다 실제처럼 다시 칠할 자리·그리기도)
            if (t.N >= from && shot < n) Snap();
        }
        strip.Save(path);
        game.Stop(true);
        Console.WriteLine($"replay {seed} {th} {from}~{to}: {shot}장 → {path}");
    }

    // 건너뛰기 장면 띠: 덩굴손을 밟는 순간 → 5잎을 지나는 동안 → 내려선 뒤 (잎 N+1 의 덩굴손, 지그재그로 꺾이는 줄기)
    static readonly double[] SkipTimes = { .00, .07, .14, .24, .34, .44, .54, .66 };

    static void SkipScenes(string dir, int f, string? only)
    {
        foreach (var (th, name) in ThemeNames)
        {
            if (only != null && only != name) continue;
            var win = new Rectangle(160, 40, 1560, 1000);
            int pw = ClimbPlace.Width(ClimbLook.A2) * f, ph = ClimbView.PH * f;
            var panel = new Rectangle(win.Right - 80 - pw, win.Bottom - 60 - ph, pw, ph);
            var view = Rectangle.Inflate(panel, 30, 20);
            var host = new SceneHost("adult_sweet", f);
            var game = NewGame(host);
            var t = new Climb(5, null, 120) { Best = 91 };
            Shape(t, MidDirs);
            for (int j = t.N - 3; j <= t.N + 12; j++) { t.At(j).Kind = ClimbKind.Leaf; t.At(j).Fog = false; }
            t.At(t.N + 1).Kind = ClimbKind.Curl; t.At(t.N + 8).Kind = ClimbKind.Berry;
            t.State = Climb.Phase.Run; t.Bar = 1; t.GraceLeft = 99; t.Steps = 5;
            game.Begin(t, ClimbLook.A2, panel, Session.Fake(panel, null, () => true), host.Pet.P.Stage, th);
            for (int i = 0; i < 20; i++) game.Frame(1 / 60.0);
            using var ov = host.Overlay(game);
            using var strip = new Bitmap((view.Width + 8) * 4 + 8, (view.Height + 8) * 2 + 8);
            using var sg = Graphics.FromImage(strip);
            sg.Clear(Color.FromArgb(244, 241, 236));
            using var frame = new Bitmap(1920, 1080);
            using var fg = Graphics.FromImage(frame);
            void Shot1(int idx)
            {
                ClimbDesk.Wallpaper(fg, new Rectangle(0, 0, 1920, 1080));
                ClimbDesk.DocWindow(fg, win, false);
                ov.Render(fg);
                sg.DrawImage(frame, new Rectangle(8 + idx % 4 * (view.Width + 8), 8 + idx / 4 * (view.Height + 8), view.Width, view.Height), view, GraphicsUnit.Pixel);
            }
            int next = 0;
            t.Press(t.At(t.N + 1).Dir);                                  // 덩굴손 잎으로 (건너뛰기 시작)
            while (next < SkipTimes.Length)
            {
                double e = t.LiftLeft > 0 ? t.T.LiftSec - t.LiftLeft : t.T.LiftSec + .06;
                if (e + 1e-9 >= SkipTimes[next]) { Shot1(next++); continue; }
                game.Frame(1 / 60.0);
            }
            strip.Save(Path.Combine(dir, $"skip-{name}.png"));
            game.Stop(true);
            Console.WriteLine($"{name}: skip-{name}.png ({SkipTimes.Length}장)");
        }
    }


    // 만든 그림 모음
    static void ArtSheet(string path, int f)
    {
        var host = new SceneHost("adult_sweet", 3);
        using var v = new ClimbView(host.Sprites, "adult_sweet", ClimbLook.A2, f);
        var items = v.Gallery().ToList();
        int cols = 12, cw = 44 * f, ch = 30 * f;
        int rows = (items.Count + cols - 1) / cols;
        using var bmp = new Bitmap(cols * cw + 20, rows * ch + 20);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(186, 198, 211));
        for (int i = 0; i < items.Count; i++)
        {
            var px = items[i].Item2;
            for (int y = 0; y < px.H; y++)
                for (int x = 0; x < px.W; x++)
                {
                    uint c = px.P[y * px.W + x];
                    if (c >> 24 == 0) continue;
                    using var b = new SolidBrush(Color.FromArgb((int)c));
                    g.FillRectangle(b, 10 + i % cols * cw + x * f, 10 + i / cols * ch + y * f, f, f);
                }
        }
        bmp.Save(path);
    }

    // ── 속도 재기 (--mode-bench climb): 검사용 봇으로 600잎 (여섯 구역·모든 기믹을 지난다, 60초), 덩굴 A2 배율 3·4 · A4 배율 3 · 덩굴 말고 넷 (A2) 배율 3·4 ──
    public static string BenchRun()
    {
        var sb = new StringBuilder();
        var kinds = new List<(ClimbLook, int, ClimbTheme)> { (ClimbLook.A2, 3, ClimbTheme.Vine), (ClimbLook.A2, 4, ClimbTheme.Vine), (ClimbLook.A4, 3, ClimbTheme.Vine) };
        foreach (var th in AllThemes.Skip(1)) { kinds.Add((ClimbLook.A2, 3, th)); kinds.Add((ClimbLook.A2, 4, th)); }
        foreach (var (look, k, th) in kinds)
        {
            var host = new SceneHost("adult_sweet", k);
            var game = NewGame(host);
            game.auto = true; game.skill = ClimbSkill.Probe;
            var win = new Rectangle(300, 90, 1320, 860);
            int pw = ClimbPlace.Width(look) * k, ph = ClimbView.PH * k;
            var panel = look == ClimbLook.A2 ? new Rectangle(win.Right - 80 - pw, win.Bottom - 60 - ph, pw, ph) : new Rectangle(win.Right - pw / 2, win.Bottom - 50 - ph, pw, ph);
            var t = new Climb(31);
            game.Begin(t, look, panel, Session.Fake(panel), host.Pet.P.Stage, th);
            using var ov = host.Overlay(game);
            sb.AppendLine($"{look}/{th} 배율 {k} (판 {panel.Width}×{panel.Height}): " + Bench.Run($"climb@{look}{k}", ov, game, panel, () => !t.Done && t.N < 600, () => game.Frame(1 / 60.0), 60 * 240) + $" — {t.N}잎 {t.RunClock:0}초");
            game.Stop(true);
        }
        sb.AppendLine(Table(DefaultSeeds));
        return sb.ToString().TrimEnd();
    }
}
