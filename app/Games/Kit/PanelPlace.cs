namespace Bolok;

// 판 자리 계산 (화면과 무관한 순수 계산이라 --selfcheck 로 검사한다): 「볼록이 가까이에서 열린다」(YELL 2026-11-24 아이스런)가 게임 공통 컨셉이 되어
// 아이스런 TrailGame 안에 있던 것을 옮겨 아이스런·클라이밍이 같이 쓴다 (옮기기만 했다: 아이스런은 전과 똑같이 돈다)
static class PanelPlace
{
    // 창 안에서 볼록이 가까이: 볼록이가 판 아래 가운데쯤 오게, 창(모니터와 겹친 부분) 안으로 맞춘다 (아래는 6칸 띄운다)
    public static Rectangle NearPet(Rectangle inside, double petX, double petY, int w, int h, int k) =>
        new((int)Math.Clamp(petX - w / 2.0, inside.Left, Math.Max(inside.Left, inside.Right - w)),
            (int)Math.Clamp(petY + 8 * k - h, inside.Top, Math.Max(inside.Top, inside.Bottom - h - 6 * k)), w, h);

    // 볼록이 둘레: 볼록이가 판 아래 가운데쯤 오게 (모니터 안으로)
    public static Rectangle Around(Rectangle area, double petX, double petY, int w, int h, int k) =>
        new((int)Math.Clamp(petX - w / 2.0, area.Left, area.Right - w), (int)Math.Clamp(petY + 8 * k - h, area.Top, area.Bottom - h), w, h);

    // r 이 가장 많이 걸친 모니터 (없으면 fallback)
    public static Rectangle MostOn(IReadOnlyList<Rectangle> screens, Rectangle fallback, Rectangle r)
    {
        Rectangle best = fallback;
        long most = -1;
        foreach (var sc in screens) { var i = Rectangle.Intersect(sc, r); long a = (long)i.Width * i.Height; if (a > most) (most, best) = (a, sc); }
        return best;
    }
}
