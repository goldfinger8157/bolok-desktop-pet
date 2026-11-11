using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Bolok;

// 자동 업데이트: 깃허브 릴리스의 최신 버전이 지금보다 높으면 Bolok.exe 를 받아 바꿔치기한다. 다음에 켤 때 새 버전으로 뜬다.
// 실행 중인 exe 는 덮어쓸 수 없지만 이름은 바꿀 수 있어서: 지금 것 → Bolok.exe.old, 받은 것 → Bolok.exe. .old 는 다음 시작 때 지운다.
// 일반 실행(배포용 한 파일 exe)에서만 돈다. 디버그 모드와 개발 빌드(버전 0.0.0)는 건드리지 않는다
public static class Updater
{
    public const string Repo = "goldfinger8157/bolok-desktop-pet", Asset = "Bolok.exe";
    public static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
    public static string? Ready { get; private set; }   // 바꿔치기까지 끝난 새 버전 (다시 시작하면 적용)

    public sealed record Release(Version Version, string Tag, string Url, long Size, string? Sha256);

    public static Version? ParseTag(string? tag)
    {
        tag = tag?.Trim().TrimStart('v', 'V');
        return Version.TryParse(tag, out var v) ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : null;
    }

    // 깃허브 "최신 릴리스" 응답에서 지금보다 새 버전의 Bolok.exe 를 찾는다. 초안·시험판·같거나 낮은 버전·exe 없는 릴리스는 null
    public static Release? Newer(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("tag_name", out var tagEl)) return null;
        bool Flag(string name) => r.TryGetProperty(name, out var f) && f.ValueKind == JsonValueKind.True;
        if (Flag("draft") || Flag("prerelease")) return null;
        string tag = tagEl.GetString() ?? "";
        if (ParseTag(tag) is not { } v || v <= new Version(current.Major, current.Minor, Math.Max(0, current.Build))) return null;
        if (!r.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        foreach (var a in assets.EnumerateArray())
        {
            if (a.GetProperty("name").GetString() != Asset) continue;
            string url = a.GetProperty("browser_download_url").GetString() ?? "";
            if (!url.StartsWith($"https://github.com/{Repo}/releases/download/", StringComparison.Ordinal)) return null; // 우리 저장소 것만
            string? digest = a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            return new Release(v, tag, url, a.GetProperty("size").GetInt64(), digest?.StartsWith("sha256:") == true ? digest[7..] : null);
        }
        return null;
    }

    // 받은 파일이 릴리스에 적힌 크기·해시와 같은지
    public static bool Verify(string path, Release r)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != r.Size || info.Length < 1_000_000) return false;
        if (r.Sha256 == null) return true;
        using var f = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(f)).Equals(r.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    // 바꿔치기. 중간에 실패하면 원래대로 돌려놓는다
    public static bool Swap(string exe, string fresh)
    {
        string old = exe + ".old";
        try
        {
            File.Move(exe, old, true);
            try { File.Move(fresh, exe); }
            catch { File.Move(old, exe); throw; }
            return true;
        }
        catch { return false; }
    }

    public static void Cleanup(string exe)
    {
        foreach (string leftover in new[] { exe + ".old", exe + ".new" })
            try { File.Delete(leftover); } catch { /* 아직 잠겨 있으면 다음에 */ }
    }

    // 시작하고 20초 뒤, 그 뒤로 하루에 한 번 확인한다. 실패는 조용히 넘긴다 (인터넷이 없을 수도 있다)
    public static void Start(Func<bool> enabled, Action<string> onReady)
    {
        string? exe = Environment.ProcessPath;
        if (exe == null || Path.GetFileName(exe) != Asset || Current.Major + Current.Minor + Current.Build == 0) return;
        Cleanup(exe);
        _ = Task.Run(async () =>
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("bolok-desktop-pet");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            await Task.Delay(TimeSpan.FromSeconds(20));
            while (Ready == null)
            {
                try
                {
                    if (enabled() && Newer(await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"), Current) is { } r)
                    {
                        string fresh = exe + ".new";
                        await using (var to = File.Create(fresh))
                        await using (var from = await http.GetStreamAsync(r.Url)) await from.CopyToAsync(to);
                        if (Verify(fresh, r) && Swap(exe, fresh)) { Ready = r.Tag; onReady(r.Tag); }
                        else File.Delete(fresh);
                    }
                }
                catch (Exception e) { Dbg.Log("update-error", ("msg", e.Message)); }
                await Task.Delay(TimeSpan.FromHours(24));
            }
        });
    }

    // 새 exe 로 다시 시작: 새 프로세스는 이 프로세스가 끝나길 기다렸다가 뜬다 (한 번에 하나만 뜨게 막아 둔 것 때문)
    public static void Restart()
    {
        if (Environment.ProcessPath is { } exe) Process.Start(new ProcessStartInfo(exe, $"--after {Environment.ProcessId}") { UseShellExecute = false });
        Application.Exit();
    }
}
