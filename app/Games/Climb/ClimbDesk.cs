using System.Drawing.Drawing2D;

namespace Bolok;

// 장면 그려 보기용 가짜 바탕화면·문서 창 (시안 run/mockups/climb 의 그림을 그대로 옮겼다): 게임 코드가 아니다
static class ClimbDesk
{
    static ulong DeskHash(int a, int b) { ulong h = (ulong)(a * 73856093) ^ (ulong)(b * 19349663) ^ 0x9E3779B97F4A7C15UL; h ^= h >> 29; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 32; return h; }

    public static void Wallpaper(Graphics g, Rectangle r)
    {
        for (int y = r.Top; y < r.Bottom; y += 4)
        {
            double t = (double)(y - r.Top) / Math.Max(1, r.Height);
            g.FillRectangle(new SolidBrush(Color.FromArgb((int)(64 + 40 * t), (int)(98 + 30 * t), (int)(138 + 20 * t))), r.Left, y, r.Width, 4);
        }
        // 부드러운 물결 무늬
        for (int i = 0; i < 3; i++)
        {
            using var b = new SolidBrush(Color.FromArgb(22, 255, 255, 255));
            var pts = new List<PointF>();
            for (int x = r.Left; x <= r.Right; x += 10) pts.Add(new PointF(x, r.Top + r.Height * (.35f + i * .18f) + (float)Math.Sin(x * .006 + i) * 40));
            pts.Add(new PointF(r.Right, r.Bottom)); pts.Add(new PointF(r.Left, r.Bottom));
            g.FillPolygon(b, pts.ToArray());
        }
    }

    // 문서 창: 제목 줄, 도구 줄, 흰 쪽에 글줄처럼 보이는 회색 줄
    public static void DocWindow(Graphics g, Rectangle r, bool dark = false)
    {
        var oldClip = g.Clip;
        using (var sh = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillRectangle(sh, r.X + 4, r.Y + 6, r.Width, r.Height);
        g.SetClip(r, CombineMode.Intersect);
        Color bg = dark ? Color.FromArgb(30, 32, 38) : Color.FromArgb(243, 244, 246);
        g.FillRectangle(new SolidBrush(bg), r);
        // 제목 줄
        int top = r.Top;
        g.FillRectangle(new SolidBrush(dark ? Color.FromArgb(24, 25, 30) : Color.FromArgb(238, 240, 243)), r.Left, top, r.Width, 32);
        g.FillRectangle(new SolidBrush(dark ? Color.FromArgb(90, 140, 220) : Color.FromArgb(56, 110, 210)), r.Left + 12, top + 10, 12, 12);
        g.FillRectangle(new SolidBrush(dark ? Color.FromArgb(150, 154, 162) : Color.FromArgb(120, 126, 136)), r.Left + 34, top + 13, 150, 6);
        using (var pen = new Pen(dark ? Color.FromArgb(180, 184, 190) : Color.FromArgb(80, 86, 96), 1.4f))
        {
            int bx = r.Right - 138;
            g.DrawLine(pen, bx + 18, top + 16, bx + 28, top + 16);
            g.DrawRectangle(pen, bx + 64, top + 11, 10, 10);
            g.DrawLine(pen, bx + 110, top + 11, bx + 120, top + 21); g.DrawLine(pen, bx + 120, top + 11, bx + 110, top + 21);
        }
        top += 32;
        // 도구 줄
        g.FillRectangle(new SolidBrush(dark ? Color.FromArgb(37, 39, 46) : Color.FromArgb(250, 250, 251)), r.Left, top, r.Width, 40);
        for (int i = 0; i < 14; i++)
        {
            int x = r.Left + 14 + i * 34 + (i > 4 ? 14 : 0) + (i > 9 ? 14 : 0);
            g.FillRectangle(new SolidBrush(dark ? Color.FromArgb(78, 82, 92) : Color.FromArgb(200, 205, 214)), x, top + 12, 20, 16);
        }
        g.FillRectangle(new SolidBrush(dark ? Color.FromArgb(50, 52, 60) : Color.FromArgb(225, 228, 233)), r.Left, top + 40, r.Width, 1);
        top += 41;
        // 쪽 (가운데 흰 종이) 또는 코드
        int pageL = r.Left + 40, pageR = r.Right - 40;
        if (!dark) g.FillRectangle(Brushes.White, pageL, top + 16, pageR - pageL, r.Bottom - top);
        else g.FillRectangle(new SolidBrush(Color.FromArgb(32, 34, 40)), r.Left, top, r.Width, r.Bottom - top);
        // 스크롤 막대
        g.FillRectangle(new SolidBrush(dark ? Color.FromArgb(70, 74, 84) : Color.FromArgb(200, 204, 212)), r.Right - 10, top + 30, 5, 80);
        int y = top + 48, row = 0;
        int textL = dark ? r.Left + 56 : pageL + 46, textR = dark ? r.Right - 30 : pageR - 46;
        while (y < r.Bottom - 10)
        {
            ulong h = DeskHash(row, 3);
            if (dark)
            {
                g.FillRectangle(new SolidBrush(Color.FromArgb(80, 86, 98)), r.Left + 14, y, 18, 5);
                int indent = (int)(h % 4) * 20, x = textL + indent;
                int words = 4 + (int)(h >> 4) % 9;
                for (int wd = 0; wd < words && x < textR; wd++)
                {
                    ulong hw = DeskHash(row, wd + 10);
                    int ww = 24 + (int)(hw % 90);
                    var col = ((hw >> 8) % 5) switch { 0 => Color.FromArgb(198, 120, 221), 1 => Color.FromArgb(97, 175, 239), 2 => Color.FromArgb(152, 195, 121), 3 => Color.FromArgb(229, 192, 123), _ => Color.FromArgb(171, 178, 191) };
                    g.FillRectangle(new SolidBrush(col), x, y, Math.Min(ww, textR - x), 5);
                    x += ww + 8;
                }
                y += 18; row++;
                continue;
            }
            if (row % 9 == 0)
            {
                g.FillRectangle(new SolidBrush(Color.FromArgb(70, 76, 88)), textL, y, 140 + (int)(h % 120), 9);
                y += 26; row++;
                continue;
            }
            if (row % 9 == 5)
            {
                g.FillRectangle(new SolidBrush(Color.FromArgb(219, 232, 247)), textL, y, 150, 86);
                g.FillPolygon(new SolidBrush(Color.FromArgb(160, 192, 226)), new[] { new Point(textL + 20, y + 72), new Point(textL + 64, y + 30), new Point(textL + 96, y + 60), new Point(textL + 112, y + 46), new Point(textL + 136, y + 72) });
                g.FillEllipse(new SolidBrush(Color.FromArgb(250, 210, 120)), textL + 104, y + 14, 14, 14);
                for (int l = 0; l < 5; l++) g.FillRectangle(new SolidBrush(Color.FromArgb(178, 184, 194)), textL + 166, y + 6 + l * 16, Math.Min(textR - textL - 166, 120 + (int)(DeskHash(row, l) % 140)), 6);
                y += 104; row++;
                continue;
            }
            int lines = 2 + (int)(h % 3);
            for (int l = 0; l < lines; l++)
            {
                int width = l == lines - 1 ? (int)((textR - textL) * (.35 + (DeskHash(row, l) % 50) / 100.0)) : textR - textL - (int)(DeskHash(row, l) % 30);
                bool bullet = row % 9 == 7;
                int x = textL + (bullet ? 18 : 0);
                if (bullet && l == 0) g.FillEllipse(new SolidBrush(Color.FromArgb(150, 156, 166)), textL + 4, y, 6, 6);
                // 낱말 사이 틈
                int cx = x;
                ulong hw = DeskHash(row * 7 + l, 1);
                while (cx < x + width - 10)
                {
                    int ww = 14 + (int)(hw % 46); hw = hw * 6364136223846793005UL + 1442695040888963407UL;
                    var col = (hw >> 40) % 23 == 0 ? Color.FromArgb(96, 140, 220) : Color.FromArgb(176, 182, 192);
                    g.FillRectangle(new SolidBrush(col), cx, y, Math.Min(ww, x + width - cx), 6);
                    cx += ww + 6;
                }
                y += 16;
            }
            y += 14; row++;
        }
        g.Clip = oldClip;
        using (var edge = new Pen(dark ? Color.FromArgb(70, 72, 80) : Color.FromArgb(168, 172, 180))) g.DrawRectangle(edge, r.X, r.Y, r.Width - 1, r.Height - 1);
    }
}
