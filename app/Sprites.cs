using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json;

namespace Bolok;

// 그림 두 장을 겹쳐 쓴다: 고른 캐릭터 그림(assets/pets/<종>.png, 형태·볼·자세)이 먼저, 없는 이름은 공통 그림(assets/sprites.png:
// 알·아이콘·이모티콘·게임·소품·캐릭터 썸네일)에서. 캐릭터는 고른 하나만 읽는다 (34종을 다 읽으면 메모리가 한참 는다).
// exe 에는 햄스터만 들어 있다. 다른 종은 PetSheets 가 받아 둔 것을 With 로 읽는다.
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

    public static Sprites Embedded(string species = DefaultSpecies)
    {
        var a = Assembly.GetExecutingAssembly();
        if (a.GetManifestResourceInfo($"pets/{species}.png") == null) species = DefaultSpecies;
        using var png = a.GetManifestResourceStream("sprites.png")!;
        using var json = a.GetManifestResourceStream("sprites.json")!;
        using var petPng = a.GetManifestResourceStream($"pets/{species}.png")!;
        using var petJson = a.GetManifestResourceStream($"pets/{species}.json")!;
        return new Sprites(new Sheet(png, json), new Sheet(petPng, petJson), species);
    }

    // 캐릭터 그림은 받은 바이트로, 공통 그림은 개발 폴더(dir)나 exe 에서
    public static Sprites With(string species, (byte[] Png, byte[] Json) pet, string? dir)
    {
        Sheet common;
        if (dir != null)
        {
            using var png = new FileStream(Path.Combine(dir, "sprites.png"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var json = new FileStream(Path.Combine(dir, "sprites.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            common = new Sheet(png, json);
        }
        else
        {
            var a = Assembly.GetExecutingAssembly();
            using var png = a.GetManifestResourceStream("sprites.png")!;
            using var json = a.GetManifestResourceStream("sprites.json")!;
            common = new Sheet(png, json);
        }
        using var pp = new MemoryStream(pet.Png);
        using var pj = new MemoryStream(pet.Json);
        return new Sprites(common, new Sheet(pp, pj), species);
    }

    public static Sprites FromDir(string dir, string species = DefaultSpecies)
    {
        // 읽는 동안에도 생성기가 쓸 수 있게 공유해서 연다
        const FileShare share = FileShare.ReadWrite | FileShare.Delete;
        FileStream Open(string path) => new(Path.Combine(dir, path), FileMode.Open, FileAccess.Read, share);
        if (!File.Exists(Path.Combine(dir, "pets", species + ".png"))) species = DefaultSpecies;
        using var png = Open("sprites.png");
        using var json = Open("sprites.json");
        using var petPng = Open($"pets/{species}.png");
        using var petJson = Open($"pets/{species}.json");
        return new Sprites(new Sheet(png, json), new Sheet(petPng, petJson), species);
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
