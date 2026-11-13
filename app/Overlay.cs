namespace Bolok;

// 화면 전체를 덮는 투명 창: 구멍·부스러기·씨앗·하트·말풍선.
// WS_EX_TRANSPARENT 라 클릭이 전부 뒤로 지나간다 — 눈으로만 가려지고 일은 막지 않는다
public sealed class Overlay : Form
{
    readonly Brain b;
    readonly Func<Sprites> sprites;
    public Func<Visitor?> Guest = () => null;           // 놀러 온 떠난 친구
    public Func<Sprites?> GuestSprites = () => null;
    public Func<(string Kind, string From, double X, double Y, double At)?> Present = () => null; // 친구가 두고 간 선물
    public Func<(string Kind, double X, double Y, bool Falling, bool Flip)?> Clover = () => null; // 바닥에 돋은 네잎클로버·새싹·해바라기, 내려오는 낙엽
    public Func<(double X, double Y, int Seed)?> LeafPile = () => null;
    public Func<(PointF Pos, int Stage)?> Snowman = () => null; // 눈 올 때 볼록이 곁 눈사람 (발 자리, 단계 0~2) // 땅에 쌓인 낙엽 더미 (YELL 2026-11-13: 쌓인 낙엽에서 줍는 느낌, 던져서 떨궜을 때도)
    static readonly string[] PileKinds = { "item/fallen", "item/ginkgo", "item/maple", "item/redmaple" };
    static readonly string[] PileTurns = { "", "_r1", "_r2", "_r3", "_45" }; // 0°·90°·180°·270°·45° 회전 + 좌우 뒤집기 — 장마다 제각각 (YELL: 다 같은 방향이면 어색, 기울이기 말고 회전만)
    static readonly (double Dx, double Dy)[] PileAt = { (-1.15, .1), (1.0, 0), (-.5, .35), (.55, .3), (.05, -.05) };
    public Func<IReadOnlyList<string>> Dreams = () => Array.Empty<string>();                     // 꿈 말풍선 그림들
    internal Func<IReadOnlyList<IGameMode>> Modes = () => Array.Empty<IGameMode>();               // 게임 모듈 (app/Games)
    public Func<Weather?> Sky = () => null;                                                       // 눈·비와 우산 잎 (Weather.cs)
    public Sprites Art => sprites();
    readonly SolidBrush barLine = new(Color.FromArgb(92, 58, 40));
    readonly SolidBrush holeBrush = new(Color.FromArgb(7, 9, 12)), bitBrush = new(Color.White);
    List<Rectangle> last = new();
    int holesSeen = -1;

    public Overlay(Brain brain, Func<Sprites> sprites, Rectangle bounds)
    {
        b = brain;
        this.sprites = sprites;
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
    internal List<Rectangle> Dirty()
    {
        var r = dirtySpare; // 지지난 프레임 목록을 다시 쓴다 (Sync 가 last 와 바꿔 준다)
        r.Clear();
        int s = sprites().Cell;
        foreach (var f in b.Bits) { var p = ToClient(f.X, f.Y); r.Add(new Rectangle(p.X, p.Y, 8, 8)); }
        if (Sky() is { } sky) // 날씨: 알갱이마다 작은 칸, 우산 잎·줄기
        {
            foreach (var d in sky.Drops) r.Add(Client(Weather.Box(d, sky.Scale)));
            if (sky.LeafVersion != leafSeen) // 잎은 움직이거나 장이 바뀔 때만: 옛 자리와 새 자리
            {
                leafSeen = sky.LeafVersion;
                if (!leafWas.IsEmpty) { r.Add(Client(leafWas)); r.Add(Client(stemWas)); }
                (leafWas, stemWas) = (sky.Leaf, sky.Stem);
                if (!sky.Leaf.IsEmpty) { r.Add(Client(sky.Leaf)); r.Add(Client(sky.Stem)); }
            }
        }
        foreach (var h in b.Hearts) { var p = ToClient(h.X, h.Y); r.Add(new Rectangle(p.X - s, p.Y - s * 2, s * 2, s * 2)); }
        foreach (var seed in b.Foods) { var p = ToClient(seed.X, seed.Y); r.Add(new Rectangle(p.X - s, p.Y - s, s * 2, s * 2)); }
        if (b.Emoting) r.Add(EmoteRect());
        if (b.Saying) r.Add(Client(NoteRect(b.X, b.Y, b.Area, b.SayLines!)));
        for (var (ms, i) = (Modes(), 0); i < ms.Count; i++) if (ms[i].Running) ms[i].Dirty(this, r); // 번호로 돈다: 목록 약속(IReadOnlyList)을 foreach 로 돌면 프레임마다 메모리를 잡는다
        if (Guest() is { } gv) r.Add(Client(GuestRect(gv)));
        if (Present() is { } pr) r.Add(Client(Around(pr.X, pr.Y - 8 * b.Scale, 14 * b.Scale, 14 * b.Scale)));
        if (Clover() is { } cl) r.Add(Client(Around(cl.X, cl.Y - 10 * b.Scale, 16 * b.Scale, 16 * b.Scale)));
        if (LeafPile() is { } lp) r.Add(Client(Around(lp.X, lp.Y - 4 * b.Scale, b.CellPx * .85, 12 * b.Scale)));
        if (Snowman() is { } sm) r.Add(Client(Around(sm.Pos.X, sm.Pos.Y - 12 * b.Scale, 12 * b.Scale, 14 * b.Scale)));
        if (b.Now < b.BasketUntil + .1) r.Add(Client(Around(b.BasketX, b.BasketY, 18 * b.Scale, 14 * b.Scale)));
        if (Dreams().Count > 0) r.Add(Client(DreamRect()));
        return r;
    }

    public void Sync()
    {
        if (holesSeen != b.HolesVersion) { holesSeen = b.HolesVersion; Invalidate(); }
        var now = Dirty();
        var once = dirtyOnce;
        once.Clear();
        InvalidateMerged(last, now, once);
        (dirtySpare, last) = (last, now);
    }

    // 다시 칠할 곳을 64px 칸으로 모아 이어진 칸은 한 줄로 알린다. 작은 사각형을 수백 개 따로 알리면
    // 윈도우가 합치느라 한 프레임이 100ms 까지 늘었다 (공이 수십 개인 게임, 2026-11-04).
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

    protected override void OnPaint(PaintEventArgs e) => Render(e.Graphics);

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
            bitBrush.Color = f.C == PetForm.Key ? Color.White : f.C; // 붓 하나를 색만 바꿔 쓴다 (조각마다 새로 만들지 않게)
            var p = ToClient(f.X, f.Y);
            g.FillRectangle(bitBrush, p.X, p.Y, 8, 8);
        }
        if (Sky() is { } sky) DrawWeather(g, sky, sp); // 날씨는 다른 그림(말풍선·하트·바구니) 아래에
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
        for (var (ms, i) = (Modes(), 0); i < ms.Count; i++) if (ms[i].Running) ms[i].Draw(this, g);
        if (Guest() is { } gv && GuestSprites() is { } gs) DrawGuest(g, gv, gs);
        if (Present() is { } pr) // 선물: 바닥에서 통통, 가끔 반짝
        {
            int k = b.Scale, hop = Math.Sin(Environment.TickCount64 / 180.0) > .6 ? k : 0;
            Put(g, sp.Get("item/" + pr.Kind, 0, k), (int)pr.X, (int)pr.Y - 6 * k - hop);
            if (Environment.TickCount64 / 400 % 3 == 0) Put(g, sp.Get("game/spark", 0, Math.Max(1, k - 1)), (int)pr.X + 6 * k, (int)pr.Y - 12 * k);
        }
        if (Snowman() is { } sm && sp.Has("weather/snowman")) // 눈사람: 발 줄(그림의 37)을 볼록이 발에 맞춘다
            Put(g, sp.Get("weather/snowman", sm.Stage, b.Scale), (int)sm.Pos.X, (int)sm.Pos.Y - 20 * b.Scale);
        if (LeafPile() is { } lp) // 낙엽 더미: 누운 잎 몇 장을 흩어 놓는다 (주울 낙엽보다 먼저 = 밑에)
        {
            int s = Bolok.Clover.Scale(b.Scale);
            for (int i = 0; i < PileAt.Length; i++)
            {
                var (dx, dy) = PileAt[i];
                int h = (lp.Seed * 31 + i * 17) & 0xffff;
                string kind = PileKinds[h % PileKinds.Length] + PileTurns[h / 3 % PileTurns.Length];
                if (sp.Has(kind)) Put(g, sp.Get(kind, 0, s, h / 21 % 2 == 0), (int)(lp.X + dx * b.CellPx * .5), (int)(lp.Y - 3 * s + dy * b.CellPx * .25));
            }
        }
        if (Clover() is { } cl) // 네잎클로버·새싹: 바닥에서 살랑, 가끔 반짝 (줄기 끝이 땅에)
        {
            int k = b.Scale, s = Bolok.Clover.Scale(k), sway = Math.Sin(Environment.TickCount64 / 260.0) > .7 ? 1 : 0;
            if (cl.Falling) Put(g, sp.Get("item/" + cl.Kind, 0, s, cl.Flip), (int)cl.X, (int)cl.Y); // 낙엽: 팔랑팔랑 뒤집히며 내려온다
            else
            {
                string gk = Bolok.Clover.Falls(cl.Kind) && sp.Has("item/" + cl.Kind + "_r1") ? cl.Kind + "_r1" : cl.Kind; // 땅의 낙엽은 90° 돌려 옆으로 누운 그림
                Put(g, sp.Get("item/" + gk, 0, s), (int)cl.X + sway, (int)cl.Y - 3 * s);
                if (Environment.TickCount64 / 500 % 4 == 0) Put(g, sp.Get("game/spark", 0, s), (int)cl.X + 5 * s, (int)cl.Y - 12 * s);
            }
        }
        if (Dreams() is { Count: > 0 } dreams) DrawDream(g, dreams);
    }

    // ── 날씨 (Weather.cs): 알갱이는 공통 그림에서 그 칸만 잘라 둔 조각을 그대로 찍는다 (프레임마다 새로 만드는 것 없음) ──
    readonly Bitmap?[] weatherPics = new Bitmap?[Weather.Pics];
    int leafSeen = -1;
    Rectangle leafWas, stemWas;
    Sprites? weatherPicsOf;
    int weatherPicsScale;
    readonly SolidBrush stemDark = new(Color.FromArgb(58, 126, 70)), stemLight = new(Color.FromArgb(110, 190, 110));

    void DrawWeather(Graphics g, Weather w, Sprites sp)
    {
        if (!w.Leaf.IsEmpty && Weather.HasLeaf(sp)) // 우산: 줄기(볼록이 머리 뒤로 들어간다) 위에 잎
        {
            // 줄기: 잎 밑동에서 바깥으로 살짝 휘어 몸 옆(손)까지, 아래 끝은 손잡이처럼 몸 쪽으로 동그랗게 말린다 (YELL 2026-11-13: 손잡이 부분은 휘게)
            int k = w.LeafK, out1 = w.StemSide; // 줄기는 잎 밑동 쪽 바깥으로 휜다
            double x0 = w.StemTo.X - Left, y0 = w.StemTo.Y - Top, x2 = w.StemFrom.X - Left, y2 = w.StemFrom.Y - Top;
            double x1 = Math.Min(x0, x2) * (out1 < 0 ? 1 : 0) + Math.Max(x0, x2) * (out1 > 0 ? 1 : 0) + out1 * 3 * k, y1 = (y0 + y2) / 2;
            int steps = Math.Max(2, (int)(Math.Abs(y2 - y0) + Math.Abs(x2 - x0)) / Math.Max(1, k));
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps, a = (1 - t) * (1 - t), b2 = 2 * (1 - t) * t, c2 = t * t;
                int x = (int)(a * x0 + b2 * x1 + c2 * x2), y = (int)(a * y0 + b2 * y1 + c2 * y2);
                g.FillRectangle(stemDark, x, y, 2 * k, 2 * k);
                g.FillRectangle(stemLight, x + k / 2, y, Math.Max(1, k), Math.Max(1, k));
            }
            int hx = (int)x2, hy = (int)y2, inw = -out1; // 손잡이 끝: 아래로 한 칸, 몸 쪽으로 말려 올라간다
            foreach (var (dx, dy) in new[] { (0, 2), (inw, 3), (2 * inw, 3), (3 * inw, 2) })
                g.FillRectangle(stemDark, hx + dx * k, hy + dy * k, 2 * k, 2 * k);
            g.DrawImageUnscaled(sp.Get(Weather.LeafName, w.LeafFrame, w.LeafK, w.LeafFlip), w.LeafAt.X - Left, w.LeafAt.Y - Top);
        }
        if (w.Drops.Count == 0) return;
        int s = w.Scale;
        if (weatherPicsOf != sp || weatherPicsScale != s)
        {
            (weatherPicsOf, weatherPicsScale) = (sp, s);
            for (int i = 0; i < weatherPics.Length; i++)
            {
                weatherPics[i]?.Dispose();
                var (name, frame, _, _) = Weather.PicArt(i);
                weatherPics[i] = sp.Has(name) ? sp.Get(name, frame, s).Clone(Weather.Src(i, s), System.Drawing.Imaging.PixelFormat.Format32bppArgb) : null;
            }
        }
        int right = Left + Width, bottom = Top + Height;
        foreach (var d in w.Drops)
        {
            if (d.X < Left - 40 || d.X > right + 40 || d.Y < Top - 40 || d.Y > bottom + 40) continue; // 다른 모니터
            if (weatherPics[Weather.Pic(d)] is not { } pic) continue;
            g.DrawImageUnscaled(pic, d.X - Left - pic.Width / 2, d.Y - Top - pic.Height / 2);
        }
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
    string? noteFontName;
    readonly Dictionary<string, Size> noteSizes = new();
    static readonly Graphics measure = Graphics.FromImage(new Bitmap(1, 1));

    Font NoteFont()
    {
        if (noteFont == null || noteFontScale != b.Scale || noteFontName != L.Font) // 언어가 바뀌면 글꼴도 (FontFamily.Name 은 한국어 윈도우에서 「맑은 고딕」으로 나와 요청한 이름과 견주면 늘 달랐다 → 프레임마다 새로 만들던 것)
        {
            noteFont?.Dispose();
            noteSizes.Clear();
            noteFont = new Font(L.Font, 4 * b.Scale + 3, FontStyle.Bold, GraphicsUnit.Pixel);
            noteFontScale = b.Scale;
            noteFontName = L.Font;
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

    // ── 게임 그리기 도우미 (게임 모듈이 쓴다) ──
    public static Rectangle Around(double x, double y, double hw, double hh) => new((int)(x - hw), (int)(y - hh), (int)(hw * 2), (int)(hh * 2));

    // 숫자와 + × (도트 숫자 그림, 화면 좌표 가운데)
    public void Label(Graphics g, string text, double cx, double cy, int k)
    {
        int step = 5 * k, x0 = (int)cx - (text.Length - 1) * step / 2;
        for (int i = 0; i < text.Length; i++) Put(g, sprites().Get("num/" + (text[i] == '×' ? 'x' : text[i]), 0, k), x0 + i * step, (int)cy);
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

    public Rectangle Client(Rectangle r) => new(r.X - Left, r.Y - Top, r.Width, r.Height);

    public void Put(Graphics g, Bitmap bmp, int x, int y)
    {
        var p = ToClient(x, y);
        g.DrawImageUnscaled(bmp, p.X - bmp.Width / 2, p.Y - bmp.Height / 2);
    }
}
