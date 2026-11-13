namespace Bolok;

// 화면 없이 게임 모듈을 그려 보거나(--mode-scene) 속도를 잴 때(--mode-bench) 쓰는 가짜 앱. 판은 열지 않는다
sealed class SceneHost : IGameHost
{
    public static readonly Rectangle Area = new(0, 0, 1920, 1080);

    public SceneHost(string stage = "child", int scale = 3)
    {
        Sprites = Sprites.Embedded();
        Pet = new Brain(new Pet { Stage = stage }, new Tuning(), Area) { Scale = scale };
    }

    public Brain Pet { get; }
    public Sprites Sprites { get; }
    public bool Busy => false;
    public GameLayer OpenLayer(Rectangle field, MouseEventHandler down, MouseEventHandler up, Action quit) => throw new NotSupportedException();
    public void CloseLayer() { }
    public void ShowPet(bool show) { }
    public Sprites? SpritesOf(string species) => null;
    public void Save() { }
    // 본체 창이 없으니 지금 자세의 첫 장으로 본다 (그림 이름은 모습·자세마다 한 번만 만든다)
    readonly Dictionary<(string Stage, string Pose), string> hitNames = new();
    public bool HitPet(int x, int y, int pad)
    {
        if (!hitNames.TryGetValue((Pet.P.Stage, Pet.Pose), out var name)) hitNames[(Pet.P.Stage, Pet.Pose)] = name = Pet.SpriteName();
        return Sprites.Near(Sprites.Get(name, 0, 1, Pet.Dir < 0), (x - (Pet.X - Pet.CellPx / 2.0)) / Pet.Scale, (y - (Pet.Y - Pet.CellPx)) / Pet.Scale, pad);
    }
    public void HideFromCapture(bool on) { }
    public IReadOnlyList<string> Friends => Array.Empty<string>();
    public void Repaint() { }

    // 이 게임 하나만 그리는 그림 창 (띄우지 않는다)
    public Overlay Overlay(IGameMode game)
    {
        var modes = new[] { game };
        return new(Pet, () => Sprites, Area) { Modes = () => modes };
    }
}

static class SceneKit
{
    // 다시 칠할 자리(Dirty)가 모자라지 않은지 본다: 실제 창처럼 지난 프레임·이번 프레임 자리만(64px 칸으로 모아) 지우고 다시 그린 화면이
    // 통째로 새로 그린 화면과 같아야 한다 (모자라면 지나간 그림이 화면에 남는다). step 이 규칙을 한 프레임 돌린다 (끝났으면 false).
    // every 프레임마다 견줘서 달랐던 횟수와 처음 달라진 프레임(-1 이면 없음)을 돌려준다
    public static (int Bad, int First) Repaint(Overlay ov, IGameMode game, Rectangle field, Func<bool> step, int maxFrames, int every = 10)
    {
        var view = Rectangle.Intersect(Rectangle.Inflate(field, 40, 40), new Rectangle(0, 0, ov.Width, ov.Height));
        var back = Color.FromArgb(150, 160, 170);
        using var screen = new Bitmap(ov.Width, ov.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var fresh = new Bitmap(ov.Width, ov.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var gs = Graphics.FromImage(screen);
        using var gf = Graphics.FromImage(fresh);
        using var clip = new Region();
        gs.Clear(back);
        gf.SetClip(view);
        List<Rectangle> last = new(), now = new();
        var cells = new HashSet<(int, int)>();
        int bad = 0, first = -1;
        for (int i = 0; i < maxFrames && step(); i++)
        {
            now.Clear();
            game.Dirty(ov, now);
            cells.Clear();
            clip.MakeEmpty();
            foreach (var list in new[] { last, now })
                foreach (var r0 in list)
                {
                    var r = Rectangle.Intersect(r0, new Rectangle(0, 0, ov.Width, ov.Height));
                    if (r.IsEmpty) continue;
                    for (int y = r.Top / 64; y <= (r.Bottom - 1) / 64; y++)
                        for (int x = r.Left / 64; x <= (r.Right - 1) / 64; x++)
                            if (cells.Add((x, y))) clip.Union(new Rectangle(x * 64, y * 64, 64, 64));
                }
            gs.Clip = clip;
            gs.Clear(back);
            ov.Render(gs);
            (last, now) = (now, last);
            if (i % every != 0) continue;
            gf.Clear(back);
            ov.Render(gf);
            if (Same(screen, fresh, view)) continue;
            bad++;
            if (first < 0) first = i;
        }
        return (bad, first);
    }

    static bool Same(Bitmap a, Bitmap b, Rectangle view)
    {
        var da = a.LockBits(view, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var db = b.LockBits(view, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            int[] ra = new int[view.Width], rb = new int[view.Width];
            for (int y = 0; y < view.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(da.Scan0 + y * da.Stride, ra, 0, ra.Length);
                System.Runtime.InteropServices.Marshal.Copy(db.Scan0 + y * db.Stride, rb, 0, rb.Length);
                if (!ra.AsSpan().SequenceEqual(rb)) return false;
            }
            return true;
        }
        finally { a.UnlockBits(da); b.UnlockBits(db); }
    }

    // 장면 여러 장을 옆으로 이어 한 그림으로: ready(n) 이 규칙을 한 프레임 돌리고 n 번째 장면을 찍을 때가 됐는지 알려 준다.
    // view: 모니터에서 잘라 볼 자리 (화면 좌표)
    public static void Shots(string path, Overlay ov, Rectangle view, int count, Func<int, bool> ready, int maxFrames = 60 * 600)
    {
        using var bmp = new Bitmap((view.Width + 20) * count, view.Height + 20);
        using var g = Graphics.FromImage(bmp);
        using var frame = new Bitmap(ov.Width, ov.Height);
        using var fg = Graphics.FromImage(frame);
        g.Clear(Color.White);
        for (int shot = 0, i = 0; shot < count && i < maxFrames; i++)
        {
            if (!ready(shot)) continue;
            fg.Clear(Color.FromArgb(150, 160, 170));
            ov.Render(fg);
            g.DrawImage(frame, new Rectangle(shot * (view.Width + 20) + 10, 10, view.Width, view.Height), view, GraphicsUnit.Pixel);
            shot++;
        }
        bmp.Save(path);
    }
}
