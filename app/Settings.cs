using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace Bolok;

// 도감에 보여줄 것: 지금 키우는 아이, 만나 본 종, 자연으로 떠난 친구들
public sealed record ReleasedPet(string Species, string Stage, double AgeMin, DateTime At);
public sealed record PetBook(string Species, bool IsEgg, IReadOnlyCollection<string> Met, IReadOnlyList<ReleasedPet> Released,
    IReadOnlyDictionary<string, DateTime>? Visits = null); // Visits: 떠난 친구가 마지막으로 놀러 온 때

// 상태 탭에 보여줄 것 (1초마다 새로 받는다)
public sealed record GameStat(string Name, string Value, bool Played);
public sealed record PetStatus(string Species, string Stage, double AgeMin, double Full, double Mood, double Energy, bool Asleep, string? Need,
    int Miss, int Bites, int BitesTotal, double? GrowIn, double GrowFrac, string? GrowTo, IReadOnlyList<GameStat> Games);

// 설정 창. patio 설정 창을 따른다: 왼쪽 목록(맨 위 볼록이 얼굴) + 오른쪽 제목과 묶음 카드
// 상태 / 도감(나올 수 있는 친구들, 풀어주기) / 일반(게임 소리, 노는 법) / 업데이트(버전, 직접 받기, 자동 업데이트 — 기본 꺼짐)
public sealed class Settings : Form
{
    public const string ReleasesUrl = "https://github.com/" + Updater.Repo + "/releases/latest";
    public static readonly string[] Pages = { "상태", "도감", "일반", "업데이트" };

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

    public Settings(bool soundOn, bool autoUpdate, Func<Sprites> spritesOf, Func<PetStatus> statusOf, PetBook book)
    {
        (this.soundOn, autoOn, this.spritesOf, this.statusOf, this.book) = (soundOn, autoUpdate, spritesOf, statusOf, book);
        Text = "볼록이 설정";
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
        foreach (var id in sp.SpeciesNames.Keys)
        {
            var two = new Bitmap(sp.Get("thumb/" + id, 0, 2));
            thumbs[id] = (new Bitmap(sp.Get("thumb/" + id, 0, 1)), two, Ui.Faded(two));
        }

        Ui.K = DeviceDpi / 96f;
        Build();
        tick.Tick += (_, _) => { frame++; UpdateStatus(); };
        tick.Start();
        FormClosed += (_, _) =>
        {
            tick.Dispose();
            foreach (var (a, b, c) in thumbs.Values) { a.Dispose(); b.Dispose(); c.Dispose(); }
        };
    }

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
            new("상태", "상태", Ui.Pink, Glyphs.Heart),
            new("도감", "도감", Ui.Orange, Glyphs.Paw),
            new("일반", "일반", Ui.Gray, Glyphs.Gear),
            new("업데이트", "업데이트", Ui.Blue, Glyphs.Download),
        }, page) { Foot = "볼록이 " + VersionText };
        nav.Picked += ShowPage;
        header = new Header();
        stacks["상태"] = StatusPage();
        stacks["도감"] = BookPage();
        stacks["일반"] = GeneralPage();
        stacks["업데이트"] = UpdatePage();

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
        header.Text = id;
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

        var cond = s.Group("컨디션", "25 아래로 내려가면 볼록이가 말풍선으로 불러요.");
        cond.Add("포만", "밥을 주면 차요", full = new Meter(Ui.Orange));
        cond.Add("기분", "놀아 주거나 쓰다듬으면 올라요", mood = new Meter(Ui.Pink));
        cond.Add("기운", "자고 나면 차요", energy = new Meter(Ui.Ok));

        care = s.Group("돌봄", "", out _, out var foot);
        careFoot = foot!;
        missItem = care.Add("돌봄 실수", "배고프거나 심심한 채로 15분 넘게 두면 하나씩 늘어요", value: "0번");
        bitesItem = care.Add("볼 속 화면 조각", "기분이 나쁘면 화면을 갉아 먹어요. 밥을 주면 뱉어 내요", value: "0입");

        games = s.Group("게임 기록");
        foreach (var gs in statusOf().Games) games.Add(gs.Name, value: gs.Value);
        return s;
    }

    void UpdateStatus()
    {
        var st = statusOf();
        var sp = spritesOf();
        bool egg = st.Stage == "egg";
        string name = egg ? "알" : sp.SpeciesNames.GetValueOrDefault(st.Species, "볼록이");

        // 왼쪽 위 얼굴은 어느 페이지에서나
        var face = egg ? sp.Get("egg/idle", 0, 1) : thumbs.TryGetValue(st.Species, out var t) ? t.One : null;
        string line2 = egg ? "무엇이 나올까요?" : $"{name} · {Names.Form(st.Stage)}";
        if (nav.Face != face || nav.Name2 != line2) { (nav.Face, nav.Name2) = (face, line2); nav.Invalidate(); }
        if (page == "도감" && !book.IsEgg && nowItem.Right is Btn && nowItem.Label != line2) { nowItem.Label = line2; nowCard.Invalidate(); } // 자라면 이름 옆 모습도
        if (page != "상태") return;

        string pose = $"{st.Stage}/0/{(st.Asleep ? "sleep" : "idle")}";
        string key = egg ? "egg/idle" : sp.Species == st.Species && sp.Has(pose) ? pose : "thumb/" + st.Species; // 그림을 아직 못 받았으면 썸네일로
        hero.Face = sp.Get(key, frame, 1);
        if (!crops.TryGetValue(st.Species + "|" + key, out var crop)) crops[st.Species + "|" + key] = crop = Ui.Opaque(sp.Get(key, 0, 1), 2);
        hero.Crop = crop;
        hero.Title = "볼록이";
        hero.Sub = (egg ? "알 · 무엇이 나올까요?" : $"{name} · {Names.Form(st.Stage)}") + $" · 나이 {Span(st.AgeMin)}";
        (hero.Chip, hero.ChipColor) = egg ? ("곧 깨어나요", Ui.Gray)
            : st.Asleep ? ("쿨쿨 자는 중", Ui.Indigo)
            : st.Need switch
            {
                "hungry" => ("배고파요", Ui.Orange),
                "bored" => ("심심해요", Ui.Pink),
                "sleepy" => ("졸려요", Ui.Indigo),
                _ => (st.Mood >= 70 ? "기분 좋아요" : "잘 지내요", Ui.Ok),
            };
        (hero.Grow, hero.GrowFrac) = st.GrowTo == null ? ("다 자랐어요", -1)
            : st.GrowIn is { } left ? ($"{st.GrowTo}까지 {Span(left)}", st.GrowFrac)
            : (st.GrowTo, -1); // 남은 시간 없이 글만 (알이 그림을 기다릴 때)
        hero.Invalidate();

        (full.Value, mood.Value, energy.Value) = (st.Full, st.Mood, st.Energy);
        missItem.Value = $"{st.Miss}번";
        missItem.ValueColor = st.Miss > 0 ? Ui.Fg : Ui.Dim;
        bitesItem.Value = $"{st.Bites}입";
        bitesItem.ValueColor = st.Bites > 0 ? Ui.Fg : Ui.Dim;
        bitesItem.Desc = $"기분이 나쁘면 화면을 갉아 먹어요. 밥을 주면 뱉어 내요. 지금까지 {st.BitesTotal}입";
        care.Invalidate();
        string foot = st.Stage is "egg" or "baby" or "child"
            ? "돌봄 실수가 8번이 되거나 화면을 모두 12입 넘게 먹으면 심술이로 자라요. 밥보다 쓰다듬기를 훨씬 많이 하면 애교쟁이, 아니면 통통이가 돼요."
            : "";
        if (careFoot.Text != foot) { careFoot.Text = foot; stacks["상태"].Relayout(); }

        for (int i = 0; i < games.Items.Count && i < st.Games.Count; i++)
            (games.Items[i].Value, games.Items[i].ValueColor) = (st.Games[i].Value, st.Games[i].Played ? Ui.Fg : Ui.Dim);
        games.Invalidate();
    }

    // 3분, 2시간 5분, 1일 3시간
    public static string Span(double min)
    {
        int m = Math.Max(0, (int)Math.Ceiling(min));
        if (m < 60) return $"{m}분";
        int h = m / 60, mm = m % 60;
        if (h < 24) return mm > 0 ? $"{h}시간 {mm}분" : $"{h}시간";
        int d = h / 24, hh = h % 24;
        return hh > 0 ? $"{d}일 {hh}시간" : $"{d}일";
    }

    // ── 도감 ──
    Stack BookPage()
    {
        var s = new Stack();
        s.Desc("알이 깨지기 전엔 누가 나올지 몰라요. 만나 본 친구는 또렷하게, 아직 못 만난 친구는 흐리게 보여요. 새 알이 오면 그 친구 그림(약 300KB)을 저장소에서 받아 와요.");
        nowCard = s.Group("지금 키우는 아이");
        nowItem = nowCard.Add("");
        grid = new BookGrid(spritesOf().SpeciesNames.Select(kv => (kv.Key, kv.Value, (Image)thumbs[kv.Key].Two, (Image)thumbs[kv.Key].Faded)).ToList());
        s.Group("도감", null, out var head, out _).AddBody(grid, 14);
        bookTitle = head!;
        goneCard = s.Group("떠난 친구들", "", out _, out var foot);
        goneFoot = foot!;
        return s;
    }

    // 도감 새로 칠하기 (풀어준 뒤, 알이 깨진 뒤에 불린다)
    public void UpdateBook(PetBook b)
    {
        book = b;
        var names = spritesOf().SpeciesNames;
        string name = names.GetValueOrDefault(b.Species, "볼록이");

        nowItem.Icon = b.IsEgg ? spritesOf().Get("egg/idle", 0, 1) : thumbs.TryGetValue(b.Species, out var t) ? t.One : null;
        var release = new Btn("풀어주기…", BtnKind.Danger) { Enabled = !b.IsEgg };
        release.Click += (_, _) => BeginInvoke(AskRelease); // 누른 버튼을 바로 지우지 않게 한 박자 뒤에
        nowCard.SetRight(nowItem, release);
        if (b.IsEgg) (nowItem.Label, nowItem.Desc) = ("알", "깨어나면 누가 나왔는지 도감에 올라가요");
        else (nowItem.Label, nowItem.Desc) = ($"{name} · {Names.Form(statusOf().Stage)}", "풀어주면 자연으로 떠나고, 그 자리에 새 알이 와요. 다시 돌아오지 않아요");

        int seen = names.Keys.Count(id => b.Met.Contains(id) || (id == b.Species && !b.IsEgg));
        bookTitle.Text = $"도감  {seen} / {names.Count}";
        grid.Met = b.Met.ToHashSet();
        grid.Current = b.IsEgg ? null : b.Species;
        grid.Invalidate();

        goneCard.Clear();
        if (b.Released.Count == 0) goneCard.Add("아직 떠난 친구가 없어요").LabelColor = Ui.Dim;
        foreach (var r in b.Released.Reverse().Take(12))
        {
            string visit = b.Visits?.TryGetValue(r.Species, out var v) == true && v > r.At ? $" · {v:M월 d일}에 놀러 왔어요" : "";
            var it = goneCard.Add($"{names.GetValueOrDefault(r.Species, r.Species)} · {Names.Form(r.Stage)}",
                $"{r.At:M월 d일}에 떠났어요 · 함께한 시간 {Span(r.AgeMin)}{visit}");
            it.Icon = thumbs.TryGetValue(r.Species, out var rt) ? rt.One : null;
        }
        goneFoot.Text = b.Released.Count > 12 ? $"그 전에 떠난 친구 {b.Released.Count - 12}명은 줄였어요." : "";
        if (stacks.TryGetValue("도감", out var st)) st.Relayout();
    }

    // 풀어주기: 창을 따로 띄우지 않고 그 줄에서 한 번 더 묻는다
    internal void AskRelease()
    {
        string name = spritesOf().SpeciesNames.GetValueOrDefault(book.Species, "볼록이");
        nowItem.Desc = $"정말 {name}{Names.Josa(name, "를", "을")} 보내 줄까요? 다시 돌아오지 않아요";
        var cancel = new Btn("취소");
        var go = new Btn("보내 주기", BtnKind.DangerFill);
        cancel.Click += (_, _) => BeginInvoke(() => UpdateBook(book));
        go.Click += (_, _) => BeginInvoke(() =>
        {
            if (ReleaseAsked?.Invoke() == true)
            {
                nowItem.Desc = "자연으로 떠나는 중이에요… 곧 새 알이 와요";
                nowCard.SetRight(nowItem, null);
                stacks["도감"].Relayout();
            }
            else
            {
                UpdateBook(book);
                nowItem.Desc = "지금은 풀어줄 수 없어요. 게임이 끝나면 다시 해 주세요";
                stacks["도감"].Relayout();
            }
        });
        nowCard.SetRight(nowItem, new Pair(cancel, go));
        stacks["도감"].Relayout();
    }

    // ── 일반 ──
    Stack GeneralPage()
    {
        var s = new Stack();
        sound = new Switch(soundOn);
        sound.Changed += () => { soundOn = sound.On; Changed?.Invoke(); };
        s.Group("소리", "기본은 꺼져 있어요.").Add("게임 소리", "총쏘기·미니건 같은 게임의 효과음이에요", sound);

        var how = s.Group("볼록이와 노는 법");
        how.Add("밥 주기 · 놀아주기 · 게임", "볼록이를 오른쪽 클릭하면 메뉴가 나와요");
        how.Add("잡아서 던지기", "끌었다가 놓으면 날아가요. 살살 던지면 좋아하고, 세게 던지면 어지러워해요");
        how.Add("상태 보기", "작업 표시줄 오른쪽의 볼록이 아이콘을 누르면 이 창의 상태가 열려요");
        how.Add("먹은 화면 돌려받기", "밥을 주면 볼 속에 넣어 둔 화면 조각을 뱉어 내요");
        return s;
    }

    // ── 업데이트 ──
    Stack UpdatePage()
    {
        var s = new Stack();
        var ver = s.Group("버전");
        ver.Add("지금 버전", value: VersionText).ValueColor = Ui.Fg;
        check = new Btn("지금 확인");
        check.Click += async (_, _) => await CheckNow();
        checkItem = ver.Add("새 버전 확인", "저장소에 새 버전이 있는지 확인만 해요. 내려받지는 않아요", check);
        var restart = new Btn("다시 시작", BtnKind.Primary);
        restart.Click += (_, _) => RestartAsked?.Invoke();
        readyItem = ver.Add("새 버전을 받아 뒀어요", "다시 시작하면 새 버전으로 바뀌어요", restart);
        readyItem.LabelColor = Ui.Ok;
        readyItem.Hidden = true;

        var open = new Btn("열기");
        open.Click += (_, _) => Open(ReleasesUrl);
        s.Group("직접 받기").Add("릴리스 페이지", "저장소에서 Bolok.exe 를 내려받아 쓰던 파일에 덮어쓰면 설치돼요", open);

        auto = new Switch(autoOn);
        auto.Changed += () => { autoOn = auto.On; Changed?.Invoke(); };
        s.Group("자동 업데이트", $"github.com/{Updater.Repo} 에서만 받아요. 받은 파일의 크기와 지문(sha256)을 확인한 뒤, 다음에 볼록이를 켤 때 새 버전으로 바뀌어요.")
            .Add("자동 업데이트", "켜면 하루에 한 번 새 버전을 확인해서 받아 둬요. 기본은 꺼져 있어요", auto);
        ShowReady();
        return s;
    }

    public static string VersionText => Updater.IsDevBuild ? "개발 빌드" : "v" + Updater.Current.ToString(3);

    static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { /* 브라우저가 없으면 그만 */ }
    }

    public void ShowReady()
    {
        if (Updater.Ready is not { } tag || readyItem == null) return;
        readyItem.Hidden = false;
        readyItem.Label = $"새 버전 {tag} 을 받아 뒀어요";
        if (stacks.TryGetValue("업데이트", out var s)) s.Relayout();
    }

    // 확인만 한다. 내려받지는 않는다 (받는 건 릴리스 페이지에서 직접, 또는 자동 업데이트를 켰을 때)
    async Task CheckNow()
    {
        check.Enabled = false;
        SetCheck("확인하는 중…", null);
        try
        {
            var r = await Updater.Latest();
            if (IsDisposed) return;
            if (r == null) SetCheck("지금이 최신 버전이에요", Ui.Ok);
            else SetCheck($"새 버전 {r.Tag} 이 있어요. 릴리스 페이지에서 받거나 자동 업데이트를 켜 주세요", Ui.Orange);
        }
        catch { if (!IsDisposed) SetCheck("확인하지 못했어요. 인터넷 연결을 봐 주세요", Ui.Danger); }
        if (!IsDisposed) check.Enabled = true;
    }

    void SetCheck(string text, Color? c)
    {
        (checkItem.Desc, checkItem.LabelColor) = (text, c);
        stacks["업데이트"].Relayout();
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

// 도감 칸들: 만난 친구는 또렷하게, 아직이면 흐리게, 지금 아이는 파란 테두리와 「지금」
sealed class BookGrid : View, IFlow
{
    readonly List<(string Id, string Name, Image Img, Image Faded)> all;
    public HashSet<string> Met = new();
    public string? Current;
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
                var ps = Ui.Measure("지금", Ui.F(11, true));
                var pill = new RectangleF(r.X + D(6), r.Y + D(6), ps.Width + D(12), D(18));
                Ui.Fill(g, pill, pill.Height / 2, Ui.Accent);
                Ui.Text(g, "지금", Ui.F(11, true), Rectangle.Round(pill), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | Ui.OneLine);
            }
        }
    }
}
