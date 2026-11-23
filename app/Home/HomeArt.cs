using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Bolok;

// 방석·집 물건 하나의 그림 데이터 (assets/home.json 한 줄). 칸 = 배율 1 도트, 좌표는 모두 물건 그림 왼쪽 위가 (0, 0)인 칸 좌표.
//   Rows 글자 그림 (한 글자 = 색표 Pal 의 색 하나, . 은 투명) · Door 집 문 안 (펫이 보이는 자리) · Front 펫을 덮는 앞 겹
//   Seat 펫 칸(40×40)의 아래 가운데가 놓이는 자리 — x 는 펫 「몸통 네모」의 가운데가 닿을 자리 (HomeArt.PetCell), 집은 빼꼼(idle) 때의 y
//   Sleep 잘 때 Seat 보다 더 내려가는 칸 · Top 캣타워 어른이 올라가 자는 자리
sealed class HomeItem
{
    public readonly string Id;
    public readonly bool House;
    public readonly int W, H, Sleep;
    public readonly (int X, int Y) Seat;
    public readonly (int X, int Y)? Top;
    public readonly (int Y, int X0, int X1)[] Door, Front;
    internal readonly string[] Rows;
    internal readonly Dictionary<char, Color> Pal;

    internal HomeItem(string id, JsonElement e)
    {
        Id = id;
        House = e.GetProperty("kind").GetString() == "house";
        (W, H) = (e.GetProperty("w").GetInt32(), e.GetProperty("h").GetInt32());
        Seat = (e.GetProperty("seat")[0].GetInt32(), e.GetProperty("seat")[1].GetInt32());
        Sleep = e.TryGetProperty("sleep", out var s) ? s.GetInt32() : 0;
        Top = e.TryGetProperty("top", out var t) ? (t[0].GetInt32(), t[1].GetInt32()) : null;
        Door = Runs(e, "door");
        Front = Runs(e, "front");
        Pal = e.GetProperty("pal").EnumerateObject().ToDictionary(p => p.Name[0], p => ParseColor(p.Value.GetString()!));
        Rows = e.GetProperty("rows").EnumerateArray().Select(r => r.GetString()!).ToArray();
        if (Rows.Length != H || Rows.Any(r => r.Length != W)) throw new InvalidDataException($"home.json {id}: 그림 줄 크기가 {W}×{H} 가 아니에요");
        foreach (var r in Rows) foreach (char c in r) if (c != '.' && !Pal.ContainsKey(c)) throw new InvalidDataException($"home.json {id}: 색표에 없는 글자 {c}");
    }

    static (int, int, int)[] Runs(JsonElement e, string key) =>
        e.TryGetProperty(key, out var a) ? a.EnumerateArray().Select(r => (r[0].GetInt32(), r[1].GetInt32(), r[2].GetInt32())).ToArray() : Array.Empty<(int, int, int)>();

    static Color ParseColor(string hex) => Color.FromArgb(Convert.ToInt32(hex[1..3], 16), Convert.ToInt32(hex[3..5], 16), Convert.ToInt32(hex[5..7], 16));

    // 칸 (x, y) 의 색 (투명이면 null)
    public Color? At(int x, int y) => Rows[y][x] is var c && c != '.' ? Pal[c] : null;
}

// 방석·집 그림: assets/home.json 읽기 (exe 안 / 개발 폴더), (물건, 배율)마다 그림 한 장과 펫을 가리는 Region 한 개를 만들어 둔다.
// 가려 그리기: 펫은 지금 창 그대로 그리고, 그릴 때 「가릴 자리」만 뺀다 — 방석 위는 앞 겹(Front), 집 안은 (문 안 Door 만 빼고 전부) + 앞 겹.
// 만든 Bitmap·Region 은 이 객체가 갖고 있다 (Dispose 하면 모두 놓는다)
sealed class HomeArt : IDisposable
{
    public const int PetCellPx = 40;

    readonly List<HomeItem> items = new();
    readonly Dictionary<string, HomeItem> byId = new();
    readonly Dictionary<(string, int), Bitmap> bodies = new();
    readonly Dictionary<(string, int, bool), Region> clips = new();

    public IReadOnlyList<HomeItem> Items => items;
    public IEnumerable<HomeItem> Cushions => items.Where(i => !i.House);
    public IEnumerable<HomeItem> Houses => items.Where(i => i.House);
    public HomeItem? Find(string id) => byId.GetValueOrDefault(id);

    HomeArt(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var p in doc.RootElement.GetProperty("items").EnumerateObject())
        {
            var it = new HomeItem(p.Name, p.Value);
            items.Add(it);
            byId[p.Name] = it;
        }
    }

    static HomeArt? embedded;
    public static HomeArt Embedded
    {
        get
        {
            if (embedded != null) return embedded;
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("home.json")!;
            return embedded = new HomeArt(s);
        }
    }

    // 개발: 저장소 assets/home.json 을 읽는다 (고치면 바로 다시 읽는다). 없으면 exe 안 것
    static (string Path, DateTime Time, HomeArt Art)? fromDir;
    public static HomeArt FromDir(string assetsDir)
    {
        var path = Path.Combine(assetsDir, "home.json");
        if (!File.Exists(path)) return Embedded;
        var t = File.GetLastWriteTimeUtc(path);
        if (fromDir is { } c && c.Path == path && c.Time == t) return c.Art;
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var art = new HomeArt(s);
        fromDir = (path, t, art);
        return art;
    }

    // 물건 그림 한 장 (배율 k, 투명 바탕). 칸 하나가 k×k 픽셀
    public Bitmap Body(HomeItem it, int k)
    {
        if (bodies.TryGetValue((it.Id, k), out var hit)) return hit;
        int w = it.W * k, h = it.H * k;
        var px = new int[w * h];
        for (int y = 0; y < it.H; y++)
            for (int x = 0; x < it.W; x++)
            {
                if (it.At(x, y) is not { } c) continue;
                int argb = c.ToArgb();
                for (int j = 0; j < k; j++) Array.Fill(px, argb, (y * k + j) * w + x * k, k);
            }
        var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(px, 0, data.Scan0, px.Length); // 줄 길이가 4 의 배수라 stride 가 w*4 와 같다
        b.UnlockBits(data);
        return bodies[(it.Id, k)] = b;
    }

    // 펫을 가릴 자리 (물건 그림 왼쪽 위 = (0, 0) 인 픽셀 좌표). inside = 집 문 안에 있다 → 문 안 말고는 다 가리고 앞 겹을 더 가린다.
    // 아니면(방석 위·캣타워 위) 앞 겹만. 호출한 쪽이 Clone 해서 쓴다 (여기 것은 고치지 않는다)
    public Region Clip(HomeItem it, int k, bool inside)
    {
        if (clips.TryGetValue((it.Id, k, inside), out var hit)) return hit;
        Region r;
        if (inside)
        {
            int m = PetCellPx * k; // 펫 칸이 물건 밖으로 나가는 만큼 넉넉히
            r = new Region(new Rectangle(-m, -m, it.W * k + 2 * m, it.H * k + 2 * m));
            foreach (var (y, x0, x1) in it.Door) r.Exclude(new Rectangle(x0 * k, y * k, (x1 - x0 + 1) * k, k));
        }
        else { r = new Region(); r.MakeEmpty(); }
        foreach (var (y, x0, x1) in it.Front) r.Union(new Rectangle(x0 * k, y * k, (x1 - x0 + 1) * k, k));
        return clips[(it.Id, k, inside)] = r;
    }

    // 펫 그림의 「몸통 네모」 가운데 (칸 좌표): 파란 z 글자를 뺀 불투명 칸의 가로 범위. 시안이 몸통 가운데로 맞췄다 (어린이 20.5, 어른 22.0 쯤)
    public static double BodyCenter(Bitmap petCell1)
    {
        int x0 = petCell1.Width, x1 = -1;
        for (int y = 0; y < petCell1.Height; y++)
            for (int x = 0; x < petCell1.Width; x++)
            {
                var c = petCell1.GetPixel(x, y);
                if (c.A == 0 || (c.B > 200 && c.R < 160)) continue; // 투명, z
                (x0, x1) = (Math.Min(x0, x), Math.Max(x1, x));
            }
        return x1 < 0 ? petCell1.Width / 2.0 : (x0 + x1 + 1) / 2.0;
    }

    // 펫 칸 왼쪽 위의 물건 칸 좌표. seatX 는 몸통 가운데가 닿을 자리, seatY 는 펫 칸 아래 줄
    public static (int X, int Y) PetCell(double seatX, int seatY, double bodyCenter) => ((int)Math.Floor(seatX - bodyCenter + .5), seatY - PetCellPx);

    public void Dispose()
    {
        foreach (var b in bodies.Values) b.Dispose();
        foreach (var r in clips.Values) r.Dispose();
        bodies.Clear();
        clips.Clear();
    }
}
