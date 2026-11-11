namespace Bolok;

// 모니터 배치. 작업 영역끼리 맞닿은 곳을 찾아 볼록이가 모니터 사이를 건너게 한다.
// 모니터 사이의 빈 공간은 아무도 볼 수 없으니 그리로는 들어가지 않는다
public sealed class World
{
    public readonly List<Rectangle> Areas;

    public World(IEnumerable<Rectangle> areas) => Areas = areas.ToList();

    public bool Inside(double x, double y) => Areas.Any(a => Has(a, x, y));

    static bool Has(Rectangle a, double x, double y) => x >= a.Left && x < a.Right && y >= a.Top && y < a.Bottom;

    // 점이 든 영역, 없으면 가장 가까운 영역
    public Rectangle At(double x, double y)
    {
        foreach (var a in Areas) if (Has(a, x, y)) return a;
        return Areas.OrderBy(a =>
        {
            double dx = Math.Max(Math.Max(a.Left - x, x - a.Right), 0), dy = Math.Max(Math.Max(a.Top - y, y - a.Bottom), 0);
            return dx * dx + dy * dy;
        }).First();
    }

    // avoid 밖에서 (x, y) 에 가장 가까운 자리. 어느 모니터에도 빈 곳이 없으면 null
    public (double X, double Y)? NearestFree(double x, double y, Rectangle avoid, int cell, int floorGap)
    {
        (double X, double Y)? best = null;
        double bestCost = double.MaxValue, half = cell / 2.0 + 4;
        var here = At(x, y);
        foreach (var a in Areas)
        {
            double minX = a.Left + half, maxX = a.Right - half, minY = a.Top + cell, maxY = a.Bottom - floorGap;
            double cx = Math.Clamp(x, minX, maxX), cy = Math.Clamp(y, minY, maxY);
            void Try(double px, double py)
            {
                double cost = Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y)) + (a == here ? 0 : cell * 2);
                if (cost < bestCost) { bestCost = cost; best = (px, py); }
            }
            if (!avoid.IntersectsWith(a)) { Try(cx, cy); continue; }
            if (avoid.Left - half >= minX) Try(Math.Min(cx, avoid.Left - half), cy);
            if (avoid.Right + half <= maxX) Try(Math.Max(cx, avoid.Right + half), cy);
            if (avoid.Top - 2 >= minY) Try(cx, Math.Min(cy, avoid.Top - 2));
            if (avoid.Bottom + cell + 2 <= maxY) Try(cx, Math.Max(cy, avoid.Bottom + cell + 2));
        }
        return best;
    }

    const int Touch = 3; // 이 정도 틈은 맞닿은 것으로 친다

    static bool SideBySide(Rectangle a, Rectangle b, int cell) =>
        (Math.Abs(a.Right - b.Left) <= Touch || Math.Abs(b.Right - a.Left) <= Touch)
        && Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top) >= cell * 2;

    static bool Stacked(Rectangle a, Rectangle b, int cell) =>
        (Math.Abs(a.Bottom - b.Top) <= Touch || Math.Abs(b.Bottom - a.Top) <= Touch)
        && Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left) >= cell * 2;

    // from 에서 to 로 갈 때 다음에 건너갈 모니터. 이어지지 않으면 null
    public Rectangle? NextArea(Rectangle from, Rectangle to, int cell)
    {
        var cameFrom = new Dictionary<Rectangle, Rectangle> { [from] = from };
        var queue = new Queue<Rectangle>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            if (at == to)
            {
                while (cameFrom[at] != from) at = cameFrom[at];
                return at;
            }
            foreach (var n in Areas)
                if (!cameFrom.ContainsKey(n) && (SideBySide(at, n, cell) || Stacked(at, n, cell)))
                {
                    cameFrom[n] = at;
                    queue.Enqueue(n);
                }
        }
        return null;
    }

    // a 에서 b 로 건너는 길: 경계 앞(A)까지 간 뒤 경계 너머(B)로 곧장 간다
    public (double AX, double AY, double BX, double BY) Door(Rectangle a, Rectangle b, double x, double y, int cell, int floorGap)
    {
        double step = cell * .7;
        if (SideBySide(a, b, cell))
        {
            double top = Math.Max(a.Top, b.Top) + cell, bottom = Math.Min(a.Bottom, b.Bottom) - floorGap;
            double dy = Math.Clamp(y, top, Math.Max(top, bottom));
            bool right = Math.Abs(a.Right - b.Left) <= Touch;
            double edge = right ? b.Left : b.Right, dir = right ? 1 : -1;
            return (edge - dir * step, dy, edge + dir * step, dy);
        }
        double left = Math.Max(a.Left, b.Left) + cell / 2.0, far = Math.Min(a.Right, b.Right) - cell / 2.0;
        double dx = Math.Clamp(x, left, Math.Max(left, far));
        bool down = Math.Abs(a.Bottom - b.Top) <= Touch;
        double seam = down ? b.Top : b.Bottom;
        return (dx, down ? seam - floorGap : seam + cell, dx, down ? seam + cell : seam - floorGap);
    }
}
