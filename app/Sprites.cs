using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json;

namespace Bolok;

// 그림은 두 군데서 온다: 캐릭터(형태/볼/자세 줄과 도감 썸네일 thumb/<종>)는 데이터(assets/pets/art.json)로 PetArt 가 그리고,
// 나머지(알·아이콘·이모티콘·게임·소품)는 공통 그림(assets/sprites.png). 캐릭터 줄은 처음 쓸 때 그 줄만 그린다 (한 벌을 다 그리면 0.3초 걸린다).
// 쓴 프레임은 배율별로 캐시한다
public sealed class Sprites
{
    public const string DefaultSpecies = "hamster";

    // 공통 그림: PNG 한 장 + 줄 목록 (tools/sprites.py 가 만든다)
    sealed class Sheet
    {
        public readonly Bitmap Bmp;
        public readonly Dictionary<string, (int Row, int Frames)> Rows = new();
        public readonly int Cell;

        public Sheet(Stream png, Stream json)
        {
            using var raw = new Bitmap(png);
            Bmp = raw.Clone(new Rectangle(0, 0, raw.Width, raw.Height), PixelFormat.Format32bppArgb);
            using var doc = JsonDocument.Parse(json);
            Cell = doc.RootElement.GetProperty("cell").GetInt32();
            foreach (var r in doc.RootElement.GetProperty("rows").EnumerateObject())
                Rows[r.Name] = (r.Value[0].GetInt32(), r.Value[1].GetInt32());
        }
    }

    readonly Sheet common;
    readonly PetArt art;
    readonly Dictionary<string, (Bitmap Bmp, int Frames)> drawn = new();
    readonly Dictionary<(string, int, int, bool), Bitmap> cache = new();
    public readonly int Cell;
    public readonly string Species;
    public IReadOnlyList<string> SpeciesList => art.Species; // 고를 수 있는 캐릭터 (art.json 순서)
    public string BaseSpecies => PetArt.Base(Species); // 색 변형("cat#1")을 뺀 종

    Sprites(Sheet common, PetArt art, string species) => (this.common, this.art, Cell, Species) = (common, art, common.Cell, species);

    // exe 안 데이터로. 못 그리는 종이면 햄스터
    public static Sprites Embedded(string species = DefaultSpecies) => Drawn(PetArt.Embedded, species, null);

    // 데이터로 그리는 캐릭터 + 공통 그림(개발 폴더 dir 이나 exe 에서)
    public static Sprites Drawn(PetArt art, string species, string? dir)
    {
        if (!art.Can(species) && !art.Can(DefaultSpecies)) art = PetArt.Embedded; // 개발 중 그림 데이터가 앱보다 새로워 못 읽으면 exe 안 데이터로 (안 그리고 멈추지 않게)
        return new(Common(dir), art, art.Can(species) ? species : DefaultSpecies);
    }

    // 개발: 저장소 assets 의 데이터·공통 그림으로
    public static Sprites FromDir(string dir, string species = DefaultSpecies) => Drawn(PetArt.FromDir(dir), species, dir);

    static Sheet Common(string? dir)
    {
        if (dir != null)
        {
            // 읽는 동안에도 생성기가 쓸 수 있게 공유해서 연다
            using var png = new FileStream(Path.Combine(dir, "sprites.png"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var json = new FileStream(Path.Combine(dir, "sprites.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return new Sheet(png, json);
        }
        var a = Assembly.GetExecutingAssembly();
        using var epng = a.GetManifestResourceStream("sprites.png")!;
        using var ejson = a.GetManifestResourceStream("sprites.json")!;
        return new Sheet(epng, ejson);
    }

    static string? ThumbOf(string name) => name.StartsWith("thumb/") ? name[6..] : null;

    // 데이터로 그리는 줄: 이 종의 형태/볼/자세, 아무 종의 thumb/<종>. 처음 물을 때 그린다
    (Bitmap Bmp, int Frames)? Row(string name)
    {
        if (drawn.TryGetValue(name, out var d)) return d;
        if (plain.Contains(name)) return null;
        if (ThumbOf(name) is { } id) { if (!art.Can(id)) return null; d = (art.Thumb(id), 2); }
        else if (art.HasRow(name)) d = art.Row(Species, name);
        else { plain.Add(name); return null; }
        return drawn[name] = d;
    }

    readonly HashSet<string> plain = new(); // 데이터로 그리는 줄이 아니라고 이미 확인한 이름 (공통 그림: 그릴 때마다 이름을 쪼개 보지 않게)

    public int Frames(string name) => Row(name) is { } d ? d.Frames : common.Rows.TryGetValue(name, out var r) ? r.Frames : 1;

    public bool Has(string name) => (ThumbOf(name) is { } id ? art.Can(id) : art.HasRow(name)) || common.Rows.ContainsKey(name);

    public Bitmap Get(string name, int frame, int scale, bool flip = false)
    {
        Bitmap src;
        int n, y;
        if (Row(name) is { } d) (src, n, y) = (d.Bmp, d.Frames, 0);
        else
        {
            var (row, rn) = common.Rows[name];
            (src, n, y) = (common.Bmp, rn, row * Cell);
        }
        var key = (name, frame % n, scale, flip);
        if (cache.TryGetValue(key, out var hit)) return hit;

        var b = new Bitmap(Cell * scale, Cell * scale, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(b))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(src, new Rectangle(0, 0, b.Width, b.Height), new Rectangle((frame % n) * Cell, y, Cell, Cell), GraphicsUnit.Pixel);
        }
        if (flip) b.RotateFlip(RotateFlipType.RotateNoneFlipX);
        return cache[key] = b;
    }
}
