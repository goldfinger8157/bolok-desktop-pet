namespace Bolok;

// 클라이밍 콤보·카페 장면 (--mode-scene climb x.png combo[:테마|a4] / cafe[:테마|a4]): 진짜 판(ClimbGame·ClimbView)을 그대로 돌려 한 장에 잇는다.
// combo-<이름>.png: 위 밝은 창 · 아래 어두운 창, 장면 일곱 (콤보 3·7·18·42 한 칸 폴짝 가운데 · 5마다 터짐 둘 · 끊김). cafe-<이름>.png: 들어가고 나오기 일곱 (오른쪽 카페 밝은 창 · 왼쪽 카페 어두운 창)
static partial class ClimbCheck
{
    static readonly (ClimbLook Look, ClimbTheme Th, string Name)[] FxKinds =
    {
        (ClimbLook.A2, ClimbTheme.Vine, "vine"), (ClimbLook.A2, ClimbTheme.Desk, "desk"), (ClimbLook.A2, ClimbTheme.Sweet, "sweet"),
        (ClimbLook.A2, ClimbTheme.Balloon, "balloon"), (ClimbLook.A2, ClimbTheme.Block, "block"), (ClimbLook.A4, ClimbTheme.Vine, "a4"),
    };

    static Rectangle FxPanel(ClimbLook look, int f, Rectangle win)
    {
        int pw = ClimbPlace.Width(look) * f, ph = ClimbView.PH * f;
        return look == ClimbLook.A2 ? new Rectangle(win.Right - 80 - pw, win.Bottom - 60 - ph, pw, ph) : new Rectangle(win.Right - pw / 2, win.Bottom - 50 - ph, pw, ph);
    }

    // 장면 바탕: 판을 열고 길을 맞춘다 (dirs = 잎 N−1~N+8 의 방향). cafe: 지금 선 발판 옆 카페가 선 쪽 (0 이면 없다)
    static (ClimbGame, Climb) FxBase(SceneHost host, ClimbLook look, Rectangle panel, ClimbTheme th, int[] dirs, int cafe, int start = 87)
    {
        var game = NewGame(host);
        game.LoadState("{\"Best\":91,\"Plays\":9,\"Last\":60,\"Berries\":40}");
        var t = new Climb(5, null, start) { Best = 91 };
        Shape(t, dirs);
        for (int j = t.N - 6; j <= t.N + 12; j++) { t.At(j).Kind = ClimbKind.Leaf; t.At(j).Fog = false; t.At(j).Cafe = 0; t.At(j).Visited = false; }
        if (cafe != 0) t.PutCafe(cafe);
        t.State = Climb.Phase.Run; t.Bar = .8; t.GraceLeft = 99; t.Steps = 5; t.Puff = false;
        game.Begin(t, look, panel, Session.Fake(panel, null, () => true), host.Pet.P.Stage, th);
        for (int i = 0; i < 30; i++) game.Frame(1 / 60.0);
        return (game, t);
    }

    static void Frames(ClimbGame g, int n) { for (int i = 0; i < n; i++) g.Frame(1 / 60.0); }

    static Bitmap FxStrip(ClimbLook look, bool dark, int f, int count, Func<SceneHost, Rectangle, int, (ClimbGame, Climb)> make)
    {
        var win = new Rectangle(160, 40, 1560, 1000);
        var panel = FxPanel(look, f, win);
        var view = Rectangle.Inflate(panel, 12, 12);
        var bmp = new Bitmap((view.Width + 8) * count + 8, view.Height + 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(244, 241, 236));
        using var frame = new Bitmap(1920, 1080);
        using var fg = Graphics.FromImage(frame);
        for (int i = 0; i < count; i++)
        {
            var host = new SceneHost("adult_sweet", f);
            var (game, _) = make(host, panel, i);
            using var ov = host.Overlay(game);
            ClimbDesk.Wallpaper(fg, new Rectangle(0, 0, 1920, 1080));
            ClimbDesk.DocWindow(fg, win, dark);
            ov.Render(fg);
            g.DrawImage(frame, new Rectangle(8 + i * (view.Width + 8), 8, view.Width, view.Height), view, GraphicsUnit.Pixel);
            game.Stop(true);
        }
        return bmp;
    }

    static void ComboScenes(string dir, int f, string? only)
    {
        Directory.CreateDirectory(dir);
        foreach (var (look, th, name) in FxKinds)
        {
            if (only != null && only != name) continue;
            Bitmap Row(bool dark) => FxStrip(look, dark, f, 7, (host, panel, i) =>
            {
                var (game, t) = FxBase(host, look, panel, th, MidDirs, 0);
                int dirNext = t.At(t.N + 1).Dir;
                switch (i)
                {
                    case 0: t.SetCombo(3); t.Press(dirNext); Frames(game, 3); break;
                    case 1: t.SetCombo(7); t.Press(dirNext); Frames(game, 3); break;
                    case 2: t.SetCombo(18); t.Press(dirNext); Frames(game, 3); break;
                    case 3: t.SetCombo(42); t.Press(dirNext); Frames(game, 3); break;
                    case 4: t.SetCombo(19); t.Press(dirNext); Frames(game, 12); break;      // 20: 5마다 터짐 (퍼지기 전)
                    case 5: t.SetCombo(39); t.Press(dirNext); Frames(game, 18); break;      // 40: 퍼진 뒤 (서 있는 채)
                    default: t.SetCombo(18); Frames(game, 4); t.BreakCombo(); Frames(game, 6); break;   // 끊김: 쪽지가 툭 떨어진다
                }
                return (game, t);
            });
            using var a = Row(false); using var b = Row(true);
            using var all = new Bitmap(Math.Max(a.Width, b.Width), a.Height + b.Height);
            using (var g = Graphics.FromImage(all)) { g.DrawImageUnscaled(a, 0, 0); g.DrawImageUnscaled(b, 0, a.Height); }
            string path = Path.Combine(dir, $"combo-{name}.png");
            all.Save(path);
            Console.WriteLine($"{name}: {path}");
        }
    }

    // 카페: 발판에서 카페 쪽 키 → 걸어 들어감 → 앉음 → 홀짝 → 막대가 차서 happy → 길 쪽 키로 걸어 나옴 → 발판에 돌아옴 (불 꺼짐)
    static void CafeScenes(string dir, int f, string? only)
    {
        Directory.CreateDirectory(dir);
        foreach (var (look, th, name) in FxKinds)
        {
            if (only != null && only != name) continue;
            Bitmap Row(bool dark, int d) => FxStrip(look, dark, f, 7, (host, panel, i) =>
            {
                var (game, t) = FxBase(host, look, panel, th, CafeDirs(d), d);
                t.Bar = .45;
                Frames(game, 10);
                if (i >= 1) t.Press(d);
                switch (i)
                {
                    case 1: Frames(game, 12); break;                                  // 걸어 들어가는 중
                    case 2: Frames(game, 30); break;                                  // 앉음
                    case 3: Frames(game, 60); break;                                  // 홀짝
                    case 4: while (t.Bar < 1) Frames(game, 1); Frames(game, 6); break; // 막대 가득: happy
                    case 5: while (t.Bar < 1) Frames(game, 1); Frames(game, 20); t.Press(-d); Frames(game, 9); break;   // 걸어 나오는 중
                    case 6: while (t.Bar < 1) Frames(game, 1); Frames(game, 20); t.Press(-d); Frames(game, 40); break;  // 돌아옴 (불 꺼짐)
                }
                return (game, t);
            });
            using var a = Row(false, 1); using var b = Row(true, -1);
            using var all = new Bitmap(Math.Max(a.Width, b.Width), a.Height + b.Height);
            using (var g = Graphics.FromImage(all)) { g.DrawImageUnscaled(a, 0, 0); g.DrawImageUnscaled(b, 0, a.Height); }
            string path = Path.Combine(dir, $"cafe-{name}.png");
            all.Save(path);
            Console.WriteLine($"{name}: {path}");
        }
    }
}
