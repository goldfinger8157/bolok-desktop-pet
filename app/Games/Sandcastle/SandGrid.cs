using System.Drawing;

namespace Bolok;

// 모래 격자 및 젖은 모래 물리 시뮬레이션
//  - 셀 단위: Solid(뭉친 젖은 모래), Falling(흘러내리는 모래알), Heap(쌓인 모래더미), Ground(파괴되지 않는 바닥)
//  - 처마 한도(MaxOverhang): 아래 받침이 없어도 옆 지지에서 MaxOverhang 칸까지는 버티고, 넘으면 우르르 무너진다.
//  - Falling sand: 아래가 비면 떨어지고, 막히면 좌우로 미끄러져 안식각을 이룬다.
//  - 결정론적: 같은 씨앗이면 매 프레임 완전히 같은 결과.
sealed class SandGrid
{
    public const int MaxOverhang = 6;        // 처마가 버티는 최대 가로 칸 수 (평균 12~20차례 긴장감 유지)
    public const int MinScoopGrains = 12;     // 퍼내기가 유효하다고 인정되는 최소 모래알 수

    public enum CellType : byte
    {
        Empty = 0,
        Solid = 1,      // 뭉친 젖은 모래 (처마 버팀)
        Falling = 2,    // 흘러내리는 중인 모래알
        Heap = 3,       // 흘러내려 바닥/더미에 쌓인 모래알
        Ground = 4,     // 성 바닥/양옆 모래 땅 지지층
    }

    public readonly int Gw, Gh;               // 격자 크기 (칸 수)
    public readonly int CellSize;             // 한 칸의 화면 px 크기
    public readonly Rectangle FieldBounds;    // 화면 좌표 상의 전체 격자 사각형
    public readonly Rectangle CastleBounds;   // 화면 좌표 상의 창(성) 사각형

    public readonly CellType[] Cells;
    public readonly int[] Colors;             // 각 셀의 ARGB 색상
    readonly int[] supportL;                  // 좌측 지지 거리 계산용 버퍼
    readonly int[] supportR;                  // 우측 지지 거리 계산용 버퍼
    readonly (int x, int y)[] queue;          // BFS 큐

    // 시뮬레이션 변화 추적 (Dirty 사각형 갱신용)
    public bool HasChanges { get; private set; }
    public int MinChangeX, MinChangeY, MaxChangeX, MaxChangeY;

    // 움직이고 있는 모래알 수
    public int ActiveFallingCount { get; private set; }

    // 움직이고 있는 모래알의 영역 (물리 연산 범위 최소화)
    public int FallingMinX { get; private set; }
    public int FallingMaxX { get; private set; }
    public int FallingMinY { get; private set; }
    public int FallingMaxY { get; private set; }

    public void ResetFallingBounds()
    {
        FallingMinX = Gw;
        FallingMaxX = -1;
        FallingMinY = Gh;
        FallingMaxY = -1;
    }

    public void ExpandFallingBounds(int gx, int gy)
    {
        if (gx < FallingMinX) FallingMinX = gx;
        if (gx > FallingMaxX) FallingMaxX = gx;
        if (gy < FallingMinY) FallingMinY = gy;
        if (gy > FallingMaxY) FallingMaxY = gy;
    }

    public SandGrid(int gw, int gh, int cellSize, Rectangle fieldBounds, Rectangle castleBounds)
    {
        Gw = gw;
        Gh = gh;
        CellSize = cellSize;
        FieldBounds = fieldBounds;
        CastleBounds = castleBounds;

        Cells = new CellType[gw * gh];
        Colors = new int[gw * gh];
        supportL = new int[gw * gh];
        supportR = new int[gw * gh];
        queue = new (int, int)[gw * gh];

        ResetChangeBounds();
        ResetFallingBounds();
    }

    // 복사본 생성 (AI 시뮬레이션용)
    public SandGrid Clone()
    {
        var copy = new SandGrid(Gw, Gh, CellSize, FieldBounds, CastleBounds);
        Array.Copy(Cells, copy.Cells, Cells.Length);
        Array.Copy(Colors, copy.Colors, Colors.Length);
        copy.ActiveFallingCount = ActiveFallingCount;
        copy.FallingMinX = FallingMinX;
        copy.FallingMaxX = FallingMaxX;
        copy.FallingMinY = FallingMinY;
        copy.FallingMaxY = FallingMaxY;
        return copy;
    }

    public void CopyTo(SandGrid dst)
    {
        Array.Copy(Cells, dst.Cells, Cells.Length);
        Array.Copy(Colors, dst.Colors, Colors.Length);
        dst.ActiveFallingCount = ActiveFallingCount;
        dst.FallingMinX = FallingMinX;
        dst.FallingMaxX = FallingMaxX;
        dst.FallingMinY = FallingMinY;
        dst.FallingMaxY = FallingMaxY;
        dst.ResetChangeBounds();
    }

    public void ResetChangeBounds()
    {
        HasChanges = false;
        MinChangeX = Gw;
        MinChangeY = Gh;
        MaxChangeX = -1;
        MaxChangeY = -1;
    }

    public void MarkChanged(int gx, int gy)
    {
        HasChanges = true;
        if (gx < MinChangeX) MinChangeX = gx;
        if (gy < MinChangeY) MinChangeY = gy;
        if (gx > MaxChangeX) MaxChangeX = gx;
        if (gy > MaxChangeY) MaxChangeY = gy;
    }

    public Rectangle GetChangeRectScreen()
    {
        if (!HasChanges) return Rectangle.Empty;
        int sx = FieldBounds.Left + MinChangeX * CellSize;
        int sy = FieldBounds.Top + MinChangeY * CellSize;
        int sw = (MaxChangeX - MinChangeX + 1) * CellSize;
        int sh = (MaxChangeY - MinChangeY + 1) * CellSize;
        return new Rectangle(sx, sy, sw, sh);
    }

    public int Index(int gx, int gy) => gy * Gw + gx;
    public bool InBounds(int gx, int gy) => gx >= 0 && gx < Gw && gy >= 0 && gy < Gh;

    // ── 창 모양의 모래성 초기화 (시안 2번 c2: 모래로 빚은 창) ──
    public void BuildCastle(Random rnd)
    {
        int SandBase = Color.FromArgb(238, 208, 140).ToArgb();
        int SandDk = Color.FromArgb(196, 160, 96).ToArgb();
        int SandLt = Color.FromArgb(252, 232, 176).ToArgb();
        int Shadow = Color.FromArgb(120, 90, 50).ToArgb();
        int DeepShadow = Color.FromArgb(100, 75, 40).ToArgb();
        int HeaderIcon = Color.FromArgb(250, 220, 100).ToArgb();

        int CastleLeftG = (CastleBounds.Left - FieldBounds.Left) / CellSize;
        int CastleTopG = (CastleBounds.Top - FieldBounds.Top) / CellSize;
        int CastleRightG = CastleLeftG + CastleBounds.Width / CellSize;
        int CastleBottomG = CastleTopG + CastleBounds.Height / CellSize;
        int CastleW = CastleRightG - CastleLeftG;
        int CastleH = CastleBottomG - CastleTopG;

        int GroundY = CastleBottomG;

        // 1. 바닥 모래 땅 (Ground): 성 아래와 양옆 얇은 모래 땅 (시안 2, 3번)
        int groundThickness = Math.Max(2, 6 / CellSize);
        for (int gy = GroundY; gy < Math.Min(Gh, GroundY + groundThickness); gy++)
        {
            for (int gx = 0; gx < Gw; gx++)
            {
                int idx = Index(gx, gy);
                Cells[idx] = CellType.Ground;
                int jitter = rnd.Next(-5, 6);
                Colors[idx] = JitterColor(SandBase, jitter);
                MarkChanged(gx, gy);
            }
        }

        // 2. 창 모양 모래성 (Solid) - 시안 2번(c2) 창 부품 모래 조각
        int TitleHeightG = Math.Max(3, 8 * 3 / CellSize); // 제목줄 높이

        // 내부 콘텐츠 분할 (좌측 사이드바/트리, 우측 상단 표/시트, 우측 하단 텍스트/차트)
        int sideBarW = CastleW / 3;

        for (int gy = CastleTopG; gy < CastleBottomG; gy++)
        {
            for (int gx = CastleLeftG; gx < CastleRightG; gx++)
            {
                int idx = Index(gx, gy);
                Cells[idx] = CellType.Solid;

                int lx = gx - CastleLeftG;
                int ly = gy - CastleTopG;

                int col;

                // A. 창 외곽 테두리 및 그림자 (오른쪽/아래는 깊은 그림자로 입체감)
                if (gx == CastleLeftG || gy == CastleTopG)
                {
                    col = SandDk;
                }
                else if (gx >= CastleRightG - 2 || gy >= CastleBottomG - 2)
                {
                    col = (gx == CastleRightG - 1 || gy == CastleBottomG - 1) ? DeepShadow : Shadow;
                }
                // B. 제목줄 띠 (살짝 도드라진 명암)
                else if (ly < TitleHeightG)
                {
                    // 제목줄 아랫변 그림자 홈
                    if (ly == TitleHeightG - 1)
                    {
                        col = Shadow;
                    }
                    // 왼쪽 작은 아이콘
                    else if (lx >= 2 && lx <= 3 && ly >= 1 && ly <= TitleHeightG - 2)
                    {
                        col = HeaderIcon;
                    }
                    // 제목줄 텍스트 바
                    else if (lx >= 6 && lx <= 18 && ly == TitleHeightG / 2)
                    {
                        col = SandLt;
                    }
                    // 오른쪽 위 닫기/최대화/최소화 단추 셋 (네모 셋)
                    else if (lx >= CastleW - 12 && lx <= CastleW - 3 && ly >= 1 && ly <= TitleHeightG - 2)
                    {
                        int btnX = (lx - (CastleW - 12));
                        col = (btnX % 3 != 0) ? Shadow : SandDk;
                    }
                    else
                    {
                        col = SandDk; // 제목줄 바탕
                    }
                }
                // C. 창 내용 (시안 2번: 표, 텍스트 줄 덩어리, 그림 박스 등이 섞인 무늬)
                else
                {
                    int cx = lx;
                    int cy = ly - TitleHeightG;
                    int ch = CastleH - TitleHeightG;

                    // 좌우 분할선 (사이드바 경계)
                    if (cx == sideBarW)
                    {
                        col = Shadow;
                    }
                    // 좌측 영역: 탐색기/사이드바 (폴더 노란 점 + 짧은 줄)
                    else if (cx < sideBarW)
                    {
                        int row = (cy - 2) / 4;
                        int inRow = (cy - 2) % 4;
                        if (row >= 0 && inRow == 0 && (cx >= 4 && cx <= 5))
                            col = HeaderIcon; // 작은 폴더 점
                        else if (row >= 0 && (inRow == 0 || inRow == 1) && (cx >= 7 && cx <= Math.Min(sideBarW - 3, 10 + (row * 7) % (sideBarW - 10))))
                            col = SandDk; // 짧은 글줄
                        else
                            col = SandBase;
                    }
                    // 우측 상단: 표 / 시트 (격자 칸과 막대)
                    else if (cy < ch / 2)
                    {
                        int tableY = cy - 2;
                        int tableX = cx - sideBarW - 2;
                        if (tableY == 0 || tableY % 5 == 0) // 행 구분선
                            col = SandDk;
                        else if (tableX > 0 && tableX % 11 == 0) // 열 구분선
                            col = SandDk;
                        else if (tableY % 5 >= 2 && tableY % 5 <= 3 && tableX % 11 >= 2 && tableX % 11 <= 8)
                            col = ((tableX / 11) % 2 == 0) ? SandLt : SandDk; // 셀 안의 데이터 블록
                        else
                            col = SandBase;
                    }
                    // 우측 하단: 차트 / 텍스트 단락 (길이가 다른 텍스트 덩어리와 막대)
                    else
                    {
                        int textY = cy - (ch / 2) - 2;
                        int textX = cx - sideBarW - 3;
                        int line = textY / 4;
                        int inLine = textY % 4;

                        // 작은 차트 박스
                        if (textX >= CastleW - sideBarW - 18 && textX <= CastleW - sideBarW - 4 && line >= 1 && line <= 3)
                        {
                            bool isChartEdge = (textX == CastleW - sideBarW - 18 || textX == CastleW - sideBarW - 4 || line == 1 || line == 3);
                            col = isChartEdge ? Shadow : SandLt;
                        }
                        // 글줄 덩어리 (제각각인 길이)
                        else if (line >= 0 && (inLine == 0 || inLine == 1))
                        {
                            int lineLen = (CastleW - sideBarW - 24) - ((line * 13) % 20);
                            if (textX >= 3 && textX <= Math.Max(8, lineLen))
                                col = SandDk;
                            else
                                col = SandBase;
                        }
                        else
                        {
                            col = SandBase;
                        }
                    }
                }

                // 모래알 지터(노이즈) 적용
                int jit = rnd.Next(-6, 7);
                Colors[idx] = JitterColor(col, jit);
                MarkChanged(gx, gy);
            }
        }
    }

    static int JitterColor(int argb, int delta)
    {
        int a = (argb >> 24) & 0xFF;
        int r = Math.Clamp(((argb >> 16) & 0xFF) + delta, 0, 255);
        int g = Math.Clamp(((argb >> 8) & 0xFF) + delta, 0, 255);
        int b = Math.Clamp((argb & 0xFF) + delta, 0, 255);
        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    // ── 특정 원 영역 퍼내기 (Scoop) ──
    // 화면 좌표 (cx, cy), 반지름 radiusPx
    public int Scoop(double cx, double cy, double radiusPx, bool checkStability = true)
    {
        int centerGx = (int)Math.Round((cx - FieldBounds.Left) / CellSize);
        int centerGy = (int)Math.Round((cy - FieldBounds.Top) / CellSize);
        int radG = Math.Max(1, (int)Math.Ceiling(radiusPx / CellSize));

        int scoopedGrains = 0;

        for (int dy = -radG; dy <= radG; dy++)
        {
            int gy = centerGy + dy;
            if (gy < 0 || gy >= Gh) continue;

            for (int dx = -radG; dx <= radG; dx++)
            {
                int gx = centerGx + dx;
                if (gx < 0 || gx >= Gw) continue;

                if (dx * dx + dy * dy <= radG * radG)
                {
                    int idx = Index(gx, gy);
                    if (Cells[idx] == CellType.Solid || Cells[idx] == CellType.Heap || Cells[idx] == CellType.Falling)
                    {
                        Cells[idx] = CellType.Empty;
                        Colors[idx] = 0;
                        scoopedGrains++;
                        MarkChanged(gx, gy);
                    }
                }
            }
        }

        if (scoopedGrains >= MinScoopGrains && checkStability)
        {
            // 퍼냈으므로 안정성 검사 및 무너짐 유발 (퍼낸 열 범위 근처만 고속 검사)
            CheckStability(centerGx - radG, centerGx + radG);
        }

        return scoopedGrains;
    }

    // ── 원 영역 내 단단한 모래알 수 세기 (고속 AI 후보 평가용) ──
    public int CountSolidGrainsInCircle(double cx, double cy, double radiusPx)
    {
        int centerGx = (int)Math.Round((cx - FieldBounds.Left) / CellSize);
        int centerGy = (int)Math.Round((cy - FieldBounds.Top) / CellSize);
        int radG = Math.Max(1, (int)Math.Ceiling(radiusPx / CellSize));

        int solidGrains = 0;

        for (int dy = -radG; dy <= radG; dy++)
        {
            int gy = centerGy + dy;
            if (gy < 0 || gy >= Gh) continue;

            int rowOffset = gy * Gw;
            for (int dx = -radG; dx <= radG; dx++)
            {
                int gx = centerGx + dx;
                if (gx < 0 || gx >= Gw) continue;

                if (dx * dx + dy * dy <= radG * radG)
                {
                    int idx = rowOffset + gx;
                    if (Cells[idx] == CellType.Solid || Cells[idx] == CellType.Heap)
                    {
                        solidGrains++;
                    }
                }
            }
        }

        return solidGrains;
    }

    // ── 처마 및 지지 안정성 검사 (Support & Collapse) ──
    // 바닥(Ground) 및 수직 지지에서 연결 거리를 계산.
    // - 한쪽만 붙은 처마(Overhang): 처마 한도(MaxOverhang)까지 버팀
    // - 양쪽에서 받치는 다리(Bridge): 처마 한도의 두 배(MaxOverhang * 2)까지 버팀
    // 한도를 초과하거나 고립된 Solid 덩어리는 Falling 으로 전환!
    // 범위가 지정되면 바뀐 열 범위(dirtyMinGx..dirtyMaxGx)만 부분 재계산하여 초고속 처리.
    public int CheckStability(int? minGx = null, int? maxGx = null)
    {
        int maxBridge = MaxOverhang * 2; // 양쪽에서 받치면 처마 한도의 2배 (12칸)

        int dirtyMinGx, dirtyMaxGx;
        if (minGx.HasValue && maxGx.HasValue)
        {
            dirtyMinGx = Math.Max(0, minGx.Value - maxBridge - 1);
            dirtyMaxGx = Math.Min(Gw - 1, maxGx.Value + maxBridge + 1);
        }
        else
        {
            dirtyMinGx = 0;
            dirtyMaxGx = Gw - 1;
        }

        // dirty 열 범위의 support 버퍼만 재설정
        for (int gy = 0; gy < Gh; gy++)
        {
            int rowOffset = gy * Gw;
            int count = dirtyMaxGx - dirtyMinGx + 1;
            Array.Fill(supportL, -1, rowOffset + dirtyMinGx, count);
            Array.Fill(supportR, -1, rowOffset + dirtyMinGx, count);
        }

        // 1. 수직 지지 기둥(Pillars) 탐색: Ground 또는 Ground에서 바로 위로 이어진 Solid 셀들 (거리 0)
        // 캐시 친화적 행 우선 순회 (아래쪽 행부터 위쪽 행으로)
        for (int gy = Gh - 1; gy >= 0; gy--)
        {
            int rowOffset = gy * Gw;
            int belowOffset = (gy + 1) * Gw;
            for (int gx = dirtyMinGx; gx <= dirtyMaxGx; gx++)
            {
                int idx = rowOffset + gx;
                if (Cells[idx] == CellType.Ground)
                {
                    supportL[idx] = 0;
                    supportR[idx] = 0;
                }
                else if (Cells[idx] == CellType.Solid)
                {
                    if (gy + 1 < Gh && (Cells[belowOffset + gx] == CellType.Ground || supportL[belowOffset + gx] == 0))
                    {
                        supportL[idx] = 0;
                        supportR[idx] = 0;
                    }
                }
            }
        }

        // 2. 좌측 기둥의 오른쪽 경계에서 우측으로 수평/상하 지지 전파 (supportL)
        int qHead = 0, qTail = 0;
        for (int gy = 0; gy < Gh; gy++)
        {
            int rowOffset = gy * Gw;
            int xStart = Math.Max(0, dirtyMinGx - 1);
            int xEnd = Math.Min(Gw - 2, dirtyMaxGx);
            for (int gx = xStart; gx <= xEnd; gx++)
            {
                int idx = rowOffset + gx;
                if (Cells[idx] == CellType.Solid && supportL[idx] == 0)
                {
                    int rIdx = idx + 1;
                    if (Cells[rIdx] == CellType.Solid && supportL[rIdx] == -1)
                    {
                        queue[qTail++] = (gx, gy);
                    }
                }
            }
        }

        while (qHead < qTail)
        {
            var (gx, gy) = queue[qHead++];
            int d = supportL[Index(gx, gy)];
            if (d >= maxBridge) continue;

            // 우측으로 전파 (거리 + 1)
            if (gx + 1 < Gw)
            {
                int nIdx = Index(gx + 1, gy);
                if (Cells[nIdx] == CellType.Solid && (supportL[nIdx] == -1 || supportL[nIdx] > d + 1))
                {
                    supportL[nIdx] = d + 1;
                    queue[qTail++] = (gx + 1, gy);
                }
            }
            // 위로 전파 (거리 + 1)
            if (gy > 0)
            {
                int nIdx = Index(gx, gy - 1);
                if (Cells[nIdx] == CellType.Solid && (supportL[nIdx] == -1 || supportL[nIdx] > d + 1))
                {
                    supportL[nIdx] = d + 1;
                    queue[qTail++] = (gx, gy - 1);
                }
            }
            // 아래로 전파 (거리 + 1)
            if (gy + 1 < Gh)
            {
                int nIdx = Index(gx, gy + 1);
                if (Cells[nIdx] == CellType.Solid && (supportL[nIdx] == -1 || supportL[nIdx] > d + 1))
                {
                    supportL[nIdx] = d + 1;
                    queue[qTail++] = (gx, gy + 1);
                }
            }
        }

        // 3. 우측 기둥의 왼쪽 경계에서 좌측으로 수평/상하 지지 전파 (supportR)
        qHead = 0; qTail = 0;
        for (int gy = 0; gy < Gh; gy++)
        {
            int rowOffset = gy * Gw;
            int xStart = Math.Max(1, dirtyMinGx);
            int xEnd = Math.Min(Gw - 1, dirtyMaxGx + 1);
            for (int gx = xStart; gx <= xEnd; gx++)
            {
                int idx = rowOffset + gx;
                if (Cells[idx] == CellType.Solid && supportR[idx] == 0)
                {
                    int lIdx = idx - 1;
                    if (Cells[lIdx] == CellType.Solid && supportR[lIdx] == -1)
                    {
                        queue[qTail++] = (gx, gy);
                    }
                }
            }
        }

        while (qHead < qTail)
        {
            var (gx, gy) = queue[qHead++];
            int d = supportR[Index(gx, gy)];
            if (d >= maxBridge) continue;

            // 좌측으로 전파 (거리 + 1)
            if (gx > 0)
            {
                int nIdx = Index(gx - 1, gy);
                if (Cells[nIdx] == CellType.Solid && (supportR[nIdx] == -1 || supportR[nIdx] > d + 1))
                {
                    supportR[nIdx] = d + 1;
                    queue[qTail++] = (gx - 1, gy);
                }
            }
            // 위로 전파 (거리 + 1)
            if (gy > 0)
            {
                int nIdx = Index(gx, gy - 1);
                if (Cells[nIdx] == CellType.Solid && (supportR[nIdx] == -1 || supportR[nIdx] > d + 1))
                {
                    supportR[nIdx] = d + 1;
                    queue[qTail++] = (gx, gy - 1);
                }
            }
            // 아래로 전파 (거리 + 1)
            if (gy + 1 < Gh)
            {
                int nIdx = Index(gx, gy + 1);
                if (Cells[nIdx] == CellType.Solid && (supportR[nIdx] == -1 || supportR[nIdx] > d + 1))
                {
                    supportR[nIdx] = d + 1;
                    queue[qTail++] = (gx, gy + 1);
                }
            }
        }

        // 4. 지지 판정: 다리(양쪽 지지)는 maxBridge 이내, 외팔보 처마(한쪽 지지)는 MaxOverhang 이내
        int collapsedCount = 0;
        for (int gy = 0; gy < Gh; gy++)
        {
            int rowOffset = gy * Gw;
            for (int gx = dirtyMinGx; gx <= dirtyMaxGx; gx++)
            {
                int idx = rowOffset + gx;
                if (Cells[idx] != CellType.Solid) continue;

                int dl = supportL[idx];
                int dr = supportR[idx];
                if (dl == 0 || dr == 0) continue; // 수직 기둥 본체는 100% 안전

                bool safe = false;
                if (dl != -1 && dr != -1)
                {
                    // 양쪽에서 받치는 다리 (Bridge)
                    if (dl + dr <= maxBridge + 1)
                    {
                        safe = true;
                    }
                }
                else if (dl != -1)
                {
                    // 왼쪽에서만 받치는 외팔보 처마 (Overhang)
                    if (dl <= MaxOverhang) safe = true;
                }
                else if (dr != -1)
                {
                    // 오른쪽에서만 받치는 외팔보 처마 (Overhang)
                    if (dr <= MaxOverhang) safe = true;
                }

                if (!safe)
                {
                    Cells[idx] = CellType.Falling;
                    collapsedCount++;
                    MarkChanged(gx, gy);
                    ExpandFallingBounds(gx, gy);
                }
            }
        }

        ActiveFallingCount += collapsedCount;
        return collapsedCount;
    }

    // ── Falling Sand 물리 1스텝 ──
    // 아래가 비면 떨어지고, 막히면 좌하/우하로 미끄러진다 (안식각 유지).
    public int StepFalling(Random rnd)
    {
        if (ActiveFallingCount == 0) return 0;
        int moved = 0;
        int active = 0;

        int yStart = Math.Min(Gh - 2, FallingMaxY);
        int yEnd = Math.Max(0, FallingMinY);
        int xMin = Math.Max(0, FallingMinX - 1);
        int xMax = Math.Min(Gw - 1, FallingMaxX + 1);

        int nextMinX = Gw, nextMaxX = -1, nextMinY = Gh, nextMaxY = -1;

        // 아래쪽부터 위쪽으로 스캔 (낙하 충돌 방지)
        for (int gy = yStart; gy >= yEnd; gy--)
        {
            bool leftFirst = rnd.Next(2) == 0;
            int xStart = leftFirst ? xMin : xMax;
            int xStop = leftFirst ? xMax + 1 : xMin - 1;
            int xStep = leftFirst ? 1 : -1;

            int rowOffset = gy * Gw;
            int belowOffset = (gy + 1) * Gw;

            for (int gx = xStart; gx != xStop; gx += xStep)
            {
                int idx = rowOffset + gx;
                if (Cells[idx] != CellType.Falling) continue;

                int belowIdx = belowOffset + gx;

                // 1. 바로 아래가 비어있는 경우 -> 직하강
                if (Cells[belowIdx] == CellType.Empty)
                {
                    Cells[belowIdx] = CellType.Falling;
                    Colors[belowIdx] = Colors[idx];
                    Cells[idx] = CellType.Empty;
                    Colors[idx] = 0;
                    MarkChanged(gx, gy);
                    MarkChanged(gx, gy + 1);
                    if (gx < nextMinX) nextMinX = gx;
                    if (gx > nextMaxX) nextMaxX = gx;
                    if (gy + 1 < nextMinY) nextMinY = gy + 1;
                    if (gy + 1 > nextMaxY) nextMaxY = gy + 1;
                    moved++;
                    active++;
                    continue;
                }

                // 2. 바로 아래가 막힌 경우 -> 좌하 또는 우하로 미끄러짐
                int leftBelow = (gx > 0) ? belowOffset + gx - 1 : -1;
                int rightBelow = (gx + 1 < Gw) ? belowOffset + gx + 1 : -1;

                bool canLeft = leftBelow >= 0 && Cells[leftBelow] == CellType.Empty;
                bool canRight = rightBelow >= 0 && Cells[rightBelow] == CellType.Empty;

                int targetIdx = -1;
                int targetX = gx;

                if (canLeft && canRight)
                {
                    if (rnd.Next(2) == 0) { targetIdx = leftBelow; targetX = gx - 1; }
                    else { targetIdx = rightBelow; targetX = gx + 1; }
                }
                else if (canLeft) { targetIdx = leftBelow; targetX = gx - 1; }
                else if (canRight) { targetIdx = rightBelow; targetX = gx + 1; }

                if (targetIdx >= 0)
                {
                    Cells[targetIdx] = CellType.Falling;
                    Colors[targetIdx] = Colors[idx];
                    Cells[idx] = CellType.Empty;
                    Colors[idx] = 0;
                    MarkChanged(gx, gy);
                    MarkChanged(targetX, gy + 1);
                    if (targetX < nextMinX) nextMinX = targetX;
                    if (targetX > nextMaxX) nextMaxX = targetX;
                    if (gy + 1 < nextMinY) nextMinY = gy + 1;
                    if (gy + 1 > nextMaxY) nextMaxY = gy + 1;
                    moved++;
                    active++;
                }
                else
                {
                    // 더 이상 떨어질 곳이 없음 -> 바닥/더미에 안착 (Heap)
                    Cells[idx] = CellType.Heap;
                    MarkChanged(gx, gy);
                }
            }
        }

        ActiveFallingCount = active;
        if (active > 0)
        {
            FallingMinX = nextMinX;
            FallingMaxX = nextMaxX;
            FallingMinY = nextMinY;
            FallingMaxY = nextMaxY;
        }
        else
        {
            ResetFallingBounds();
        }
        return moved;
    }

    public void SettleAllFalling()
    {
        for (int i = 0; i < Cells.Length; i++)
        {
            if (Cells[i] == CellType.Falling)
            {
                Cells[i] = CellType.Heap;
                MarkChanged(i % Gw, i / Gw);
            }
        }
        ActiveFallingCount = 0;
        ResetFallingBounds();
    }

    // ── 모든 남은 모래를 와르르 무너뜨리기 (패배 시 연출) ──
    public void CollapseAll()
    {
        for (int gy = 0; gy < Gh; gy++)
        {
            for (int gx = 0; gx < Gw; gx++)
            {
                int idx = Index(gx, gy);
                if (Cells[idx] == CellType.Solid || Cells[idx] == CellType.Heap)
                {
                    Cells[idx] = CellType.Falling;
                    ActiveFallingCount++;
                    MarkChanged(gx, gy);
                    ExpandFallingBounds(gx, gy);
                }
            }
        }
    }

    // ── 모래가 완전히 안정화될 때까지 고속 시뮬레이션 (AI 후보 평가용) ──
    public void SimulateToRest(Random rnd, int maxSteps = 300)
    {
        for (int i = 0; i < maxSteps; i++)
        {
            if (ActiveFallingCount == 0)
            {
                int c = CheckStability();
                if (c == 0 && ActiveFallingCount == 0) break;
            }
            int moved = StepFalling(rnd);
            if (moved == 0 && ActiveFallingCount == 0) break;
        }
    }
}
