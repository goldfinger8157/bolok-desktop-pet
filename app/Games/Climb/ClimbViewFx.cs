using Px = Bolok.ClimbArt.Px;

namespace Bolok;

// 클라이밍 콤보 효과·카페 그리기 (ClimbView 의 나머지): docs/climb-combo.md 1-3·1-4·5-6·5-7, 시안 run/mockups/climb-combo (C1 「몸 따라」)
// · 콤보: 폴짝이 낮아지고(HopArc) 발이 바삐 움직이고 몸이 기운다 (PetNow) + 점무늬 잔상 · 발밑 테마 조각 · 3단은 지나온 발판에 반짝 · 5마다 발밑 고리 · 머리 옆 숫자 쪽지 (다음 발판 반대쪽)
// · 끊기면 회색 쪽지가 툭 떨어진다. 효과는 볼록이 몸과 다음 네 잎 발판 위에는 안 얹는다 (fxBlock) — 앞길이 가려지지 않게
// · 카페: 테마마다 그림 하나(A4 는 덩굴 것), 볼록이가 걸어 들어가 앉아 컵을 쥐고 홀짝 (sip 자세), 다녀온 카페는 불이 꺼지고 컵이 엎어진다
sealed partial class ClimbView
{
    static readonly int[] Crumbs = { -3, 12, 3, 14, 9, 11, 15, 14 };
    bool fxNow;                       // 이번 Compose 에서 콤보 효과를 그린다
    internal bool NoFx;               // 검사: 콤보 효과를 끄고 그린다 (켠 그림과 견주어 효과가 어디를 칠했나 본다)
    internal Rectangle LastTag;       // 검사: 마지막으로 그린 숫자 쪽지 (칸)

    // ── 서명 (바뀔 때만 다시 그린다) ──
    int ComboSig(Climb c, ClimbShow s)
    {
        double a5 = c.Clock - c.Combo5At, ab = c.Clock - c.ComboEndAt;
        int ring = c.Combo5At > 0 && a5 < .25 ? (a5 < .1 ? 1 : 2) : 0;
        int br = c.ComboEndAt > 0 && ab < .45 && c.ComboBroken >= c.T.Tier1 ? 1 + (int)(ab * 20) : 0;
        return c.Combo * 64 + ring * 16 + br;
    }

    int CafeSig(Climb c, ClimbShow s)
    {
        if (c.CafePhase == 0) return 0;
        int fine = c.CafePhase is 1 or 3 ? (int)(c.CafeLeft * 40) : (int)(s.Clock * 3);
        return 1 + c.CafePhase + 8 * fine + (c.Bar >= 1 && c.Clock - c.CafeFullAt < .3 ? 4000 : 0) + (c.CafeStay >= c.T.CafeSipSec ? 8000 : 0);
    }

    bool FxActive(Climb c, ClimbShow s)
    {
        bool on = false;
        if (!s.Result && !c.InCafe && !NoFx)
            on = c.Tier >= 1 && !c.Done || c.ComboEndAt > 0 && c.Clock - c.ComboEndAt < .45 && c.ComboBroken >= c.T.Tier1;   // 단계 효과 · 끊긴 쪽지
        return fxNow = on;
    }

    // ── 칸 막기: 효과가 못 얹는 칸 ──
    void MarkBlock(Px p, int x, int y, byte v)
    {
        int x0 = Math.Max(0, -x), x1 = Math.Min(p.W, CW - x), y0 = Math.Max(0, -y), y1 = Math.Min(p.H, PH - y);
        for (int j = y0; j < y1; j++)
            for (int i = x0; i < x1; i++)
                if (p.P[j * p.W + i] >> 24 != 0) fxBlock[(y + j) * CW + x + i] |= v;
    }

    // 다음 네 잎(N+1~N+4)의 발판은 효과가 못 덮는다 (ComposeVine·ComposeSkin 이 발판을 찍은 뒤 부른다)
    void MarkAhead(Climb c, int j, Px p, int x, int y)
    {
        if (fxNow && j > c.N && j <= c.N + 4) MarkBlock(p, x, y, 2);
    }

    void FxPut(int x, int y, uint col)
    {
        if ((uint)x >= (uint)CW || (uint)y >= (uint)PH) return;
        int k = y * CW + x;
        if (fxBlock[k] == 0) cells[k] = col;
    }

    void FxBlit(Px p, int x, int y)
    {
        for (int j = 0; j < p.H; j++)
            for (int i = 0; i < p.W; i++)
            {
                uint col = p.P[j * p.W + i];
                if (col >> 24 != 0) FxPut(x + i, y + j, col);
            }
    }

    bool BoxFree(int x, int y, int w, int h)
    {
        if (x < 1 || y < 1 || x + w > CW - 1 || y + h > PH - 1) return false;
        for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) if (fxBlock[(y + j) * CW + x + i] != 0) return false;
        return true;
    }

    // ── 콤보 효과 ──
    void ComboFx(Climb c, ClimbShow s, double px, double footY, bool left)
    {
        int tier = c.Tier, pxi = (int)Math.Round(px), dir = left ? -1 : 1;
        double a5 = c.Clock - c.Combo5At, ab = c.Clock - c.ComboEndAt;
        if (tier >= 1 && !c.Done)
        {
            // 잔상: 지나온 길을 따라 (뒤쪽에만) 단계 색 점무늬 실루엣
            double step = tier == 3 ? .5 : .6;
            int arc = c.T.HopArc[tier];
            for (int k = 1; k <= tier; k++)
            {
                double vg = vN - step * k, xg = PetX(c, vg), fr = vg - Math.Floor(vg);
                double yg = Foot + DY * (vN - vg) - arc * 4 * fr * (1 - fr);
                int col = tier == 1 ? 0 : tier == 2 ? 1 : 2 + ((k - 1 + c.Combo / 5) & 1);
                FxBlit(fx.Ghost(col, left, (int)Math.Floor(vg) & 1), (int)Math.Round(xg) - 20, (int)Math.Round(yg) - pet.Foot);
            }
            if (tier >= 3) FxPassed(c);
            // 발밑 조각: 선 발판과 그 아래, 지나온 쪽에만
            var set = fx.Trail[tier - 1];
            int n = tier == 1 ? 3 : tier == 2 ? 6 : 9, seed = c.Combo / 5 * 7;
            for (int i = 0; i < n; i++)
            {
                var pc = set[(i + seed) % set.Length];
                int jx = (i * 5 + seed) % 7 - 3, jy = (i * 3 + seed) % 5;
                FxBlit(pc, pxi - dir * (7 + i * 3) + jx - pc.W / 2, Foot + 3 + i * 2 + jy - pc.H / 2);
            }
            // 5마다 터짐: 발밑 납작한 고리 (퍼지기 전 · 퍼진 뒤)
            if (c.Combo5At > 0 && a5 < .25)
            {
                int fr = a5 < .1 ? 0 : 1, m = 10;
                double rx = fr == 0 ? 13 : 22, ry = fr == 0 ? 3 : 5;
                for (int i = 0; i < m; i++)
                {
                    double a = Math.PI * 2 * i / m + fr * .3;
                    var pc = fx.Burst[i % fx.Burst.Length];
                    FxBlit(pc, (int)Math.Round(px + Math.Cos(a) * rx) - pc.W / 2, (int)Math.Round(Foot + 2 + Math.Sin(a) * ry) - pc.H / 2 + (fr == 1 ? 3 : 0));
                }
            }
            PutTag(c, c.Combo, tier, false, pxi, left, a5 < .25 ? -3 : 0);
        }
        else if (c.ComboEndAt > 0 && ab < .45 && c.ComboBroken >= c.T.Tier1)
            PutTag(c, c.ComboBroken, 0, true, pxi, left, (int)(ab * ab * 70));   // 끊김: 회색 쪽지가 툭 떨어진다
    }

    // 3단: 지나온 발판 셋에 반짝 (덩굴은 작은 꽃)
    void FxPassed(Climb c)
    {
        for (int j = c.N - 1; j >= c.N - 3; j--)
        {
            var (x, y) = LeafAt(c, j, vN);
            if (y > PH - 6) continue;
            var pc = Skin == null && j % 2 == 0 ? fx.Bloom : fx.Twinkle;
            FxBlit(pc, x + (j % 2 == 0 ? -7 : 6) - pc.W / 2, y - 5 - pc.H / 2);
        }
    }

    // ── 도트 숫자 쪽지: 프레임마다 새 그림을 안 만들고 표에서 바로 칠한다 ──
    static int DigitCount(int v) { int n = 1; while (v >= 10) { v /= 10; n++; } return n; }
    static bool InChip(int x, int y, int w, int h)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return false;
        return Math.Min(x, w - 1 - x) + Math.Min(y, h - 1 - y) >= 1;      // 모서리 한 칸 깎음
    }

    void ChipSize(int combo, bool big, out int w, out int h)
    {
        int gw = big ? 5 : 3, gh = big ? 7 : 5, pad = big ? 3 : 2;
        w = DigitCount(combo) * (gw + 1) - 1 + pad * 2 + 2;     // 테두리 한 칸씩 포함
        h = gh + pad * 2 + 2;
    }

    void PutChip(int x, int y, int combo, int tier, bool broken)
    {
        bool big = tier >= 3 && !broken;
        int n = DigitCount(combo), gw = big ? 5 : 3, gh = big ? 7 : 5, pad = big ? 3 : 2;
        int iw = n * (gw + 1) - 1 + pad * 2, ih = gh + pad * 2;
        uint frame = broken ? FxPaint.GreyFrame : FxPaint.TierCol(tier, combo / 5), ink = broken ? FxPaint.GreyInk : FxPaint.Line, fill = broken ? FxPaint.GreyFill : FxPaint.ChipFill;
        var table = big ? FxPaint.F57 : FxPaint.F35;
        for (int py = 0; py < ih + 2; py++)
            for (int pxx = 0; pxx < iw + 2; pxx++)
            {
                int lx = pxx - 1, ly = py - 1;
                uint col = 0;
                if (InChip(lx, ly, iw, ih))
                {
                    col = lx == 0 || ly == 0 || lx == iw - 1 || ly == ih - 1 ? frame : fill;
                    int dx = lx - pad, dy = ly - pad;
                    if (dx >= 0 && dy >= 0 && dy < gh)
                    {
                        int idx = dx / (gw + 1), cx = dx % (gw + 1);
                        if (idx < n && cx < gw)
                        {
                            int v = combo;
                            for (int k = n - 1 - idx; k > 0; k--) v /= 10;
                            if (table[v % 10][dy * gw + cx] == '1') col = ink;
                        }
                    }
                }
                else if (InChip(lx - 1, ly, iw, ih) || InChip(lx + 1, ly, iw, ih) || InChip(lx, ly - 1, iw, ih) || InChip(lx, ly + 1, iw, ih)) col = FxPaint.Line;
                if (col != 0) FxPut(x + pxx, y + py, col);
            }
    }

    // 쪽지 자리: 머리 옆 다음 발판 반대쪽 → 몸 옆 → 더 바깥 → 발밑 옆. 다음 네 잎 발판·볼록이 몸에 안 겹치는 첫 자리 (모두 겹치면 발밑 옆)
    void PutTag(Climb c, int combo, int tier, bool broken, int pxi, bool left, int drop)
    {
        bool big = tier >= 3 && !broken;
        ChipSize(combo, big, out int w, out int h);
        int side = left ? 1 : -1, hw = pet.HalfW, tall = pet.Tall;
        int x0 = pxi + side * (hw + 4 + w / 2) - w / 2, xFar = x0 + side * 6;
        int headY = Foot - tall - 2, bodyY = Foot - tall / 2 - h / 2, lowY = Foot + 3;
        int bx = x0, by = headY;
        bool ok = false;
        for (int k = 0; k < 5 && !ok; k++)           // 머리 옆 · 몸 옆 · 더 바깥 머리 옆 · 더 바깥 몸 옆 · 발밑 옆
        {
            int cx = k >= 2 && k < 4 ? xFar : x0, cy = k switch { 0 or 2 => headY, 1 or 3 => bodyY, _ => lowY };
            int xx = Math.Clamp(cx, 1, CW - w - 1);
            if (!BoxFree(xx, cy, w, h)) continue;
            (bx, by, ok) = (xx, cy, true);
        }
        if (!ok) (bx, by) = (Math.Clamp(x0, 1, CW - w - 1), lowY);
        by += drop;
        LastTag = new Rectangle(bx, by, w, h);
        PutChip(bx, by, combo, tier, broken);
        if (broken)
        {
            for (int k = 0; k < Crumbs.Length; k += 2) FxPut(bx + Crumbs[k] + 2, by + Crumbs[k + 1] + drop / 2, FxPaint.GreyFrame);   // 떨어지는 부스러기
            return;
        }
        if (tier >= 3) { FxBlit(fx.Twinkle, bx + w - 3, by - 4); FxBlit(fx.Twinkle, bx - 3, by + h - 3); }
        else if (tier == 2) FxBlit(fx.Twinkle, bx + w - 2, by - 3);
    }

    // ── 카페 ──
    // 카페 발판의 카페를 발판 뒤에 그린다 (열림: 불·김 · 다녀감: 불 꺼짐·컵 엎어짐·바램)
    void DrawCafes(Climb c, int lo, int hi)
    {
        for (int j = Math.Max(lo, 1); j <= hi; j++)
        {
            ref var lf = ref c.At(j);
            if (lf.Cafe == 0) continue;
            int d = lf.Cafe;
            bool open = !lf.Visited || c.InCafe && j == c.N;
            var (lx, ly) = LeafAt(c, j, vN);
            var spr = fx.Cafe(open, d, out int sx, out int sy);
            PutPx(spr, lx + d * fx.SeatOff - sx, ly - sy);
        }
    }

    // 카페 안 볼록이: 걸어 들어감 → 앉음 → 홀짝 (막대가 차면 happy, 6초 뒤 컵이 빔) → 걸어 나옴
    ClimbPose CafePet(Climb c, ClimbShow s, ref double x, ref double footY, ref bool left, ref int frame, ref int id, int sink)
    {
        int d = c.CafeSideNow;
        double lx = x, seat = lx + d * fx.SeatOff;
        footY = Foot;
        if (c.CafePhase is 1 or 3)
        {
            bool inn = c.CafePhase == 1;
            double q = Math.Clamp(1 - c.CafeLeft / (inn ? c.T.CafeInSec : c.T.CafeOutSec), 0, 1);
            x = inn ? lx + (seat - lx) * q : seat + (lx - seat) * q;
            footY = Foot + sink * (inn ? 1 - q : q);
            left = inn ? d < 0 : d > 0;
            frame = 1 + (int)(q * 6) % 2 * 2; id = 30;
            return pet.Walk;
        }
        x = seat;
        double sat = c.CafeStay - c.T.CafeInSec;
        if (sat < .25) { left = d > 0; frame = (int)(s.Clock * 2); id = 31; return pet.Idle; }      // 앉아 숨 고르며 발판 쪽을 본다
        left = d < 0;
        if (c.Bar >= 1 && c.Clock - c.CafeFullAt < .3) { frame = (int)(s.Clock * 6); id = 34; return pet.Happy; }
        if (c.CafeStay >= c.T.CafeSipSec) { frame = (int)(s.Clock * 2); id = 33; return pet.SipEnd; }
        frame = (int)(s.Clock * 3); id = 32;
        return pet.Sip;
    }

    // ── 자가 검사 ──
    internal uint[] CellsNow(Climb c, ClimbShow s) { Compose(); return cells; }
    internal byte[] Blocks => fxBlock;

    (int X, int Y) LeafXY(Climb c, int j, double vn, double vx)
    {
        var (dir, col, _, _, _) = Info(c, j);
        return (Look == ClimbLook.A2 ? (int)Math.Round(CW / 2.0 + col * DX - vx) : CW / 2 + dir * SideA4, (int)Math.Round(Foot - DY * (j - vn)));
    }

    // 카페 그림이 차지하는 칸 상자 (볼록이가 vn 높이, 시선 가운데가 vx 일 때)
    internal Rectangle CafeBox(Climb c, int j, double vn, double vx)
    {
        int d = c.At(j).Cafe;
        var (lx, ly) = LeafXY(c, j, vn, vx);
        var spr = fx.Cafe(true, d, out int sx, out int sy);
        return new Rectangle(lx + d * fx.SeatOff - sx, ly - sy, spr.W, spr.H);
    }

    // 발판 j 그림이 차지하는 칸 상자 (서 있지 않은 보통 모습)
    internal Rectangle StepBox(Climb c, int j, double vn, double vx)
    {
        var (x, y) = LeafXY(c, j, vn, vx);
        var (_, _, kind, _, _) = Info(c, j);
        if (Skin != null) { var sp = Skin.Step(VarOf(j), Info(c, j + 1).Dir, 0); return new Rectangle(x - sp.Ax, y - sp.Ay, sp.P.W, sp.P.H); }
        int z = c.Zone(Math.Max(j, 0)), d = Info(c, j).Dir < 0 ? 0 : 1;
        var lf = kind == ClimbKind.Flower ? leafBig[z, d] : leaf[z, d];
        return new Rectangle(x - lf.Ax, y - lf.Ay, lf.P.W, lf.P.H);
    }
}
