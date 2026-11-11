using System.Diagnostics;

namespace Bolok;

// 설정 창: 일반(게임 소리) / 업데이트(지금 버전, 저장소에서 직접 받기 안내, 지금 확인, 자동 업데이트 — 기본 꺼짐)
public sealed class Settings : Form
{
    public const string ReleasesUrl = "https://github.com/" + Updater.Repo + "/releases/latest";
    readonly CheckBox sound = new() { Text = "게임 소리", AutoSize = true };
    readonly CheckBox auto = new() { Text = "자동 업데이트", AutoSize = true };
    readonly Label status = new() { AutoSize = true, MaximumSize = new Size(380, 0) };
    readonly Button check = new() { Text = "지금 확인", AutoSize = true };
    readonly Button restart = new() { Text = "새 버전으로 다시 시작", AutoSize = true, Visible = false };

    public bool Sound => sound.Checked;
    public bool AutoUpdate => auto.Checked;
    public event Action? Changed;
    public event Action? RestartAsked;

    public Settings(bool soundOn, bool autoUpdate)
    {
        Text = "볼록이 설정";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(440, 330);
        TopMost = true;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var general = new TabPage("일반") { Padding = new Padding(14) };
        var update = new TabPage("업데이트") { Padding = new Padding(14) };
        tabs.TabPages.AddRange(new[] { general, update });
        Controls.Add(tabs);

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

    public static string VersionText => Updater.IsDevBuild ? "개발 빌드" : "v" + Updater.Current.ToString(3);

    static FlowLayoutPanel Stack(params Control[] rows)
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        foreach (var r in rows) { r.Margin = new Padding(3, 4, 3, 6); p.Controls.Add(r); }
        return p;
    }

    static Label Note(string text) => new() { Text = text, AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = SystemColors.GrayText };

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
