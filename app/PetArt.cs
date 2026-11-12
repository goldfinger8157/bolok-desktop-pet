using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Bolok;

// 캐릭터 그림을 그림 파일 대신 데이터로 그린다 (YELL 2026-11-12: 색만 바꾼 분홍 볼록이도 그림 추가 없이 데이터 한 줄로).
// 데이터는 assets/pets/art.json (색·형태·자세·종) 하나. tools/sprites.py 의 pet() 을 그대로 옮겨서 둘은 픽셀 하나까지 같다
// (자가 검사가 저장소의 assets/pets/<종>.png 와 비교한다).
// 지금은 햄스터 몸(둥근 귀·기본 얼굴)에 색을 바꾼 종까지 그린다. 꼬리·뿔·날개·무늬 같은 종 특징은 아직이라 그런 종은 Can 이 false,
// 그 종은 예전처럼 받은 그림을 쓴다
public sealed class PetArt
{
    public const int Cell = 40;

    // 지금 그릴 수 있는 종 데이터 키. 이 밖의 키(ears·tail·marks …)가 있으면 아직 못 그린다
    static readonly HashSet<string> Known = new() { "name", "note", "fur", "shade", "cream", "ear", "paw", "blush", "form_colors" };
    static readonly string[] ColorKeys = { "fur", "shade", "cream", "ear", "paw", "blush" };
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
    public IEnumerable<string> Poses => poses.Select(p => p.Name);
    public bool Can(string id) => species.TryGetValue(id, out var s) && s.Keys.All(Known.Contains);
    public IEnumerable<string> Rows => forms.SelectMany(f => Enumerable.Range(0, Cheeks).SelectMany(c => poses.Select(p => $"{f.Name}/{c}/{p.Name}")));

    // 한 종의 그림 전부: 형태 × 볼 단계 × 자세 줄, 줄마다 프레임 (tools/sprites.py 의 pet_rows·sheet 와 같은 배치)
    public (Bitmap Bmp, Dictionary<string, (int Row, int Frames)> Rows) Sheet(string id)
    {
        if (!Can(id)) throw new ArgumentException($"아직 데이터로 못 그리는 종: {id}");
        var S = species[id];
        int cols = poses.Max(p => p.Frames.Length), rows = forms.Count * Cheeks * poses.Count, width = cols * Cell;
        var px = new int[rows * Cell * width];
        var index = new Dictionary<string, (int, int)>();
        int r = 0;
        foreach (var (form, F) in forms)
            for (int cheek = 0; cheek < Cheeks; cheek++)
                foreach (var (pose, frames) in poses)
                {
                    index[$"{form}/{cheek}/{pose}"] = (r, frames.Length);
                    for (int i = 0; i < frames.Length; i++)
                    {
                        var g = Pet(S, form, F, cheek, frames[i]);
                        for (int y = 0; y < Cell; y++)
                            for (int x = 0; x < Cell; x++)
                                px[(r * Cell + y) * width + i * Cell + x] = unchecked((int)g.P[y * Cell + x]);
                    }
                    r++;
                }
        var bmp = new Bitmap(width, rows * Cell, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < bmp.Height; y++) Marshal.Copy(px, y * width, data.Scan0 + y * data.Stride, width);
        }
        finally { bmp.UnlockBits(data); }
        return (bmp, index);
    }

    // ── 그리기 (tools/sprites.py 와 줄마다 같게. 반올림은 파이썬 round 처럼 짝수 쪽, int() 는 0 쪽으로 자른다) ──

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

    Canvas Pet(Bag S, string form, Bag F, int cheek, Bag f)
    {
        var c = new Dictionary<string, uint>(colors);
        foreach (var k in ColorKeys) if (S.Has(k)) c[k] = Rgb(S.S(k, "#000000"));
        if (S.Has("form_colors")) // 햄스터: 형태마다 털색을 따로 정해 두었다
        {
            if (S.Obj("form_colors")!.Obj(form) is { } fc) foreach (var k in fc.Keys) c[k] = Rgb(fc.S(k, "#000000"));
        }
        else if (tints.TryGetValue(form, out var t)) // 다른 종: 아기는 밝게, 심술이는 어둡게, 애교쟁이는 살짝 따뜻하게
            (c["fur"], c["shade"]) = (Mix(c["fur"], t.Color, t.T), Mix(c["shade"], t.Color, t.T));

        var g = new Canvas();
        double rx = F.N("rx"), ry = F.N("ry");
        const double ground = 37;
        double lie = f.N("lie"), sq = f.N("sq"), dangle = f.N("dangle");
        if (lie != 0) (rx, ry) = (rx + 2, ry - 2.5); // 엎드려 자기: 납작하고 넓게
        (rx, ry) = (rx + sq * .5, ry - sq * .5);
        double cx = 20.5, cy = ground - ry - 1 + f.N("dy");
        if (dangle != 0) { (ry, rx) = (ry + 1, rx - 1); cy = 19; } // 들렸을 때는 몸이 아래로 늘어진다
        double turn = f.N("turn");
        int fx = R(20 + turn * (rx > 9 ? 1 : .7));       // 고개 돌린 쪽으로 얼굴이 옮겨간다
        int gap = R(F.N("eyegap") - Math.Abs(turn) * .4); // 옆을 보면 눈 사이가 좁아 보인다
        int ey = R(cy - ry * .24), my = R(cy + ry * .3);

        // 1) 실루엣 — 귀 → 몸·볼주머니·발
        double ear = F.N("ear"), ex = rx * .62;
        double[] earX = { cx + -1 * ex + (fx - 20) * .4, cx + 1 * ex + (fx - 20) * .4 };
        foreach (var x in earX) g.Ell(x, cy - ry * .86, ear, ear, c["fur"]);
        g.Ell(cx, cy, rx, ry, c["shade"]);
        double bulge = cheek * 2.2 + f.N("puff") * 1.2; // 먹은 화면이 많을수록 불룩하다
        if (bulge != 0)
            foreach (int s in new[] { -1, 1 }) g.Ell(fx + .5 + s * rx * .72, cy + ry * .22, 2 + bulge * .75, 2.4 + bulge * .6, c["cream"]);
        if (lie == 0)
        {
            int step = (int)f.N("step");
            for (int i = 0; i < 2; i++)
            {
                int s = i == 0 ? -1 : 1;
                double lift = step == i + 1 ? 1 : 0;
                if (dangle != 0) lift = -.6 * (((int)dangle + i) % 2);
                g.Ell(cx + s * rx * .5, cy + ry + .5 - lift, 2.8, 1.9, c["paw"]);
                if (dangle != 0) g.Ell(cx + s * (rx + .4), cy + 1, 2.2, 1.7, c["paw"]); // 번쩍 들리면 팔이 벌어진다
            }
        }

        // 2) 안쪽 칠하기 — 테두리 한 겹을 그림자로 남기고 털·배를 올린다
        g.Ell(cx, cy - .8, rx - 1, ry - .6, c["fur"], true);
        foreach (var x in earX) { double r = ear * .55; g.Ell(x, cy - ry * .86, r, r, c["ear"]); }
        g.Ell(fx + .5, cy + ry * .82, rx * .86, ry * .82, c["cream"], true);
        g.Ell(fx + .5, my, rx * .36, ry * .26, c["cream"], true);
        foreach (int s in new[] { -1, 1 })
            g.Ell(fx + .5 + s * (F.N("eyegap") + 1.8), cy + ry * .1, (2 + F.N("blush") * .3) * 1, 1.2, c["blush"], true);

        // 3) 윤곽선 → 얼굴
        g.Outline(c["line"]);
        Eyes(g, f.S("eye", "open"), fx, ey, gap, F.N("eye"), c);
        if (F.T("brow")) // 심술이: 늘 찌푸린 눈썹
            foreach (int s in new[] { -1, 1 })
            {
                int x = fx + s * gap;
                g.Pix(c["line"], (x - 2 * s, ey - 3), (x - s, ey - 4), (x, ey - 5), (x + s, ey - 5), (x + 2 * s, ey - 6));
            }
        g.Pix(c["nose"], (fx - 1, my - 2), (fx, my - 2), (fx + 1, my - 2), (fx, my - 1));
        Mouth(g, f.S("mouth", "w"), fx, my, c);

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
        if (f.T("cover")) // 앞발로 눈을 가리고 볼이 빨개진다
            foreach (int s in new[] { -1, 1 })
            {
                g.Ell(fx + .5 + s * (gap + 1.5), cy + ry * .14, 3, 1.5, c["blush"]);
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

    static void Eyes(Canvas g, string kind, int fx, int ey, int gap, double r, Dictionary<string, uint> c)
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

    // ── 자가 검사: 저장소에서 돌릴 때(개발·CI)는 파이썬이 만든 assets/pets/<종>.png 와 픽셀까지 같아야 한다 ──
    public static string Check()
    {
        var art = Embedded;
        if (!art.Can("hamster") || art.Can("cat")) throw new Exception("자가 검사 실패: 햄스터는 데이터로 그리고, 꼬리 달린 고양이는 아직 못 그린다");
        var (ham, hrows) = art.Sheet("hamster");
        using (ham)
        {
            if (hrows.Count != art.forms.Count * art.Cheeks * art.poses.Count || !hrows.ContainsKey("adult_grumpy/3/fix"))
                throw new Exception("자가 검사 실패: 햄스터 줄 수");
            // 색만 바꾼 종(분홍 볼록이)은 모양이 같고 털색만 다르다
            var pink = art.With("pink", "{\"fur\": \"#FAB2CC\", \"shade\": \"#E48AB0\"}");
            using var p = pink.Sheet("pink").Bmp;
            var (a, b) = (Pixels(ham), Pixels(p));
            int same = 0, changed = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if ((a[i] == 0) != (b[i] == 0)) throw new Exception("자가 검사 실패: 색만 바꾼 종은 모양이 같다");
                if (a[i] != 0) { if (a[i] == b[i]) same++; else changed++; }
            }
            if (changed == 0 || same == 0) throw new Exception("자가 검사 실패: 색만 바꾼 종은 털색만 다르다");
        }
        for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d != null; d = d.Parent)
        {
            var dir = Path.Combine(d.FullName, "assets");
            if (!File.Exists(Path.Combine(dir, "pets", "art.json"))) continue;
            var repo = FromDir(dir);
            var lines = new List<string>();
            foreach (var id in repo.Species.Where(repo.Can)) lines.Add(Compare(repo, id, dir));
            return "그림 데이터: " + string.Join(", ", lines);
        }
        return "그림 데이터: 햄스터 그리기 확인 (저장소 밖이라 파이썬 그림과 비교는 건너뜀)";
    }

    // 데이터로 그린 것과 파이썬이 만든 그림을 줄 이름·프레임 수·픽셀까지 비교한다
    public static string Compare(PetArt art, string id, string assetsDir)
    {
        string png = Path.Combine(assetsDir, "pets", id + ".png"), json = Path.Combine(assetsDir, "pets", id + ".json");
        var (bmp, rows) = art.Sheet(id);
        using (bmp)
        using (var raw = new Bitmap(png))
        using (var made = raw.Clone(new Rectangle(0, 0, raw.Width, raw.Height), PixelFormat.Format32bppArgb))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(json));
            var want = doc.RootElement.GetProperty("rows").EnumerateObject().Select(r => (r.Name, r.Value[0].GetInt32(), r.Value[1].GetInt32())).ToList();
            var got = rows.Select(r => (r.Key, r.Value.Row, r.Value.Frames)).OrderBy(r => r.Row).ToList();
            if (!want.SequenceEqual(got)) throw new Exception($"자가 검사 실패: {id} 줄 이름·순서·프레임 수가 파이썬과 다르다");
            if (made.Width != bmp.Width || made.Height != bmp.Height) throw new Exception($"자가 검사 실패: {id} 그림 크기 {bmp.Size} ≠ {made.Size}");
            var (a, b) = (Pixels(bmp), Pixels(made));
            int diff = 0, first = -1;
            for (int i = 0; i < a.Length; i++)
            {
                bool ea = (a[i] >>> 24) == 0, eb = (b[i] >>> 24) == 0;
                if (ea && eb || a[i] == b[i]) continue;
                diff++;
                if (first < 0) first = i;
            }
            if (diff > 0)
            {
                int y = first / bmp.Width, x = first % bmp.Width;
                string row = rows.First(r => r.Value.Row == y / Cell).Key;
                throw new Exception($"자가 검사 실패: {id} 가 파이썬 그림과 {diff}픽셀 다르다 (처음: {row} {x / Cell}번 프레임 ({x % Cell},{y % Cell}))");
            }
            return $"{id} {rows.Count}줄 파이썬과 같음";
        }
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
