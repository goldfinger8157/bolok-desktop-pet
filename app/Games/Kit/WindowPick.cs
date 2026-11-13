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
}
