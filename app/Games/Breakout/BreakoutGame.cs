using System.Diagnostics;
using System.Text.Json;

namespace Bolok;

// 벽돌깨기 모듈: 앱에 붙이기(판·조작·볼록이), 그리기, 기록, 자가 검사, 디버그 명령. 규칙은 Breakout.cs
// 판은 창이 있는 모니터 전체 (공이 모니터 테두리에서 튕긴다). 볼록이는 본체 창 그대로 맨 아래에서 쟁반을 머리 위로 받치고 마우스를 따라 좌우로 움직인다
// 조작: 마우스 좌우 = 쟁반, 왼쪽 버튼 = 던지기. Esc 끝 (보스 키). 표시·효과·소리·조작은 공통 도구(app/Games/Kit)를 쓴다
sealed class BreakoutGame : IGameMode
{
    readonly IGameHost host;
    Breakout? game;
    Session? ses;
    readonly List<(IntPtr H, Rectangle R)> windows = new(); // 조각낸 창들: 닫히거나 움직이면 끝낸다
    int best, bestBefore;
    bool auto, ended, guideOn, guideMove;
    double guideX;
    Rectangle guideShown; // 안내 말풍선이 그려진 자리 (그림 창 좌표)
    double nextProbe;
    Watch hud;
    int brokenFrom, brokenTo; // 이번 프레임에 새로 깨진 조각: BrokenOrder[brokenFrom..brokenTo)
    Perf perf;

    static readonly Dictionary<string, Sound> Sounds = new()
    {
        ["plate"] = Sound.Tap, ["break"] = Sound.Hit, ["miss"] = Sound.Hurt, ["won"] = Sound.Pick,
        ["item:split"] = Sound.Pick, ["item:wide"] = Sound.Pick, ["item:giant"] = Sound.Pick, ["item:fire"] = Sound.Pick, ["item:slow"] = Sound.Pick, ["item:life"] = Sound.Pick,
    };

    public BreakoutGame(IGameHost host) => this.host = host;
    public string Id => "breakout";
    public string MenuKey => "menu.breakout";
    public bool Running => game != null;

    public void Start()
    {
        if (Running) return;
        var b = host.Pet;
        var screens = Screen.AllScreens.Select(s => s.WorkingArea).ToList();
        if (Breakout.Pick(Native.Foreground(), Native.ListWindows(), b.Area, screens, b.CellPx) is not { } pick)
        {
            // 창모드 창이 없다: 말 대신 「X 표 창」 그림을 머리 위에 띄우고 고개를 젓는다 (펫은 말하지 않는다)
            b.Emote("nowin", 3);
            b.ShakeUntil = b.Now + .5;
            Dbg.Log("break-none");
            return;
        }
        windows.Clear();
        foreach (var t in pick.Targets) windows.Add((t.Win.H, t.Win.R));
        var bo = Open(new Breakout(pick.Field, pick.Targets.Select(t => t.Board).ToList(), b.Scale, new Random()));
        ses = Session.Open(host, pick.Field, () => Stop(true));
        ses.Layer?.HidePointer(); // 쟁반이 커서를 따라다닌다: 커서는 숨긴다 (끝 화면에서 다시 보인다)
        ses.Guide("note.break_mouse", "note.break_click", "note.esc");
        nextProbe = 0;
        Dbg.Log("break-start", ("field", pick.Field.ToString()), ("boards", string.Join(" ", bo.Boards.Select(r => $"{r.X},{r.Y},{r.Width}x{r.Height}"))), ("tiles", bo.Tiles.Count));
    }

    // 판을 편다 (화면 없는 검사·장면도 같이 쓴다): 볼록이는 모니터 맨 아래 가운데에서 쟁반을 든다
    Breakout Open(Breakout bo)
    {
        var b = host.Pet;
        game = bo;
        bo.OnEvent = e => OnEvent(bo, e);
        (hud, plateSpot, sideSpot, brokenFrom, brokenTo, bestBefore, ended, perf) = (default, default, default, 0, 0, best, false, default);
        (guideOn, guideMove, guideShown) = (false, false, Rectangle.Empty);
        b.StartPuppet();
        (b.X, b.Y, b.Dir, b.Pose) = (bo.Field.Left + bo.Field.Width / 2.0, bo.Field.Bottom - b.K.FloorGap, 1, "carry");
        return bo;
    }

    void OnEvent(Breakout bo, string e)
    {
        Snd.Play(Sounds, e);
        bool item = e.StartsWith("item:", StringComparison.Ordinal);
        if (e == "miss") { if (bo.Lives > 0) host.Pet.Emote("tear", 1); } // 마지막 공을 놓쳤을 땐 끝 화면이 뜬다 (표정은 닫힐 때)
        else if (item) host.Pet.Emote(e == "item:life" ? "heart" : "spark", .8);
        if (item || e is "miss" or "won" or "over") Dbg.Log("break", ("event", e), ("broken", bo.Broken), ("lives", bo.Lives), ("balls", bo.Balls.Count));
    }

    public void Frame(double dt)
    {
        var (bo, s, b) = (game!, ses!, host.Pet);
        long t0 = Stopwatch.GetTimestamp();
        s.Tick();
        if (bo.Stage == Breakout.Phase.Play)
        {
            // 쟁반(볼록이)은 커서를 거의 바로 따라가되 살짝 미끄러진다. 끝 화면에서는 그 자리에 선다
            double half = bo.PlateW / 2.0, to = auto && LowBall(bo) is { } low ? low.X : s.Mouse.X;
            b.X += (Math.Clamp(to, bo.Field.Left + half, bo.Field.Right - half) - b.X) * Math.Min(1, dt * 30);
            if (s.LeftHit) bo.Launch();
        }
        (b.Y, b.Dir, b.Pose) = (bo.Field.Bottom - b.K.FloorGap, 1, "carry");
        bo.Update(dt, b.X, PlateTop());
        Track();
        perf.Update(Stopwatch.GetElapsedTime(t0).TotalMilliseconds, dt);

        if (bo.Stage == Breakout.Phase.Play && bo.Clock > nextProbe && windows.Count > 0)
        {
            nextProbe = bo.Clock + .25;
            foreach (var (h, r) in windows)
            {
                var st = Native.Probe(h);
                // 창이 닫히거나 움직이면 조각이 어긋나서 끝낸다
                if (!st.Alive || st.Minimized || st.R != r) { Dbg.Log("break-window-gone"); Stop(false); return; }
            }
        }
        if (bo.Done || bo.Stage != Breakout.Phase.Play && bo.Clock - bo.EndAt > .8 && s.LeftHit) Stop(false); // 끝 화면은 0.8초 뒤부터 눌러서 닫을 수 있다
    }

    // 디버그 auto: 가장 낮게 내려오는 공을 받는다
    static Breakout.Ball? LowBall(Breakout bo)
    {
        Breakout.Ball? low = null;
        double lowY = double.MinValue;
        foreach (var x in bo.Balls)
        {
            if (x.Stuck) continue;
            double y = x.Vy > 0 ? x.Y : x.Y - 2000;
            if (low == null || y > lowY) (low, lowY) = (x, y);
        }
        return low;
    }

    // quit(보스 키·디버그 stop): 결과 화면·표정 없이 바로 끝낸다. 기록은 조용히 남긴다
    public void Stop(bool quit)
    {
        if (game is not { } bo) return;
        game = null;
        if (!auto) best = Math.Max(best, bo.Broken);
        Dbg.Log("break-perf", perf.Report(bo));
        ses?.Close(host.Pet, quit, bo.Stage == Breakout.Phase.Won, "break", ("broken", bo.Broken), ("tiles", bo.Tiles.Count), ("sec", Math.Round(bo.Clock)), ("esc", quit));
        ses = null;
        auto = false;
        Dbg.Log("mode-end", ("id", Id));
        host.Save();
    }

    // ── 쟁반 윗면: 머리 위로 올린 앞발(그 자세 그림에서 가장 높은 곳)에 쟁반 아래 두 줄이 걸친다. 형태(아기·어린이·어른 넷)마다 그림에서 잰다 ──
    readonly Dictionary<(string, int), int> pawTop = new();
    Sprites? pawOf;

    double PlateTop()
    {
        var (b, sp) = (host.Pet, host.Sprites);
        if (!ReferenceEquals(pawOf, sp)) { pawTop.Clear(); pawOf = sp; } // 종이나 그림 데이터가 바뀌었다
        var key = (b.P.Stage, b.P.Cheek(b.K));
        if (!pawTop.TryGetValue(key, out int top)) pawTop[key] = top = PawTop(sp, $"{key.Item1}/{key.Item2}/carry");
        return PlateTopAt(b.Y, top, sp.Cell, b.Scale);
    }

    static int PawTop(Sprites sp, string name) => Props.Trim(Props.Solid(sp.Get(name, 0, 1))).Y0;
    static double PlateTopAt(double feet, int top, int cell, int k) => feet - (cell - top) * k - 3 * k;

    // 한 판의 시간 재기: 규칙(Frame), 다시 칠할 자리(Dirty), 그리기(Draw). 판이 끝나면 break-perf 로 남긴다
    struct Perf
    {
        int frames, paints;
        double updSum, updMax, dirtySum, dirtyMax, paintSum, paintMax, dtSum, dtMax;
        public void Update(double ms, double dt) { frames++; (updSum, updMax, dtSum, dtMax) = (updSum + ms, Math.Max(updMax, ms), dtSum + dt, Math.Max(dtMax, dt)); }
        public void Dirty(double ms) => (dirtySum, dirtyMax) = (dirtySum + ms, Math.Max(dirtyMax, ms));
        public void Paint(double ms) { paints++; (paintSum, paintMax) = (paintSum + ms, Math.Max(paintMax, ms)); }
        public (string, object?)[] Report(Breakout bo)
        {
            int n = Math.Max(1, frames), p = Math.Max(1, paints);
            return new (string, object?)[] { ("fps", Math.Round(frames / Math.Max(.001, dtSum))), ("gapMax", Math.Round(dtMax * 1000)),
                ("updAvg", Math.Round(updSum / n, 3)), ("updMax", Math.Round(updMax, 2)), ("dirtyAvg", Math.Round(dirtySum / n, 3)), ("dirtyMax", Math.Round(dirtyMax, 2)),
                ("paintAvg", Math.Round(paintSum / p, 3)), ("paintMax", Math.Round(paintMax, 2)), ("paints", paints), ("tiles", bo.Tiles.Count), ("broken", bo.Broken) };
        }
    }

    // ── 그리기: 창 위에 조각 금, 깨진 자리는 까만 구멍, 떨어지는 파편·아이템, 쟁반, 쇠공, 볼록이 곁에 남은 공·켜진 아이템, 끝 화면 ──
    // 가장 많이 칠하는 게임이라 (조각 330개) 프레임마다 새로 잡는 메모리 없이, 바뀐 자리만 다시 칠한다:
    //  · 금은 조각마다 넷씩 칠하지 않고 줄·칸 경계를 통째로 긋고, 깨진 조각은 한 줄에서 이어진 것끼리 한 번에 칠한다
    //  · 공·아이템은 둘레를 잘라 둔 그림(Stamps), 쟁반은 늘인 그림을 한 번 만들어 둔다
    //  · 쟁반·볼록이 곁 표시는 움직이거나 바뀐 프레임에만, 새로 깨진 조각은 그 자리만 다시 칠한다
    static readonly SolidBrush CrackLight = new(Color.FromArgb(244, 246, 250)), CrackDark = new(Color.FromArgb(58, 62, 72)), HoleEdge = new(Color.FromArgb(46, 50, 60));

    // 금 굵기 (px): 위·왼쪽 밝은 줄과 아래·오른쪽 어두운 줄이 저마다 이만큼 (전에는 배율의 절반이라 배율 3 에서 1px 이었다)
    public static int CrackPx(int k) => Math.Max(1, k);

    Sprites? artOf;
    int artK;
    Stamp ball = null!, fireBall = null!, bigBall = null!, bigFire = null!, hudBall = null!;
    readonly Stamp[] item = new Stamp[Breakout.Kinds.Length], hudItem = new Stamp[Breakout.Kinds.Length];
    readonly Dictionary<int, Bitmap> plates = new(); // 너비 → 늘인 쟁반 그림

    void Art(Overlay o, int k)
    {
        if (ReferenceEquals(artOf, o.Art) && artK == k) return;
        var st = Stamps.Of(o.Art);
        (artOf, artK) = (o.Art, k);
        int small = GameHud.Small(k);
        (ball, fireBall, bigBall, bigFire, hudBall) = (st.Get("game/ball", 0, k), st.Get("game/fireball", 0, k), st.Get("game/ball", 0, 2 * k), st.Get("game/fireball", 0, 2 * k), st.Get("game/ball", 0, small));
        for (int i = 0; i < item.Length; i++) (item[i], hudItem[i]) = (st.Get("game/item_" + Breakout.Kinds[i], 0, k), st.Get("game/item_" + Breakout.Kinds[i], 0, small));
        foreach (var p in plates.Values) p.Dispose();
        plates.Clear();
    }

    // 쟁반: 양 끝 6칸은 그대로 두고 가운데를 늘인다 (가운데 칸은 모두 같아서 늘여도 티가 안 난다). 너비마다 한 번만 만든다
    Bitmap Plate(Sprites sp, int k, int w) => plates.TryGetValue(w, out var hit) ? hit : plates[w] = PlateArt(sp, k, w);

    static Bitmap PlateArt(Sprites sp, int k, int w)
    {
        var img = sp.Get("game/plate", 0, k);
        int h = 5 * k;
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        g.DrawImage(img, new Rectangle(0, 0, 6 * k, h), new Rectangle(2 * k, 17 * k, 6 * k, h), GraphicsUnit.Pixel);
        g.DrawImage(img, new Rectangle(6 * k, 0, w - 12 * k, h), new Rectangle(8 * k, 17 * k, 24 * k, h), GraphicsUnit.Pixel);
        g.DrawImage(img, new Rectangle(w - 6 * k, 0, 6 * k, h), new Rectangle(32 * k, 17 * k, 6 * k, h), GraphicsUnit.Pixel);
        return bmp;
    }

    // 가끔만 바뀌는 그림 자리: 바뀐 프레임에만 옛 자리와 새 자리를 다시 칠한다
    struct Spot
    {
        int key;
        public Rectangle Now { get; private set; }
        public Rectangle Dirty { get; private set; } // 이번 프레임에 다시 칠할 자리 (없으면 빈 네모)
        public void Set(Rectangle now, int k)
        {
            Dirty = now == Now && k == key ? Rectangle.Empty : Now.IsEmpty ? now : now.IsEmpty ? Now : Rectangle.Union(Now, now);
            (Now, key) = (now, k);
        }
    }

    Spot plateSpot, sideSpot;

    // 볼록이 곁 표시: 남은 공(날아가는 것은 빼고)은 왼쪽, 켜진 아이템은 오른쪽, 몸 높이에 작게. 모니터 끝이라 자리가 없으면 반대쪽에 이어 놓는다.
    // 아이템은 끝나기 2초 전부터 깜빡인다. 안내 말풍선이 떠 있는 동안에는 숨긴다 (말풍선이 그 자리에 뜬다)
    const int MaxSide = 8;
    readonly int[] sideX = new int[MaxSide], sideArt = new int[MaxSide]; // 그림: -1 공, 0~ 아이템 종류, 깜빡여 안 보이는 칸은 -2
    int sideN, sideY;

    void Side(Breakout bo)
    {
        var b = host.Pet;
        int k = bo.K, s = GameHud.Small(k), px = (int)b.X, near = 18 * k, ballStep = 9 * s, itemStep = 14 * s;
        (sideN, sideY) = (0, (int)b.Y - 9 * k);
        if (ses?.GuideOn(bo.Clock) == true) { sideSpot.Set(Rectangle.Empty, 0); return; }
        int ballSide = px - near - 3 * ballStep - 4 * s < bo.Field.Left ? 1 : -1;
        int itemSide = px + near + 2 * k + 3 * itemStep + 7 * s > bo.Field.Right ? -1 : 1;
        int itemFrom = itemSide == ballSide ? near + 4 * ballStep + 4 * s : near + 2 * k;
        for (int i = 0; i < bo.Lives - 1 && sideN < MaxSide; i++) (sideX[sideN], sideArt[sideN++]) = (px + ballSide * (near + i * ballStep), -1);
        for (int kind = 1, n = 0; kind <= 4; kind++) // wide, giant, fire, slow (Breakout.Kinds 순서)
        {
            double left = (kind == 1 ? bo.WideUntil : kind == 2 ? bo.GiantUntil : kind == 3 ? bo.FireUntil : bo.SlowUntil) - bo.Clock;
            if (left <= 0 || sideN >= MaxSide) continue;
            (sideX[sideN], sideArt[sideN++]) = (px + itemSide * (itemFrom + n++ * itemStep), left > 2 || (int)(bo.Clock * 6) % 2 == 0 ? kind : -2);
        }
        if (sideN == 0) { sideSpot.Set(Rectangle.Empty, 0); return; }
        int x0 = int.MaxValue, x1 = int.MinValue, key = sideN;
        for (int i = 0; i < sideN; i++) (x0, x1, key) = (Math.Min(x0, sideX[i]), Math.Max(x1, sideX[i]), HashCode.Combine(key, sideX[i], sideArt[i]));
        sideSpot.Set(Rectangle.FromLTRB(x0 - 7 * s - 1, sideY - 5 * s - 1, x1 + 7 * s + 1, sideY + 5 * s + 1), key);
    }

    static Rectangle PlateRect(Breakout bo) => new((int)(bo.PlateX - bo.PlateW / 2.0) - 1, (int)bo.PlateTop - 1, bo.PlateW + 2, bo.PlateH + bo.K + 2); // 눌린 자리까지
    static bool Bumped(Breakout bo) => bo.Clock - bo.PlateHitAt < .08; // 공을 받으면 쟁반이 살짝 눌린다

    // 끝 화면: 쟁반 위에 깬 조각 수 (새 기록이면 왕관). 모니터 전체가 판이라 한가운데가 아니라 볼록이 곁에 작게 둔다
    Rectangle resultAt; // GameHud.Result 에 넘길 판 (그 가운데 조금 위에 상자가 놓인다)

    static Rectangle ResultField(Breakout bo, double petX)
    {
        int k = bo.K, cx = (int)Math.Clamp(petX, bo.Field.Left + 30 * k, bo.Field.Right - 30 * k), cy = (int)bo.PlateTop - 23 * k;
        return new Rectangle(cx - 50 * k, cy - 45 * k, 100 * k, 100 * k);
    }

    static Rectangle ResultRect(Rectangle f, int k) => Overlay.Around(f.Left + f.Width / 2, f.Top + 45 * k, 30 * k + 16, 20 * k + 18);

    // 프레임마다 한 번: 새로 깨진 조각, 표시 값·자리가 바뀌었는지 (바뀐 프레임에만 그 자리를 다시 칠한다)
    void Track()
    {
        var bo = game!;
        (brokenFrom, brokenTo) = (brokenTo, bo.BrokenOrder.Count);
        hud.Set((int)bo.Stage);
        plateSpot.Set(PlateRect(bo), HashCode.Combine(bo.PlateW, Bumped(bo)));
        Side(bo);
        bool note = ses?.GuideOn(bo.Clock) == true; // 안내 말풍선은 볼록이를 따라다닌다: 움직이거나 꺼질 때만 다시 칠한다
        (guideMove, guideOn, guideX) = (note != guideOn || note && host.Pet.X != guideX, note, host.Pet.X);
        if (bo.Stage != Breakout.Phase.Play && !ended)
        {
            (ended, resultAt) = (true, ResultField(bo, host.Pet.X));
            ses?.Layer?.ShowPointer(); // 끝났다는 걸 커서로도 알린다
        }
    }

    Stamp BallArt(Breakout bo) => bo.Giant ? (bo.Fire ? bigFire : bigBall) : bo.Fire ? fireBall : ball;
    Stamp ItemArt(Breakout.Item it) => item[Math.Max(0, Array.IndexOf(Breakout.Kinds, it.Kind))];

    // 공·아이템 그림은 칸의 19.5 에 가운데가 있다: 가운데가 (x, y) 일 때 칸 왼쪽 위 (화면 좌표)
    static (int X, int Y) CellAt(double x, double y, int scale) => ((int)(x - 19.5 * scale), (int)(y - 19.5 * scale));

    static Rectangle CellBox(Stamp st, double x, double y, int scale)
    {
        var (cx, cy) = CellAt(x, y, scale);
        return Rectangle.Inflate(st.Box(cx + 20 * scale, cy + 20 * scale), 1, 1);
    }

    // 돌며 떨어지는 파편: 뒤집히듯 커졌다 작아지는 네모
    static int ChipSize(Breakout.Shard s) => Math.Max(2, (int)(s.Size * (1.1 + .7 * Math.Abs(Math.Cos(s.Angle)))));

    public void Dirty(Overlay o, List<Rectangle> r)
    {
        if (game is not { } bo || !o.Bounds.IntersectsWith(bo.Field)) return; // 다른 모니터 그림 창은 할 일이 없다
        long t0 = Stopwatch.GetTimestamp();
        int k = bo.K;
        Art(o, k);
        if (hud.Fresh) // 판이 막 열렸다: 창 자리 전체와 쟁반·곁 표시를 한 번
        {
            foreach (var board in bo.Boards) r.Add(o.Client(board));
            r.Add(o.Client(plateSpot.Now));
            if (!sideSpot.Now.IsEmpty) r.Add(o.Client(sideSpot.Now));
        }
        for (int i = brokenFrom; i < brokenTo; i++) r.Add(o.Client(bo.BrokenOrder[i].R)); // 새로 깨진 조각은 그 자리만
        if (bo.Stage == Breakout.Phase.Play)
        {
            var art = BallArt(bo);
            int bk = bo.Giant ? 2 * k : k;
            foreach (var x in bo.Balls) r.Add(o.Client(CellBox(art, x.X, x.Y, bk)));
        }
        foreach (var it in bo.Items) r.Add(o.Client(CellBox(ItemArt(it), it.X, it.Y, k)));
        foreach (var s in bo.Shards)
        {
            int half = ChipSize(s) / 2 + 2;
            r.Add(o.Client(new Rectangle((int)s.X - half, (int)s.Y - half, half * 2, half * 2)));
        }
        if (!plateSpot.Dirty.IsEmpty) r.Add(o.Client(plateSpot.Dirty));
        if (!sideSpot.Dirty.IsEmpty) r.Add(o.Client(sideSpot.Dirty));
        if (ended && hud.Changed) r.Add(o.Client(ResultRect(resultAt, k)));
        if (guideMove || hud.Fresh && guideOn)
        {
            if (!guideShown.IsEmpty) r.Add(guideShown); // 옛 자리
            int n = r.Count;
            ses?.DirtyGuide(o, r, bo.Clock, host.Pet.X, host.Pet.Y);
            guideShown = r.Count > n ? r[n] : Rectangle.Empty;
        }
        perf.Dirty(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
    }

    // 네모 묶음: 같은 붓으로 칠할 네모를 모아 두었다가 차례에 맞춰 칠한다 (다시 쓰는 배열이라 메모리를 새로 잡지 않는다).
    // 하나씩 칠한다: Graphics.FillRectangles 로 한꺼번에 넘기면 겹친 네모를 한 도형으로 채우느라 훨씬 느렸다
    sealed class Batch
    {
        Rectangle[] rects = new Rectangle[256];
        int n;

        public void Add(int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0) return;
            if (n == rects.Length) Array.Resize(ref rects, n * 2);
            rects[n++] = new Rectangle(x, y, w, h);
        }

        public void Fill(Graphics g, Brush b)
        {
            for (int i = 0; i < n; i++) g.FillRectangle(b, rects[i]);
            n = 0;
        }
    }

    readonly Batch light = new(), dark = new(), holes = new(), lips = new();

    // 조각 격자 하나: 다시 칠하는 곳에 걸친 줄·칸만 본다
    void DrawGrid(Overlay o, Graphics g, Breakout bo, in Breakout.Grid gr, Rectangle clip, int u)
    {
        var area = o.Client(gr.R);
        var vis = Rectangle.Intersect(area, clip);
        if (vis.IsEmpty) return;
        int cols = gr.Cols, rows = gr.Rows, left = area.Left, top = area.Top, w = area.Width, h = area.Height;
        int EdgeX(int c) => left + w * c / cols; // 조각 경계 (Breakout 이 조각을 나눈 식 그대로)
        int EdgeY(int r) => top + h * r / rows;
        int c0 = (vis.Left - left) * cols / w, c1 = (vis.Right - 1 - left) * cols / w, r0 = (vis.Top - top) * rows / h, r1 = (vis.Bottom - 1 - top) * rows / h;
        if (c0 + 1 < cols && EdgeX(c0 + 1) <= vis.Left) c0++; // 어림한 칸이 하나 앞일 수 있다 (경계를 정수로 내려서)
        if (c1 + 1 < cols && EdgeX(c1 + 1) <= vis.Right - 1) c1++;
        if (r0 + 1 < rows && EdgeY(r0 + 1) <= vis.Top) r0++;
        if (r1 + 1 < rows && EdgeY(r1 + 1) <= vis.Bottom - 1) r1++;
        int xa = EdgeX(c0), xb = EdgeX(c1 + 1), ya = EdgeY(r0), yb = EdgeY(r1 + 1);
        bool anyLeft = false;
        for (int r = r0; r <= r1; r++)
        {
            // 깨진 조각: 까만 구멍과 위쪽 얇은 턱. 한 줄에서 이어진 것끼리 한 번에
            int y = EdgeY(r), y2 = EdgeY(r + 1), at = gr.First + r * cols, run = -1;
            bool whole = true;
            for (int c = c0; c <= c1 + 1; c++)
            {
                if (c <= c1 && bo.Tiles[at + c].Broken) { if (run < 0) run = c; continue; }
                if (c <= c1) whole = false;
                if (run < 0) continue;
                int x = EdgeX(run), x2 = EdgeX(c);
                holes.Add(x, y, x2 - x, y2 - y);
                lips.Add(x, y, x2 - x, u);
                run = -1;
            }
            if (whole) continue; // 다 깨진 줄에는 금을 긋지 않는다
            anyLeft = true;
            // 금: 위·왼쪽은 밝게, 아래·오른쪽은 어둡게. 창 바탕이 밝든 어둡든 보인다
            light.Add(xa, y, xb - xa, u);
            dark.Add(xa, y2 - u, xb - xa, u);
        }
        if (anyLeft) // 줄 금(밝은) → 칸 금(밝은) → 줄 금(어두운) → 칸 금(어두운): 모서리가 조각마다 따로 칠할 때와 같게
        {
            for (int c = c0; c <= c1; c++) light.Add(EdgeX(c), ya, u, yb - ya);
            light.Fill(g, CrackLight);
            for (int c = c0; c <= c1; c++) dark.Add(EdgeX(c + 1) - u, ya, u, yb - ya);
            dark.Fill(g, CrackDark);
        }
        holes.Fill(g, Pal.Hole);
        lips.Fill(g, HoleEdge);
    }

    public void Draw(Overlay o, Graphics g)
    {
        if (game is not { } bo || !o.Bounds.IntersectsWith(bo.Field)) return;
        long t0 = Stopwatch.GetTimestamp();
        int k = bo.K, u = CrackPx(k);
        Art(o, k);
        var clip = Rectangle.Ceiling(g.ClipBounds);
        foreach (var gr in bo.Grids) DrawGrid(o, g, bo, gr, clip, u);

        foreach (var s in bo.Shards) // 돌며 떨어지는 파편 (창 내용은 찍지 않고 밝은 유리색 네모로만)
        {
            int size = ChipSize(s), x = (int)s.X - o.Left - size / 2, y = (int)s.Y - o.Top - size / 2;
            dark.Add(x - 1, y - 1, size + 2, size + 2);
            light.Add(x, y, size, size);
        }
        dark.Fill(g, Pal.ChipEdge);
        light.Fill(g, Pal.Chip);

        foreach (var it in bo.Items) // 떨어지는 아이템
        {
            var (x, y) = CellAt(it.X, it.Y, k);
            ItemArt(it).DrawCell(g, x - o.Left, y - o.Top, 40 * k);
        }

        var p0 = o.ToClient(bo.PlateX - bo.PlateW / 2.0, bo.PlateTop + (Bumped(bo) ? k : 0));
        g.DrawImageUnscaled(Plate(o.Art, k, bo.PlateW), p0.X, p0.Y);

        if (bo.Stage == Breakout.Phase.Play)
        {
            var art = BallArt(bo);
            int bk = bo.Giant ? 2 * k : k; // 왕볼은 두 배
            foreach (var x in bo.Balls)
            {
                var (cx, cy) = CellAt(x.X, x.Y, bk);
                art.DrawCell(g, cx - o.Left, cy - o.Top, 40 * bk);
            }
        }

        for (int i = 0; i < sideN; i++)
            if (sideArt[i] > -2) (sideArt[i] < 0 ? hudBall : hudItem[sideArt[i]]).Put(o, g, sideX[i], sideY);

        if (ended) GameHud.Result(o, g, resultAt, k, bo.Broken, bestBefore, bo.Broken > bestBefore, "game/tile");
        ses?.DrawGuide(o, g, bo.Clock, host.Pet.X, host.Pet.Y);
        perf.Paint(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
    }

    // 디버그: break / break launch / break stop / break auto(쟁반이 공을 따라간다, 기록 안 남김) / break item <종류>
    public bool Command(string[] a)
    {
        if (a.Length == 0 || a[0] != "break" && a[0] != Id) return false;
        string what = a.Length > 1 ? a[1] : "start";
        if (what == "stop") Stop(true);
        else if (what == "launch") game?.Launch();
        else if (what == "auto") auto = true;
        else if (what == "item" && a.Length > 2) game?.Apply(a[2]);
        else if (!Running && !host.Busy) Start();
        return true;
    }

    public IEnumerable<GameStat> Stats()
    {
        yield return new GameStat(L.T("game.breakout"), best > 0 ? L.T("game.breakout_val", best.ToString("N0", L.Culture)) : L.T("game.never"), best > 0);
    }

    sealed record Record(int Best);
    public string? SaveState() => best > 0 ? JsonSerializer.Serialize(new Record(best)) : null;

    public void LoadState(string? json)
    {
        if (json == null) return;
        try { if (JsonSerializer.Deserialize<Record>(json) is { } r) best = r.Best; }
        catch (JsonException) { /* 깨진 기록은 버린다 */ }
    }

    // ── 화면 없이: 시험 판 (1920×1040 모니터, 창 하나 = 330조각), 받아치는 사람 ──
    static readonly Rectangle TestField = new(0, 0, 1920, 1040), TestBoard = new(285, 100, 1350, 660);
    static readonly string[] GuideKeys = { "note.break_mouse", "note.break_click", "note.esc" };

    // 쟁반을 공 밑으로 가져가는 사람: 가장 낮게 내려오는 공을 받고, 공이 높이 있으면 떨어지는 아이템을 받으러 간다
    sealed class Bot
    {
        readonly Random aim;
        double offset;
        public int Items;

        public Bot(Breakout bo, int seed)
        {
            aim = new Random(seed);
            var chain = bo.OnEvent;
            bo.OnEvent = e =>
            {
                chain?.Invoke(e);
                if (e == "plate") offset = (aim.NextDouble() - .5) * bo.PlateW * .8;
                if (e.StartsWith("item:", StringComparison.Ordinal)) Items++;
            };
        }

        public double Want(Breakout bo, double plateTop)
        {
            Breakout.Ball? low = null;
            Breakout.Item? item = null;
            foreach (var x in bo.Balls) if (!x.Stuck && x.Vy > 0 && (low == null || x.Y > low.Y)) low = x;
            foreach (var x in bo.Items) if (item == null || x.Y > item.Y) item = x;
            double want = low != null && (low.Y > plateTop - 400 || item == null) ? low.X - offset : item?.X ?? bo.PlateX;
            return Math.Clamp(want, bo.Field.Left + bo.PlateW / 2.0, bo.Field.Right - bo.PlateW / 2.0);
        }
    }

    // 화면 없는 한 판: 가짜 앱에 판을 펴고, 마우스 자리는 넣어 주는 대로
    static (BreakoutGame Game, Breakout Bo, SceneHost Host) Fake(Breakout bo, Func<Point> mouse, string stage = "child", int best = 0)
    {
        var host = new SceneHost(stage, bo.K);
        var game = new BreakoutGame(host) { best = best };
        game.Open(bo);
        game.ses = Session.Fake(bo.Field, mouse: mouse);
        game.ses.Guide(GuideKeys);
        return (game, bo, host);
    }

    // ── 그려 보기 (--mode-scene breakout[@배율]) ──
    // 윗줄: 금·구멍·파편·아이템 / 아이템 효과(왕볼·넓은 쟁반·곁 표시) / 끝 화면(새 기록 = 왕관). 창 자리는 반은 밝게 반은 어둡게 깔아 금이 어디서나 보이는지 본다
    // 아랫줄: 형태(아기·어린이·어른 넷)마다 쟁반이 올린 앞발에 얹히는지 — 햄스터와 귀·뿔·꼬리가 높은 종, 그리고 새 기록이 아닐 때의 끝 화면
    public static void Scene(string path, int k)
    {
        var field = new Rectangle(0, 0, 220 * k, 178 * k);
        var board = new Rectangle(15 * k, 8 * k, 190 * k, 60 * k);
        string[] species = { Sprites.DefaultSpecies, "corgi", "unicorn" };
        int cellW = 64 * k, cellH = 58 * k, gap = 10, rowY = field.Height + 2 * gap;
        using var bmp = new Bitmap(3 * (field.Width + gap) + gap, rowY + species.Length * cellH + gap);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);

        void Shot(BreakoutGame game, SceneHost host, Rectangle view, int x, int y)
        {
            using var ov = host.Overlay(game);
            using var frame = new Bitmap(ov.Width, ov.Height);
            using var fg = Graphics.FromImage(frame);
            fg.Clear(Color.FromArgb(150, 160, 170));
            using var paper = new SolidBrush(Color.FromArgb(246, 247, 249));
            using var ink = new SolidBrush(Color.FromArgb(38, 42, 52));
            fg.FillRectangle(paper, board.Left, board.Top, board.Width / 2, board.Height); // 조각낸 창: 밝은 반, 어두운 반
            fg.FillRectangle(ink, board.Left + board.Width / 2, board.Top, board.Width - board.Width / 2, board.Height);
            ov.Render(fg);
            var pet = host.Pet; // 본체 창은 그림 창 위에 뜬다
            fg.DrawImageUnscaled(host.Sprites.Get(pet.SpriteName(), 0, k), (int)pet.X - 20 * k, (int)pet.Y - 40 * k);
            g.DrawImage(frame, new Rectangle(x, y, view.Width, view.Height), view, GraphicsUnit.Pixel);
        }

        // 받아치는 사람이 until 까지 한다 (실제 조작 길 그대로: 마우스 → 쟁반)
        void Play(BreakoutGame game, Breakout bo, Bot bot, Action<double> aim, Func<bool> until, double maxSec = 40)
        {
            for (int i = 0; i < maxSec * 60 && game.Running && !until(); i++) { aim(bot.Want(bo, bo.PlateTop)); game.Frame(1 / 60.0); }
        }

        {
            double want = field.Width / 2.0;
            var (game, bo, host) = Fake(new Breakout(field, new[] { board }, k, new Random(4)), () => new Point((int)want, 0));
            var bot = new Bot(bo, 3);
            Play(game, bo, bot, x => want = x, () => bo.Broken >= 8 && bo.Shards.Count > 0 && bo.Items.Count > 0 && bo.Clock > Session.GuideSec);
            host.Pet.Hush();
            Shot(game, host, field, gap, gap);

            foreach (var kind in new[] { "split", "split", "giant", "wide", "slow", "life" }) game.Command(new[] { "break", "item", kind });
            double from = bo.Clock;
            Play(game, bo, bot, x => want = x, () => bo.Clock - from > .6);
            Shot(game, host, field, gap + field.Width + gap, gap);

            bo.Lives = 1; // 끝 화면: 남은 공을 다 놓친다
            foreach (var x in bo.Balls) (x.X, x.Y, x.Vx, x.Vy, x.Stuck) = (field.Left + 30 * k, field.Bottom - 2 * k, 0, bo.Speed, false);
            Play(game, bo, bot, _ => want = field.Width * .6, () => bo.Stage != Breakout.Phase.Play && bo.Clock - bo.EndAt > 1);
            host.Pet.Hush(); // 장면에서는 시간이 흐르지 않아 앞서 뜬 표정 그림이 남는다
            Shot(game, host, field, gap + 2 * (field.Width + gap), gap);
        }

        // 형태마다: 본체 그림 위에 쟁반과 얹힌 공 (자리는 게임이 쓰는 식 그대로)
        var forms = Showcase.Forms;
        using var floor = new SolidBrush(Color.FromArgb(150, 160, 170));
        for (int row = 0; row < species.Length; row++)
        {
            var sp = Sprites.Embedded(species[row]);
            using var tray = PlateArt(sp, k, 36 * k);
            var ballArt = Stamps.Of(sp).Get("game/ball", 0, k);
            for (int i = 0; i < forms.Length; i++)
            {
                int x0 = gap + i * (cellW + gap / 2), y0 = rowY + row * cellH, cx = x0 + cellW / 2, feet = y0 + cellH - 4 * k;
                g.FillRectangle(floor, x0, y0, cellW, cellH - gap / 2);
                double top = PlateTopAt(feet, PawTop(sp, $"{forms[i]}/0/carry"), sp.Cell, k);
                g.DrawImageUnscaled(sp.Get($"{forms[i]}/0/carry", 0, k), cx - 20 * k, feet - 40 * k);
                g.DrawImageUnscaled(tray, cx - 18 * k, (int)top);
                var (bx, by) = CellAt(cx, top - (int)(3.5 * k), k);
                ballArt.DrawCell(g, bx, by, 40 * k);
            }
        }

        // 새 기록이 아닐 때의 끝 화면 (작은 왕관 + 최고 기록), 어른 형태로
        {
            double want = field.Width / 2.0;
            var (game, bo, host) = Fake(new Breakout(field, new[] { board }, k, new Random(9)), () => new Point((int)want, 0), "adult_playful", best: 128);
            var bot = new Bot(bo, 5);
            Play(game, bo, bot, x => want = x, () => bo.Broken >= 14 && bo.Clock > Session.GuideSec);
            bo.Lives = 1;
            foreach (var x in bo.Balls) (x.X, x.Y, x.Vx, x.Vy, x.Stuck) = (field.Left + 30 * k, field.Bottom - 2 * k, 0, bo.Speed, false);
            Play(game, bo, bot, _ => { }, () => bo.Stage != Breakout.Phase.Play && bo.Clock - bo.EndAt > 1.2);
            host.Pet.Hush();
            var view = Rectangle.Intersect(field, new Rectangle((int)host.Pet.X - 45 * k, field.Bottom - 2 * cellH, 90 * k, 2 * cellH));
            Shot(game, host, view, gap + forms.Length * (cellW + gap / 2) + gap, rowY);
        }
        bmp.Save(path);
    }

    // 속도 재기: 같은 씨로 한 판을 받아치는 사람으로 (--mode-bench breakout).
    // 첫 줄은 다른 게임과 같은 잣대(판 = 모니터 전체를 프레임마다 다시 그린다), 셋째 줄은 실제 창처럼 지난 프레임·이번 프레임 자리(64px 칸)만 다시 그릴 때
    public static string BenchRun()
    {
        (BreakoutGame, Breakout, Bot, SceneHost) Make()
        {
            var (game, bo, host) = Fake(new Breakout(TestField, new[] { TestBoard }, 3, new Random(7)), () => Point.Empty);
            return (game, bo, new Bot(bo, 3), host);
        }
        static bool Over(Breakout bo) => bo.Stage != Breakout.Phase.Play && bo.Clock - bo.EndAt > 2; // 끝 화면 2초까지
        string line;
        {
            var (game, bo, bot, host) = Make();
            using var ov = host.Overlay(game);
            double top = game.PlateTop(), want = 0;
            line = Bench.Run("breakout", ov, game, bo.Field, () => { if (Over(bo)) return false; want = bot.Want(bo, top); host.Pet.Hush(); return true; },
                () => { host.Pet.X = want; bo.Update(1 / 60.0, want, top); game.Track(); });
            line += $"\n  {bo.Clock:F0}초에 {bo.Broken}/{bo.Tiles.Count}조각, 아이템 {bot.Items}개";
        }
        {
            var (game, bo, bot, host) = Make();
            using var ov = host.Overlay(game);
            using var desk = Graphics.FromHwnd(IntPtr.Zero);
            using var ctx = new BufferedGraphicsContext();
            using var buf = ctx.Allocate(desk, new Rectangle(0, 0, ov.Width, ov.Height));
            using var clip = new Region();
            List<Rectangle> last = new(), now = new();
            var cells = new HashSet<(int, int)>();
            double top = game.PlateTop();
            long ticks = 0, worst = 0, bytes = 0, area = 0;
            int frames = 0;
            for (int i = 0; !Over(bo); i++)
            {
                double want = bot.Want(bo, top);
                host.Pet.Hush();
                host.Pet.X = want;
                bo.Update(1 / 60.0, want, top);
                game.Track();
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
                buf.Graphics.Clip = clip;
                (last, now) = (now, last);
                if (i < 30) { ov.Render(buf.Graphics); continue; } // 처음 그림 만들기·JIT 는 빼고
                long a0 = GC.GetAllocatedBytesForCurrentThread(), t0 = Stopwatch.GetTimestamp();
                ov.Render(buf.Graphics);
                long t1 = Stopwatch.GetTimestamp() - t0;
                (ticks, worst, bytes, area, frames) = (ticks + t1, Math.Max(worst, t1), bytes + GC.GetAllocatedBytesForCurrentThread() - a0, area + cells.Count, frames + 1);
            }
            int n = Math.Max(1, frames);
            line += $"\n  바뀐 자리만 그리기: {ticks * 1e6 / Stopwatch.Frequency / n:0.0}µs, 가장 느린 프레임 {worst * 1e3 / Stopwatch.Frequency:0.00}ms, 메모리 {bytes / n}B/프레임, 넓이 {area * 64.0 * 64 / n / 1000:0.0}천px²/프레임";
        }
        return line;
    }

    // ── 자가 검사 (화면 없이) ──
    static void Expect(bool ok, string what) { if (!ok) throw new Exception("자가 검사 실패: " + what); }

    public static string Check()
    {
        string rules = RuleCheck();

        // ── 모듈: 조작, 시작 가드, 보스 키, 끝 화면, 기록, 형태마다 쟁반, 다시 칠할 자리 ──
        var field = TestField;
        var board = new Rectangle(300, 120, 1100, 700);
        Breakout Make(int seed, int k = 3) => new(field, new[] { board }, k, new Random(seed));
        var mouse = new Point(960, 0);
        var keys = new HashSet<Keys> { Keys.Space, Keys.A, Keys.D, Keys.Left, Keys.Right, Keys.Up, Keys.ControlKey, Keys.ShiftKey, Keys.Tab };

        // 조작: 마우스 좌우 = 쟁반, 왼쪽 버튼 = 던지기. 키로는 아무 일도 없다
        var (game, bo, host) = Fake(Make(11), () => mouse);
        game.ses = Session.Fake(field, keys.Contains, () => true, () => mouse);
        var pet = host.Pet;
        void Frames(double sec) { for (int i = 0; i < sec * 60 && game.Running; i++) game.Frame(1 / 60.0); }
        Frames(.5);
        Expect(bo.Balls[0].Stuck && Math.Abs(pet.X - 960) < 1, "키(Space·글자·방향키·Ctrl·Shift)로는 던지지도 움직이지도 않는다");
        Expect(pet.Mode == Mode.Puppet && pet.Pose == "carry" && pet.Y == field.Bottom - pet.K.FloorGap, "볼록이는 모니터 맨 아래에서 쟁반을 든다");
        mouse = new Point(400, 0);
        Frames(.5);
        Expect(Math.Abs(pet.X - 400) < 1 && Math.Abs(bo.PlateX - 400) < 1 && bo.Balls[0].Stuck && Math.Abs(bo.Balls[0].X - 400) < 1, $"마우스를 따라 쟁반이 움직인다: {pet.X:0}");
        mouse = new Point(-900, 0);
        Frames(.5);
        Expect(Math.Abs(pet.X - (field.Left + bo.PlateW / 2.0)) < 1, "쟁반은 모니터 밖으로 안 나간다");
        game.ses.Press(MouseButtons.Right, true);
        Frames(.1);
        Expect(bo.Balls[0].Stuck, "오른쪽 버튼으로는 던지지 않는다");
        game.ses.Press(MouseButtons.Left, true);
        Frames(.05);
        Expect(!bo.Balls[0].Stuck && bo.Balls[0].Vy < 0, "왼쪽 버튼으로 던진다");

        // 시작 가드, 디버그 명령, 보스 키
        game.Start();
        Expect(ReferenceEquals(game.game, bo), "하는 중에 또 시작해도 판은 그대로다");
        Expect(game.Command(new[] { "break", "item", "wide" }) && bo.Wide && game.Command(new[] { "breakout", "item", "life" }) && bo.Lives == 4, "디버그 명령: break item <종류>");
        Expect(!game.Command(new[] { "raid" }) && !game.Command(new[] { "breaker" }), "남의 명령은 받지 않는다");
        pet.Hush();
        using (var ov = host.Overlay(game))
        {
            var rects = new List<Rectangle>();
            game.Dirty(ov, rects);
            Expect(rects.Count > 0, "하는 동안에는 다시 칠할 자리가 있다");
            Expect(game.Command(new[] { "break", "stop" }) && !game.Running, "디버그 명령: break stop");
            rects.Clear();
            game.Dirty(ov, rects);
            Expect(rects.Count == 0 && pet.Mode == Mode.Wander && !pet.Emoting, "보스 키로 끝내면 결과 화면·표정 없이 바로 끝나고 그릴 것도 없다");
        }

        // 끝 화면: 다 놓치면 깬 조각 수를 보이고, 0.8초 뒤부터 눌러 닫는다. 기록이 남고 옛 기록(BreakBest)도 이어받는다
        Breakout Lose(BreakoutGame g, Breakout x, int broken)
        {
            game = g;
            x.Launch();
            for (int i = 0; i < broken; i++) // 맨 아랫줄 조각을 아래에서 하나씩 맞힌다
            {
                var t = x.Tiles[x.Tiles.Count - 1 - i];
                (x.Balls[0].X, x.Balls[0].Y, x.Balls[0].Vx, x.Balls[0].Vy) = (t.R.Left + t.R.Width / 2.0, t.R.Bottom + 40, 0, -x.Speed);
                Frames(.2);
            }
            x.Lives = 1;
            foreach (var ball in x.Balls) (ball.X, ball.Y, ball.Vx, ball.Vy) = (1800, field.Bottom - 10, 0, x.Speed);
            Frames(.3);
            return x;
        }
        mouse = new Point(300, 0);
        var legacy = GameModes.WithLegacy(new SaveData { BreakBest = 2 }).GetValueOrDefault("breakout");
        var (end, lost, endHost) = Fake(Make(12), () => mouse);
        end.LoadState(legacy);
        Expect(end.best == 2 && end.Stats().First().Played, "옛 기록(BreakBest)을 이어받는다");
        end.bestBefore = end.best;
        Lose(end, lost, 3);
        Expect(lost.Stage == Breakout.Phase.Lost && lost.Broken == 3 && end.Running && end.ended, $"다 놓치면 끝 화면: {lost.Stage}, {lost.Broken}조각");
        double stoodAt = endHost.Pet.X;
        mouse = new Point(1500, 0);
        end.ses!.Press(MouseButtons.Left, true);
        Frames(.5);
        Expect(end.Running && endHost.Pet.X == stoodAt, "끝 화면은 0.8초 동안은 눌러도 안 닫히고, 볼록이는 그 자리에 선다");
        Frames(.4);
        end.ses.Press(MouseButtons.Left, true);
        Frames(.05);
        Expect(!end.Running && end.best == 3 && end.SaveState()!.Contains('3') && endHost.Pet.Mode != Mode.Puppet && endHost.Pet.Emoting, "0.8초 뒤 누르면 닫히고 기록이 남는다");
        var (wait, lost2, _) = Fake(Make(13), () => mouse);
        wait.Command(new[] { "break", "auto" });
        Lose(wait, lost2, 2);
        Frames(Breakout.EndSec - .5);
        Expect(wait.Running, "끝 화면은 3.5초 동안 떠 있다");
        Frames(.6);
        Expect(!wait.Running && wait.best == 0, "안 눌러도 3.5초 뒤 닫힌다. 디버그 auto 판은 기록에 안 남는다");

        // 형태마다(아기·어린이·어른 넷): 쟁반이 머리 위로 올린 앞발에 얹히고, 던지기 전 공은 그 위에 얹힌다
        foreach (string form in Showcase.Forms)
            foreach (int k in new[] { 2, 3, 6 })
            {
                var (fg, fb, fh) = Fake(Make(1, k), () => mouse, form);
                game = fg;
                Frames(.1);
                double feet = fh.Pet.Y, standing = (2 * PetArt.Embedded.Size(form).Ry + 4) * k;
                Expect(fb.PlateTop + fb.PlateH <= feet - standing * .8 && fb.PlateTop >= feet - standing - 12 * k, $"쟁반은 머리 위 앞발에 얹힌다 ({form} ×{k}): 발에서 {feet - fb.PlateTop:0}px, 선 키 {standing:0}px");
                Expect(fb.Balls[0].Stuck && fb.Balls[0].Y == fb.PlateTop - fb.BallR, $"던지기 전 공은 쟁반 위에 얹힌다 ({form} ×{k})");
                fg.Stop(true);
            }

        // 다시 칠할 자리: 받아치는 사람이 한 판을 끝 화면까지 (실제 조작 길 그대로). 바뀐 자리만 다시 그려도 화면에 지나간 그림이 남지 않는다
        int repaints = 0;
        foreach (var (k, boards, seed) in new[] { (3, new[] { TestBoard }, 7), (5, new[] { new Rectangle(200, 80, 700, 420), new Rectangle(1000, 140, 760, 300) }, 8) })
        {
            double want = 960;
            var (pg, pb, ph) = Fake(new Breakout(field, boards, k, new Random(seed)), () => new Point((int)want, 0));
            var bot = new Bot(pb, 3);
            using var ov = ph.Overlay(pg);
            int frames = 0;
            var (bad, first) = SceneKit.Repaint(ov, pg, field, () =>
            {
                if (!pg.Running) return false;
                want = bot.Want(pb, pb.PlateTop);
                if (frames++ % 400 == 399) pb.Apply(Breakout.Kinds[frames / 400 % Breakout.Kinds.Length]); // 아이템 효과도 골고루
                pg.Frame(1 / 60.0);
                ph.Pet.Hush(); // 머리 위 표정 그림은 게임이 아니라 앱이 다시 칠한다
                return pg.Running;
            }, 60 * 150);
            Expect(bad == 0 && !pg.Running, $"바뀐 자리만 다시 칠해도 화면이 맞다 (×{k}): {bad}번 다름 (처음 {first}프레임), 끝남 {!pg.Running}, {pb.Broken}/{pb.Tiles.Count}조각");
            repaints += frames;
        }
        return $"{rules}, 모듈: 형태 {Showcase.Forms.Length}종 × 배율 3종 쟁반, {repaints}프레임 다시 칠하기";
    }

    static string RuleCheck()
    {
        var field = new Rectangle(0, 0, 1920, 1040);
        var screens = new[] { field, new Rectangle(1920, 0, 1920, 1040) };
        int cell = 120;
        var used = new Win(new IntPtr(5), new Rectangle(300, 120, 1100, 700), false);
        var full = new Win(new IntPtr(6), new Rectangle(0, 0, 1920, 1040), false);
        var top = new Win(new IntPtr(7), new Rectangle(200, 200, 900, 500), true);
        var other = new Win(new IntPtr(8), new Rectangle(2100, 100, 1200, 800), false);

        // 고르기: 쓰던 창모드 창 → 볼록이 모니터의 큰 창 → 다른 모니터. 최대화·맨 위 고정 창은 안 된다
        Expect(Breakout.Pick(used, new[] { full, used }, field, screens, cell)?.Targets[0].Win == used, "메뉴 전에 쓰던 창모드 창에서 한다");
        Expect(Breakout.Pick(full, new[] { full, top, used }, field, screens, cell)?.Targets[0].Win == used, "최대화·맨 위 고정 창은 건너뛴다");
        Expect(Breakout.Pick(null, new[] { other }, field, screens, cell) is { } o2 && o2.Field == screens[1], "다른 모니터의 창이면 그 모니터에서");
        Expect(Breakout.Pick(full, new[] { full, top }, field, screens, cell) == null, "창모드 창이 없으면 못 한다");
        var tall = new Win(new IntPtr(9), new Rectangle(300, 100, 1100, 920), false);
        Expect(Breakout.Pick(tall, new[] { tall }, field, screens, cell)?.Targets[0].Board.Bottom == field.Bottom - cell * 3, "쟁반 위 칸 셋 높이까지만 조각낸다");
        // 작은 창이 여럿이면 함께: 겹치거나 가린 창, 다른 모니터 창은 빼고 넷까지
        var s1 = new Win(new IntPtr(11), new Rectangle(100, 100, 500, 300), false);
        var s2 = new Win(new IntPtr(12), new Rectangle(700, 80, 400, 300), false);
        var s3 = new Win(new IntPtr(13), new Rectangle(800, 200, 400, 300), false);   // s2 밑에 깔려 가린다
        var s4 = new Win(new IntPtr(14), new Rectangle(1300, 150, 450, 350), false);
        var s5 = new Win(new IntPtr(15), new Rectangle(2000, 100, 500, 300), false);  // 다른 모니터
        var many = Breakout.Pick(s1, new[] { s1, s2, s3, s4, s5 }, field, screens, cell)!.Value;
        Expect(many.Targets.Select(t => t.Win).SequenceEqual(new[] { s1, s2, s4 }), "작은 창 여럿을 함께 쓴다 (가린 창·다른 모니터 창은 빼고): "
            + string.Join(",", many.Targets.Select(t => t.Win.H)));
        Expect(Breakout.Pick(used, new[] { used, s4 }, field, screens, cell)!.Value.Targets.Count == 1, "큰 창 하나면 그것만");

        // 조각: 창을 빈틈없이 덮고, 잘게 나뉜다
        var board = used.R;
        Breakout Make(int seed = 1) => new(field, new[] { board }, 3, new Random(seed));
        var bo = Make();
        Expect(bo.Tiles.Count >= 100 && bo.Tiles.Sum(t => t.R.Width * (long)t.R.Height) == board.Width * (long)board.Height
            && bo.Tiles.All(t => board.Contains(t.R)), $"조각이 창을 빈틈없이 덮는다: {bo.Tiles.Count}개");
        var two = new Breakout(field, many.Targets.Select(t => t.Board).ToList(), 3, new Random(1));
        Expect(many.Targets.All(t => two.Tiles.Any(x => t.Board.Contains(x.R))), "여러 창이면 창마다 조각낸다");

        double plateTop = field.Bottom - 100; // 시험 창(아래 820)과 쟁반 사이를 넉넉히
        void Tick(Breakout x, double sec, Func<Breakout, double>? plateX = null)
        {
            for (double t = 0; t < sec; t += 1 / 60.0) x.Update(1 / 60.0, plateX?.Invoke(x) ?? 960, plateTop);
        }
        Breakout.Ball Fly(Breakout x, double bx, double by, double vx, double vy)
        {
            x.Launch();
            var ball = x.Balls[0];
            (ball.X, ball.Y, ball.Vx, ball.Vy) = (bx, by, vx, vy);
            return ball;
        }

        // 쟁반 위에 얹혀 있다가 클릭하면 위로, 안 누르면 3초 뒤 저절로
        Tick(bo, .5, _ => 700);
        Expect(bo.Balls[0].Stuck && bo.Balls[0].X == 700 && bo.Balls[0].Y < plateTop, "던지기 전엔 쟁반 위에 얹혀 따라다닌다");
        bo.Launch();
        Expect(!bo.Balls[0].Stuck && bo.Balls[0].Vy < 0, "클릭하면 위로 던진다");
        var auto = Make();
        Tick(auto, Breakout.AutoLaunch + .2);
        Expect(!auto.Balls[0].Stuck, "안 누르면 3초 뒤 저절로 던진다");

        // 쟁반: 가운데는 똑바로 위, 오른쪽 끝은 오른쪽 위로 눕게
        var mid = Make();
        var m = Fly(mid, 960, plateTop - 60, 0, mid.Speed);
        Tick(mid, .12);
        Expect(m.Vy < 0 && Math.Abs(m.Vx) < mid.Speed * .05, "쟁반 가운데에 맞으면 똑바로 위로");
        var edge = Make();
        var e1 = Fly(edge, 960 + edge.PlateW / 2.0 - 4, plateTop - 60, 0, edge.Speed);
        Tick(edge, .12);
        Expect(e1.Vy < 0 && e1.Vx > edge.Speed * .7, "쟁반 끝에 맞으면 비스듬히");

        // 모니터 테두리에서 튕긴다
        var wall = Make();
        var w1 = Fly(wall, 60, 900, -wall.Speed * .8, -wall.Speed * .6);
        Tick(wall, .3);
        Expect(w1.Vx > 0 && w1.X >= field.Left + wall.BallR, "모니터 왼쪽 끝에서 튕긴다");

        // 조각: 아래에서 맞으면 깨지고 아래로 튕긴다. 아주 빨라도 건너뛰지 않는다
        var hit = Make();
        var target = hit.Tiles.Where(t => t.R.Left < 800 && t.R.Right > 800).MaxBy(t => t.R.Bottom)!;
        var h1 = Fly(hit, 800, target.R.Bottom + 80, 0, -hit.Speed);
        Tick(hit, .3);
        Expect(target.Broken && hit.Broken == 1 && h1.Vy > 0 && hit.Shards.Count > 0, "맞은 조각이 깨지고 아래로 튕긴다");
        var fast = Make();
        fast.Speed = 20000;
        Fly(fast, 800, target.R.Bottom + 80, 0, -20000);
        fast.Update(1 / 30.0, 960, plateTop);
        Expect(fast.Broken == 1, "아주 빨라도 조각을 건너뛰지 않는다");

        // 놓치면 하나를 잃고 다시 쟁반 위로, 셋 다 잃으면 끝. 다 깨면 이긴다
        var miss = Make();
        for (int i = 0; i < 3; i++)
        {
            Fly(miss, 200, field.Bottom - 20, 0, miss.Speed);
            Tick(miss, .3, _ => 1600);
        }
        Expect(miss.Lives == 0 && miss.Stage == Breakout.Phase.Lost, "셋 다 놓치면 끝");
        Tick(miss, 2.1);
        Expect(!miss.Done, "끝난 뒤 결과를 보이는 동안(3.5초)은 정리하지 않는다");
        Tick(miss, Breakout.EndSec - 2);
        Expect(miss.Done, "끝나고 3.5초 뒤 정리한다");
        var win = Make();
        foreach (var t in win.Tiles.Skip(1)) t.Broken = true;
        var last = win.Tiles[0];
        Fly(win, last.R.Left + last.R.Width / 2.0, last.R.Bottom + 60, 0, -win.Speed);
        Tick(win, .3);
        Expect(win.Stage == Breakout.Phase.Won, "다 깨면 이긴다");

        // 아이템: 떨어지는 걸 쟁반으로 받으면 효과. 놓치면 사라진다
        var drop = Make();
        drop.Items.Add(new Breakout.Item { Kind = "wide", X = 960, Y = plateTop - 150 });
        drop.Items.Add(new Breakout.Item { Kind = "life", X = 300, Y = plateTop - 150 });
        Tick(drop, 1.5);
        Expect(drop.Wide && drop.PlateW > drop.Balls.Count * 0 + 36 * 3 && drop.Lives == 3 && drop.Items.Count == 0, "받은 아이템만 효과, 놓친 건 사라진다");
        Tick(drop, Breakout.WideSec);
        Expect(!drop.Wide && drop.PlateW == 36 * 3, "쟁반 늘이기는 시간이 지나면 돌아온다");
        // 공 나누기: 날아가는 공마다 둘로, 여덟까지
        var split = Make();
        Fly(split, 960, 500, 100, -split.Speed);
        split.Apply("split");
        Expect(split.Balls.Count == 2 && split.Balls.All(x => x.Vy < 0) && split.Balls[0].Vx != split.Balls[1].Vx, "공 나누기: 둘로 갈라진다");
        for (int i = 0; i < 4; i++) split.Apply("split");
        Expect(split.Balls.Count == Breakout.MaxBalls, "공은 여덟까지");
        // 공이 여럿이면 모두 빠져야 하나를 잃는다
        var multi = Make();
        Fly(multi, 960, 500, 0, -multi.Speed);
        multi.Apply("split");
        (multi.Balls[0].X, multi.Balls[0].Y, multi.Balls[0].Vx, multi.Balls[0].Vy) = (200, field.Bottom - 20, 0, multi.Speed);
        Tick(multi, .3, _ => 1600);
        Expect(multi.Lives == 3 && multi.Balls.Count == 1, "공이 남아 있으면 안 잃는다");
        // 느리게, 끈끈이, 불공, 하나 더
        var slow = Make();
        var sb = Fly(slow, 100, 900, 0, -slow.Speed); // 조각을 깨면 조금 빨라지니 창 밖에서
        slow.Apply("slow");
        Tick(slow, 1 / 60.0);
        Expect(Math.Abs(Math.Sqrt(sb.Vx * sb.Vx + sb.Vy * sb.Vy) - slow.Speed * .65) < 1, "느리게: 공이 느려진다");
        Expect(!Breakout.Kinds.Contains("sticky"), "공을 붙잡는 아이템은 없다");
        // 왕볼: 두 배로 커져서 튕기지 않고, 지나가는 자리에 닿는 조각을 몽땅 부순다
        var giant = Make();
        double gx = giant.Tiles.Select(t => t.R.Left).Where(x => x > 800).Min() - giant.BallR * 1.5; // 두 칸 경계 가까이: 보통 공은 한 칸, 왕볼은 두 칸에 닿는다
        int rowsHere = giant.Tiles.Count(t => t.R.Left <= gx && t.R.Right > gx);
        var gb = Fly(giant, gx, board.Bottom + 60, 0, -giant.Speed);
        giant.Apply("giant");
        Tick(giant, 1.15);
        Expect(giant.Radius == giant.BallR * 2 && gb.Vy < 0 && giant.Broken >= rowsHere * 2 - 2,
            $"왕볼: 튕기지 않고 두 줄을 쭉 부순다 ({giant.Broken}/{rowsHere * 2})");
        var fire = Make();
        var col = fire.Tiles.Where(t => t.R.Left < 800 && t.R.Right > 800).ToList();
        var fb = Fly(fire, 800, board.Bottom + 60, 0, -fire.Speed);
        fire.Apply("fire");
        Tick(fire, 1.2);
        Expect(fire.Broken >= col.Count && col.All(t => t.Broken), $"불공: 튕기지 않고 한 줄을 뚫는다 ({fire.Broken}/{col.Count})");
        Expect(fire.Shards.Count <= Breakout.MaxShards && fire.BrokenOrder.Count == fire.Broken, "몰아 깨도 파편은 40개까지, 깨진 차례를 남긴다");
        var life = Make();
        for (int i = 0; i < 4; i++) life.Apply("life");
        Expect(life.Lives == Breakout.MaxLives, "하나 더: 다섯까지");

        // 쟁반을 공 밑으로 가져가는 사람: 공이 모니터 밖으로 안 나가고, 조각이 계속 깨지고, 아이템도 받는다
        var play = Make(7);
        var aim = new Random(3);
        double offset = 0;
        int items = 0;
        play.OnEvent = e => { if (e == "plate") offset = (aim.NextDouble() - .5) * play.PlateW * .8; if (e.StartsWith("item:")) items++; };
        bool inside = true;
        for (double t = 0; t < 180 && !play.Done; t += 1 / 60.0)
        {
            // 가장 낮게 내려오는 공을 받고, 공이 높이 있으면 떨어지는 아이템을 받으러 간다
            var low = play.Balls.Where(x => !x.Stuck && x.Vy > 0).MaxBy(x => x.Y);
            var item = play.Items.MaxBy(x => x.Y);
            double want = low != null && (low.Y > plateTop - 400 || item == null) ? low.X - offset : item?.X ?? play.PlateX;
            play.Update(1 / 60.0, Math.Clamp(want, field.Left + play.PlateW / 2.0, field.Right - play.PlateW / 2.0), plateTop);
            inside &= play.Balls.All(x => x.X >= field.Left && x.X <= field.Right && x.Y >= field.Top);
        }
        Expect(inside, "공은 모니터 밖으로 안 나간다");
        Expect(play.Broken >= play.Tiles.Count / 2 && items > 0, $"쟁반으로 받아치면 조각이 깨진다: {play.Broken}/{play.Tiles.Count}, 아이템 {items}");
        return $"벽돌깨기 받아치는 사람: {play.Clock:F0}초에 {play.Broken}/{play.Tiles.Count}조각, 아이템 {items}개, 공 {play.Lives}개 남음";
    }
}
