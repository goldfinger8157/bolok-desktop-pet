using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Bolok;

// ICBM 모듈: 앱에 붙이기(볼록이·소리·떠난 친구), 그리기, 자가 검사, 디버그 명령. 규칙은 Icbm.cs, 구멍 그림은 IcbmArt.cs
// 조작: 메뉴로 쏜다. 하는 중에 또 고르면 한 발 더 (구멍 다섯까지). 판을 깔지 않아 일하던 창은 그대로 쓰고, 키·마우스는 읽지 않는다.
// Esc(보스 키)면 구멍·미사일·빼꼼하던 친구·자연이 그 프레임에 다 사라지고 볼록이는 표정 없이 하던 대로 돌아간다.
// 볼록이를 잡아서 옮기면(host.Pet.Mode 가 Puppet 이 아니면) 그 자리를 따라가고, 놓으면 거기서부터 이어서 한다
sealed class IcbmGame : IGameMode
{
    readonly IGameHost host;
    Icbm? icbm;
    Session? ses;
    Func<Rectangle?> activeWindow = () => null;   // 쓰던 창 (거기에는 안 떨어뜨린다)
    Random fx = new();                            // 그림만 흔드는 난수 (미사일 떨림·꼬리 불꽃). 규칙의 난수와 섞지 않는다

    // 사건 → 소리 (Kit 의 표). 떨어질 때 울리는 「콰콰콰콰」(rumble)는 이 게임에만 있는 소리라 따로 낸다
    static readonly Dictionary<string, Sound> Sounds = new() { ["press"] = Sound.Tap, ["launch"] = Sound.ShotGun, ["boom"] = Sound.Boom };

    public IcbmGame(IGameHost host) => this.host = host;
    public string Id => "icbm";
    public string MenuKey => "menu.icbm";
    public bool Running => icbm != null;

    public void Start() => Start(Screen.AllScreens.Select(s => s.WorkingArea).ToList(), new Random(), () => Native.Foreground()?.R, host.Friends);

    // friends: 자연으로 떠난 친구들 (구멍에서 가끔 빼꼼, 떠날 때 색 그대로)
    void Start(IReadOnlyList<Rectangle> screens, Random rng, Func<Rectangle?> active, IReadOnlyList<string> friends)
    {
        var b = host.Pet;
        if (icbm != null) { if (!icbm.Launch()) b.Emote("no"); return; } // 이미 하는 중이면 한 발 더 (구멍 다섯까지)
        if (host.Busy) return;
        if (b.P.Asleep) { b.Emote("zzz"); return; }
        if (b.P.Energy < 10) { b.Emote("no"); return; } // 고칠 기운이 없으면 안 쏜다
        Reset(new Icbm(screens, b.X, b.Y, b.Scale, rng) { Friends = friends, OnEvent = OnEvent });
        activeWindow = active;
        ses = Session.Open(host, b.Area, () => Stop(true), layer: false); // 판 없이: 클릭도 키도 일하던 창으로 간다
        b.StartPuppet();
        icbm!.Launch();
        Dbg.Log("mode-start", ("id", Id));
    }

    void OnEvent(string e)
    {
        if (icbm is not { } ic) return;
        var b = host.Pet;
        Snd.Play(Sounds, e);
        switch (e)
        {
            case "fall": Sfx.Play("rumble"); break;
            case "peek": b.Emote("heart", 1.6); Dbg.Log("icbm-peek", ("friend", ic.Peeks.Count > 0 ? ic.Peeks[^1].Species : null)); break; // 옛 친구를 알아본다
            case "boom": b.Emote("bang", 1); break;
            case "blown": b.Throw(ic.BlownVx, ic.BlownVy, hard: true); break; // 어지러워하다가 거기서부터 고치러 간다
            case "nowhere": b.Emote("ask", 2); break; // 떨어뜨릴 곳이 없다: 갸웃 (말 대신 그림)
            case "repaired": b.P.Energy = Math.Max(0, b.P.Energy - 4); b.Emote("spark", .8); break; // 일은 기운이 든다
        }
        if (e is "boom" or "nowhere" or "repaired") Dbg.Log("icbm", ("event", e), ("craters", ic.Craters.Count), ("x", (int)ic.TX), ("y", (int)ic.TY));
    }

    public void Frame(double dt)
    {
        var (ic, b) = (icbm!, host.Pet);
        ic.Update(dt, ic.Stage == Icbm.Phase.Away ? activeWindow() : null);
        Track();
        if (b.Mode != Mode.Puppet) // 잡아서 옮기거나 던지는 중: 볼록이가 가는 대로 두고, 내려앉으면 거기서 이어서 한다
        {
            ic.Hold(b.X, b.Y);
            if (b.Mode == Mode.Wander) b.StartPuppet();
            return;
        }
        (b.X, b.Y, b.Dir, b.Pose) = (ic.X, ic.Y, ic.Dir, ic.Pose);
        if (ic.Finished) Stop(false);
    }

    // quit(보스 키·디버그 stop): 구멍·미사일·친구·자연을 그 프레임에 다 지우고, 표정·별 없이 하던 대로 돌아간다. 아니면 다 고치고 기뻐한다
    public void Stop(bool quit)
    {
        if (icbm is not { } ic) return;
        Reset(null);
        ses?.Close(); // 녹화 숨김을 풀고 그림 창을 통째로 다시 칠한다
        ses = null;
        var b = host.Pet;
        if (quit)
        {
            Hush();
            if (b.Mode == Mode.Puppet) b.LeavePuppet(); // 잡혀 있거나 날아가는 중이면 그대로 둔다 (내려앉으면 평소대로 돌아다닌다)
            Dbg.Log("icbm-quit", ("launched", ic.Launched), ("craters", ic.Craters.Count));
        }
        else b.EndPuppet(true, "icbm", ("launched", ic.Launched));
        Dbg.Log("mode-end", ("id", Id));
    }

    // 울리던 소리(떨어지는 소리는 2초가 넘는다)도 바로 끊는다
    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    static extern bool StopSound(IntPtr none, IntPtr module, uint flags);
    static void Hush() { if (Sfx.On) StopSound(IntPtr.Zero, IntPtr.Zero, 0); }

    // ── 그리기: 구멍(자연·빼꼼), 빨간 버튼, 파편·연기, 떨어질 자리, 미사일, 버섯구름 (글자 없이 그림만) ──
    // 움직이지 않는 것은 바뀐 프레임에만 그 자리를 다시 칠한다. 프레임마다 다시 칠하는 건 파편·연기와 미사일뿐이다
    struct Seen
    {
        bool on;
        int key;
        Rectangle rect;
        public void Set(List<Rectangle> dirty, bool now, int k, Rectangle r)
        {
            if (now == on && (!now || k == key && r == rect)) return;
            if (on) dirty.Add(rect);
            if (now) dirty.Add(r);
            (on, key, rect) = (now, k, r);
        }
    }

    Sprites? artOf;
    int artK;
    Stamp buttonUp = null!, buttonDown = null!, aim = null!, heart = null!;
    Stamp[] nuke = null!;
    Stamp? missile, rising, falling; // 가는 쪽으로 기울인 미사일: 지금 그릴 것, 곧장 솟는 것(한 판에 한 번 만든다), 떨어지는 것(쏠 때마다 방향이 다르다)
    float fallingDeg;

    readonly List<CraterPic> pics = new();          // 구멍마다 그림 (구멍이 난 차례대로)
    readonly List<Rectangle> dirty = new();         // 이번 프레임에 바뀐 자리 (화면 좌표)
    readonly List<Rectangle> cloudRects = new();    // 버섯구름이 그려진 자리
    readonly (int X, int Y, int Size, int Heat)[] flames = new (int, int, int, int)[7];
    int versionSeen = -1, cloudKey, flameCount;
    Seen button, target;
    bool missileOn, aimOn, buttonOn;
    Point missileAt, buttonAt;
    Rectangle missileRect;

    // 판을 새로 놓거나(ic) 치운다(null): 그림은 다 버린다
    void Reset(Icbm? ic)
    {
        foreach (var p in pics) p.Dispose();
        pics.Clear();
        DropMissiles();
        dirty.Clear();
        cloudRects.Clear();
        (versionSeen, cloudKey, flameCount, button, target, missileOn, aimOn, buttonOn) = (-1, 0, 0, default, default, false, false, false);
        icbm = ic;
    }

    void Art(Sprites sp, int k)
    {
        if (ReferenceEquals(artOf, sp) && artK == k) return;
        var st = Stamps.Of(sp);
        (artOf, artK) = (sp, k);
        (buttonUp, buttonDown, aim) = (st.Get("game/button_up", 0, k), st.Get("game/button_down", 0, k), st.Get("game/aim", 0, 2 * k));
        (heart, nuke) = (st.Get("icon/heart", 0, CraterPic.HeartScale(k)), st.Row("game/nuke", Icbm.CloudScale * k));
        DropMissiles();
    }

    void DropMissiles()
    {
        rising?.Bmp.Dispose();
        falling?.Bmp.Dispose();
        missile = rising = falling = null;
    }

    // 미사일을 가는 쪽으로 기울인 그림 한 장 (칸의 (39, 39)·배율 2k 자리가 미사일 자리).
    // 솟는 동안·떨어지는 동안은 방향이 그대로라 프레임마다 돌려 그리지 않고 한 번씩만 만든다
    const float Up = -90;
    Stamp Turned(Sprites sp, int k, float deg)
    {
        if (deg == Up && rising != null) return rising;
        if (deg != Up && falling != null && deg == fallingDeg) return falling;
        int half = (int)Math.Ceiling(41 * k * Math.Sqrt(2)) + 2;
        using var cell = new Bitmap(half * 2, half * 2, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(cell))
        {
            g.TranslateTransform(half, half);
            g.RotateTransform(deg);
            g.DrawImageUnscaled(sp.Get("game/missile", 0, 2 * k), -39 * k, -39 * k);
        }
        var stamp = new Stamp(cell);
        if (deg == Up) return rising = stamp;
        falling?.Bmp.Dispose();
        fallingDeg = deg;
        return falling = stamp;
    }

    static int CloudFrame(Icbm ic, Icbm.Blast bl) => Math.Min(7, (int)((ic.Clock - bl.At) / .14));
    static Rectangle Join(Rectangle a, Rectangle b) => a.IsEmpty ? b : b.IsEmpty ? a : Rectangle.Union(a, b);

    // 프레임마다 한 번 (규칙 다음): 그림을 규칙에 맞추고, 바뀐 자리를 모은다. 그리기(Draw)는 여기서 정한 것만 그린다
    void Track()
    {
        var ic = icbm!;
        var sp = host.Sprites;
        int k = ic.K;
        Art(sp, k);
        dirty.Clear();

        if (ic.Version != versionSeen) // 구멍이 생기거나 줄거나 없어졌다: 그 구멍 자리만 다시 칠한다
        {
            versionSeen = ic.Version;
            for (int i = pics.Count - 1; i >= 0; i--)
            {
                if (ic.Craters.Contains(pics[i].Hole)) continue;
                pics[i].Gone(dirty);
                pics.RemoveAt(i);
            }
            foreach (var cr in ic.Craters)
            {
                CraterPic? pic = null;
                foreach (var p in pics) if (p.Hole == cr) pic = p;
                if (pic == null) pics.Add(pic = new CraterPic(cr, k));
                pic.Shape(dirty);
            }
        }
        foreach (var pic in pics) if (pic.Hole.Friend != null) pic.Animate(ic.Clock, ic.PeekAt(pic.Hole), sp, dirty); // 구름·냇물·빼꼼

        buttonOn = ic.Stage <= Icbm.Phase.Boom;
        buttonAt = new Point((int)(ic.ButtonX - 20 * k) + 20 * k, (int)(ic.ButtonY - 24 * k) + 20 * k); // 칸 가운데
        button.Set(dirty, buttonOn, ic.ButtonDown ? 1 : 0, (ic.ButtonDown ? buttonDown : buttonUp).Box(buttonAt.X, buttonAt.Y));

        aimOn = ic.HasTarget && (int)(ic.Clock * 10) % 2 == 0; // 떨어질 자리가 깜빡인다
        target.Set(dirty, aimOn, 0, aim.Box((int)ic.TX, (int)ic.TY));

        missileOn = ic.MissileOn;
        if (missileOn)
        {
            missile = Turned(sp, k, (float)(Math.Atan2(ic.MVy, ic.MVx) * 180 / Math.PI));
            int mx = (int)ic.MX, my = (int)ic.MY;
            var around = Rectangle.Empty;
            flameCount = 0;
            if (ic.Stage == Icbm.Phase.Fall) // 우르르 덜덜 떨리고, 꼬리에서 불꽃이 일렁인다 (글자는 쓰지 않는다: 어느 나라 사람이 봐도 같게)
            {
                (mx, my) = (mx + fx.Next(-3, 4) * k / 2, my + fx.Next(-3, 4) * k / 2);
                double len = Math.Max(1, Math.Sqrt(ic.MVx * ic.MVx + ic.MVy * ic.MVy)), ux = ic.MVx / len, uy = ic.MVy / len;
                for (int f = 0; f < flames.Length; f++) // 꼬리 쪽으로 갈수록 작고 붉게, 매번 조금씩 다르게
                {
                    double back = (16 + f * 5 + fx.Next(4)) * k, wob = (fx.NextDouble() - .5) * 6 * k;
                    int size = Math.Max(k, (9 - f) * k + fx.Next(-1, 2) * k);
                    int x = mx - (int)(ux * back) + (int)(-uy * wob), y = my - (int)(uy * back) + (int)(ux * wob);
                    flames[f] = (x, y, size, Math.Min(2, f / 2 + fx.Next(2)));
                    around = Join(around, new Rectangle(x - size / 2, y - size / 2, size, size));
                }
                flameCount = flames.Length;
            }
            missileAt = new Point(mx, my);
            missileRect = Join(around, missile.Box(mx, my));
        }

        int key = ic.Blasts.Count; // 버섯구름은 그림이 넘어갈 때만
        foreach (var bl in ic.Blasts) key = key * 31 + CloudFrame(ic, bl) + (int)bl.X * 7 + (int)bl.Y * 13;
        if (key != cloudKey)
        {
            cloudKey = key;
            dirty.AddRange(cloudRects);
            cloudRects.Clear();
            foreach (var bl in ic.Blasts) cloudRects.Add(nuke[CloudFrame(ic, bl)].Box((int)bl.X, (int)bl.Y - 18 * Icbm.CloudScale * k));
            dirty.AddRange(cloudRects);
        }
    }

    static Rectangle BitRect(in Icbm.Bit bit)
    {
        int s = bit.Smoke ? Math.Max(2, (int)(bit.Size * Math.Min(1, bit.Life / .5))) : bit.Size + 2;
        return new Rectangle((int)bit.X - (bit.Smoke ? s / 2 : bit.Size / 2 + 1), (int)bit.Y - (bit.Smoke ? s / 2 : bit.Size / 2 + 1), s, s);
    }

    public void Dirty(Overlay o, List<Rectangle> rs)
    {
        if (icbm is not { } ic) return;
        foreach (var r in dirty) rs.Add(o.Client(r));
        if (missileOn) rs.Add(o.Client(missileRect));
        foreach (ref readonly var bit in ic.Bits) rs.Add(o.Client(BitRect(bit)));
    }

    public void Draw(Overlay o, Graphics g)
    {
        if (icbm is not { } ic || artOf == null) return;
        int k = ic.K;
        foreach (var pic in pics) pic.Draw(o, g);
        foreach (var pk in ic.Peeks) // 빼꼼하는 친구는 구멍들 위에 (나온 차례대로)
            foreach (var pic in pics) if (pic.Hole == pk.Hole) pic.DrawPeek(o, g, heart);
        if (buttonOn) (ic.ButtonDown ? buttonDown : buttonUp).Draw(g, buttonAt.X - o.Left, buttonAt.Y - o.Top); // 빨간 버튼
        foreach (ref readonly var bit in ic.Bits) // 연기는 사라지며 작아지고, 파편은 칸만 한 네모
        {
            var p = o.ToClient(bit.X, bit.Y);
            if (bit.Smoke) Fx.Puff(g, p.X, p.Y, bit.Size, bit.Life);
            else Fx.Chip(g, p.X, p.Y, bit.Size);
        }
        if (aimOn) aim.Put(o, g, (int)ic.TX, (int)ic.TY);
        if (missileOn && missile != null)
        {
            for (int f = 0; f < flameCount; f++) Fx.Ember(g, flames[f].X - o.Left, flames[f].Y - o.Top, flames[f].Size, flames[f].Heat);
            missile.Draw(g, missileAt.X - o.Left, missileAt.Y - o.Top);
        }
        foreach (var bl in ic.Blasts) // 버섯구름: 그림의 바닥 가운데(20, 38)를 터진 자리에
            nuke[CloudFrame(ic, bl)].Put(o, g, (int)bl.X, (int)bl.Y - 18 * Icbm.CloudScale * k);
    }

    // 디버그: icbm (쏜다. 하는 중이면 한 발 더) / icbm stop (보스 키처럼 바로 치운다)
    public bool Command(string[] a)
    {
        if (a.Length == 0 || a[0] != Id) return false;
        if (a.Length > 1 && a[1] == "stop") Stop(true);
        else Start();
        return true;
    }

    public IEnumerable<GameStat> Stats() => Array.Empty<GameStat>(); // 기록이 없는 놀이다
    public string? SaveState() => null;
    public void LoadState(string? json) { }

    // ── 화면 없이 그려 보기 (--mode-scene icbm): 위는 쏘는 장면 셋(버튼을 누르고 솟는다, 떨어진다, 터진다),
    //    아래는 구멍들 (친구 구멍: 막 터짐·다 올라옴·눈 깜빡·올라오는 중 / 검은 구멍, 고치며 줄어드는 친구 구멍 둘, 다른 자리의 검은 구멍) ──
    static readonly Color Back = Color.FromArgb(150, 160, 170);

    public static void Scene(string path, int k)
    {
        var area = SceneHost.Area;
        var host = new SceneHost(scale: k);
        (host.Pet.X, host.Pet.Y) = (500, 900);
        var game = new IcbmGame(host) { fx = new Random(1) };
        using var ov = host.Overlay(game);
        using var frame = new Bitmap(ov.Width, ov.Height);
        using var fg = Graphics.FromImage(frame);
        int side = 640 * Math.Max(3, k) / 3;
        using var bmp = new Bitmap(Math.Max(side * 3 + 40, area.Width + 20), side + area.Height + 30);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        void Shot(Rectangle view, int x, int y)
        {
            fg.Clear(Back);
            ov.Render(fg);
            g.DrawImage(frame, new Rectangle(x, y, view.Width, view.Height), view, GraphicsUnit.Pixel);
        }
        Rectangle Around(double cx, double cy) =>
            new((int)Math.Clamp(cx - side / 2, area.Left, Math.Max(area.Left, area.Right - side)), (int)Math.Clamp(cy - side / 2, area.Top, Math.Max(area.Top, area.Bottom - side)), Math.Min(side, area.Width), Math.Min(side, area.Height));

        game.Start(new[] { area }, new Random(2), () => null, new[] { Sprites.DefaultSpecies });
        var ic = game.icbm!;
        void Until(Func<bool> done) { for (int i = 0; i < 60 * 60 && !done(); i++) game.Frame(1 / 60.0); }
        Until(() => ic.Stage == Icbm.Phase.Rise && ic.MY < ic.ButtonY - 70 * k);
        Shot(Around(ic.ButtonX, ic.ButtonY - side / 3), 10, 10);
        Until(() => ic.Stage == Icbm.Phase.Fall && ic.FallT >= .6);
        Shot(Around(ic.TX, ic.TY - side / 4), 20 + side, 10);
        Until(() => ic.Blasts.Count > 0 && ic.Clock - ic.Blasts[^1].At >= .6);
        Shot(Around(ic.TX, ic.TY - side / 4), 30 + side * 2, 10);
        game.Stop(true);
        host.Pet.Hush(); // 머리 위에 남은 「펑」 그림은 뺀다

        ic = new Icbm(new[] { area }, 500, 900, k, new Random(2)) { Clock = 3.3 };
        game.Reset(ic);
        double r0 = Icbm.CraterR * k / 3.0, gap = Math.Max(400, r0 * 2.9);
        int cols = (int)Math.Clamp((area.Width - 2 * r0) / gap, 1, 4);
        Icbm.Crater Hole(int col, double y, double frac, string? friend, double dx = 0)
        {
            var c = new Icbm.Crater { X = 360 + col * gap + dx, Y = y, R0 = r0, R = Math.Ceiling(r0 * frac / Icbm.Cell) * Icbm.Cell, Friend = friend };
            if (col < cols) ic.Craters.Add(c);
            return c;
        }
        double[] peekFor = { double.NaN, 1.2, 1.95, .3 }; // 위: 친구 구멍 — 터지자마자(아직 안 나옴), 다 올라옴, 눈 깜빡, 올라오는 중
        for (int col = 0; col < peekFor.Length; col++)
        {
            var c = Hole(col, 380, 1, Sprites.DefaultSpecies);
            if (col < cols && !double.IsNaN(peekFor[col])) ic.Peeks.Add(new Icbm.Peek { Hole = c, Species = c.Friend!, At = ic.Clock - peekFor[col] });
        }
        Hole(0, 820, 1, null); // 아래: 그냥 검은 구멍, 고치며 줄어드는 친구 구멍, 다른 자리(다른 모양)의 검은 구멍
        Hole(1, 820, .66, Sprites.DefaultSpecies);
        Hole(2, 820, .33, Sprites.DefaultSpecies);
        Hole(3, 820, 1, null, -3);
        ic.Version++;
        game.Track();
        Shot(area, 10, 20 + side);
        game.Reset(null);
        bmp.Save(path);
    }

    // 속도 재기: 같은 씨로 다섯 발을 쏘고 다 고칠 때까지 (--mode-bench icbm). 떠난 친구가 있어 구멍 몇은 자연이 보인다
    public static string BenchRun()
    {
        var host = new SceneHost();
        var game = new IcbmGame(host) { fx = new Random(1) };
        var ic = new Icbm(new[] { SceneHost.Area }, 1500, 1000, 3, new Random(2)) { Friends = new[] { Sprites.DefaultSpecies } };
        game.Reset(ic);
        for (int i = 0; i < 5; i++) ic.Launch();
        using var ov = host.Overlay(game);
        string result = Bench.Run("icbm", ov, game, SceneHost.Area, () => !ic.Finished, () => { ic.Update(1 / 60.0, null); game.Track(); });
        game.Reset(null);
        return result;
    }

    // ── 자가 검사 (화면 없이) ──
    static void Expect(bool ok, string what) { if (!ok) throw new Exception("자가 검사 실패: " + what); }

    public static string Check()
    {
        var screens = new[] { new Rectangle(0, 0, 1920, 1040), new Rectangle(1920, -271, 1080, 1799) };
        var active = new Rectangle(300, 100, 1200, 800);
        // 떨어질 자리: 폭발이 활성 창에 닿지 않고, 볼록이 위도 아니다. 화면이 활성 창으로 꽉 차면 없다
        var rng = new Random(1);
        for (int i = 0; i < 500; i++)
        {
            var t = Icbm.Pick(screens, active, 900, 1000, Icbm.CraterR, rng)!.Value;
            var blast = Rectangle.Inflate(new Rectangle((int)t.X, (int)t.Y, 0, 0), (int)Icbm.CraterR - 1, (int)Icbm.CraterR - 1);
            Expect(!blast.IntersectsWith(active) && screens.Any(sc => sc.Contains(blast)), $"활성 창에는 안 떨어진다 ({t.X:F0},{t.Y:F0})");
        }
        // 버섯구름이 화면 위·옆에서 잘리지 않을 만큼 떨어져서 터진다 (실제 한 발씩 쏴서 본다)
        for (int seed = 0; seed < 40; seed++)
        {
            var shot = new Icbm(screens, 900, 1000, 3, new Random(seed));
            shot.Launch();
            for (int i = 0; i < 60 * 20 && shot.Craters.Count == 0; i++) shot.Update(1 / 60.0, active);
            var cr = shot.Craters[0];
            var cloud = Rectangle.FromLTRB((int)cr.X - Icbm.CloudHalf * 3, (int)cr.Y - Icbm.CloudUp * 3, (int)cr.X + Icbm.CloudHalf * 3, (int)cr.Y);
            Expect(screens.Any(sc => sc.Contains(cloud)), $"버섯구름이 화면에서 안 잘린다 (seed {seed}: {cr.X:F0},{cr.Y:F0})");
        }
        Expect(Icbm.Pick(new[] { screens[0] }, screens[0], 900, 1000, Icbm.CraterR, rng) == null, "화면이 활성 창으로 꽉 차면 떨어질 데가 없다");

        // 한 발: 버튼 → 솟음 → 화면 밖 → 떨어짐 → 터짐 → 달려가 고침 → 끝
        var ic = new Icbm(screens, 900, 1000, 3, new Random(2));
        var seen = new List<Icbm.Phase>();
        var events = new List<string>();
        ic.OnEvent = events.Add;
        Expect(ic.Launch(), "발사 명령을 받는다");
        PointF hit = default;
        for (int i = 0; i < 60 * 120 && !ic.Finished; i++)
        {
            ic.Update(1 / 60.0, active);
            if (seen.Count == 0 || seen[^1] != ic.Stage) seen.Add(ic.Stage);
            if (ic.Craters.Count == 1 && hit == default) hit = new PointF((float)ic.Craters[0].X, (float)ic.Craters[0].Y);
        }
        Expect(seen.SequenceEqual(new[] { Icbm.Phase.ToButton, Icbm.Phase.Press, Icbm.Phase.Rise, Icbm.Phase.Away, Icbm.Phase.Fall, Icbm.Phase.Boom, Icbm.Phase.ToCrater, Icbm.Phase.Repair, Icbm.Phase.Done }),
            "차례: " + string.Join(">", seen));
        events.Remove("blown");
        Expect(string.Join(",", events) == "press,launch,fall,boom,repaired" && ic.Finished && ic.Craters.Count == 0, "터지고 나면 고쳐서 구멍이 사라진다: " + string.Join(",", events));
        Expect(Math.Abs(ic.X - hit.X) < 3 && !active.Contains((int)hit.X, (int)hit.Y), "볼록이가 터진 자리까지 가서 고친다");
        Expect(ic.Bits.Length == 0 && ic.Blasts.Count == 0, "파편·연기·버섯구름은 남지 않는다");

        // 떨어지는 건 콰콰콰콰~ 천천히: 1.6~2.6초
        var slow = new Icbm(screens, 900, 1000, 3, new Random(4));
        slow.Launch();
        double fallFrom = -1, fallTo = -1;
        for (int i = 0; i < 60 * 30 && fallTo < 0; i++)
        {
            slow.Update(1 / 60.0, active);
            if (slow.Stage == Icbm.Phase.Fall && fallFrom < 0) fallFrom = slow.Clock;
            if (fallFrom >= 0 && slow.Stage != Icbm.Phase.Fall) fallTo = slow.Clock;
        }
        Expect(fallTo - fallFrom is >= Icbm.FallMinSec - .05 and <= Icbm.FallMaxSec + .1, $"천천히 떨어진다: {fallTo - fallFrom:F2}초");

        // 떠난 친구 구멍: 터질 때 정해지고(자연이 바로 보인다), 그 구멍에서만 친구가 빼꼼. 고치는 구멍에서는 쏙, 친구가 없으면 다 검은 구멍
        int friendHoles = 0, allHoles = 0, peekStarts = 0;
        for (int seed = 0; seed < 8; seed++)
        {
            var pk = new Icbm(screens, 900, 1000, 3, new Random(seed)) { Friends = new[] { "hamster", "otter" } };
            var seenHoles = new HashSet<Icbm.Crater>();
            for (int i = 0; i < 5; i++) pk.Launch();
            for (int i = 0; i < 60 * 600 && !pk.Finished; i++)
            {
                int before = pk.Peeks.Count;
                pk.Update(1 / 60.0, active);
                foreach (var c in pk.Craters) if (seenHoles.Add(c)) { allHoles++; if (c.Friend != null) friendHoles++; Expect(c.Friend == null || pk.Friends.Contains(c.Friend), "떠난 친구 중 하나"); }
                peekStarts += Math.Max(0, pk.Peeks.Count - before);
                Expect(pk.Peeks.All(p => pk.Craters.Contains(p.Hole) && p.Hole.Friend == p.Species && pk.Clock - p.Hole.At >= Icbm.FirstPeek - 1e-6)
                    && pk.Peeks.GroupBy(p => p.Hole).All(g => g.Count() == 1), $"빼꼼은 친구 구멍에서 그 친구만, 구멍마다 하나씩 (seed {seed})");
                Expect(!(pk.Stage == Icbm.Phase.Repair && pk.Craters.Count == 1 && pk.Peeks.Count > 0), $"고치는 구멍에서는 안 나온다 (seed {seed})");
            }
        }
        Expect(friendHoles > 0 && friendHoles < allHoles && Math.Abs(friendHoles / (double)allHoles - Icbm.FriendChance) < .25,
            $"구멍 일부만 친구 구멍: {friendHoles}/{allHoles}");
        Expect(peekStarts >= friendHoles, $"친구 구멍에서는 빼꼼한다: {peekStarts}번 / 친구 구멍 {friendHoles}");
        var lonely = new Icbm(screens, 900, 1000, 3, new Random(1));
        for (int i = 0; i < 5; i++) lonely.Launch();
        bool anyFriend = false;
        for (int i = 0; i < 60 * 600 && !lonely.Finished; i++) { lonely.Update(1 / 60.0, active); anyFriend |= lonely.Craters.Any(c => c.Friend != null) || lonely.Peeks.Count > 0; }
        Expect(!anyFriend, "떠난 친구가 없으면 다 검은 구멍, 아무도 안 나온다");
        Expect(Icbm.Rise(0) == 0 && Icbm.Rise(1.4) == 1 && Icbm.Rise(Icbm.PeekSec) == 0, "빼꼼: 올라왔다가 내려간다");

        // 고치기 전에 또 쏘면 줄을 서고, 구멍은 다섯까지. 다 고치면 끝난다
        var many = new Icbm(screens, 900, 1000, 3, new Random(3));
        int ok = 0;
        for (int i = 0; i < 8; i++) if (many.Launch()) ok++;
        Expect(ok == Icbm.MaxCraters, $"한꺼번에 다섯 발까지: {ok}");
        int most = 0;
        for (int i = 0; i < 60 * 600 && !many.Finished; i++) { many.Update(1 / 60.0, active); most = Math.Max(most, many.Craters.Count); }
        Expect(many.Finished && most == Icbm.MaxCraters && many.Launched == Icbm.MaxCraters, $"다섯 발 다 떨어지고 다 고친다: 구멍 최대 {most}, {many.Clock:F0}초");

        // 가로 + 세로 모니터: 고치러 갈 때 모니터 사이 빈 곳(화면 밖)으로 안 지나간다. 걷는 속도는 평소(70)보다 조금 빠른 100
        var world = new World(screens);
        int crossed = 0;
        for (int seed = 0; seed < 12; seed++)
        {
            var walk = new Icbm(screens, 300, 1000, 3, new Random(seed));
            walk.Launch();
            double lastX = 300, lastY = 1000, fastest = 0;
            for (int i = 0; i < 60 * 200 && !walk.Finished; i++)
            {
                walk.Update(1 / 60.0, null);
                Expect(world.Inside(walk.X, walk.Y), $"화면 밖으로 걷지 않는다 (seed {seed}: {walk.X:F0},{walk.Y:F0})");
                if (walk.Stage == Icbm.Phase.ToCrater) fastest = Math.Max(fastest, Math.Sqrt((walk.X - lastX) * (walk.X - lastX) + (walk.Y - lastY) * (walk.Y - lastY)) * 60);
                (lastX, lastY) = (walk.X, walk.Y);
            }
            Expect(walk.Finished && fastest <= Icbm.GoSpeed + 1, $"뛰지 않고 걸어가서 고친다 (seed {seed}: {fastest:F0}px/s)");
            if (walk.X >= 1920) crossed++;
        }
        Expect(crossed >= 2, $"다른 모니터의 구멍도 건너가서 고친다: {crossed}/12");

        // 가끔(4번에 한 번쯤)은 볼록이 근처에 떨어지고, 그러면 볼록이가 폭발 반대쪽으로 날아간다
        int nearHits = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            var nb = new Icbm(screens, 900, 700, 3, new Random(seed));
            bool blown = false;
            nb.OnEvent = e => blown |= e == "blown";
            nb.Launch();
            for (int i = 0; i < 60 * 20 && nb.Craters.Count == 0; i++) nb.Update(1 / 60.0, null);
            var c0 = nb.Craters[0];
            double dx = 900 - c0.X, dy = 700 - 36 - c0.Y, d = Math.Sqrt(dx * dx + dy * dy);
            Expect(blown == d < Icbm.CraterR * 1.7, $"폭발 가까이 있을 때만 날아간다 (seed {seed}, {d:F0}px)");
            if (!blown) continue;
            nearHits++;
            Expect(nb.BlownVx * dx + nb.BlownVy * dy > 0 && Math.Sqrt(nb.BlownVx * nb.BlownVx + nb.BlownVy * nb.BlownVy) >= new Tuning().DizzySpeed,
                $"폭발 반대쪽으로 세게 던진 만큼 빠르게 날아간다 (seed {seed})");
            var far = new Brain(new Pet { Stage = "child", Mood = 50 }, new Tuning(), new Rectangle(0, 0, 1920, 1080)) { X = 900, Y = 500 };
            far.Throw(nb.BlownVx, nb.BlownVy, hard: true);
            for (int i = 0; i < 3000 && far.Mode == Mode.Thrown; i++) far.Update(1 / 60.0, false);
            Expect(far.Pose == "dizzy", $"날아갔다 내려앉으면 어지러워한다 (seed {seed})");
        }
        Expect(nearHits is >= 25 and <= 80, $"4번에 한 번쯤 볼록이 근처에 떨어진다: {nearHits}/200");

        // 잡아서 옮기기: 가는 중에 구멍 옆에 놓아 주면 거기서부터 걸어가 금방 고친다. 고치던 중에 잡으면 처음부터 다시
        var carry = new Icbm(screens, 300, 1000, 3, new Random(5));
        carry.Launch();
        for (int i = 0; i < 60 * 30 && carry.Stage != Icbm.Phase.ToCrater; i++) carry.Update(1 / 60.0, null);
        var hole = carry.Craters[0];
        for (int i = 0; i < 30; i++) { carry.Update(1 / 60.0, null); carry.Hold(hole.X - 60, hole.Y); }
        Expect(carry.Stage == Icbm.Phase.ToCrater && carry.X == hole.X - 60 && carry.Y == hole.Y, "잡고 있는 동안은 잡힌 자리에 있다");
        double from = carry.Clock;
        for (int i = 0; i < 60 * 10 && carry.Stage != Icbm.Phase.Repair; i++) carry.Update(1 / 60.0, null);
        Expect(carry.Stage == Icbm.Phase.Repair && carry.Clock - from < 2, $"구멍 옆에 놓으면 금방 도착한다: {carry.Clock - from:F1}초");
        for (int i = 0; i < 90; i++) carry.Update(1 / 60.0, null);
        Expect(hole.R < hole.R0, "고치는 중엔 구멍이 줄어든다");
        carry.Hold(hole.X + 200, hole.Y);
        Expect(carry.Stage == Icbm.Phase.ToCrater && hole.R == hole.R0, "고치다 잡히면 구멍은 그대로, 다시 가서 처음부터");
        for (int i = 0; i < 60 * 20 && !carry.Finished; i++) carry.Update(1 / 60.0, null);
        Expect(carry.Finished, "놓아 주면 끝까지 고친다");

        // 떨어질 데가 없으면 하늘에서 터지고 구멍은 안 난다
        var none = new Icbm(new[] { screens[0] }, 900, 1000, 3, new Random(4));
        bool sky = false;
        none.OnEvent = e => sky |= e == "nowhere";
        none.Launch();
        for (int i = 0; i < 60 * 20 && !none.Finished; i++) none.Update(1 / 60.0, screens[0]);
        Expect(sky && none.Finished && none.Craters.Count == 0, "떨어질 데가 없으면 구멍 없이 끝난다");

        string art = ArtChecks();
        string module = ModuleChecks();
        return $"ICBM: 한 발 {ic.Clock:F0}초(발사부터 복구까지), 다섯 발 {many.Clock:F0}초, {art}, {module}";
    }

    // 구멍 그림: 규칙(Ragged)과 같은 모양을 그리고, 자연은 안쪽 칸만 빈틈없이 덮으며, 움직이는 것만 고쳐 그린 그림이 새로 그린 그림과 같다
    static string ArtChecks()
    {
        var sp = Sprites.Embedded();
        var dirty = new List<Rectangle>();
        int holes = 0, frames = 0;
        foreach (int k in new[] { 2, 3, 5 })
            foreach (var (x, y, frac) in new[] { (360.0, 380.0, 1.0), (1521.5, 820.25, .66), (997.0, 820.0, .33), (-300.5, 77.0, 1.0), (2500.0, 1300.0, .2) })
            {
                double r0 = Icbm.CraterR * k / 3.0;
                var cr = new Icbm.Crater { X = x, Y = y, R0 = r0, R = Math.Ceiling(r0 * frac / Icbm.Cell) * Icbm.Cell, Friend = Sprites.DefaultSpecies };
                var shape = Ragged.At(x, y);
                using var pic = new CraterPic(cr, k);
                pic.Shape(dirty);
                Expect(pic.SameShape(shape.Cells(cr.R, Icbm.Cell), shape.Cracks(cr.R, cr.R0, Icbm.Cell / 2)), $"그리는 모양이 규칙의 폭발 모양과 같다: ({x}, {y}) r {cr.R}");
                var reach = Rectangle.Inflate(new Rectangle((int)x, (int)y, 0, 0), CraterPic.Reach(cr), CraterPic.Reach(cr));
                Expect(reach.Contains(pic.Bounds), "구멍과 금은 정해 둔 넓이 안에 그려진다");

                // 1/60 초씩 12초: 구름이 흐르고 반짝이가 옮겨 가고 친구가 올라왔다 내려간다. 바뀐 자리만 고친 그림 = 그 순간을 처음부터 그린 그림
                var peek = new Icbm.Peek { Hole = cr, Species = cr.Friend, At = 2 };
                int changed = 0;
                for (int i = 0; i <= 60 * 12; i++)
                {
                    double clock = i / 60.0;
                    var showing = clock >= peek.At && clock - peek.At <= Icbm.PeekSec ? peek : null;
                    dirty.Clear();
                    pic.Animate(clock, showing, sp, dirty);
                    if (dirty.Count > 0) changed++;
                    foreach (var d in dirty) Expect(reach.Contains(d), "바뀐 자리도 그 넓이 안이다");
                    if (i % 45 != 0) continue;
                    using var fresh = new CraterPic(cr, k);
                    fresh.Shape(dirty);
                    fresh.Animate(clock, showing, sp, dirty);
                    Expect(pic.SamePicture(fresh), $"고쳐 그린 자연이 새로 그린 것과 같다: 배율 {k}, ({x}, {y}), {clock:0.00}초");
                    Expect(pic.NatureFits(), "자연은 구멍 안쪽 칸만 빈틈없이 덮는다");
                    Expect(pic.SameAsPostcard(clock), $"구멍 속 자연이 엽서의 자연과 같은 그림이다: 배율 {k}, ({x}, {y}), {clock:0.00}초");
                }
                Expect(changed < 60 * 12 * .6, $"자연은 움직인 프레임에만 고쳐 그린다: {changed}/720");
                (holes, frames) = (holes + 1, frames + changed);
            }
        return $"구멍 그림 {holes}개 규칙과 같은 모양(자연은 프레임 {frames * 100 / (holes * 721)}% 에서만 고쳐 그림)";
    }

    // 모듈: 메뉴로 쏘고 또 쏘기, 잡아서 옮기기, 보스 키로 한 프레임에 치우기, 바뀐 자리만 다시 칠하기
    static string ModuleChecks()
    {
        var area = new[] { SceneHost.Area };
        var friends = new[] { Sprites.DefaultSpecies };
        var host = new SceneHost();
        var b = host.Pet;
        var game = new IcbmGame(host) { fx = new Random(1) };
        using var ov = host.Overlay(game);
        void Place() { (b.X, b.Y, b.Mode) = (900, 1000, Mode.Wander); b.Hush(); }
        void Frames(int n) { for (int i = 0; i < n && game.Running; i++) { b.Update(1 / 60.0, false); game.Frame(1 / 60.0); } }

        // 못 쏘는 때: 자는 중, 기운이 없을 때 (말 대신 그림으로 알린다)
        Place();
        b.P.Asleep = true;
        game.Start(area, new Random(2), () => null, friends);
        Expect(!game.Running && b.EmoteName == "zzz", "자는 중에는 안 쏜다");
        (b.P.Asleep, b.P.Energy) = (false, 5);
        game.Start(area, new Random(2), () => null, friends);
        Expect(!game.Running && b.EmoteName == "no", "기운이 없으면 안 쏜다");
        b.P.Energy = 80;

        // 쏘기: 판(키·클릭 받는 창)을 열지 않는다 (열면 이 가짜 앱이 예외를 낸다). 하는 중에 또 고르면 한 발 더, 다섯까지
        Place();
        game.Start(area, new Random(2), () => null, friends);
        var ic = game.icbm!;
        Expect(game.Running && b.Mode == Mode.Puppet && ic.Launched == 1 && game.ses is { Layer: null }, "메뉴로 쏜다 (판 없이)");
        b.Hush();
        for (int i = 0; i < 4; i++) game.Start(area, new Random(9), () => null, friends);
        Expect(ReferenceEquals(game.icbm, ic) && ic.Launched + ic.Queued == Icbm.MaxCraters && !b.Emoting, $"하는 중에 또 고르면 한 발 더: {ic.Launched + ic.Queued}");
        game.Start(area, new Random(9), () => null, friends);
        Expect(ic.Launched + ic.Queued == Icbm.MaxCraters && b.EmoteName == "no" && b.Emoting, "다섯 발이 넘으면 고개를 젓는다");

        // 잡아서 옮기기: 구멍이 난 뒤 볼록이를 들면 규칙이 그 자리를 따라오고, 놓으면 다시 이어서 한다
        Frames(60 * 12);
        Expect(ic.Craters.Count >= 1, "구멍이 난다");
        b.Mode = Mode.Held;
        (b.X, b.Y) = (400, 500);
        game.Frame(1 / 60.0);
        Expect(ic.X == 400 && ic.Y == 500 && b.Mode == Mode.Held, "잡고 있는 동안은 잡힌 자리를 따라간다");
        b.Mode = Mode.Wander;
        game.Frame(1 / 60.0);
        Expect(b.Mode == Mode.Puppet, "놓으면 이어서 한다");

        // 보스 키: 구멍·미사일·친구·자연이 그 프레임에 다 사라지고, 볼록이는 표정 없이 하던 대로
        Frames(60 * 14);
        Expect(game.Running && ic.Craters.Count >= 2 && game.pics.Count == ic.Craters.Count, $"구멍마다 그림이 있다: {game.pics.Count}/{ic.Craters.Count}");
        using var shot = new Bitmap(ov.Width, ov.Height, PixelFormat.Format32bppPArgb);
        using var sg = Graphics.FromImage(shot);
        int Painted()
        {
            sg.Clear(Back);
            ov.Render(sg);
            var data = shot.LockBits(new Rectangle(0, 0, shot.Width, shot.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                int n = 0, back = Back.ToArgb();
                var row = new int[shot.Width];
                for (int y = 0; y < shot.Height; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                    foreach (int p in row) if (p != back) n++;
                }
                return n;
            }
            finally { shot.UnlockBits(data); }
        }
        for (int i = 0; i < 60 * 10 && b.Mode != Mode.Puppet; i++) Frames(1); // 폭발에 날아갔으면 내려앉을 때까지
        b.Hush();
        var open = game.ses!;
        int before = Painted();
        double emoteWas = b.EmoteUntil;
        game.Stop(true);
        var rects = new List<Rectangle>();
        game.Dirty(ov, rects);
        Expect(before > 10000 && Painted() == 0 && rects.Count == 0 && game.pics.Count == 0, $"보스 키로 끝내면 그림이 한 프레임에 다 사라진다: {before} → {Painted()}점");
        Expect(!game.Running && open.Closed && b.Mode == Mode.Wander && b.EmoteUntil == emoteWas && !b.Emoting && b.Pose != "happy", "보스 키로 끝내면 표정 없이 바로 끝난다");

        // 날아가는 중(폭발에 휘말림)에 보스 키: 볼록이는 날던 대로 두고 게임만 치운다
        Place();
        game.Start(area, new Random(2), () => null, friends);
        b.Throw(900, -900, hard: true);
        game.Frame(1 / 60.0);
        game.Stop(true);
        Expect(!game.Running && b.Mode == Mode.Thrown, "날아가는 중에 끝내면 날던 대로 둔다");

        // 다시 칠할 자리: 다섯 발을 쏘고 다 고칠 때까지. 바뀐 자리만 다시 그려도 화면에 지나간 그림이 남지 않는다 (볼록이가 폭발에 날아가도)
        Place();
        b.P.Energy = 80;
        game.Start(area, new Random(2), () => null, friends);
        ic = game.icbm!;
        for (int i = 0; i < 4; i++) game.Start(area, new Random(2), () => null, friends);
        int friendHoles = 0, peeks = 0, most = 0, cells = 0, steps = 0;
        var (bad, first) = SceneKit.Repaint(ov, game, SceneHost.Area, () =>
        {
            if (!game.Running) return false;
            b.Update(1 / 60.0, false);
            int peeking = ic.Peeks.Count;
            game.Frame(1 / 60.0);
            if (!game.Running) return false;
            b.Hush(); // 머리 위 그림(펑·하트·반짝)과 볼록이가 내려앉으며 튀긴 부스러기는 앱이 따로 다시 칠한다: 여기서는 게임 그림만 본다
            b.Bits.Clear();
            peeks += Math.Max(0, ic.Peeks.Count - peeking);
            if (ic.Craters.Count > most) { most = ic.Craters.Count; friendHoles = ic.Craters.Count(c => c.Friend != null); }
            rects.Clear();
            game.Dirty(ov, rects);
            foreach (var r in rects) cells += (r.Width + 63) / 64 * ((r.Height + 63) / 64);
            steps++;
            return true;
        }, 60 * 300, every: 7);
        Expect(!game.Running && ic.Finished && bad == 0, $"바뀐 자리만 다시 칠해도 화면이 맞다: {bad}번 다름 (처음 {first}프레임), 끝남 {ic.Finished}");
        Expect(most == Icbm.MaxCraters && friendHoles > 0 && peeks > 0, $"그 판에 친구 구멍과 빼꼼이 있었다: 구멍 {most}, 친구 구멍 {friendHoles}, 빼꼼 {peeks}");
        Expect(b.Mode != Mode.Puppet && b.EmoteName == "star", "다 고치면 기뻐하며 끝난다");
        return $"보스 키로 {before / 1000}천 점이 한 프레임에 사라짐, 다시 칠한 넓이 프레임마다 {cells * 64.0 * 64 / Math.Max(1, steps) / 1000:0.0}천px²쯤";
    }
}
