using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace Bolok;

// 도감에 보여줄 것: 지금 키우는 아이, 만나 본 종, 자연으로 떠난 친구들
public sealed record ReleasedPet(string Species, string Stage, double AgeMin, DateTime At);
public sealed record PetBook(string Species, bool IsEgg, IReadOnlyCollection<string> Met, IReadOnlyList<ReleasedPet> Released,
    IReadOnlyDictionary<string, DateTime>? Visits = null) // Visits: 떠난 친구가 마지막으로 놀러 온 때
{
    public IReadOnlyList<GiftRecord> Gifts { get; init; } = Array.Empty<GiftRecord>();
    public IReadOnlyList<PhotoRecord> Album { get; init; } = Array.Empty<PhotoRecord>();
    public IReadOnlyList<CardRecord> Cards { get; init; } = Array.Empty<CardRecord>();
    public IReadOnlyDictionary<string, int>? VisitCounts { get; init; } // 우정 하트
}

// 상태 탭에 보여줄 것 (1초마다 새로 받는다)
public sealed record GameStat(string Name, string Value, bool Played);
public sealed record PetStatus(string Species, string Stage, double AgeMin, double Full, double Mood, double Energy, bool Asleep, string? Need,
    int Miss, int Bites, int BitesTotal, double? GrowIn, double GrowFrac, string? GrowTo, IReadOnlyList<GameStat> Games);

// 설정 창. patio 설정 창을 따른다: 왼쪽 목록(맨 위 볼록이 얼굴) + 오른쪽 제목과 묶음 카드
// 상태 / 도감(나올 수 있는 친구들, 풀어주기) / 일반(게임 소리, 노는 법) / 업데이트(버전, 직접 받기, 자동 업데이트 — 기본 꺼짐)
public sealed class Settings : Form
{
    public const string ReleasesUrl = "https://github.com/" + Updater.Repo + "/releases/latest";
    public static readonly string[] Pages = { "status", "book", "memories", "general", "update" }; // 이름은 page.<id> (언어별)

    readonly Func<Sprites> spritesOf;
    readonly Func<PetStatus> statusOf;
    readonly Dictionary<string, (Bitmap One, Bitmap Two, Bitmap Faded)> thumbs = new();
    readonly Dictionary<string, Rectangle> crops = new(); // 초상화: 그림마다 실제로 그려진 부분
    readonly System.Windows.Forms.Timer tick = new() { Interval = 500 };
    PetBook book;
    bool soundOn, autoOn;
    string page = Pages[0];
    int frame;

    NavList nav = null!;
    Header header = null!;
    readonly Dictionary<string, Stack> stacks = new();
    // 상태
    Hero hero = null!;
    Meter full = null!, mood = null!, energy = null!;
    Card care = null!, games = null!;
    Card.Item missItem = null!, bitesItem = null!;
    TextBlock careFoot = null!;
    // 도감
    Card nowCard = null!, goneCard = null!;
    Card.Item nowItem = null!;
    BookGrid grid = null!;
    TextBlock bookTitle = null!, goneFoot = null!;
    // 일반·업데이트
    Switch sound = null!, auto = null!;
    Card.Item checkItem = null!, readyItem = null!;
    Btn check = null!;

    public bool Sound => soundOn;
    public bool AutoUpdate => autoOn;
    public event Action? Changed;
    public event Action? RestartAsked;
    public Func<bool>? ReleaseAsked; // 풀어주기를 확인했을 때. 지금 풀어줄 수 없으면 false
    public Action<string>? LanguageChosen;

    public Settings(bool soundOn, bool autoUpdate, Func<Sprites> spritesOf, Func<PetStatus> statusOf, PetBook book)
    {
        (this.soundOn, autoOn, this.spritesOf, this.statusOf, this.book) = (soundOn, autoUpdate, spritesOf, statusOf, book);
        Text = L.T("set.title");
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Ui.Bg;
        KeyPreview = true;
        TopMost = true;

        var sp = spritesOf();
        foreach (var id in sp.SpeciesList)
        {
            var two = new Bitmap(sp.Get("thumb/" + id, 0, 2));
            thumbs[id] = (new Bitmap(sp.Get("thumb/" + id, 0, 1)), two, Ui.Faded(two));
        }

        Ui.K = DeviceDpi / 96f;
        Build();
        tick.Tick += (_, _) => { frame++; UpdateStatus(); };
        tick.Start();
        L.Changed += Relang;
        FormClosed += (_, _) =>
        {
            L.Changed -= Relang;
            foreach (var im in memImages) im.Dispose();
            tick.Dispose();
            foreach (var (a, b, c) in thumbs.Values) { a.Dispose(); b.Dispose(); c.Dispose(); }
        };
    }

    void Relang() { Text = L.T("set.title"); Build(); } // 언어가 바뀌면 통째로 다시 짠다

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.DarkTitle(this, Ui.Bg);
        if (Math.Abs(DeviceDpi / 96f - Ui.K) > .01f) { Ui.K = DeviceDpi / 96f; Build(); } // 다른 배율 모니터에서 열렸으면 다시 짠다
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Ui.K = e.DeviceDpiNew / 96f;
        Build();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Close();
        base.OnKeyDown(e);
    }

    // ── 창 짜기 ──
    void Build()
    {
        SuspendLayout();
        foreach (var c in Controls.Cast<Control>().ToList()) c.Dispose();
        Controls.Clear();
        stacks.Clear();
        ClientSize = new Size(Ui.D(800), Ui.D(600));

        nav = new NavList(new()
        {
            new("status", L.T("page.status"), Ui.Pink, Glyphs.Heart),
            new("book", L.T("page.book"), Ui.Orange, Glyphs.Paw),
            new("memories", L.T("page.memories"), Ui.Indigo, Glyphs.Star),
            new("general", L.T("page.general"), Ui.Gray, Glyphs.Gear),
            new("update", L.T("page.update"), Ui.Blue, Glyphs.Download),
        }, page) { Foot = L.T("set.foot", VersionText), Name1 = L.Name };
        nav.Picked += ShowPage;
        header = new Header();
        stacks["status"] = StatusPage();
        stacks["book"] = BookPage();
        stacks["memories"] = new Stack(); // 내용은 UpdateBook 이 채운다
        stacks["general"] = GeneralPage();
        stacks["update"] = UpdatePage();

        int side = Ui.D(210), top = Ui.D(52);
        nav.Bounds = new Rectangle(0, 0, side, ClientSize.Height);
        header.Bounds = new Rectangle(side, 0, ClientSize.Width - side, top);
        Controls.Add(nav);
        Controls.Add(header);
        foreach (var s in stacks.Values)
        {
            s.Visible = false;
            s.Bounds = new Rectangle(side, top, ClientSize.Width - side, ClientSize.Height - top);
            Controls.Add(s);
        }
        UpdateBook(book);
        ShowPage(page);
        ResumeLayout();
    }

    public void SelectTab(string name) => ShowPage(name);
    internal void ScrollToEnd() => stacks[page].ScrollTo(int.MaxValue); // 그림 확인용

    void ShowPage(string id)
    {
        if (!stacks.ContainsKey(id)) return;
        page = id;
        nav.Active = id;
        header.Text = L.T("page." + id);
        foreach (var (k, s) in stacks) s.Visible = k == id;
        UpdateStatus();
        stacks[id].Relayout();
    }

    // ── 상태 ──
    Stack StatusPage()
    {
        var s = new Stack();
        hero = new Hero();
        s.Group(null).AddBody(hero, 16);

        var cond = s.Group(L.T("st.condition"), L.T("st.condition_foot"));
        cond.Add(L.T("st.full"), L.T("st.full_desc"), full = new Meter(Ui.Orange));
        cond.Add(L.T("st.mood"), L.T("st.mood_desc"), mood = new Meter(Ui.Pink));
        cond.Add(L.T("st.energy"), L.T("st.energy_desc"), energy = new Meter(Ui.Ok));

        care = s.Group(L.T("st.care"), "", out _, out var foot);
        careFoot = foot!;
        missItem = care.Add(L.T("st.miss"), L.T("st.miss_desc"), value: L.T("st.miss_value", 0));
        bitesItem = care.Add(L.T("st.bites"), L.T("st.bites_desc", 0), value: L.T("st.bites_value", 0));

        games = s.Group(L.T("st.games"));
        foreach (var gs in statusOf().Games) games.Add(gs.Name, value: gs.Value);
        return s;
    }

    void UpdateStatus()
    {
        var st = statusOf();
        var sp = spritesOf();
        bool egg = st.Stage == "egg";
        string name = egg ? L.Stage("egg") : L.Species(st.Species);

        // 왼쪽 위 얼굴은 어느 페이지에서나
        var face = egg ? sp.Get("egg/idle", 0, 1) : thumbs.TryGetValue(st.Species, out var t) ? t.One : null;
        string line2 = egg ? L.T("st.unknown") : $"{name} · {Names.Form(st.Stage)}";
        if (nav.Face != face || nav.Name2 != line2) { (nav.Face, nav.Name2) = (face, line2); nav.Invalidate(); }
        if (page == "book" && !book.IsEgg && nowItem.Right is Btn && nowItem.Label != line2) { nowItem.Label = line2; nowCard.Invalidate(); } // 자라면 이름 옆 모습도
        if (page != "status") return;

        string pose = $"{st.Stage}/0/{(st.Asleep ? "sleep" : "idle")}";
        string key = egg ? "egg/idle" : sp.Species == st.Species && sp.Has(pose) ? pose : "thumb/" + st.Species; // 그림을 아직 못 받았으면 썸네일로
        hero.Face = sp.Get(key, frame, 1);
        if (!crops.TryGetValue(st.Species + "|" + key, out var crop)) crops[st.Species + "|" + key] = crop = Ui.Opaque(sp.Get(key, 0, 1), 2);
        hero.Crop = crop;
        hero.Title = L.Name;
        hero.Sub = (egg ? $"{L.Stage("egg")} · {L.T("st.unknown")}" : $"{name} · {Names.Form(st.Stage)}") + " · " + L.T("st.age", Span(st.AgeMin));
        (hero.Chip, hero.ChipColor) = egg ? (L.T("chip.hatching"), Ui.Gray)
            : st.Asleep ? (L.T("chip.asleep"), Ui.Indigo)
            : st.Need switch
            {
                "hungry" => (L.T("chip.hungry"), Ui.Orange),
                "bored" => (L.T("chip.bored"), Ui.Pink),
                "sleepy" => (L.T("chip.sleepy"), Ui.Indigo),
                _ => (L.T(st.Mood >= 70 ? "chip.happy" : "chip.ok"), Ui.Ok),
            };
        (hero.Grow, hero.GrowFrac) = st.GrowTo == null ? (L.T("grow.done"), -1)
            : st.GrowIn is { } left ? (L.T("grow." + st.GrowTo, Span(left)), st.GrowFrac)
            : (L.T("grow." + st.GrowTo), -1); // 남은 시간 없이 글만 (알이 그림을 기다릴 때)
        hero.Invalidate();

        (full.Value, mood.Value, energy.Value) = (st.Full, st.Mood, st.Energy);
        missItem.Value = L.T("st.miss_value", st.Miss);
        missItem.ValueColor = st.Miss > 0 ? Ui.Fg : Ui.Dim;
        bitesItem.Value = L.T("st.bites_value", st.Bites);
        bitesItem.ValueColor = st.Bites > 0 ? Ui.Fg : Ui.Dim;
        bitesItem.Desc = L.T("st.bites_desc", st.BitesTotal);
        care.Invalidate();
        string foot = st.Stage is "egg" or "baby" or "child"
            ? L.T("st.care_foot")
            : "";
        if (careFoot.Text != foot) { careFoot.Text = foot; stacks["status"].Relayout(); }

        for (int i = 0; i < games.Items.Count && i < st.Games.Count; i++)
            (games.Items[i].Value, games.Items[i].ValueColor) = (st.Games[i].Value, st.Games[i].Played ? Ui.Fg : Ui.Dim);
        games.Invalidate();
    }

    public static string Span(double min) => L.Span(min); // 3분, 2시간 5분, 1일 3시간 (언어별)

    // ── 도감 ──
    Stack BookPage()
    {
        var s = new Stack();
        s.Desc(L.T("bk.desc"));
        nowCard = s.Group(L.T("bk.now"));
        nowItem = nowCard.Add("");
        grid = new BookGrid(spritesOf().SpeciesList.Select(id => (id, L.Species(id), (Image)thumbs[id].Two, (Image)thumbs[id].Faded)).ToList()) { NowText = L.T("bk.now_pill") };
        s.Group(L.T("page.book"), null, out var head, out _).AddBody(grid, 14);
        bookTitle = head!;
        goneCard = s.Group(L.T("bk.gone"), "", out _, out var foot);
        goneFoot = foot!;
        return s;
    }

    // 도감 새로 칠하기 (풀어준 뒤, 알이 깨진 뒤에 불린다)
    public void UpdateBook(PetBook b)
    {
        book = b;
        var names = spritesOf().SpeciesList;
        string name = L.Species(b.Species);

        nowItem.Icon = b.IsEgg ? spritesOf().Get("egg/idle", 0, 1) : thumbs.TryGetValue(b.Species, out var t) ? t.One : null;
        var release = new Btn(L.T("bk.release"), BtnKind.Danger) { Enabled = !b.IsEgg };
        release.Click += (_, _) => BeginInvoke(AskRelease); // 누른 버튼을 바로 지우지 않게 한 박자 뒤에
        nowCard.SetRight(nowItem, release);
        if (b.IsEgg) (nowItem.Label, nowItem.Desc) = (L.Stage("egg"), L.T("bk.egg_desc"));
        else (nowItem.Label, nowItem.Desc) = ($"{name} · {Names.Form(statusOf().Stage)}", L.T("bk.release_desc"));

        int seen = names.Count(id => b.Met.Contains(id) || (id == b.Species && !b.IsEgg));
        bookTitle.Text = L.T("bk.book", seen, names.Count);
        grid.Met = b.Met.ToHashSet();
        grid.Current = b.IsEgg ? null : b.Species;
        grid.Invalidate();

        goneCard.Clear();
        if (b.Released.Count == 0) goneCard.Add(L.T("bk.none_gone")).LabelColor = Ui.Dim;
        foreach (var r in b.Released.Reverse().Take(12))
        {
            string visit = b.Visits?.TryGetValue(r.Species, out var v) == true && v > r.At ? " · " + L.T("bk.visited_on", L.Date(v)) : "";
            int hearts = Math.Min(5, b.VisitCounts?.GetValueOrDefault(r.Species) ?? 0);
            var it = goneCard.Add($"{L.Species(r.Species)} · {Names.Form(r.Stage)}",
                L.T("bk.left_on", L.Date(r.At)) + " · " + L.T("bk.together", Span(r.AgeMin)) + visit, value: new string('♥', hearts) + new string('♡', 5 - hearts));
            it.ValueColor = Ui.Pink; // 우정 하트: 놀러 올 때마다 하나, 다섯이면 아기를 데려온다
            it.Icon = thumbs.TryGetValue(r.Species, out var rt) ? rt.One : null;
        }
        goneFoot.Text = b.Released.Count > 12 ? L.T("bk.more_gone", b.Released.Count - 12) : "";
        if (stacks.TryGetValue("book", out var st)) st.Relayout();
        FillMemories(b);
    }

    // ── 추억: 선물(종류마다 개수), 성장 앨범(사진), 엽서. 그림은 파일에서 읽어 창을 닫을 때 버린다 ──
    readonly List<Image> memImages = new();

    void FillMemories(PetBook b)
    {
        if (!stacks.TryGetValue("memories", out var old)) return;
        var s = new Stack { Bounds = old.Bounds, Visible = old.Visible };
        int index = Controls.GetChildIndex(old);
        Controls.Remove(old);
        old.Dispose();
        foreach (var im in memImages) im.Dispose();
        memImages.Clear();
        stacks["memories"] = s;
        Controls.Add(s);
        Controls.SetChildIndex(s, index);

        s.Desc(L.T("mem.desc"));
        var giftCard = s.Group(L.T("mem.gifts"));
        var kinds = b.Gifts.GroupBy(g => g.Kind).Select(g => (Kind: g.Key, Count: g.Count())).OrderByDescending(g => g.Count).ToList();
        if (kinds.Count == 0) giftCard.Add(L.T("mem.gifts_none")).LabelColor = Ui.Dim;
        else
        {
            var tiles = kinds.Select(k => ((Image)Keep(Memories.Icon(spritesOf().Get("item/" + k.Kind, 0, 1), 48)), "×" + k.Count)).ToList();
            giftCard.AddBody(new Gallery(tiles, 72, 72), 14);
        }
        var albumCard = s.Group(L.T("mem.album"));
        var photos = b.Album.Reverse().Take(30).Select(p => (Img: Memories.Load(p.File), p)).Where(x => x.Img != null).ToList();
        if (photos.Count == 0) albumCard.Add(L.T("mem.album_none")).LabelColor = Ui.Dim;
        else albumCard.AddBody(new Gallery(photos.Select(x => ((Image)Keep(x.Img!), L.Date(x.p.At))).ToList(), Memories.PhotoSize, Memories.PhotoSize), 14);
        var cardCard = s.Group(L.T("mem.cards"));
        var pics = b.Cards.Reverse().Take(12).Select(c => (Img: Memories.Load(c.File), c)).Where(x => x.Img != null).ToList();
        if (pics.Count == 0) cardCard.Add(L.T("mem.cards_none")).LabelColor = Ui.Dim;
        else cardCard.AddBody(new Gallery(pics.Select(x => ((Image)Keep(x.Img!), $"{L.Species(x.c.From)} · {L.Date(x.c.At)}")).ToList(), Memories.CardW, Memories.CardH), 14);
        if (page == "memories") s.Relayout();
    }

    Bitmap Keep(Bitmap b) { memImages.Add(b); return b; }

    // 풀어주기: 창을 따로 띄우지 않고 그 줄에서 한 번 더 묻는다
    internal void AskRelease()
    {
        nowItem.Desc = L.T("bk.confirm", L.With(L.Species(book.Species), "를", "을"));
        var cancel = new Btn(L.T("bk.cancel"));
        var go = new Btn(L.T("bk.send"), BtnKind.DangerFill);
        cancel.Click += (_, _) => BeginInvoke(() => UpdateBook(book));
        go.Click += (_, _) => BeginInvoke(() =>
        {
            if (ReleaseAsked?.Invoke() == true)
            {
                nowItem.Desc = L.T("bk.leaving");
                nowCard.SetRight(nowItem, null);
                stacks["book"].Relayout();
            }
            else
            {
                UpdateBook(book);
                nowItem.Desc = L.T("bk.cant");
                stacks["book"].Relayout();
            }
        });
        nowCard.SetRight(nowItem, new Pair(cancel, go));
        stacks["book"].Relayout();
    }

    // ── 일반 ──
    Stack GeneralPage()
    {
        var s = new Stack();
        sound = new Switch(soundOn);
        sound.Changed += () => { soundOn = sound.On; Changed?.Invoke(); };
        var lang = s.Group(L.T("gen.language")); // 누르면 바로 바뀐다
        foreach (var (code, native) in L.Languages)
        {
            var it = lang.Add(native, value: code == L.Code ? "✓" : "");
            it.ValueColor = Ui.Accent;
            it.Click = () => BeginInvoke(() => LanguageChosen?.Invoke(code));
        }

        s.Group(L.T("gen.sound"), L.T("gen.sound_foot")).Add(L.T("gen.game_sound"), L.T("gen.game_sound_desc"), sound);

        var how = s.Group(L.T("gen.how"));
        how.Add(L.T("gen.how1"), L.T("gen.how1_desc"));
        how.Add(L.T("gen.how2"), L.T("gen.how2_desc"));
        how.Add(L.T("gen.how3"), L.T("gen.how3_desc"));
        how.Add(L.T("gen.how4"), L.T("gen.how4_desc"));
        return s;
    }

    // ── 업데이트 ──
    Stack UpdatePage()
    {
        var s = new Stack();
        var ver = s.Group(L.T("up.version"));
        ver.Add(L.T("up.current"), value: VersionText).ValueColor = Ui.Fg;
        check = new Btn(L.T("up.check"));
        check.Click += async (_, _) => await CheckNow();
        checkItem = ver.Add(L.T("up.check_label"), L.T("up.check_desc"), check);
        var restart = new Btn(L.T("up.restart"), BtnKind.Primary);
        restart.Click += (_, _) => RestartAsked?.Invoke();
        readyItem = ver.Add(L.T("up.ready"), L.T("up.ready_desc"), restart);
        readyItem.LabelColor = Ui.Ok;
        readyItem.Hidden = true;

        var open = new Btn(L.T("up.open"));
        open.Click += (_, _) => Open(ReleasesUrl);
        s.Group(L.T("up.manual")).Add(L.T("up.releases"), L.T("up.releases_desc"), open);

        auto = new Switch(autoOn);
        auto.Changed += () => { autoOn = auto.On; Changed?.Invoke(); };
        s.Group(L.T("up.auto"), L.T("up.auto_foot", Updater.Repo))
            .Add(L.T("up.auto"), L.T("up.auto_desc"), auto);
        ShowReady();
        return s;
    }

    public static string VersionText => Updater.IsDevBuild ? L.T("set.dev") : "v" + Updater.Current.ToString(3);

    static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { /* 브라우저가 없으면 그만 */ }
    }

    public void ShowReady()
    {
        if (Updater.Ready is not { } tag || readyItem == null) return;
        readyItem.Hidden = false;
        readyItem.Label = L.T("up.ready_tag", tag);
        if (stacks.TryGetValue("update", out var s)) s.Relayout();
    }

    // 확인만 한다. 내려받지는 않는다 (받는 건 릴리스 페이지에서 직접, 또는 자동 업데이트를 켰을 때)
    async Task CheckNow()
    {
        check.Enabled = false;
        SetCheck(L.T("up.checking"), null);
        try
        {
            var r = await Updater.Latest();
            if (IsDisposed) return;
            if (r == null) SetCheck(L.T("up.latest"), Ui.Ok);
            else SetCheck(L.T("up.new", r.Tag), Ui.Orange);
        }
        catch { if (!IsDisposed) SetCheck(L.T("up.fail"), Ui.Danger); }
        if (!IsDisposed) check.Enabled = true;
    }

    void SetCheck(string text, Color? c)
    {
        (checkItem.Desc, checkItem.LabelColor) = (text, c);
        stacks["update"].Relayout();
    }
}

// 오른쪽 위 제목
sealed class Header : View
{
    public Header() { BackColor = Ui.Bg; }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Ui.Text(g, Text, Ui.F(16, true), new Rectangle(D(24), 0, Width - D(48), Height), Ui.Fg, TextFormatFlags.VerticalCenter | Ui.OneLine);
        using var line = new Pen(Ui.Line);
        g.DrawLine(line, 0, Height - 1, Width, Height - 1);
    }
}

// 버튼 둘 나란히 (취소 · 보내 주기)
sealed class Pair : View
{
    public Pair(Control a, Control b)
    {
        BackColor = Ui.Card;
        Controls.Add(a);
        Controls.Add(b);
        a.Location = Point.Empty;
        b.Location = new Point(a.Width + D(8), 0);
        Size = new Size(b.Right, Math.Max(a.Height, b.Height));
    }

    protected override void OnPaint(PaintEventArgs e) => e.Graphics.Clear(BackColor);
}

// 상태 맨 위: 움직이는 얼굴, 이름, 지금 기분, 자라기까지 남은 시간
sealed class Hero : View, IFlow
{
    public Image? Face;
    public Rectangle Crop;
    public string Title = "", Sub = "", Chip = "", Grow = "";
    public Color ChipColor = Ui.Ok;
    public double GrowFrac = -1;

    public Hero() { BackColor = Ui.Card; }
    public int Measure(int width) => D(104);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int box = D(104);
        Ui.Fill(g, new RectangleF(0, 0, box, box), D(14), Ui.Field);
        if (Face != null) // 실제로 그려진 부분을 상자에 맞게 정수배로 (아기·알도 크게)
        {
            var c = Crop.IsEmpty ? new Rectangle(0, 0, Face.Width, Face.Height) : Crop;
            int room = box - D(16), k = Math.Max(1, (int)Math.Min(room / (float)c.Width, room / (float)c.Height));
            Ui.Pixel(g, Face, new Rectangle((box - c.Width * k) / 2, (box - c.Height * k) / 2, c.Width * k, c.Height * k), c);
        }

        int x = box + D(20), w = Width - x;
        Ui.Text(g, Title, Ui.F(20, true), new Rectangle(x, D(4), w, D(28)), Ui.Fg, Ui.OneLine);
        Ui.Text(g, Sub, Ui.F(13), new Rectangle(x, D(34), w, D(20)), Ui.Dim, Ui.OneLine);

        var cs = Ui.Measure(Chip, Ui.F(12, true));
        var chip = new RectangleF(x, D(60), cs.Width + D(20), D(22));
        Ui.Fill(g, chip, chip.Height / 2, Ui.Mix(ChipColor, Ui.Card, .72));
        Ui.Text(g, Chip, Ui.F(12, true), Rectangle.Round(chip), Ui.Mix(ChipColor, Color.White, .35), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | Ui.OneLine);

        var gs = Ui.Measure(Grow, Ui.F(12));
        Ui.Text(g, Grow, Ui.F(12), new Rectangle(x, D(88), gs.Width + 2, D(16)), Ui.Dim, Ui.OneLine);
        if (GrowFrac >= 0)
        {
            float bx = x + gs.Width + D(10), bw = Math.Min(D(140), Width - bx - D(4)), h = D(6), by = D(88) + (D(16) - h) / 2f;
            if (bw > D(30))
            {
                Ui.Fill(g, new RectangleF(bx, by, bw, h), h / 2, Ui.Track);
                Ui.Fill(g, new RectangleF(bx, by, Math.Max(h, bw * (float)Math.Clamp(GrowFrac, 0, 1)), h), h / 2, Ui.Accent);
            }
        }
    }
}

// 갤러리: 그림 칸 + 아래 짧은 글 (추억 페이지). 그림은 도트라 뭉개지지 않게 정수배로
sealed class Gallery : View, IFlow
{
    readonly List<(Image Img, string Caption)> tiles;
    readonly int w, h;

    public Gallery(List<(Image, string)> tiles, int w, int h) { (this.tiles, this.w, this.h) = (tiles, w, h); BackColor = Ui.Card; }

    static int Gap => D(10);
    Size Tile => new(D(w) + D(8), D(h) + D(8) + D(20));
    int Cols(int width) => Math.Max(1, (width + Gap) / (Tile.Width + Gap));

    public int Measure(int width)
    {
        int c = Cols(width), rows = (tiles.Count + c - 1) / c;
        return rows * Tile.Height + (rows - 1) * Gap;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int c = Cols(Width);
        for (int i = 0; i < tiles.Count; i++)
        {
            var r = new Rectangle(i % c * (Tile.Width + Gap), i / c * (Tile.Height + Gap), Tile.Width, Tile.Height);
            if (!e.ClipRectangle.IntersectsWith(r)) continue;
            var (img, caption) = tiles[i];
            Ui.Fill(g, new RectangleF(r.X, r.Y, r.Width, r.Height - D(20)), D(8), Ui.Hex(0x1f2023));
            int k = Math.Max(1, (int)Math.Floor(Ui.K)); // 화면 배율에 맞춰 정수배
            int iw = Math.Min(img.Width * k, D(w)), ih = Math.Min(img.Height * k, D(h));
            Ui.Pixel(g, img, new Rectangle(r.X + (r.Width - iw) / 2, r.Y + D(4) + (D(h) - ih) / 2, iw, ih));
            Ui.Text(g, caption, Ui.F(11), new Rectangle(r.X, r.Bottom - D(18), r.Width, D(18)), Ui.Dim, TextFormatFlags.HorizontalCenter | Ui.OneLine);
        }
    }
}

// 도감 칸들: 만난 친구는 또렷하게, 아직이면 흐리게, 지금 아이는 파란 테두리와 「지금」
sealed class BookGrid : View, IFlow
{
    readonly List<(string Id, string Name, Image Img, Image Faded)> all;
    public HashSet<string> Met = new();
    public string? Current;
    public string NowText = "Now";
    int hover = -1;

    public BookGrid(List<(string, string, Image, Image)> all)
    {
        this.all = all;
        BackColor = Ui.Card;
    }

    static Size Tile => new(D(92), D(110));
    static int Gap => D(8);
    int Cols(int w) => Math.Max(1, (w + Gap) / (Tile.Width + Gap));

    public int Measure(int width)
    {
        int c = Cols(width), rows = (all.Count + c - 1) / c;
        return rows * Tile.Height + (rows - 1) * Gap;
    }

    Rectangle Box(int i)
    {
        int c = Cols(Width), total = c * Tile.Width + (c - 1) * Gap, x0 = (Width - total) / 2;
        return new Rectangle(x0 + i % c * (Tile.Width + Gap), i / c * (Tile.Height + Gap), Tile.Width, Tile.Height);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = Enumerable.Range(0, all.Count).FirstOrDefault(i => Box(i).Contains(e.Location), -1);
        if (h != hover) { hover = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var tileBg = Ui.Hex(0x1f2023);
        for (int i = 0; i < all.Count; i++)
        {
            var (id, name, img, faded) = all[i];
            var r = Box(i);
            if (!e.ClipRectangle.IntersectsWith(r)) continue;
            bool now = id == Current, met = now || Met.Contains(id);
            Ui.Fill(g, r, D(10), now ? Ui.Mix(Ui.Accent, tileBg, .86) : tileBg);
            Ui.Stroke(g, r, D(10), now ? Ui.Accent : i == hover ? Ui.BtnLine : Ui.CardLine, now ? 2 : 1);
            Ui.Pixel(g, met ? img : faded, new Rectangle(r.X + (r.Width - D(80)) / 2, r.Y + D(4), D(80), D(80)));
            Ui.Text(g, name, Ui.F(12, now), new Rectangle(r.X + D(2), r.Y + D(86), r.Width - D(4), D(18)), met ? Ui.Fg : Ui.Hex(0x5f636b),
                TextFormatFlags.HorizontalCenter | Ui.OneLine);
            if (now)
            {
                var ps = Ui.Measure(NowText, Ui.F(11, true));
                var pill = new RectangleF(r.X + D(6), r.Y + D(6), ps.Width + D(12), D(18));
                Ui.Fill(g, pill, pill.Height / 2, Ui.Accent);
                Ui.Text(g, NowText, Ui.F(11, true), Rectangle.Round(pill), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | Ui.OneLine);
            }
        }
    }
}
