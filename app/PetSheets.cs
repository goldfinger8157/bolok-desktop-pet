using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Bolok;

// 캐릭터 그림 가져오기. 데이터(assets/pets/art.json)로 그릴 수 있는 종(지금은 햄스터)은 받을 것 없이 바로 그린다 (Load·Ready).
// 아직 못 그리는 종은 새 알이 생길 때 저장소의 「이 버전 태그」에서 그 종 그림만 받아 둔다 (알이 깨지기 전, 약 300KB).
// 받은 파일은 exe 안 목록(assets/pets/manifest.json: 크기·sha256)과 맞아야만 저장하고 쓴다. 개발(디버그)은 저장소 assets 를 바로 읽는다
public sealed class PetSheets
{
    public sealed record Entry(string Png, long PngSize, string Json, long JsonSize);

    readonly string cacheDir;
    readonly string? assetsDir;
    readonly Func<HttpClient> http;
    readonly HashSet<string> fetching = new();
    public readonly string GitRef;
    public readonly IReadOnlyDictionary<string, Entry> Manifest;

    // 그리는 데이터: 개발은 저장소 것(고치면 바로 다시 읽는다), 릴리스는 exe 안 것
    public PetArt Art => assetsDir != null ? PetArt.FromDir(assetsDir) : PetArt.Embedded;

    // 그 종 그림: 데이터로 그리거나 받아 둔 것. 아직 없으면 null (Fetch 로 받는다)
    public Sprites? Load(string species, string? dir) => Art.Can(species) ? Sprites.Drawn(Art, species, dir) : Read(species) is { } p ? Sprites.With(species, p, dir) : null;

    // 지금 바로 쓸 수 있나 (그릴 수 있거나 받아 두었다)
    public bool Ready(string species) => Art.Can(species) || Read(species) != null;

    public PetSheets(string cacheDir, string? assetsDir, string gitRef, IReadOnlyDictionary<string, Entry>? manifest = null, Func<HttpClient>? http = null)
    {
        (this.cacheDir, this.assetsDir, GitRef) = (cacheDir, assetsDir, gitRef);
        Manifest = manifest ?? EmbeddedManifest();
        this.http = http ?? Client;
    }

    // 릴리스는 자기 태그(v0.1.3)에서, 개발 빌드는 main 에서
    public static string RefFor(Version v, bool dev) => dev ? "main" : "v" + v.ToString(3);

    public string Url(string file) => $"https://raw.githubusercontent.com/{Updater.Repo}/{GitRef}/assets/pets/{file}";

    static HttpClient Client()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("bolok-desktop-pet");
        return c;
    }

    public static Dictionary<string, Entry> ReadManifest(Stream s)
    {
        using var doc = JsonDocument.Parse(s);
        var d = new Dictionary<string, Entry>();
        foreach (var e in doc.RootElement.EnumerateObject())
            d[e.Name] = new Entry(e.Value.GetProperty("png").GetString()!, e.Value.GetProperty("png_size").GetInt64(),
                e.Value.GetProperty("json").GetString()!, e.Value.GetProperty("json_size").GetInt64());
        return d;
    }

    public static Dictionary<string, Entry> EmbeddedManifest()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("pets/manifest.json")!;
        return ReadManifest(s);
    }


    // 받아 둔 파일 이름에 지문 앞자리를 붙인다: 버전이 바뀌어 그림이 달라지면 새로 받는다
    string CachePath(string species, string ext)
    {
        var e = Manifest[species];
        return Path.Combine(cacheDir, $"{species}.{(ext == "png" ? e.Png : e.Json)[..12]}.{ext}");
    }

    public static bool Ok(byte[] b, string sha256, long size) =>
        b.Length == size && Convert.ToHexString(SHA256.HashData(b)).Equals(sha256, StringComparison.OrdinalIgnoreCase);

    // 지금 바로 읽을 수 있는 PNG 그림 (개발 폴더, 받아 둔 것 순). 없거나 목록과 안 맞으면 null. 데이터로 그리는 종은 Load·Ready 로
    public (byte[] Png, byte[] Json)? Read(string species)
    {
        if (assetsDir != null)
        {
            string p = Path.Combine(assetsDir, "pets", species + ".png"), j = Path.Combine(assetsDir, "pets", species + ".json");
            return File.Exists(p) && File.Exists(j) ? (File.ReadAllBytes(p), File.ReadAllBytes(j)) : null;
        }
        if (!Manifest.TryGetValue(species, out var e)) return null;
        string pp = CachePath(species, "png"), jp = CachePath(species, "json");
        if (!File.Exists(pp) || !File.Exists(jp)) return null;
        try
        {
            byte[] png = File.ReadAllBytes(pp), json = File.ReadAllBytes(jp);
            return Ok(png, e.Png, e.PngSize) && Ok(json, e.Json, e.JsonSize) ? (png, json) : null;
        }
        catch (IOException) { return null; }
    }

    // 바로 쓸 수 있는 종들 (깨어날 때까지 원래 종을 못 받았으면 이 중에서 나온다)
    public List<string> Local(IEnumerable<string> species) => species.Where(Ready).ToList();

    // 저장소에서 받아 둔다. 이미 있으면 바로 true. 같은 종을 받는 중이면 false (끝나면 Read 로 보인다)
    public async Task<bool> Fetch(string species)
    {
        if (Ready(species)) return true;
        if (!Manifest.TryGetValue(species, out var e)) return false;
        lock (fetching) if (!fetching.Add(species)) return false;
        try
        {
            using var c = http();
            var png = await Get(c, species + ".png", e.PngSize);
            var json = png == null ? null : await Get(c, species + ".json", e.JsonSize);
            if (png == null || json == null || !Ok(png, e.Png, e.PngSize) || !Ok(json, e.Json, e.JsonSize)) return false;
            Directory.CreateDirectory(cacheDir);
            Write(CachePath(species, "json"), json);
            Write(CachePath(species, "png"), png);
            return true;
        }
        catch (Exception x) when (x is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException) { return false; }
        finally { lock (fetching) fetching.Remove(species); }
    }

    // 목록보다 크면 그만 읽는다
    async Task<byte[]?> Get(HttpClient c, string file, long size)
    {
        using var r = await c.GetAsync(Url(file), HttpCompletionOption.ResponseHeadersRead);
        if (!r.IsSuccessStatusCode || r.Content.Headers.ContentLength is { } n && n != size) return null;
        await using var s = await r.Content.ReadAsStreamAsync();
        using var m = new MemoryStream();
        var buf = new byte[81920];
        for (int k; (k = await s.ReadAsync(buf)) > 0;)
        {
            m.Write(buf, 0, k);
            if (m.Length > size) return null;
        }
        return m.ToArray();
    }

    static void Write(string path, byte[] b)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, b);
        File.Move(tmp, path, true);
    }

    // 지금 목록에 없는 옛 파일(지난 버전 그림, 받다 만 것)은 지운다
    public void Cleanup()
    {
        if (assetsDir != null || !Directory.Exists(cacheDir)) return;
        var keep = Manifest.Keys.SelectMany(sp => new[] { CachePath(sp, "png"), CachePath(sp, "json") }).Select(Path.GetFileName).ToHashSet();
        foreach (var f in Directory.GetFiles(cacheDir))
            if (!keep.Contains(Path.GetFileName(f)))
                try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
