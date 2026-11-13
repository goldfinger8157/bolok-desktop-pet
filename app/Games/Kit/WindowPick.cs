namespace Bolok;

// 게임판이 될 창 고르기 (포트리스의 섬, 미니건이 쏠 창). 화면과 무관한 계산이라 --selfcheck 로 검사한다
static class WindowPick
{
    // 섬이 될 창: 같은 모니터의 창모드 창 중 다른 창에 가리지 않고 서로 안 겹치는 것, 넓은 것부터 max 개.
    // 메뉴를 열기 전 쓰던 창이 창모드면 그 모니터, 아니면 볼록이 모니터. 창이 없으면 빈 목록
    // topRoom: 창 위에 남겨야 할 여유 (칸 배수). 미니건은 위쪽 창도 쏘니 0
    public static (Rectangle Field, List<Rectangle> Wins) Islands(Win? used, IReadOnlyList<Win> wins, Rectangle petArea, IReadOnlyList<Rectangle> screens, int cell, int max, double topRoom = 1)
    {
        long Area(Rectangle r) => r.Width * (long)r.Height;
        Rectangle FieldOf(Win w) => screens.OrderByDescending(s => Area(Rectangle.Intersect(s, w.R))).FirstOrDefault();
        bool Windowed(Win w, Rectangle f)
        {
            var r = Rectangle.Intersect(w.R, f);
            return !w.Topmost && Area(r) < Area(f) * 85 / 100 && r.Width >= cell * 1.5 && r.Height >= cell && r.Top >= f.Top + cell * topRoom;
        }
        var field = used is { } u && Windowed(u, FieldOf(u)) ? FieldOf(u) : petArea;
        var list = new List<Rectangle>();
        foreach (var (w, i) in wins.Select((w, i) => (w, i)).OrderByDescending(x => Area(Rectangle.Intersect(x.w.R, field))))
        {
            if (list.Count >= max) break;
            if (FieldOf(w) != field || !Windowed(w, field)) continue;
            var r = Rectangle.Intersect(w.R, field);
            if (wins.Take(i).Any(x => x.R.IntersectsWith(r)) || list.Any(x => Rectangle.Inflate(x, 4, 4).IntersectsWith(r))) continue;
            list.Add(r);
        }
        return (field, list);
    }

    // 시합장이 될 창 하나 (모래성, 나중에 배구·버그 떼도): 섬과 같은 규칙(창모드·다른 창에 안 가림·최대화 아님)에
    // 너비·높이가 minW×minH 이상인 것. ==메뉴를 열기 전 쓰던 창(used)이 되면 그 창==, 아니면 가장 넓은 창.
    // 손잡이(H)까지 돌려주니 게임 중 창이 움직이면 따라갈 수 있다. 창 위 여유(topRoom)는 기본 0 — 창 위에 무엇을 얹는 게임은 자리가 없을 때를 스스로 다룬다
    // (섬 기본값 1칸을 그대로 쓰니 화면 위쪽에 붙은 창이 모두 빠져 「쓸 창 없음」이 됐다, 2026-11-13 시험).
    // 없으면 Arena 가 null — 그때 무엇을 할지(바탕화면 판, 「X 표 창」 그림)는 게임이 정한다
    public static (Rectangle Field, Win? Arena) Arena(Win? used, IReadOnlyList<Win> wins, Rectangle petArea, IReadOnlyList<Rectangle> screens, int cell, int minW, int minH, double topRoom = 0)
    {
        var (field, list) = Islands(used, wins, petArea, screens, cell, int.MaxValue, topRoom);
        if (used is { } u && list.Contains(Rectangle.Intersect(u.R, field)) && u.R.Width >= minW && u.R.Height >= minH) return (field, u);
        foreach (var r in list.Where(r => r.Width >= minW && r.Height >= minH).OrderByDescending(r => r.Width * (long)r.Height))
            foreach (var w in wins)
                if (Rectangle.Intersect(w.R, field) == r) return (field, w);
        return (field, null);
    }
}
