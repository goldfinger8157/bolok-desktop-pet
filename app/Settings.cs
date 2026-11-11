using System.Diagnostics;
using System.Drawing.Imaging;

namespace Bolok;

// 도감에 보여줄 것: 지금 키우는 아이, 만나 본 종, 자연으로 떠난 친구들
public sealed record ReleasedPet(string Species, string Stage, double AgeMin, DateTime At);
public sealed record PetBook(string Species, bool IsEgg, IReadOnlyCollection<string> Met, IReadOnlyList<ReleasedPet> Released);

// 설정 창: 상태 / 도감(나올 수 있는 친구들, 풀어주기) / 일반(게임 소리) / 업데이트(지금 버전, 직접 받기 안내, 지금 확인, 자동 업데이트 — 기본 꺼짐)
public sealed class Settings : Form
{
    public const string ReleasesUrl = "https://github.com/" + Updater.Repo + "/releases/latest";
    readonly CheckBox sound = new() { Text = "게임 소리", AutoSize = true };
    readonly CheckBox auto = new() { Text = "자동 업데이트", AutoSize = true };
    readonly Label status = new() { AutoSize = true, MaximumSize = new Size(500, 0) };
    readonly Label statusText = new() { AutoSize = true, Font = new Font("Malgun Gothic", 11f), Margin = new Padding(3, 6, 3, 10) };
    readonly Button check = new() { Text = "지금 확인", AutoSize = true };
    readonly Button restart = new() { Text = "새 버전으로 다시 시작", AutoSize = true, Visible = false };
    readonly Button release = new() { Text = "풀어주기...", AutoSize = true };
    readonly Label now = new() { AutoSize = true, Font = new Font("Malgun Gothic", 10f, FontStyle.Bold) };
    readonly Label gone = new() { AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = SystemColors.GrayText };
    readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    readonly Dictionary<string, Label> cards = new();
    readonly Sprites sprites;
    readonly Func<string> statusOf;
    readonly System.Windows.Forms.Timer tick = new() { Interval = 1000 };

    public bool Sound => sound.Checked;
    public bool AutoUpdate => auto.Checked;
    public event Action? Changed;
    public event Action? RestartAsked;
    public event Action? ReleaseAsked;

    public Settings(bool soundOn, bool autoUpdate, Sprites sprites, PetBook book, Func<string> statusOf)
    {
        (this.sprites, this.statusOf) = (sprites, statusOf);
        Text = "볼록이 설정";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 560);
        TopMost = true;

        var state = new TabPage("상태") { Padding = new Padding(14), Name = "상태" };
        var pets = new TabPage("도감") { Padding = new Padding(10), Name = "도감" };
        var general = new TabPage("일반") { Padding = new Padding(14), Name = "일반" };
        var update = new TabPage("업데이트") { Padding = new Padding(14), Name = "업데이트" };
        tabs.TabPages.AddRange(new[] { state, pets, general, update });
        Controls.Add(tabs);

        // 상태: 예전 상태창 내용을 1초마다 새로
        state.Controls.Add(Stack(statusText, Note("포만·기분·기운은 하트 네 칸이에요. 볼 속은 먹은 화면 조각 수예요. 밥을 주면 다시 뱉어 내요.")));
        tick.Tick += (_, _) => { if (tabs.SelectedTab == state) statusText.Text = statusOf(); };
        tick.Start();
        statusText.Text = statusOf();
        FormClosed += (_, _) => tick.Dispose();

        // 도감: 나올 수 있는 친구들 (만나 본 아이는 또렷하게, 아직이면 흐리게). 고를 수는 없다 — 알이 깨져야 안다
        var grid = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
        foreach (var (id, name) in sprites.SpeciesNames)
        {
            var card = new Label
            {
                Text = name, ImageAlign = ContentAlignment.TopCenter, TextAlign = ContentAlignment.BottomCenter,
                Size = new Size(92, 106), BackColor = Color.White, Margin = new Padding(3), Padding = new Padding(0, 4, 0, 6), BorderStyle = BorderStyle.FixedSingle,
            };
            cards[id] = card;
            grid.Controls.Add(card);
        }
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
        bottom.Controls.AddRange(new Control[] { now, release, Note("풀어주면 지금 아이는 자연으로 떠나고, 그 자리에 새 알이 와요. 다시 돌아오지 않아요."), gone });
        release.Click += (_, _) => ReleaseAsked?.Invoke();
        pets.Controls.Add(grid);
        pets.Controls.Add(bottom);
        pets.Controls.Add(new Label { Text = "나올 수 있는 친구들이에요. 알이 깨지기 전엔 누가 나올지 몰라요.", Dock = DockStyle.Top, Height = 26, ForeColor = SystemColors.GrayText });
        UpdateBook(book);

        (sound.Checked, auto.Checked) = (soundOn, autoUpdate);
        sound.CheckedChanged += (_, _) => Changed?.Invoke();
        auto.CheckedChanged += (_, _) => Changed?.Invoke();
        general.Controls.Add(Stack(sound, Note("총쏘기·미니건 같은 게임의 효과음이에요. 기본은 꺼져 있어요.")));

        var link = new LinkLabel { Text = "저장소 릴리스 페이지 열기", AutoSize = true };
        link.LinkClicked += (_, _) => Open(ReleasesUrl);
        check.Click += async (_, _) => await CheckNow();
        restart.Click += (_, _) => RestartAsked?.Invoke();
        update.Controls.Add(Stack(
            new Label { Text = $"지금 버전: {VersionText}", AutoSize = true, Font = new Font(Font, FontStyle.Bold) },
            Note("새 버전은 저장소에서 Bolok.exe 를 직접 내려받아, 쓰던 파일에 덮어쓰면 설치돼요."),
            link,
            check,
            status,
            restart,
            new Label { Height = 8, Width = 10 },
            auto,
            Note($"켜면 하루에 한 번 저장소(github.com/{Updater.Repo})에서 새 Bolok.exe 를 자동으로 내려받아요. 다음에 볼록이를 켤 때 새 버전으로 바뀌어요. 기본은 꺼져 있어요.")));
        ShowReady();
    }

    public void SelectTab(string name)
    {
        if (tabs.TabPages[name] is { } page) tabs.SelectedTab = page;
        statusText.Text = statusOf();
    }

    // 도감 새로 칠하기: 지금 아이(주황 테두리), 만나 본 종(또렷), 아직(흐리게)
    public void UpdateBook(PetBook book)
    {
        foreach (var (id, card) in cards)
        {
            bool current = id == book.Species && !book.IsEgg, met = book.Met.Contains(id);
            var thumb = sprites.Get("thumb/" + id, 0, 2);
            card.Image?.Dispose();
            card.Image = met || current ? new Bitmap(thumb) : Faded(thumb);
            card.ForeColor = met || current ? SystemColors.ControlText : SystemColors.GrayText;
            card.BackColor = current ? Color.FromArgb(255, 243, 224) : Color.White;
            card.BorderStyle = current ? BorderStyle.Fixed3D : BorderStyle.FixedSingle;
        }
        string name = sprites.SpeciesNames.GetValueOrDefault(book.Species, book.Species);
        int seen = cards.Keys.Count(id => book.Met.Contains(id) || (id == book.Species && !book.IsEgg));
        now.Text = (book.IsEgg ? "지금: 알 (무엇이 나올까요?)" : $"지금: {name}") + $"      도감 {seen}/{cards.Count}";
        release.Enabled = !book.IsEgg;
        gone.Text = book.Released.Count == 0 ? "" : $"떠난 친구들 {book.Released.Count}: " + string.Join(", ", book.Released.TakeLast(6).Reverse()
            .Select(r => $"{sprites.SpeciesNames.GetValueOrDefault(r.Species, r.Species)} ({Names.Form(r.Stage)}, {r.At:M/d})"));
    }

    // 아직 못 만난 친구는 흐리게
    static Bitmap Faded(Bitmap src)
    {
        var dst = new Bitmap(src.Width, src.Height);
        using var g = Graphics.FromImage(dst);
        using var attrs = new ImageAttributes();
        attrs.SetColorMatrix(new ColorMatrix { Matrix00 = .55f, Matrix11 = .55f, Matrix22 = .6f, Matrix33 = .35f, Matrix40 = .3f, Matrix41 = .3f, Matrix42 = .3f });
        g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
        return dst;
    }

    public static string VersionText => Updater.IsDevBuild ? "개발 빌드" : "v" + Updater.Current.ToString(3);

    static FlowLayoutPanel Stack(params Control[] rows)
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        foreach (var r in rows) { r.Margin = new Padding(3, 4, 3, 6); p.Controls.Add(r); }
        return p;
    }

    static Label Note(string text) => new() { Text = text, AutoSize = true, MaximumSize = new Size(500, 0), ForeColor = SystemColors.GrayText };

    static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { /* 브라우저가 없으면 그만 */ }
    }

    public void ShowReady()
    {
        if (Updater.Ready is not { } tag) return;
        status.Text = $"새 버전 {tag} 을 받아 뒀어요. 다시 시작하면 바뀌어요.";
        restart.Visible = true;
    }

    // 확인만 한다. 내려받지는 않는다 (받는 건 릴리스 페이지에서 직접, 또는 자동 업데이트를 켰을 때)
    async Task CheckNow()
    {
        check.Enabled = false;
        status.Text = "확인하는 중…";
        try
        {
            var r = await Updater.Latest();
            status.Text = r == null ? "지금이 최신 버전이에요." : $"새 버전 {r.Tag} 이 있어요. 위 링크에서 받거나 자동 업데이트를 켜 주세요.";
        }
        catch { status.Text = "확인하지 못했어요. 인터넷 연결을 봐 주세요."; }
        check.Enabled = true;
    }
}
