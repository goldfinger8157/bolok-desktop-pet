using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json;

namespace Bolok;

// 그림 두 장을 겹쳐 쓴다: 고른 캐릭터 그림(형태·볼·자세)이 먼저, 없는 이름은 공통 그림(assets/sprites.png:
// 알·아이콘·이모티콘·게임·소품·캐릭터 썸네일)에서. 캐릭터는 고른 하나만 읽는다 (34종을 다 읽으면 메모리가 한참 는다).
// 캐릭터 그림은 데이터(assets/pets/art.json)로 그릴 수 있는 종이면 PetArt 가 그리고(Drawn), 아니면 PetSheets 가 받아 둔 PNG 를 With 로 읽는다.
// 쓴 프레임은 배율별로 캐시한다
public sealed class Sprites
{
    public const string DefaultSpecies = "hamster";

    sealed class Sheet
    {
        public readonly Bitmap Bmp;
        public readonly Dictionary<string, (int Row, int Frames)> Rows = new();
        public readonly Dictionary<string, string> Names = new();   // 공통 그림에만: 종 → 이름
        public readonly int Cell;

        public Sheet((Bitmap Bmp, Dictionary<string, (int Row, int Frames)> Rows) drawn) // PetArt 가 그린 것
        {
            (Bmp, Cell) = (drawn.Bmp, PetArt.Cell);
            foreach (var (k, v) in drawn.Rows) Rows[k] = v;
        }

        public Sheet(Stream png, Stream json)
        {
            using var raw = new Bitmap(png);
            Bmp = raw.Clone(new Rectangle(0, 0, raw.Width, raw.Height), PixelFormat.Format32bppArgb);
            using var doc = JsonDocument.Parse(json);
            Cell = doc.RootElement.GetProperty("cell").GetInt32();
            foreach (var r in doc.RootElement.GetProperty("rows").EnumerateObject())
                Rows[r.Name] = (r.Value[0].GetInt32(), r.Value[1].GetInt32());
            if (doc.RootElement.TryGetProperty("species", out var sp))
                foreach (var s in sp.EnumerateObject()) Names[s.Name] = s.Value.GetString() ?? s.Name;
        }
    }

    readonly Sheet common, pet;
    readonly Dictionary<(string, int, int, bool), Bitmap> cache = new();
    public readonly int Cell;
    public readonly string Species;
    public IReadOnlyDictionary<string, string> SpeciesNames => common.Names;   // 고를 수 있는 캐릭터 (생성기 순서)

    Sprites(Sheet common, Sheet pet, string species) => (this.common, this.pet, Cell, Species) = (common, pet, common.Cell, species);

    // exe 안 데이터로. 못 그리는 종이면 햄스터
    public static Sprites Embedded(string species = DefaultSpecies) => Drawn(PetArt.Embedded, PetArt.Embedded.Can(species) ? species : DefaultSpecies, null);

    // 데이터로 그린 캐릭터 + 공통 그림(개발 폴더 dir 이나 exe 에서)
    public static Sprites Drawn(PetArt art, string species, string? dir) => new(Common(dir), new Sheet(art.Sheet(species)), species);

    // 받은 캐릭터 그림(PNG) + 공통 그림
    public static Sprites With(string species, (byte[] Png, byte[] Json) pet, string? dir)
    {
        using var pp = new MemoryStream(pet.Png);
        using var pj = new MemoryStream(pet.Json);
        return new Sprites(Common(dir), new Sheet(pp, pj), species);
    }

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

    // 개발: 저장소 assets 에서. 데이터로 그릴 수 있으면 그리고, 아니면 생성기가 만든 PNG, 그것도 없으면 햄스터
    public static Sprites FromDir(string dir, string species = DefaultSpecies)
    {
        var art = PetArt.FromDir(dir);
        if (art.Can(species)) return Drawn(art, species, dir);
        string png = Path.Combine(dir, "pets", species + ".png"), json = Path.Combine(dir, "pets", species + ".json");
        if (!File.Exists(png) || !File.Exists(json)) return Drawn(art, DefaultSpecies, dir);
        return With(species, (File.ReadAllBytes(png), File.ReadAllBytes(json)), dir);
    }

    public bool Has(string name) => pet.Rows.ContainsKey(name) || common.Rows.ContainsKey(name);

    public Bitmap Get(string name, int frame, int scale, bool flip = false)
    {
        var sheet = pet.Rows.ContainsKey(name) ? pet : common;
        var (row, n) = sheet.Rows[name];
        var key = (name, frame % n, scale, flip);
        if (cache.TryGetValue(key, out var hit)) return hit;

        var b = new Bitmap(Cell * scale, Cell * scale, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(b))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(sheet.Bmp, new Rectangle(0, 0, b.Width, b.Height),
                new Rectangle((frame % n) * Cell, row * Cell, Cell, Cell), GraphicsUnit.Pixel);
        }
        if (flip) b.RotateFlip(RotateFlipType.RotateNoneFlipX);
        return cache[key] = b;
    }
}
