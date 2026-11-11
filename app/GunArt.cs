using System.Drawing.Drawing2D;

namespace Bolok;

// 총 그리기: 손잡이(칸의 10, 21)를 축으로 조준점 쪽으로 돌린다. 불꽃은 총과 같은 각도로 돌려 총구 끝에 붙인다
public static class GunArt
{
    const int PivotX = 10, PivotY = 21;
    static readonly Dictionary<string, (int X, int Y)> tips = new();

    // 총 그림에서 가장 오른쪽 열이 총구 끝
    static (int X, int Y) Tip(Sprites sp, string name)
    {
        if (tips.TryGetValue(name, out var t)) return t;
        var bmp = sp.Get(name, 0, 1);
        for (int x = bmp.Width - 1; x >= 0; x--)
        {
            var ys = Enumerable.Range(0, bmp.Height).Where(y => bmp.GetPixel(x, y).A > 0).ToList();
            if (ys.Count > 0) return tips[name] = (x, (int)Math.Round(ys.Average()));
        }
        return tips[name] = (PivotX, PivotY);
    }

    public static void Draw(Graphics g, Sprites sp, string gun, PointF pivot, double angle, double kick, int k, int flashFrame)
    {
        string name = "game/gun_" + gun;
        var bmp = sp.Get(name, 0, k);
        float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
        var state = g.Save();
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.TranslateTransform(pivot.X - cos * (float)kick, pivot.Y - sin * (float)kick);
        g.RotateTransform((float)(angle * 180 / Math.PI));
        if (cos < 0) g.ScaleTransform(1, -1); // 왼쪽을 겨누면 뒤집어서 총이 거꾸로 서지 않게
        g.DrawImage(bmp, -PivotX * k, -PivotY * k, bmp.Width, bmp.Height);
        if (flashFrame >= 0)
        {
            var (tx, ty) = Tip(sp, name);
            var flame = sp.Get("game/muzzle", flashFrame, k);
            g.DrawImage(flame, (tx + 1 - PivotX) * k, (ty - 20 - PivotY) * k, flame.Width, flame.Height);
        }
        g.Restore(state);
    }
}
