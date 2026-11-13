using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Bolok;

// 미니건 모듈: 앱에 붙이기(판·조작·볼록이·쏠 창 고르기), 그리기, 자가 검사, 디버그 명령. 규칙은 Minigun.cs
// 판은 쏠 창이 있는 모니터 전체. 볼록이 본체는 그대로 두고(꼭두각시) 총·구멍·파편만 그림 창에 그린다. 점수·기록은 없다
// 조작: 마우스 조준(볼록이가 마우스 X 로 걷는다), 왼쪽 꾹 쏘기, 오른쪽 미사일, Shift+오른쪽 수류탄. Esc 끝 (보스 키)
// 끝: 창을 다 깨면(Minigun.Intact == 0) 저절로 끝난다 (YELL 2026-11-13). 파편이 가라앉는 CheerSec 동안 볼록이가 기뻐하고(별) 판 가운데에 깬 창 수가 뜬 뒤 보통 끝내기(Stop(false))로.
// 그동안에는 더 쏘지 않는다. 보스 키는 그때도 바로, 표정 없이 끝낸다. 쏘던 창이 다 닫히거나 움직여도 끝난다
// 표시·효과·소리·조작은 공통 도구(app/Games/Kit)를 쓴다. 하는 동안 메모리를 새로 잡지 않고, 바뀐 자리만 다시 칠한다
sealed class MinigunGame : IGameMode
{
    readonly IGameHost host;
    Minigun? gun;
    Session? ses;
    readonly List<(IntPtr H, Rectangle R)> windows = new();   // 쏘는 창들 (닫히거나 움직이면 대상에서 뺀다)
    bool forceFire, real, grenade, mock;
    Point? forceAim;
    double checkAt;
    public const double CheerSec = 1.2;   // 다 깨고 기뻐하는 시간 (파편은 길어야 1.2초 날아다닌다)
    double clearAt = -1;                  // 다 깬 때 (-1: 아직)
    Point clearAim;                       // 그때 겨누던 자리 (기뻐하는 동안 총은 그대로 든다)
    int cleared;                          // 깬 창 수

    static readonly Dictionary<string, Sound> Sounds = new()
    {
        ["throw"] = Sound.Tap, ["launch"] = Sound.ShotGun, ["boom"] = Sound.Boom, ["chunk"] = Sound.Hit, ["dual"] = Sound.Pick, ["break"] = Sound.Hit,
    };

    public MinigunGame(IGameHost host) => this.host = host;
    public string Id => "minigun";
    public string MenuKey => "menu.minigun";
    public bool Running => gun != null;

    static readonly string[] GuideKeys = { "note.gun_aim", "note.gun_hold", "note.gun_right", "note.gun_shift", "note.esc" };

    static (double Hold, double Hand) GripOf(string stage, int k)
    {
        var (rx, ry) = PetArt.Embedded.Size(stage); // 지금 몸 크기대로 쥔다 (아기는 낮게, 어른은 높게)
        return Minigun.Grip(rx, ry, k);
    }

    public void Start()
    {
        if (Running) return;
        var b = host.Pet;
        var wins = Native.ListWindows();
        var (field, rects) = WindowPick.Islands(Native.Foreground(), wins, b.Area, Screen.AllScreens.Select(s => s.WorkingArea).ToList(), b.CellPx, 8, topRoom: 0);
        if (rects.Count == 0) // 쏠 창(최대화 안 한 창)이 없다: 말 대신 「X 표 창」 그림과 도리도리 (펫은 말하지 않는다)
        {
            b.Emote("nowin", 3);
            b.ShakeUntil = b.Now + .5;
            Dbg.Log("gun-none");
            return;
        }
        windows.Clear();
        foreach (var w in wins) if (rects.Contains(Rectangle.Intersect(w.R, field))) windows.Add((w.H, w.R));
        var mg = gun = new Minigun(field, rects, field.Bottom - b.K.FloorGap, b.Scale, new Random(), GripOf(b.P.Stage, b.Scale));
        mg.OnEvent = OnEvent;
        (forceFire, forceAim, checkAt, real, grenade, mock) = (false, null, 0, true, false, false);
        Reset();
        ses = Session.Open(host, field, () => Stop(true));
        if (ses.Layer != null) ses.Layer.Cursor = Cursors.Cross;
        ses.Guide(GuideKeys);
        b.StartPuppet();
        Dbg.Log("mode-start", ("id", Id), ("field", field.ToString()), ("targets", string.Join(" ", rects.Select(r => $"{r.X},{r.Y},{r.Width}x{r.Height}"))));
    }

    void OnEvent(string e)
    {
        if (gun is not { } mg) return;
        if (e == "shot") { if (mg.Shots % 4 == 0) Snd.Play(Sound.ShotMg); return; } // 매 발 소리를 내면 소리가 서로 끊긴다
        if (e == "hit") return;
        Snd.Play(Sounds, e);
        if (e == "dual") host.Pet.Emote("star", 1.2); // 쌍 미니건
        if (e is "boom" or "dual" or "break") Dbg.Log("gun-" + e, ("shots", mg.Shots), ("broken", mg.Breaks));
    }

    public void Frame(double dt)
    {
        var (mg, s, b) = (gun!, ses!, host.Pet);
        s.Tick();
        bool cheer = clearAt >= 0; // 다 깨고 기뻐하는 중: 더 쏘지 않고 파편만 가라앉는다
        var m = cheer ? clearAim : forceAim ?? s.Mouse;
        grenade = s.Shift;
        if (s.RightHit && !cheer) { if (grenade) mg.Throw(m.X, m.Y); else mg.Launch(m.X, m.Y); }
        bool fire = !cheer && (forceFire || s.Left && (!real || (Control.MouseButtons & MouseButtons.Left) != 0)); // 떼는 걸 놓쳐도 계속 쏘지 않게
        mg.Update(dt, m.X, m.Y, fire);
        (b.X, b.Y, b.Dir) = (mg.X, mg.Ground, Math.Cos(mg.Aim) >= 0 ? 1 : -1);
        b.Pose = Pose(mg);
        if (mg.Clock > checkAt) // 창이 닫히거나 움직이면 그 창만 대상에서 뺀다
        {
            checkAt = mg.Clock + .5;
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                var (h, r) = windows[i];
                var st = Native.Probe(h);
                if (st.Alive && !st.Minimized && st.R == r) continue;
                windows.RemoveAt(i);
                mg.RemoveTarget(Rectangle.Intersect(r, mg.Field));
                Dbg.Log("gun-window-gone");
            }
        }
        if (!cheer && mg.Targets.Count > 0 && mg.Intact == 0) // 창을 다 깼다: 잠깐 기뻐하고 끝낸다 (말은 하지 않는다)
        {
            (clearAt, clearAim, cleared) = (mg.Clock, m, mg.Targets.Count);
            b.Emote("star", CheerSec + .2);
            Snd.Play(Sound.Pick);
            Dbg.Log("gun-clear", ("windows", cleared), ("shots", mg.Shots), ("sec", mg.Clock));
        }
        Track();
        if (mg.Targets.Count == 0 || clearAt >= 0 && mg.Clock - clearAt >= CheerSec) Stop(false); // 쏠 창이 다 없어졌거나, 다 깨고 기뻐할 만큼 기뻐했다
    }

    string Pose(Minigun mg) =>
        clearAt >= 0 ? (mg.Clock - mg.WalkAt < .1 ? "walk" : "happy") : mg.Clock - mg.BigAt < .3 ? "hop" : mg.Firing ? "angry" : mg.Clock - mg.WalkAt < .1 ? "walk" : "idle";

    // quit(보스 키·디버그 stop): 표정 없이 바로 끝낸다. 그림은 같은 프레임에 다 사라진다 (Session.Close 가 그림 창을 통째로 다시 칠한다)
    public void Stop(bool quit)
    {
        if (gun is not { } mg) return;
        gun = null;
        if (quit && clearAt >= 0) host.Pet.Hush(); // 기뻐하던 중에 보스 키: 머리 위 별도 같이 지운다
        ses?.Close(host.Pet, quit, true, "minigun", ("shots", mg.Shots), ("gone", mg.GoneCells), ("booms", mg.Booms), ("broken", mg.Broken), ("cleared", clearAt >= 0), ("esc", quit)); // 이기고 지는 건 없다. 실컷 쏘면 즐겁다
        ses = null;
        clearAt = -1;
        windows.Clear();
        frame.Clear();
        (forceFire, forceAim) = (false, null);
        Dbg.Log("mode-end", ("id", Id));
    }

    // ── 그리기: 파인 칸·금, 연기, 불덩이, 미사일·수류탄, 파편·탄피·불꽃, 예광탄, 총, 머리 위 큰 무기 그림 + 쿨다운 막대 ──
    // 자주 찍는 그림은 한 번만 찾아 둔다 (개발 중 그림이 바뀌면 다시)
    const int PivotX = 10, PivotY = 21;   // 총 그림의 손잡이 (칸 안 자리, GunArt 와 같다)
    Sprites? artOf;
    int artK;
    Stamp gunArt = null!, rocket = null!, rocketIcon = null!, nadeIcon = null!, nade = null!;
    readonly Stamp[] muzzle = new Stamp[2];
    Rectangle gunBox;                               // 손잡이에서 본 총 그림 자리 (오른쪽을 겨눌 때)
    readonly Rectangle[] muzzleBox = new Rectangle[2];
    int rocketReach;
    Pen? tracer;
    static readonly SolidBrush Brass = new(Color.FromArgb(214, 170, 60));
    static readonly SolidBrush ChipLight = new(Color.FromArgb(238, 240, 246)), ChipDark = new(Color.FromArgb(150, 154, 166));
    static readonly SolidBrush Glass = new(Color.FromArgb(232, 236, 242)), GlassBar = new(Color.FromArgb(96, 124, 168)); // 장면 그림용 가짜 창
    static readonly Bitmap?[] chips = new Bitmap?[160];

    void Art(Sprites sp, int k)
    {
        if (ReferenceEquals(artOf, sp) && artK == k) return;
        var st = Stamps.Of(sp);
        int gk = Math.Max(2, (int)Math.Round(k * 5 / 3.0)), half = sp.Cell * gk / 2;
        (artOf, artK) = (sp, k);
        gunArt = st.Get("game/gun_minigun", 0, gk);
        (rocket, rocketIcon, nade, nadeIcon) = (st.Get("game/rocket", 0, gk), st.Get("game/rocket", 0, k), st.Get("game/grenade", 0, k), st.Get("game/grenade", 0, k));
        gunBox = new Rectangle(half + gunArt.Dx - PivotX * gk, half + gunArt.Dy - PivotY * gk, gunArt.Bmp.Width, gunArt.Bmp.Height);
        var (tx, ty) = Tip(sp, "game/gun_minigun");
        for (int i = 0; i < 2; i++) // 불꽃 그림은 칸 왼쪽 가운데(0, 20)가 총구 끝에 붙는다
        {
            muzzle[i] = st.Get("game/muzzle", i, gk);
            muzzleBox[i] = new Rectangle((tx + 1 - PivotX) * gk + half + muzzle[i].Dx, (ty - 20 - PivotY) * gk + half + muzzle[i].Dy, muzzle[i].Bmp.Width, muzzle[i].Bmp.Height);
        }
        rocketReach = (int)Math.Ceiling(Math.Sqrt(rocket.Bmp.Width * rocket.Bmp.Width + rocket.Bmp.Height * rocket.Bmp.Height) / 2) + 6 * gk;
        tracer?.Dispose();
        tracer = new Pen(Pal.Shot.Color, Math.Max(2, k));
    }

    // 총 그림에서 가장 오른쪽 열이 총구 끝 (칸 안 자리)
    static (int X, int Y) Tip(Sprites sp, string name)
    {
        var bmp = sp.Get(name, 0, 1);
        for (int x = bmp.Width - 1; x >= 0; x--)
        {
            int n = 0, sum = 0;
            for (int y = 0; y < bmp.Height; y++) if (bmp.GetPixel(x, y).A > 0) { n++; sum += y; }
            if (n > 0) return (x, (int)Math.Round(sum / (double)n));
        }
        return (PivotX, PivotY);
    }

    // 파편 한 조각: 어두운 테두리, 위는 밝고 아래는 어두운 네모 (크기마다 한 번만 그려 둔다)
    static Bitmap Chip(int size)
    {
        size = Math.Clamp(size, 2, chips.Length - 1);
        if (chips[size] is { } hit) return hit;
        var bmp = new Bitmap(size + 2, size + 2, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        int u = Math.Max(1, size / 6);
        g.FillRectangle(Pal.ChipEdge, 0, 0, size + 2, size + 2);
        g.FillRectangle(Pal.Chip, 1, 1, size, size);
        g.FillRectangle(ChipLight, 1, 1, size, u);
        g.FillRectangle(ChipDark, 1, 1 + size - u, size, u);
        return chips[size] = bmp;
    }

    static int Flash(Minigun mg) => mg.Firing && mg.Spin >= 1 && mg.Clock - mg.LastShot < .05 ? (int)(mg.Clock * 40) % 2 : -1;
    // 불덩이(BlastArt.Burst): 도트 한 칸은 2 × 배율 px, 구멍보다 조금 크게. 그림은 도트 하나를 1px 로 작게 받아 두고 그릴 때 늘린다
    // (반지름 130 × 배율/3 짜리를 제 크기로 받아 두면 한 장이 0.7~2.8MB 라 모양 × 장 수만큼 수십 MB 가 쌓인다)
    static int Dot(int k) => 2 * k;
    static int FireDots(double r, int k) => (int)Math.Round(r * 1.15 / Dot(k));
    static Rectangle FireRect(double x, double y, double r, int k)
    {
        var box = BlastArt.PopRect(0, 0, FireDots(r, k), 1);
        return new Rectangle((int)x + box.X * Dot(k), (int)y + box.Y * Dot(k), box.Width * Dot(k), box.Height * Dot(k));
    }

    // 손잡이가 pv(화면 좌표)일 때 총(과 불꽃)이 그려지는 자리
    Rectangle GunRect(PointF pv, double aim, double kick, int flash)
    {
        double cos = Math.Cos(aim), sin = Math.Sin(aim), ox = pv.X - cos * kick, oy = pv.Y - sin * kick;
        var box = flash >= 0 ? Rectangle.Union(gunBox, muzzleBox[flash]) : gunBox;
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        for (int c = 0; c < 4; c++)
        {
            double lx = (c & 1) == 0 ? box.Left : box.Right, ly = (c & 2) == 0 ? box.Top : box.Bottom;
            if (cos < 0) ly = -ly; // 왼쪽을 겨누면 뒤집어 그린다
            double x = ox + lx * cos - ly * sin, y = oy + lx * sin + ly * cos;
            (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
        }
        return Rectangle.FromLTRB((int)Math.Floor(x0) - 3, (int)Math.Floor(y0) - 3, (int)Math.Ceiling(x1) + 3, (int)Math.Ceiling(y1) + 3);
    }

    // 머리 위 표시: 크림색 판에 고른 큰 무기 그림, 그 아래 쿨다운 막대 (총에 겹쳐도 보이게 판을 깐다)
    static Rectangle HudRect(int x, int headY, int k) => new(x - 9 * k, headY - 20 * k, 18 * k, 20 * k);
    static Rectangle HudPlate(int x, int headY, int k) => new(x - 8 * k, headY - 19 * k, 16 * k, 11 * k);
    static Rectangle HudBar(int x, int headY, int k) => new(x - 7 * k, headY - 6 * k, 14 * k, 2 * k);

    // ── 다시 칠할 자리: 프레임마다 한 번 모아 둔다 (화면 좌표). 날아다니는 것은 지금 자리(지난 자리는 그림 창이 기억한다),
    // 가만히 있는 것(총·머리 위 표시·파인 칸)은 바뀐 프레임에만 옛 자리와 새 자리 ──
    readonly List<Rectangle> frame = new(1024);
    readonly Rectangle[] gunShown = new Rectangle[2];
    readonly (float X, float Y, double Aim, double Kick, int Flash)[] gunKey = new (float, float, double, double, int)[2];
    Rectangle hudShown;
    int hudKey, hudX, hudY;
    bool tracked;   // 첫 프레임에 자리를 모으기 전에는 그리지 않는다 (그 전에 그리면 다시 칠할 자리에 안 든 그림이 남는다)
    string? headStage;
    double headUp;

    void Reset()
    {
        frame.Clear();
        (gunShown[0], gunShown[1], hudShown, hudKey, headStage, tracked, clearAt) = (default, default, default, 0, null, false, -1);
    }

    void Track()
    {
        var mg = gun!;
        int k = mg.K;
        Art(host.Sprites, k);
        frame.Clear();
        tracked = true;
        foreach (var r in mg.Carved) frame.Add(Rectangle.Inflate(r, 1, 1));
        int t = Math.Max(2, k) + 1;
        for (int i = 0; i < mg.Bullets.Count; i++)
        {
            ref var b = ref mg.Bullets[i];
            double tx = b.X - b.Vx * .012, ty = b.Y - b.Vy * .012;
            frame.Add(Rectangle.FromLTRB((int)Math.Min(b.X, tx) - t, (int)Math.Min(b.Y, ty) - t, (int)Math.Max(b.X, tx) + t + 1, (int)Math.Max(b.Y, ty) + t + 1));
        }
        for (int i = 0; i < mg.Shells.Count; i++)
        {
            ref var s = ref mg.Shells[i];
            frame.Add(s.Missile ? Overlay.Around(s.X, s.Y, rocketReach, rocketReach) : Rectangle.Inflate(nade.Box((int)s.X, (int)s.Y), 1, 1));
        }
        for (int i = 0; i < mg.Puffs.Count; i++) { ref var p = ref mg.Puffs[i]; frame.Add(Overlay.Around(p.X, p.Y, p.Size / 2 + 2, p.Size / 2 + 2)); }
        for (int i = 0; i < mg.Bits.Count; i++) { ref var p = ref mg.Bits[i]; frame.Add(Overlay.Around(p.X, p.Y, p.Size / 2 + 3, p.Size / 2 + 3)); }
        for (int i = 0; i < mg.Blasts.Count; i++) { ref var bl = ref mg.Blasts[i]; frame.Add(FireRect(bl.X, bl.Y, bl.R, k)); }

        int flash = Flash(mg), guns = mg.GunCount;
        for (int i = 0; i < 2; i++)
        {
            if (i >= guns) { if (!gunShown[i].IsEmpty) { frame.Add(gunShown[i]); gunShown[i] = default; } continue; }
            var (pv, aim) = mg.Gun(i);
            var key = (pv.X, pv.Y, aim, mg.Kick, flash);
            if (!gunShown[i].IsEmpty && key == gunKey[i]) continue;
            if (!gunShown[i].IsEmpty) frame.Add(gunShown[i]);
            (gunKey[i], gunShown[i]) = (key, GunRect(pv, aim, mg.Kick * 2 * k, flash));
            frame.Add(gunShown[i]);
        }

        // 머리 위: 고른 큰 무기(Shift 를 누르고 있으면 수류탄, 아니면 미사일)와 쿨다운 막대. 머리 위 표정 그림이 뜨는 동안은 숨긴다
        var pet = host.Pet;
        if (!ReferenceEquals(headStage, pet.P.Stage)) (headStage, headUp) = (pet.P.Stage, pet.Y - pet.HeadY); // 머리 높이는 모습이 바뀔 때만 다시 잰다 (잴 때마다 메모리를 잡는다)
        (hudX, hudY) = ((int)mg.X, (int)(mg.Ground - headUp));
        int hkey = pet.Emoting || clearAt >= 0 ? -1 : HashCode.Combine(hudX, hudY, grenade, (int)(mg.Ready(grenade) * 14 * k));
        if (hkey != hudKey || hudShown.IsEmpty)
        {
            if (!hudShown.IsEmpty) frame.Add(hudShown);
            (hudKey, hudShown) = (hkey, HudRect(hudX, hudY, k));
            frame.Add(hudShown);
        }
        if (clearAt >= 0) frame.Add(GameHud.CenterRect(mg.Field, k)); // 깬 창 수 (파편이 그 위를 지나가니 기뻐하는 동안에는 프레임마다)
    }

    public void Dirty(Overlay o, List<Rectangle> rs)
    {
        if (gun is not { } mg || !tracked) return;
        foreach (var r in frame) rs.Add(o.Client(r));
        ses?.DirtyGuide(o, rs, mg.Clock, mg.X, mg.Ground);
    }

    public void Draw(Overlay o, Graphics g)
    {
        if (gun is not { } mg || !tracked) return;
        int k = mg.K;
        Art(host.Sprites, k);
        if (mock) DrawMock(o, g, mg);
        DrawHoles(o, g, mg);
        for (int i = 0; i < mg.Puffs.Count; i++)
        {
            ref var p = ref mg.Puffs[i];
            var at = o.ToClient(p.X, p.Y);
            Fx.Puff(g, at.X, at.Y, p.Size, p.Life);
        }
        var (im, pm) = (g.InterpolationMode, g.PixelOffsetMode); // 늘리거나 돌려 그리는 그림(불덩이·미사일·총)도 도트가 뭉개지지 않게
        (g.InterpolationMode, g.PixelOffsetMode) = (InterpolationMode.NearestNeighbor, PixelOffsetMode.Half);
        for (int i = 0; i < mg.Blasts.Count; i++)
        {
            ref var bl = ref mg.Blasts[i];
            var at = o.Client(FireRect(bl.X, bl.Y, bl.R, k));
            g.DrawImage(BlastArt.Burst(BlastArt.Variant(bl.X, bl.Y), BlastArt.Frame(mg.Clock - bl.At), FireDots(bl.R, k), 1), at.X, at.Y, at.Width, at.Height);
        }
        for (int i = 0; i < mg.Shells.Count; i++)
        {
            ref var s = ref mg.Shells[i];
            if (!s.Missile) { nade.Put(o, g, (int)s.X, (int)s.Y); continue; }
            var at = o.ToClient(s.X, s.Y);
            int u = mg.GunK, tail = rocket.Dx - ((int)(mg.Clock * 30) % 2 == 0 ? 4 : 3) * u;
            g.TranslateTransform(at.X, at.Y); // 가는 쪽으로 돌려서, 꽁무니에 불꽃
            g.RotateTransform((float)(Math.Atan2(s.Vy, s.Vx) * 180 / Math.PI));
            g.FillRectangle(Pal.Flame[2], tail, -u, rocket.Dx - tail + u, 2 * u);
            g.FillRectangle(Pal.Flame[0], rocket.Dx - 2 * u, -u / 2, 2 * u + u, Math.Max(1, u));
            g.DrawImage(rocket.Bmp, rocket.Dx, rocket.Dy, rocket.Bmp.Width, rocket.Bmp.Height);
            g.ResetTransform();
        }

        for (int i = 0; i < mg.Bits.Count; i++) // 파편(칸만 한 네모), 탄피, 맞은 자리 불꽃
        {
            ref var bit = ref mg.Bits[i];
            var p = o.ToClient(bit.X, bit.Y);
            int s2 = bit.Size / 2;
            switch (bit.Kind)
            {
                case Minigun.BitKind.Brass: g.FillRectangle(Brass, p.X - s2, p.Y - k / 2, bit.Size, Math.Max(1, k)); break;
                case Minigun.BitKind.Spark: // 十 모양으로 번쩍
                    g.FillRectangle(Pal.Flame[0], p.X - s2, p.Y - s2 / 3, bit.Size, s2 * 2 / 3);
                    g.FillRectangle(Pal.Flame[0], p.X - s2 / 3, p.Y - s2, s2 * 2 / 3, bit.Size);
                    g.FillRectangle(Pal.White, p.X - s2 / 3, p.Y - s2 / 3, s2 * 2 / 3, s2 * 2 / 3);
                    break;
                default: g.DrawImageUnscaled(Chip(bit.Size), p.X - s2 - 1, p.Y - s2 - 1); break;
            }
        }
        for (int i = 0; i < mg.Bullets.Count; i++) // 예광탄
        {
            ref var b = ref mg.Bullets[i];
            var p = o.ToClient(b.X, b.Y);
            var q = o.ToClient(b.X - b.Vx * .012, b.Y - b.Vy * .012);
            g.DrawLine(tracer!, q.X, q.Y, p.X, p.Y);
        }

        int flash = Flash(mg);
        for (int i = 0, n = mg.GunCount; i < n; i++) // 쌍 미니건이면 양손에 하나씩
        {
            var (pivot, aim) = mg.Gun(i);
            var pv = o.ToClient(pivot.X, pivot.Y);
            float cos = (float)Math.Cos(aim), sin = (float)Math.Sin(aim), kick = (float)(mg.Kick * 2 * k);
            g.TranslateTransform(pv.X - cos * kick, pv.Y - sin * kick);
            g.RotateTransform((float)(aim * 180 / Math.PI));
            if (cos < 0) g.ScaleTransform(1, -1); // 왼쪽을 겨누면 뒤집어서 총이 거꾸로 서지 않게
            g.DrawImage(gunArt.Bmp, gunBox.X, gunBox.Y, gunBox.Width, gunBox.Height);
            if (flash >= 0) g.DrawImage(muzzle[flash].Bmp, muzzleBox[flash].X, muzzleBox[flash].Y, muzzleBox[flash].Width, muzzleBox[flash].Height);
            g.ResetTransform();
        }
        (g.InterpolationMode, g.PixelOffsetMode) = (im, pm);

        var pet = host.Pet;
        if (mock) Fx.Pet(o, g, host.Sprites, pet.P.Stage, clearAt >= 0 ? "happy" : mg.Firing ? "angry" : "idle", mg.Clock, k, mg.X, mg.Ground, Math.Cos(mg.Aim) < 0);
        if (!pet.Emoting && clearAt < 0)
        {
            var plate = o.Client(HudPlate(hudX, hudY, k));
            int edge = Math.Max(1, k / 2);
            g.FillRectangle(Pal.Line, plate.X + edge, plate.Y, plate.Width - 2 * edge, plate.Height); // 말풍선처럼 모서리를 깎은 판
            g.FillRectangle(Pal.Line, plate.X, plate.Y + edge, plate.Width, plate.Height - 2 * edge);
            g.FillRectangle(Pal.Cream, plate.X + edge, plate.Y + edge, plate.Width - 2 * edge, plate.Height - 2 * edge);
            (grenade ? nadeIcon : rocketIcon).Put(o, g, hudX, hudY - 13 * k - k / 2);
            double ready = mg.Ready(grenade);
            GameHud.Bar(o, g, HudBar(hudX, hudY, k), k, ready, ready >= 1 ? Pal.Gold : Pal.Red);
        }
        if (clearAt >= 0) GameHud.Result(o, g, mg.Field, k, cleared, 0, false, "game/tile"); // 다 깼다: 금 간 조각 × 깬 창 수 (글자 없이)
        ses?.DrawGuide(o, g, mg.Clock, mg.X, mg.Ground);
    }

    // 파인 칸은 까맣게, 아직 남은 칸과 맞닿은 가장자리는 그을린 갈색으로, 금은 반 칸짜리 그을음으로. 다시 칠하는 곳에 걸친 칸만, 같은 줄의 이어진 칸은 한 번에 칠한다.
    // 다 깨진 창은 창 자리를 통째로 까맣게 (칸에 딱 맞지 않아 가장자리에 남는 자투리까지: 깨진 창에 테두리 한 줄이 떠 있지 않게)
    static void DrawHoles(Overlay o, Graphics g, Minigun mg)
    {
        if (mg.GoneCells == 0 && mg.CrackCells == 0) return;
        for (int n = 0; n < mg.Targets.Count; n++) if (mg.Done(n)) g.FillRectangle(Pal.Hole, o.Client(mg.Targets[n]));
        var f = o.Client(mg.Field);
        var clip = g.ClipBounds;
        int c = mg.Cell, h = c / 2, w = mg.Gw;
        int x0 = (int)Math.Max(0, Math.Floor((clip.Left - f.X) / c)), x1 = (int)Math.Min(mg.Gw - 1, Math.Floor((clip.Right - f.X) / c));
        int y0 = (int)Math.Max(0, Math.Floor((clip.Top - f.Y) / c)), y1 = (int)Math.Min(mg.Gh - 1, Math.Floor((clip.Bottom - f.Y) / c));
        var look = mg.Looks;
        var crack = mg.Cracks;
        for (int gy = y0; gy <= y1; gy++)
            for (int gx = x0; gx <= x1; gx++)
            {
                int i = gy * w + gx;
                byte v = look[i];
                if (v == 0)
                {
                    int m = crack[i];
                    if (m == 0) continue;
                    int px = f.X + gx * c, py = f.Y + gy * c;
                    if ((m & 1) != 0) g.FillRectangle(Pal.Soot, px, py, h, h);
                    if ((m & 2) != 0) g.FillRectangle(Pal.Soot, px + h, py, h, h);
                    if ((m & 4) != 0) g.FillRectangle(Pal.Soot, px, py + h, h, h);
                    if ((m & 8) != 0) g.FillRectangle(Pal.Soot, px + h, py + h, h, h);
                    continue;
                }
                int from = gx;
                while (gx < x1 && look[i + 1] == v) { gx++; i++; }
                g.FillRectangle(v == 1 ? Pal.Hole : Pal.Soot, f.X + from * c, f.Y + gy * c, (gx - from + 1) * c, c);
            }
    }

    // 장면 그림(--mode-scene)에서만: 쏘는 창을 밝은 네모로 그려 넣는다 (실제 앱에서는 진짜 창이 그 자리에 있다)
    static void DrawMock(Overlay o, Graphics g, Minigun mg)
    {
        foreach (var t in mg.Targets)
        {
            var r = o.Client(t);
            g.FillRectangle(Glass, r);
            g.FillRectangle(GlassBar, r.X, r.Y, r.Width, 10 * mg.K);
        }
    }

    // 디버그: minigun / minigun aim <x> <y> / minigun fire on|off / minigun missile <x> <y> / minigun nade <x> <y> / minigun dual / minigun stop
    public bool Command(string[] a)
    {
        if (a.Length == 0 || a[0] != Id) return false;
        if (a.Length > 1 && a[1] == "stop") Stop(true);
        else if (a.Length > 3 && a[1] == "aim") forceAim = new Point(int.Parse(a[2]), int.Parse(a[3]));
        else if (a.Length > 2 && a[1] == "fire") forceFire = a[2] == "on";
        else if (a.Length > 3 && a[1] == "nade") gun?.Throw(double.Parse(a[2]), double.Parse(a[3]));
        else if (a.Length > 3 && a[1] == "missile") gun?.Launch(double.Parse(a[2]), double.Parse(a[3]));
        else if (a.Length > 1 && a[1] == "dual") gun?.StartDual();
        else if (!Running && !host.Busy) Start();
        return true;
    }

    public IEnumerable<GameStat> Stats() { yield break; } // 점수가 없는 게임
    public string? SaveState() => null;
    public void LoadState(string? json) { }

    // ── 화면 없이: 가짜 판 (자가 검사·장면·속도 재기) ──
    static readonly Rectangle[] TestWins = { new(150, 120, 600, 420), new(850, 80, 520, 380), new(1450, 300, 400, 450) };

    sealed class Rig
    {
        public readonly SceneHost Host;
        public readonly MinigunGame Game;
        public readonly Minigun Mg;
        public readonly HashSet<Keys> Keys = new();
        public Point Mouse;
        public bool Front = true;

        public Rig(Rectangle field, Rectangle[] wins, int k = 3, int seed = 5, string stage = "child", bool guide = false)
        {
            Host = new SceneHost(stage, k);
            Game = new MinigunGame(Host);
            Mg = Game.gun = new Minigun(field, wins, field.Bottom - Host.Pet.K.FloorGap, k, new Random(seed), GripOf(stage, k), dualChance: 0);
            Mg.OnEvent = Game.OnEvent;
            Game.ses = Session.Fake(field, Keys.Contains, () => Front, () => Mouse);
            if (guide) Game.ses.Guide(GuideKeys);
            Host.Pet.StartPuppet();
        }

        public void Frames(int n) { for (int i = 0; i < n && Game.Running; i++) Game.Frame(1 / 60.0); }
        public void Click(MouseButtons button) { Game.ses!.Press(button, true); Frames(1); Game.ses?.Press(button, false); }
    }

    // 같은 씨로 20초: 창 셋을 훑으며 계속 쏘고, 1.5초마다 미사일·수류탄을 번갈아, 5초에 쌍 미니건 (--mode-bench minigun)
    public static string BenchRun()
    {
        var rig = new Rig(SceneHost.Area, TestWins, guide: true);
        using var ov = rig.Host.Overlay(rig.Game);
        rig.Game.ses!.Press(MouseButtons.Left, true);
        int f = 0;
        return Bench.Run("minigun", ov, rig.Game, SceneHost.Area, () =>
        {
            if (f >= 1200 || !rig.Game.Running) return false; // 20초, 그 전에 창을 다 깨면 거기까지
            double t = f / 60.0;
            rig.Mouse = new Point((int)(950 + 750 * Math.Sin(t * .9)), (int)(260 + 160 * Math.Sin(t * 2.3)));
            if (f % 90 == 45)
            {
                if (f / 90 % 2 == 0) rig.Keys.Remove(Keys.ShiftKey); else rig.Keys.Add(Keys.ShiftKey);
                rig.Game.ses.Press(MouseButtons.Right, true);
            }
            if (f == 300) rig.Mg.StartDual();
            f++;
            return true;
        }, () => rig.Game.Frame(1 / 60.0));
    }

    // 장면 여섯: 쏘는 중(큰 파편·금) → 날아가는 미사일(연기 꼬리) → 미사일 폭발 → 수류탄 폭발 → 다 가라앉은 뒤(들쭉날쭉한 구멍·금, 쌍 미니건)
    // → 창을 다 깼다 (남은 조각이 무너져 내리고, 볼록이가 기뻐하고, 가운데에 깬 창 수)
    public static void Scene(string path, int k)
    {
        var wins = new[] { new Rectangle(560, 330, 420, 330), new Rectangle(1040, 250, 330, 300) };
        var rig = new Rig(SceneHost.Area, wins, k, guide: true);
        var (game, mg) = (rig.Game, rig.Mg);
        game.mock = true;
        using var ov = rig.Host.Overlay(game);
        rig.Mouse = new Point(760, 500);
        int f = 0, launched = -1, boomed = -1, thrown = -1, nadeBoom = -1, smashed = -1;
        SceneKit.Shots(path, ov, new Rectangle(500, 210, 920, 860), 6, shot =>
        {
            f++;
            switch (shot)
            {
                case 0: game.forceFire = f > 60; break;                                   // 1초 걷고 1.2초 쏜다
                case 1: // 오른쪽 창으로 미사일
                    rig.Mouse = new Point(1230, 380);
                    game.forceFire = false;
                    if (launched < 0 && f > 150) { game.ses!.Press(MouseButtons.Right, true); launched = f; }
                    break;
                case 3: // Shift + 오른쪽: 왼쪽 창 안쪽으로 수류탄
                    rig.Mouse = new Point(700, 470);
                    rig.Keys.Add(Keys.ShiftKey);
                    if (thrown < 0 && f > boomed + 60) { game.ses!.Press(MouseButtons.Right, true); thrown = f; }
                    break;
                case 4: // 쌍 미니건 (머리 위 표시가 보이게 별 그림은 띄우지 않는다)
                    rig.Keys.Clear();
                    rig.Mouse = new Point(1290, 330);
                    if (f == nadeBoom + 20) mg.DualUntil = mg.Clock + 9;
                    game.forceFire = f > nadeBoom + 80;
                    break;
                case 5: // 부스러기만 남기고 판다 → 90% 를 넘어 남은 조각이 무너지고, 다 깼으니 기뻐한다
                    game.forceFire = false;
                    if (smashed < 0) { smashed = f; Smash(mg); }
                    break;
            }
            int booms = mg.Booms;
            game.Frame(1 / 60.0);
            if (mg.Booms > booms) { if (boomed < 0) boomed = f; else nadeBoom = f; }
            return shot switch
            {
                0 => f == 132,
                1 => launched > 0 && mg.Shells.Count > 0 && mg.Shells[0].Y < 640, // 창에 닿기 조금 전
                2 => boomed > 0 && f == boomed + 6,
                3 => nadeBoom > 0 && f == nadeBoom + 8,
                4 => nadeBoom > 0 && f == nadeBoom + 112,
                _ => f == smashed + 22,
            };
        });
    }

    // 장면·검사용: 창마다 흩어진 부스러기(열두 칸에 하나)만 남기고 판다. 90% 를 넘으니 다음 프레임에 남은 조각이 무너진다
    static void Smash(Minigun mg)
    {
        var dot = Ragged.Seeded(1);
        for (int gy = 0; gy < mg.Gh; gy++)
            for (int gx = 0; gx < mg.Gw; gx++)
                if (mg.Land(gx, gy) && !mg.Gone(gx, gy) && (gx * 5 + gy * 3) % 12 != 0) mg.Carve(mg.Field.Left + (gx + .5) * mg.Cell, mg.Field.Top + (gy + .5) * mg.Cell, 1, dot);
    }

    // ── 자가 검사 (화면 없이) ──
    static void Expect(bool ok, string what) { if (!ok) throw new Exception("자가 검사 실패: " + what); }

    public static string Check()
    {
        var field = new Rectangle(0, 0, 1920, 1040);
        var win = new Rectangle(700, 200, 500, 300);
        double ground = 990;
        var grip = GripOf("child", 3);
        Minigun Make(int seed = 1, int k = 3) => new(field, new[] { win }, ground, k, new Random(seed), GripOf("child", k), dualChance: 0);
        void Run(Minigun m, double sec, double mx, double my, bool fire) { for (double t = 0; t < sec; t += 1 / 60.0) m.Update(1 / 60.0, mx, my, fire); }
        int Area(Minigun m) => m.GoneCells * m.Cell * m.Cell;

        // 마우스 X 쪽으로 걷고, 위쪽만 겨눈다
        var walk = Make();
        Run(walk, 3, 1500, 900, false);
        Expect(Math.Abs(walk.X - 1500) < 3 && walk.Aim < 0, $"볼록이는 마우스 X 쪽으로 걷는다: {walk.X:F0}");
        Run(walk, .2, 1500, 1040, false);
        Expect(walk.Aim <= -.11 && walk.Aim >= -Math.PI + .11, "아래로는 못 쏜다");

        // 칸은 6 × 배율. 총은 몸 가운데 높이 (ry + 4) × 배율에 쥔다: 아기는 낮게, 어른은 높게, 어느 모습이든 몸 안
        Expect(walk.Cell == 18 && Make(k: 5).Cell == 30, "칸은 6 × 배율");
        foreach (string form in Showcase.Forms)
            foreach (int k in new[] { 2, 3, 5 })
            {
                var (rx, ry) = PetArt.Embedded.Size(form);
                var held = new Minigun(field, new[] { win }, ground, k, new Random(1), GripOf(form, k), dualChance: 0);
                double up = ground - held.Pivot.Y;
                Expect(Math.Abs(up - (ry + 4) * k) < .01 && up > 4 * k && up < (2 * ry + 4) * k, $"총은 몸 가운데 높이에 쥔다 ({form} ×{k}): {up:F1}");
                held.StartDual();
                Expect(Math.Abs(held.Gun(1).Pivot.X - held.Gun(0).Pivot.X - 2 * (rx + 2.3) * k) < .01, $"쌍 미니건은 몸 양옆에 ({form} ×{k})");
            }
        Expect(GripOf("baby", 3).Hold < GripOf("child", 3).Hold, "아기는 어린이보다 낮게 쥔다");

        // 총신이 돌아야 쏘고, 돌고 나면 초당 25발
        var spin = Make();
        Run(spin, Minigun.SpinUpSec * .5, 950, 100, true);
        Expect(spin.Shots == 0, "총신이 도는 동안은 안 나간다");
        Run(spin, 1, 950, 100, true);
        Expect(spin.Shots is >= 18 and <= 27, $"돌고 나면 초당 25발: {spin.Shots}");

        // 맞은 자리가 파이고, 계속 쏘면 아래부터 위로 뚫고 올라간다. 창 밖은 안 파인다
        var dig = Make();
        Run(dig, 3.5, 950, 0, false);
        int hits = 0, chunks = 0;
        dig.OnEvent = e => { if (e == "hit") hits++; else if (e == "chunk") chunks++; };
        double Top(Minigun m) { for (int gy = 0; gy < m.Gh; gy++) for (int gx = 0; gx < m.Gw; gx++) if (m.Gone(gx, gy)) return gy * m.Cell; return double.MaxValue; }
        for (int i = 0; i < 120 && hits + chunks == 0; i++) dig.Update(1 / 60.0, 950, 0, true); // 총신이 돌고 첫 발이 맞을 때까지
        int early = dig.GoneCells, firstBits = 0, firstMax = 0, firstMin = 999;
        for (int i = 0; i < dig.Bits.Count; i++)
            if (dig.Bits[i].Kind == Minigun.BitKind.Chip) { firstBits++; firstMax = Math.Max(firstMax, dig.Bits[i].Size); firstMin = Math.Min(firstMin, dig.Bits[i].Size); }
        double d1 = Top(dig);
        Expect(hits + chunks == 1 && firstBits == (chunks == 1 ? Minigun.ChunkBits : Minigun.HitBits) && (chunks == 1 ? firstMin >= 18 && firstMax <= 32 : firstMin >= 10 && firstMax <= 20),
            $"한 발 맞으면 파편 {Minigun.HitBits}개(11~20px), 덩어리면 {Minigun.ChunkBits}개(18~32px): {firstBits}개 {firstMin}~{firstMax}px");
        Run(dig, 4, 950, 0, true);
        double d2 = Top(dig);
        Expect(early > 0 && dig.GoneCells > early && d2 < d1 && d1 >= win.Bottom - 120, $"아래부터 파여 올라간다: {d1:F0} → {d2:F0}");
        bool Stray(Minigun m) // 창 밖이 파였거나, 파인 칸·창 밖에 금이 남았나
        {
            for (int gx = 0; gx < m.Gw; gx++)
                for (int gy = 0; gy < m.Gh; gy++)
                    if (m.Gone(gx, gy) && !win.Contains(gx * m.Cell + 2, gy * m.Cell + 2) || m.Cracks[gy * m.Gw + gx] != 0 && (m.Gone(gx, gy) || !m.Land(gx, gy))) return true;
            return false;
        }
        Expect(!Stray(dig), "창 밖은 안 파인다");
        int dug = Area(dig);
        Expect(dug > 15000 && d2 <= win.Top + 2 * dig.Cell, $"한 자리를 4초 쏘면 창 위까지 뚫린다: {dug}px², 위 {d2:F0}");
        var sweep = Make();
        Run(sweep, 3.5, 950, 0, false);
        (hits, chunks) = (0, 0);
        sweep.OnEvent = e => { if (e == "hit") hits++; else if (e == "chunk") chunks++; };
        for (int i = 0; i < 240; i++) sweep.Update(1 / 60.0, 720 + i * 2, 0, true); // 창 아래를 4초 동안 훑는다
        int swept = Area(sweep);
        Expect(swept > 70000, $"훑으며 4초 쏘면 넓게 파인다: {swept}px², {hits + chunks}발 맞음");
        Expect(chunks * 10 >= (hits + chunks) * 2 && chunks * 10 <= (hits + chunks) * 4, $"열 발에 세 발꼴로 큰 덩어리: {chunks}/{hits + chunks}");
        Expect(sweep.CrackCells > 0 && !Stray(sweep), $"덩어리 둘레에 금이 간다 (창 안의 남은 칸에만): {sweep.CrackCells}");
        Expect(sweep.Bits.Count <= Minigun.MaxBits, "파편 수 한도");

        // 덩어리 하나에 금 3~5줄 (Ragged.Cracks: 한 줄에 반 칸 5~10개. 구멍 안에서 시작하는 한두 칸은 안 새겨진다)
        int lines = 0;
        for (int seed = 0; seed < 40 && lines == 0; seed++) // 첫 발이 덩어리인 판을 찾아 (다른 구멍에 금이 가려지지 않게)
        {
            var one = new Minigun(field, new[] { new Rectangle(0, 0, 1920, 900) }, ground, 3, new Random(seed), grip, dualChance: 0);
            string? firstHit = null;
            one.OnEvent = e => firstHit ??= e is "hit" or "chunk" ? e : null;
            Run(one, 3.5, 950, 0, false);
            for (int i = 0; i < 120 && firstHit == null; i++) one.Update(1 / 60.0, 950, 0, true);
            if (firstHit == "chunk") lines = one.CrackCells;
        }
        Expect(lines is >= 3 * 3 and <= 5 * 10, $"덩어리 하나에 금 3~5줄: 반 칸 {lines}개");

        // 수류탄: 찍은 자리에서 크게 터진다 (가는 길의 창 아랫변에서 안 터진다). 구멍은 동그랗지 않다
        var nade = Make();
        Run(nade, 3.5, 950, 0, false);
        bool boom = false;
        nade.OnEvent = e => boom |= e == "boom";
        nade.Throw(950, 350);
        int flying = nade.Shells.Count;
        double blastY = 0;
        for (int i = 0; i < 60 * 3 && !boom; i++) { nade.Update(1 / 60.0, 950, 350, false); if (nade.Blasts.Count > 0) blastY = nade.Blasts[0].Y; }
        int big = Area(nade), blastBits = 0;
        for (int i = 0; i < nade.Bits.Count; i++) if (nade.Bits[i].Size >= 18) blastBits++;
        Expect(boom && flying == 1 && nade.Blasts.Count == 1 && Math.Abs(blastY - 350) < 1 && big > 24000 && big < 40000,
            $"수류탄은 가는 길의 창 아랫변에서 안 터지고 찍은 자리(창 안쪽)에서 크게 터진다: y {blastY:F0}, {big}px²");
        Expect(blastBits == Minigun.BlastBits && nade.CrackCells > 0, $"폭발 파편 {Minigun.BlastBits}개와 금: {blastBits}개, 금 {nade.CrackCells}");
        var (near, far) = Reach(nade, 950, 350);
        Expect(far > near * 1.25 && near > 100 * .55 && far < 100 * 1.45, $"폭발 구멍은 들쭉날쭉하다 (가장자리까지 {near:F0}~{far:F0}px)");
        foreach (var (tx, ty) in new[] { (500.0, 150.0), (1700.0, 300.0), (980.0, 600.0) })
        {
            var aim = new Minigun(field, Array.Empty<Rectangle>(), ground, 3, new Random(2), grip);
            aim.Throw(tx, ty);
            double last = double.MaxValue, bx = 0, by = 0; // 터지기 직전 프레임에 찍은 자리 가까이 와 있어야 한다 (포물선이 제대로 맞는지)
            for (int i = 0; i < 60 * 3 && aim.Shells.Count > 0; i++)
            {
                last = Math.Sqrt((aim.Shells[0].X - tx) * (aim.Shells[0].X - tx) + (aim.Shells[0].Y - ty) * (aim.Shells[0].Y - ty));
                aim.Update(1 / 60.0, aim.X, 0, false);
                if (aim.Blasts.Count > 0) (bx, by) = (aim.Blasts[0].X, aim.Blasts[0].Y);
            }
            Expect(last < 40 && aim.Booms == 1 && bx == tx && by == ty, $"수류탄은 찍은 자리로 날아가 거기서 터진다 ({tx},{ty}): 직전 {last:F0}px");
        }
        var miss = Make();
        miss.Throw(100, 900);
        Run(miss, 3, 100, 900, false);
        Expect(miss.Booms == 1 && miss.GoneCells == 0 && miss.Shells.Count == 0, "창 밖을 찍으면 거기서 터지고 창은 그대로, 터진 수류탄은 사라진다");

        // 미사일: 총구에서 곧게 나가며 빨라지고(300 → 1400) 연기를 끌다가 처음 닿는 창 칸에서 터진다. 수류탄보다 크게 판다
        var mis = Make();
        Run(mis, 3.5, 950, 0, false);
        Expect(mis.Launch(950, 250) && !mis.Launch(950, 250), "미사일은 쿨다운 동안 한 발만");
        var s0 = mis.Shells[0];
        double v0 = s0.Speed, vMax = 0, off = 0;
        int smoke = 0;
        for (int i = 0; i < 60 * 3 && mis.Booms == 0; i++)
        {
            mis.Update(1 / 60.0, 950, 250, false);
            smoke = Math.Max(smoke, mis.Puffs.Count);
            if (mis.Shells.Count == 0) continue;
            var s = mis.Shells[0];
            (vMax, off) = (Math.Max(vMax, s.Speed), Math.Max(off, Math.Abs((s.X - s0.X) * s0.Vy - (s.Y - s0.Y) * s0.Vx)));
        }
        int huge = Area(mis);
        var hit = mis.Blasts[0];
        Expect(s0.Missile && Math.Abs(v0 - 300) < 1 && vMax > 1000 && vMax <= 1400 && off < .01 && smoke >= 5, $"미사일은 곧게 나가며 빨라지고 연기를 끈다: {v0:F0} → {vMax:F0}px/s, 연기 {smoke}");
        var hole = Reach(mis, hit.X, hit.Y);
        var same = Make(); // 같은 자리에 수류탄을 던진 것과 견준다
        same.Throw(hit.X, hit.Y);
        Run(same, 2, 950, 0, false);
        Expect(mis.Booms == 1 && huge > Area(same) * 1.3 && Math.Abs(hit.X - 950) < 3 && hit.Y > win.Bottom - 40 && mis.Land(950 / 18, 250 / 18) && !mis.Gone(950 / 18, 250 / 18) && hole.Far < 130 * 1.45,
            $"미사일은 겨눈 자리까지 안 가고 처음 닿는 창 아랫변에서 수류탄보다 크게 터진다: y {hit.Y:F0}, {huge}px² (수류탄이면 {Area(same)}px²), 가장 먼 가장자리 {hole.Far:F0}px");
        var wide = Make();
        Run(wide, 3.5, 950, 0, false);
        wide.Launch(100, 60);
        Run(wide, 3, 100, 60, false);
        Expect(wide.Booms == 1 && wide.GoneCells == 0 && wide.Shells.Count == 0, "창을 못 만난 미사일은 화면 끝에서 터지고 창은 그대로");
        double cooling = mis.Ready(false);
        Run(mis, Minigun.MissileCooldown, 950, 250, false);
        Expect(cooling < 1 && mis.Ready(false) == 1 && mis.Ready(true) == 1 && Make().Ready(false) == 1, $"큰 무기는 쿨다운이 지나면 다시 준비된다: {cooling:F2} → 1");

        // 배율이 커지면 칸·구멍·파편이 같이 커진다
        var twice = Make(k: 6);
        Run(twice, 3.5, 950, 0, false);
        twice.Throw(950, 350);
        Run(twice, 2, 950, 350, false);
        Expect(Area(twice) > big * 2.5, $"배율 6 이면 수류탄 구멍도 크다: {Area(twice)}px² (배율 3: {big})");

        // 파편이 꽉 차 있어도 폭발 파편은 나온다
        var busy = new Minigun(field, new[] { new Rectangle(300, 100, 1300, 700) }, ground, 3, new Random(4), grip, dualChance: 0);
        busy.StartDual();
        Run(busy, 4, 950, 0, true);
        int before = 0, after = 0;
        for (int i = 0; i < busy.Bits.Count; i++) if (busy.Bits[i].Size >= 18 && busy.Bits[i].Kind == Minigun.BitKind.Chip) before++;
        busy.Throw(950, 500);
        for (int i = 0; i < 180 && busy.Booms == 0; i++) busy.Update(1 / 60.0, 950, 0, true);
        for (int i = 0; i < busy.Bits.Count; i++) if (busy.Bits[i].Size >= 18 && busy.Bits[i].Kind == Minigun.BitKind.Chip) after++;
        Expect(busy.Bits.Count <= Minigun.MaxBits && after >= before + 30, $"파편이 많아도 폭발 파편은 나온다: 큰 조각 {before} → {after}, 모두 {busy.Bits.Count}");

        // 쌍 미니건: 총이 둘이라 두 배로 쏟아진다. 하는 동안 한 번씩 저절로 된다
        var dual = Make();
        dual.StartDual();
        Run(dual, Minigun.SpinUpSec + 1, 950, 100, true);
        Expect(dual.Dual && dual.GunCount == 2 && dual.Gun(0).Pivot.X != dual.Gun(1).Pivot.X && dual.Shots >= 36, $"쌍 미니건: 양손에 총 둘, 두 배로: {dual.Shots}발");
        Run(dual, Minigun.DualSec, 950, 100, false);
        Expect(!dual.Dual && dual.GunCount == 1, "10초 지나면 다시 하나");
        var later = Make();
        bool came = false;
        later.OnEvent = e => came |= e == "dual";
        Run(later, 45, 950, 100, false);
        Expect(came, "하는 동안 20~40초마다 쌍 미니건이 된다");

        // 창이 사라지면 대상에서 빼고 파인 자리·금도 지운다
        dig.RemoveTarget(win);
        Expect(dig.Targets.Count == 0 && dig.GoneCells == 0 && dig.CrackCells == 0 && !dig.Solid(950, 400), "창이 사라지면 대상에서 뺀다");

        // 다 깨진 창: 칸의 90% 가 파이면 남은 조각이 무너져 내린다 (그 전에는 그대로). 창이 여럿이면 창마다 따로 센다
        var dot = Ragged.Seeded(1);
        var twoWins = new[] { new Rectangle(306, 198, 360, 270), new Rectangle(900, 144, 270, 180) }; // 칸(18px)에 딱 맞는 창 둘
        var crumble = new Minigun(field, twoWins, ground, 3, new Random(1), grip, dualChance: 0);
        int breaks = 0;
        crumble.OnEvent = e => { if (e == "break") breaks++; };
        int Cells(Minigun m, Rectangle t) { int n = 0; for (int gx = 0; gx < m.Gw; gx++) for (int gy = 0; gy < m.Gh; gy++) if (m.Land(gx, gy) && t.Contains(gx * m.Cell + 2, gy * m.Cell + 2)) n++; return n; }
        void Pick(Minigun m, Rectangle t, int count) // 창 t 의 칸을 위에서부터 count 개 한 칸씩 판다
        {
            for (int gy = 0; gy < m.Gh && count > 0; gy++)
                for (int gx = 0; gx < m.Gw && count > 0; gx++)
                    if (m.Land(gx, gy) && !m.Gone(gx, gy) && t.Contains(gx * m.Cell + 2, gy * m.Cell + 2) && m.Carve((gx + .5) * m.Cell, (gy + .5) * m.Cell, 1, dot)) count--;
        }
        int cellsA = Cells(crumble, twoWins[0]), cellsB = Cells(crumble, twoWins[1]), needA = (int)Math.Ceiling(cellsA * Minigun.BreakAt);
        Expect(crumble.Intact == 2 && crumble.Broken == 0 && cellsA == 20 * 15 && cellsB == 15 * 10, $"처음에는 다 멀쩡하다: {cellsA}칸, {cellsB}칸");
        Pick(crumble, twoWins[0], needA - 1);
        crumble.Update(1 / 60.0, 950, 0, false);
        Expect(crumble.GoneCells == needA - 1 && crumble.Intact == 2 && breaks == 0 && crumble.Share(0) < Minigun.BreakAt, $"90% 에 한 칸 모자라면 그대로다: {crumble.GoneCells}/{cellsA}칸");
        Pick(crumble, twoWins[0], 1);
        crumble.Update(1 / 60.0, 950, 0, false);
        int fell = 0;
        for (int i = 0; i < crumble.Bits.Count; i++) if (crumble.Bits[i].Kind == Minigun.BitKind.Chip) fell++;
        Expect(crumble.GoneCells == cellsA && crumble.Intact == 1 && crumble.Broken == 1 && breaks == 1 && crumble.Share(0) == 1 && crumble.Share(1) == 0 && fell == cellsA - needA && crumble.CrackCells == 0,
            $"90% 가 파이면 남은 조각이 무너져 내린다: {crumble.GoneCells}/{cellsA}칸, 덜 깨진 창 {crumble.Intact}, 떨어지는 조각 {fell}개");
        Expect(crumble.Carved.Any(r => r.Contains(Rectangle.Inflate(twoWins[0], -crumble.Cell, -crumble.Cell))) && crumble.Looks.IndexOf((byte)2) < 0, "무너진 창은 통째로 다시 칠하고, 그을린 가장자리가 남지 않는다");
        crumble.Update(1 / 60.0, 950, 0, false);
        Expect(breaks == 1 && crumble.Intact == 1, "한 번 무너진 창은 다시 무너지지 않는다");
        Pick(crumble, twoWins[1], cellsB * 8 / 10);
        crumble.Update(1 / 60.0, 950, 0, false);
        Expect(crumble.Intact == 1 && crumble.Broken == 1 && breaks == 1, "다른 창을 80% 깨도 아직 다 깬 게 아니다");
        var half = new Minigun(field, twoWins, ground, 3, new Random(1), grip, dualChance: 0);
        Pick(half, twoWins[0], cellsA);
        half.Update(1 / 60.0, 950, 0, false);
        half.RemoveTarget(twoWins[1]);
        Expect(half.Targets.Count == 1 && half.Intact == 0 && half.GoneCells == cellsA, "덜 깨진 창이 닫히면 남은 창은 다 깨진 창뿐이다");
        Pick(crumble, twoWins[1], cellsB);
        crumble.Update(1 / 60.0, 950, 0, false);
        crumble.RemoveTarget(twoWins[0]);
        Expect(crumble.Targets.Count == 1 && crumble.Intact == 0 && crumble.GoneCells == cellsB && crumble.Share(0) == 1, "깨진 창이 닫혀도 남은 창의 칸은 그대로 센다");

        // 조작: 글자 키로는 아무 일도 없다. 왼쪽 꾹 = 쏘기, 오른쪽 = 미사일, Shift + 오른쪽 = 수류탄. 하는 중에 또 시작해도 그대로. 보스 키로 끝내면 티가 나지 않는다
        var small = new Rectangle(400, 300, 900, 780);
        var smallWins = new[] { new Rectangle(560, 380, 420, 300), new Rectangle(1040, 340, 220, 260) };
        var rig = new Rig(small, smallWins, guide: true);
        var (game, play) = (rig.Game, rig.Mg);
        rig.Mouse = new Point(760, 450);
        foreach (var key in new[] { Keys.Q, Keys.A, Keys.D, Keys.W, Keys.S, Keys.R, Keys.Space, Keys.Tab }) rig.Keys.Add(key);
        rig.Frames(40);
        Expect(play.Shots == 0 && play.Shells.Count == 0 && !game.grenade, "글자 키·Space·Tab 으로는 아무 일도 없다");
        rig.Keys.Clear();
        rig.Click(MouseButtons.Right);
        Expect(play.Shells.Count == 1 && play.Shells[0].Missile && !game.grenade, "오른쪽 버튼 = 미사일");
        rig.Keys.Add(Keys.ShiftKey);
        rig.Click(MouseButtons.Right);
        Expect(play.Shells.Count == 2 && !play.Shells[1].Missile && game.grenade, "Shift + 오른쪽 버튼 = 수류탄");
        rig.Keys.Clear();
        game.ses!.Press(MouseButtons.Left, true);
        rig.Frames(60);
        game.ses.Press(MouseButtons.Left, false);
        int shots = play.Shots;
        rig.Frames(20);
        Expect(shots > 10 && play.Shots == shots, $"왼쪽을 누르고 있는 동안만 쏜다: {shots}발");
        Expect(rig.Host.Pet.Mode == Mode.Puppet && rig.Host.Pet.X == play.X && rig.Host.Pet.Y == play.Ground, "볼록이는 게임이 정한 자리에 선다");
        game.Start();
        Expect(ReferenceEquals(game.gun, play), "하는 중에 또 시작해도 판은 그대로다");

        // 다시 칠할 자리: 쏘고 미사일·수류탄을 터뜨리는 6초. 바뀐 자리만 다시 그려도 화면에 지나간 그림이 남지 않는다
        game.Stop(true);
        Expect(!game.Running && rig.Host.Pet.Mode == Mode.Wander && !rig.Host.Pet.Emoting, "보스 키로 끝내면 표정 없이 바로 끝난다");
        var gone = new Rig(small, smallWins);
        foreach (var t in gone.Mg.Targets.ToArray()) gone.Mg.RemoveTarget(t);
        gone.Frames(2);
        Expect(!gone.Game.Running && gone.Host.Pet.Mode != Mode.Puppet && gone.Host.Pet.Emoting, "쏠 창이 다 없어지면 끝난다 (이때는 기뻐한다)");
        // 창 둘은 6초면 다 깨져서 판이 끝난다: 총알이 안 닿는 구석(마우스가 가는 자리보다 오른쪽 아래)에 작은 창을 하나 더 둬서 이 검사 동안에는 끝나지 않게
        rig = new Rig(small, smallWins.Append(new Rectangle(1210, 820, 80, 200)).ToArray(), guide: true);
        (game, play) = (rig.Game, rig.Mg);
        using var ov = rig.Host.Overlay(game);
        int n = 0;
        var io = game.ses!;
        io.Press(MouseButtons.Left, true);
        var (bad, first) = SceneKit.Repaint(ov, game, small, () =>
        {
            n++;
            rig.Mouse = new Point((int)(850 + 330 * Math.Sin(n / 60.0 * 1.7)), (int)(470 + 90 * Math.Sin(n / 60.0 * 3.1)));
            if (n == 150) play.DualUntil = play.Clock + 3; // 쌍 미니건 (머리 위 별은 그림 창이 따로 그리니 여기서는 띄우지 않는다)
            if (n % 70 == 35)
            {
                if (n / 70 % 2 == 0) rig.Keys.Remove(Keys.ShiftKey); else rig.Keys.Add(Keys.ShiftKey);
                io.Press(MouseButtons.Right, true);
            }
            game.Frame(1 / 60.0);
            return game.Running;
        }, 360, every: 6);
        Expect(bad == 0 && play.Booms >= 4, $"바뀐 자리만 다시 칠해도 화면이 맞다: {bad}번 다름 (처음 {first}프레임), 폭발 {play.Booms}번");

        // 하는 동안 메모리를 거의 새로 잡지 않는다 (폭발할 때만 조금)
        var rects = new List<Rectangle>(2048);
        for (int i = 0; i < 60; i++) { game.Frame(1 / 60.0); rects.Clear(); game.Dirty(ov, rects); }
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 300; i++)
        {
            rig.Mouse = new Point((int)(850 + 330 * Math.Sin(i / 60.0 * 1.7)), 460);
            game.Frame(1 / 60.0);
            rects.Clear();
            game.Dirty(ov, rects);
        }
        long perFrame = (GC.GetAllocatedBytesForCurrentThread() - a0) / 300;
        Expect(perFrame < 200, $"쏘는 동안 프레임마다 새로 잡는 메모리가 거의 없다: {perFrame}B");

        // 파인 칸은 까맣게 그려지고, 끝나면 같은 프레임에 다 사라진다
        int core = play.Looks.IndexOf((byte)1);
        var (hx, hy) = (play.Field.Left + (core % play.Gw) * play.Cell, play.Field.Top + (core / play.Gw) * play.Cell);
        using var peek = new Bitmap(play.Cell, play.Cell);
        using var pg = Graphics.FromImage(peek);
        pg.TranslateTransform(-hx, -hy); // 뚫린 칸 하나만 들여다본다 (파편이 지나갈 수 있으니 끝난 뒤와 견준다)
        game.Draw(ov, pg);
        Expect(core >= 0 && peek.GetPixel(0, 0).A == 255, "파인 칸이 그려진다");
        game.Stop(true);
        rects.Clear();
        game.Dirty(ov, rects);
        pg.Clear(Color.Transparent);
        game.Draw(ov, pg);
        Expect(!game.Running && rects.Count == 0 && peek.GetPixel(0, 0).A == 0, "끝나면 그림도 같은 프레임에 사라진다 (다시 칠할 자리도, 그릴 것도 없다)");

        // 창을 다 깨면 저절로 끝난다: 쏘아서 창 하나를 다 깨도(다른 창이 남았으면) 계속하고, 마지막 창까지 깨면 잠깐 기뻐한 뒤 이긴 것으로 끝난다
        rig = new Rig(small, smallWins);
        (game, play) = (rig.Game, rig.Mg);
        var pet = rig.Host.Pet;
        game.ses!.Press(MouseButtons.Left, true);
        int sweeps = 0;
        bool Sweep(Rectangle t, double sec, Func<bool> done) // 창 t 아래를 좌우로 훑으며 쏜다 (done 이면 그만)
        {
            for (int i = 0; i < sec * 60 && game.Running && !done(); i++, sweeps++)
            {
                double u = Math.Abs((sweeps / 60.0 / 3 % 2) - 1); // 3초에 한 번 건너간다
                rig.Mouse = new Point((int)(t.Left + 8 + (t.Width - 16) * u), t.Top - 30);
                rig.Frames(1);
            }
            return done();
        }
        Sweep(smallWins[1], 1.5, () => false);
        Expect(game.Running && play.Intact == 2 && play.GoneCells > 0 && game.clearAt < 0, "조금 깬 것으로는 끝나지 않는다");
        Expect(Sweep(smallWins[1], 60, () => play.Intact == 1) && game.Running && game.clearAt < 0, $"창 하나를 다 깨도 다른 창이 남았으면 계속한다: {play.Share(1):P0}");
        double oneAt = play.Clock;
        rig.Mouse = new Point(700, 320);
        game.ses.Press(MouseButtons.Left, false);
        rig.Frames(120);
        Expect(game.Running && play.Intact == 1 && play.Broken == 1 && pet.Mode == Mode.Puppet, "깨진 창이 하나 있어도 가만히 두면 끝나지 않는다");
        game.ses.Press(MouseButtons.Left, true);
        Expect(Sweep(smallWins[0], 90, () => game.clearAt >= 0) && game.Running && play.Intact == 0 && play.Breaks == 2, $"마지막 창까지 깨면 기뻐하기 시작한다: {play.Share(0):P0}");
        double clearSec = play.Clock;
        int shotsAtClear = play.Shots, cheerFrames = 0;
        Expect(pet.Emoting && pet.EmoteName == "star" && pet.Mode == Mode.Puppet && !pet.Saying, "다 깨면 머리 위에 별이 뜬다 (말은 하지 않는다)");
        rig.Mouse = new Point(600, 320);
        game.ses.Press(MouseButtons.Right, true); // 기뻐하는 동안에는 눌러도 더 쏘지 않는다
        rig.Frames(20);
        cheerFrames += 20;
        Expect(game.Running && play.Shots == shotsAtClear && play.Shells.Count == 0 && pet.Pose == "happy", $"기뻐하는 동안에는 쏘지 않고 기뻐한다: {pet.Pose}");
        rects.Clear();
        game.Dirty(ov, rects);
        Expect(rects.Contains(ov.Client(GameHud.CenterRect(small, 3))), "기뻐하는 동안 판 가운데(깬 창 수)를 다시 칠한다");
        while (game.Running && cheerFrames < 600) { rig.Frames(1); cheerFrames++; }
        Expect(!game.Running && Math.Abs(cheerFrames / 60.0 - CheerSec) < .06, $"{CheerSec}초 기뻐하고 저절로 끝난다: {cheerFrames}프레임");
        Expect(pet.Mode == Mode.Anim && pet.Pose == "happy" && pet.EmoteName == "star" && pet.Emoting && !pet.Saying, $"이긴 것으로 끝난다 (기뻐하고 별, 말은 없다): {pet.Mode} {pet.Pose} {pet.EmoteName}");
        pet.Hush();

        // 기뻐하는 중에도 보스 키는 바로, 표정 없이 끝낸다. 그리고 다 깨는 장면도 바뀐 자리만 다시 칠해 맞다
        rig = new Rig(small, smallWins);
        (game, play) = (rig.Game, rig.Mg);
        foreach (var t in play.Targets) play.Carve(t.Left + t.Width / 2.0, t.Top + t.Height / 2.0, 2000, dot);
        rig.Frames(10);
        Expect(game.Running && game.clearAt >= 0 && rig.Host.Pet.Emoting && play.Breaks == 0 && play.Intact == 0, "통째로 깨져도 다 깬 것이다 (무너질 조각은 없다)");
        game.Stop(true);
        rects.Clear();
        game.Dirty(ov, rects);
        Expect(!game.Running && rig.Host.Pet.Mode == Mode.Wander && !rig.Host.Pet.Emoting && rects.Count == 0 && game.clearAt < 0, "기뻐하는 중에 보스 키를 눌러도 표정 없이 바로 끝난다");
        rig = new Rig(small, smallWins);
        (game, play) = (rig.Game, rig.Mg);
        using var ov2 = rig.Host.Overlay(game);
        game.ses!.Press(MouseButtons.Left, true);
        n = 0;
        (bad, first) = SceneKit.Repaint(ov2, game, small, () =>
        {
            n++;
            rig.Mouse = new Point((int)(850 + 330 * Math.Sin(n / 60.0 * 1.7)), 330);
            if (n == 90) Smash(play); // 부스러기만 남긴다 → 무너진다
            game.Frame(1 / 60.0);
            rig.Host.Pet.Hush(); // 머리 위 별은 그림 창이 따로 그린다
            return game.Running;
        }, 600, every: 3);
        Expect(bad == 0 && !game.Running && play.Breaks == 2 && n > 90 + CheerSec * 60 - 3 && n < 90 + CheerSec * 60 + 6, $"다 깨고 기뻐하는 장면도 바뀐 자리만 다시 칠해 맞다: {bad}번 다름 (처음 {first}프레임), {n}프레임, 무너진 창 {play.Breaks}");
        rig.Host.Pet.Hush();
        return $"미니건: 한 자리 4초 {dug}px² 파임(아래 {d1:F0} → {d2:F0}), 훑으며 4초 {swept}px², 수류탄 한 방 {big}px², 미사일 한 방 {huge}px², 덩어리 금 반 칸 {lines}개, 쏘는 동안 {perFrame}B/프레임, " +
               $"창 둘을 훑어 쏴서 {oneAt:F0}초에 하나·{clearSec:F0}초에 다 깨고 {cheerFrames / 60.0:F1}초 기뻐한 뒤 저절로 끝남";
    }

    // 파인 자리의 가장자리가 (x, y) 에서 가장 가까운 거리와 가장 먼 거리 (16 방향으로 재서)
    static (double Near, double Far) Reach(Minigun m, double x, double y)
    {
        double near = double.MaxValue, far = 0;
        for (int a = 0; a < 16; a++)
        {
            double dx = Math.Cos(a * Math.PI / 8), dy = Math.Sin(a * Math.PI / 8), d = 0;
            if (!m.Land((int)((x + dx * 4 - m.Field.Left) / m.Cell), (int)((y + dy * 4 - m.Field.Top) / m.Cell))) continue; // 창 밖 쪽은 재지 않는다
            while (d < 400 && m.Gone((int)((x + dx * d - m.Field.Left) / m.Cell), (int)((y + dy * d - m.Field.Top) / m.Cell))) d += 2;
            if (!m.Land((int)((x + dx * d - m.Field.Left) / m.Cell), (int)((y + dy * d - m.Field.Top) / m.Cell))) continue; // 창 끝까지 뚫렸다
            (near, far) = (Math.Min(near, d), Math.Max(far, d));
        }
        return (near, far);
    }
}
