using Px = Bolok.ClimbArt.Px;

namespace Bolok;

// 클라이밍 테마 그림 공통 도구 (docs/climb.md 「테마」, 시안 run/mockups/climb2): 발판 한 장·덩어리 만들기·가운데 쪼개기 …
// 알파는 0·255 뿐 (색 열쇠 창), 그림자는 ClimbView 가 점무늬로 만든다. 글자 그림 없음.

sealed record SkinPx(Px P, int Ax, int Ay, int Sink = 0);
sealed record SkinTone(uint F, uint H, uint S);

static class SkinKit
{
    public static uint C(string h) => ClimbArt.C(h);
    public static readonly uint Line = ClimbArt.Line, White = ClimbArt.White;
    public static uint Mix(uint a, uint b, double t) => ClimbArt.Mix(a, b, t);
    public static SkinTone Tone(string hex, double hi = .42, double sh = .28)
    {
        var f = C(hex);
        return new SkinTone(f, Mix(f, White, hi), Mix(f, C("#5A3048"), sh));
    }
    // 둥근 모서리 덩어리: 윗줄 H, 아랫줄 S, 나머지 F. r 이 크면 모서리를 더 깎는다
    public static void Block(Px g, int x, int y, int w, int h, SkinTone t, int r = 1)
    {
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                int dx = Math.Min(i, w - 1 - i), dy = Math.Min(j, h - 1 - j);
                if (dx + dy < r) continue;
                g.Put(x + i, y + j, j == 0 ? t.H : j == h - 1 ? t.S : t.F);
            }
    }
    public static SkinPx Flip(SkinPx s, int dir) => dir >= 0 ? s : new SkinPx(s.P.Flipped(), s.P.W - 1 - s.Ax, s.Ay, s.Sink);
    public static SkinPx Fin(Px g, int ax, int ay, int sink = 0) { g.Outline(Line); return new SkinPx(g, ax, ay, sink); }
    // 가로 한 줄 (x0~x1) 을 y 에 칠한다
    public static void Row(Px g, int x0, int x1, int y, uint c) { for (int x = x0; x <= x1; x++) g.Put(x, y, c); }
    // 점선 아닌 두 칸 굵은 선 (테두리 포함 그림용): 속 색 c, 둘레는 Outline 이 칠한다
    public static void Thick(Px g, double x0, double y0, double x1, double y1, uint c, int w = 2)
    {
        for (int k = 0; k < w; k++) g.LineTo(x0, y0 + k, x1, y1 + k, c);
    }
    // 둘레에 빈 칸을 덧댄다 (테두리를 그릴 자리)
    public static Px Pad(Px g, int n)
    {
        var r = new Px(g.W + n * 2, g.H + n * 2);
        r.Blit(g, n, n);
        return r;
    }
    // 줍는 것: 가장 아래 찬 줄 가운데가 발판에 닿는다
    public static SkinPx Base(Px p)
    {
        int low = 0;
        for (int y = 0; y < p.H; y++) for (int x = 0; x < p.W; x++) if (p.On(x, y)) low = y;
        return new SkinPx(p, p.W / 2, low);
    }
    public static Px Scaled(Px p, int k)
    {
        var r = new Px(p.W * k, p.H * k);
        for (int y = 0; y < r.H; y++) for (int x = 0; x < r.W; x++) r.P[y * r.W + x] = p.P[y / k * p.W + x / k];
        return r;
    }
    // 가운데 줄 x(y) 로 둘로 쪼개 따로 옮긴다 (갈라짐). xs: 각 줄의 갈라지는 x
    public static Px Split(Px src, Func<int, int> xs, int dxL, int dyL, int dxR, int dyR, int pad = 4)
    {
        var r = new Px(src.W + pad * 2, src.H + pad * 2);
        for (int y = 0; y < src.H; y++)
            for (int x = 0; x < src.W; x++)
            {
                uint c = src.P[y * src.W + x];
                if (c >> 24 == 0) continue;
                bool left = x < xs(y);
                r.Put(x + pad + (left ? dxL : dxR), y + pad + (left ? dyL : dyR), c);
            }
        return r;
    }
}
