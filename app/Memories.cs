using System.Drawing.Imaging;

namespace Bolok;

// 추억 (YELL 2026-11-12): 놀러 온 친구가 두고 간 선물, 자랄 때마다 찍히는 사진, 놀러 왔던 친구가 다음 날 보내는 그림 엽서.
// 모두 그림만 (글자 없음). 사진·엽서는 데이터 폴더의 memories/ 에 PNG 로 남는다
public sealed record GiftRecord(string Kind, string From, DateTime At);
public sealed record PhotoRecord(string Species, string Stage, DateTime At, string File);
public sealed record CardRecord(string From, DateTime At, string File);

public static class Memories
{
    public static readonly string[] Kinds = { "acorn", "flower", "shell", "leaf", "feather", "pebble", "berry", "clover" };
    public const int PhotoSize = 96, CardW = 240, CardH = 160;
    public const double CardChance = .7; // 놀러 왔던 친구가 다음 날 엽서를 보낼 확률

    // 사진: 단계마다 다른 파스텔 바탕에 그 모습 (그려진 부분을 정수배로 크게, 바닥에 서게)
    public static void Photo(Sprites sp, string sprite, string stage, string path)
    {
        using var bmp = new Bitmap(PhotoSize, PhotoSize);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(stage switch { "egg" => Ui.Hex(0xfff1d0), "baby" => Ui.Hex(0xffe2ec), "child" => Ui.Hex(0xdcf4e6), _ => Ui.Hex(0xdcebff) });
            using (var grass = new SolidBrush(Ui.Hex(0xbfe3a8))) g.FillRectangle(grass, 0, PhotoSize - 12, PhotoSize, 12);
            Stand(g, sp.Get(sprite, 0, 1), new Rectangle(6, 6, PhotoSize - 12, PhotoSize - 14));
        }
        Save(bmp, path);
    }

    // 엽서: 그 친구가 사는 자연(하늘·숲·냇물)에 친구가 서 있고, 오른쪽 위에 하트 우표
    public static void Card(Sprites common, Sprites? friend, string species, string form, int seed, string path)
    {
        using var bmp = new Bitmap(CardW, CardH);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            var box = new Rectangle(8, 8, CardW - 16, CardH - 16);
            g.SetClip(box);
            Overlay.PaintNature(g, box, box.Bottom - 22, seed, seed % 13, 3);
            string pose = $"{form}/0/happy";
            var who = friend != null && friend.Has(pose) ? friend.Get(pose, 0, 1) : common.Get("thumb/" + species, 0, 1);
            Stand(g, who, new Rectangle(box.Left + box.Width / 2 - 48, box.Bottom - 22 - 76 + 6, 96, 76));
            g.ResetClip();
            using (var edge = new Pen(Ui.Hex(0xd8d8d8))) g.DrawRectangle(edge, 0, 0, CardW - 1, CardH - 1);
            var stamp = new Rectangle(CardW - 44, 14, 30, 30);
            using (var paper = new SolidBrush(Color.White)) g.FillRectangle(paper, stamp);
            using (var dash = new Pen(Ui.Hex(0xe8b0b8)) { DashPattern = new float[] { 2, 2 } }) g.DrawRectangle(dash, stamp);
            Stand(g, common.Get("icon/heart", 0, 1), Rectangle.Inflate(stamp, -5, -5));
        }
        Save(bmp, path);
    }

    // 그려진 부분만 잘라 정수배로 키워 상자 바닥 가운데에 세운다 (도트가 안 깨지게)
    static void Stand(Graphics g, Bitmap img, Rectangle box)
    {
        var c = Ui.Opaque(img, 0);
        int k = Math.Max(1, Math.Min(box.Width / c.Width, box.Height / c.Height));
        Ui.Pixel(g, img, new Rectangle(box.Left + (box.Width - c.Width * k) / 2, box.Bottom - c.Height * k, c.Width * k, c.Height * k), c);
    }

    // 작은 그림(선물)을 잘라 정수배로 키운 새 그림 (설정 창용)
    public static Bitmap Icon(Bitmap img, int size)
    {
        var c = Ui.Opaque(img, 0);
        int k = Math.Max(1, Math.Min(size / c.Width, size / c.Height));
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        Ui.Pixel(g, img, new Rectangle((size - c.Width * k) / 2, (size - c.Height * k) / 2, c.Width * k, c.Height * k), c);
        return bmp;
    }

    static void Save(Bitmap bmp, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        bmp.Save(tmp, ImageFormat.Png);
        File.Move(tmp, path, true);
    }

    // 파일을 잠그지 않게 읽어 온다 (없거나 깨졌으면 null)
    public static Bitmap? Load(string path)
    {
        try
        {
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var raw = new Bitmap(f);
            return new Bitmap(raw);
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { return null; }
    }
}
