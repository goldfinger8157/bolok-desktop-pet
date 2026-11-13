namespace Bolok;

public enum Edge { Left, Right, Top }

// 창의 어느 가장자리, 어느 지점에 숨을지. T 는 가장자리 위 위치(0~1)라 창이 옮겨가도 같은 자리를 다시 구할 수 있다.
// A 는 그 자리가 있는 모니터의 작업 영역
public readonly record struct Peek(IntPtr H, Edge E, double T, Rectangle A);

// 발 위치. X·Y 는 빼꼼 나온 자리, Duck 은 쏙 들어간 자리, Out 은 창 밖에서 들어가기 시작하는 자리
public readonly record struct PeekPos(double X, double Y, double DuckX, double DuckY, double OutX, double OutY);

// 숨을 자리 찾기 — 화면과 무관한 순수 계산이라 --selfcheck 로 검사한다
public static class HideSpots
{
    const double SideDuck = .34, TopDuck = .3, SideOut = .75;

    public static PeekPos At(Rectangle r, Peek p, int cell, double shown)
    {
        var area = p.A;
        double yMin = Math.Max(r.Top + cell * 1.1, area.Top + cell), yMax = Math.Min(r.Bottom - 6, area.Bottom - 4);
        double y = yMin + p.T * Math.Max(0, yMax - yMin);
        switch (p.E)
        {
            case Edge.Left:
                return new(r.Left, y, r.Left + cell * SideDuck, y, r.Left - cell * SideOut, y);
            case Edge.Right:
                return new(r.Right, y, r.Right - cell * SideDuck, y, r.Right + cell * SideOut, y);
            default:
                double x = r.Left + cell * .7 + p.T * Math.Max(0, r.Width - cell * 1.4);
                double top = r.Top + cell * (1 - shown);
                return new(x, top, x, top + cell * TopDuck, x, r.Top - 2);
        }
    }

    static bool Fits(Rectangle r, Peek p, int cell, double shown)
    {
        if (r.Width < cell * 2 || r.Height < cell * 1.5) return false;
        var area = p.A;
        var at = At(r, p, cell, shown);
        if (p.E == Edge.Top)
            return r.Top - cell >= area.Top && at.X - cell / 2.0 >= area.Left && at.X + cell / 2.0 <= area.Right;
        double yMin = Math.Max(r.Top + cell * 1.1, area.Top + cell), yMax = Math.Min(r.Bottom - 6, area.Bottom - 4);
        return yMin <= yMax && at.OutX - cell / 2.0 >= area.Left && at.OutX + cell / 2.0 <= area.Right;
    }

    // 볼록이가 차지할 자리. 이곳이 더 위의 창에 덮여 있으면 숨어도 안 보인다
    static Rectangle Box(PeekPos at, Peek p, int cell) => p.E == Edge.Top
        ? new Rectangle((int)at.X - cell / 2, (int)at.OutY - cell, cell, cell)
        : new Rectangle((int)Math.Min(at.X, at.OutX) - cell / 2, (int)at.Y - cell, (int)Math.Abs(at.X - at.OutX) + cell, cell);

    public static List<Peek> Find(IReadOnlyList<Win> wins, IReadOnlyList<Rectangle> areas, int cell, double shown, Random rng, IntPtr only = default)
    {
        var found = new List<Peek>();
        for (int i = 0; i < wins.Count; i++)
        {
            var w = wins[i];
            if (w.Topmost || (only != default && w.H != only)) continue;
            foreach (var area in areas)
            {
                if (!w.R.IntersectsWith(area)) continue;
                foreach (var e in new[] { Edge.Left, Edge.Right, Edge.Top })
                {
                    var p = new Peek(w.H, e, rng.NextDouble(), area);
                    if (!Fits(w.R, p, cell, shown)) continue;
                    var box = Box(At(w.R, p, cell, shown), p, cell);
                    bool covered = false;
                    for (int j = 0; j < i; j++) covered |= wins[j].R.IntersectsWith(box);
                    if (!covered) found.Add(p);
                }
            }
        }
        return found;
    }

    public static double PerchX(Rectangle r, double t, int cell) => r.Left + cell * .7 + t * (r.Width - cell * 1.4);

    // 창 윗변에 누울 자리. 윗변이 더 위의 창에 덮여 있지 않고, 위쪽에 몸이 들어갈 공간이 있어야 한다
    public static List<Peek> FindPerch(IReadOnlyList<Win> wins, IReadOnlyList<Rectangle> areas, int cell, Random rng)
    {
        var found = new List<Peek>();
        for (int i = 0; i < wins.Count; i++)
        {
            var w = wins[i];
            if (w.Topmost || w.R.Width < cell * 1.6) continue;
            foreach (var area in areas)
            {
                if (!w.R.IntersectsWith(area) || w.R.Top - cell < area.Top || w.R.Top >= area.Bottom) continue;
                var p = new Peek(w.H, Edge.Top, rng.NextDouble(), area);
                double x = PerchX(w.R, p.T, cell);
                if (x - cell / 2.0 < area.Left || x + cell / 2.0 > area.Right) continue;
                var box = new Rectangle((int)x - cell / 2, w.R.Top - cell, cell, cell + 6);
                bool covered = false;
                for (int j = 0; j < i; j++) covered |= wins[j].R.IntersectsWith(box);
                if (!covered) found.Add(p);
            }
        }
        return found;
    }

    // 창이 old 에서 now 로 움직이며 box 를 쳤는지. 친 방향과 그 가장자리가 움직인 거리를 돌려준다.
    // 이미 겹쳐 있던 것(창 뒤를 지나가던 중)은 친 것이 아니다
    public static (double Dx, double Dy, int Moved)? HitBy(Rectangle old, Rectangle now, Rectangle box)
    {
        if (old.IntersectsWith(box) || !now.IntersectsWith(box)) return null;
        if (old.Right <= box.Left && now.Right > box.Left) return (1, 0, now.Right - old.Right);
        if (old.Left >= box.Right && now.Left < box.Right) return (-1, 0, old.Left - now.Left);
        if (old.Bottom <= box.Top && now.Bottom > box.Top) return (0, 1, now.Bottom - old.Bottom);
        return (0, -1, old.Top - now.Top);
    }

    // 들켰을 때 무슨 반응을 할지. 성격과 기분이 비율을 바꾼다
    public static string Reaction(string why, string stage, double mood, double roll)
    {
        var w = why switch
        {
            "closed" or "minimized" => new Dictionary<string, double> { ["look"] = 50, ["shock"] = 30, ["angry"] = 10 },
            "poked" => new Dictionary<string, double> { ["shock"] = 40, ["shy"] = 45, ["freeze"] = 15 },
            "activated" => new Dictionary<string, double> { ["shock"] = 60, ["shy"] = 20, ["angry"] = 10, ["freeze"] = 10 },
            _ => new Dictionary<string, double> { ["shock"] = 30, ["shy"] = 25, ["angry"] = 15, ["freeze"] = 15, ["chase"] = 15 },
        };
        if (stage == "adult_sweet" && w.ContainsKey("shy")) w["shy"] += 60;
        if (stage == "adult_grumpy") { w.Remove("shy"); w["angry"] = w.GetValueOrDefault("angry") + 60; }
        if (stage == "adult_playful") { w.Remove("freeze"); w["chase"] = w.GetValueOrDefault("chase") + 60; } // 장난꾸러기: 들키면 오히려 쫓아온다
        if (mood < 40 && w.ContainsKey("angry")) w["angry"] += 25;

        double total = w.Values.Sum(), at = roll * total;
        foreach (var (kind, weight) in w.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            at -= weight;
            if (at < 0) return kind;
        }
        return "shock";
    }
}
