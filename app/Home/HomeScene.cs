using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Bolok;

// 방석·집 장면 그려 보기 (화면을 띄우지 않는다): --home-scene 그림.png desk|nap|night|reveal|menu
// 진짜 Brain·HomeGeom·Overlay 를 돌려 정해진 순간마다 한 장씩 뽑아 가로로 잇는다 (바탕은 데스크톱 띠: 뒤 창·작업 표시줄). 글자 없이.
static class HomeScene
{
    const int FW = 480, FH = 300, TaskH = 30, K = 3; // 한 장 크기, 작업 표시줄, 배율
    static readonly Dictionary<string, int> PoseMs = new() { ["idle"] = 600, ["walk"] = 130, ["sleep"] = 800, ["happy"] = 220, ["groom"] = 200, ["think"] = 700, ["stretch"] = 400, ["hop"] = 130, ["look"] = 500 };

    sealed class Rig : IDisposable
    {
        public readonly Brain B;
        public readonly HomeArt Art = HomeArt.Embedded;
        public readonly Sprites Sp;
        public readonly HomeItem? Cushion, House;
        public Point CushionOrigin, HouseOrigin;
        public readonly Overlay Ov;
        public (double X, double Y, double T)? Reveal;
        public double RevealSize;
        public bool HideCushion, HideHouse, FlashCushion, FlashHouse;
        public int CushionDy, HouseDy;
        public readonly List<Rectangle> Back = new(); // 뒤에 열린 창들 (비면 기본 창 하나). 먼저 넣은 것이 뒤
        public HomeRoost? Roost;                      // 창 위에 놓인 물건 하나 (Step 이 창을 따라가고 떨어뜨린다)
        public bool RoostHouse;
        readonly int fh;
        readonly Rectangle area;
        public double Floor => area.Bottom - new Tuning().FloorGap;

        public Rig(string species, string stage, string? cushion, string? house, double cushionX, double houseX, double petX, double energy = 60, int frameH = FH)
        {
            fh = frameH;
            area = new Rectangle(0, 0, FW, fh - TaskH);
            Sp = Sprites.Drawn(PetArt.Embedded, species, null);
            B = new Brain(new Pet { Stage = stage, Full = 90, Mood = 90, Energy = energy }, new Tuning(), area) { Scale = K, X = petX, Y = Floor };
            B.NightNow = () => true;
            Cushion = cushion == null ? null : Art.Find(cushion);
            House = house == null ? null : Art.Find(house);
            double bcIdle = Center("idle"), bcSleep = Center("sleep");
            if (Cushion != null) { CushionOrigin = HomeGeom.Origin(Cushion, K, cushionX, Floor); B.Cushion = HomeGeom.Spot(Cushion, K, CushionOrigin, Floor, bcIdle, bcSleep, false); }
            if (House != null) { HouseOrigin = HomeGeom.Origin(House, K, houseX, Floor); B.House = HomeGeom.Spot(House, K, HouseOrigin, Floor, bcIdle, bcSleep, false); }
            Ov = new Overlay(B, () => Sp, new Rectangle(0, 0, FW, fh)) { Reveal = () => Reveal, RevealSize = () => RevealSize };
        }

        double Center(string pose) { string n = $"{B.P.Stage}/0/{pose}"; return Sp.Has(n) ? HomeArt.BodyCenter(Sp.Get(n, 0, 1)) : 20.5; }

        public void Step(double sec)
        {
            for (int i = 0, n = (int)Math.Round(sec * 60); i < n; i++)
            {
                if (Roost is { Active: true } r && (RoostHouse ? House : Cushion) is { } it && r.Tick(B.Now, 1 / 60.0, it, K) != RoostChange.None) Place(RoostHouse, r.CenterX, r.FloorY, r.Falling);
                B.Update(1 / 60.0, false);
            }
        }

        // 물건을 그 자리(그림 아래 가운데 x, 발 자리 y)로 옮기고 Brain 에 자리를 다시 넘긴다 (HomeLife.FollowRoost 와 같은 일)
        public void Place(bool house, double cx, double floorY, bool moving)
        {
            var it = house ? House : Cushion;
            if (it == null) return;
            var o = HomeGeom.Origin(it, K, cx, floorY);
            var spot = HomeGeom.Spot(it, K, o, floorY, Center("idle"), Center("sleep"), moving, Floor);
            if (house) (HouseOrigin, B.House) = (o, spot); else (CushionOrigin, B.Cushion) = (o, spot);
        }

        public Bitmap Frame()
        {
            var bmp = new Bitmap(FW, fh, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            Backdrop(g, fh, Back);
            bool onHouse = B.Mode == Mode.Home && B.HomeIsHouse, onCushion = B.Mode == Mode.Home && !B.HomeIsHouse;
            if (Cushion != null && !HideCushion) DrawItem(g, Cushion, CushionOrigin, CushionDy, onCushion, FlashCushion);
            if (House != null && !HideHouse) DrawItem(g, House, HouseOrigin, HouseDy, onHouse, FlashHouse);
            Ov.Render(g); // 말풍선 zzz·하트·반짝이
            DrawPet(g, onCushion ? Cushion : onHouse ? House : null, onCushion ? CushionOrigin : HouseOrigin);
            return bmp;
        }

        void DrawItem(Graphics g, HomeItem it, Point origin, int dy, bool covered, bool flash)
        {
            var body = Art.Body(it, K, covered);
            if (flash) HomeArt.DrawFlash(g, body, origin.X, origin.Y + dy);
            else g.DrawImageUnscaled(body, origin.X, origin.Y + dy);
        }

        void DrawPet(Graphics g, HomeItem? on, Point origin)
        {
            string name = B.SpriteName();
            int ms = PoseMs.GetValueOrDefault(B.Pose, 600), frame = (int)(B.Now * 1000 / ms);
            var st = g.Save();
            Region? clip = null;
            if (on != null && B.HomeClipNow != HomeClipKind.None)
            {
                clip = HomeGeom.Clip(Art, on, K, origin, B.HomeClipNow, B.Pose == "sleep", B.X, B.Y, K);
                g.SetClip(clip, CombineMode.Exclude);
            }
            g.DrawImageUnscaled(Sp.Get(name, frame, K, B.Dir < 0), (int)B.X - B.CellPx / 2, (int)B.Y - B.CellPx);
            g.Restore(st);
            clip?.Dispose();
        }

        public void Dispose() { Ov.Dispose(); }
    }

    static void Backdrop(Graphics g, int fh, List<Rectangle> back)
    {
        using (var sky = new LinearGradientBrush(new Rectangle(0, 0, FW, fh), Color.FromArgb(128, 148, 172), Color.FromArgb(96, 114, 140), 90f)) g.FillRectangle(sky, 0, 0, FW, fh);
        using var win = new SolidBrush(Color.FromArgb(240, 242, 246));
        using var bar = new SolidBrush(Color.FromArgb(96, 130, 190));
        using var edge = new Pen(Color.FromArgb(70, 80, 95));
        // 뒤에 열린 창 (활성 창이 아니라서 펫이 안 비킨다)
        foreach (var w in back.Count > 0 ? back : new List<Rectangle> { new(190, 18, 270, 150) })
        {
            g.FillRectangle(win, w); g.FillRectangle(bar, w.X, w.Y, w.Width, 14); g.DrawRectangle(edge, w);
        }
        using var task = new SolidBrush(Color.FromArgb(32, 36, 44));
        g.FillRectangle(task, 0, fh - TaskH, FW, TaskH);
    }

    static Bitmap Strip(List<Bitmap> frames, int rows = 1)
    {
        int cols = (frames.Count + rows - 1) / rows, fh = frames[0].Height;
        var sheet = new Bitmap(cols * FW + (cols + 1) * 6, rows * fh + (rows + 1) * 6);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(242, 243, 245));
        for (int i = 0; i < frames.Count; i++) g.DrawImageUnscaled(frames[i], 6 + i % cols * (FW + 6), 6 + i / cols * (fh + 6));
        foreach (var f in frames) f.Dispose();
        return sheet;
    }

    public static int Run(string[] args)
    {
        string scene = args[2];
        var frames = new List<Bitmap>();
        int rows = 1;
        switch (scene)
        {
            case "desk": Desk(frames); break;
            case "nap": Nap(frames); rows = 2; break;
            case "night": Night(frames); rows = 2; break;
            case "reveal": Reveal(frames); rows = 2; break;
            case "roost": Roost(frames); rows = 2; break;
            case "drop": Drop(frames); rows = 3; break;
            case "menu": Menu(args[1]); return 0;
            default: throw new ArgumentException("모르는 장면: " + scene);
        }
        using var sheet = Strip(frames, rows);
        sheet.Save(args[1]);
        return 0;
    }

    // 놓은 모습: 집 앞 빼꼼·캣타워 위에서 자기·방석에 앉기 세 가지 (서로 다른 종·물건)
    static void Desk(List<Bitmap> frames)
    {
        void One(string species, string stage, string cushion, string house, HomeUse use, bool houseSide)
        {
            using var r = new Rig(species, stage, cushion, house, 340, 130, 260);
            r.B.Windows = () => new List<Win>();
            if (!r.B.EnterHome(houseSide, use)) throw new Exception("들어가지 못했다 " + species);
            r.Step(.3);
            frames.Add(r.Frame());
        }
        One("shiba", "adult_playful", "round", "kennel", HomeUse.Peek, true);
        One("cat", "adult_sweet", "donut", "cattree", HomeUse.Sleep, true);
        One("penguin", "adult_chubby", "cloud", "igloo", HomeUse.Nap, false);
        One("hamster", "child", "leaf", "rounddoor", HomeUse.Nap, false);
        One("fox", "adult_sweet", "flower", "mushroom", HomeUse.Sleep, true);
        One("cat", "adult_grumpy", "futon", "box", HomeUse.Nap, false);
    }

    // 방석으로 걸어가 낮잠 (윗줄 시바 + 동그란 방석, 아랫줄 고양이 + 이불 요 세트) → 깨면 기지개
    static void Nap(List<Bitmap> frames)
    {
        foreach (var (sp, stage, cu) in new[] { ("shiba", "adult_playful", "round"), ("cat", "adult_sweet", "futon") })
        {
            using var r = new Rig(sp, stage, cu, null, 340, 0, 40);
            r.B.Windows = () => new List<Win>();
            r.B.Care("sleep"); // 기운 60 → 방석 낮잠 (창이 없으니 방석뿐)
            foreach (double t in new[] { .4, 1.2, 1.1, .9 }) { r.Step(t); frames.Add(r.Frame()); }
            r.Step(1.5);
            frames.Add(r.Frame());
            r.B.P.Energy = 100; r.B.P.Asleep = false; // 깼다
            r.Step(.6); frames.Add(r.Frame());
        }
    }

    // 밤에 집으로: 걸어가 → 문 앞에서 빼꼼 → 자리 잡고 잔다 → 깨서 내다보기 → 걸어 나오기
    static void Night(List<Bitmap> frames)
    {
        foreach (var (sp, stage, house) in new[] { ("shiba", "adult_playful", "kennel"), ("cat", "adult_sweet", "cattree") })
        {
            using var r = new Rig(sp, stage, null, house, 340, 340, 40);
            r.B.Windows = () => new List<Win>();
            r.B.GoHome(true, HomeUse.Sleep);
            r.Step(.5); frames.Add(r.Frame());
            for (int guard = 0; r.B.Mode == Mode.GoHome && Math.Abs(r.B.X - r.B.House.X) > 110 && guard < 600; guard++) r.Step(.05);
            frames.Add(r.Frame());
            for (int guard = 0; r.B.Mode == Mode.GoHome && guard < 600; guard++) r.Step(.05);
            r.Step(.5); frames.Add(r.Frame()); // 문 앞에서 빼꼼 (캣타워는 오르는 중)
            for (int guard = 0; !r.B.P.Asleep && guard < 600; guard++) r.Step(.05);
            r.Step(1.2); frames.Add(r.Frame()); // 잔다
            r.B.P.Energy = 100; r.B.P.Asleep = false;
            r.Step(.3); frames.Add(r.Frame()); // 깨서 내다본다
            r.Step(1.3); frames.Add(r.Frame()); // 걸어 나온다
        }
    }

    // 받는 순간 (7장): 반짝이 모임 → 하얗게 톡 떨어짐 → 통 → 퍼지는 반짝이 → 달려감 → 도착해서 기뻐하기
    static void Reveal(List<Bitmap> frames)
    {
        foreach (var (sp, stage, cu, house) in new[] { ("shiba", "adult_playful", (string?)null, (string?)"kennel"), ("hamster", "child", "leaf", null) })
        {
            using var r = new Rig(sp, stage, cu, house, 340, 340, 50);
            r.B.Windows = () => new List<Win>();
            var it = (house != null ? r.House : r.Cushion)!;
            var origin = house != null ? r.HouseOrigin : r.CushionOrigin;
            bool isHouse = house != null;
            double cx = origin.X + it.W * K / 2.0, cy = origin.Y + it.H * K / 2.0;
            r.RevealSize = Math.Max(it.W, it.H) * K;
            // 0.0: 놓일 쪽을 보고 깜짝
            r.B.Dir = cx > r.B.X ? 1 : -1;
            r.B.Emote("bang", .8);
            r.B.Greet("look", 1.2);
            (r.HideCushion, r.HideHouse) = (!isHouse, isHouse);
            double t0 = r.B.Now, last = 0;
            bool ran = false;
            foreach (double t in new[] { .15, .62, .78, 1.0, 1.2, 1.7, 2.3, 2.9 })
            {
                r.Step(t - last); last = t;
                double tt = r.B.Now - t0;
                r.Reveal = tt < 1.4 ? (cx, cy, tt) : null;
                bool placed = tt >= .5;
                int dy = placed ? (int)HomeGeom.DropDy(Math.Clamp((tt - .5) / .35, 0, 1), K) : 0;
                bool flash = tt >= .5 && tt < .75;
                if (isHouse) (r.HideHouse, r.HouseDy, r.FlashHouse) = (!placed, dy, flash); else (r.HideCushion, r.CushionDy, r.FlashCushion) = (!placed, dy, flash);
                if (tt >= 1.2 && !ran) { ran = true; r.B.GoHome(isHouse, isHouse ? HomeUse.Peek : HomeUse.Sit, fast: true, stay: isHouse ? 2 : 3, cheer: true); }
                if (t != 1.2) frames.Add(r.Frame());
            }
        }
    }

    // 창 위 방석·집 (윗줄 방석, 아랫줄 집): 바닥에서 밑까지 걸어가 → 올라가 → 안에서 쉬고 → 창을 옮기면 같이 → 창이 닫히면 떨어져 → 바닥에 내려앉는다
    static void Roost(List<Bitmap> frames)
    {
        const int H = 420;
        foreach (bool house in new[] { false, true })
        {
            var (sp, stage, cu, ho, energy) = house ? ("shiba", "adult_playful", (string?)null, (string?)"kennel", 80.0) : ("cat", "adult_sweet", "donut", null, 40.0);
            using var r = new Rig(sp, stage, cu, ho, 0, 0, 40, energy, H);
            var win = new Rectangle(170, 215, 300, 165);
            bool alive = true;
            var wh = new IntPtr(5);
            r.B.Windows = () => new List<Win> { new(wh, win, false) };
            r.B.Probe = _ => new WinState(alive, false, win);
            r.Back.Add(win);
            var it = house ? r.House! : r.Cushion!;
            r.Roost = new HomeRoost(r.B);
            r.RoostHouse = house;
            if (!r.Roost.Snap(it, K, win.Left + win.Width / 2.0 + 20, win.Top + HomeRoost.Perch(K))) throw new Exception("창 위에 못 놓았다 " + sp);
            r.Place(house, r.Roost.CenterX, r.Roost.FloorY, false);
            if (!(house ? r.B.GoHome(true, HomeUse.Peek, stay: 60) : r.B.GoHome(false, HomeUse.Nap))) throw new Exception("못 간다 " + sp);
            var spot = house ? r.B.House : r.B.Cushion;
            double gx = house ? spot.X : spot.SleepX;
            for (int guard = 0; r.B.Mode == Mode.GoHome && Math.Abs(r.B.X - gx) > 70 && guard < 900; guard++) r.Step(.05);
            frames.Add(r.Frame());                                                    // 바닥을 걸어 밑으로
            for (int guard = 0; r.B.Mode == Mode.GoHome && r.B.Y > r.Floor - 45 && guard < 900; guard++) r.Step(.05);
            frames.Add(r.Frame());                                                    // 곧게 올라간다
            for (int guard = 0; r.B.Mode == Mode.GoHome && guard < 900; guard++) r.Step(.05);
            r.Step(1.6); frames.Add(r.Frame());                                       // 안에서 쉰다
            win.Offset(-90, -30);
            r.Step(.3); frames.Add(r.Frame());                                        // 창을 옮기면 같이
            alive = false; win = new Rectangle(0, 0, 0, 0);
            r.Back.Clear();
            r.Step(.22); frames.Add(r.Frame());                                       // 창이 닫혔다: 떨어진다
            r.Step(1.6); frames.Add(r.Frame());                                       // 바닥에 내려앉았다
        }
    }

    // 들어 놓은 펫 (윗줄 방석에 앉기, 가운뎃줄 집 빼꼼, 아랫줄 기운 낮을 때 방석 낮잠): 들고 있다 → 놓자 들어감 → 머무는 중 → 8~12초 뒤 나옴
    static void Drop(List<Bitmap> frames)
    {
        foreach (var (sp, stage, cu, ho, energy) in new[] { ("hamster", "child", (string?)"leaf", (string?)null, 90.0), ("shiba", "adult_playful", null, "kennel", 90.0), ("cat", "adult_sweet", "futon", null, 40.0) })
        {
            bool house = ho != null;
            using var r = new Rig(sp, stage, cu, ho, 250, 250, 60, energy);
            r.B.Windows = () => new List<Win>();
            var it = (house ? r.House : r.Cushion)!;
            var origin = house ? r.HouseOrigin : r.CushionOrigin;
            int cx = origin.X + it.W * K / 2;
            r.B.Lift();
            r.B.Mouse = new Point(cx - 60, origin.Y - 30);
            r.Step(.15); frames.Add(r.Frame());                                       // 들고 있다 (위에서)
            r.B.Mouse = new Point(cx, origin.Y + it.H * K / 2);
            r.Step(.3); // 멈췄다 놓는다 (마지막 0.12초 속도로 던져지지 않게)
            r.B.Drop();                                                                // 손을 놓았다
            r.Step(.3); frames.Add(r.Frame());                                         // 들어가 있다
            r.Step(4.5); frames.Add(r.Frame());                                        // 머무는 중
            for (int guard = 0; r.B.Mode == Mode.Home && guard < 400; guard++) r.Step(.1);
            r.Step(.5); frames.Add(r.Frame());                                         // 나왔다
        }
    }

    // 메뉴 (진짜 메뉴 항목을 화면 밖 그림으로: 펫 메뉴의 「집·방석」 ▸ 방석 목록, 물건 메뉴)
    static void Menu(string path)
    {
        L.Use("ko");
        var art = HomeArt.Embedded;
        var b = new Brain(new Pet { Stage = "adult_sweet" }, new Tuning(), new Rectangle(0, 0, 1920, 1080));
        var sp = Sprites.Drawn(PetArt.Embedded, "cat", null);
        var home = new HomeLife(b, () => art, () => sp, () => "cat", _ => 3, () => false, () => false, () => { });
        foreach (var id in new[] { "donut", "basket", "futon", "kennel", "cattree", "box" }) home.Book.Receive(id);
        home.Book.Place(false, "basket"); home.Book.Place(true, "cattree");
        var pet = new HomeMenu.PetItem(home, () => art);
        using var menu = new ContextMenuStrip();
        menu.Items.Add("밥 주기 (3/5)"); menu.Items.Add("놀아주기"); menu.Items.Add("재우기");
        menu.Items.Add(pet.Root);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("설정..."); menu.Items.Add("종료");
        pet.Refresh(menu);
        using var sub = new ContextMenuStrip(); // 「집·방석」 을 펼친 모습
        sub.Items.Add(L.T("menu.home_cushion") + "  \u25B8"); sub.Items.Add(L.T("menu.home_house") + "  \u25B8");
        sub.Items.Add(new ToolStripSeparator());
        sub.Items.Add(L.T("menu.home_reset")); sub.Items.Add(new ToolStripMenuItem(L.T("menu.home_hide")) { Checked = true });
        using var cushions = HomeMenu.ItemMenu(home, art, false, 32);
        using var houses = HomeMenu.ItemMenu(home, art, true, 32);
        var shots = new List<Bitmap> { Capture(menu), Capture(sub), Capture(cushions), Capture(houses) };
        int w = shots.Sum(s => s.Width) + 14 * (shots.Count + 1), h = shots.Max(s => s.Height) + 28;
        using var sheet = new Bitmap(w, h);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(118, 138, 160));
            int x = 14;
            foreach (var s in shots) { g.DrawImageUnscaled(s, x, 14); x += s.Width + 14; s.Dispose(); }
        }
        sheet.Save(path);
    }

    static Bitmap Capture(ToolStripDropDown d)
    {
        d.PerformLayout();
        var size = d.PreferredSize;
        d.Size = size;
        var bmp = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height));
        d.CreateControl();
        d.DrawToBitmap(bmp, new Rectangle(Point.Empty, size));
        return bmp;
    }
}
