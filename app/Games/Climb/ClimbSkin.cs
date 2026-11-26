using Px = Bolok.ClimbArt.Px;

namespace Bolok;

// 클라이밍 테마 (docs/climb.md 「테마」, YELL 2026-11-25): 규칙은 같고 그림만 다르다. 덩굴(콩나무) 말고 넷: 책상 위 문구 · 과자 탑 · 풍선 · 장난감 블록.
// 판 하나에 테마 하나: 판을 열 때 그 테마의 그림만 다 구워 둔다 (발판 모든 종류 × 좌우 × 서 있는 모습, 줍는 것, 띠, 표시). 프레임에서는 찍기만 한다.
// 콤보 조각·카페 그림은 ClimbFxArt.cs 가 테마마다 만든다 (YELL 2026-11-26)
enum ClimbTheme : byte { Vine, Desk, Sweet, Balloon, Block }

abstract class ClimbSkin
{
    public const int Stages = 5;                     // 0 보통 · 1 서 있음(싱싱) · 2 서 있음(조짐) · 3 서 있음(곧) · 4 무너진 뒤
    public const int MaxSink = 6;                    // 볼록이가 발판과 함께 가라앉는 줄 수의 한도 (검사)

    protected static SkinPx Fin(Px g, int ax, int ay, int sink = 0) => SkinKit.Fin(g, ax, ay, sink);
    protected static readonly uint White = SkinKit.White;
    public abstract ClimbTheme Theme { get; }
    public abstract int Variants { get; }
    protected abstract SkinPx StepR(int v, int stage);   // 오른쪽 보기
    protected abstract SkinPx WeakR(int stage);          // 약한 발판: 0 보통 · 1 버티는 중(처짐)
    public SkinPx Berry = null!, Save = null!, SaveHead = null!;
    public Px[] Band = new Px[2];
    public Px[] Pip = new Px[4];                         // 시간 조각 · 결과 상자 아이콘: 싱싱 · 노랑 · 갈색 · 빈 칸

    SkinPx[,,] step = null!;      // [종류][왼쪽·오른쪽][모습]
    SkinPx[,] weak = null!;       // [왼쪽·오른쪽][모습]

    // 발판 그림을 다 구워 둔다 (판을 열 때 한 번)
    void Bake()
    {
        step = new SkinPx[Variants, 2, Stages];
        weak = new SkinPx[2, 2];
        for (int v = 0; v < Variants; v++)
            for (int s = 0; s < Stages; s++)
            {
                var r = StepR(v, s);
                step[v, 1, s] = r;
                step[v, 0, s] = SkinKit.Flip(r, -1);
            }
        for (int s = 0; s < 2; s++)
        {
            var r = WeakR(s);
            weak[1, s] = r;
            weak[0, s] = SkinKit.Flip(r, -1);
        }
    }

    public SkinPx Step(int v, int dir, int stage) => step[v, dir >= 0 ? 1 : 0, stage];
    public SkinPx Weakp(int dir, int stage) => weak[dir >= 0 ? 1 : 0, stage];

    // 자가 검사·그림 모음: 구운 그림 전부
    public IEnumerable<(string, Px)> Gallery()
    {
        for (int v = 0; v < Variants; v++) for (int s = 0; s < Stages; s++) { yield return ($"발판 {v}/{s} 오른쪽", Step(v, 1, s).P); yield return ($"발판 {v}/{s} 왼쪽", Step(v, -1, s).P); }
        for (int s = 0; s < 2; s++) { yield return ($"약한 발판 {s} 오른쪽", Weakp(1, s).P); yield return ($"약한 발판 {s} 왼쪽", Weakp(-1, s).P); }
        foreach (var (n, m) in new[] { ("열매", Berry), ("한 번 살려 주는 것", Save), ("머리 위 그것", SaveHead) }) yield return (n, m.P);
        for (int i = 0; i < Band.Length; i++) yield return ($"띠 {i}", Band[i]);
        for (int i = 0; i < Pip.Length; i++) yield return ($"표시 {i}", Pip[i]);
    }

    // 모든 발판·줍는 것의 가라앉는 줄 수 (검사): 서 있는 발판 모습마다
    public int MaxSinkSeen()
    {
        int m = 0;
        for (int v = 0; v < Variants; v++) for (int s = 0; s < Stages; s++) m = Math.Max(m, Step(v, 1, s).Sink);
        for (int s = 0; s < 2; s++) m = Math.Max(m, Weakp(1, s).Sink);
        return m;
    }

    // 이 테마의 그림을 구워 돌려준다 (덩굴은 null: 덩굴 그림은 ClimbView 가 만든다)
    public static ClimbSkin? Make(ClimbTheme t)
    {
        ClimbSkin? s = t switch
        {
            ClimbTheme.Desk => new DeskSkin(),
            ClimbTheme.Sweet => new SweetSkin(),
            ClimbTheme.Balloon => new BalloonSkin(),
            ClimbTheme.Block => new BlockSkin(),
            _ => null,
        };
        s?.Bake();
        return s;
    }
}
