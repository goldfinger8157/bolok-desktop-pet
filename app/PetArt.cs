using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Bolok;

// 캐릭터 그림을 그림 파일 대신 데이터로 그린다 (YELL 2026-11-12: 색만 바꾼 분홍 볼록이도 그림 추가 없이 데이터 한 줄로).
// 데이터는 assets/pets/art.json (색·형태·자세·종) 하나. 처음엔 tools/sprites.py 의 pet() 을 옮겨 34종 × 700줄을 픽셀까지 맞췄고(2026-11-12),
// 그 뒤로는 여기가 캐릭터 그리기의 유일한 원본이다. 디버그 메뉴 「캐릭터 확인」으로 보며 고친다.
// 종 특징(귀 모양·꼬리·등·뿔·날개·무늬·얼굴·코·부리·몸 모양)까지 다 그린다. 모르는 데이터 키가 있는 종은 Can 이 false
public sealed class PetArt
{
    public const int Cell = 40;

    // 그릴 줄 아는 종 데이터 키. 이 밖의 키가 있으면 못 그린다 (오타 등)
    static readonly HashSet<string> Known = new()
    {
        "name", "note", "fur", "shade", "cream", "ear", "paw", "blush", "form_colors", "ears", "ear_k", "ear_out", "tail", "back", "spike", "horns", "wings",
        "marks", "cap", "mane", "whisker", "face", "snout", "belly", "nose", "beak", "smile", "teeth", "mouth_in", "no_cover", "body", "wide", "eye_k", "eye_up", "blush_k",
        "eye", "point", "ear_fill", "tongue", "sleepy", "munch", "stubby", "wing", "horn",
        "mouth", "glare", "light_lines",
    };
    static readonly string[] ColorKeys = { "fur", "shade", "cream", "ear", "paw", "blush", "eye" }; // eye: 렉돌 파란 눈
    static readonly string[] FrameKeys =
    {
        "sq", "dy", "lie", "dangle", "turn", "step", "puff", "eye", "mouth", "hold", "belly", "tear", "heart", "z", "blushy", "cover",
        "sweat", "hands", "up", "hammer", "dirt", "mark", "vein", "stars", "rot",
    };

    // art.json 의 객체 하나 (숫자·글·색·참거짓을 파이썬 dict.get 처럼 꺼낸다)
    sealed class Bag
    {
        readonly Dictionary<string, JsonElement> v = new();
        public Bag(JsonElement e) { foreach (var p in e.EnumerateObject()) v[p.Name] = p.Value.Clone(); }
        public IEnumerable<string> Keys => v.Keys;
        public bool Has(string k) => v.ContainsKey(k);
        public double N(string k, double d = 0) => v.TryGetValue(k, out var e)
            ? e.ValueKind switch { JsonValueKind.Number => e.GetDouble(), JsonValueKind.True => 1, JsonValueKind.False => 0, _ => d } : d;
        public bool T(string k) => v.TryGetValue(k, out var e) && e.ValueKind switch // 파이썬의 참거짓
        {
            JsonValueKind.Number => e.GetDouble() != 0, JsonValueKind.True => true, JsonValueKind.String => e.GetString()!.Length > 0,
            JsonValueKind.Array => e.GetArrayLength() > 0, JsonValueKind.Object => e.EnumerateObject().Any(), _ => false,
        };
        public string S(string k, string d) => v.TryGetValue(k, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString()! : d;
        public Bag? Obj(string k) => v.TryGetValue(k, out var e) && e.ValueKind == JsonValueKind.Object ? new Bag(e) : null;
        public IEnumerable<string> List(string k) => v.TryGetValue(k, out var e) && e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Select(x => x.GetString()!) : Array.Empty<string>();
    }

    readonly Dictionary<string, uint> colors = new();
    readonly Dictionary<string, (uint Color, double T)> tints = new();
    readonly List<(string Name, Bag F)> forms = new();
    readonly List<(string Name, Bag[] Frames)> poses = new();
    readonly Dictionary<string, Bag> species = new();
    readonly List<string> order = new();
    public readonly int Cheeks;

    PetArt(JsonElement root)
    {
        if (root.GetProperty("cell").GetInt32() != Cell) throw new InvalidDataException("art.json cell 은 40 이어야 해요");
        Cheeks = root.GetProperty("cheeks").GetInt32();
        foreach (var p in root.GetProperty("colors").EnumerateObject()) colors[p.Name] = Rgb(p.Value.GetString()!);
        foreach (var p in root.GetProperty("tints").EnumerateObject())
        {
            string c = p.Value[0].GetString()!;
            tints[p.Name] = (colors.TryGetValue(c, out var named) ? named : Rgb(c), p.Value[1].GetDouble());
        }
        foreach (var p in root.GetProperty("forms").EnumerateObject()) forms.Add((p.Name, new Bag(p.Value)));
        foreach (var p in root.GetProperty("poses").EnumerateObject())
        {
            var frames = p.Value.EnumerateArray().Select(f => new Bag(f)).ToArray();
            foreach (var f in frames)
                foreach (var k in f.Keys)
                    if (!FrameKeys.Contains(k)) throw new InvalidDataException($"art.json 자세 {p.Name}: 모르는 값 {k}");
            poses.Add((p.Name, frames));
        }
        foreach (var p in root.GetProperty("species").EnumerateObject()) { species[p.Name] = new Bag(p.Value); order.Add(p.Name); }
    }

    PetArt(PetArt from) // 종 하나를 더 얹을 때 (나머지는 같이 쓴다)
    {
        (colors, tints, forms, poses, Cheeks) = (from.colors, from.tints, from.forms, from.poses, from.Cheeks);
        species = new(from.species);
        order = new(from.order);
    }

    public static PetArt Parse(Stream s)
    {
        using var doc = JsonDocument.Parse(s);
        return new PetArt(doc.RootElement);
    }

    static PetArt? embedded;
    public static PetArt Embedded
    {
        get
        {
            if (embedded != null) return embedded;
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("pets/art.json")!;
            return embedded = Parse(s);
        }
    }

    // 개발: 저장소 assets/pets/art.json 을 읽는다 (고치면 바로 다시 읽는다). 없으면 exe 안 것
    static (string Path, DateTime Time, PetArt Art)? fromDir;
    public static PetArt FromDir(string assetsDir)
    {
        var path = Path.Combine(assetsDir, "pets", "art.json");
        if (!File.Exists(path)) return Embedded;
        var t = File.GetLastWriteTimeUtc(path);
        if (fromDir is { } c && c.Path == path && c.Time == t) return c.Art;
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var art = Parse(s);
        fromDir = (path, t, art);
        return art;
    }

    // 데이터 한 줄로 종 하나를 더한 그림 데이터 (시험·시안용: 분홍 볼록이 같은 것)
    public PetArt With(string id, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var art = new PetArt(this);
        art.species[id] = new Bag(doc.RootElement);
        if (!art.order.Contains(id)) art.order.Add(id);
        return art;
    }

    public IReadOnlyList<string> Species => order;

    // 형태의 몸 반지름 (칸 픽셀). 클릭 자리·머리 위 말풍선 높이가 이걸 따른다
    public (double Rx, double Ry) Size(string form) => forms.FirstOrDefault(f => f.Name == form).F is { } F ? (F.N("rx"), F.N("ry")) : (10.5, 10);

    // 발에서 머리 위(말풍선 자리)까지 칸 높이의 몇 배인지
    public double HeadRatio(string form) => form == "egg" ? .68 : (2 * Size(form).Ry + 1) / Cell + .075;
    public IEnumerable<string> Poses => poses.Select(p => p.Name);
    public bool Can(string id) => species.TryGetValue(id, out var s) && s.Keys.All(Known.Contains);
    public IEnumerable<string> Rows => forms.SelectMany(f => Enumerable.Range(0, Cheeks).SelectMany(c => poses.Select(p => $"{f.Name}/{c}/{p.Name}")));

    // 줄 이름 "형태/볼/자세" 가 있나
    public bool HasRow(string name)
    {
        var p = name.Split('/');
        return p.Length == 3 && forms.Any(f => f.Name == p[0]) && int.TryParse(p[1], out int c) && c >= 0 && c < Cheeks && poses.Any(x => x.Name == p[2]);
    }

    // 한 줄: 그 자세의 프레임들을 옆으로 이어 붙인 그림 (Sprites 가 처음 쓸 때 부른다)
    public (Bitmap Bmp, int Frames) Row(string id, string name)
    {
        if (!Can(id) || !HasRow(name)) throw new ArgumentException($"못 그리는 줄: {id} {name}");
        var p = name.Split('/');
        var F = forms.First(f => f.Name == p[0]).F;
        var frames = poses.First(x => x.Name == p[2]).Frames;
        return (Strip(frames.Select(f => Pet(species[id], p[0], F, int.Parse(p[1]), f))), frames.Length);
    }

    // 도감 썸네일: 어린이가 서 있는 그림 + 눈 감은 그림 (설정 창 도감, ICBM 구멍에서 빼꼼, 꿈)
    public Bitmap Thumb(string id)
    {
        var F = forms.First(f => f.Name == "child").F;
        var idle = poses.First(x => x.Name == "idle").Frames;
        return Strip(new[] { idle[0], idle[3] }.Select(f => Pet(species[id], "child", F, 0, f)));
    }

    static Bitmap Strip(IEnumerable<Canvas> frames)
    {
        var list = frames.ToList();
        int width = list.Count * Cell;
        var px = new int[width * Cell];
        for (int i = 0; i < list.Count; i++)
            for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++) px[y * width + i * Cell + x] = unchecked((int)list[i].P[y * Cell + x]);
        var bmp = new Bitmap(width, Cell, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, width, Cell), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < Cell; y++) Marshal.Copy(px, y * width, data.Scan0 + y * data.Stride, width);
        }
        finally { bmp.UnlockBits(data); }
        return bmp;
    }

    // ── 그리기 (반올림은 짝수 쪽, 정수로 바꿀 땐 0 쪽으로 자른다: 파이썬에서 옮겨 올 때 맞춘 그대로) ──

    static uint Rgb(string hex) => 0xFF000000u | Convert.ToUInt32(hex[1..], 16);
    static uint Rgb(int r, int g, int b) => 0xFF000000u | (uint)(r << 16 | g << 8 | b);
    static int R(double v) => (int)Math.Round(v, MidpointRounding.ToEven);
    static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);

    static uint Mix(uint a, uint b, double t)
    {
        int C(uint v, int sh) => (int)(v >> sh & 255);
        int M(int x, int y) => R(x + (y - x) * t);
        return Rgb(M(C(a, 16), C(b, 16)), M(C(a, 8), C(b, 8)), M(C(a, 0), C(b, 0)));
    }

    sealed class Canvas
    {
        public uint[] P = new uint[Cell * Cell]; // 0 = 빈 칸

        public void Put(int x, int y, uint c, bool clip = false)
        {
            if (0 <= x && x < Cell && 0 <= y && y < Cell && (!clip || P[y * Cell + x] != 0)) P[y * Cell + x] = c;
        }

        public void Ell(double cx, double cy, double rx, double ry, uint c, bool clip = false)
        {
            if (rx <= 0 || ry <= 0) return;
            for (int y = Math.Max(0, (int)(cy - ry) - 1); y < Math.Min(Cell, (int)(cy + ry) + 2); y++)
                for (int x = Math.Max(0, (int)(cx - rx) - 1); x < Math.Min(Cell, (int)(cx + rx) + 2); x++)
                {
                    double a = (x + .5 - cx) / rx, b = (y + .5 - cy) / ry;
                    if (a * a + b * b < 1) Put(x, y, c, clip);
                }
        }

        public void Pix(uint c, params (double X, double Y)[] pts) { foreach (var (x, y) in pts) Put(R(x), R(y), c); }

        // 네모에 가까운 둥근 몸 (초타원 |x|^3 + |y|^3 < 1): 카피바라처럼 각진 통통이
        public void Box(double cx, double cy, double rx, double ry, uint c, bool clip = false)
        {
            if (rx <= 0 || ry <= 0) return;
            for (int y = Math.Max(0, (int)(cy - ry) - 1); y < Math.Min(Cell, (int)(cy + ry) + 2); y++)
                for (int x = Math.Max(0, (int)(cx - rx) - 1); x < Math.Min(Cell, (int)(cx + rx) + 2); x++)
                {
                    double a = Math.Abs((x + .5 - cx) / rx), b = Math.Abs((y + .5 - cy) / ry);
                    if (a * a * a + b * b * b < 1) Put(x, y, c, clip);
                }
        }

        // 두 점을 잇는 한 칸 굵기 선 (드래곤 날개 뼈대)
        public void Line(double x0, double y0, double x1, double y1, uint c, bool clip = false)
        {
            int n = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0))));
            for (int i = 0; i <= n; i++) Put(R(x0 + (x1 - x0) * i / n), R(y0 + (y1 - y0) * i / n), c, clip);
        }

        // 다각형 채우기 (픽셀 가운데가 안에 들면)
        public void Poly((double X, double Y)[] pts, uint c, bool clip = false)
        {
            double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            for (int y = Math.Max(0, (int)minY - 1); y < Math.Min(Cell, (int)maxY + 2); y++)
                for (int x = Math.Max(0, (int)minX - 1); x < Math.Min(Cell, (int)maxX + 2); x++)
                {
                    double px = x + .5, py = y + .5;
                    bool inside = false;
                    for (int i = 0; i < pts.Length; i++)
                    {
                        var (x1, y1) = pts[i];
                        var (x2, y2) = pts[(i + pts.Length - 1) % pts.Length];
                        if ((y1 > py) != (y2 > py) && px < (x2 - x1) * (py - y1) / (y2 - y1) + x1) inside = !inside;
                    }
                    if (inside) Put(x, y, c, clip);
                }
        }

        public void Outline(uint c)
        {
            var edge = new List<int>();
            for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                {
                    if (P[y * Cell + x] != 0) continue;
                    bool Near(int a, int b) => 0 <= x + a && x + a < Cell && 0 <= y + b && y + b < Cell && P[(y + b) * Cell + x + a] != 0;
                    if (Near(1, 0) || Near(-1, 0) || Near(0, 1) || Near(0, -1)) edge.Add(y * Cell + x);
                }
            foreach (var i in edge) P[i] = c;
        }

        public void Rotate() // 90도 (파이썬: p[y][x] = p[CELL-1-x][y])
        {
            var n = new uint[Cell * Cell];
            for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++) n[y * Cell + x] = P[(Cell - 1 - x) * Cell + y];
            P = n;
        }
    }

    static readonly int[] Sides = { -1, 1 };
    static uint Col(Bag S, string k) => Rgb(S.S(k, "#000000"));

    Canvas Pet(Bag S, string form, Bag F, int cheek, Bag f)
    {
        var c = new Dictionary<string, uint>(colors);
        foreach (var k in ColorKeys) if (S.Has(k)) c[k] = Col(S, k);
        if (S.Has("snout")) c["snout"] = Col(S, "snout");
        if (S.Has("form_colors")) // 햄스터: 형태마다 털색을 따로 정해 두었다
        {
            if (S.Obj("form_colors")!.Obj(form) is { } fc) foreach (var k in fc.Keys) c[k] = Col(fc, k);
        }
        else if (tints.TryGetValue(form, out var t)) // 다른 종: 아기는 밝게, 심술이는 어둡게, 애교쟁이는 살짝 따뜻하게
            (c["fur"], c["shade"]) = (Mix(c["fur"], t.Color, t.T), Mix(c["shade"], t.Color, t.T));

        var g = new Canvas();
        double wide = S.N("wide", 1);
        double rx = F.N("rx") * wide, ry = F.N("ry") / Math.Sqrt(wide);
        const double ground = 37;
        double lie = f.N("lie"), sq = f.N("sq"), dangle = f.N("dangle");
        bool lying = lie != 0;
        if (lying) (rx, ry) = (rx + 2, ry - 2.5); // 엎드려 자기: 납작하고 넓게
        (rx, ry) = (rx + sq * .5, ry - sq * .5);
        double cx = 20.5, cy = ground - ry - 1 + f.N("dy");
        if (dangle != 0) { (ry, rx) = (ry + 1, rx - 1); cy = 19; } // 들렸을 때는 몸이 아래로 늘어진다
        string body = S.S("body", "");
        if (body == "ghost" && !lying) cy -= 2; // 유령은 살짝 떠 있다
        if (body == "orb" && !lying) cy -= 1.5; // 몽마: 발 없이 동그랗게 살짝 떠 있다
        double turn = f.N("turn");
        int fx = R(20 + turn * (rx > 9 ? 1 : .7));       // 고개 돌린 쪽으로 얼굴이 옮겨간다
        int gap = R(F.N("eyegap") - Math.Abs(turn) * .4); // 옆을 보면 눈 사이가 좁아 보인다
        int ey = R(cy - ry * .24) - (int)S.N("eye_up"), my = R(cy + ry * (S.S("face", "") == "flat" ? .14 : .3)); // 엑조틱: 코·입이 눈 사이로 올라붙은 납작 얼굴

        // 1) 실루엣 — 꼬리·등(가시·털뭉치)·귀·뿔·날개 → 몸·볼주머니·발
        double ear = F.N("ear") * S.N("ear_k", 1), ex = rx * .62;
        string ears = S.S("ears", "round");
        bool roundEars = ears is "round" or "small" or "big";
        double RoundK(bool inner) => ears switch { "round" => inner ? .55 : 1, "small" => inner ? .4 : .75, _ => inner ? 1.1 : 1.7 };
        double[] earX = { cx + -1 * ex + (fx - 20) * .4, cx + 1 * ex + (fx - 20) * .4 };
        double fk = F.N("feature", 1); // 형태마다 꼬리·뿔·날개·가시 크기 (아기는 작게, 어른은 다 자라게)
        Behind(g, S, c, cx, cy, rx, ry, lying, fk);
        if (roundEars)
        {
            if (ears == "big") earX = new[] { cx + -1 * rx * .82, cx + 1 * rx * .82 };
            foreach (var x in earX) g.Ell(x, cy - ry * (ears == "big" ? .7 : .86), ear * RoundK(false), ear * RoundK(false), c["fur"]);
        }
        else if (ears is "pointy" or "fox" or "tuft")
        {
            double h = ear * (ears == "fox" ? 2.1 : ears == "pointy" ? 1.7 : 1.1);
            foreach (int s in Sides) g.Poly(EarTri(cx, cy, rx, ry, s, h, fx), S.Has("ear_fill") ? Col(S, "ear_fill") : c["fur"]);
        }
        else if (ears == "side")
            foreach (int s in Sides) g.Ell(cx + s * (rx + .6), cy - ry * .4, ear * 1.1, ear * .55, c["fur"]);
        else if (ears == "long") // 알파카: 길쭉하게 선 귀
            foreach (int s in Sides) g.Ell(cx + s * rx * .48, cy - ry - 1.5, 1.7, ear * 1.3, c["fur"]);
        else if (ears == "fold") // 스코티시폴드: 앞으로 접혀 머리에 납작하게 붙은 작은 귀
            foreach (var x in earX) g.Ell(x, cy - ry * .9, ear * 1.05, ear * .62, S.Has("ear_fill") ? Col(S, "ear_fill") : c["fur"]); // 퍼그는 까만 귀
        if (S.Has("ear_out") && roundEars) // 판다: 귀 바깥이 까맣다
            foreach (var x in earX) { double r = ear * RoundK(false); g.Ell(x, cy - ry * .86, r, r, Col(S, "ear_out")); }
        if (body == "box") g.Box(cx, cy, rx, ry, c["shade"]); else g.Ell(cx, cy, rx, ry, c["shade"]);
        bool pear = F.T("pear") && !lying && dangle == 0 && body == ""; // 통통이: 아래가 퍼진 서양배 모양
        if (pear) g.Ell(cx, cy + ry * .3, rx * 1.08, ry * .72, c["shade"]);
        if (body == "blob" && !lying) g.Ell(cx, cy + ry * .5, rx * 1.12, ry * .55, c["shade"]); // 슬라임: 바닥이 퍼진 덩어리
        else if (body == "ghost" && !lying) // 유령: 아래가 한쪽으로 가늘어지는 연기 꼬리
        {
            var puffs = new[] { (.15, .85, rx * .72), (.55, 1.05, 3.2), (.85, 1.18, 2.1), (1.05, 1.15, 1.3) };
            for (int i = 0; i < puffs.Length; i++)
            {
                var (x, y, r) = puffs[i];
                g.Ell(cx + x * rx, cy + y * ry, r, r * (i == 0 ? .7 : 1), c["shade"]);
            }
        }
        double bulge = cheek * 2.2 + f.N("puff") * 1.2; // 먹은 화면이 많을수록 불룩하다
        if (bulge != 0)
            foreach (int s in Sides) g.Ell(fx + .5 + s * rx * .72, cy + ry * .22, 2 + bulge * .75, 2.4 + bulge * .6, c["cream"]);
        if (body == "octo" && !lying) // 문어: 발 대신 다리 다섯
        {
            int step = (int)f.N("step");
            for (int i = -2; i < 3; i++)
            {
                int lift = step != 0 && (i + step) % 2 == 0 ? 1 : 0;
                g.Ell(cx + i * rx * .4, cy + ry - lift, 1.9, 2.6, c["shade"]);
            }
        }
        else if (body is "blob" or "ghost" or "orb" && !lying)
        {
            if (dangle != 0) // 들리면 작은 팔
                foreach (int s in Sides) g.Ell(cx + s * (rx + .2), cy + 1, 2, 1.6, c["shade"]);
        }
        else if (body is "ghost" or "orb") { }
        else if (!lying)
        {
            int step = (int)f.N("step");
            for (int i = 0; i < 2; i++)
            {
                int s = Sides[i];
                double lift = step == i + 1 ? 1 : 0;
                if (dangle != 0) lift = -.6 * (((int)dangle + i) % 2);
                bool stubby = S.T("stubby"); // 웰시코기: 짧은 다리 (발이 납작하고 조금 벌어진다)
                g.Ell(cx + s * rx * (stubby ? .6 : .5), cy + ry + .5 - lift, 2.8 * F.N("paw_k", 1) * (stubby ? 1.1 : 1), 1.9 * F.N("paw_k", 1) * (stubby ? .55 : 1), c["paw"]); // 아기는 발이 짧다
                if (dangle != 0) g.Ell(cx + s * (rx + .4), cy + 1, 2.2, 1.7, c["paw"]); // 번쩍 들리면 팔이 벌어진다
            }
        }

        // 2) 안쪽 칠하기 — 테두리 한 겹을 그림자로 남기고 털·배를 올린다
        if (body == "box") g.Box(cx, cy - .8, rx - 1, ry - .6, c["fur"], true); else g.Ell(cx, cy - .8, rx - 1, ry - .6, c["fur"], true);
        if (pear) g.Ell(cx, cy + ry * .3 - .8, rx * 1.08 - 1, ry * .72 - .6, c["fur"], true);
        if (body == "blob" && !lying) g.Ell(cx, cy + ry * .45, rx * 1.05, ry * .45, c["fur"], true);
        else if (body == "ghost" && !lying) g.Ell(cx + rx * .15, cy + ry * .75, rx * .65, ry * .45, c["fur"], true);
        if (roundEars)
            foreach (var x in earX) { double r = ear * RoundK(true); g.Ell(x, cy - ry * (ears == "big" ? .7 : .86), r, r, c["ear"]); }
        else if (ears == "long")
            foreach (int s in Sides) g.Ell(cx + s * rx * .48, cy - ry - 1.5, .8, ear * .9, c["ear"], true);
        else if (ears is "pointy" or "fox")
            foreach (int s in Sides)
            {
                var tri = EarTri(cx, cy, rx, ry, s, ear * (ears == "fox" ? 2.1 : 1.7), fx);
                double mx = (tri[0].X + tri[1].X + tri[2].X) / 3, mY = (tri[0].Y + tri[1].Y + tri[2].Y) / 3;
                g.Poly(tri.Select(p => (mx + (p.X - mx) * .5, mY + (p.Y - mY) * .5 + .5)).ToArray(), c["ear"], true);
                if (ears == "fox") // 여우 귀 끝은 짙다
                    g.Poly(new[] { tri[2], (tri[2].X + (tri[0].X - tri[2].X) * .3, tri[2].Y + (tri[0].Y - tri[2].Y) * .3),
                        (tri[2].X + (tri[1].X - tri[2].X) * .3, tri[2].Y + (tri[1].Y - tri[2].Y) * .3) }, Rgb(70, 44, 34), true);
            }
        string face = S.S("face", "");
        if (face == "bigbelly") g.Ell(fx + .5, cy + ry * .25, rx * .78, ry * .8, c["cream"], true); // 펭귄: 얼굴부터 배까지 하얗다
        else if (face == "wool") g.Ell(fx + .5, cy + ry * .05, rx * .6, ry * .62, c["cream"], true); // 양: 털 속에 얼굴만
        else if (!S.Has("belly") || S.T("belly")) g.Ell(fx + .5, cy + ry * .82, rx * .86, ry * .82, c["cream"], true);
        if (face == "capy") g.Box(fx + .5, my - .4, rx * .56, ry * .38, Col(S, "snout"), true); // 카피바라: 크고 뭉툭한 네모 주둥이
        else if (face == "snout") g.Ell(fx + .5, my - .5, rx * .42, ry * .3, Col(S, "snout"), true);
        else if (face == "cheeks") g.Ell(fx + .5, my + .5, rx * .62, ry * .34, c["cream"], true); // 여우·레서판다: 볼과 주둥이가 하얗다
        else if (face == "urajiro") g.Ell(fx + .5, my + .3, rx * .76, ry * .42, c["cream"], true); // 시바: 주둥이부터 볼·턱까지 넓게 크림색(우라지로)
        else if (face == "muzzle") // 리트리버: 도톰하게 튀어나온 크림색 주둥이와 양쪽으로 처진 볼살 (YELL: 특징은 주댕이)
        {
            uint rim = Mix(c["shade"], c["cream"], .15);
            double mw = rx * .52, mh = ry * .31;
            g.Ell(fx + .5, my - .4, mw + 1, mh + 1, rim, true);
            foreach (int s in Sides) g.Ell(fx + .5 + s * mw * .5, my + mh * .55, mw * .55 + 1, mh * .6 + 1, rim, true);
            g.Ell(fx + .5, my - .4, mw, mh, c["cream"], true);
            foreach (int s in Sides) g.Ell(fx + .5 + s * mw * .5, my + mh * .55, mw * .55, mh * .6, c["cream"], true);
        }
        if (face == "pale") g.Ell(fx + .5, cy - ry * .08, rx * .72, ry * .56, c["cream"], true); // 수달: 얼굴이 밝다
        else if (face is not ("bigbelly" or "wool" or "frog" or "plain")) g.Ell(fx + .5, my, rx * .36, ry * .26, c["cream"], true); // plain: 주둥이 없이 (까망 악마)
        double sk = Math.Min(1, F.N("rx") / 10.5); // 아기는 얼굴이 작아서 무늬도 작게
        Marks(g, S, c, cx, cy, rx, ry, fx, ey, my, gap, true, sk);
        foreach (int s in Sides)
            g.Ell(fx + .5 + s * (F.N("eyegap") + 1.8), cy + ry * .1, (2 + F.N("blush") * .3) * S.N("blush_k", 1), 1.2, c["blush"], true);
        if (ears == "flop") // 리트리버: 머리 위 옆에서 시작해 볼 옆으로 축 처진 귀. 얼굴 위에 덮고 둘레 한 칸을 짙게 둘러 갈라 보이게
        {
            uint earColor = S.Has("ear_fill") ? Col(S, "ear_fill") : Mix(c["fur"], c["shade"], .45), rim = Mix(c["shade"], c["line"], .12);
            foreach (int s in Sides) // 물방울처럼 아래가 조금 넓게 (위는 머리에 붙고 끝은 볼 옆으로 늘어진다)
            {
                double ex2 = cx + s * rx * .84, ey2 = cy - ry * .15, erx = ear * .62, ery = ear * 1.6;
                double bx = ex2 + s * .4, by = ey2 + ery * .5, brx = erx * 1.25, bry = ery * .5;
                g.Ell(ex2, ey2, erx + 1, ery + 1, rim);
                g.Ell(bx, by, brx + 1, bry + 1, rim);
                g.Ell(ex2, ey2, erx, ery, earColor);
                g.Ell(bx, by, brx, bry, earColor);
            }
        }

        // 3) 윤곽선 → 얼굴
        g.Outline(c["line"]);
        if (S.S("tail", "") == "wrap") // 몽마 꼬리: 테두리 없는 가는 한 칸 선 (몸에서 나와 고리 한 번 → 끝 세모까지)
        {
            uint tl = Mix(S.Has("point") ? Col(S, "point") : c["shade"], c["line"], .35);
            var path = WrapTail(cx, cy, rx, ry, F.N("feature", 1));
            for (int i = 1; i < path.Count; i++) g.Line(path[i - 1].X, path[i - 1].Y, path[i].X, path[i].Y, tl);
        }
        Marks(g, S, c, cx, cy, rx, ry, fx, ey, my, gap, false, sk);
        if (ears == "fold") // 접힌 자리: 귀 아래쪽에 짧은 선
            foreach (var x in earX)
            {
                int y = R(cy - ry * .9 + ear * .2), x0 = R(x);
                foreach (int dx in new[] { -1, 0, 1 }) g.Put(x0 + dx, y, c["line"], true);
            }
        if (F.T("tuft") && !HeadMark(S)) // 아기: 정수리에 털 한 가닥 (머리에 갓·갈기·뿔이 있는 종은 빼고)
        {
            int top = (int)Math.Ceiling(cy - ry - .5) - 1; // 윤곽선 줄
            g.Pix(c["line"], (fx, top - 1), (fx, top - 2), (fx + 1, top - 3), (fx + 2, top - 2));
        }
        if (face == "frog") ey = R(cy - ry * .78); // 개구리 눈은 머리 위 혹에
        string eyeKind = f.S("eye", "open");
        if (S.T("glare") && eyeKind == "open") eyeKind = "angry"; // 까망 악마: 평소에도 째려본다
        Eyes(g, eyeKind, fx, ey, gap, F.N("eye") * S.N("eye_k", 1), c, F.T("lash"), F.T("lid") || S.T("sleepy")); // 카피바라는 늘 느긋하게 반쯤 감은 눈
        if (F.T("brow")) // 심술이: 늘 찌푸린 눈썹 (몸 밖으로는 안 나가게: 개구리처럼 눈이 머리 위에 있으면 더듬이처럼 보인다)
            foreach (int s in Sides)
            {
                int x = fx + s * gap;
                foreach (var (bx, by) in new[] { (x - 2 * s, ey - 3), (x - s, ey - 4), (x, ey - 5), (x + s, ey - 5), (x + 2 * s, ey - 6) }) g.Put(bx, by, c["line"], true);
            }
        string beak = S.S("beak", ""), mk = f.S("mouth", S.S("mouth", "w")); // 자세가 입을 안 정하면 종마다 평소 입 (까망 악마는 삐죽)
        if (S.T("munch") && !f.Has("mouth") && (f.Has("sq") || f.Has("dy"))) mk = "chew"; // 리트리버: 가만히·걸을 때 입을 오물오물 (찹츄찹츄)
        if (beak != "")
        {
            if (mk is "open" or "wide" or "o") Mouth(g, mk, fx, my, c);
            Beak(g, beak, fx, my, c);
        }
        else
        {
            if (S.T("light_lines")) c = new Dictionary<string, uint>(c) { ["line"] = Mix(c["fur"], c["white"], .55) }; // 까만 몸: 입 선이 묻히지 않게 밝게
            string nose = S.S("nose", "pink");
            if (nose is not ("none" or "snoot")) Nose(g, nose, fx, my, c);
            if (S.T("smile") && mk == "w") // 쿼카: 늘 활짝 웃는다
            {
                g.Pix(c["line"], (fx - 3, my), (fx - 2, my + 1), (fx - 1, my + 2), (fx, my + 2), (fx + 1, my + 2), (fx + 2, my + 2), (fx + 3, my + 1), (fx + 4, my));
                g.Pix(c["red"], (fx - 1, my + 1), (fx, my + 1), (fx + 1, my + 1), (fx + 2, my + 1));
            }
            else if (face == "frog" && mk == "w") // 개구리: 넓게 웃는 입
                g.Pix(c["line"], Enumerable.Range(-4, 9).Select(i => ((double)(fx + i), (double)(my + (Math.Abs(i) < 3 ? 1 : 0)))).ToArray());
            else Mouth(g, mk, fx, my, S.Has("mouth_in") ? new Dictionary<string, uint>(c) { ["red"] = Col(S, "mouth_in") } : c);
            if (nose == "snoot") Nose(g, nose, fx, my, c); // 돼지 코는 입보다 앞에 (입을 벌려도 코가 보이게)
            if (S.T("tongue") && mk == "w") // 시바: 살짝 내민 분홍 혀
            {
                g.Pix(Rgb(236, 112, 128), (fx, my + 1), (fx + 1, my + 1), (fx, my + 2), (fx + 1, my + 2));
                g.Pix(c["line"], (fx - 1, my + 2), (fx + 2, my + 2), (fx, my + 3), (fx + 1, my + 3), (fx + 2, my + 1));
            }
            if (S.T("teeth") && mk is "w" or "chew" or "flat") // 비버 앞니
            {
                g.Pix(c["line"], (fx - 1, my + 1), (fx + 2, my + 1), (fx - 1, my + 2), (fx + 2, my + 2), (fx, my + 3), (fx + 1, my + 3));
                g.Pix(S.S("teeth", "").StartsWith('#') ? Col(S, "teeth") : c["white"], (fx, my + 1), (fx + 1, my + 1), (fx, my + 2), (fx + 1, my + 2)); // 비버 앞니는 주황
            }
        }

        // 4) 소품
        if (f.T("hold")) // 앞발로 씨앗을 쥐고 오물오물
        {
            g.Ell(fx + .5, my + 6, 2.4, 3.2, c["line"]);
            g.Ell(fx + .5, my + 6, 1.5, 2.3, c["seed"]);
            g.Pix(c["stripe"], (fx, my + 5), (fx, my + 7));
            foreach (int s in new[] { -1, 1 }) g.Ell(fx + .5 + s * 3.2, my + 6, 1.8, 1.6, c["paw"]);
        }
        if (f.T("belly")) g.Pix(c["shade"], (fx - 1, my + 6), (fx, my + 5), (fx + 1, my + 6)); // 꼬르륵
        if (f.T("tear")) g.Pix(c["blue"], (fx - gap - 2, ey + 3), (fx - gap - 2, ey + 4), (fx - gap - 2, ey + 5));
        if (f.T("heart")) Heart(g, 32, 2, c["red"]);
        if (f.Has("z"))
        {
            int z = (int)f.N("z"), x = 31 + z, y = 5 - z * 2;
            var pts = new List<(double, double)>();
            for (int i = 0; i < 5; i++) pts.Add((x + i, y));
            pts.AddRange(new (double, double)[] { (x + 3, y + 1), (x + 2, y + 2), (x + 1, y + 3) });
            for (int i = 0; i < 5; i++) pts.Add((x + i, y + 4));
            g.Pix(c["blue"], pts.ToArray());
        }
        if (f.T("blushy"))
            foreach (int s in new[] { -1, 1 }) g.Ell(fx + .5 + s * (gap + 1.5), cy + ry * .14, 3, 1.5, c["blush"]);
        if (f.T("cover")) // 앞발로 눈을 가리고 볼이 빨개진다 (유령은 가리지 않는다)
            foreach (int s in new[] { -1, 1 })
            {
                g.Ell(fx + .5 + s * (gap + 1.5), cy + ry * .14, 3, 1.5, c["blush"]);
                if (S.T("no_cover")) continue; // 유령은 가리지 않는다 (부우의 특징)
                g.Ell(fx + .5 + s * gap, ey + 1, 3.3, 2.9, c["line"]);
                g.Ell(fx + .5 + s * gap, ey + 1, 2.4, 2, c["paw"]);
            }
        if (f.Has("sweat"))
        {
            int y = ey - 6 + (int)f.N("sweat") * 2, x = fx + gap + 5;
            g.Pix(c["blue"], (x, y), (x - 1, y + 1), (x, y + 1), (x + 1, y + 1), (x, y + 2));
        }
        if (f.Has("hands")) // 앞발로 얼굴을 비빈다
            foreach (int s in new[] { -1, 1 })
            {
                g.Ell(fx + .5 + s * 3, my - f.N("hands"), 2.4, 2, c["line"]);
                g.Ell(fx + .5 + s * 3, my - f.N("hands"), 1.5, 1.2, c["paw"]);
            }
        if (f.Has("up")) // 앞발을 머리 위로 올려 쟁반을 받친다
            foreach (int s in new[] { -1, 1 })
            {
                g.Ell(fx + .5 + s * rx * .6, cy - ry - f.N("up"), 2.4, 2, c["line"]);
                g.Ell(fx + .5 + s * rx * .6, cy - ry - f.N("up"), 1.5, 1.2, c["paw"]);
            }
        if (f.Has("hammer")) Hammer(g, (int)f.N("hammer"), (int)(fx + rx), (int)cy, c);
        if (f.Has("dirt")) // 튀는 흙
        {
            int d = (int)f.N("dirt");
            g.Pix(c["seed"], (4 + d, 34 - d * 3), (7 - d, 31 - d), (35 - d, 33 - d * 2), (32 + d, 30 + d));
        }
        string mark = f.S("mark", "");
        if (mark == "dots")
            foreach (int x in new[] { 31, 34, 37 }) g.Pix(c["line"], (x, 8), (x + 1, 8), (x, 9), (x + 1, 9));
        if (f.T("vein")) // 빠직
            g.Pix(c["red"], (33, 3), (33, 4), (32, 4), (36, 3), (36, 4), (37, 4), (33, 7), (33, 8), (32, 7), (36, 7), (36, 8), (37, 7));
        if (mark == "!")
        {
            var pts = new List<(double, double)>();
            foreach (int x in new[] { 34, 35 }) for (int y = 2; y < 8; y++) pts.Add((x, y));
            pts.Add((34, 9)); pts.Add((35, 9));
            g.Pix(c["red"], pts.ToArray());
        }
        if (mark == "?") g.Pix(c["blue"], (33, 3), (34, 2), (35, 2), (36, 2), (37, 3), (37, 4), (36, 5), (35, 6), (35, 7), (35, 9));
        if (f.Has("stars")) // 머리 위를 도는 별
            for (int i = 0; i < 3; i++)
            {
                double a = (f.N("stars") / 4 + i / 3.0) * 2 * Math.PI;
                int x = R(cx + Math.Cos(a) * (rx - 1)), y = R(cy - ry - 1 + Math.Sin(a) * 3);
                foreach (var (u, v) in new[] { (0, 0), (-1, 0), (1, 0), (0, -1), (0, 1) }) g.Put(x + u, y + v, Rgb(255, 214, 80));
            }
        if (F.T("flower")) // 애교쟁이: 귀에 꽃
        {
            int x = R(earX[1]), y = R(cy - ry * .95);
            foreach (var (a, b) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) }) g.Put(x + a, y + b, Rgb(255, 120, 170));
            g.Put(x, y, Rgb(255, 236, 110));
        }
        for (int i = 0; i < (int)f.N("rot"); i++) g.Rotate(); // 90도씩 돌리기
        return g;
    }

    // 뾰족 귀 삼각형: 머리 위 양옆, 끝이 바깥으로 살짝
    static (double X, double Y)[] EarTri(double cx, double cy, double rx, double ry, int s, double h, int fx) => new[]
    {
        (cx + s * rx * .18 + (fx - 20) * .3, cy - ry * .78),
        (cx + s * rx * .86 + (fx - 20) * .3, cy - ry * .42),
        (cx + s * rx * .62 + (fx - 20) * .3, cy - ry - h * .9),
    };

    // 아기 머리털을 안 그리는 종: 머리에 갓·털뭉치·이끼·갈기·뿔이 있거나, 뾰족 머리가 다른 캐릭터를 닮는 몸(슬라임·유령)
    static bool HeadMark(Bag S) => S.List("marks").Any(m => m is "cap" or "topknot" or "moss" or "hair" or "tuft" or "mane") || S.S("horns", "") is "unicorn" or "one"
        || S.S("body", "") is "blob" or "ghost" or "orb";

    // 몽마 꼬리 길: 몸 오른쪽 아래에서 가는 선으로 나와 S자로 살짝 휘며 오른쪽 위로 (YELL 이 보낸 악마 꼬리 그림). 끝에 뾰족한 스페이드. 칸 밖으로는 안 나가게
    static List<(double X, double Y)> WrapTail(double cx, double cy, double rx, double ry, double fk)
    {
        double ex = Math.Min(cx + rx + 1.5 + 2.5 * fk, 36.5);
        (double X, double Y) p0 = (cx + rx * .8, cy + ry * .5), p1 = (Math.Min(cx + rx + 3.5 * fk, 38), cy + ry * .75),
            p2 = (cx + rx + .5 * fk, cy + ry * .05), p3 = (ex, cy - ry * (.2 + .25 * fk));
        var pts = new List<(double, double)>();
        for (int i = 0; i <= 16; i++)
        {
            double t = i / 16.0, u = 1 - t;
            pts.Add((u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X,
                     u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y));
        }
        return pts;
    }

    // 몸 뒤에 그리는 것: 꼬리, 가시·털뭉치, 뿔, 날개
    // fk: 형태마다 크기 (아기 .5 → 어른 1). soft 는 너무 작아지면 안 보이는 것(털뭉치·지느러미)용
    void Behind(Canvas g, Bag S, Dictionary<string, uint> c, double cx, double cy, double rx, double ry, bool lying, double fk)
    {
        uint shade = c["shade"], cream = c["cream"], dark = Mix(c["shade"], colors["line"], .35);
        double soft = .5 + .5 * fk;
        switch (S.S("tail", ""))
        {
            case "thin": // 고양이: 오른쪽 뒤로 휘어 올라간다
                for (int i = 0; i < 9; i++)
                {
                    double a = -.6 + i * .2;
                    g.Ell(cx + rx * .75 + Math.Cos(a) * 4 * fk + i * .3 * fk, cy + ry * .55 - i * 1.3 * fk, 1.5 * soft, 1.5 * soft, shade);
                }
                break;
            case "fluffy": // 여우: 크고 끝이 하얗다
                g.Ell(cx + rx * .95, cy + ry * .1, 4.2 * fk, ry * .8, shade);
                g.Ell(cx + rx * .95 + fk, cy - ry * .55, 3 * fk, 2.6 * fk, cream);
                break;
            case "curl": // 시바: 등 위로 말린 꼬리 (몸 옆으로 비죽 나와 보이게)
                g.Ell(cx + rx * .95, cy - ry * .35, 3.6 * soft, 3.4 * soft, shade);
                g.Ell(cx + rx * .95, cy - ry * .35, 1.6 * soft, 1.5 * soft, cream);
                break;
            case "bushy": // 다람쥐: 머리보다 높이 솟는 큰 꼬리
                g.Ell(cx + rx * .75, cy - ry * .45, 5.5 * fk, ry * 1.05 * soft, shade);
                g.Ell(cx + rx * .75 + 1.5 * fk, cy - ry * (.45 + .7 * soft), 3.6 * fk, 3.2 * fk, shade);
                break;
            case "ring": // 너구리: 줄무늬 꼬리
                g.Ell(cx + rx * .95, cy + ry * .05, 3.4 * soft, ry * .78, shade);
                for (int i = -2; i < 3; i++)
                {
                    int y = R(cy + ry * .05 + i * ry * .3), x0 = R(cx + rx * .95), w = R(3 * soft);
                    g.Pix(dark, Enumerable.Range(-w, w * 2 + 1).Select(dx => ((double)(x0 + dx), (double)y)).ToArray());
                }
                break;
            case "flat": // 비버: 옆으로 비죽 나온 넓적한 꼬리, 비늘 무늬
            {
                uint paddle = Rgb(110, 80, 60), scale = Rgb(84, 60, 44);
                double tx = cx + rx * .95 + 1.5 * fk, ty = cy + ry * .5, w = 3.6 * soft, h = Math.Max(2.2, ry * .55);
                g.Ell(tx, ty, w, h, paddle);
                for (int y = R(ty - h) + 1; y < R(ty + h); y += 2)
                    for (int x = R(tx - w) + 1 + (y / 2 % 2); x < R(tx + w); x += 2) g.Put(x, y, scale, true);
                break;
            }
            case "plume": // 렉돌: 크고 풍성한 깃털 꼬리 (포인트 색)
            {
                uint tc = S.Has("point") ? Col(S, "point") : shade;
                g.Ell(cx + rx * .92, cy - ry * .05, 4.6 * fk, ry * .85, tc);
                g.Ell(cx + rx * .92 + .8 * fk, cy - ry * .35, 2.2 * fk, 2 * fk, Mix(tc, c["white"], .22));
                break;
            }
            case "wrap": // 몽마: 꼬리 끝 스페이드 (위가 뾰족하고 아래 두 혹 사이로 꼬리 선이 들어간다). 꼬리 선은 테두리 뒤에 가늘게
            {
                var tip = WrapTail(cx, cy, rx, ry, fk)[^1];
                string[] spade = fk < .7 ? new[] { ".#.", "###", "#.#" } : new[] { "..#..", ".###.", "#####", "#####", "##.##" };
                int w = spade[0].Length, x0 = R(tip.X) - w / 2, y0 = R(tip.Y) - spade.Length;
                uint sc = S.Has("point") ? Col(S, "point") : shade;
                for (int y = 0; y < spade.Length; y++)
                    for (int x = 0; x < w; x++) if (spade[y][x] == '#') g.Put(x0 + x, y0 + y, sc);
                break;
            }
            case "long": // 드래곤: 바닥을 따라 길게 뻗다가 끝이 말려 올라가는 꼬리 (칸 밖으로는 안 나가게)
                for (int i = 0; i < 10; i++)
                {
                    double x = Math.Min(cx + rx * .6 + i * 1.2 * fk, 38), y = cy + ry * .78 - (i > 5 ? (i - 5) * 1.1 * fk : 0), r = (2.7 - i * .17) * soft;
                    g.Ell(x, y, r, r, shade);
                }
                break;
            case "taper": // 수달: 굵게 시작해서 가늘어지는 꼬리
                for (int i = 0; i < 6; i++)
                    g.Ell(cx + rx * .8 + i * 1.3 * fk, cy + ry * .62 - i * .45 * fk, (2.7 - i * .32) * soft, (2.3 - i * .28) * soft, shade);
                break;
            case "dragon":
                g.Poly(new[] { (cx + rx * .5, cy + ry * .3), (cx + rx * .6, cy + ry * .95), (cx + rx + 6 * fk, cy + ry * .1) }, shade);
                break;
            case "multi": // 구미호: 겹친 꼬리 다발 (부채처럼 펼치지 않는다)
            {
                var tails = new[] { (.7, -.55), (.95, -.15), (.85, .25) };
                for (int i = 0; i < tails.Length; i++)
                {
                    var (dx, dy) = tails[i];
                    g.Ell(cx + rx * dx + i * .6, cy + ry * dy, 3.6 * soft, ry * .55, shade);
                    g.Ell(cx + rx * dx + i * .6 + .8, cy + ry * dy - ry * .42, 2.2 * soft, 1.8 * soft, cream);
                }
                break;
            }
            case "spade": // 꼬마 악마: 가는 꼬리 끝에 하트
                for (int i = 0; i < 7; i++) g.Ell(cx + rx * .7 + i * .9 * fk, cy + ry * .55 - i * .9 * fk, .9, .9, shade);
                Heart(g, R(cx + rx * .7 + 5 * fk), R(cy + ry * .55 - 9 * fk), shade);
                break;
        }
        string back = S.S("back", "");
        if (back == "spikes") // 고슴도치: 위쪽 둘레로 가시
            for (int k = 0; k < 11; k++)
            {
                double a = Math.PI + k * Math.PI / 10;
                double bx = cx + Math.Cos(a) * rx * .95, by = cy + Math.Sin(a) * ry * .95;
                double tx = cx + Math.Cos(a) * (rx + 4 * fk), ty = cy + Math.Sin(a) * (ry + 4 * fk);
                double nx = -Math.Sin(a) * 2 * soft, ny = Math.Cos(a) * 2 * soft;
                g.Poly(new[] { (bx + nx, by + ny), (bx - nx, by - ny), (tx, ty) }, Col(S, "spike"));
            }
        else if (back == "wool" && !lying) // 양: 몸 둘레로 몽글몽글
            for (int k = 0; k < 12; k++)
            {
                double a = 2 * Math.PI * k / 12;
                g.Ell(cx + Math.Cos(a) * rx * .92, cy + Math.Sin(a) * ry * .88, 3.3 * soft, 3.1 * soft, c["fur"]);
            }
        string horns = S.S("horns", "");
        foreach (int s in Sides)
            switch (horns)
            {
                case "dragon": g.Poly(new[] { (cx + s * rx * .22, cy - ry * .8), (cx + s * rx * .5, cy - ry * .72), (cx + s * rx * .4, cy - ry - 4 * fk) }, Rgb(244, 220, 140)); break;
                case "swept": // 드래곤: 머리 위 옆에서 바깥 뒤로 뻗은 뿔 (상아색)
                    g.Poly(new[] { (cx + s * rx * .28, cy - ry * .78), (cx + s * rx * .56, cy - ry * .66), (cx + s * (rx * .62 + 3 * fk), cy - ry - 4.5 * fk) },
                        S.Has("horn") ? Col(S, "horn") : Rgb(242, 230, 200));
                    break;
                case "goat": g.Poly(new[] { (cx + s * rx * .18, cy - ry * .8), (cx + s * rx * .42, cy - ry * .78), (cx + s * rx * .55, cy - ry - 4.5 * fk) }, Rgb(210, 196, 170)); break;
                case "imp": g.Poly(new[] { (cx + s * rx * .25, cy - ry * .82), (cx + s * rx * .48, cy - ry * .74), (cx + s * rx * .5, cy - ry - 3 * fk) }, S.Has("horn") ? Col(S, "horn") : Rgb(90, 60, 110)); break;
                case "antler": // 가지 친 뿔: 아기는 혹 두 개, 자라면서 길어지고 가지를 친다
                {
                    double bx = cx + s * rx * .32, by = cy - ry * .85;
                    var pts = new List<(double, double)>();
                    for (int i = 0; i < R(7 * fk); i++) pts.Add((bx + s * i * .3, by - i));
                    if (fk >= .7) for (int i = 0; i < R(4 * fk); i++) pts.Add((bx + s * (1 + i), by - 3 - i * .6));
                    if (fk >= .95) for (int i = 0; i < 2; i++) pts.Add((bx + s * (2 + i * .2), by - 6 - i));
                    g.Pix(Rgb(176, 140, 100), pts.ToArray());
                    break;
                }
            }
        if (horns == "unicorn") g.Poly(new[] { (cx - 1.6, cy - ry * .82), (cx + 2.6, cy - ry * .82), (cx + 1, cy - ry - 5 * fk) }, Rgb(240, 206, 110));
        else if (horns == "one") g.Poly(new[] { (cx - 1.4, cy - ry * .84), (cx + 2.4, cy - ry * .84), (cx + .5, cy - ry - 4 * fk) }, Rgb(246, 236, 210)); // 도깨비: 가운데 뿔 하나
        string wings = S.S("wings", "");
        foreach (int s in Sides)
            switch (wings)
            {
                case "flipper": g.Ell(cx + s * rx * .98, cy + ry * .15, 2.2 * soft, ry * .5, shade); break;
                case "wing": g.Ell(cx + s * rx * .9, cy + ry * .15, 2.8 * soft, ry * .58, Mix(c["shade"], colors["line"], .2)); break;
                case "small": g.Ell(cx + s * rx * .95, cy + ry * .1, 2.4 * soft, ry * .38, shade); break;
                case "dragon": g.Poly(new[] { (cx + s * rx * .5, cy - ry * .3), (cx + s * (rx + 5.5 * fk), cy - ry * (.3 + .65 * fk)), (cx + s * (rx + 4 * fk), cy + ry * .15) }, Rgb(150, 214, 186)); break;
                case "side": // 까망 악마: 몸 양옆으로 펼친 박쥐 날개 — 날개막 + 뼈대 셋, 아랫단 물결
                {
                    uint mem = S.Has("wing") ? Col(S, "wing") : Mix(shade, colors["line"], .3), bone = Mix(mem, colors["line"], .55);
                    double reach = Math.Min(rx + 6 * fk, 19.2);
                    (double X, double Y) sh = (cx + s * rx * .7, cy - ry * .15), tip = (cx + s * reach, cy - ry * (.35 + .25 * fk)),
                        f1 = (cx + s * reach, cy + ry * .2), v1 = (cx + s * (rx + (reach - rx) * .55), cy + ry * .04),
                        f2 = (cx + s * (rx + (reach - rx) * .3), cy + ry * .3), inner = (cx + s * rx * .8, cy + ry * .25);
                    g.Poly(new[] { sh, tip, f1, v1, f2, inner }, mem);
                    foreach (var end in new[] { tip, f1, f2 }) g.Line(sh.X, sh.Y, end.X, end.Y, bone);
                    break;
                }
                case "bat": // 드래곤: 어깨에서 머리 위로 크게 펼친 박쥐 날개 — 날개막 + 뼈대 두 줄, 아랫단은 물결 (칸 밖으로는 안 나가게)
                {
                    uint mem = S.Has("wing") ? Col(S, "wing") : Mix(shade, colors["line"], .3), bone = Mix(mem, colors["line"], .55);
                    double reach = Math.Min(rx + 6.5 * fk, 19.2), top = cy - ry * (.6 + .6 * fk), w = reach - rx * .4;
                    (double X, double Y) sh = (cx + s * rx * .4, cy - ry * .2), tip = (cx + s * reach, top),            // 맨 위 뼈 끝 (뾰족)
                        f1 = (cx + s * (reach - .5), cy - ry * .05), v1 = (cx + s * (rx * .4 + w * .72), cy - ry * .1),     // 바깥 뼈 끝, 오목
                        f2 = (cx + s * (rx * .4 + w * .5), cy + ry * .16), v2 = (cx + s * (rx * .4 + w * .28), cy + ry * .06), // 안쪽 뼈 끝, 오목
                        inner = (cx + s * rx * .55, cy + ry * .24);
                    g.Poly(new[] { sh, tip, f1, v1, f2, v2, inner }, mem);
                    foreach (var end in new[] { tip, f1, f2 }) g.Line(sh.X, sh.Y, end.X, end.Y, bone);
                    break;
                }
                case "imp": g.Poly(new[] { (cx + s * rx * .55, cy - ry * .25), (cx + s * (rx + 4.5 * fk), cy - ry * (.25 + .5 * fk)), (cx + s * (rx + 2.5 * fk), cy + ry * .05) }, Rgb(120, 80, 140)); break; // 작은 박쥐 날개
            }
    }

    // 무늬: 윤곽선 전(몸 안쪽 색칠)과 후(선·작은 점)
    void Marks(Canvas g, Bag S, Dictionary<string, uint> c, double cx, double cy, double rx, double ry, int fx, int ey, int my, int gap, bool before, double sk)
    {
        uint cream = c["cream"], line = colors["line"], dark = Mix(c["shade"], line, .45);
        foreach (var m in S.List("marks"))
        {
            if (before)
                switch (m)
                {
                    case "crescent": // 반달가슴곰: 가슴의 흰 반달
                    {
                        var pts = new List<(double, double)>();
                        int n = R(5 * sk);
                        for (int i = -n; i <= n; i++) pts.Add((fx + i, R(cy + ry * .42 + Math.Abs(i) * .45)));
                        for (int i = -n + 1; i < n; i++) pts.Add((fx + i, R(cy + ry * .42 + Math.Abs(i) * .45) + 1));
                        g.Pix(cream, pts.ToArray());
                        break;
                    }
                    case "disc": foreach (int s in Sides) g.Ell(fx + .5 + s * gap, ey + .5, 4 * sk, 3.8 * sk, cream, true); break; // 부엉이: 눈 둘레 얼굴판
                    case "mask": // 너구리: 눈 가면
                        g.Ell(fx + .5, ey + .7, gap + 4 * sk, 2.8 * sk, Rgb(70, 70, 78), true);
                        g.Ell(fx + .5, ey - 3.2 * sk, gap + 2 * sk, 1.1, cream, true);
                        break;
                    case "plates": // 아기 용: 배 비늘
                        for (int i = 0; i < 3; i++)
                        {
                            int y = R(cy + ry * (.38 + i * .2));
                            g.Pix(Mix(c["cream"], line, .25), Enumerable.Range(-R(3 * sk), R(3 * sk) + R(4 * sk) + 1).Select(x => ((double)(fx + x), (double)y)).ToArray());
                        }
                        break;
                    case "eyepatch": foreach (int s in Sides) g.Ell(fx + .5 + s * gap, ey + 1, 3.2 * sk, 3.8 * sk, Rgb(66, 62, 68), true); break; // 판다: 비스듬한 눈 무늬
                    case "brows": foreach (int s in Sides) g.Ell(fx + .5 + s * gap, ey - 3.5 * sk, 1.8 * sk, 1.1, cream, true); break; // 레서판다: 흰 눈썹 점
                    case "patches": // 얼룩 돼지
                        g.Ell(cx - rx * .55, cy - ry * .45, 3.4 * sk, 2.6 * sk, Rgb(150, 104, 76), true);
                        g.Ell(cx + rx * .62, cy + ry * .35, 3 * sk, 2.4 * sk, Rgb(150, 104, 76), true);
                        break;
                    case "spots": // 문어: 연한 점
                        foreach (var (x, y) in new[] { (-.55, -.55), (.5, -.6), (-.7, .1), (.72, .05) }) g.Ell(cx + x * rx, cy + y * ry, 1.3 * sk, 1.3 * sk, cream, true);
                        break;
                    case "cracks": // 골렘: 갈라진 금
                        g.Pix(Mix(c["shade"], line, .5), (cx - rx * .6, cy - ry * .2), (cx - rx * .5, cy - ry * .1), (cx - rx * .55, cy), (cx - rx * .45, cy + ry * .1),
                            (cx + rx * .55, cy + ry * .35), (cx + rx * .62, cy + ry * .45), (cx + rx * .5, cy + ry * .5));
                        break;
                    case "cap": // 버섯 요정: 머리 위 큰 갓과 크림 점
                    {
                        uint cap = Col(S, "cap");
                        g.Ell(fx + .5, cy - ry * .55, rx * 1.25, ry * .62, cap);
                        g.Ell(fx + .5, cy - ry * .32, rx * 1.1, 1.2, Mix(cap, line, .3));
                        foreach (var (x, y) in new[] { (-.55, -.8), (.15, -1.05), (.6, -.7) }) g.Ell(fx + .5 + x * rx, cy + y * ry * .78, 1.4 * sk, 1.1 * sk, cream);
                        break;
                    }
                    case "topknot": // 알파카: 정수리 털뭉치 (귀 사이를 덮는 뭉게구름)
                        foreach (var (x, y) in new[] { (-3.5, .82), (0, .98), (3.5, .82), (-1.8, .7), (1.8, .7) }) g.Ell(fx + .5 + x * sk, cy - ry * y, 3 * sk, 2.7 * sk, cream);
                        break;
                    case "moss": // 골렘: 이끼와 작은 꽃
                        g.Ell(fx + .5, cy - ry * .88, rx * .55, 2 * sk, Rgb(120, 176, 90));
                        g.Pix(Rgb(250, 236, 120), (fx + 2, cy - ry - 2));
                        g.Pix(Rgb(246, 160, 190), (fx + 1, cy - ry - 2), (fx + 3, cy - ry - 2), (fx + 2, cy - ry - 3), (fx + 2, cy - ry - 1));
                        break;
                    case "mane": // 유니콘: 한 가지 색 갈기 (무지개는 피한다)
                        g.Ell(fx - 1, cy - ry * .78, rx * .42, 2.6 * sk, Col(S, "mane"));
                        g.Ell(cx + rx * .78, cy - ry * .2, 2 * sk, ry * .45, Col(S, "mane"));
                        break;
                    case "hair": // 도깨비: 부스스한 머리털
                        g.Poly(new (double, double)[] { (fx - 5 * sk, cy - ry * .7), (fx - 3 * sk, cy - ry - 2 * sk), (fx - 1 * sk, cy - ry * .75), (fx + 1 * sk, cy - ry - 3 * sk),
                            (fx + 3 * sk, cy - ry * .75), (fx + 5 * sk, cy - ry - 1 * sk), (fx + 6 * sk, cy - ry * .6) }, Rgb(60, 70, 90));
                        break;
                    case "tuft": g.Pix(c["fur"], (fx, cy - ry - 1), (fx + 1, cy - ry - 2), (fx + 1, cy - ry - 1), (fx + 2, cy - ry - 3)); break; // 오리: 정수리 깃
                    case "blaze": // 말라뮤트: 코에서 이마로 올라가는 흰 줄
                        g.Poly(new[] { (fx + .5 - 2.2 * sk, my - 1.0), (fx + .5 + 2.2 * sk, my - 1.0), (fx + .5, cy - ry * .8) }, cream, true);
                        break;
                    case "stripes": // 고양이: 옆구리 줄무늬 (짧은 가로줄 셋씩)
                        foreach (int s in Sides)
                            for (int i = 0; i < 3; i++)
                            {
                                int y = R(cy + ry * (-.08 + i * .26));
                                for (int j = 0; j < 2; j++) g.Put(R(cx + s * (rx - 1.5 - j)), y, Mix(c["shade"], line, .45), true);
                            }
                        break;
                    case "points": // 렉돌: 눈 둘레가 짙은 포인트, 코에서 이마까지는 흰 역V (바이컬러)
                        g.Ell(fx + .5, ey + .3, gap + 2.6 * sk, 3.6 * sk, Mix(Col(S, "point"), c["fur"], .3), true);
                        g.Poly(new[] { (fx + .5 - 3 * sk, my + 1.0), (fx + .5 + 3 * sk, my + 1.0), (fx + .5, ey - 1.5 * sk) }, cream, true);
                        break;
                    case "pugmask": // 퍼그: 주둥이와 눈 둘레가 까만 가면
                    {
                        uint mask = Rgb(70, 58, 52);
                        g.Ell(fx + .5, my - .8, rx * .5, ry * .36, mask, true);
                        foreach (int s in Sides) g.Ell(fx + .5 + s * gap, ey + .6, 3.2 * sk, 3.2 * sk, mask, true);
                        break;
                    }
                    case "bib": // 리트리버: 주둥이 아래 가슴에 크림색 털 (주둥이와 섞이지 않게 아래로)
                        g.Ell(fx + .5, cy + ry * .92, rx * .56, ry * .28, cream, true);
                        break;
                    case "ruff": // 렉돌: 가슴 목털이 풍성하다
                        g.Ell(fx + .5, cy + ry * .42, rx * .74, ry * .42, cream, true);
                        foreach (int s in Sides) g.Ell(fx + .5 + s * rx * .6, cy + ry * .3, 2.2 * sk, 1.7 * sk, cream, true);
                        break;
                    case "calico": // 엑조틱 삼색: 흰 바탕에 주황·검정 얼룩
                        g.Ell(cx - rx * .5, cy - ry * .6, 3.8 * sk, 2.8 * sk, Rgb(229, 150, 80), true);
                        g.Ell(cx + rx * .55, cy - ry * .55, 3.2 * sk, 2.6 * sk, Rgb(74, 62, 58), true);
                        g.Ell(cx + rx * .72, cy + ry * .35, 2.6 * sk, 2.2 * sk, Rgb(229, 150, 80), true);
                        break;
                }
            else
                switch (m)
                {
                    case "tabby": // 고양이: 이마 줄무늬
                        foreach (var (x, h) in new[] { (-2, 2), (0, 3), (2, 2) })
                            g.Pix(dark, Enumerable.Range(0, h).Select(i => ((double)(fx + x), (double)R(ey - 4 - i))).ToArray());
                        break;
                    case "shine": // 슬라임: 반짝이는 윗면
                        g.Pix(c["white"], (cx - rx * .55, cy - ry * .55), (cx - rx * .55 + 1, cy - ry * .55), (cx - rx * .55, cy - ry * .55 + 1), (cx - rx * .3, cy - ry * .72));
                        break;
                    case "beard": g.Pix(c["line"], (fx, my + 3), (fx + 1, my + 3), (fx, my + 4), (fx + 1, my + 4), (fx, my + 5)); break; // 염소 수염
                    case "wrinkles": // 퍼그: 이마 주름 두 줄
                    {
                        uint w = Mix(c["shade"], line, .35);
                        int top = ey - R(4 * sk) - 1;
                        foreach (var (x, y) in new[] { (-2, 0), (-1, -1), (0, -1), (1, -1), (2, 0), (-1, -3), (0, -3), (1, -3) }) g.Put(fx + x, top + y, w, true);
                        break;
                    }
                }
        }
        if (!before && S.T("whisker"))
            foreach (int s in Sides) g.Pix(c["line"], (fx + s * (gap + 3), my - 1), (fx + s * (gap + 5), my - 2), (fx + s * (gap + 3), my + 1), (fx + s * (gap + 5), my + 1));
    }

    static void Nose(Canvas g, string kind, int fx, int my, Dictionary<string, uint> c)
    {
        switch (kind)
        {
            case "pink": g.Pix(c["nose"], (fx - 1, my - 2), (fx, my - 2), (fx + 1, my - 2), (fx, my - 1)); break;
            case "dark": g.Pix(c["line"], (fx - 1, my - 2), (fx, my - 2), (fx + 1, my - 2), (fx + 2, my - 2), (fx, my - 1), (fx + 1, my - 1)); break;
            case "big": // 코알라: 크고 까만 코
                g.Ell(fx + .5, my - 2.2, 2.2, 2.4, c["line"]);
                g.Put(fx - 1, my - 3, Rgb(120, 110, 110));
                break;
            case "pig": // 돼지: 큰 코에 콧구멍 둘
                g.Ell(fx + .5, my - 1.5, 3.2, 2.2, c["line"]);
                g.Ell(fx + .5, my - 1.5, 2.3, 1.4, c["ear"]);
                g.Pix(c["line"], (fx - 1, my - 2), (fx + 2, my - 2));
                break;
            case "nostrils": g.Pix(c["line"], (fx - 1, my - 2), (fx + 2, my - 2), (fx - 1, my - 1), (fx + 2, my - 1)); break; // 카피바라
            case "snoot": // 돼지: 얼굴 가운데 크게 튀어나온 동그란 코 (테두리·밝은 코판·콧구멍 둘)
                g.Ell(fx + .5, my - 1.1, 3.9, 2.7, c["line"]);
                g.Ell(fx + .5, my - 1.1, 3.0, 1.9, c.TryGetValue("snout", out var disc) ? disc : c["ear"]);
                g.Pix(Mix(c["line"], c["ear"], .25), (fx - 1, my - 2), (fx - 1, my - 1), (fx + 2, my - 2), (fx + 2, my - 1));
                break;
            case "dog": // 강아지: 크고 까만 코에 반짝임
                g.Ell(fx + .5, my - 1.6, 2.1, 1.5, c["eye"]);
                g.Put(fx - 1, my - 2, Rgb(140, 128, 128));
                break;
        }
    }

    static void Beak(Canvas g, string kind, int fx, int my, Dictionary<string, uint> c)
    {
        uint orange = Rgb(246, 160, 60), deep = Rgb(206, 112, 40);
        switch (kind)
        {
            case "small": // 펭귄: 작은 세모 부리
                g.Pix(c["line"], Enumerable.Range(-2, 6).Select(x => ((double)(fx + x), (double)(my - 3)))
                    .Concat(new (double, double)[] { (fx - 1, my - 1), (fx + 2, my - 1), (fx, my), (fx + 1, my) }).ToArray());
                g.Pix(orange, Enumerable.Range(-1, 4).Select(x => ((double)(fx + x), (double)(my - 2)))
                    .Concat(new (double, double)[] { (fx, my - 1), (fx + 1, my - 1) }).ToArray());
                break;
            case "hook": // 부엉이: 아래로 굽은 부리
                g.Pix(c["line"], (fx - 1, my - 3), (fx + 2, my - 3), (fx - 1, my - 2), (fx + 2, my - 2), (fx, my), (fx + 1, my));
                g.Pix(orange, (fx, my - 3), (fx + 1, my - 3), (fx, my - 2), (fx + 1, my - 2), (fx, my - 1), (fx + 1, my - 1));
                break;
            case "bill": // 오리: 넓적한 부리
                g.Ell(fx + .5, my - .8, 4.6, 2.4, c["line"]);
                g.Ell(fx + .5, my - .8, 3.6, 1.5, orange);
                g.Pix(deep, (fx - 1, my - 1), (fx + 2, my - 1));
                break;
        }
    }

    static void Eyes(Canvas g, string kind, int fx, int ey, int gap, double r, Dictionary<string, uint> c, bool lash = false, bool lid = false)
    {
        foreach (var (x, s) in new[] { (fx - gap, -1), (fx + gap, 1) }) // s: 얼굴 바깥쪽 방향
        {
            switch (kind)
            {
                case "open" or "wide" or "angry":
                {
                    double w = r + (kind == "wide" ? .6 : 0);
                    g.Ell(x + .5, ey + .5, w, w + .1, c["eye"]);
                    g.Put(x - 1, ey - 1, c["white"]);
                    g.Put(x, ey - 1, c["white"]);
                    if (r >= 2.6) g.Put(x + 1, ey + 1, c["white"]); // 큰 눈엔 반짝임을 하나 더
                    if (lash) // 애교쟁이: 바깥 위로 속눈썹 두 가닥
                    {
                        int ox = s < 0 ? x - 3 : x + 4;
                        foreach (var (lx, ly) in new[] { (ox, ey - 1), (ox + s, ey - 2), (ox, ey - 3) }) g.Put(lx, ly, c["line"], true);
                    }
                    if (lid && kind == "open") // 심술이: 위 눈꺼풀이 반쯤 내려와 심드렁하게
                    {
                        uint skin = g.P[Math.Clamp(ey - 4, 0, Cell - 1) * Cell + Math.Clamp(x, 0, Cell - 1)];
                        if (skin == 0 || skin == c["line"]) skin = c["fur"];
                        for (int i = -2; i <= 3; i++)
                        {
                            g.Put(x + i, ey - 3, skin, true);
                            g.Put(x + i, ey - 2, skin, true);
                        }
                        for (int i = -1; i <= 2; i++) g.Put(x + i, ey - 1, c["line"], true);
                        g.Put(x, ey, c["white"]);
                    }
                    if (kind == "angry") // 안쪽이 내려간 눈꺼풀로 흘겨본다
                        foreach (var (i, j) in new[] { (-2, -1), (-1, -2), (0, -2), (1, -3), (2, -3) })
                        {
                            g.Put(x + i * s, ey + j, c["line"]);
                            for (int k = 1; k < 4; k++) g.Put(x + i * s, ey + j - k, c["fur"]);
                        }
                    break;
                }
                case "blink" or "shut":
                    g.Pix(c["eye"], (x - 2, ey), (x - 1, ey + 1), (x, ey + 1), (x + 1, ey + 1), (x + 2, ey));
                    break;
                case "happy":
                    g.Pix(c["eye"], (x - 2, ey + 1), (x - 1, ey), (x, ey - 1), (x + 1, ey), (x + 2, ey + 1));
                    break;
                case "heart":
                {
                    string[] rows = { ".#.#.", "#####", "#####", ".###.", "..#.." };
                    for (int dy = 0; dy < rows.Length; dy++)
                        g.Pix(c["red"], rows[dy].Select((ch, i) => (ch, i)).Where(t => t.ch == '#').Select(t => ((double)(x - 2 + t.i), (double)(ey - 2 + dy))).ToArray());
                    break;
                }
                case "wink":
                    if (s < 0)
                    {
                        g.Ell(x + .5, ey + .5, r, r + .1, c["eye"]);
                        g.Put(x - 1, ey - 1, c["white"]);
                        g.Put(x, ey - 1, c["white"]);
                    }
                    else g.Pix(c["eye"], (x - 2, ey - 2), (x - 1, ey - 1), (x, ey), (x - 1, ey + 1), (x - 2, ey + 2));
                    break;
                case "x":
                    for (int i = -2; i < 3; i++) { g.Put(x + i, ey + i, c["eye"]); g.Put(x + i, ey - i, c["eye"]); }
                    break;
                case "sad":
                    g.Ell(x + .5, ey + 1.5, r - .3, r, c["eye"]);
                    g.Put(x - 1, ey, c["white"]);
                    g.Pix(c["line"], (x - 2 * s, ey - 3), (x - s, ey - 3), (x, ey - 4), (x + s, ey - 4));
                    break;
            }
        }
    }

    static void Mouth(Canvas g, string kind, int fx, int my, Dictionary<string, uint> c)
    {
        switch (kind)
        {
            case "w": g.Pix(c["line"], (fx - 2, my), (fx - 1, my + 1), (fx, my), (fx + 1, my + 1), (fx + 2, my)); break;
            case "chew": g.Pix(c["line"], (fx - 2, my + 1), (fx - 1, my), (fx, my + 1), (fx + 1, my), (fx + 2, my + 1)); break;
            case "flat": g.Pix(c["line"], (fx - 1, my + 1), (fx, my + 1), (fx + 1, my + 1)); break;
            case "y": g.Pix(c["line"], (fx, my), (fx - 2, my + 1), (fx - 1, my + 1), (fx + 1, my + 1), (fx + 2, my + 1)); break;      // ㅅ 입 (곰·사슴처럼 주둥이 있는 동물)
            case "smile": g.Pix(c["line"], (fx - 2, my), (fx - 1, my + 1), (fx, my + 1), (fx + 1, my + 1), (fx + 2, my)); break; // 방긋 (여우·유니콘·슬라임)
            case "dot": g.Pix(c["line"], (fx, my + 1), (fx + 1, my + 1)); break;                                                 // 콩알 입 (몽마·유령·고슴도치)
            case "frown": g.Pix(c["line"], (fx - 2, my + 1), (fx - 1, my), (fx, my), (fx + 1, my), (fx + 2, my + 1)); break;
            case "o": g.Ell(fx + .5, my + 1.5, 1.8, 1.8, c["line"]); break;
            case "open" or "wide":
            {
                var (h, w) = kind == "open" ? (2.6, 3.4) : (3.8, 4.5);
                g.Ell(fx + .5, my + h, w, h, c["line"]);
                g.Ell(fx + .5, my + h + .5, w - 1, h - 1, c["red"]);
                if (kind == "wide") // 앞니 — 화면을 갉는 도구
                    foreach (int i in new[] { -2, -1, 1, 2 }) g.Pix(c["white"], (fx + i, my + 1), (fx + i, my + 2));
                break;
            }
        }
    }

    static void Heart(Canvas g, int x, int y, uint c)
    {
        g.Pix(c, (x, y), (x + 1, y), (x + 3, y), (x + 4, y));
        for (int i = 0; i < 5; i++) g.Put(x + i, y + 1, c);
        for (int i = 1; i < 4; i++) g.Put(x + i, y + 2, c);
        g.Put(x + 2, y + 3, c);
    }

    // 큰 망치: 머리 위로 들었다(0) 옆으로 내리쳤다(1). 자루 두 줄, 머리에 테두리
    static void Hammer(Canvas g, int pose, int hx, int hy, Dictionary<string, uint> c)
    {
        uint gray = Rgb(150, 150, 160), lite = Rgb(210, 210, 218), wood = Rgb(150, 100, 60);
        void Head(int x0, int y0, int w, int h)
        {
            for (int dx = -1; dx < w + 1; dx++) for (int dy = -1; dy < h + 1; dy++) g.Put(x0 + dx, y0 + dy, c["line"]);
            for (int dx = 0; dx < w; dx++) for (int dy = 0; dy < h; dy++) g.Put(x0 + dx, y0 + dy, gray);
            for (int dx = 0; dx < w; dx++) g.Put(x0 + dx, y0, lite);
        }
        if (pose == 0)
        {
            foreach (int dx in new[] { -1, 2 }) for (int i = -2; i < 10; i++) g.Put(hx + dx, hy - i, c["line"]);
            foreach (int dx in new[] { 0, 1 }) for (int i = -2; i < 10; i++) g.Put(hx + dx, hy - i, wood);
            Head(hx - 3, hy - 14, 8, 5);
        }
        else
        {
            hx = Math.Min(hx, 26); // 몸이 큰 형태도 망치 머리가 칸 밖으로 안 나가게
            for (int i = -2; i < 8; i++) foreach (int dy in new[] { -1, 2 }) g.Put(hx + i, hy + 1 + FloorDiv(i, 2) + dy, c["line"]);
            for (int i = -2; i < 8; i++) foreach (int dy in new[] { 0, 1 }) g.Put(hx + i, hy + 1 + FloorDiv(i, 2) + dy, wood);
            Head(hx + 7, hy + 1, 5, 8);
            g.Pix(Rgb(250, 185, 40), (hx + 13, hy + 10), (hx + 14, hy + 7), (hx + 10, hy + 11), (hx + 15, hy + 11));
        }
    }

    // ── 자가 검사: 모든 종·형태·자세를 그려 보고(빈 그림이 없게), 색만 바꾼 종은 모양이 같은지 ──
    public static string Check()
    {
        var art = Embedded;
        static void Expect(bool ok, string what) { if (!ok) throw new Exception("자가 검사 실패: " + what); }
        Expect(art.Species.All(art.Can) && !art.With("typo", "{\"tial\": \"thin\"}").Can("typo"), "모든 종을 데이터로 그리고, 모르는 키가 있는 종은 못 그린다");
        Expect(art.HasRow("adult_grumpy/3/fix") && !art.HasRow("adult/0/idle") && !art.HasRow("child/9/idle") && !art.HasRow("child/0/dance"), "줄 이름은 형태/볼/자세");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int rows = 0;
        foreach (var id in art.Species)
            foreach (var (form, _) in art.forms)
                foreach (var (pose, frames) in art.poses)
                {
                    var (bmp, n) = art.Row(id, $"{form}/0/{pose}");
                    using (bmp)
                    {
                        var px = Pixels(bmp);
                        for (int i = 0; i < n; i++)
                            Expect(Enumerable.Range(0, Cell * Cell).Any(j => px[j / Cell * bmp.Width + i * Cell + j % Cell] != 0), $"{id} {form}/0/{pose} {i}번이 비었다");
                    }
                    rows++;
                }
        double perSpecies = sw.Elapsed.TotalMilliseconds / art.Species.Count;
        var pink = art.With("pink", "{\"fur\": \"#FAB2CC\", \"shade\": \"#E48AB0\"}");
        foreach (var pose in new[] { "idle", "walk", "sleep", "happy" })
        {
            var (h, _) = art.Row("hamster", $"child/0/{pose}");
            var (k, _) = pink.Row("pink", $"child/0/{pose}");
            var (a, b) = (Pixels(h), Pixels(k));
            Expect(a.Zip(b).All(t => (t.First == 0) == (t.Second == 0)) && a.Zip(b).Any(t => t.First != t.Second), "색만 바꾼 종은 모양이 같고 색만 다르다");
            h.Dispose(); k.Dispose();
        }
        return $"그림 데이터: {art.Species.Count}종 × {rows / art.Species.Count}줄(볼 0) 그림 확인, 한 종 {perSpecies:0}ms";
    }

    static uint[] Pixels(Bitmap bmp)
    {
        var px = new int[bmp.Width * bmp.Height];
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < bmp.Height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, px, y * bmp.Width, bmp.Width);
        }
        finally { bmp.UnlockBits(data); }
        return px.Select(v => unchecked((uint)v)).ToArray();
    }
}
