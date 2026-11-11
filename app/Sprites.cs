using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json;

namespace Bolok;

// assets/sprites.png 를 잘라 쓴다. 쓴 프레임은 배율별로 캐시한다
public sealed class Sprites
{
    readonly Bitmap sheet;
    readonly Dictionary<string, (int Row, int Frames)> rows = new();
    readonly Dictionary<(string, int, int, bool), Bitmap> cache = new();
    public readonly int Cell;

    Sprites(Stream png, Stream json)
    {
        using var raw = new Bitmap(png);
        sheet = raw.Clone(new Rectangle(0, 0, raw.Width, raw.Height), PixelFormat.Format32bppArgb);
        using var doc = JsonDocument.Parse(json);
        Cell = doc.RootElement.GetProperty("cell").GetInt32();
        foreach (var r in doc.RootElement.GetProperty("rows").EnumerateObject())
            rows[r.Name] = (r.Value[0].GetInt32(), r.Value[1].GetInt32());
    }

    public static Sprites Embedded()
    {
        var a = Assembly.GetExecutingAssembly();
        using var png = a.GetManifestResourceStream("sprites.png")!;
        using var json = a.GetManifestResourceStream("sprites.json")!;
        return new Sprites(png, json);
    }

    public static Sprites FromDir(string dir)
    {
        // 읽는 동안에도 생성기가 쓸 수 있게 공유해서 연다
        const FileShare share = FileShare.ReadWrite | FileShare.Delete;
        using var png = new FileStream(Path.Combine(dir, "sprites.png"), FileMode.Open, FileAccess.Read, share);
        using var json = new FileStream(Path.Combine(dir, "sprites.json"), FileMode.Open, FileAccess.Read, share);
        return new Sprites(png, json);
    }

    public Bitmap Get(string name, int frame, int scale, bool flip = false)
    {
        var (row, n) = rows[name];
        var key = (name, frame % n, scale, flip);
        if (cache.TryGetValue(key, out var hit)) return hit;

        var b = new Bitmap(Cell * scale, Cell * scale, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(b))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(sheet, new Rectangle(0, 0, b.Width, b.Height),
                new Rectangle((frame % n) * Cell, row * Cell, Cell, Cell), GraphicsUnit.Pixel);
        }
        if (flip) b.RotateFlip(RotateFlipType.RotateNoneFlipX);
        return cache[key] = b;
    }
}
