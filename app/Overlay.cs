namespace Bolok;

// 화면 전체를 덮는 투명 창: 구멍·부스러기·씨앗·하트·말풍선.
// WS_EX_TRANSPARENT 라 클릭이 전부 뒤로 지나간다 — 눈으로만 가려지고 일은 막지 않는다
public sealed class Overlay : Form
{
    readonly Brain b;
    readonly Func<Sprites> sprites;
    readonly Func<Game?> game;
    public Func<Breakout?> Break = () => null;
    public Func<Minigun?> MinigunGame = () => null;
    public Func<Icbm?> IcbmGame = () => null;
    public Func<Visitor?> Guest = () => null;           // 놀러 온 떠난 친구
    public Func<Sprites?> GuestSprites = () => null;
    public Func<(string Kind, string From, double X, double Y, double At)?> Present = () => null; // 친구가 두고 간 선물
    public Func<(string Kind, double X, double Y)?> Clover = () => null; // 바닥에 돋은 네잎클로버·새싹
    public Func<IReadOnlyList<string>> Dreams = () => Array.Empty<string>();                     // 꿈 말풍선 그림들
    internal Func<IReadOnlyList<IGameMode>> Modes = () => Array.Empty<IGameMode>();               // 게임 모듈 (app/Games)
    public Sprites Art => sprites();
    Icbm? icbmSeen;
    int icbmVersion = -1;
    List<Rectangle> craterRects = new();
    Minigun? gunSeen;
    int carvedSeen;
    Bitmap? gunHoles;   // 파인 칸 그림 (칸 하나가 한 점, 그릴 때 4배로 늘린다)
    Breakout? breakSeen;
    int brokenSeen;
    int gameSeen = -1;
    readonly SolidBrush redFill = new(Color.FromArgb(232, 72, 92));
    readonly SolidBrush barLine = new(Color.FromArgb(92, 58, 40)), barBack = new(Color.FromArgb(255, 243, 224)), barFill = new(Color.FromArgb(250, 185, 40));
    readonly SolidBrush holeBrush = new(Color.FromArgb(7, 9, 12));
    List<Rectangle> last = new();
    int holesSeen = -1;

    public Overlay(Brain brain, Func<Sprites> sprites, Func<Game?> game, Rectangle bounds)
    {
        b = brain;
        this.sprites = sprites;
        this.game = game;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        BackColor = PetForm.Key;
        TransparencyKey = PetForm.Key;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override bool ShowWithoutActivation => true;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_DPICHANGED) return; // 모니터 하나를 그대로 덮는 창이라 크기가 바뀌면 안 된다
        base.WndProc(ref m);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var c = base.CreateParams;
            c.ExStyle |= Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED;
            return c;
        }
    }

    public Point ToClient(double x, double y) => new((int)x - Left, (int)y - Top);

    // 머리 위 말풍선 자리. 꼬리 끝(칸의 20, 27)이 머리를 가리키고, 처음 0.15초 동안 톡 튀어 오른다
    Rectangle EmoteRect()
    {
        int size = sprites().Cell * b.Scale, half = 7 * b.Scale;
        double rise = Math.Min(1, (b.Now - b.EmoteAt) / .15) * 8 * b.Scale / 3;
        var a = b.Area;
        int x = Math.Clamp((int)b.X, a.Left + half, a.Right - half);
        int y = Math.Max(a.Top + 16 * b.Scale, (int)(b.HeadY + 2 * b.Scale / 3 - rise));
        var p = ToClient(x, y);
        return new Rectangle(p.X - size / 2, p.Y - 27 * b.Scale, size, size);
    }

    // 이번 프레임에 그릴 것들의 자리. 지난 프레임 자리와 함께 다시 칠한다
    List<Rectangle> Dirty()
    {
        var r = dirtySpare; // 지지난 프레임 목록을 다시 쓴다 (Sync 가 last 와 바꿔 준다)
        r.Clear();
        int s = sprites().Cell;
        foreach (var f in b.Bits) { var p = ToClient(f.X, f.Y); r.Add(new Rectangle(p.X, p.Y, 8, 8)); }
        foreach (var h in b.Hearts) { var p = ToClient(h.X, h.Y); r.Add(new Rectangle(p.X - s, p.Y - s * 2, s * 2, s * 2)); }
        foreach (var seed in b.Foods) { var p = ToClient(seed.X, seed.Y); r.Add(new Rectangle(p.X - s, p.Y - s, s * 2, s * 2)); }
        if (b.Emoting) r.Add(EmoteRect());
        if (b.Saying) r.Add(Client(NoteRect(b.X, b.Y, b.Area, b.SayLines!)));
        if (IcbmGame() is { } ic)
        {
            int k = b.Scale;
            r.Add(Client(new Rectangle((int)ic.ButtonX - 20 * k, (int)ic.ButtonY - 26 * k, 40 * k, 28 * k)));
            if (ic.MissileOn) r.Add(Client(Rectangle.Inflate(new Rectangle((int)ic.MX, (int)ic.MY, 0, 0), ic.Stage == Icbm.Phase.Fall ? 64 * k : 34 * k, ic.Stage == Icbm.Phase.Fall ? 64 * k : 34 * k))); // 떨어질 땐 꼬리 불꽃까지
            foreach (var cr in ic.Craters) if (cr.Friend != null) r.Add(Client(Rectangle.Inflate(new Rectangle((int)cr.X, (int)cr.Y, 0, 0), CraterReach(cr), CraterReach(cr)))); // 구름·냇물이 움직인다
            if (ic.HasTarget) r.Add(Client(Rectangle.Inflate(new Rectangle((int)ic.TX, (int)ic.TY, 0, 0), 24 * k, 24 * k)));
            foreach (var bit in ic.Bits) r.Add(Client(new Rectangle((int)bit.X - bit.Size, (int)bit.Y - bit.Size, bit.Size * 2, bit.Size * 2)));
            foreach (var bl in ic.Blasts) r.Add(Client(new Rectangle((int)bl.X - 62 * k, (int)bl.Y - 118 * k, 124 * k, 124 * k)));
        }
        if (MinigunGame() is { } mg)
        {
            int k = b.Scale;
            foreach (var (pv, _) in mg.Guns()) r.Add(Client(Rectangle.Inflate(new Rectangle((int)pv.X, (int)pv.Y, 0, 0), 36 * mg.GunK, 36 * mg.GunK)));
            foreach (var bl in mg.Bullets) r.Add(Client(Rectangle.FromLTRB((int)Math.Min(bl.X, bl.X - bl.Vx * .012) - 3, (int)Math.Min(bl.Y, bl.Y - bl.Vy * .012) - 3, (int)Math.Max(bl.X, bl.X - bl.Vx * .012) + 3, (int)Math.Max(bl.Y, bl.Y - bl.Vy * .012) + 3)));
            foreach (var bit in mg.Bits) r.Add(Client(new Rectangle((int)bit.X - bit.Size, (int)bit.Y - bit.Size, bit.Size * 2, bit.Size * 2)));
            foreach (var n in mg.Nades) r.Add(Client(new Rectangle((int)n.X - 8 * k, (int)n.Y - 8 * k, 16 * k, 16 * k)));
            foreach (var bl in mg.Blasts) r.Add(Client(Rectangle.Inflate(new Rectangle((int)bl.X, (int)bl.Y, 0, 0), (int)(bl.R * 1.3), (int)(bl.R * 1.3))));
            if (GunNote(mg) is { } note) r.Add(Client(NoteRect(b.X, b.Y, mg.Field, note)));
        }
        if (Break() is { } bo)
        {
            foreach (var ball in bo.Balls) r.Add(Client(BallRect(bo, ball)));
            foreach (var it in bo.Items) r.Add(Client(ItemRect(it)));
            r.Add(Client(PlateRect(bo)));
            r.Add(Client(LivesRect(bo)));
            foreach (var sh in bo.Shards) r.Add(Client(new Rectangle((int)sh.X - sh.Size * 2, (int)sh.Y - sh.Size * 2, sh.Size * 4, sh.Size * 4)));
            if (BreakNote(bo) is { } note) r.Add(Client(NoteRect(b.X, b.Y, bo.Field, note)));
        }
        for (var (ms, i) = (Modes(), 0); i < ms.Count; i++) if (ms[i].Running) ms[i].Dirty(this, r); // 번호로 돈다: 목록 약속(IReadOnlyList)을 foreach 로 돌면 프레임마다 메모리를 잡는다
        if (Guest() is { } gv) r.Add(Client(GuestRect(gv)));
        if (Present() is { } pr) r.Add(Client(Around(pr.X, pr.Y - 8 * b.Scale, 14 * b.Scale, 14 * b.Scale)));
        if (Clover() is { } cl) r.Add(Client(Around(cl.X, cl.Y - 10 * b.Scale, 16 * b.Scale, 16 * b.Scale)));
        if (b.Now < b.BasketUntil + .1) r.Add(Client(Around(b.BasketX, b.BasketY, 18 * b.Scale, 14 * b.Scale)));
        if (Dreams().Count > 0) r.Add(Client(DreamRect()));
        if (game() is { } gm)
        {
            r.Add(Client(HudRect(gm)));
            r.Add(Client(CenterRect(gm)));
            r.Add(Client(gm.ExitButton()));
            int m = 8 * b.Scale, aim = 30 * b.Scale / 3 * 2, boom = 20 * b.Scale * 2;
            foreach (var mark in gm.Marks) { var p = ToClient(mark.X, mark.Y); r.Add(new Rectangle(p.X - m, p.Y - m, m * 2, m * 2)); }
            int gun = 40 * b.Scale * 2 + 20 * b.Scale * 2; // 총 + 앞으로 뻗는 불꽃
            var gp = ToClient(gm.GunPivot.X, gm.GunPivot.Y);
            r.Add(new Rectangle(gp.X - gun, gp.Y - gun, gun * 2, gun + 20 * b.Scale)); // 총이 돌아가는 반원
            if (gm.Stage == Game.Phase.Boom) { var p = ToClient(gm.BoomX, gm.BoomY); r.Add(new Rectangle(p.X - boom, p.Y - boom, boom * 2, boom * 2)); }
        }
        return r;
    }

    public void Sync()
    {
        if (holesSeen != b.HolesVersion) { holesSeen = b.HolesVersion; Invalidate(); }
        int seen = game()?.Version ?? -1;
        if (seen != gameSeen) // 탄흔이 늘거나 게임이 끝나면 한 번 다시 칠한다
        {
            if (seen < 0 || gameSeen < 0) Invalidate();
            else foreach (var h in game()!.Holes) Invalidate(HoleRect(h.X, h.Y));
            gameSeen = seen;
        }
        var now = Dirty();
        var once = dirtyOnce;
        once.Clear();
        if (Break() is { } bo) // 새 판이면 창 자리 전체를, 그다음엔 새로 깨진 조각 자리만 한 번 다시 칠한다
        {
            if (bo != breakSeen) { (breakSeen, brokenSeen) = (bo, 0); once.AddRange(bo.Boards.Select(r => Client(Rectangle.Inflate(r, 2, 2)))); }
            for (; brokenSeen < bo.BrokenOrder.Count; brokenSeen++) once.Add(Client(Rectangle.Inflate(bo.BrokenOrder[brokenSeen].R, 2, 2)));
        }
        if (IcbmGame() is { } ic) // 구멍이 생기거나 줄면 그 자리만 한 번 다시 칠한다
        {
            if (ic != icbmSeen || ic.Version != icbmVersion)
            {
                (icbmSeen, icbmVersion) = (ic, ic.Version);
                once.AddRange(craterRects);
                craterRects = ic.Craters.Select(c => Client(Rectangle.Inflate(new Rectangle((int)c.X, (int)c.Y, 0, 0), CraterReach(c), CraterReach(c)))).ToList();
                once.AddRange(craterRects);
            }
        }
        else if (icbmSeen != null) { icbmSeen = null; craterRects.Clear(); }
        if (MinigunGame() is { } mg) // 새 판이면 그림을 새로, 파인 자리는 그 자리만 칠하고 한 번 다시 그린다
        {
            if (mg != gunSeen)
            {
                (gunSeen, carvedSeen) = (mg, 0);
                gunHoles?.Dispose();
                gunHoles = Bounds.IntersectsWith(mg.Field) ? new Bitmap(mg.Gw, mg.Gh, System.Drawing.Imaging.PixelFormat.Format32bppArgb) : null;
                once.Add(Client(mg.Field));
            }
            for (; carvedSeen < mg.Carved.Count; carvedSeen++)
            {
                PaintHoles(mg, mg.Carved[carvedSeen]);
                once.Add(Client(Rectangle.Inflate(mg.Carved[carvedSeen], 2, 2)));
            }
        }
        else if (gunSeen != null) { gunSeen = null; gunHoles?.Dispose(); gunHoles = null; }
        InvalidateMerged(last, now, once);
        (dirtySpare, last) = (last, now);
    }

    // 다시 칠할 곳을 64px 칸으로 모아 이어진 칸은 한 줄로 알린다. 작은 사각형을 수백 개 따로 알리면
    // 윈도우가 합치느라 한 프레임이 100ms 까지 늘었다 (벽돌깨기 불공, 2026-11-04).
    // 칸 표와 목록은 다시 쓴다: 프레임마다 새로 잡는 메모리가 없다 (2026-11-13, 전에는 프레임마다 3KB)
    const int DirtyCell = 64;
    List<Rectangle> dirtySpare = new();
    readonly List<Rectangle> dirtyOnce = new();
    bool[] dirtyGrid = Array.Empty<bool>();
    void InvalidateMerged(List<Rectangle> a, List<Rectangle> b2, List<Rectangle> c)
    {
        var area = ClientRectangle;
        int cols = (area.Width + DirtyCell - 1) / DirtyCell, rows = (area.Height + DirtyCell - 1) / DirtyCell;
        if (dirtyGrid.Length != cols * rows) dirtyGrid = new bool[cols * rows];
        int top = rows, bottom = -1;
        void Mark(List<Rectangle> rects)
        {
            foreach (var r0 in rects)
            {
                var r = Rectangle.Intersect(r0, area); // 다른 모니터 것은 버린다
                if (r.IsEmpty) continue;
                int y0 = r.Top / DirtyCell, y1 = (r.Bottom - 1) / DirtyCell, x0 = r.Left / DirtyCell, x1 = (r.Right - 1) / DirtyCell;
                (top, bottom) = (Math.Min(top, y0), Math.Max(bottom, y1));
                for (int y = y0; y <= y1; y++) Array.Fill(dirtyGrid, true, y * cols + x0, x1 - x0 + 1);
            }
        }
        Mark(a); Mark(b2); Mark(c);
        for (int y = top; y <= bottom; y++)
        {
            int row = y * cols;
            for (int x = 0; x < cols; x++)
            {
                if (!dirtyGrid[row + x]) continue;
                int from = x;
                while (x + 1 < cols && dirtyGrid[row + x + 1]) x++;
                Invalidate(new Rectangle(from * DirtyCell, y * DirtyCell, (x - from + 1) * DirtyCell, DirtyCell));
            }
            Array.Clear(dirtyGrid, row, cols);
        }
    }

    // 그리는 데 걸린 시간 (모든 모니터 합). 벽돌깨기 판이 끝날 때 로그에 남긴다
    public static double PaintMsSum, PaintMsMax;
    public static int Paints;

    protected override void OnPaint(PaintEventArgs e)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        Render(e.Graphics);
        double ms = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        (PaintMsSum, PaintMsMax, Paints) = (PaintMsSum + ms, Math.Max(PaintMsMax, ms), Paints + 1);
    }

    public void Render(Graphics g)
    {
        var sp = sprites();
        int cell = Math.Max(4, 8 * b.Scale / 3);
        foreach (var h in b.Holes)
            foreach (var c in h.Cells)
            {
                var p = ToClient(c.X, c.Y);
                g.FillRectangle(holeBrush, p.X, p.Y, cell, cell);
            }
        foreach (var f in b.Bits)
        {
            using var br = new SolidBrush(f.C == PetForm.Key ? Color.White : f.C);
            var p = ToClient(f.X, f.Y);
            g.FillRectangle(br, p.X, p.Y, 8, 8);
        }
        if (b.Now < b.BasketUntil) // 먹이 바구니: 씨앗을 꺼내 던지고 남은 수를 보인다. 비었으면 흔들어 보인다
        {
            int k = b.Scale, wob = b.BasketEmpty ? (int)Math.Round(Math.Sin(b.Now * 40) * 2 * k) : 0;
            Put(g, sp.Get("item/basket", 0, k), (int)b.BasketX + wob, (int)b.BasketY);
            Label(g, b.P.Food.ToString(), b.BasketX + 9 * k, b.BasketY - 6 * k, Math.Max(1, k - 1));
        }
        foreach (var seed in b.Foods)
        {
            var p = ToClient(seed.X, seed.Y);
            g.DrawImageUnscaled(sp.Get("icon/seed", 0, 1), p.X - sp.Cell / 2, p.Y - sp.Cell / 2);
        }
        foreach (var h in b.Hearts)
        {
            var p = ToClient(h.X, h.Y);
            g.DrawImageUnscaled(sp.Get("icon/heart", 0, 1), p.X - sp.Cell / 2, p.Y - sp.Cell);
        }
        if (b.Emoting)
        {
            var r = EmoteRect();
            g.DrawImageUnscaled(sp.Get("emo/" + b.EmoteName, 0, b.Scale), r.X, r.Y);
        }
        if (b.Saying) DrawNoteAt(g, b.X, b.Y, b.Area, b.SayLines!);
        if (game() is { } gm) DrawGame(g, gm);
        if (Break() is { } bo) DrawBreak(g, bo);
        if (MinigunGame() is { } mg) DrawMinigun(g, mg);
        if (IcbmGame() is { } ic) DrawIcbm(g, ic);
        for (var (ms, i) = (Modes(), 0); i < ms.Count; i++) if (ms[i].Running) ms[i].Draw(this, g);
        if (Guest() is { } gv && GuestSprites() is { } gs) DrawGuest(g, gv, gs);
        if (Present() is { } pr) // 선물: 바닥에서 통통, 가끔 반짝
        {
            int k = b.Scale, hop = Math.Sin(Environment.TickCount64 / 180.0) > .6 ? k : 0;
            Put(g, sp.Get("item/" + pr.Kind, 0, k), (int)pr.X, (int)pr.Y - 6 * k - hop);
            if (Environment.TickCount64 / 400 % 3 == 0) Put(g, sp.Get("game/spark", 0, Math.Max(1, k - 1)), (int)pr.X + 6 * k, (int)pr.Y - 12 * k);
        }
        if (Clover() is { } cl) // 네잎클로버·새싹: 바닥에서 살랑, 가끔 반짝 (줄기 끝이 땅에)
        {
            int k = b.Scale, s = Bolok.Clover.Scale(k), sway = Math.Sin(Environment.TickCount64 / 260.0) > .7 ? 1 : 0;
            Put(g, sp.Get("item/" + cl.Kind, 0, s), (int)cl.X + sway, (int)cl.Y - 3 * s);
            if (Environment.TickCount64 / 500 % 4 == 0) Put(g, sp.Get("game/spark", 0, s), (int)cl.X + 5 * s, (int)cl.Y - 12 * s);
        }
        if (Dreams() is { Count: > 0 } dreams) DrawDream(g, dreams);
    }

    // 꿈 말풍선: 머리 위 한쪽에 작은 동그라미 둘 + 구름 말풍선, 안에 그림이 3초마다 바뀐다
    Rectangle DreamRect()
    {
        int k = b.Scale;
        double cx = b.X + DreamSide * 16 * k, cy = b.HeadY - 15 * k; // 작게 (YELL: 좀 크다). 머리에서 작은 구름 둘을 거쳐 올라간다
        return Around(cx, cy, 11 * k, 10 * k);
    }

    // 잠자는 그림의 z 는 머리 오른쪽 위(볼록이가 왼쪽을 보면 그림이 뒤집혀 왼쪽 위)에 있다. 꿈 말풍선은 그 반대쪽에 (YELL: 겹친다)
    int DreamSide => b.Dir < 0 ? 1 : -1;

    void DrawDream(Graphics g, IReadOnlyList<string> dreams)
    {
        int k = b.Scale;
        var r = Client(DreamRect());
        int side = DreamSide;
        var head = ToClient(b.X + side * 8 * k, b.HeadY);
        var mode = (g.InterpolationMode, g.PixelOffsetMode);
        (g.InterpolationMode, g.PixelOffsetMode) = (System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor, System.Drawing.Drawing2D.PixelOffsetMode.Half);
        void Put(Bitmap blob, int cx, int cy) => g.DrawImage(blob, new Rectangle(cx - blob.Width * k / 2, cy - blob.Height * k / 2, blob.Width * k, blob.Height * k));
        Put(DreamDot, head.X, head.Y - k);                       // 머리에서 몽글몽글 올라가는 작은 구름 둘
        Put(DreamPuff, head.X + side * 3 * k, head.Y - 5 * k);
        g.DrawImage(side > 0 ? DreamCloudFlip : DreamCloud, new Rectangle(r.X, r.Y, 22 * k, 20 * k));
        (g.InterpolationMode, g.PixelOffsetMode) = mode;
        string pic = dreams[(int)(Environment.TickCount64 / 3000 % dreams.Count)];
        var sp = sprites();
        if (sp.Has(pic)) // 그려진 부분을 재서 구름 안에 맞는 정수배로 (작은 선물은 크게, 큰 그림은 작게)
        {
            var one = sp.Get(pic, 0, 1);
            if (!dreamCrops.TryGetValue(pic, out var c)) dreamCrops[pic] = c = Ui.Opaque(one, 0);
            int innerW = 13 * k, innerH = 9 * k, z = Math.Max(1, Math.Min(innerW / c.Width, innerH / c.Height));
            int mx = r.X + 11 * k, my = r.Y + 21 * k / 2;
            Ui.Pixel(g, one, new Rectangle(mx - c.Width * z / 2, my - c.Height * z / 2, c.Width * z, c.Height * z), c);
        }
    }
    readonly Dictionary<string, Rectangle> dreamCrops = new();

    // 꿈 구름 (YELL: 타원 말풍선 대신 살짝 구름 모양): 동그라미 몇 개를 겹친 흰 덩어리에 한 칸 갈색 테두리.
    // 볼록이 그림처럼 칸 단위로 만들어 배율만큼 키운다. 큰 혹이 볼록이 반대쪽 위로 오게 좌우를 뒤집어 쓴다
    static readonly (double X, double Y, double R)[] CloudPuffs = { (11, 11.2, 5.6), (6.2, 11.2, 3.9), (15.8, 11.4, 3.7), (8.8, 7.3, 4), (14, 7.2, 3.4), (8.4, 13.9, 2.9), (13.8, 14, 2.9) };
    static Bitmap? dreamCloud, dreamCloudFlip, dreamDot, dreamPuff;
    static Bitmap DreamCloud => dreamCloud ??= Blob(22, 20, CloudPuffs);
    static Bitmap DreamCloudFlip => dreamCloudFlip ??= Flipped(DreamCloud);
    static Bitmap DreamDot => dreamDot ??= Blob(4, 4, new[] { (2.0, 2.0, 1.0) });
    static Bitmap DreamPuff => dreamPuff ??= Blob(6, 5, new[] { (2.4, 2.6, 1.5), (3.8, 2.4, 1.3) });

    static Bitmap Flipped(Bitmap src) { var b = (Bitmap)src.Clone(); b.RotateFlip(RotateFlipType.RotateNoneFlipX); return b; }

    // 동그라미들 안은 흰색, 그 바깥 한 칸은 테두리 (칸 가운데로 잰다)
    static Bitmap Blob(int w, int h, (double X, double Y, double R)[] puffs)
    {
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double px = x + .5, py = y + .5;
                bool In(double grow) => puffs.Any(p => (px - p.X) * (px - p.X) + (py - p.Y) * (py - p.Y) < (p.R + grow) * (p.R + grow));
                if (In(0)) bmp.SetPixel(x, y, Color.White);
                else if (In(1)) bmp.SetPixel(x, y, Color.FromArgb(92, 58, 40));
            }
        return bmp;
    }

    // ── 놀러 온 친구: 볼록이처럼 발이 (X, Y), 머리 위에 하트·zzz ──
    Rectangle GuestRect(Visitor v)
    {
        int cell = 40 * v.K, extra = v.Baby ? (int)(cell * .8) : 0; // 아기가 뒤에 있으면 그만큼 넓게
        return new Rectangle((int)v.X - cell / 2 - 8 - extra, (int)v.Y - cell - 30 * v.K, cell + 16 + extra * 2, cell + 30 * v.K + 8);
    }

    void DrawGuest(Graphics g, Visitor v, Sprites gs)
    {
        int cell = 40 * v.K;
        string name = $"{v.Form}/0/{v.Pose}";
        if (!gs.Has(name)) name = $"{v.Form}/0/idle";
        if (!gs.Has(name)) return;
        int ms = v.Pose switch { "walk" => 130, "sleep" => 800, "idle" => 600, _ => 220 };
        var p = ToClient(v.X - cell / 2.0, v.Y - cell);
        if (v.Baby) // 아기가 뒤에서 졸졸 (같은 자세, 조금 늦게)
        {
            string bn = gs.Has($"baby/0/{v.Pose}") ? $"baby/0/{v.Pose}" : "baby/0/idle";
            if (gs.Has(bn))
            {
                var bp = ToClient(v.BabyX - cell / 2.0, v.Y - cell);
                g.DrawImageUnscaled(gs.Get(bn, (int)(v.Clock * 1000 / ms) + 1, v.K, v.Dir < 0), bp.X, bp.Y);
            }
        }
        g.DrawImageUnscaled(gs.Get(name, (int)(v.Clock * 1000 / ms), v.K, v.Dir < 0), p.X, p.Y);
        if (v.Clock < v.EmoteUntil && sprites().Has("emo/" + v.Emote))
        {
            double head = v.Y - cell * PetArt.Embedded.HeadRatio(v.Form);
            var e = ToClient(v.X - cell / 2.0, head - 27 * v.K);
            g.DrawImageUnscaled(sprites().Get("emo/" + v.Emote, 0, v.K), e.X, e.Y);
        }
    }

    Font? noteFont;
    int noteFontScale;
    readonly Dictionary<string, Size> noteSizes = new();
    static readonly Graphics measure = Graphics.FromImage(new Bitmap(1, 1));

    Font NoteFont()
    {
        if (noteFont == null || noteFontScale != b.Scale || noteFont.FontFamily.Name != L.Font) // 언어가 바뀌면 글꼴도
        {
            noteFont?.Dispose();
            noteSizes.Clear();
            noteFont = new Font(L.Font, 4 * b.Scale + 3, FontStyle.Bold, GraphicsUnit.Pixel);
            noteFontScale = b.Scale;
        }
        return noteFont;
    }

    // 볼록이 왼쪽, 몸 높이 가운데에 꼬리를 볼록이 쪽으로. 왼쪽에 자리가 없으면 오른쪽에
    public Rectangle NoteRect(double petX, double feet, Rectangle area, string[] lines)
    {
        string key = string.Join("\n", lines);
        if (!noteSizes.TryGetValue(key, out var size))
        {
            var f = NoteFont();
            var w = lines.Max(l => measure.MeasureString(l, f, PointF.Empty, StringFormat.GenericTypographic).Width);
            noteSizes[key] = size = new Size((int)Math.Ceiling(w), f.Height * lines.Length);
        }
        int pad = 3 * b.Scale, tail = 2 * b.Scale;
        int w2 = size.Width + pad * 2, h2 = size.Height + pad * 2;
        int left = (int)(petX - b.CellPx * .3) - tail - w2;
        int x = left >= area.Left + 4 ? left : (int)(petX + b.CellPx * .3);
        return new Rectangle(x, (int)(feet - b.CellPx * .4 - h2 / 2.0), w2 + tail, h2);
    }

    // 정한 자리(화면 좌표)에 안내 말풍선 (크기는 NoteRect 로 잰다)
    public void DrawNoteBox(Graphics g, Rectangle r, string[] lines) => DrawNote(g, Client(r), lines, true);

    public void DrawNoteAt(Graphics g, double petX, double feet, Rectangle area, string[] lines)
    {
        var r = NoteRect(petX, feet, area, lines);
        DrawNote(g, Client(r), lines, r.X < petX);
    }

    // ── 벽돌깨기: 창 위에 조각 금, 깨진 자리는 까만 구멍, 떨어지는 파편, 쟁반, 쇠공, 남은 공 ──
    static string[]? BreakNote(Breakout bo) => bo.Clock < 4 ? new[] { L.T("note.break_mouse"), L.T("note.break_click"), L.T("note.esc") } : null;

    Rectangle BallRect(Breakout bo, Breakout.Ball ball) => new((int)ball.X - bo.BallR * 2 - 2, (int)ball.Y - bo.BallR * 2 - 2, bo.BallR * 4 + 4, bo.BallR * 4 + 4); // 왕볼 크기까지
    Rectangle ItemRect(Breakout.Item it) => new((int)it.X - 7 * b.Scale, (int)it.Y - 5 * b.Scale, 14 * b.Scale, 10 * b.Scale);
    Rectangle PlateRect(Breakout bo) => new((int)bo.PlateX - bo.PlateW / 2 - 2 * b.Scale, (int)bo.PlateTop - 2 * b.Scale, bo.PlateW + 4 * b.Scale, bo.PlateH + 4 * b.Scale);
    Rectangle LivesRect(Breakout bo) => new(bo.Field.Left + 4 * b.Scale, bo.Field.Bottom - 10 * b.Scale, 90 * b.Scale, 8 * b.Scale); // 남은 공 + 켜진 아이템

    readonly SolidBrush crackLight = new(Color.FromArgb(244, 246, 250)), crackDark = new(Color.FromArgb(58, 62, 72)),
        holeFill = new(Color.FromArgb(14, 16, 22)), holeEdge = new(Color.FromArgb(46, 50, 60)), shardFill = new(Color.FromArgb(214, 222, 234));

    readonly PointF[] shardPts = new PointF[4];

    void DrawBreak(Graphics g, Breakout bo)
    {
        var sp = sprites();
        int k = b.Scale, u = Math.Max(1, k / 2);
        var clip = Rectangle.Ceiling(g.ClipBounds);
        foreach (var t in bo.Tiles)
        {
            var r = Client(t.R);
            if (!r.IntersectsWith(clip)) continue; // 다시 칠하는 곳 밖은 건너뛴다
            if (t.Broken)
            {
                g.FillRectangle(holeFill, r);
                g.FillRectangle(holeEdge, r.X, r.Y, r.Width, u); // 위쪽에 얇은 턱
            }
            else // 금: 위·왼쪽은 밝게, 아래·오른쪽은 어둡게 한 줄씩. 창 바탕이 밝든 어둡든 보인다
            {
                g.FillRectangle(crackLight, r.X, r.Y, r.Width, u);
                g.FillRectangle(crackLight, r.X, r.Y, u, r.Height);
                g.FillRectangle(crackDark, r.X, r.Bottom - u, r.Width, u);
                g.FillRectangle(crackDark, r.Right - u, r.Y, u, r.Height);
            }
        }
        foreach (var s in bo.Shards) // 돌며 떨어지는 파편 (창 내용은 찍지 않고 밝은 유리색으로만)
        {
            var c = ToClient(s.X, s.Y);
            for (int i = 0; i < 4; i++)
                shardPts[i] = new PointF(c.X + (float)(Math.Cos(s.Angle + i * Math.PI / 2) * s.Size), c.Y + (float)(Math.Sin(s.Angle + i * Math.PI / 2) * s.Size * .7));
            g.FillPolygon(shardFill, shardPts);
            g.DrawPolygon(Pens.DimGray, shardPts);
        }
        foreach (var it in bo.Items) // 떨어지는 아이템
        {
            var p = ToClient(it.X - 19.5 * k, it.Y - 19.5 * k);
            g.DrawImageUnscaled(sp.Get("game/item_" + it.Kind, 0, k), p.X, p.Y);
        }
        // 쟁반: 양 끝 6칸은 그대로 두고 가운데를 늘인다 (가운데 칸은 모두 같아서 늘여도 티가 안 난다)
        int bump = bo.Clock - bo.PlateHitAt < .08 ? k : 0; // 공을 받으면 쟁반이 살짝 눌린다
        var img = sp.Get("game/plate", 0, k);
        var p0 = ToClient(bo.PlateX - bo.PlateW / 2.0, bo.PlateTop + bump);
        int w = bo.PlateW, h = 5 * k;
        var state = g.Save();
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        g.DrawImage(img, new Rectangle(p0.X, p0.Y, 6 * k, h), new Rectangle(2 * k, 17 * k, 6 * k, h), GraphicsUnit.Pixel);
        g.DrawImage(img, new Rectangle(p0.X + 6 * k, p0.Y, w - 12 * k, h), new Rectangle(8 * k, 17 * k, 24 * k, h), GraphicsUnit.Pixel);
        g.DrawImage(img, new Rectangle(p0.X + w - 6 * k, p0.Y, 6 * k, h), new Rectangle(32 * k, 17 * k, 6 * k, h), GraphicsUnit.Pixel);
        g.Restore(state);
        if (bo.Stage == Breakout.Phase.Play)
            foreach (var ball in bo.Balls)
            {
                int bk = bo.Giant ? k * 2 : k; // 왕볼은 두 배
                var p = ToClient(ball.X - 19.5 * bk, ball.Y - 19.5 * bk);
                g.DrawImageUnscaled(sp.Get(bo.Fire ? "game/fireball" : "game/ball", 0, bk), p.X, p.Y);
            }
        // 왼쪽 아래: 남은 공(날아가는 것은 빼고)과 켜진 아이템. 끝나기 2초 전부터 깜빡인다
        var lives = Client(LivesRect(bo));
        int small = Math.Max(1, k - 1), x = lives.X - 16 * small;
        for (int i = 0; i < bo.Lives - 1; i++, x += 9 * small)
            g.DrawImageUnscaled(sp.Get("game/ball", 0, small), x, lives.Y - 16 * small);
        x += 6 * small;
        foreach (var (kind, until) in new[] { ("wide", bo.WideUntil), ("giant", bo.GiantUntil), ("fire", bo.FireUntil), ("slow", bo.SlowUntil) })
        {
            if (bo.Clock >= until) continue;
            if (until - bo.Clock > 2 || (int)(bo.Clock * 6) % 2 == 0)
                g.DrawImageUnscaled(sp.Get("game/item_" + kind, 0, small), x - 3 * small, lives.Y - 16 * small);
            x += 14 * small;
        }
        if (BreakNote(bo) is { } note) DrawNoteAt(g, b.X, b.Y, bo.Field, note);
    }

    // ── ICBM ──
    readonly SolidBrush rimBrush = new(Color.FromArgb(70, 60, 52)), smokeBrush = new(Color.FromArgb(186, 184, 188));

    void DrawIcbm(Graphics g, Icbm ic)
    {
        var sp = sprites();
        int k = b.Scale;
        foreach (var cr in ic.Craters) PaintCrater(g, cr, ToClient(cr.X, cr.Y), rimBrush, holeFill, ic.Clock, k);
        foreach (var pk in ic.Peeks) PaintPeek(g, sp, pk, ic.Clock, ToClient(pk.Hole.X, pk.Hole.Y), k);
        if (ic.Stage <= Icbm.Phase.Boom) // 빨간 버튼
        {
            var p = ToClient(ic.ButtonX - 20 * k, ic.ButtonY - 24 * k);
            g.DrawImageUnscaled(sp.Get(ic.ButtonDown ? "game/button_down" : "game/button_up", 0, k), p.X, p.Y);
        }
        foreach (var bit in ic.Bits) // 연기는 사라지며 작아지고, 파편은 칸만 한 네모
        {
            var p = ToClient(bit.X, bit.Y);
            int s = bit.Smoke ? Math.Max(2, (int)(bit.Size * Math.Min(1, bit.Life / .5))) : bit.Size;
            if (!bit.Smoke) g.FillRectangle(chipEdge, p.X - s / 2 - 1, p.Y - s / 2 - 1, s + 2, s + 2);
            g.FillRectangle(bit.Smoke ? smokeBrush : chip, p.X - s / 2, p.Y - s / 2, s, s);
        }
        if (ic.HasTarget && (int)(ic.Clock * 10) % 2 == 0) Put(g, sp.Get("game/aim", 0, 2 * k), (int)ic.TX, (int)ic.TY); // 떨어질 자리
        if (ic.MissileOn)
        {
            var m = ToClient(ic.MX, ic.MY);
            if (ic.Stage == Icbm.Phase.Fall) m.Offset(Random.Shared.Next(-3, 4) * k / 2, Random.Shared.Next(-3, 4) * k / 2); // 우르르 덜덜 떨린다
            PaintMissile(g, sp, ic, m, k);
        }
        foreach (var bl in ic.Blasts) // 버섯구름: 바닥 가운데(20, 38)를 터진 자리에
            Put(g, sp.Get("game/nuke", Math.Min(7, (int)((ic.Clock - bl.At) / .14)), Icbm.CloudScale * k), (int)bl.X, (int)bl.Y - 18 * Icbm.CloudScale * k);
    }

    // ── ICBM 그리기 (창 좌표만 받는다. --icbm-scene 으로 화면 밖에서도 그려 본다) ──

    // 구멍 모양: 동그라미가 아니라 찢긴 모양. 자리마다 다르고(터진 자리로 정한다), 고치며 줄어도 모양은 그대로
    // 칸(12px): Hole = 뻥 뚫린 안쪽, 아니면 그을린 가장자리. 바깥으로 금(6px 칸)이 몇 줄 간다
    // 모양은 Kit 의 Ragged 가 정한다 (씨와 난수 순서가 전과 같아 같은 자리의 구멍은 전과 같은 모양이다).
    // 프레임마다 다시 뽑지 않게 구멍마다 들고 있다가 자리·크기가 바뀔 때만 새로 뽑는다 (돌려준 목록은 고치지 않는다)
    sealed class CraterArt { public (double, double, double, double) Key; public List<(int I, int J, bool Hole)> Cells = new(); public List<(int X, int Y)> Cracks = new(); }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Icbm.Crater, CraterArt> craterArt = new();

    internal static (List<(int I, int J, bool Hole)> Cells, List<(int X, int Y)> Cracks) CraterShape(Icbm.Crater cr)
    {
        var art = craterArt.GetOrCreateValue(cr);
        var key = (cr.X, cr.Y, cr.R, cr.R0);
        if (art.Key == key) return (art.Cells, art.Cracks);
        int c = Icbm.Cell;
        var shape = Ragged.At(cr.X, cr.Y);
        (art.Key, art.Cells, art.Cracks) = cr.R < c ? (key, new(), new()) : (key, shape.Cells(cr.R, c), shape.Cracks(cr.R, cr.R0, c / 2));
        return (art.Cells, art.Cracks);
    }

    // 구멍 + 금이 닿을 수 있는 넓이 (다시 칠할 자리)
    internal static int CraterReach(Icbm.Crater cr) => (int)(cr.R0 * 1.4) + 6 * Icbm.Cell;

    // 친구 구멍(Friend)이면 안쪽에 그 친구가 사는 자연을 터질 때부터 바로 그린다. 아니면 검은 구멍
    internal static void PaintCrater(Graphics g, Icbm.Crater cr, Point ctr, Brush rim, Brush hole, double clock = 0, int k = 3)
    {
        int c = Icbm.Cell;
        var (cells, cracks) = CraterShape(cr);
        BlastArt.Hole(g, cells, cracks, ctr, c, rim, cr.Friend == null ? hole : rim);
        if (cr.Friend == null) return;
        var inner = cells.Where(x => x.Hole).ToList();
        if (inner.Count == 0) return;
        var state = g.Save();
        using (var clip = new System.Drawing.Drawing2D.GraphicsPath())
        {
            foreach (var (i, j, _) in inner) clip.AddRectangle(new Rectangle(ctr.X + i * c, ctr.Y + j * c, c, c));
            g.SetClip(clip);
        }
        var box = Rectangle.FromLTRB(ctr.X + inner.Min(x => x.I) * c, ctr.Y + inner.Min(x => x.J) * c, ctr.X + (inner.Max(x => x.I) + 1) * c, ctr.Y + (inner.Max(x => x.J) + 1) * c);
        PaintNature(g, box, PeekFloor(cr, inner, ctr, Math.Max(1, (int)Math.Round((k + 1) * .6))), (int)(cr.X * 3 + cr.Y * 5), clock, k);
        g.Restore(state);
    }

    // 친구가 올라오는 바닥: 몸 너비보다 양옆 한 칸씩 더 넓게 다 뚫린 가장 아래 줄 (들쭉날쭉한 바닥 끝에서 올라오면 양옆이 잘려 귀만 보인다)
    static int PeekFloor(Icbm.Crater cr, List<(int I, int J, bool Hole)> inner, Point ctr, int ps)
    {
        int c = Icbm.Cell, half = (cr.Friend == null ? 40 * ps : 30 * ps) / c / 2 + 1;
        var holeSet = inner.Select(x => (x.I, x.J)).ToHashSet();
        int floorJ = inner.Select(x => x.J).Distinct().OrderByDescending(j => j)
            .FirstOrDefault(j => Enumerable.Range(-half, half * 2).All(i => holeSet.Contains((i, j)) && holeSet.Contains((i, j - 1))), 0);
        return ctr.Y + (floorJ + 1) * c;
    }

    // 떠난 친구가 친구 구멍(자연이 보이는 구멍)에서 빼꼼 (YELL): 아래에서 몸의 1/3 만 올라와 눈만 깜빡깜빡. 다 올라오면 작은 하트
    internal static void PaintPeek(Graphics g, Sprites sp, Icbm.Peek pk, double clock, Point ctr, int k)
    {
        int c = Icbm.Cell;
        double t = clock - pk.At, up = Icbm.Rise(t);
        var inside = CraterShape(pk.Hole).Cells.Where(x => x.Hole).ToList();
        if (inside.Count < 4 || up <= 0) return;
        int ps = Math.Max(1, (int)Math.Round((k + 1) * .6)); // 처음 크기(k+1)의 60% 쯤 (YELL: 너무 크다). 도트가 안 깨지게 정수 배율
        bool blink = up >= 1 && t % 1.1 is > .8 and < .95;
        var img = sp.Get("thumb/" + pk.Species, blink ? 1 : 0, ps);
        var body = PeekBody(pk.Species, sp.Get("thumb/" + pk.Species, 0, ps));
        int floor = PeekFloor(pk.Hole, inside, ctr, ps);
        // 언덕 위로 귀·눈·코까지 (머리가 큰 아이들이라 몸 높이의 절반쯤이 「1/3 빼꼼」으로 보인다)
        int top = floor - k - (int)(body.Height * .5 * up), x0 = ctr.X - (body.Left + body.Width / 2);
        var state = g.Save();
        using (var clip = new System.Drawing.Drawing2D.GraphicsPath())
        {
            foreach (var (i, j, _) in inside) clip.AddRectangle(new Rectangle(ctr.X + i * c, ctr.Y + j * c, c, c));
            g.SetClip(clip);
        }
        g.IntersectClip(Rectangle.FromLTRB(ctr.X - 2000, ctr.Y - 2000, ctr.X + 2000, floor - k)); // 풀 언덕 뒤에서 몸 1/3 만
        g.DrawImageUnscaled(img, x0, top - body.Top);
        g.Restore(state);
        if (up >= 1 && t % .8 < .55)
        {
            var heart = sp.Get("icon/heart", 0, Math.Max(1, ps - 1)); // 작게 (YELL)
            g.DrawImageUnscaled(heart, x0 + body.Right - heart.Width / 3, top - heart.Height * 2 / 3);
        }
    }

    // 구멍 너머 자연: 파란 하늘과 구름, 나무숲, 흐르는 냇물, 풀밭. px 칸 도트로, 자리마다 조금씩 다르게
    internal static void PaintNature(Graphics g, Rectangle box, int floor, int seed, double clock, int px)
    {
        var rng = new Random(seed);
        int W = box.Width, horizon = box.Top + (int)((floor - box.Top) * .52) / px * px;
        Color Lerp(Color a, Color b, double u) => Color.FromArgb((int)(a.R + (b.R - a.R) * u), (int)(a.G + (b.G - a.G) * u), (int)(a.B + (b.B - a.B) * u));
        void Fill(Color col, int x, int y, int w, int h) { using var br = new SolidBrush(col); g.FillRectangle(br, x, y, w, h); }

        // 하늘: 위는 짙고 지평선 쪽은 옅게 (띠마다 한 색)
        for (int y = box.Top; y < horizon; y += px * 2)
            Fill(Lerp(Color.FromArgb(84, 150, 228), Color.FromArgb(178, 218, 248), (y - box.Top) / (double)Math.Max(1, horizon - box.Top)), box.Left, y, W, px * 2);
        // 구름: 천천히 흘러간다
        for (int n = 0; n < 2; n++)
        {
            int cw = (10 + rng.Next(5)) * px, span = W + cw * 2;
            int cx = box.Left - cw + (int)((rng.Next(span) + clock * (6 + n * 4) * px) % span) / px * px;
            int cy = box.Top + (int)((horizon - box.Top) * (.18 + .3 * n)) / px * px;
            Fill(Color.White, cx, cy + px, cw, px * 2);
            Fill(Color.White, cx + px * 2, cy, cw - px * 5, px);
            Fill(Color.FromArgb(222, 236, 250), cx + px, cy + px * 3, cw - px * 2, px);
        }
        // 먼 숲: 지평선을 따라 둥근 나무 꼭대기가 이어진다
        var far = Color.FromArgb(48, 112, 74);
        for (int x = box.Left; x < box.Right; x += px)
        {
            double h = 4 + 2.5 * Math.Sin((x - box.Left) / (double)px * .55 + seed) + 1.5 * Math.Sin((x - box.Left) / (double)px * 1.7 + seed * .3);
            int top = horizon - (int)h * px;
            Fill(far, x, top, px, horizon - top + px);
            if ((x / px + seed) % 5 == 0) Fill(Color.FromArgb(70, 140, 90), x, top + px, px, px); // 잎에 빛
        }
        // 풀밭
        int stream = horizon + (int)((floor - horizon) * .35) / px * px, bank = (floor - horizon) / 4 / px * px + px * 2;
        Fill(Color.FromArgb(120, 192, 96), box.Left, horizon + px, W, box.Bottom - horizon); // 친구가 올라오는 줄 아래 구멍까지
        // 냇물: 구불구불 가로질러 흐르고, 반짝이가 흐름 따라 옮겨 간다
        int step = (int)(clock * 7);
        for (int x = box.Left; x < box.Right; x += px)
        {
            int col = (x - box.Left) / px, mid = stream + (int)(Math.Sin(col * .22 + seed * .7) * 2) * px;
            Fill(Color.FromArgb(86, 156, 92), x, mid - px, px, px); // 둑
            Fill(Color.FromArgb(70, 146, 222), x, mid, px, bank);
            if ((col + step) % 6 == 0) Fill(Color.FromArgb(196, 230, 252), x, mid + px * ((col / 6 + seed) % Math.Max(1, bank / px)), px * 2, px);
        }
        // 앞 풀 언덕: 친구는 이 뒤에서 빼꼼한다 (언덕 위 줄이 floor)
        for (int x = box.Left; x < box.Right; x += px)
        {
            int col = (x - box.Left) / px, bump = (int)(Math.Sin(col * .5 + seed) * 1.2) * px;
            Fill(Color.FromArgb(92, 172, 78), x, floor - px + bump, px, box.Bottom - floor + px);
            if ((col + seed) % 3 == 0) Fill(Color.FromArgb(150, 214, 118), x, floor - px * 2 + bump, px, px); // 풀잎
        }
        // 가까운 나무 두 그루 (양옆)
        foreach (int side in new[] { -1, 1 })
        {
            int tx = side < 0 ? box.Left + W / 6 : box.Right - W / 6, ty = horizon + px * 2, r = (5 + rng.Next(3)) * px;
            Fill(Color.FromArgb(112, 78, 50), tx - px, ty - r / 2, px * 2, r);
            for (int dy = -r; dy <= r; dy += px)
            {
                int hw = (int)Math.Sqrt(Math.Max(0, r * r - dy * dy)) / px * px;
                Fill(Color.FromArgb(40, 120, 66), tx - hw, ty - r * 2 + dy, hw * 2, px);
            }
            Fill(Color.FromArgb(76, 156, 88), tx - r / 2, ty - r * 2 - r / 2, px * 2, px);
        }
    }

    // 썸네일에서 실제로 그려진 부분 (종·배율마다 한 번만 잰다)
    static readonly Dictionary<(string, int), Rectangle> peekBodies = new();
    static Rectangle PeekBody(string species, Bitmap img) =>
        peekBodies.TryGetValue((species, img.Width), out var r) ? r : peekBodies[(species, img.Width)] = Ui.Opaque(img, 0);

    // 미사일 (가는 쪽으로 기울여서). 떨어질 땐 꼬리에서 불꽃이 일렁인다 (글자는 쓰지 않는다: 어느 나라 사람이 봐도 같게)
    static readonly Color[] flame = { Color.FromArgb(255, 236, 120), Color.FromArgb(255, 170, 50), Color.FromArgb(232, 80, 40) };
    internal static void PaintMissile(Graphics g, Sprites sp, Icbm ic, Point m, int k)
    {
        if (ic.Stage == Icbm.Phase.Fall)
        {
            double len = Math.Max(1, Math.Sqrt(ic.MVx * ic.MVx + ic.MVy * ic.MVy)), ux = ic.MVx / len, uy = ic.MVy / len;
            for (int f = 0; f < 7; f++) // 꼬리 쪽으로 갈수록 작고 붉게, 매번 조금씩 다르게
            {
                double back = (16 + f * 5 + Random.Shared.Next(4)) * k, wob = (Random.Shared.NextDouble() - .5) * 6 * k;
                int size = Math.Max(k, (9 - f) * k + Random.Shared.Next(-1, 2) * k);
                int x = m.X - (int)(ux * back) + (int)(-uy * wob), y = m.Y - (int)(uy * back) + (int)(ux * wob);
                using var br = new SolidBrush(flame[Math.Min(2, f / 2 + Random.Shared.Next(2))]);
                g.FillRectangle(br, x - size / 2, y - size / 2, size, size);
            }
        }
        var state = g.Save();
        g.TranslateTransform(m.X, m.Y);
        g.RotateTransform((float)(Math.Atan2(ic.MVy, ic.MVx) * 180 / Math.PI));
        g.DrawImageUnscaled(sp.Get("game/missile", 0, 2 * k), -39 * k, -39 * k);
        g.Restore(state);
    }

    // ── 게임 그리기 도우미 (게임 모듈이 쓴다) ──
    public static Rectangle Around(double x, double y, double hw, double hh) => new((int)(x - hw), (int)(y - hh), (int)(hw * 2), (int)(hh * 2));

    // 숫자와 + × (도트 숫자 그림, 화면 좌표 가운데)
    public void Label(Graphics g, string text, double cx, double cy, int k)
    {
        int step = 5 * k, x0 = (int)cx - (text.Length - 1) * step / 2;
        for (int i = 0; i < text.Length; i++) Put(g, sprites().Get("num/" + (text[i] == '×' ? 'x' : text[i]), 0, k), x0 + i * step, (int)cy);
    }

    // ── 미니건 ──
    static string[]? GunNote(Minigun mg) => mg.Clock < 4 ? new[] { L.T("note.gun_aim"), L.T("note.gun_hold"), L.T("note.gun_right"), L.T("note.esc") } : null;
    readonly Color holeColor = Color.FromArgb(14, 16, 22), rimColor = Color.FromArgb(70, 60, 52);
    readonly SolidBrush chip = new(Color.FromArgb(200, 204, 212)), chipEdge = new(Color.FromArgb(90, 94, 104)), brass = new(Color.FromArgb(214, 170, 60)),
        spark = new(Color.FromArgb(255, 236, 140)), sparkCore = new(Color.White);
    Pen? tracerPen;

    // 파인 칸은 까맣게, 아직 남은 칸과 맞닿은 가장자리는 그을린 갈색으로
    void PaintHoles(Minigun mg, Rectangle r)
    {
        if (gunHoles == null) return;
        int c = Minigun.Cell;
        int x0 = Math.Max(0, (r.Left - mg.Field.Left) / c), x1 = Math.Min(mg.Gw - 1, (r.Right - mg.Field.Left) / c);
        int y0 = Math.Max(0, (r.Top - mg.Field.Top) / c), y1 = Math.Min(mg.Gh - 1, (r.Bottom - mg.Field.Top) / c);
        for (int gx = x0; gx <= x1; gx++)
            for (int gy = y0; gy <= y1; gy++)
            {
                if (!mg.Gone(gx, gy)) { gunHoles.SetPixel(gx, gy, Color.Transparent); continue; }
                bool rim = (mg.Land(gx - 1, gy) && !mg.Gone(gx - 1, gy)) || (mg.Land(gx + 1, gy) && !mg.Gone(gx + 1, gy))
                    || (mg.Land(gx, gy - 1) && !mg.Gone(gx, gy - 1)) || (mg.Land(gx, gy + 1) && !mg.Gone(gx, gy + 1));
                gunHoles.SetPixel(gx, gy, rim ? rimColor : holeColor);
            }
    }

    void DrawMinigun(Graphics g, Minigun mg)
    {
        var sp = sprites();
        int k = b.Scale, c = Minigun.Cell;
        if (gunHoles != null) // 다시 칠하는 곳에 걸친 칸만 늘려 그린다
        {
            var f = Client(mg.Field);
            var clip = Rectangle.Intersect(Rectangle.Ceiling(g.ClipBounds), new Rectangle(f.X, f.Y, mg.Gw * c, mg.Gh * c));
            if (!clip.IsEmpty)
            {
                int sx = (clip.X - f.X) / c, sy = (clip.Y - f.Y) / c, sw = Math.Min(mg.Gw - sx, clip.Width / c + 2), sh = Math.Min(mg.Gh - sy, clip.Height / c + 2);
                var state = g.Save();
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(gunHoles, new Rectangle(f.X + sx * c, f.Y + sy * c, sw * c, sh * c), new Rectangle(sx, sy, sw, sh), GraphicsUnit.Pixel);
                g.Restore(state);
            }
        }
        foreach (var bit in mg.Bits) // 파편(칸만 한 네모, 어두운 테두리), 탄피, 맞은 자리 불꽃
        {
            var p = ToClient(bit.X, bit.Y);
            int s2 = bit.Size / 2;
            if (bit.Brass) g.FillRectangle(brass, p.X - s2, p.Y - k / 2, bit.Size, Math.Max(1, k));
            else if (bit.Spark)
            {
                g.FillRectangle(spark, p.X - s2, p.Y - s2 / 3, bit.Size, s2 * 2 / 3); // 十 모양으로 번쩍
                g.FillRectangle(spark, p.X - s2 / 3, p.Y - s2, s2 * 2 / 3, bit.Size);
                g.FillRectangle(sparkCore, p.X - s2 / 3, p.Y - s2 / 3, s2 * 2 / 3, s2 * 2 / 3);
            }
            else
            {
                g.FillRectangle(chipEdge, p.X - s2 - 1, p.Y - s2 - 1, bit.Size + 2, bit.Size + 2);
                g.FillRectangle(chip, p.X - s2, p.Y - s2, bit.Size, bit.Size);
            }
        }
        tracerPen ??= new Pen(Color.FromArgb(255, 220, 90), Math.Max(2, k));
        foreach (var bl in mg.Bullets) g.DrawLine(tracerPen, ToClient(bl.X - bl.Vx * .012, bl.Y - bl.Vy * .012), ToClient(bl.X, bl.Y)); // 예광탄
        foreach (var n in mg.Nades) Put(g, sp.Get("game/grenade", 0, k), (int)n.X, (int)n.Y);
        foreach (var bl in mg.Blasts)
        {
            double t = mg.Clock - bl.At;
            if (t < .45) Put(g, sp.Get("game/boom", Math.Min(3, (int)(t / .11)), Math.Max(1, (int)Math.Round(k * bl.R / (50.0 * k / 3)))), (int)bl.X, (int)bl.Y);
        }
        int flash = mg.Firing && mg.Spin >= 1 && mg.Clock - mg.LastShot < .05 ? (int)(mg.Clock * 40) % 2 : -1;
        foreach (var (pivot, aim) in mg.Guns()) // 쌍 미니건이면 양손에 하나씩
        {
            var pv = ToClient(pivot.X, pivot.Y);
            GunArt.Draw(g, sp, "minigun", new PointF(pv.X, pv.Y), aim, mg.Kick * 2 * k, mg.GunK, flash);
        }
        if (GunNote(mg) is { } note) DrawNoteAt(g, b.X, b.Y, mg.Field, note);
    }

    void DrawNote(Graphics g, Rectangle r, string[] lines, bool tailRight)
    {
        int u = b.Scale, tail = 2 * u;
        var box = new Rectangle(tailRight ? r.X : r.X + tail, r.Y, r.Width - tail, r.Height);
        // 볼록이 그림처럼 모서리를 한 칸 깎은 흰 바탕과 갈색 테두리
        g.FillRectangle(barLine, box.X + u, box.Y, box.Width - 2 * u, box.Height);
        g.FillRectangle(barLine, box.X, box.Y + u, box.Width, box.Height - 2 * u);
        g.FillRectangle(Brushes.White, box.X + u, box.Y + u, box.Width - 2 * u, box.Height - 2 * u);
        int ty = box.Y + box.Height / 2;
        if (tailRight)
        {
            g.FillRectangle(barLine, box.Right, ty - u, u, 3 * u);
            g.FillRectangle(barLine, box.Right + u, ty, u, u);
            g.FillRectangle(Brushes.White, box.Right - u, ty, 2 * u, u);
        }
        else
        {
            g.FillRectangle(barLine, box.X - u, ty - u, u, 3 * u);
            g.FillRectangle(barLine, box.X - 2 * u, ty, u, u);
            g.FillRectangle(Brushes.White, box.X - u, ty, 2 * u, u);
        }
        var f = NoteFont();
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; // 흰 바탕 위라 테두리 색이 번지지 않는다
        for (int i = 0; i < lines.Length; i++)
            g.DrawString(lines[i], f, barLine, box.X + 3 * u, box.Y + 3 * u + i * f.Height, StringFormat.GenericTypographic);
    }

    // ── 총쏘기 게임 ──

    public Rectangle Client(Rectangle r) => new(r.X - Left, r.Y - Top, r.Width, r.Height);

    Rectangle HoleRect(int x, int y)
    {
        var p = ToClient(x, y);
        int h = 7 * b.Scale;
        return new Rectangle(p.X - h, p.Y - h, h * 2, h * 2);
    }

    // 위쪽 가운데: 남은 시간 막대, 총알, 맞힌 수
    Rectangle HudRect(Game gm)
    {
        int k = b.Scale, w = 130 * k;
        return new Rectangle(gm.Arena.Left + gm.Arena.Width / 2 - w / 2, gm.Arena.Top + 6 * k, w, 26 * k);
    }

    Rectangle CenterRect(Game gm)
    {
        int k = b.Scale;
        return new Rectangle(gm.Arena.Left + gm.Arena.Width / 2 - 70 * k, gm.Arena.Top + gm.Arena.Height / 2 - 50 * k, 140 * k, 100 * k);
    }

    public void Put(Graphics g, Bitmap bmp, int x, int y)
    {
        var p = ToClient(x, y);
        g.DrawImageUnscaled(bmp, p.X - bmp.Width / 2, p.Y - bmp.Height / 2);
    }

    void Number(Graphics g, int n, int cx, int cy, int k)
    {
        var text = n.ToString();
        int step = 5 * k, x0 = cx - (text.Length - 1) * step / 2;
        for (int i = 0; i < text.Length; i++) Put(g, sprites().Get("num/" + text[i], 0, k), x0 + i * step, cy);
    }

    // 게임 창 아래 테두리에 걸쳐 조준점을 향해 돌아가는 총. 쏘면 뒤로 밀리고 총구가 들리며 불꽃이 난다
    void DrawGun(Graphics g, Game gm)
    {
        if (!Bounds.Contains(gm.GunPivot)) return;
        double a = gm.GunAngle() - gm.GunRise * Math.Sign(Math.Cos(gm.GunAngle()));
        var p = ToClient(gm.GunPivot.X, gm.GunPivot.Y);
        int flash = gm.Clock < gm.FlashUntil ? (int)(gm.Clock * 30) % 2 : -1; // 기관총은 쏘는 동안 깜빡인다
        GunArt.Draw(g, sprites(), gm.Gun.Name, p, a, gm.GunKick, b.Scale * 2, flash);
    }

    void DrawGame(Graphics g, Game gm)
    {
        var sp = sprites();
        int k = b.Scale;
        foreach (var p in gm.Decor) g.DrawImageUnscaled(sp.Get(p.Name, 0, p.Scale), p.Origin.X - Left, p.Origin.Y - Top);
        foreach (var (x, y, kind) in gm.Holes) Put(g, sp.Get("game/hole" + kind, 0, k), x, y);
        foreach (var m in gm.Marks) Put(g, sp.Get(m.Hit ? "game/hit" : "game/spark", 0, k), m.X, m.Y);

        var hud = HudRect(gm);
        int bx = hud.X + 5 * k, by = hud.Y + 3 * k, bw = 120 * k, bh = 4 * k;
        var bar = ToClient(bx, by);
        g.FillRectangle(barLine, bar.X - k, bar.Y - k, bw + 2 * k, bh + 2 * k);
        g.FillRectangle(barBack, bar.X, bar.Y, bw, bh);
        g.FillRectangle(barFill, bar.X, bar.Y, (int)(bw * gm.TimeLeft / Game.PlaySec), bh);
        bool blink = gm.Ammo == 0 && gm.ReloadLeft == 0 && gm.Clock * 4 % 2 < 1; // 빈 탄창은 깜빡여 장전하라고 알린다
        Put(g, sp.Get("game/" + gm.Gun.Name, 0, k), bx + 9 * k, by + 13 * k);
        if (gm.Gun.Mag <= 8)
            for (int i = 0; i < gm.Gun.Mag; i++)
                Put(g, sp.Get((gm.Gun.Pellets > 1 ? "game/shell" : "game/bullet") + (i < gm.Loaded && !blink ? "" : "Off"), 0, k), bx + 22 * k + i * 6 * k, by + 13 * k);
        else // 기관총: 탄창 눈금
        {
            var tick = ToClient(bx + 20 * k, by + 10 * k);
            for (int i = 0; i < gm.Gun.Mag; i++)
                g.FillRectangle(i < gm.Loaded && !blink ? barFill : barLine, tick.X + i * 2 * k, tick.Y, k, 6 * k);
        }
        Put(g, sp.Get("game/star", 0, k), bx + bw - 22 * k, by + 13 * k);
        Number(g, gm.Hits, bx + bw - 8 * k, by + 13 * k, k);

        var exit = gm.ExitButton();
        Put(g, sp.Get("game/exit", 0, k), exit.X + exit.Width / 2, exit.Y + exit.Height / 2);

        DrawGun(g, gm);
        if (gm.Stage == Game.Phase.Boom)
        {
            int f = (int)(gm.T / (Game.BoomSec / 5));
            Put(g, f < 4 ? sp.Get("game/boom", f, k * 2) : sp.Get("game/boom2", 0, k * 2), (int)gm.BoomX, (int)gm.BoomY);
        }

        int cx = gm.Arena.Left + gm.Arena.Width / 2, cy = gm.Arena.Top + gm.Arena.Height / 2;
        if (gm.Stage == Game.Phase.Count) Number(g, gm.CountNumber, cx, cy, k * 5);
        if (gm.Stage == Game.Phase.Result)
        {
            Put(g, sp.Get("game/star", 0, k * 3), cx - 18 * k, cy - 6 * k);
            Number(g, gm.Hits, cx + 12 * k, cy - 6 * k, k * 3);
            if (gm.NewBest) Put(g, sp.Get("game/crown", 0, k * 2), cx, cy - 32 * k); // 최고 기록
            else { Put(g, sp.Get("game/crown", 0, k), cx - 8 * k, cy + 22 * k); Number(g, gm.Best, cx + 6 * k, cy + 22 * k, k); }
        }
    }
}
