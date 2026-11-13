namespace Bolok;

// 게임 공통 효과: 맞은 적 깜빡, 다친 볼록이 깜빡·아파하기, 파편·연기·불꽃 네모, 판 위에 직접 그리는 작은 볼록이
static class Fx
{
    public const double FlashSec = .08;   // 맞은 적이 번쩍이는 시간
    public const double HurtSec = .35;    // 다친 볼록이가 아파하는 그림(hurt)을 보이는 시간

    // 맞은 적: 0.08초 동안 깜빡 (true 면 이번 프레임은 그리지 않는다)
    public static bool Flash(double clock, double hitAt) => clock - hitAt < FlashSec && (int)(clock * 40) % 2 == 0;

    // 다친 뒤 안 맞는 동안: 10Hz 로 깜빡 (true 면 이번 프레임은 그리지 않는다)
    public static bool Blink(double clock, double safeUntil) => clock < safeUntil && (int)(clock * 10) % 2 == 0;

    public static bool Hurting(double clock, double hurtAt) => clock - hurtAt < HurtSec;

    // ── 네모 조각들 (창 좌표, 가운데 기준) ──
    // 파편: 어두운 테두리를 두른 네모
    public static void Chip(Graphics g, int x, int y, int size)
    {
        g.FillRectangle(Pal.ChipEdge, x - size / 2 - 1, y - size / 2 - 1, size + 2, size + 2);
        g.FillRectangle(Pal.Chip, x - size / 2, y - size / 2, size, size);
    }

    // 연기: 남은 시간이 0.5초 아래로 줄면 작아진다
    public static void Puff(Graphics g, int x, int y, int size, double life)
    {
        int s = Math.Max(2, (int)(size * Math.Min(1, life / .5)));
        g.FillRectangle(Pal.Smoke, x - s / 2, y - s / 2, s, s);
    }

    // 불꽃: heat 0 노랑(가장 뜨겁다) · 1 주황 · 2 빨강
    public static void Ember(Graphics g, int x, int y, int size, int heat) =>
        g.FillRectangle(Pal.Flame[Math.Clamp(heat, 0, 2)], x - size / 2, y - size / 2, size, size);

    // ── 작은 볼록이: 본체 창을 숨기고 판 위에 직접 그릴 때. (x, feet) 는 발 가운데 화면 좌표 ──
    static readonly Dictionary<(string, string), string> petRows = new();
    static string PetRow(string stage, string pose) =>
        petRows.TryGetValue((stage, pose), out var name) ? name : petRows[(stage, pose)] = $"{stage}/0/{pose}";

    public static void Pet(Overlay o, Graphics g, Sprites sp, string stage, string pose, double clock, int scale, double x, double feet, bool left)
    {
        var stamps = Stamps.Of(sp);
        string name = PetRow(stage, pose);
        if (!stamps.Has(name)) return;
        var row = stamps.Row(name, scale, left);
        int cell = sp.Cell * scale;
        var at = o.ToClient(x - cell / 2.0, feet - cell);
        row[(int)(clock * 1000 / (pose == "walk" ? 130 : 400)) % row.Length].DrawCell(g, at.X, at.Y, cell);
    }

    // 작은 볼록이가 그려지는 칸 (화면 좌표)
    public static Rectangle PetRect(double x, double feet, int cellPx) => new((int)(x - cellPx / 2.0) - 1, (int)(feet - cellPx) - 1, cellPx + 2, cellPx + 2);
}
