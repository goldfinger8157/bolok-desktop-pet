using System.Drawing.Drawing2D;

namespace Bolok;

// 방석·집 그려 보기 (화면을 띄우지 않는다): --home-look 그림.png cushions|houses [배율]
// 시안과 같은 다섯 줄을 글자 없이 — 빈 것(밝은 바탕) / 햄스터 어린이 / 고양이 어른 / 빈 것(어두운 바탕) / 시바 어른(어두운 바탕).
// 물건은 가로로 10개, 칸마다 정해진 크기 타일(아래 Tile)에 아래 맞춤·가운데 맞춤으로 놓는다. tools/home_art.py --diff 가 같은 타일 크기로 시안과 견준다.
// 시안의 펫: 방석은 잠(sleep), 집은 어린이·시바가 빼꼼(idle)·고양이가 잠. 어린이 = 햄스터 child, 고양이 = cat adult_sweet, 시바 = shiba adult_playful (둘째 칸 모양 0, 첫 프레임)
static class HomeLook
{
    // 타일 (칸 수): 가로, 세로, 물건 아래 여백
    public static (int W, int H, int Bottom) Tile(bool house) => house ? (80, 88, 4) : (64, 64, 4);

    static readonly Color Light = Color.FromArgb(0x93, 0xA0, 0xAC), Dark = Color.FromArgb(0x2A, 0x2E, 0x36);
    // 칸 안의 z (파란 글자): 펫 칸의 x 28~38, y 0~10 — 집 안에서도 문 밖에 그대로 보이게 오림에서 뺀다 (시안도 z 가 문 밖 오른쪽 위에 보인다)
    const int ZX = 28, ZY = 0, ZW = 11, ZH = 10;

    public static int Run(string[] args)
    {
        var art = Directory.Exists("assets") ? HomeArt.FromDir("assets") : HomeArt.Embedded;
        bool house = args[2] == "houses";
        int k = args.Length > 3 ? int.Parse(args[3]) : 3;
        var list = (house ? art.Houses : art.Cushions).ToList();
        var petArt = Directory.Exists("assets") ? PetArt.FromDir("assets") : PetArt.Embedded;
        var (tw, th, bottom) = Tile(house);
        using var bmp = new Bitmap(list.Count * tw * k, 5 * th * k, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        // 펫 줄: 줄 번호, 종, 형태, 방석에서 쓰는 자세, 집에서 쓰는 자세
        var pets = new (int Row, string Species, string Form, string Pose, string HousePose)[]
        {
            (1, "hamster", "child", "sleep", "idle"),
            (2, "cat", "adult_sweet", "sleep", "sleep"),
            (4, "shiba", "adult_playful", "sleep", "idle"),
        };
        for (int row = 0; row < 5; row++) using (var b = new SolidBrush(row < 3 ? Light : Dark)) g.FillRectangle(b, 0, row * th * k, bmp.Width, th * k);
        for (int i = 0; i < list.Count; i++)
        {
            var it = list[i];
            int ix = i * tw * k + (tw - it.W) / 2 * k, iy0 = (th - bottom - it.H) * k; // 물건 왼쪽 위 (줄 안)
            for (int row = 0; row < 5; row++) g.DrawImageUnscaled(art.Body(it, k), ix, row * th * k + iy0);
            foreach (var p in pets)
            {
                var sp = Sprites.Drawn(petArt, p.Species, null);
                string name = $"{p.Form}/0/{(it.House ? p.HousePose : p.Pose)}";
                var one = sp.Get(name, 0, 1);
                var (cx, cy0) = HomeArt.PetCell(it.Seat.X, it.Seat.Y, HomeArt.BodyCenter(one));
                bool top = it.Top is not null && p.Row == 2; // 캣타워 어른은 상자 위 방석에서 잔다
                int dy = it.House && p.HousePose == "sleep" ? it.Sleep : 0;
                int cy = cy0 + dy;
                if (top) (cx, cy) = HomeArt.PetCell(it.Top!.Value.X, it.Top.Value.Y, HomeArt.BodyCenter(one));
                var st = g.Save();
                g.TranslateTransform(ix, p.Row * th * k + iy0);
                using (var clip = art.Clip(it, k, it.House && !top).Clone()) // 가릴 자리 (Region 은 물건 그림 왼쪽 위 기준)
                {
                    if (it.House && !top) clip.Exclude(new Rectangle((cx + ZX) * k, (cy + ZY) * k, ZW * k, ZH * k));
                    g.SetClip(clip, CombineMode.Exclude);
                }
                g.DrawImageUnscaled(sp.Get(name, 0, k), cx * k, cy * k);
                g.Restore(st);
            }
        }
        bmp.Save(args[1]);
        return 0;
    }
}
