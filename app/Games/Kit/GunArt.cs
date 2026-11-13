using System.Drawing.Drawing2D;

namespace Bolok;

// 총 한 자루 그리기: 손잡이(칸의 10, 21)를 축으로 조준점 쪽으로 돌린다. 불꽃은 총과 같은 각도로 돌려 총구 끝에 붙인다.
// 그림은 둘레를 잘라 미리 곱한 알파로 받아 두고(Stamps), 그려지는 자리(Rect)도 같은 셈으로 낸다 → 총이 움직인 프레임에 그 자리만 다시 칠할 수 있다
sealed class GunArt
{
    public const int PivotX = 10, PivotY = 21;

    readonly Stamp gun;
    readonly Stamp[] muzzle = new Stamp[2];
    readonly Rectangle gunBox;                              // 손잡이에서 본 총 그림 자리 (오른쪽을 겨눌 때)
    readonly Rectangle[] muzzleBox = new Rectangle[2];

    // gun: 그림 이름의 뒤쪽 (game/gun_<gun>), k: 그릴 배율
    public GunArt(Sprites sp, string gun, int k)
    {
        var st = Stamps.Of(sp);
        string name = "game/gun_" + gun;
        int half = sp.Cell * k / 2;
        this.gun = st.Get(name, 0, k);
        gunBox = new Rectangle(half + this.gun.Dx - PivotX * k, half + this.gun.Dy - PivotY * k, this.gun.Bmp.Width, this.gun.Bmp.Height);
        var (tx, ty) = Tip(sp, name);
        for (int i = 0; i < 2; i++) // 불꽃 그림은 칸 왼쪽 가운데(0, 20)가 총구 끝에 붙는다
        {
            muzzle[i] = st.Get("game/muzzle", i, k);
            muzzleBox[i] = new Rectangle((tx + 1 - PivotX) * k + half + muzzle[i].Dx, (ty - 20 - PivotY) * k + half + muzzle[i].Dy, muzzle[i].Bmp.Width, muzzle[i].Bmp.Height);
        }
    }

    // 총 그림에서 가장 오른쪽 열이 총구 끝 (칸 안 자리)
    static (int X, int Y) Tip(Sprites sp, string name)
    {
        var bmp = sp.Get(name, 0, 1);
        for (int x = bmp.Width - 1; x >= 0; x--)
        {
            int n = 0, sum = 0;
            for (int y = 0; y < bmp.Height; y++) if (bmp.GetPixel(x, y).A > 0) { n++; sum += y; }
            if (n > 0) return (x, (int)Math.Round(sum / (double)n));
        }
        return (PivotX, PivotY);
    }

    // 손잡이가 (px, py) 일 때 총(과 불꽃)이 그려지는 자리. flash: 불꽃 그림 번호 (0·1), 없으면 -1. kick: 뒤로 밀린 거리(px)
    public Rectangle Rect(double px, double py, double angle, double kick, int flash)
    {
        double cos = Math.Cos(angle), sin = Math.Sin(angle), ox = px - cos * kick, oy = py - sin * kick;
        var box = flash >= 0 ? Rectangle.Union(gunBox, muzzleBox[flash]) : gunBox;
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        for (int c = 0; c < 4; c++)
        {
            double lx = (c & 1) == 0 ? box.Left : box.Right, ly = (c & 2) == 0 ? box.Top : box.Bottom;
            if (cos < 0) ly = -ly; // 왼쪽을 겨누면 뒤집어 그린다
            double x = ox + lx * cos - ly * sin, y = oy + lx * sin + ly * cos;
            (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
        }
        return Rectangle.FromLTRB((int)Math.Floor(x0) - 3, (int)Math.Floor(y0) - 3, (int)Math.Ceiling(x1) + 3, (int)Math.Ceiling(y1) + 3);
    }

    // 손잡이가 창 좌표 (px, py) 에 오게 그린다 (도트가 뭉개지지 않게, 그리던 설정은 그대로 돌려놓는다)
    public void Draw(Graphics g, float px, float py, double angle, double kick, int flash)
    {
        var (im, pm, keep) = (g.InterpolationMode, g.PixelOffsetMode, g.TransformElements);
        (g.InterpolationMode, g.PixelOffsetMode) = (InterpolationMode.NearestNeighbor, PixelOffsetMode.Half);
        float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
        g.TranslateTransform(px - cos * (float)kick, py - sin * (float)kick);
        g.RotateTransform((float)(angle * 180 / Math.PI));
        if (cos < 0) g.ScaleTransform(1, -1); // 왼쪽을 겨누면 뒤집어서 총이 거꾸로 서지 않게
        g.DrawImage(gun.Bmp, gunBox.X, gunBox.Y, gunBox.Width, gunBox.Height);
        if (flash >= 0) g.DrawImage(muzzle[flash].Bmp, muzzleBox[flash].X, muzzleBox[flash].Y, muzzleBox[flash].Width, muzzleBox[flash].Height);
        g.TransformElements = keep;
        (g.InterpolationMode, g.PixelOffsetMode) = (im, pm);
    }
}
