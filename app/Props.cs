using System.Drawing.Drawing2D;

namespace Bolok;

// 볼록이가 숨는 창: 그 창과, 그보다 위에 있어 겹치는 창들이 함께 가린다
public sealed class Cover
{
    public readonly Rectangle R;
    readonly Rectangle[] above;
    Region? mask;

    public Cover(Rectangle r, Rectangle[] above) => (R, this.above) = (r, above);

    public bool Contains(int x, int y) => R.Contains(x, y) || above.Any(a => a.Contains(x, y));

    public Region Mask()
    {
        if (mask != null) return mask;
        mask = new Region(R);
        foreach (var a in above) mask.Union(a);
        return mask;
    }
}

// 게임 소품 하나: 화면에 놓인 자리와, 칸(40×40) 안 어느 픽셀이 차 있는지
public sealed class Prop
{
    public readonly string Name;
    public readonly int Scale;
    public readonly Point Origin;       // 칸 왼쪽 위 (화면 좌표)
    public readonly Rectangle Bounds;   // 실제로 그려진 부분
    readonly bool[,] solid;
    Region? mask;

    public Prop(string name, int scale, Point origin, bool[,] solid)
    {
        (Name, Scale, Origin, this.solid) = (name, scale, origin, solid);
        var (x0, y0, x1, y1) = Props.Trim(solid);
        Bounds = new Rectangle(origin.X + x0 * scale, origin.Y + y0 * scale, (x1 - x0 + 1) * scale, (y1 - y0 + 1) * scale);
    }

    public bool Contains(int x, int y)
    {
        int lx = (x - Origin.X) / Scale, ly = (y - Origin.Y) / Scale;
        return x >= Origin.X && y >= Origin.Y && lx < solid.GetLength(1) && ly < solid.GetLength(0) && solid[ly, lx];
    }

    // 볼록이를 가릴 모양 (화면 좌표). 차 있는 픽셀을 줄 단위로 묶는다
    public Region Mask()
    {
        if (mask != null) return mask;
        using var path = new GraphicsPath();
        for (int y = 0; y < solid.GetLength(0); y++)
            for (int x = 0; x < solid.GetLength(1); x++)
            {
                if (!solid[y, x]) continue;
                int run = x;
                while (run + 1 < solid.GetLength(1) && solid[y, run + 1]) run++;
                path.AddRectangle(new Rectangle(Origin.X + x * Scale, Origin.Y + y * Scale, (run - x + 1) * Scale, Scale));
                x = run;
            }
        return mask = new Region(path);
    }
}

public static class Props
{
    public static readonly Dictionary<string, string[]> Sets = new()
    {
        ["garden"] = new[] { "prop/mushroom", "prop/sunflower", "prop/cactus", "prop/cloud", "prop/acorn", "prop/carrot" },
        ["snack"] = new[] { "prop/strawberry", "prop/cupcake", "prop/cheese", "prop/apple", "prop/cookie", "prop/acorn" },
    };
    public static IEnumerable<string> All => Sets.Values.SelectMany(x => x).Distinct();

    // 게임에서 볼록이가 숨을 창들. 최대화 창은 빼고, 각 창보다 위에 있는 창도 함께 가린다
    public static List<Cover> Covers(IReadOnlyList<Win> wins, Rectangle arena, int cell)
    {
        var list = new List<Cover>();
        for (int i = 0; i < wins.Count; i++)
        {
            var r = Rectangle.Intersect(wins[i].R, arena);
            if (wins[i].Topmost || r.Width < cell * 1.2 || r.Height < cell) continue;
            if (r.Width * (long)r.Height >= arena.Width * (long)arena.Height * 85 / 100) continue;
            list.Add(new Cover(r, wins.Take(i).Select(w => w.R).Where(a => a.IntersectsWith(Rectangle.Inflate(r, cell, cell))).ToArray()));
        }
        return list;
    }

    public static (int X0, int Y0, int X1, int Y1) Trim(bool[,] solid)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int y = 0; y < solid.GetLength(0); y++)
            for (int x = 0; x < solid.GetLength(1); x++)
                if (solid[y, x]) (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
        return x1 < 0 ? (0, 0, 0, 0) : (x0, y0, x1, y1);
    }

    public static bool[,] Solid(Bitmap cell)
    {
        var s = new bool[cell.Height, cell.Width];
        for (int y = 0; y < cell.Height; y++)
            for (int x = 0; x < cell.Width; x++)
                s[y, x] = cell.GetPixel(x, y).A > 0;
        return s;
    }

    // 게임 창 아래쪽 안에 작은 소품 몇 개. 총·나가기 버튼 자리는 피한다
    public static List<Prop> OnFloor(Rectangle arena, string[] names, Random rng, Func<string, bool[,]> solidOf, IReadOnlyList<Rectangle> keepClear, int want = 2)
    {
        var placed = new List<Prop>();
        for (int tries = 0; tries < 40 && placed.Count < want; tries++)
        {
            string name = names[rng.Next(names.Length)];
            int scale = rng.Next(1, 3);
            var solid = solidOf(name);
            var (x0, y0, x1, y1) = Trim(solid);
            int w = (x1 - x0 + 1) * scale, h = (y1 - y0 + 1) * scale;
            if (arena.Width - w - 40 < 1) continue;
            int bx = rng.Next(arena.Left + 20, arena.Right - w - 20), by = arena.Bottom - 6 - h;
            var bounds = new Rectangle(bx, by, w, h);
            if (keepClear.Any(k => k.IntersectsWith(bounds)) || placed.Any(p => Rectangle.Inflate(p.Bounds, 30, 0).IntersectsWith(bounds))) continue;
            placed.Add(new Prop(name, scale, new Point(bx - x0 * scale, by - y0 * scale), solid));
        }
        return placed;
    }

    // 창으로 열린(최대화 아닌) 창의 윗변에 작은 소품을 앉힌다. 더 위의 창에 덮인 윗변이나 공간이 없는 곳은 건너뛴다
    public static List<Prop> OnWindows(IReadOnlyList<Win> wins, IReadOnlyList<Rectangle> areas, string[] names, Random rng,
        Func<string, bool[,]> solidOf, IReadOnlyList<Rectangle> keepClear, int cap = 8)
    {
        var placed = new List<Prop>();
        for (int i = 0; i < wins.Count && placed.Count < cap; i++)
        {
            var r = wins[i].R;
            if (wins[i].Topmost || r.Width < 160) continue;
            var area = areas.FirstOrDefault(a => a.Contains(r.Left + r.Width / 2, r.Top + 1));
            if (area.IsEmpty) continue;
            var part = Rectangle.Intersect(r, area);
            if (part.Width * (long)part.Height >= area.Width * (long)area.Height * 85 / 100) continue; // 최대화 창
            int want = rng.Next(1, r.Width > 700 ? 3 : 2);
            for (int tries = 0, got = 0; tries < 12 && got < want; tries++)
            {
                string name = names[rng.Next(names.Length)];
                int scale = rng.Next(1, 3);
                var solid = solidOf(name);
                var (x0, y0, x1, y1) = Trim(solid);
                int w = (x1 - x0 + 1) * scale, h = (y1 - y0 + 1) * scale;
                if (r.Width - w - 40 < 1) continue;
                int bx = rng.Next(r.Left + 20, r.Right - w - 20), by = r.Top + 2 - h; // 윗변에 살짝 걸터앉는다
                var bounds = new Rectangle(bx, by, w, h);
                var edge = new Rectangle(bx, r.Top, w, 4);
                if (by < area.Top || keepClear.Any(k => k.IntersectsWith(bounds))) continue;
                bool covered = false;
                for (int j = 0; j < i; j++) covered |= wins[j].R.IntersectsWith(bounds) || wins[j].R.IntersectsWith(edge);
                if (covered || placed.Any(p => Rectangle.Inflate(p.Bounds, 12, 12).IntersectsWith(bounds))) continue;
                placed.Add(new Prop(name, scale, new Point(bx - x0 * scale, by - y0 * scale), solid));
                got++;
            }
        }
        return placed;
    }
}
