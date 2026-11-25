using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Bolok;

// 방석·집 장면 그려 보기 (화면을 띄우지 않는다): --home-scene 그림.png desk|nap|night|reveal|free|swap|drag|drop|menu
// 진짜 Brain·HomeGeom·Overlay 를 돌려 정해진 순간마다 한 장씩 뽑아 가로로 잇는다 (바탕은 데스크톱 띠: 뒤 창·작업 표시줄). 글자 없이.
// 화면에는 방석·집 중 하나만 놓인다 (추가 3) — 장면마다 물건은 하나
static class HomeScene
{
    const int FW = 480, FH = 300, TaskH = 30, K = 3; // 한 장 크기, 작업 표시줄, 배율
    static readonly Dictionary<string, int> PoseMs = new() { ["idle"] = 600, ["walk"] = 130, ["sleep"] = 800, ["happy"] = 220, ["groom"] = 200, ["think"] = 700, ["stretch"] = 400, ["hop"] = 130, ["look"] = 500 };

    sealed class Rig : IDisposable
    {
        public readonly Brain B;
        public readonly HomeArt Art = HomeArt.Embedded;
        public readonly Sprites Sp;
        public readonly HomeItem Item;                 // 놓인 하나 (방석이든 집이든)
        public readonly HomeItem? Old;                 // 받는 순간 바뀌어 빠지는 옛 물건 (같은 자리에서 새 물건이 톡 떨어질 때까지 보인다)
        public Point Origin, OldOrigin;
        public readonly Overlay Ov;
        public (double X, double Y, double T)? Reveal;
        public double RevealSize;
        public bool Hide, Flash;
        public int Dy;
        public readonly List<Rectangle> Back = new(); // 뒤에 열린 창들 (비면 기본 창 하나). 먼저 넣은 것이 뒤
        readonly int fh;
        readonly Rectangle area;
        public double Floor => area.Bottom - new Tuning().FloorGap;
        public bool IsHouse => Item.House;
        public HomeSpot Spot => IsHouse ? B.House : B.Cushion;

        public Rig(string species, string stage, string item, double itemX, double petX, double energy = 60, int frameH = FH, string? old = null)
        {
            fh = frameH;
            area = new Rectangle(0, 0, FW, fh - TaskH);
            Sp = Sprites.Drawn(PetArt.Embedded, species, null);
            B = new Brain(new Pet { Stage = stage, Full = 90, Mood = 90, Energy = energy }, new Tuning(), area) { Scale = K, X = petX, Y = Floor };
            B.NightNow = () => true;
            Item = Art.Find(item)!;
            double bcIdle = Center("idle"), bcSleep = Center("sleep");
            Origin = HomeGeom.Origin(Item, K, itemX, Floor);
            var spot = HomeGeom.Spot(Item, K, Origin, Floor, bcIdle, bcSleep, false);
            if (Item.House) B.House = spot; else B.Cushion = spot;
            if (old != null) { Old = Art.Find(old)!; OldOrigin = HomeGeom.Origin(Old, K, itemX, Floor); }
            Ov = new Overlay(B, () => Sp, new Rectangle(0, 0, FW, fh)) { Reveal = () => Reveal, RevealSize = () => RevealSize };
        }

        double Center(string pose) { string n = $"{B.P.Stage}/0/{pose}"; return Sp.Has(n) ? HomeArt.BodyCenter(Sp.Get(n, 0, 1)) : 20.5; }

        public void Step(double sec)
        {
            for (int i = 0, n = (int)Math.Round(sec * 60); i < n; i++) B.Update(1 / 60.0, false);
        }

        public Bitmap Frame()
        {
            var bmp = new Bitmap(FW, fh, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            Backdrop(g, fh, Back);
            bool on = B.Mode == Mode.Home;
            if (!Hide) DrawItem(g, Item, Origin, Dy, on, Flash);
            else if (Old != null) DrawItem(g, Old, OldOrigin, 0, false, false); // 새 물건이 떨어지기 전까지는 옛 물건이 그 자리에
            Ov.Render(g); // 말풍선 zzz·하트·반짝이
            DrawPet(g, on ? Item : null, Origin);
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
            case "night": Night(frames); rows = 3; break;
            case "reveal": Reveal(frames); rows = 2; break;
            case "free": Free(frames); rows = 2; break;
            case "swap": Swap(frames); rows = 2; break;
            case "drag": Drag(frames); rows = 3; break;
            case "drop": Drop(frames); rows = 3; break;
            case "menu": Menu(args[1]); return 0;
            default: throw new ArgumentException("모르는 장면: " + scene);
        }
        using var sheet = Strip(frames, rows);
        sheet.Save(args[1]);
        return 0;
    }

    // 놓은 모습: 집 앞 빼꼼·캣타워 위에서 자기·방석에 앉기·방석 낮잠 (서로 다른 종·물건, 한 장면에 물건은 하나)
    static void Desk(List<Bitmap> frames)
    {
        void One(string species, string stage, string item, HomeUse use)
        {
            using var r = new Rig(species, stage, item, 240, 260);
            r.B.Windows = () => new List<Win>();
            if (!r.B.EnterHome(r.IsHouse, use)) throw new Exception("들어가지 못했다 " + species);
            r.Step(.3);
            frames.Add(r.Frame());
        }
        One("shiba", "adult_playful", "kennel", HomeUse.Peek);
        One("cat", "adult_sweet", "cattree", HomeUse.Sleep);
        One("penguin", "adult_chubby", "cloud", HomeUse.Nap);
        One("hamster", "child", "leaf", HomeUse.Nap);
        One("fox", "adult_sweet", "mushroom", HomeUse.Sleep);
        One("cat", "adult_grumpy", "futon", HomeUse.Nap);
    }

    // 방석으로 걸어가 낮잠 (윗줄 시바 + 동그란 방석, 아랫줄 고양이 + 이불 요 세트) → 깨면 기지개
    static void Nap(List<Bitmap> frames)
    {
        foreach (var (sp, stage, cu) in new[] { ("shiba", "adult_playful", "round"), ("cat", "adult_sweet", "futon") })
        {
            using var r = new Rig(sp, stage, cu, 340, 40);
            r.B.Windows = () => new List<Win>();
            r.B.Care("sleep"); // 기운 60 → 방석 낮잠 (창이 없으니 방석뿐)
            foreach (double t in new[] { .4, 1.2, 1.1, .9 }) { r.Step(t); frames.Add(r.Frame()); }
            r.Step(1.5);
            frames.Add(r.Frame());
            r.B.P.Energy = 100; r.B.P.Asleep = false; // 깼다
            r.Step(.6); frames.Add(r.Frame());
        }
    }

    // 밤에 놓인 하나로: 걸어가 → 문 앞에서 빼꼼 (방석이면 바로 올라앉아) → 자리 잡고 잔다 → 깨서 내다보기·기지개 → 걸어 나오기. 윗줄 집 둘, 아랫줄 방석 (밤 잠도 방석에서)
    static void Night(List<Bitmap> frames)
    {
        foreach (var (sp, stage, item) in new[] { ("shiba", "adult_playful", "kennel"), ("cat", "adult_sweet", "cattree"), ("hamster", "child", "leaf") })
        {
            using var r = new Rig(sp, stage, item, 340, 40);
            r.B.Windows = () => new List<Win>();
            r.B.GoSleepHome();
            r.Step(.5); frames.Add(r.Frame());
            for (int guard = 0; r.B.Mode == Mode.GoHome && Math.Abs(r.B.X - r.Spot.X) > 110 && guard < 600; guard++) r.Step(.05);
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

    // 받는 순간 (7장): 반짝이 모임 → 하얗게 톡 떨어짐 → 통 → 퍼지는 반짝이 → 달려감 → 도착해서 기뻐하기.
    // 윗줄 어른이 집을 받는다 (옛 방석이 그 자리에 있다가 집이 떨어지는 순간 바뀐다), 아랫줄 어린이가 방석을 받는다
    static void Reveal(List<Bitmap> frames)
    {
        foreach (var (sp, stage, item, old) in new[] { ("shiba", "adult_playful", "kennel", (string?)"round"), ("hamster", "child", "leaf", null) })
        {
            using var r = new Rig(sp, stage, item, 340, 50, old: old);
            r.B.Windows = () => new List<Win>();
            var it = r.Item;
            var origin = r.Origin;
            bool isHouse = r.IsHouse;
            double cx = origin.X + it.W * K / 2.0, cy = origin.Y + it.H * K / 2.0;
            r.RevealSize = Math.Max(it.W, it.H) * K;
            // 0.0: 놓일 쪽을 보고 깜짝
            r.B.Dir = cx > r.B.X ? 1 : -1;
            r.B.Emote("bang", .8);
            r.B.Greet("look", 1.2);
            r.Hide = true;
            var spot = isHouse ? r.B.House : r.B.Cushion;
            if (isHouse) r.B.House = default; else r.B.Cushion = default; // 아직 안 놓였다 (떨어진 뒤에 켠다)
            double t0 = r.B.Now, last = 0;
            bool ran = false;
            foreach (double t in new[] { .15, .62, .78, 1.0, 1.2, 1.7, 2.3, 2.9 })
            {
                r.Step(t - last); last = t;
                double tt = r.B.Now - t0;
                r.Reveal = tt < 1.4 ? (cx, cy, tt) : null;
                bool placed = tt >= .5;
                if (placed && r.Hide) { r.Hide = false; if (isHouse) r.B.House = spot; else r.B.Cushion = spot; }
                r.Dy = placed ? (int)HomeGeom.DropDy(Math.Clamp((tt - .5) / .35, 0, 1), K) : 0;
                r.Flash = tt >= .5 && tt < .75;
                if (tt >= 1.2 && !ran) { ran = true; r.B.GoHome(isHouse, isHouse ? HomeUse.Peek : HomeUse.Sit, fast: true, stay: isHouse ? 2 : 3, cheer: true); }
                if (t != 1.2) frames.Add(r.Frame());
            }
        }
    }

    // 진짜 HomeLife(창 없이)로 돌리는 장면: 물건 놓기·끌기·모니터 안으로 눌리기·펫 따라가기·오림이 모두 앱과 같은 코드다
    sealed class Live : IDisposable
    {
        public readonly Brain B;
        public readonly HomeLife H;
        public readonly Sprites Sp;
        public readonly Overlay Ov;
        public Point? Cursor;                          // 마우스 (작은 화살표로 그린다)
        readonly int fh;
        public double Floor => B.Area.Bottom - B.K.FloorGap;

        // item·at: 놓인 하나와 그 자리 (그림 아래 가운데 x, 펫 발 y). 둘 중 하나만 놓인다
        public Live(string species, string stage, string item, (double X, double Y) at, double petX, double petY, double energy = 60, int frameH = FH)
        {
            fh = frameH;
            var area = new Rectangle(0, 0, FW, fh - TaskH);
            Sp = Sprites.Drawn(PetArt.Embedded, species, null);
            B = new Brain(new Pet { Stage = stage, Full = 90, Mood = 90, Energy = energy }, new Tuning(), area) { Scale = K, X = petX, Y = petY };
            B.NightNow = () => true;
            B.Windows = () => new List<Win>();
            H = new HomeLife(B, () => HomeArt.Embedded, () => Sp, () => species, _ => K, () => false, () => false, () => { }) { Headless = true };
            H.Book.Place(item);
            H.Book.At = new[] { at.X, at.Y };
            (H.Book.CushionGiven, H.Book.HouseGiven) = (true, true);
            Ov = new Overlay(B, () => Sp, new Rectangle(0, 0, FW, fh));
            Step(.02);
        }

        public HomeLife.Slot Slot(bool house) => house ? H.House : H.Cushion;

        public void Step(double sec)
        {
            for (int i = 0, n = Math.Max(1, (int)Math.Round(sec * 60)); i < n; i++) { B.Update(1 / 60.0, false); H.Tick(); }
        }

        public Bitmap Frame()
        {
            var bmp = new Bitmap(FW, fh, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            Backdrop(g, fh, new List<Rectangle>());
            H.DrawShot(g, 0, 0);
            Ov.Render(g);
            string name = B.SpriteName();
            int ms = PoseMs.GetValueOrDefault(B.Pose, 600), frame = (int)(B.Now * 1000 / ms);
            var st = g.Save();
            if (B.HomeClip is { } clip) g.SetClip(clip, CombineMode.Exclude);
            g.DrawImageUnscaled(Sp.Get(name, frame, K, B.Dir < 0), (int)B.X - B.CellPx / 2, (int)B.Y - B.CellPx);
            g.Restore(st);
            if (Cursor is { } m) // 마우스 화살표 (글자 없음)
            {
                var arrow = new[] { new Point(m.X, m.Y), new Point(m.X, m.Y + 22), new Point(m.X + 6, m.Y + 17), new Point(m.X + 11, m.Y + 27), new Point(m.X + 15, m.Y + 25), new Point(m.X + 10, m.Y + 15), new Point(m.X + 17, m.Y + 15) };
                g.FillPolygon(Brushes.White, arrow);
                g.DrawPolygon(Pens.Black, arrow);
            }
            return bmp;
        }

        public void Dispose() { Ov.Dispose(); H.Dispose(); }
    }

    // 바탕화면 아무 데나 놓은 방석·집 (여섯 곳): 위·가운데·아래·왼쪽·오른쪽 — 창 윗변이 아니라 놓은 그 자리, 펫이 거기서 앉기·낮잠·빼꼼·잠·캣타워 위
    static void Free(List<Bitmap> frames)
    {
        const int H = 430;
        void One(string species, string stage, string item, (double, double) at, HomeUse use, double petX, double petY)
        {
            using var r = new Live(species, stage, item, at, petX, petY, use is HomeUse.Nap or HomeUse.Sleep ? 40 : 90, H);
            if (!r.B.EnterHome(HomeBook.IsHouse(item), use)) throw new Exception("들어가지 못했다 " + species);
            r.Step(.6);
            frames.Add(r.Frame());
        }
        One("cat", "adult_sweet", "donut", (110, 190), HomeUse.Sit, 300, 330);          // 왼쪽 위 방석에 앉기
        One("shiba", "adult_playful", "kennel", (360, 330), HomeUse.Peek, 100, 330);    // 오른쪽 아래 집 빼꼼
        One("cat", "adult_sweet", "cattree", (240, 300), HomeUse.Sleep, 100, 330);      // 가운데 캣타워 위에서 잠
        One("hamster", "child", "leaf", (380, 170), HomeUse.Nap, 100, 330);             // 오른쪽 위 방석 낮잠
        One("penguin", "adult_chubby", "igloo", (130, 260), HomeUse.Sleep, 400, 330);   // 왼쪽 가운데 이글루에서 잠
        One("fox", "adult_sweet", "flower", (350, 250), HomeUse.Sit, 100, 330);         // 오른쪽 가운데 방석에 앉기
    }

    // 방석 ↔ 집 바꾸기 (추가 3, 메뉴에서 고르는 순간): 같은 자리에서 하나만 남는다. 윗줄 앉은 채 → 집으로 (옮겨 빼꼼) → 다시 방석으로,
    // 아랫줄 방석에서 자던 채 → 집으로 (옮겨 자는 채) → 다시 방석으로
    static void Swap(List<Bitmap> frames)
    {
        const int H = 430;
        foreach (var (sp, stage, from, to, use) in new[] { ("cat", "adult_sweet", "donut", "kennel", HomeUse.Sit), ("shiba", "adult_playful", "round", "box", HomeUse.Nap) })
        {
            using var r = new Live(sp, stage, from, (260, 330), 100, 330, use == HomeUse.Nap ? 40 : 90, H);
            if (!r.B.EnterHome(false, use)) throw new Exception("들어가지 못했다 " + sp);
            r.Step(1.2); frames.Add(r.Frame());              // 방석 위 (앉아 있거나 잔다)
            r.H.SetPlaced(to); r.Step(.12); frames.Add(r.Frame());   // 집으로 바꾼 순간 (흰 번쩍)
            r.Step(1.3); frames.Add(r.Frame());              // 집 안에서
            r.H.SetPlaced(from); r.Step(.12); frames.Add(r.Frame()); // 다시 방석으로
            r.Step(1.3); frames.Add(r.Frame());              // 방석 위에서
        }
    }

    // 끌기 (윗줄 방석에 앉은 고양이, 가운뎃줄 집에서 자는 시바, 아랫줄 캣타워 위에서 자는 고양이): 가만히 → 끄는 중 → 더 끄는 중 → 놓았다 (펫이 물건과 같이 가고 물건 위에 그대로)
    static void Drag(List<Bitmap> frames)
    {
        const int H = 430;
        void One(string species, string stage, string item, HomeUse use, (double, double) at, (int X, int Y)[] path)
        {
            bool useHouse = HomeBook.IsHouse(item);
            using var r = new Live(species, stage, item, at, 100, 330, use is HomeUse.Nap or HomeUse.Sleep ? 40 : 90, H);
            if (!r.B.EnterHome(useHouse, use)) throw new Exception("들어가지 못했다 " + species);
            r.Step(1.2);
            var s = r.Slot(useHouse);
            var first = path[0];
            r.Cursor = new Point(first.X, first.Y);
            frames.Add(r.Frame());                                                      // 가만히 (마우스를 물건 위에 눌렀다)
            r.H.BeginDrag(s, first.X - (s.Origin.X + s.Item!.W * K / 2), first.Y - s.Origin.Y);
            for (int i = 1; i < path.Length; i++)
            {
                for (int j = 1; j <= 6; j++) // 한 점에서 다음 점까지 여섯 걸음
                {
                    var p = new Point(path[i - 1].X + (path[i].X - path[i - 1].X) * j / 6, path[i - 1].Y + (path[i].Y - path[i - 1].Y) * j / 6);
                    r.Cursor = p;
                    r.H.DragTo(s, p);
                    r.Step(.016);
                }
                if (i < path.Length - 1) frames.Add(r.Frame());                         // 끄는 중
            }
            r.H.EndDrag(s);
            r.Cursor = null;
            r.Step(.5);
            frames.Add(r.Frame());                                                      // 놓았다
        }
        One("cat", "adult_sweet", "donut", HomeUse.Sit, (110, 200), new[] { (110, 190), (200, 120), (300, 110), (360, 160) });
        One("shiba", "adult_playful", "kennel", HomeUse.Sleep, (120, 340), new[] { (120, 300), (200, 240), (300, 200), (370, 260) });
        One("cat", "adult_sweet", "cattree", HomeUse.Sleep, (360, 340), new[] { (360, 250), (300, 200), (200, 220), (130, 280) });
    }

    // 들어 놓은 펫 (윗줄 방석에 앉기, 가운뎃줄 집 빼꼼, 아랫줄 기운 낮을 때 방석 낮잠): 들고 있다 → 놓자 들어감 → 머무는 중 → 8~12초 뒤 나옴
    static void Drop(List<Bitmap> frames)
    {
        foreach (var (sp, stage, item, energy) in new[] { ("hamster", "child", "leaf", 90.0), ("shiba", "adult_playful", "kennel", 90.0), ("cat", "adult_sweet", "futon", 40.0) })
        {
            using var r = new Rig(sp, stage, item, 250, 60, energy);
            r.B.Windows = () => new List<Win>();
            var it = r.Item;
            var origin = r.Origin;
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
        home.Book.Place("cattree"); // 놓인 하나 (집). 체크는 이 하나에만 — 방석 목록에는 체크가 없다
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
        using var item = HomeMenu.ItemMenu(home, art, 32); // 집을 오른쪽 클릭: 받은 방석·받은 집이 다 나온다
        var shots = new List<Bitmap> { Capture(menu), Capture(sub), Capture(item) };
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
