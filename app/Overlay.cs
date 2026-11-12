namespace Bolok;

// 화면 전체를 덮는 투명 창: 구멍·부스러기·씨앗·하트·말풍선.
// WS_EX_TRANSPARENT 라 클릭이 전부 뒤로 지나간다 — 눈으로만 가려지고 일은 막지 않는다
public sealed class Overlay : Form
{
    readonly Brain b;
    readonly Func<Sprites> sprites;
    readonly Func<Game?> game;
    public Func<Runner?> Runner = () => null;
    public Func<Breakout?> Break = () => null;
    public Func<Worms?> WormsGame = () => null;
    public Func<Minigun?> MinigunGame = () => null;
    public Func<Icbm?> IcbmGame = () => null;
    public Func<Squad?> SquadGame = () => null;
    public Func<IReadOnlyList<string>> SquadFriends = () => Array.Empty<string>();
    Icbm? icbmSeen;
    int icbmVersion = -1;
    List<Rectangle> craterRects = new();
    Minigun? gunSeen;
    int carvedSeen;
    Bitmap? gunHoles;   // 파인 칸 그림 (칸 하나가 한 점, 그릴 때 4배로 늘린다)
    Worms? wormsSeen;
    int blastsSeen;
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

    Point ToClient(double x, double y) => new((int)x - Left, (int)y - Top);

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
        var r = new List<Rectangle>();
        int s = sprites().Cell;
        foreach (var f in b.Bits) { var p = ToClient(f.X, f.Y); r.Add(new Rectangle(p.X, p.Y, 8, 8)); }
        foreach (var h in b.Hearts) { var p = ToClient(h.X, h.Y); r.Add(new Rectangle(p.X - s, p.Y - s * 2, s * 2, s * 2)); }
        foreach (var seed in b.Foods) { var p = ToClient(seed.X, seed.Y); r.Add(new Rectangle(p.X - s, p.Y - s, s * 2, s * 2)); }
        if (b.Emoting) r.Add(EmoteRect());
        if (b.Saying) r.Add(Client(NoteRect(b.X, b.Y, b.Area, b.SayLines!)));
        if (Runner() is { } run)
        {
            foreach (var t in run.Things) if (!t.Gone) r.Add(Client(Rectangle.Inflate(t.Box, 8, 8)));
            r.Add(Client(EnergyRect(run)));
            if (NoteLines(run) is { } note) r.Add(Client(NoteRect(run.PetX, run.Ground, run.Track, note)));
        }
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
        if (WormsGame() is { } wm)
        {
            int k = b.Scale, cell = b.CellPx;
            foreach (var h in new[] { wm.Me, wm.Cpu }) r.Add(Client(new Rectangle((int)h.X - 24 * k, (int)h.Y - 54 * k, 48 * k, 58 * k)));
            r.Add(Client(Rectangle.Inflate(new Rectangle((int)wm.Active.X, (int)wm.CenterY(wm.Active), 0, 0), (int)(cell * 1.4), (int)(cell * 1.4))));
            foreach (var sh in wm.Shells) r.Add(Client(new Rectangle((int)sh.X - 12 * k, (int)sh.Y - 12 * k, 24 * k, 24 * k)));
            foreach (var bl in wm.Blasts) if (wm.Clock - bl.At < .5) r.Add(Client(Rectangle.Inflate(new Rectangle((int)bl.X, (int)bl.Y, 0, 0), 22 * k, 22 * k)));
            r.Add(Client(WormsHud(wm)));
            if (WormsNote(wm) is { } note) r.Add(Client(NoteRect(b.X, b.Y, wm.Field, note)));
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
        if (SquadGame() is { } sq)
        {
            int k = sq.K;
            foreach (var bg in sq.Bugs) r.Add(Client(Around(bg.X, bg.Y, sq.BugR + 4 * k, sq.BugR + 4 * k)));
            if (sq.Boss is { } boss) r.Add(Client(Around(boss.X, boss.Y - 8 * k, sq.BossR + 12 * k, sq.BossR + 22 * k)));
            foreach (var gt in sq.Gates) r.Add(Client(Around(gt.X, gt.Y, gt.W / 2 + 4, sq.GateH / 2 + 10 * k)));
            foreach (var bx in sq.Boxes) r.Add(Client(Around(bx.X, bx.Y - 8 * k, sq.BoxHalf + 8 * k, sq.BoxHalf + 22 * k)));
            foreach (var sh in sq.Shots) r.Add(Client(Around(sh.X, sh.Y, 5 * k, 8 * k)));
            foreach (var pp in sq.Pops) r.Add(Client(Around(pp.X, pp.Y, pp.R + 6, pp.R + 6)));
            r.Add(Client(SquadRect(sq)));
        }
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
        var once = new List<Rectangle>();
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
        if (WormsGame() is { } wm)
        {
            if (wm != wormsSeen) { (wormsSeen, blastsSeen) = (wm, 0); once.Add(Client(wm.Field)); }
            for (; blastsSeen < wm.Blasts.Count; blastsSeen++)
            {
                var bl = wm.Blasts[blastsSeen];
                once.Add(Client(Rectangle.Inflate(new Rectangle((int)bl.X, (int)bl.Y, 0, 0), (int)bl.R + 4, (int)bl.R + 4)));
            }
        }
        InvalidateMerged(last.Concat(now).Concat(once));
        last = now;
    }

    // 다시 칠할 곳을 64px 칸으로 모아 이어진 칸은 한 줄로 알린다. 작은 사각형을 수백 개 따로 알리면
    // 윈도우가 합치느라 한 프레임이 100ms 까지 늘었다 (벽돌깨기 불공, 2026-11-04)
    const int DirtyCell = 64;
    readonly HashSet<(int X, int Y)> dirtyCells = new();
    void InvalidateMerged(IEnumerable<Rectangle> rects)
    {
        var area = ClientRectangle;
        dirtyCells.Clear();
        foreach (var r0 in rects)
        {
            var r = Rectangle.Intersect(r0, area); // 다른 모니터 것은 버린다
            if (r.IsEmpty) continue;
            for (int y = r.Top / DirtyCell; y <= (r.Bottom - 1) / DirtyCell; y++)
                for (int x = r.Left / DirtyCell; x <= (r.Right - 1) / DirtyCell; x++) dirtyCells.Add((x, y));
        }
        foreach (var row in dirtyCells.GroupBy(c => c.Y))
        {
            var xs = row.Select(c => c.X).Order().ToList();
            for (int i = 0, j; i < xs.Count; i = j + 1)
            {
                for (j = i; j + 1 < xs.Count && xs[j + 1] == xs[j] + 1; j++) { }
                Invalidate(new Rectangle(xs[i] * DirtyCell, row.Key * DirtyCell, (xs[j] - xs[i] + 1) * DirtyCell, DirtyCell));
            }
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
        if (Runner() is { } run) DrawRun(g, run);
        if (Break() is { } bo) DrawBreak(g, bo);
        if (WormsGame() is { } wm) DrawWorms(g, wm);
        if (MinigunGame() is { } mg) DrawMinigun(g, mg);
        if (IcbmGame() is { } ic) DrawIcbm(g, ic);
        if (SquadGame() is { } sq) DrawSquad(g, sq);
    }

    // ── 쿠키런 모드: 점수판 없이 길 위의 것과 발밑의 가는 체력 막대만 ──
    Rectangle EnergyRect(Runner run)
    {
        int w = 20 * b.Scale;
        return new Rectangle((int)run.PetX - w / 2, (int)run.Ground + 4, w, 2 * b.Scale);
    }

    void DrawRun(Graphics g, Runner run)
    {
        var sp = sprites();
        foreach (var t in run.Things)
        {
            if (t.Gone) continue;
            var p = ToClient(t.X - t.Ox, t.Y - t.Oy);
            g.DrawImageUnscaled(sp.Get(t.Sprite, 0, t.Scale), p.X, p.Y);
        }
        var e = Client(EnergyRect(run));
        g.FillRectangle(barLine, e.X - 1, e.Y - 1, e.Width + 2, e.Height + 2);
        g.FillRectangle(barBack, e);
        g.FillRectangle(run.Energy < 25 ? redFill : barFill, e.X, e.Y, (int)(e.Width * run.Energy / 100), e.Height);
        if (NoteLines(run) is { } note) DrawNoteAt(g, run.PetX, run.Ground, run.Track, note);
    }

    // 처음 4초는 조작 안내, 레벨이 오르면 잠깐 「Lv.N!」. 글자 말풍선은 이것뿐이다 (YELL 이 달라고 함)
    const double GuideSec = 4;
    static string[]? NoteLines(Runner run) =>
        run.Clock < GuideSec ? new[] { $"Lv.{run.Level}  목표 {Bolok.Runner.GoalOf(run.Level)}", "Ctrl: 점프", "Shift: 앉기", "Ctrl+Shift 꾹: 종료" }
        : run.Clock - run.LevelAt < 1.6 ? new[] { $"Lv.{run.Level}!" } : null;

    Font? noteFont;
    int noteFontScale;
    readonly Dictionary<string, Size> noteSizes = new();
    static readonly Graphics measure = Graphics.FromImage(new Bitmap(1, 1));

    Font NoteFont()
    {
        if (noteFont == null || noteFontScale != b.Scale)
        {
            noteFont?.Dispose();
            noteSizes.Clear();
            noteFont = new Font("Malgun Gothic", 4 * b.Scale + 3, FontStyle.Bold, GraphicsUnit.Pixel);
            noteFontScale = b.Scale;
        }
        return noteFont;
    }

    // 볼록이 왼쪽, 몸 높이 가운데에 꼬리를 볼록이 쪽으로. 왼쪽에 자리가 없으면 오른쪽에
    Rectangle NoteRect(double petX, double feet, Rectangle area, string[] lines)
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

    void DrawNoteAt(Graphics g, double petX, double feet, Rectangle area, string[] lines)
    {
        var r = NoteRect(petX, feet, area, lines);
        DrawNote(g, Client(r), lines, r.X < petX);
    }

    // ── 벽돌깨기: 창 위에 조각 금, 깨진 자리는 까만 구멍, 떨어지는 파편, 쟁반, 쇠공, 남은 공 ──
    static string[]? BreakNote(Breakout bo) => bo.Clock < 4 ? new[] { "마우스: 쟁반", "클릭: 던지기", "Esc: 끝" } : null;

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
    internal static (List<(int I, int J, bool Hole)> Cells, List<(int X, int Y)> Cracks) CraterShape(Icbm.Crater cr)
    {
        int c = Icbm.Cell;
        var cells = new List<(int, int, bool)>();
        var cracks = new List<(int, int)>();
        if (cr.R < c) return (cells, cracks);
        var rng = new Random((int)(cr.X * 7 + cr.Y * 13));
        double p3 = rng.NextDouble() * 6.3, p5 = rng.NextDouble() * 6.3, p7 = rng.NextDouble() * 6.3, p11 = rng.NextDouble() * 6.3;
        double F(double a) => 1 + .13 * Math.Sin(3 * a + p3) + .09 * Math.Sin(5 * a + p5) + .07 * Math.Sin(7 * a + p7) + .06 * Math.Sin(11 * a + p11);
        int n = (int)Math.Ceiling(cr.R * 1.4 / c) + 1;
        for (int j = -n; j <= n; j++)
            for (int i = -n; i <= n; i++)
            {
                double x = (i + .5) * c, y = (j + .5) * c, d = Math.Sqrt(x * x + y * y), r = cr.R * F(Math.Atan2(y, x));
                if (d < r - c) cells.Add((i, j, true));
                else if (d < r) cells.Add((i, j, false));
            }
        int h = c / 2, count = 3 + rng.Next(3);
        for (int m = 0; m < count; m++) // 금: 가장자리에서 바깥으로 지그재그
        {
            double a = rng.NextDouble() * Math.PI * 2, r = cr.R * F(a) - h, side = 0;
            int len = (int)((5 + rng.Next(6)) * cr.R / Math.Max(cr.R0, 1));
            for (int t = 0; t < len; t++, r += h)
            {
                side = Math.Clamp(side + (rng.NextDouble() - .5) * 1.2, -1.5, 1.5);
                double x = Math.Cos(a) * r - Math.Sin(a) * side * h, y = Math.Sin(a) * r + Math.Cos(a) * side * h;
                cracks.Add(((int)Math.Floor(x / h) * h, (int)Math.Floor(y / h) * h));
            }
        }
        return (cells, cracks);
    }

    // 구멍 + 금이 닿을 수 있는 넓이 (다시 칠할 자리)
    internal static int CraterReach(Icbm.Crater cr) => (int)(cr.R0 * 1.4) + 6 * Icbm.Cell;

    // 친구 구멍(Friend)이면 안쪽에 그 친구가 사는 자연을 터질 때부터 바로 그린다. 아니면 검은 구멍
    internal static void PaintCrater(Graphics g, Icbm.Crater cr, Point ctr, Brush rim, Brush hole, double clock = 0, int k = 3)
    {
        int c = Icbm.Cell;
        var (cells, cracks) = CraterShape(cr);
        foreach (var (i, j, inside) in cells) g.FillRectangle(inside && cr.Friend == null ? hole : rim, ctr.X + i * c, ctr.Y + j * c, c, c);
        foreach (var (x, y) in cracks) g.FillRectangle(rim, ctr.X + x, ctr.Y + y, c / 2, c / 2);
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
    static void PaintNature(Graphics g, Rectangle box, int floor, int seed, double clock, int px)
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

    // ── 볼록이 부대 ──
    static Rectangle Around(double x, double y, double hw, double hh) => new((int)(x - hw), (int)(y - hh), (int)(hw * 2), (int)(hh * 2));

    // 부대원·헬기·머릿수 숫자가 있는 자리
    static Rectangle SquadRect(Squad sq)
    {
        int k = sq.K;
        double left = sq.X - sq.SquadHalf - 24 * k, right = Math.Max(sq.X + sq.SquadHalf + 24 * k, sq.HeliX + 24 * k);
        return Rectangle.FromLTRB((int)left, (int)(sq.SquadTop - 30 * k), (int)right, (int)sq.Ground + 6 * k);
    }

    // 숫자와 + × (도트 숫자 그림, 화면 좌표 가운데)
    void Label(Graphics g, string text, double cx, double cy, int k)
    {
        int step = 5 * k, x0 = (int)cx - (text.Length - 1) * step / 2;
        for (int i = 0; i < text.Length; i++) Put(g, sprites().Get("num/" + (text[i] == '×' ? 'x' : text[i]), 0, k), x0 + i * step, (int)cy);
    }

    // 문: 파란 테두리 + 체크무늬 (빈 칸은 투명이라 뒤가 비친다 — 투명색 창이라 진짜 반투명은 분홍빛이 돈다)
    readonly Dictionary<(int, int, int), Bitmap> glass = new();
    Bitmap Glass(int w, int h, int k)
    {
        if (glass.TryGetValue((w, h, k), out var bmp)) return bmp;
        bmp = new Bitmap(Math.Max(1, w), Math.Max(1, h), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        using var edge = new SolidBrush(Color.FromArgb(40, 110, 220));
        using var fill = new SolidBrush(Color.FromArgb(120, 180, 250));
        using var light = new SolidBrush(Color.FromArgb(200, 230, 255));
        for (int y = 0; y < h; y += k)
            for (int x = 0; x < w; x += k)
            {
                bool border = x < 2 * k || y < 2 * k || x >= w - 2 * k || y >= h - 2 * k;
                if (border) g.FillRectangle(edge, x, y, k, k);
                else if (y < 3 * k) g.FillRectangle(light, x, y, k, k);
                else if ((x / k + y / k) % 2 == 0) g.FillRectangle(fill, x, y, k, k);
            }
        return glass[(w, h, k)] = bmp;
    }

    static readonly string[] SquadPrize = { "", "game/mg", "game/grenade", "game/heli" };

    void DrawSquad(Graphics g, Squad sq)
    {
        var sp = sprites();
        int k = sq.K, small = Math.Max(2, k - 1);
        foreach (var gt in sq.Gates) // 문: 「+3」「×2」
        {
            if (gt.Used) continue;
            var p = ToClient(gt.X - gt.W / 2, gt.Y - sq.GateH / 2);
            g.DrawImageUnscaled(Glass((int)gt.W, (int)sq.GateH, k), p.X, p.Y);
            Label(g, gt.Mul > 1 ? "×" + gt.Mul : "+" + gt.Add, gt.X, gt.Y, k + 2); // 크게: 어느 문이 좋은지 한눈에
        }
        foreach (var bx in sq.Boxes) // 선물 상자: 위에 받을 무기, 앞에 남은 숫자. 맞으면 살짝 들썩
        {
            int shake = sq.Clock - bx.Hit < .05 ? k : 0;
            Put(g, sp.Get("game/gift", 0, k), (int)bx.X, (int)bx.Y - shake);
            string prize = sq.Weapon >= Squad.MaxWeapon ? "game/missile" : SquadPrize[sq.Weapon + 1];
            Put(g, sp.Get(prize, 0, prize == "game/grenade" ? k + 1 : small), (int)bx.X, (int)(bx.Y - sq.BoxHalf - 8 * k));
            Label(g, bx.Hp.ToString(), bx.X, bx.Y + 3 * k, k);
        }
        int n = 0;
        foreach (var bg in sq.Bugs) Put(g, sp.Get("game/bug", ((int)(sq.Clock * 6) + n++) % 2, sq.BugScale), (int)bg.X, (int)bg.Y);
        if (sq.Boss is { } boss) // 보스: 체력 막대와 숫자
        {
            Put(g, sp.Get("game/bugboss", (int)(sq.Clock * 4) % 2, k + 1), (int)boss.X, (int)boss.Y);
            var bar = ToClient(boss.X - 22 * k, boss.Y - sq.BossR - 6 * k);
            using var dark = new SolidBrush(Color.FromArgb(60, 34, 80));
            using var red = new SolidBrush(Color.FromArgb(232, 72, 92));
            g.FillRectangle(dark, bar.X - k, bar.Y - k, 44 * k + 2 * k, 3 * k + 2 * k);
            g.FillRectangle(red, bar.X, bar.Y, (int)(44 * k * Math.Max(0, boss.Hp) / (double)boss.Max), 3 * k);
            Label(g, Math.Max(0, boss.Hp).ToString(), boss.X, boss.Y - sq.BossR - 14 * k, k);
        }
        using (var bullet = new SolidBrush(Color.FromArgb(255, 220, 90)))
        using (var edge = new SolidBrush(Color.FromArgb(150, 90, 30)))
            foreach (var sh in sq.Shots)
            {
                if (sh.Nade) { Put(g, sp.Get("game/grenade", 0, small), (int)sh.X, (int)sh.Y); continue; }
                var p = ToClient(sh.X - k, sh.Y - 3 * k);
                g.FillRectangle(edge, p.X - 1, p.Y - 1, 2 * k + 2, 6 * k + 2);
                g.FillRectangle(bullet, p.X, p.Y, 2 * k, 6 * k);
            }
        foreach (var pp in sq.Pops) // 터짐
            Put(g, sp.Get("game/boom", Math.Min(3, (int)((sq.Clock - pp.At) / .12)), Math.Max(1, (int)(pp.R / 14))), (int)pp.X, (int)pp.Y);
        var friends = SquadFriends();
        int i = 0;
        foreach (var (fx, fy) in sq.Formation()) // 부대원: 도감 친구들이 통통 튄다
        {
            int bob = Math.Sin(sq.Clock * 12 + i) > 0 ? -k : 0;
            Put(g, sp.Get("thumb/" + (friends.Count > 0 ? friends[i % friends.Count] : "hamster"), 0, 1), (int)fx, (int)fy + bob);
            i++;
        }
        if (sq.Weapon >= 3) Put(g, sp.Get("game/heli", (int)(sq.Clock * 20) % 2, k), (int)sq.HeliX, (int)(sq.SquadTop - 10 * k));
        if (sq.Stage == Squad.Phase.Play) Label(g, sq.Members.ToString(), sq.X, sq.SquadTop - 14 * k, k + 1); // 머릿수
    }

    // ── 미니건 ──
    static string[]? GunNote(Minigun mg) => mg.Clock < 4 ? new[] { "마우스: 조준", "왼쪽 누르고 있기: 미니건", "오른쪽 클릭: 수류탄", "Esc: 끝" } : null;
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

    // ── 포트리스 ──
    static string[]? WormsNote(Worms w) => w.Clock < 5
        ? new[] { $"내 대포: {w.Me.Gun.Label} (HP {w.Me.Gun.Hp})", "마우스: 조준", "누르고 있다 놓기: 쏘기", "←→: 걷기  Space: 점프", "위 화살표: 바람", "Esc: 끝" } : null;
    readonly SolidBrush grass = new(Color.FromArgb(120, 200, 90)), grassDark = new(Color.FromArgb(70, 140, 60)), hpGood = new(Color.FromArgb(110, 200, 110)),
        dirt = new(Color.FromArgb(139, 94, 60)), windFill = new(Color.FromArgb(96, 150, 230));
    Pen? holePen;

    Rectangle WormsHud(Worms w) => new(w.Field.Left + w.Field.Width / 2 - 46 * b.Scale, w.Field.Top + 4 * b.Scale, 92 * b.Scale, 14 * b.Scale);

    void DrawWorms(Graphics g, Worms w)
    {
        var sp = sprites();
        int k = b.Scale, cell = b.CellPx, small = Math.Max(1, k - 1);
        foreach (var l in w.Lands) // 섬 윗변의 풀, 밑면의 흙 줄 (섬 두께가 보이게)
        {
            var r = Client(l);
            g.FillRectangle(grass, r.X, r.Y, r.Width, 2 * k);
            g.FillRectangle(grassDark, r.X, r.Y + 2 * k, r.Width, k);
            g.FillRectangle(dirt, r.X, r.Bottom - 2 * k, r.Width, 2 * k);
        }
        if (w.Blasts.Count > 0) // 깎인 자리는 섬 안에서만 까만 구멍
        {
            var state = g.Save();
            using var land = new Region();
            land.MakeEmpty();
            foreach (var l in w.Lands) land.Union(Client(l));
            g.SetClip(land, System.Drawing.Drawing2D.CombineMode.Intersect);
            holePen ??= new Pen(holeEdge.Color, k);
            foreach (var bl in w.Blasts)
            {
                var c = ToClient(bl.X, bl.Y);
                int r = (int)bl.R;
                g.FillEllipse(holeFill, c.X - r, c.Y - r, 2 * r, 2 * r);
                g.DrawEllipse(holePen, c.X - r, c.Y - r, 2 * r, 2 * r);
            }
            g.Restore(state);
        }
        var cpu = w.Cpu; // 라이벌 (볼록이는 제 창으로 그려진다)
        if (!cpu.Out)
        {
            var p = ToClient(cpu.X - 20 * k, cpu.Y - 40 * k);
            g.DrawImageUnscaled(sp.Get($"adult_grumpy/1/{w.PoseOf(cpu)}", (int)(w.Clock * 6), k, cpu.Dir < 0), p.X, p.Y);
        }
        foreach (var h in new[] { w.Me, cpu }) // 체력 막대, 차례 화살표
        {
            if (h.Out) continue;
            var bar = ToClient(h.X - 10 * k, h.Y - 34 * k);
            g.FillRectangle(barLine, bar.X - 1, bar.Y - 1, 20 * k + 2, 2 * k + 2);
            g.FillRectangle(barBack, bar.X, bar.Y, 20 * k, 2 * k);
            g.FillRectangle(h.Hp * 3 < h.Gun.Hp ? redFill : hpGood, bar.X, bar.Y, 20 * k * h.Hp / h.Gun.Hp, 2 * k);
            Number(g, h.Hp, (int)h.X + 16 * k, (int)h.Y - 33 * k, small);
            if (w.Active == h && w.Stage == Worms.Phase.Turn)
            {
                int bob = (int)(Math.Sin(w.Clock * 6) * k), ax = bar.X + 10 * k, ay = bar.Y - 4 * k + bob;
                g.FillPolygon(barFill, new[] { new Point(ax - 3 * k, ay - 3 * k), new Point(ax + 3 * k, ay - 3 * k), new Point(ax, ay) });
            }
        }
        if (w.Stage == Worms.Phase.Turn && !w.Active.Dead && (w.MyTurn || w.PoseOf(cpu) == "angry")) // 조준경과 힘
        {
            var h = w.Active;
            double a = w.MyTurn ? w.Aim : w.CpuAim;
            Put(g, sp.Get("game/aim", 0, small), (int)(h.X + Math.Cos(a) * cell * .9), (int)(w.CenterY(h) + Math.Sin(a) * cell * .9));
            if (w.MyTurn && w.ChargeFrom >= 0)
            {
                var bar = ToClient(h.X - 10 * k, h.Y - 30 * k);
                g.FillRectangle(barLine, bar.X - 1, bar.Y - 1, 20 * k + 2, 2 * k + 2);
                g.FillRectangle(w.Power > .8 ? redFill : barFill, bar.X, bar.Y, (int)(20 * k * w.Power), 2 * k);
            }
        }
        foreach (var s in w.Shells)
        {
            if (s.Kind == "multi") { Put(g, sp.Get("game/ball", 0, small), (int)s.X, (int)s.Y); continue; } // 멀티캐논은 작은 쇠공
            int sk = s.Kind == "giant" ? k * 2 : k; // 왕대포는 두 배
            var state = g.Save(); // 포탄은 날아가는 쪽을 본다
            var c = ToClient(s.X, s.Y);
            g.TranslateTransform(c.X, c.Y);
            g.RotateTransform((float)(Math.Atan2(s.Vy, s.Vx) * 180 / Math.PI));
            g.DrawImageUnscaled(sp.Get("game/rocket", 0, sk), (int)(-19.5 * sk), (int)(-19.5 * sk));
            g.Restore(state);
        }
        foreach (var bl in w.Blasts)
        {
            double t = w.Clock - bl.At;
            if (t < .45) Put(g, sp.Get("game/boom", Math.Min(3, (int)(t / .11)), Math.Max(1, (int)Math.Round(k * bl.R / (50.0 * k / 3)))), (int)bl.X, (int)bl.Y); // 폭발 크기만큼
        }
        // 위 가운데: 구름 + 바람 화살표(길이)와 세기 칸 다섯, 남은 시간, 지금 차례 대포
        var hud = Client(WormsHud(w));
        g.FillRectangle(barLine, hud);
        g.FillRectangle(Brushes.White, hud.X + k, hud.Y + k, hud.Width - 2 * k, hud.Height - 2 * k);
        var at = new Point(hud.X + Left, hud.Y + Top);
        int mid = hud.Y + hud.Height / 2;
        Put(g, sp.Get("prop/cloud", 0, 1), at.X + 9 * k, at.Y + hud.Height / 2);
        int wx = hud.X + 30 * k, len = (int)(Math.Abs(w.Wind) * 12 * k) + k, dir = w.Wind >= 0 ? 1 : -1;
        g.FillRectangle(windFill, Math.Min(wx, wx + dir * len), mid - k, len, 2 * k);
        g.FillPolygon(windFill, new[] { new Point(wx + dir * (len + 4 * k), mid), new Point(wx + dir * len, mid - 4 * k), new Point(wx + dir * len, mid + 4 * k) });
        int bars = (int)Math.Round(Math.Abs(w.Wind) * 5);
        for (int i = 0; i < 5; i++) g.FillRectangle(i < bars ? windFill : barBack, hud.X + 48 * k + i * 3 * k, mid - 3 * k, 2 * k, 6 * k);
        Number(g, (int)Math.Ceiling(w.TimeLeft), at.X + 70 * k, at.Y + hud.Height / 2, small);
        var gun = w.Active.Gun;
        Put(g, gun.Shells > 1 ? sp.Get("game/ball", 0, small) : sp.Get("game/rocket", 0, gun.Name == "giant" ? k : small), at.X + 84 * k, at.Y + hud.Height / 2);
        if (WormsNote(w) is { } note) DrawNoteAt(g, b.X, b.Y, w.Field, note);
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

    Rectangle Client(Rectangle r) => new(r.X - Left, r.Y - Top, r.Width, r.Height);

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

    void Put(Graphics g, Bitmap bmp, int x, int y)
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
