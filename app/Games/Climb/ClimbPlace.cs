namespace Bolok;

// 클라이밍 판 자리 (docs/climb.md 2-1, YELL 2026-11-25): 볼록이 가까이에서 연다. 화면과 무관한 순수 계산이라 --selfcheck 로 검사한다.
//  · 모양 A4 (창 테두리 덩굴): 가끔 (세 판에 한 번쯤), 볼록이가 창 왼쪽·오른쪽 테두리 가까이에 있고 그 테두리가 판 높이만큼 비어 있고 테두리 양옆이 화면 안일 때
//  · 그 밖에는 모양 A2 (콩나무 투명): 볼록이가 올라간 맨 위 창이 판보다 넉넉하면 그 창 안 볼록이 가까이, 아니면 바탕화면 볼록이 둘레 (아이스런과 같은 규칙, Kit/PanelPlace)
// 판 크기: A2 150×176칸, A4 96×176칸 (칸 = 그림 한 점 = 화면 f 점)
sealed record ClimbPlan(ClimbLook Look, Rectangle Panel, IntPtr Win, int Side, Point Off, string Why);

static class ClimbPlace
{
    public static int Width(ClimbLook look) => look == ClimbLook.A2 ? ClimbView.WideA2 : ClimbView.WideA4;

    // 창 테두리에 걸친 판 (A4): side +1 오른쪽 테두리, −1 왼쪽. 판 가운데(줄기)가 테두리 위. 화면(모니터)에 안 들어가거나 창이 판보다 작으면 null.
    // wantY: 판 위쪽이 되고 싶은 자리 (창 안으로 눌러 넣는다)
    public static Rectangle? EdgePanel(Rectangle win, int side, double wantY, int pw, int ph, Rectangle monitor)
    {
        int bx = side > 0 ? win.Right : win.Left, x = bx - pw / 2;
        var inside = Rectangle.Intersect(win, monitor);
        if (x < monitor.Left || x + pw > monitor.Right) return null;   // 양옆 모두 화면 안이어야 한다 (최대화 창은 바깥 자리가 없다)
        if (inside.Height < ph || win.Width < pw) return null;         // 테두리가 판 높이만큼 있어야 한다
        return new Rectangle(x, (int)Math.Clamp(wantY, inside.Top, inside.Bottom - ph), pw, ph);
    }

    // wins: 위에서 아래 순서 (Native.ListWindows). petX·petY: 볼록이 발 (화면). cell: 볼록이 칸 (px). edge: A4 를 써도 되나 (이번 판이 「가끔」에 뽑혔나)
    public static ClimbPlan Plan(IReadOnlyList<Win> wins, double petX, double petY, int cell, int f, IReadOnlyList<Rectangle> screens, Rectangle petArea, bool edge)
    {
        int ph = ClimbView.PH * f;
        double petYa = petY + (ClimbView.PH - ClimbView.Foot - 8) * f;   // PanelPlace 는 볼록이가 판 아래에서 8칸 위에 오게 놓는다: 이 판은 발이 위에서 Foot 줄 (아래에서 40칸)
        if (edge)
        {
            int pw = Width(ClimbLook.A4) * f;
            ClimbPlan? best = null;
            double bestDx = double.MaxValue;
            for (int i = 0; i < wins.Count; i++)
            {
                var w = wins[i];
                if (w.Topmost) continue;
                var mon = PanelPlace.MostOn(screens, petArea, w.R);
                foreach (int side in new[] { 1, -1 })
                {
                    double bx = side > 0 ? w.R.Right : w.R.Left, dx = Math.Abs(petX - bx);
                    if (dx > cell * 1.25 || petY - cell / 2.0 < w.R.Top || petY - cell / 2.0 > w.R.Bottom || dx >= bestDx) continue;   // 볼록이가 그 테두리 가까이 있어야 한다
                    if (EdgePanel(w.R, side, petY - ClimbView.Foot * f, pw, ph, mon) is not { } p) continue;
                    bool covered = false;                                                                                        // 테두리가 다른 창에 가리지 않아야 한다 (위 창·항상 위 창)
                    for (int k = 0; k < wins.Count && !covered; k++) covered = k != i && (k < i || wins[k].Topmost) && wins[k].R.IntersectsWith(p);
                    if (covered) continue;
                    (best, bestDx) = (new ClimbPlan(ClimbLook.A4, p, w.H, side, new Point(p.X - w.R.X, p.Y - w.R.Y), "edge"), dx);
                }
            }
            if (best != null) return best;
        }
        int pw2 = Width(ClimbLook.A2) * f;
        var at = new Point((int)petX, (int)(petY - cell / 2.0));
        string why = edge ? "no-edge" : "plain";
        foreach (var w in wins)   // 위 창부터
        {
            if (w.Topmost || !w.R.Contains(at)) continue;
            var inside = Rectangle.Intersect(w.R, PanelPlace.MostOn(screens, petArea, w.R));
            if (inside.Width >= pw2 + 24 && inside.Height >= ph + 48)
            {
                var p = PanelPlace.NearPet(inside, petX, petYa, pw2, ph, f);
                return new ClimbPlan(ClimbLook.A2, p, w.H, 0, new Point(p.X - w.R.X, p.Y - w.R.Y), why + "-window");
            }
            break;   // 볼록이 바로 밑 창만 본다 (그 아래 가려진 창은 쓰지 않는다)
        }
        return new ClimbPlan(ClimbLook.A2, PanelPlace.Around(petArea, petX, petYa, pw2, ph, f), IntPtr.Zero, 0, Point.Empty, why + "-desktop");
    }

    // 판의 테마 (docs/climb.md 「테마」, YELL 2026-11-25 「덩굴하고 무작위로」): 창 테두리 덩굴(A4)은 늘 덩굴. 그 밖(A2)은 덩굴·책상 위 문구·과자 탑·풍선·장난감 블록 다섯 가운데 씨로 고른다 (같은 씨면 같은 테마, 고르게)
    // forced: 디버그 climb theme 로 정한 것 (null 이면 씨로). A4 에서는 정해도 덩굴이다
    public static ClimbTheme Theme(int seed, ClimbLook look, ClimbTheme? forced = null)
    {
        if (look == ClimbLook.A4) return ClimbTheme.Vine;
        return forced ?? Pick(seed);
    }

    public static ClimbTheme Pick(int seed)
    {
        uint h = unchecked((uint)seed * 2246822519u + 0x9E3779B9u);
        h ^= h >> 15; h *= 2654435761u; h ^= h >> 13; h *= 0x85EBCA6Bu; h ^= h >> 16;
        return (ClimbTheme)(h % 5);
    }

    // 세 판에 한 번쯤: 씨로 정한다 (같은 씨면 같은 모양)
    public static bool Coin(int seed) { uint h = unchecked((uint)seed * 2654435761u); return (h >> 11) % 3 == 0; }
}
