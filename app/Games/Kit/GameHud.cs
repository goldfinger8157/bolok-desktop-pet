namespace Bolok;

// 게임 화면 표시 (글자 없이 그림·숫자만): 판 모서리, 점수(왼쪽 위), 하트(오른쪽 위), 막대, 별 + 번호, 3·2·1, 끝 점수 + 왕관.
// 작은 판 게임은 모두 같은 자리에 같은 크기로 놓는다. f 는 판(화면 좌표), k 는 볼록이 배율.
// 짝이 되는 …Rect 는 다시 칠할 자리다 (화면 좌표): 값이 바뀐 프레임에만 넣으면 된다 (Watch)
static class GameHud
{
    public static int Small(int k) => Math.Max(1, k - 1);   // 판 위 그림·숫자의 보통 배율 (볼록이보다 한 단계 작게)
    public static int HeartScale(int k) => Math.Max(1, (k + 1) / 3); // 하트 그림(icon/heart)은 칸을 꽉 채워 그려져 있어 따로 작은 배율을 쓴다

    // ── 판 모서리 네 군데에 작은 ㄱ자 (판이 어디까지인지만 알려 준다) ──
    public static void Corners(Overlay o, Graphics g, Rectangle f0, int k)
    {
        var f = o.Client(f0);
        int len = 8 * k, t = Math.Max(1, k);
        var b = Pal.Edge;
        g.FillRectangle(b, f.Left, f.Top, len, t); g.FillRectangle(b, f.Left, f.Top, t, len);
        g.FillRectangle(b, f.Right - len, f.Top, len, t); g.FillRectangle(b, f.Right - t, f.Top, t, len);
        g.FillRectangle(b, f.Left, f.Bottom - t, len, t); g.FillRectangle(b, f.Left, f.Bottom - len, t, len);
        g.FillRectangle(b, f.Right - len, f.Bottom - t, len, t); g.FillRectangle(b, f.Right - t, f.Bottom - len, t, len);
    }

    // ── 숫자 (도트 숫자 그림). align -1: (x, y) 가 첫 글자 가운데, 0: 전체 가운데, 1: 마지막 글자 가운데. lead: 앞에 붙일 + 나 x ──
    public static void Number(Overlay o, Graphics g, int n, double x, double y, int scale, int align = 0, char lead = '\0')
    {
        var glyphs = Stamps.Of(o.Art).Digits(scale);
        Span<int> text = stackalloc int[12];
        int len = 0;
        n = Math.Max(0, n);
        do { text[len++] = n % 10; n /= 10; } while (n > 0);
        if (lead != '\0') text[len++] = lead == '+' ? 10 : 11;
        int step = 5 * scale, x0 = (int)x - (align < 0 ? 0 : align > 0 ? (len - 1) * step : (len - 1) * step / 2);
        for (int i = 0; i < len; i++) glyphs[text[len - 1 - i]].Put(o, g, x0 + i * step, (int)y);
    }

    public static int Digits(int n) { int len = 1; for (n = Math.Max(0, n); n >= 10; n /= 10) len++; return len; }

    // ── 점수: 왼쪽 위 ──
    public static void Score(Overlay o, Graphics g, Rectangle f, int k, int score) =>
        Number(o, g, score, f.Left + 6 * k + 5 * Small(k) / 2, f.Top + 7 * k, Small(k), -1);

    public static Rectangle ScoreRect(Rectangle f, int k) => new(f.Left + 3 * k, f.Top + 2 * k, 8 * 5 * Small(k) + 6 * k, 10 * k);

    // ── 하트(목숨): 오른쪽 위, 오른쪽 끝부터 ──
    static int HeartStep(int k) => Math.Max(8 * k, 22 * HeartScale(k));

    public static void Hearts(Overlay o, Graphics g, Rectangle f, int k, int lives)
    {
        var heart = Stamps.Of(o.Art).Get("icon/heart", 0, HeartScale(k));
        for (int i = 0; i < lives; i++) heart.Put(o, g, f.Right - 6 * k - 10 * HeartScale(k) - i * HeartStep(k), f.Top + 7 * k);
    }

    public static Rectangle HeartsRect(Rectangle f, int k, int max = 5)
    {
        int w = 6 * k + 22 * HeartScale(k) + (max - 1) * HeartStep(k);
        return new Rectangle(f.Right - w, f.Top + 7 * k - 12 * HeartScale(k), w, 24 * HeartScale(k));
    }

    // ── 막대: 갈색 테두리, 크림 바탕, 채운 만큼 fill (r 은 화면 좌표 안쪽 크기) ──
    public static void Bar(Overlay o, Graphics g, Rectangle r, int k, double frac, Brush fill)
    {
        var bar = o.Client(r);
        int edge = Math.Max(1, k / 2);
        g.FillRectangle(Pal.Line, bar.X - edge, bar.Y - edge, bar.Width + 2 * edge, bar.Height + 2 * edge);
        g.FillRectangle(Pal.Cream, bar);
        g.FillRectangle(fill, bar.X, bar.Y, (int)(bar.Width * Math.Clamp(frac, 0, 1)), bar.Height);
    }

    public static Rectangle BarRect(Rectangle r, int k) => Rectangle.Inflate(r, Math.Max(1, k / 2), Math.Max(1, k / 2));

    // ── 별 + 번호 (판 번호·레벨). (x, y) 는 별 가운데, 숫자는 그 오른쪽에 ──
    public static void Star(Overlay o, Graphics g, double x, double y, int scale, int n)
    {
        Stamps.Of(o.Art).Get("game/star", 0, scale).Put(o, g, (int)x, (int)y);
        Number(o, g, n, x + 14 * scale, y, scale, -1);
    }

    public static Rectangle StarRect(double x, double y, int scale, int digits = 3) =>
        new((int)x - 9 * scale, (int)y - 9 * scale, (20 + 5 * digits) * scale, 18 * scale);

    static (int X, int Y) Center(Rectangle f) => (f.Left + f.Width / 2, (int)(f.Top + f.Height * .45));

    // 판 가운데(조금 위)에 크게: 판 번호
    public static void Stage(Overlay o, Graphics g, Rectangle f, int k, int n)
    {
        var (cx, cy) = Center(f);
        Star(o, g, cx - (14 + 5 * Digits(n)) * k / 2 + 2 * k, cy, k, n);
    }

    // ── 3·2·1: 판 한가운데에 아주 크게 ──
    public static void Count(Overlay o, Graphics g, Rectangle f, int k, int n) =>
        Number(o, g, n, f.Left + f.Width / 2, f.Top + f.Height / 2, 5 * k);

    public static Rectangle CountRect(Rectangle f, int k) => Overlay.Around(f.Left + f.Width / 2, f.Top + f.Height / 2, 20 * k, 22 * k);

    // 판 번호·끝 점수가 그려지는 가운데 자리
    public static Rectangle CenterRect(Rectangle f, int k)
    {
        var (cx, cy) = Center(f);
        return Rectangle.Intersect(f, Overlay.Around(cx, cy, 50 * k, 32 * k));
    }

    // ── 끝: 어두운 상자 안에 점수를 크게. 새 기록이면 상자 위에 큰 왕관, 아니면 아래 줄에 작은 왕관 + 최고 기록 ──
    // icon: 점수 앞에 붙일 그림 이름 (잡은 수면 벌레 그림처럼). 있으면 숫자 앞에 × 를 붙인다. best 가 0 이면 아래 줄은 없다
    public static void Result(Overlay o, Graphics g, Rectangle f, int k, int score, int best, bool newBest, string? icon = null)
    {
        var stamps = Stamps.Of(o.Art);
        var (cx, cy) = Center(f);
        int big = k + 1, small = Small(k);
        bool second = !newBest && best > 0;
        int glyphs = Digits(score) + (icon != null ? 1 : 0);
        int w1 = (icon != null ? 14 * k : 0) + glyphs * 5 * big, w2 = second ? (13 + 5 * Digits(best)) * small : 0;
        int half = Math.Max(w1, w2) / 2 + 5 * k, row2 = cy + 5 * big + 2 * k + 4 * small;
        var box = o.Client(Rectangle.FromLTRB(cx - half, cy - 5 * big - 3 * k, cx + half, (second ? row2 + 4 * small : cy + 5 * big) + 3 * k));
        // 말풍선처럼 모서리를 한 칸 깎은 상자: 크림 테두리, 갈색 속
        g.FillRectangle(Pal.Cream, box.X + k, box.Y - k, box.Width - 2 * k, box.Height + 2 * k);
        g.FillRectangle(Pal.Cream, box.X - k, box.Y + k, box.Width + 2 * k, box.Height - 2 * k);
        g.FillRectangle(Pal.Cream, box);
        g.FillRectangle(Pal.Line, box.X + k, box.Y, box.Width - 2 * k, box.Height);
        g.FillRectangle(Pal.Line, box.X, box.Y + k, box.Width, box.Height - 2 * k);
        if (icon != null) stamps.Get(icon, 0, small).Put(o, g, cx - w1 / 2 + 7 * k, cy - k);
        Number(o, g, score, cx - w1 / 2 + (icon != null ? 14 * k : 0) + 5 * big / 2, cy, big, -1, icon != null ? 'x' : '\0');
        if (newBest) stamps.Get("game/crown", 0, big).Put(o, g, cx, cy - 5 * big - 3 * k - 7 * big);
        else if (second)
        {
            stamps.Get("game/crown", 0, small).Put(o, g, cx - w2 / 2 + 6 * small, row2);
            Number(o, g, best, cx - w2 / 2 + 13 * small + 5 * small / 2, row2, small, -1);
        }
    }
}

// 값이 바뀐 프레임만 알아낸다 (가만히 있는 표시는 다시 칠하지 않게). 프레임마다 Set 을 한 번 부르고, Dirty 에서 Changed 를 본다.
// 처음 두 프레임은 늘 바뀐 것으로 친다 (판이 막 열렸을 때 한 번은 그려야 한다)
struct Watch
{
    int last, frames;
    public bool Changed { get; private set; }   // 값이 바뀌었다 (처음 두 프레임 포함)
    public bool Fresh { get; private set; }     // 처음 두 프레임이다: 판 전체를 한 번 칠한다
    public void Set(int key) { Fresh = frames < 2; Changed = Fresh || key != last; last = key; if (Fresh) frames++; }
}
