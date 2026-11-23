using System.Drawing.Drawing2D;

namespace Bolok;

// 방석·집 이음매 (docs/home.md 5·7장·추가 2): 물건 창 둘의 자리·배율·끌기·모니터·창 순서·숨기기, 줄 것 보기·받는 순간 연출,
// Brain 에 자리(HomeSpot)와 오릴 자리(HomeClip) 채우기, 디버그 명령. 화면 계산은 HomeGeom 에 모아 자가 검사·그려 보기가 같이 쓴다.
// 물건은 바탕화면 어디든 (x, y) 에 놓인다 — 놓은 모니터의 작업 영역 안에 통째로 들게만 눌러 준다 (HomeGeom.Fit). 창에 붙지 않는다
// Headless: 창을 안 만들고 같은 길을 돌린다 (자가 검사·장면 그리기가 끌기·창 순서까지 진짜 코드로 돌리게). Topmost·RaiseAbove·PetFollow 는 이음매
sealed class HomeLife : IDisposable
{
    // 물건 하나가 놓인 자리 (방석 하나·집 하나)
    public sealed class Slot
    {
        public bool House;
        public HomeForm? Form;
        public HomeItem? Item;
        public int K = 3;
        public double CenterX, FloorY;      // 그림 아래 가운데 x, 펫 발(seat 줄) y — 그림은 seat 줄이 이 y 에 오게 놓인다. 저장 칸의 [x, y] 가 이것
        public Point Origin;                // 그림 왼쪽 위 (화면 좌표). 끄는 동안은 마우스를 따른다
        public Rectangle Area;              // 놓인 모니터의 작업 영역
        public bool Shown, Moving, Covered;
        public int GrabX, GrabY;            // 끌 때 잡은 자리: 그림 가운데에서 마우스까지 가로, 그림 위에서 마우스까지 세로 (잡은 자리 그대로 따라온다)
        public IntPtr TestHandle;           // Headless 에서 창 순서를 기록할 때 쓰는 가짜 창 번호
        internal bool Vis;                  // Headless 에서 보이는 중인가
        public string? Id => Item?.Id;
    }

    public readonly HomeBook Book = new();
    public readonly Slot Cushion = new(), House = new() { House = true };
    readonly Slot[] all; // 프레임마다 돌 때 쓴다 (IEnumerable 로 돌면 열거자를 새로 잡는다)

    readonly Brain b;
    readonly Func<HomeArt> artOf;
    readonly Func<Sprites> spritesOf;
    readonly Func<string> speciesOf;
    readonly Func<Rectangle, int> scaleOf;
    readonly Func<bool> playing, afk;
    readonly Action save;
    readonly Random rng = new();
    HomeArt? artSeen;
    bool dirty = true;
    double nextSlow;
    string stageSeen = "", speciesSeen = "";
    int petScaleSeen;
    Sprites? bcSprites;
    string bcStage = "";
    double bcIdle = 20.5, bcSleep = 20.5;
    Region? clip;
    (HomeClipKind, int, string, int, Point, bool, int) clipKey;
    bool dragDirty, needRaise;
    double nextDragRaise;

    public bool Headless;                          // 창을 안 만든다 (자가 검사·그려 보기)
    public Action<IntPtr> Topmost = Native.Topmost; // 창 하나를 같은 항상 위 층의 맨 위로 (자가 검사는 기록으로 바꾼다)
    public Action? RaiseAbove;                     // 물건 위에 올릴 나머지: Overlay → 펫 순서 (PetForm 이 준다)
    public Action? PetFollow;                      // 물건을 끄는 동안 펫 창을 같은 프레임에 옮긴다 (PetForm 이 준다)
    public const double DragRaiseSec = .1;         // 끄는 동안 창 순서를 다시 거는 간격

    // 받는 순간 (7장): T0 부터 2.6초. 0.0 반짝이 모임 → 0.5 물건이 하얗게 톡 떨어짐 → 1.2 펫이 달려감
    sealed class RevealState { public double T0; public Slot Slot = null!; public bool Run; public bool Placed; }
    RevealState? reveal;
    public (double X, double Y, double T)? RevealNow
    {
        get
        {
            if (reveal is not { } r) return null;
            double t = b.Now - r.T0;
            if (t >= 1.4) return null;
            var s = r.Slot;
            return (s.Origin.X + s.Item!.W * s.K / 2.0, s.Origin.Y + s.Item.H * s.K / 2.0, t);
        }
    }
    public bool Revealing => reveal != null;
    public double RevealSize => reveal is { } r && r.Slot.Item is { } it ? Math.Max(it.W, it.H) * r.Slot.K : 0;

    public HomeLife(Brain b, Func<HomeArt> artOf, Func<Sprites> spritesOf, Func<string> speciesOf, Func<Rectangle, int> scaleOf, Func<bool> playing, Func<bool> afk, Action save)
    {
        (this.b, this.artOf, this.spritesOf, this.speciesOf, this.scaleOf, this.playing, this.afk, this.save) = (b, artOf, spritesOf, speciesOf, scaleOf, playing, afk, save);
        all = new[] { Cushion, House };
    }

    public IReadOnlyList<Slot> Slots => all;
    Slot SlotOf(bool house) => house ? House : Cushion;
    public bool Owns(IntPtr h) => all.Any(s => s.Form != null && s.Form.IsHandleCreated && s.Form.Handle == h);

    // ── 저장 ──
    // 놓인 자리 [x, y] 는 Book 이 들고 있고 (그림 아래 가운데 x, 펫 발 y), 놓을 때·모니터가 바뀔 때 갱신된다. 불러오면 모니터 안으로 눌러 준다 (Layout).
    // 바닥 자리만 있던 옛 저장 [x, 바닥 y] 도 같은 칸이라 그대로 읽힌다
    public void Load(HomeSave? s) { Book.Load(s); dirty = true; }

    public HomeSave Snapshot() => Book.Snapshot();

    // Book 을 밖에서 바꿨다 (디버그·자가 검사): 자리·그림·오림을 다음 프레임에 새로 잡는다
    public void Relayout() { dirty = true; clipKey = default; }
    public void NewPet() { Book.NewPet(); reveal = null; dirty = true; }

    // ── 자리 ──
    double FloorOf(Rectangle area) => area.Bottom - b.K.FloorGap;

    // 기본 자리 (그림 아래 가운데 x): 펫이 있는 모니터의 오른쪽 아래 — 집은 Area.Right − 집 폭/2 − 24·S, 방석은 그 왼쪽 (집이 아직 없어도 그 자리 — 나중에 집이 와도 안 겹친다)
    double DefaultX(bool house, Rectangle area)
    {
        int k = scaleOf(area);
        double s = k / 3.0, hw = (HouseWidth() * k) / 2.0;
        var art = artOf();
        double cw = (art.Find(Book.Cushion ?? "") is { } c ? c.W : 50) * k / 2.0;
        double x = house ? area.Right - hw - 24 * s : area.Right - hw * 2 - 24 * s - 16 * s - cw;
        double w = house ? hw : cw;
        return Math.Clamp(x, area.Left + w, area.Right - w);
    }

    int HouseWidth() => artOf().Find(Book.House ?? "") is { } h ? h.W : 62;

    void ApplyDefault(bool house)
    {
        var area = b.Area;
        double x = DefaultX(house, area), y = FloorOf(area);
        if (house) Book.HouseAt = new[] { x, y }; else Book.CushionAt = new[] { x, y };
    }

    // 물건 하나의 그림·배율·자리를 새로 잡는다 (물건이 바뀌었거나 모니터·배율이 바뀌었을 때만)
    void Layout(Slot s, bool want)
    {
        var art = artOf();
        var id = Book.Placed(s.House);
        var item = id == null ? null : art.Find(id);
        var at = s.House ? Book.HouseAt : Book.CushionAt;
        if (item != null && at == null) { ApplyDefault(s.House); at = s.House ? Book.HouseAt : Book.CushionAt; }
        s.Item = item;
        if (item == null || at == null) { s.Shown = false; s.Vis = false; s.Form?.Hide(); return; }
        if (!s.Moving)
        {
            var area = b.World.At(at[0], at[1] - 1);
            s.Area = area;
            s.K = scaleOf(area);
            (s.CenterX, s.FloorY) = HomeGeom.Fit(item, s.K, area, b.K.FloorGap, at[0], at[1]); // 어느 모니터에도 안 들거나 크기가 바뀌었으면 안으로
            s.Origin = HomeGeom.Origin(item, s.K, s.CenterX, s.FloorY);
        }
        if (!Headless) s.Form ??= MakeForm(s);
        s.Form?.Set(art, item, s.K, s.Origin, s.Covered);
        bool show = want && !(reveal is { } r && r.Slot == s && !r.Placed);
        if (show && !IsVisible(s)) { ShowForm(s); needRaise = true; } // 새로 뜬 창은 항상 위 층의 맨 위에 생기니 펫·Overlay 도 다시 그 위로 (Refresh 끝에서)
        else if (!show && IsVisible(s)) { if (Headless) s.Vis = false; else s.Form!.Hide(); }
        s.Shown = show && IsVisible(s);
    }

    bool IsVisible(Slot s) => Headless ? s.Vis : s.Form is { Visible: true };

    void ShowForm(Slot s)
    {
        if (Headless) { s.Vis = true; return; }
        s.Form!.Show();
        s.Form.Location = s.Origin;
    }

    HomeForm MakeForm(Slot s)
    {
        var f = new HomeForm(s.House) { Now = () => b.Now };
        f.Clicked = _ => Clicked(s);
        f.RightClicked = _ => HomeMenu.ForItem(this, s, f);
        f.Pressed = _ => Raise(); // 누르는 순간 창 순서를 한 번 더 건다 (눌러서 이 창이 위로 올라와도 펫이 그 위에 남게)
        f.DragStarted = _ => BeginDrag(s, f.GrabX, f.GrabY);
        f.DragMoved = (_, p) => DragTo(s, p);
        f.DragEnded = _ => EndDrag(s);
        return f;
    }

    // ── 끌기 (HomeForm 이 부르고, 자가 검사도 그대로 부른다) ──
    // 끌기 시작: grab = 누른 자리가 그림 가운데·위에서 얼마나 떨어졌나 (그 자리를 잡은 채로 따라온다)
    public void BeginDrag(Slot s, int grabX, int grabY)
    {
        if (s.Item == null) return;
        (s.GrabX, s.GrabY, s.Moving) = (grabX, grabY, true);
        dirty = true;
        Spots(); // 끄는 중임을 Brain 에 바로 알린다 (다른 행동이 이 물건을 목적지로 잡지 않게)
        nextDragRaise = b.Now + DragRaiseSec;
        Raise();
    }

    // 끄는 동안: 잡은 자리를 마우스에 맞춘다. 마우스가 있는 모니터의 배율로, 그 모니터 작업 영역 안에 그림이 통째로 들게 눌러서.
    // 안·위에 있는 펫은 같은 프레임에 같이 옮기고 (Brain.HomeFollow), 오림도 새로 잡고, 펫 창도 옮긴다 — 순서도 다시 건다
    public void DragTo(Slot s, Point p)
    {
        if (s.Item is not { } item || !s.Moving) return;
        var area = b.World.At(p.X, p.Y);
        int k = scaleOf(area);
        if (k != s.K) { s.GrabX = (int)Math.Round(s.GrabX * (double)k / s.K); s.GrabY = (int)Math.Round(s.GrabY * (double)k / s.K); s.K = k; }
        s.Area = area;
        double wantX = p.X - s.GrabX, wantY = p.Y - s.GrabY + item.Seat.Y * k;
        (s.CenterX, s.FloorY) = HomeGeom.Fit(item, k, area, b.K.FloorGap, wantX, wantY);
        s.Origin = HomeGeom.Origin(item, k, s.CenterX, s.FloorY);
        s.Form?.Set(artOf(), item, k, s.Origin, s.Covered);
        dragDirty = true;
        Spots();
        FollowPet();
        if (b.Now >= nextDragRaise) { nextDragRaise = b.Now + DragRaiseSec; Raise(); }
    }

    // 놓았다: 놓은 그 자리에 그대로 (모니터 안으로만 눌려 있다). 저장은 [x, y]
    public void EndDrag(Slot s)
    {
        if (!s.Moving) return;
        s.Moving = false;
        if (s.Item is not { } item) return;
        var at = new[] { s.CenterX, s.FloorY };
        if (s.House) Book.HouseAt = at; else Book.CushionAt = at;
        s.Form?.Set(artOf(), item, s.K, s.Origin, s.Covered);
        Dbg.Log("home-move", ("kind", s.House ? "house" : "cushion"), ("id", item.Id), ("x", (int)s.CenterX), ("y", (int)s.FloorY));
        dirty = true;
        Spots();
        FollowPet();
        Raise();
        save();
    }

    // 물건이 옮겨졌다: 안·위에 있는 펫을 같은 프레임에 따라가게 하고 (Brain), 오림을 새로 잡고, 펫 창을 옮긴다
    void FollowPet()
    {
        b.HomeFollow();
        UpdateClip(artOf());
        PetFollow?.Invoke();
    }

    // 왼쪽 클릭 (6장): 펫이 그 안·위에서 자면 깨우기, 다른 데 있고 한가하면 부르기 (와서 방석엔 4~6초 앉고 집엔 2초 빼꼼하고 나온다 — 수치 안 바뀜)
    void Clicked(Slot s)
    {
        if (b.Mode == Mode.Home && b.HomeIsHouse == s.House) { if (b.P.Asleep) b.Care("wake"); return; }
        if (b.P.Asleep || b.P.Stage is "egg" or "baby") return;
        if (s.House) b.GoHome(true, HomeUse.Peek, stay: 2);
        else b.GoHome(false, HomeUse.Sit, stay: 4 + rng.NextDouble() * 2);
    }

    // ── 매 프레임 ──
    public void Tick()
    {
        var art = artOf();
        if (!ReferenceEquals(art, artSeen)) { artSeen = art; dirty = true; bcSprites = null; }
        string stage = b.P.Stage, species = speciesOf();
        if (stage != stageSeen || species != speciesSeen || b.Scale != petScaleSeen) { (stageSeen, speciesSeen, petScaleSeen) = (stage, species, b.Scale); dirty = true; }

        bool want = !Book.Hidden && stage is not ("egg" or "baby") && !playing();
        Refresh(want); // 바뀐 게 있을 때만 다시 잡는다

        if (b.Now >= nextSlow)
        {
            nextSlow = b.Now + 1;
            Slow(want);
        }
        TickReveal();
        UpdateClip(art);
        foreach (var s in all) s.Form?.Tick();
    }

    bool wantSeen;

    void Refresh(bool want)
    {
        if (!dirty && want == wantSeen && !dragDirty) return;
        wantSeen = want;
        dirty = dragDirty = false;
        foreach (var s in all) Layout(s, want);
        Spots();
        if (needRaise) { needRaise = false; Raise(); }
    }

    // 1초에 한 번만 하는 일: 줄 것 보기, 모니터 배율 바뀜
    void Slow(bool want)
    {
        foreach (var s in all)
            if (s.Item != null && !s.Moving && scaleOf(b.World.At(s.CenterX, s.FloorY - 1)) != s.K) dirty = true; // 다른 배율의 모니터로 옮겨졌다
        if (reveal != null || Book.Hidden) return;
        if (!CanGive()) return;
        if (HomeBook.Owed(b.P.Stage, Book.CushionGiven, Book.HouseGiven) is { } kind) Give(kind == "house", null, true);
    }

    // 줄 수 있는 때: 한가하고(돌아다니는 중), 깨어 있고, 게임·자리 비움·떠나는 중이 아니고, 자라는 연출이 다 끝난 뒤 (연출 뒤 0.4초)
    bool CanGive() => b.Mode == Mode.Wander && !b.P.Asleep && !playing() && !afk() && !b.Growing && b.GrowT >= Brain.GrowSec + .4 && b.P.Stage is not ("egg" or "baby");

    // 받는다: id 가 없으면 종에 맞게 뽑는다. 받은 것은 연출 0.0 에 이미 저장 (연출 중에 게임·보스 키·들기가 끼어도 물건은 그대로 놓인다)
    public void Give(bool house, string? id, bool animate)
    {
        id ??= HomeBook.Pick(house, speciesOf(), Book.Got, rng);
        bool first = !Book.Has(id);
        Book.Place(house, id);
        if (house) { Book.HouseGiven = true; ApplyDefaultIfNone(true); } else { Book.CushionGiven = true; ApplyDefaultIfNone(false); }
        Dbg.Log("home-give", ("kind", house ? "house" : "cushion"), ("id", id), ("species", PetArt.Base(speciesOf())), ("first", first));
        save();
        var s = SlotOf(house);
        if (!animate || playing()) { dirty = true; return; }
        StartReveal(s);
    }

    void ApplyDefaultIfNone(bool house) { if ((house ? Book.HouseAt : Book.CushionAt) == null) ApplyDefault(house); }

    // 연출만 (받지 않는다)
    public void StartReveal(Slot s)
    {
        reveal = new RevealState { T0 = b.Now, Slot = s };
        dirty = true;
        Refresh(!Book.Hidden && b.P.Stage is not ("egg" or "baby") && !playing()); // 자리·그림을 잡는다 (연출이 시작됐으니 아직 안 보인다: 0.5초에 띄운다)
        if (s.Item == null) { reveal = null; return; }
        b.Dir = s.Origin.X + s.Item.W * s.K / 2 > b.X ? 1 : -1; // 놓일 쪽을 본다
        b.Emote("bang", .8);
        b.Greet("look", 1.2);
    }

    void TickReveal()
    {
        if (reveal is not { } r) return;
        double t = b.Now - r.T0;
        var s = r.Slot;
        if (playing() || Book.Hidden) { reveal = null; dirty = true; return; } // 게임·숨기기가 끼면 물건은 그냥 놓이고 뒷부분(달려가기)만 건너뛴다
        if (t >= .5 && !r.Placed)
        {
            r.Placed = true;
            dirty = true;
            Refresh(true);
            if (s.Form != null) s.Form.FlashUntil = b.Now + .25;
        }
        if (r.Placed && s.Form != null && s.Item is { } it)
        {
            double u = Math.Clamp((t - .5) / .35, 0, 1);
            double dy = HomeGeom.DropDy(u, s.K); // 4칸 위에서 떨어져 한 번 통 (−2칸 → 0)
            var p = new Point(s.Origin.X, s.Origin.Y + (int)dy);
            if (s.Form.Location != p) s.Form.Location = p;
        }
        if (t >= 1.2 && !r.Run)
        {
            r.Run = true;
            if (b.Mode is Mode.Wander or Mode.Anim) b.GoHome(s.House, s.House ? HomeUse.Peek : HomeUse.Sit, fast: true, stay: s.House ? 2 : 3, cheer: true); // 달려가 기뻐하고, 방석이면 3초 앉기·집이면 2초 빼꼼
        }
        if (t >= 2.6) { reveal = null; if (s.Item is { } ri) s.Form?.Set(artOf(), ri, s.K, s.Origin, s.Covered); }   // 연출 중에 물건이 지워졌으면 건너뛴다
    }

    // ── Brain 에 자리 넘기기 ──
    void Spots()
    {
        var sp = spritesOf();
        string stage = b.P.Stage;
        if (!ReferenceEquals(bcSprites, sp) || bcStage != stage)
        {
            (bcSprites, bcStage) = (sp, stage);
            bcIdle = Center(sp, stage, "idle");
            bcSleep = Center(sp, stage, "sleep");
        }
        foreach (var s in all)
        {
            var spot = new HomeSpot();
            if (s.Item is { } it && s.Shown && stage is not ("egg" or "baby"))
                spot = HomeGeom.Spot(it, s.K, s.Origin, s.FloorY, bcIdle, bcSleep, s.Moving);
            if (s.House) b.House = spot; else b.Cushion = spot;
        }
    }

    static double Center(Sprites sp, string stage, string pose)
    {
        string name = $"{stage}/0/{pose}";
        return sp.Has(name) ? HomeArt.BodyCenter(sp.Get(name, 0, 1)) : 20.5;
    }

    // ── 펫을 가릴 자리 (HomeClip) ──
    void UpdateClip(HomeArt art)
    {
        var kind = b.HomeClipNow;
        var s = SlotOf(b.HomeIsHouse);
        if (kind == HomeClipKind.None || s.Item is not { } it || !s.Shown)
        {
            if (clip != null) { clip.Dispose(); clip = null; b.HomeClip = null; clipKey = default; }
            foreach (var x in all) if (x.Covered) { x.Covered = false; if (x.Item is { } xi) x.Form?.Set(art, xi, x.K, x.Origin, false); }   // 물건이 지워진 채면 그릴 것이 없다
            return;
        }
        bool sleeping = b.Pose == "sleep";
        var key = (kind, b.HomeIsHouse ? 1 : 0, it.Id, s.K, s.Origin, sleeping, b.Scale);   // 펫 배율이 바뀌어도 다시 오린다
        if (key == clipKey && clip != null) return;
        clipKey = key;
        var old = clip;
        var r = HomeGeom.Clip(art, it, s.K, s.Origin, kind, sleeping, b.X, b.Y, b.Scale);
        clip = r;
        b.HomeClip = r;
        old?.Dispose();
        if (!s.Covered) { s.Covered = true; s.Form?.Set(art, it, s.K, s.Origin, true); } // 이불 요 세트: 펫이 있는 동안은 이불을 끌어올린 그림
    }

    // ── 창 순서·숨기기·화면 녹화 ──
    // 창 순서 (위 → 아래): 펫 → Overlay → 집·방석 → 다른 창. 아래부터 차례로 올린다: 물건 창들, 그 위에 RaiseAbove(Overlay → 펫).
    // 물건 창은 눌리거나·새로 뜨거나·끌려도 펫보다 위로 못 간다 — 물건을 올릴 때마다 반드시 그 위로 펫까지 다시 올린다
    public void Raise()
    {
        foreach (var s in all)
            if (s.Shown && HandleOf(s) is var h && h != IntPtr.Zero) Topmost(h);
        RaiseAbove?.Invoke();
    }

    IntPtr HandleOf(Slot s) => s.TestHandle != IntPtr.Zero ? s.TestHandle : s.Form is { IsHandleCreated: true } f ? f.Handle : IntPtr.Zero;

    public void HideFromCapture(bool on) { foreach (var s in all) if (s.Form is { IsHandleCreated: true } f) Native.HideFromCapture(f.Handle, on); }

    public void SetHidden(bool hidden) { Book.Hidden = hidden; dirty = true; save(); Dbg.Log("home-hide", ("hidden", hidden)); }

    // 메뉴: 놓인 것 바꾸기 (그 자리 그대로 그림만, 0.25초 흰 번쩍). 펫이 그 위·안에 있으면 그대로 있고 오림만 새로
    public void SetPlaced(bool house, string id)
    {
        if (Book.Placed(house) == id) return;
        Book.Place(house, id);
        if (house) Book.HouseGiven = true; else Book.CushionGiven = true;
        var s = SlotOf(house);
        // 폭이 달라도 아래 가운데 기준 (Layout 이 CenterX 를 그대로 쓴다)
        dirty = true;
        clipKey = default; // 오림을 새로
        Refresh(!Book.Hidden && b.P.Stage is not ("egg" or "baby") && !playing());
        if (s.Form != null) s.Form.FlashUntil = b.Now + .25;
        Dbg.Log("home-set", ("kind", house ? "house" : "cushion"), ("id", id));
        save();
    }

    // 「제자리로」: 기본 자리로 (화면 밖에 있어도 꺼내 올 수 있다)
    public void ResetSpot(bool? house)
    {
        foreach (var s in all)
        {
            if (house != null && s.House != house) continue;
            ApplyDefault(s.House);
        }
        dirty = true;
        clipKey = default;
        Dbg.Log("home-move", ("reset", house?.ToString() ?? "both"));
        save();
    }

    // 모니터가 바뀌었다 (뺐다·해상도): 자리가 어느 작업 영역에도 안 들면 가장 가까운 모니터 안으로 눌러 저장 칸에도 적고, 두 물건이 겹치면 방석을 옆으로
    public void Settle()
    {
        foreach (var s in all)
        {
            var at = s.House ? Book.HouseAt : Book.CushionAt;
            if (at == null || s.Item == null) continue;
            var area = b.World.At(at[0], at[1] - 1);
            var (x, y) = HomeGeom.Fit(s.Item, scaleOf(area), area, b.K.FloorGap, at[0], at[1]);
            if (Math.Abs(x - at[0]) > .5 || Math.Abs(y - at[1]) > .5) Dbg.Log("home-settle", ("kind", s.House ? "house" : "cushion"), ("x", (int)x), ("y", (int)y));
            if (s.House) Book.HouseAt = new[] { x, y }; else Book.CushionAt = new[] { x, y };
        }
        if (Cushion.Item is { } c && House.Item is { } h && Book.CushionAt is { } ca && Book.HouseAt is { } ha)
        {
            var area = b.World.At(ha[0], ha[1] - 1);
            int ck = scaleOf(b.World.At(ca[0], ca[1] - 1)), hk = scaleOf(area);
            double cw = c.W * ck / 2.0, hw = h.W * hk / 2.0;
            if (Math.Abs(ca[0] - ha[0]) < cw + hw && Math.Abs(ca[1] - ha[1]) < 2)
            {
                double left = ha[0] - hw - cw - 16, right = ha[0] + hw + cw + 16;
                double nx = left >= area.Left + cw ? left : Math.Min(right, area.Right - cw);
                var (fx, fy) = HomeGeom.Fit(c, ck, area, b.K.FloorGap, nx, ca[1]);
                Book.CushionAt = new[] { fx, fy };
            }
        }
        dirty = true;
        clipKey = default;
    }

    // ── 그림 ──
    // 디버그 shot: 화면 모양에서 가상 화면 원점(vx, vy)을 뺀 자리에 물건을 그린다
    public void DrawShot(Graphics g, int vx, int vy)
    {
        var art = artOf();
        foreach (var s in all)
            if (s.Item is { } it && s.Shown) g.DrawImageUnscaled(art.Body(it, s.K, s.Covered), s.Origin.X - vx, s.Origin.Y - vy);
    }

    // ── 디버그 명령 (run/cmd.txt) ──
    public void Command(string[] a)
    {
        string sub = a.Length > 1 ? a[1] : "state";
        switch (sub)
        {
            case "give":
                if (a.Length > 2 && a[2] == "all")
                {
                    foreach (var id in HomeBook.Cushions.Concat(HomeBook.Houses)) Book.Receive(id);
                    foreach (bool h in new[] { false, true }) if (Book.Placed(h) == null) { Book.Place(h, HomeBook.Pick(h, speciesOf(), new List<string>(), rng)); ApplyDefaultIfNone(h); }
                    (Book.CushionGiven, Book.HouseGiven) = (true, Pet.Grown(b.P.Stage) || Book.HouseGiven);
                    dirty = true; save();
                    Dbg.Log("home-give", ("all", true));
                    break;
                }
                {
                    string? kind = a.Length > 2 && a[2] is "cushion" or "house" ? a[2] : null;
                    string? id = a.Length > 3 ? a[3] : a.Length > 2 && kind == null ? a[2] : null;
                    bool house = kind != null ? kind == "house" : id != null ? HomeBook.IsHouse(id) : HomeBook.Owed(b.P.Stage, Book.CushionGiven, Book.HouseGiven) == "house";
                    Give(house, id != null && HomeBook.Known(id) ? id : null, true);
                }
                break;
            case "set":
                if (a.Length > 2 && HomeBook.Known(a[2])) SetPlaced(HomeBook.IsHouse(a[2]), a[2]);
                break;
            case "clear":
                Book.Clear();
                reveal = null;
                dirty = true; clipKey = default;
                save();
                Dbg.Log("home-clear");
                break;
            case "spot":
                if (a.Length > 2 && a[2] == "reset") { ResetSpot(null); break; }
                if (a.Length > 3 && a[2] is "cushion" or "house" && double.TryParse(a[3], out double x))
                {
                    // home spot <cushion|house> <x> [y]: 그 자리로 (y 를 안 주면 지금 y). 모니터 안으로 눌러 놓인다
                    bool h = a[2] == "house";
                    var at = h ? Book.HouseAt : Book.CushionAt;
                    double y = a.Length > 4 && double.TryParse(a[4], out double yy) ? yy : at?[1] ?? FloorOf(b.Area);
                    var arr = new[] { x, y };
                    if (h) Book.HouseAt = arr; else Book.CushionAt = arr;
                    dirty = true; clipKey = default; save();
                    Dbg.Log("home-move", ("kind", a[2]), ("x", (int)x), ("y", (int)y));
                    break;
                }
                foreach (var s in all)
                    Dbg.Log("home-spot", ("kind", s.House ? "house" : "cushion"), ("id", s.Id), ("shown", s.Shown), ("k", s.K), ("x", (int)s.CenterX), ("floor", (int)s.FloorY), ("origin", s.Origin.ToString()),
                        ("seat", s.Item?.Seat.ToString()), ("petX", (int)b.X), ("petY", (int)b.Y), ("mode", b.Mode.ToString()), ("use", b.AtHome.ToString()));
                break;
            case "sleep":
                if (b.P.Asleep) b.Care("wake");
                if (!(House.Shown && b.GoHome(true, HomeUse.Sleep)) && !b.GoHome(false, HomeUse.Nap)) Dbg.Log("home-none");
                break;
            case "nap": if (!b.GoHome(false, HomeUse.Nap)) Dbg.Log("home-none"); break;
            case "sit": if (!b.GoHome(false, HomeUse.Sit)) Dbg.Log("home-none"); break;
            case "out": b.HomeOut(); break;
            case "reveal":
                {
                    string? id = a.Length > 2 && HomeBook.Known(a[2]) ? a[2] : null;
                    bool house = id != null ? HomeBook.IsHouse(id) : Book.House != null && Pet.Grown(b.P.Stage);
                    var s = SlotOf(house);
                    if (id != null && Book.Placed(house) != id) SetPlaced(house, id);
                    ApplyDefaultIfNone(house);
                    StartReveal(s);
                }
                break;
            case "pick":
                {
                    string sp = a.Length > 2 ? a[2] : speciesOf();
                    int seed = a.Length > 3 ? int.Parse(a[3]) : 1, n = a.Length > 4 ? int.Parse(a[4]) : 1000;
                    foreach (bool house in new[] { true, false })
                    {
                        var r = new Random(seed);
                        var count = new Dictionary<string, int>();
                        for (int i = 0; i < n; i++) { string id = HomeBook.Pick(house, sp, new List<string>(), r); count[id] = count.GetValueOrDefault(id) + 1; }
                        Dbg.Log("home-pick", ("species", sp), ("family", HomeBook.FamilyOf(sp)), ("kind", house ? "house" : "cushion"), ("n", n), ("count", string.Join(" ", count.OrderByDescending(c => c.Value).Select(c => $"{c.Key}:{c.Value}"))));
                    }
                }
                break;
            case "hide": SetHidden(true); break;
            case "show": SetHidden(false); break;
            default:
                Dbg.Log("home-state", ("save", System.Text.Json.JsonSerializer.Serialize(Book.Snapshot())), ("revealing", Revealing));
                break;
        }
    }

    public void Dispose()
    {
        clip?.Dispose();
        foreach (var s in all) { s.Form?.Close(); s.Form?.Dispose(); }
    }
}

// 화면 계산 (창 없이): 그림 놓을 자리와 Brain 에 넘길 HomeSpot. HomeLife·그려 보기·자가 검사가 같이 쓴다
static class HomeGeom
{
    public const int ZX = 28, ZY = 0, ZW = 11, ZH = 10; // 잠자는 그림의 z: 펫 칸 x 28~38, y 0~10

    // 그림 왼쪽 위 (화면 좌표): 아래 가운데가 centerX, seat 줄(펫 칸 아래)이 floorY 에 오게
    public static Point Origin(HomeItem it, int k, double centerX, double floorY) => new((int)Math.Round(centerX) - it.W * k / 2, (int)Math.Round(floorY) - it.Seat.Y * k);

    // 받는 순간 물건이 떨어지는 높이 (픽셀, 위가 음수): u 0~1 — 4칸 위에서 떨어져 한 번 통 (−2칸 → 0)
    public static double DropDy(double u, int k) => u < .6 ? -4 * k * (1 - u / .6) : u < .8 ? -2 * k * Math.Sin((u - .6) / .2 * Math.PI) : 0;

    // 펫을 가릴 Region (화면 좌표, 부르는 쪽이 Dispose): 방석·캣타워 위는 앞 겹, 집 안은 문 안 말고 전부 + 앞 겹, 문 안에서 자도 z 는 문 밖에 보인다
    public static Region Clip(HomeArt art, HomeItem it, int k, Point origin, HomeClipKind kind, bool sleeping, double petX, double petY, int petScale)
    {
        var r = art.Clip(it, k, kind == HomeClipKind.Inside).Clone();
        int pk = petScale, cell = HomeArt.PetCellPx * pk;
        if (kind == HomeClipKind.Inside && sleeping) // 펫 칸의 x 28~38, y 0~10
            r.Exclude(new Rectangle((int)(petX - cell / 2.0) - origin.X + ZX * pk, (int)(petY - cell) - origin.Y + ZY * pk, ZW * pk, ZH * pk));
        r.Translate(origin.X, origin.Y);
        return r;
    }

    // 놓을 수 있는 자리로 맞춘다 (모니터 작업 영역 area 안): x·y 는 그림 아래 가운데 x, 펫 발(seat 줄) y.
    // ① 그림이 통째로 영역 안에 든다 ② 펫이 서 있는 자리가 영역 안이다 — 발이 바닥(영역 아래 − floorGap)보다 낮지 않고, 펫 머리가 영역 위로 안 나간다
    // (방석에 앉은 펫은 발이 2칸 올라가고, 캣타워 위에 선 펫은 top 줄까지 올라간다). 영역이 작아 안 맞으면 위·왼쪽을 우선한다
    public static (double X, double Y) Fit(HomeItem it, int k, Rectangle area, int floorGap, double x, double y)
    {
        int w = it.W * k, half = w / 2;
        double xl = area.Left + half, xh = area.Right - (w - half);
        int rise = it.Top is { } t ? (it.Seat.Y - t.Y) * k : it.House ? 0 : 2 * k;   // 펫이 seat 줄보다 올라가는 높이
        double yl = Math.Max(area.Top + it.Seat.Y * k, area.Top + HomeArt.PetCellPx * k + rise);
        double yh = Math.Min(area.Bottom - floorGap, area.Bottom - (it.H - it.Seat.Y) * k);
        return (Math.Clamp(x, xl, Math.Max(xl, xh)), Math.Clamp(y, yl, Math.Max(yl, yh)));
    }

    public static HomeSpot Spot(HomeItem it, int k, Point origin, double floorY, double bcIdle, double bcSleep, bool moving)
    {
        double PetX(double seatX, double bc) => origin.X + (HomeArt.PetCell(seatX, it.Seat.Y, bc).X + HomeArt.PetCellPx / 2.0) * k;
        var s = new HomeSpot
        {
            On = true, Moving = moving, X = PetX(it.Seat.X, bcIdle), SleepX = PetX(it.Seat.X, bcSleep),
            Bounds = new Rectangle(origin.X, origin.Y, it.W * k, it.H * k),
            Y = origin.Y + it.Seat.Y * k,
            SleepDy = it.House ? it.Sleep * k : 0,
            SitDy = it.House ? 0 : -2 * k,
        };
        if (it.Top is { } t)
        {
            s.HasTop = true;
            s.TopX = PetX(t.X, bcSleep);
            s.TopY = origin.Y + t.Y * k;
        }
        return s;
    }
}
