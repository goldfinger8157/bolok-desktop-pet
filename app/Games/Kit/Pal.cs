namespace Bolok;

// 게임 공통 색. 붓째로 둔다 (프레임마다 새로 만들지 않게): 색이 필요하면 Pal.Line.Color
// 값은 tools/sprites.py 의 색·Overlay 의 옛 붓들과 같다. 게임마다 따로 정하지 않고 여기 것을 쓴다
static class Pal
{
    public static readonly SolidBrush
        Line = new(Color.FromArgb(92, 58, 40)),        // 테두리·글자 선
        Cream = new(Color.FromArgb(255, 243, 224)),    // 막대 바탕·상자 테두리
        White = new(Color.White),
        Gold = new(Color.FromArgb(250, 185, 40)),      // 채워진 막대
        Red = new(Color.FromArgb(232, 72, 92)),
        Cyan = new(Color.FromArgb(80, 214, 226)),      // 경험치 막대
        Hole = new(Color.FromArgb(14, 16, 22)),        // 뚫린 구멍
        Soot = new(Color.FromArgb(70, 60, 52)),        // 그을린 가장자리·금
        Chip = new(Color.FromArgb(200, 204, 212)),     // 파편
        ChipEdge = new(Color.FromArgb(90, 94, 104)),
        Smoke = new(Color.FromArgb(186, 184, 188)),
        Edge = new(Color.FromArgb(150, 165, 190)),     // 판 모서리 ㄱ자
        Shot = new(Color.FromArgb(255, 220, 90)),      // 내 탄
        Spit = new(Color.FromArgb(200, 110, 230)),     // 적 탄
        Slash = new(Color.FromArgb(110, 170, 240));    // 칼 자국 가장자리

    // 불꽃: 뜨거운 쪽부터 (노랑 → 주황 → 빨강)
    public static readonly SolidBrush[] Flame = { new(Color.FromArgb(255, 236, 120)), new(Color.FromArgb(255, 170, 50)), new(Color.FromArgb(232, 80, 40)) };
}
