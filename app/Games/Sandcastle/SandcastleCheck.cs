using System.Diagnostics;
using System.Drawing;

namespace Bolok;

// 모래성: 자가 검사(--selfcheck), 화면 없이 그려 보기(--mode-scene sandcastle), 속도 재기(--mode-bench sandcastle)
sealed partial class SandcastleGame
{
    static void Expect(bool cond, string msg)
    {
        if (!cond) throw new Exception("자가 검사 실패: " + msg);
    }

    // ── 자가 검사 ──
    public static string Check()
    {
        var field = new Rectangle(0, 0, 1920, 1080);
        var castle = new Rectangle(500, 300, 500, 320);

        // 1. 아무것도 안 하면 성은 그대로 (저절로 무너지지 않음)
        {
            var w = new Sandcastle(field, castle, 3, new Random(1), Sandcastle.TurnActor.Player);
            int initialSolids = w.Grid.Cells.Count(c => c == SandGrid.CellType.Solid);
            for (int i = 0; i < 60; i++) w.Update(1 / 60.0);
            int afterSolids = w.Grid.Cells.Count(c => c == SandGrid.CellType.Solid);
            Expect(initialSolids == afterSolids && w.Grid.ActiveFallingCount == 0,
                $"아무것도 안 하면 성은 그대로: 초기 {initialSolids}, 60프레임 후 {afterSolids}, 낙하 모래 {w.Grid.ActiveFallingCount}");
        }

        // 2. 퍼 가면 동그라미 안 모래만 없어짐 / 허공 퍼 가기는 무효·차례 그대로
        {
            var w = new Sandcastle(field, castle, 3, new Random(2), Sandcastle.TurnActor.Player);
            int solidsBefore = w.Grid.Cells.Count(c => c == SandGrid.CellType.Solid);

            // 허공(성 위쪽 먼 곳) 퍼 가기 시도
            bool validAir = w.TryExecuteScoop(field.Left + 50, field.Top + 50, 20, Sandcastle.TurnActor.Player, out bool valid);
            Expect(!valid && !validAir && w.ActiveTurn == Sandcastle.TurnActor.Player && w.Stage == Sandcastle.Phase.PlayerTurn,
                "허공 퍼 가기는 무효이고 차례가 그대로 유지되어야 함");

            // 성 본체 적정 위치 퍼 가기
            double cx = castle.Left + castle.Width * 0.3;
            double cy = castle.Top + castle.Height * 0.6;
            bool validHit = w.TryExecuteScoop(cx, cy, 25, Sandcastle.TurnActor.Player, out valid);
            Expect(valid && validHit && w.LastScooper == Sandcastle.TurnActor.Player,
                "성 위 유효 퍼 가기는 성공해야 함");

            // 안정화까지 시뮬레이션
            for (int i = 0; i < 180 && (w.Stage == Sandcastle.Phase.Scooping || w.Stage == Sandcastle.Phase.Falling); i++)
                w.Update(1 / 60.0);

            int solidsAfter = w.Grid.Cells.Count(c => c == SandGrid.CellType.Solid);
            Expect(solidsAfter < solidsBefore, "퍼낸 후 모래가 감소해야 함");
        }

        // 3. 받침 없는 덩어리는 떨어지고, 한도 안의 처마는 버티고, 넘으면 무너짐
        {
            var grid = new SandGrid(100, 80, 4, new Rectangle(0, 0, 400, 320), new Rectangle(50, 50, 300, 200));
            // 바닥 세팅
            for (int gx = 0; gx < 100; gx++)
            {
                grid.Cells[grid.Index(gx, 70)] = SandGrid.CellType.Ground;
            }

            // 기둥 하나 (x=20)
            for (int gy = 40; gy < 70; gy++)
            {
                grid.Cells[grid.Index(20, gy)] = SandGrid.CellType.Solid;
            }

            // 처마: x=21부터 가로로 뻗음
            // (1) 한도 이내 (MaxOverhang - 2 = 6칸)
            for (int gx = 21; gx <= 20 + 6; gx++)
            {
                grid.Cells[grid.Index(gx, 40)] = SandGrid.CellType.Solid;
            }
            int c1 = grid.CheckStability();
            Expect(c1 == 0, $"한도 안의 처마(6칸 <= {SandGrid.MaxOverhang})는 무너지지 않아야 함: 무너진 수={c1}");

            // (2) 한도 초과 처마 추가 (총 15칸 처마)
            for (int gx = 20 + 7; gx <= 20 + 15; gx++)
            {
                grid.Cells[grid.Index(gx, 40)] = SandGrid.CellType.Solid;
            }
            int c2 = grid.CheckStability();
            Expect(c2 > 0, $"한도를 넘는 처마(15칸 > {SandGrid.MaxOverhang})는 무너져야 함: 무너진 수={c2}");

            // (3) 공중에 완전히 떠 있는 독립 덩어리
            for (int gx = 50; gx <= 55; gx++)
            {
                for (int gy = 20; gy <= 25; gy++)
                {
                    grid.Cells[grid.Index(gx, gy)] = SandGrid.CellType.Solid;
                }
            }
            int c3 = grid.CheckStability();
            Expect(c3 >= 36, "받침 없는 공중 덩어리는 전부 무너져야 함");

            // (4) 양쪽에서 받치는 다리 (Bridge)
            // 좌측 기둥(x=60), 우측 기둥(x=71), 그 사이 10칸 다리 (x=61..70)
            for (int gy = 40; gy < 70; gy++)
            {
                grid.Cells[grid.Index(60, gy)] = SandGrid.CellType.Solid;
                grid.Cells[grid.Index(71, gy)] = SandGrid.CellType.Solid;
            }
            for (int gx = 61; gx <= 70; gx++)
            {
                grid.Cells[grid.Index(gx, 40)] = SandGrid.CellType.Solid;
            }
            int c4 = grid.CheckStability();
            Expect(c4 == 0, $"양쪽에서 받치는 10칸 다리(<= 12칸)는 무너지지 않아야 함: 무너진 수={c4}");
        }

        // 4. 깃발 밑을 무너뜨리면 퍼 간 쪽이 짐
        {
            var w = new Sandcastle(field, castle, 3, new Random(3), Sandcastle.TurnActor.Player);
            // 깃발 바로 밑을 크게 퍼냄
            w.TryExecuteScoop(w.FlagBaseX, w.FlagBaseY + 15, 45, Sandcastle.TurnActor.Player, out _);

            for (int i = 0; i < 300 && !w.Done; i++)
            {
                w.Update(1 / 60.0);
            }

            Expect(w.Stage == Sandcastle.Phase.Lost,
                $"깃발 밑을 무너뜨린 플레이어는 패배해야 함 (현재: {w.Stage})");
            Expect(w.FlagAngle > 0.5, "깃발이 쓰러져 있어야 함");
        }

        // 5. 라이벌은 안전한 수가 있으면 바로 지는 수를 두지 않음
        {
            var w = new Sandcastle(field, castle, 3, new Random(4), Sandcastle.TurnActor.Cpu);
            var (cx, cy, cr) = w.PlanCpuMove(strictSafe: true);

            // 라이벌이 고른 수가 안전한지 검증
            var clone = w.Grid.Clone();
            clone.Scoop(cx, cy, cr);
            clone.SimulateToRest(new Random(99));
            bool safe = w.IsFlagSupported(clone);

            Expect(safe, "라이벌은 안전한 수가 있을 때 안전한 수를 두어야 함");
        }

        // 6. 같은 씨앗 = 같은 결과
        {
            var w1 = new Sandcastle(field, castle, 3, new Random(42), Sandcastle.TurnActor.Player);
            var w2 = new Sandcastle(field, castle, 3, new Random(42), Sandcastle.TurnActor.Player);

            w1.TryExecuteScoop(castle.Left + 100, castle.Top + 100, 25, Sandcastle.TurnActor.Player, out _);
            w2.TryExecuteScoop(castle.Left + 100, castle.Top + 100, 25, Sandcastle.TurnActor.Player, out _);

            for (int i = 0; i < 60; i++)
            {
                w1.Update(1 / 60.0);
                w2.Update(1 / 60.0);
            }

            bool cellsMatch = w1.Grid.Cells.SequenceEqual(w2.Grid.Cells);
            bool flagMatch = Math.Abs(w1.FlagAngle - w2.FlagAngle) < 1e-6;
            Expect(cellsMatch && flagMatch, "같은 씨앗이면 완전히 같은 격자 상태와 깃발 각도를 가져야 함");
        }

        // 7. Stop(true)는 결과 없이 바로 끝나고 그린 것을 지움 / 기록 저장·불러오기 / 쓸 창이 없으면 시작 안 함
        {
            var host = new SceneHost();
            var game = new SandcastleGame(host);

            // 쓸 창이 없으면 시작 안 함
            var fakeWin = new Win(new IntPtr(10), new Rectangle(0, 0, 50, 50), false); // 너무 작은 창
            var (f, picked) = WindowPick.Arena(fakeWin, new[] { fakeWin }, host.Pet.Area, new[] { host.Pet.Area }, 120, 250, 160);
            Expect(picked == null, "크기 기준 미달 창은 Arena로 선택되지 않아야 함");

            // 게임 시작 후 Stop(true)
            var w = new Sandcastle(field, castle, 3, new Random(7));
            game.Open(w, Session.Fake(field), events: false);
            Expect(game.Running, "게임이 실행 중이어야 함");

            game.Stop(true); // 보스 키
            Expect(!game.Running, "Stop(true) 후에는 즉시 Running이 false여야 함");
            Expect(game.Stats().First().Value == L.T("game.sandcastle_none"), "Stop(true)로 끝난 판은 승리로 기록되지 않아야 함");

            // 기록 저장 / 불러오기
            string? state = game.SaveState();
            var game2 = new SandcastleGame(host);
            game2.LoadState(state);
            Expect(game2.SaveState() == state, "SaveState와 LoadState가 일치해야 함");
        }

        // 8. 라이벌 종 ≠ 내 종
        {
            var host = new SceneHost();
            var game = new SandcastleGame(host);
            game.InitRival(new Random(42));
            Expect(!string.IsNullOrEmpty(game.RivalSpecies), "라이벌 종이 설정되어야 함");
            Expect(game.RivalSpecies != PetArt.Base(host.Sprites.Species),
                $"라이벌 종({game.RivalSpecies}) ≠ 내 종({PetArt.Base(host.Sprites.Species)})");
        }

        // 9. 창이 화면 맨 위(y=0)에 붙어 있어도 깃발이 화면 밖으로 잘리지 않음
        {
            var topCastle = new Rectangle(500, 0, 480, 300); // y=0 맨 위 창
            var topField = new Rectangle(0, 0, 1920, 1080);
            var w = new Sandcastle(topField, topCastle, 3, new Random(1));
            double flagTipY = w.FlagBaseY - 21 * w.K;
            Expect(flagTipY >= topField.Top, $"화면 맨 위 창이어도 깃발 꼭대기({flagTipY})가 화면 상단({topField.Top}) 안쪽이어야 함");
        }

        // 10. 디버그 end win|lose 는 차례와 상관없이 깃발 쓰러짐과 게임 종료로 이어짐
        {
            var w = new Sandcastle(field, castle, 3, new Random(2));
            w.ForceEnd(win: false); // lose 강제
            Expect(w.Stage == Sandcastle.Phase.FlagCollapsing, "ForceEnd 호출 즉시 FlagCollapsing 단계로 진입해야 함");
            for (int i = 0; i < 150 && !w.Done; i++) w.Update(1 / 60.0);
            Expect(w.Done && w.Stage == Sandcastle.Phase.Lost, "ForceEnd(false) 후 최종 결과는 Lost여야 함");
        }

        // 11. 퍼내기 후 차례 수 정상 증가 (turns)
        {
            var w = new Sandcastle(field, castle, 3, new Random(10), Sandcastle.TurnActor.Player);
            Expect(w.TurnsCount == 1, "시작 시 turns == 1");

            // 1번째 퍼내기 (Player)
            w.TryExecuteScoop(castle.Left + 100, castle.Bottom - 50, 30, Sandcastle.TurnActor.Player, out _);
            for (int i = 0; i < 90 && w.Stage != Sandcastle.Phase.CpuThinking; i++) w.Update(1 / 60.0);
            Expect(w.TurnsCount == 2 && w.ActiveTurn == Sandcastle.TurnActor.Cpu, $"1회 퍼내기 후 Cpu 차례(turns={w.TurnsCount})");

            // 2번째 퍼내기 (Cpu)
            var (cx, cy, cr) = w.PlanCpuMove();
            w.TryExecuteScoop(cx, cy, cr, Sandcastle.TurnActor.Cpu, out _);
            for (int i = 0; i < 90 && w.Stage != Sandcastle.Phase.PlayerTurn; i++) w.Update(1 / 60.0);
            Expect(w.TurnsCount == 3 && w.ActiveTurn == Sandcastle.TurnActor.Player, $"2회 퍼내기 후 Player 차례(turns={w.TurnsCount})");
        }

        // 12. 차례 지키기: 차례가 아닌 쪽의 퍼내기는 거부되고, sandcastle cpu는 지금 차례인 쪽으로 수행
        {
            var w = new Sandcastle(field, castle, 3, new Random(1), Sandcastle.TurnActor.Player);
            Expect(w.ActiveTurn == Sandcastle.TurnActor.Player, "시작 시 Player 차례");

            // Cpu가 Player 차례에 퍼내려고 시도 -> 거부
            bool ok = w.TryExecuteScoop(castle.Left + 100, castle.Bottom - 50, 30, Sandcastle.TurnActor.Cpu, out bool valid);
            Expect(!ok && !valid, "Player 차례에 Cpu의 퍼내기 시도는 거부되어야 함");
            Expect(w.ActiveTurn == Sandcastle.TurnActor.Player && w.Stage == Sandcastle.Phase.PlayerTurn, "차례가 유지되어야 함");

            // 지금 차례(Player)인 쪽으로 AI 한 수
            var (cx, cy, cr) = w.PlanCpuMove();
            bool playerCpuOk = w.TryExecuteScoop(cx, cy, cr, w.ActiveTurn, out bool playerCpuValid);
            Expect(playerCpuOk && playerCpuValid, "w.ActiveTurn으로 두면 유효하게 실행되어야 함");
        }

        // 13. 끝난 뒤 2.5초 대기 (EndAt 기록 확인)
        {
            var w = new Sandcastle(field, castle, 3, new Random(2));
            w.ForceEnd(win: true);
            for (int i = 0; i < 150 && !w.Done; i++) w.Update(1 / 60.0);
            Expect(w.Done, "게임 종료");
            Expect(w.EndAt > 0, "종료 시점 EndAt이 기록되어야 함");
        }

        // 14. 모듈이 쓰는 표정 이름(nowin, star) 및 자세 이름(idle, happy, dizzy) 유효성 검증
        {
            var host = new SceneHost();
            string[] emotes = { "nowin", "star" };
            foreach (var emo in emotes)
            {
                Expect(host.Sprites.Has("emo/" + emo), $"표정 emo/{emo} 가 host.Sprites 에 존재해야 함");
            }

            string[] poses = { "idle", "happy", "dizzy" };
            foreach (var pose in poses)
            {
                Expect(host.Sprites.Has($"child/1/{pose}"), $"자세 child/1/{pose} 가 host.Sprites 에 존재해야 함");
                Expect(host.Sprites.Has($"adult_grumpy/1/{pose}"), $"자세 adult_grumpy/1/{pose} 가 host.Sprites 에 존재해야 함");
            }
        }

        // 15. 모래가 가라앉는 동안(Falling/Scooping)에는 추가 퍼내기가 거부됨
        {
            var w = new Sandcastle(field, castle, 3, new Random(5), Sandcastle.TurnActor.Player);
            bool firstOk = w.TryExecuteScoop(castle.Left + 100, castle.Bottom - 50, 30, Sandcastle.TurnActor.Player, out bool firstValid);
            Expect(firstOk && firstValid && w.Stage == Sandcastle.Phase.Scooping, "첫 번째 퍼내기는 성공하여 Scooping 상태");

            // Scooping 상태에서 두 번째 퍼내기 시도 -> 거부
            bool doubleScoop1 = w.TryExecuteScoop(castle.Left + 150, castle.Bottom - 50, 30, Sandcastle.TurnActor.Player, out bool validDouble1);
            Expect(!doubleScoop1 && !validDouble1, "삽 퍼내기 연출 중(Scooping)에는 추가 퍼내기가 거부되어야 함");

            // Scooping 완료 후 Falling 단계로 진입
            w.Update(0.4);
            Expect(w.Stage == Sandcastle.Phase.Falling, "현재 Falling 상태");

            // Falling 상태에서 두 번째 퍼내기 시도 -> 거부
            bool doubleScoop2 = w.TryExecuteScoop(castle.Left + 150, castle.Bottom - 50, 30, Sandcastle.TurnActor.Player, out bool validDouble2);
            Expect(!doubleScoop2 && !validDouble2, "모래가 가라앉는 동안(Falling)에는 추가 퍼내기가 거부되어야 함");

            // 라이벌 AI(Cpu)도 마찬가지로 Falling 중 퍼내기 시도 시 거부
            bool doubleScoopCpu = w.TryExecuteScoop(castle.Left + 150, castle.Bottom - 50, 30, Sandcastle.TurnActor.Cpu, out bool validDoubleCpu);
            Expect(!doubleScoopCpu && !validDoubleCpu, "모래가 가라앉는 동안 Cpu의 퍼내기도 거부되어야 함");
        }

        return "모래성: 격자 유지·처마 버팀/무너짐·다리 지지·퍼내기·깃발 쓰러짐·AI 안전 수·결정론·보스 키·라이벌 종 구분·상단 깃발 보정·디버그 종료·차례 지키기·표정 자세 유효성·가라앉는 중 퍼내기 거부 모두 통과";
    }

    // ── 화면 없이 그려 보기 (--mode-scene sandcastle <경로>) ──
    public static void Scene(string path, int k)
    {
        if (GameModes.SceneArg == "check")
        {
            Console.WriteLine(Check());
            return;
        }

        var host = new SceneHost(scale: k);
        var game = new SandcastleGame(host) { showMe = true };

        int fw = Math.Min(1920, 1280 * k / 3);
        int fh = Math.Min(1040, 720 * k / 3);
        var field = new Rectangle(0, 0, fw, fh);
        var castle = new Rectangle((int)(fw * 0.28), (int)(fh * 0.35), (int)(fw * 0.44), (int)(fh * 0.45));

        using var ov = host.Overlay(game);

        // 4개의 샷:
        // 샷 0: 시작 (온전한 모래성, 깃발 직립, 내 차례 빨간 화살표 + 커서)
        // 샷 1: 내 차례 첫 퍼내기 (구멍 뚫림, 흘러내리는 모래알, 삽 연출)
        // 샷 2: 한창 진행 중 (아래 양옆 처마 버팀, 바닥 더미 소복이 쌓임, 라이벌 차례)
        // 샷 3: 지는 순간 (깃발 쓰러짐, 성 전체 무너짐, 패배자 x눈 / 승자 별)
        Sandcastle? play = null;
        int curShot = -1;
        int shotFrames = 0;

        SceneKit.Shots(path, ov, field, 4, shot =>
        {
            if (curShot != shot)
            {
                curShot = shot;
                shotFrames = 0;

                if (shot == 0)
                {
                    play = new Sandcastle(field, castle, k, new Random(10), Sandcastle.TurnActor.Player);
                    game.Open(play, Session.Fake(field, mouse: () => new Point(castle.Left + 120, castle.Top + 80)), events: false);
                }
                else if (shot == 1)
                {
                    // 첫 퍼내기 진행 (삽 위에 모래가 들려 떠오르고 모래알이 떨어짐)
                    play!.TryExecuteScoop(castle.Left + 120, castle.Top + 80, 26 * k / 3.0, Sandcastle.TurnActor.Player, out _);
                }
                else if (shot == 2)
                {
                    // 한창: 시안 3, 4번처럼 아래 양옆이 크게 파여 처마를 이루고 바닥에 더미가 쌓인 모습
                    play = new Sandcastle(field, castle, k, new Random(22), Sandcastle.TurnActor.Cpu);
                    game.Open(play, Session.Fake(field), events: false);

                    // 좌하단, 우하단, 중앙 아래 퍼내기
                    play.TryExecuteScoop(castle.Left + 25, castle.Bottom - 35, 30 * k / 3.0, Sandcastle.TurnActor.Player, out _);
                    for (int i = 0; i < 40; i++) play.Update(1 / 60.0);

                    play.TryExecuteScoop(castle.Right - 35, castle.Bottom - 40, 28 * k / 3.0, Sandcastle.TurnActor.Cpu, out _);
                    for (int i = 0; i < 40; i++) play.Update(1 / 60.0);

                    play.TryExecuteScoop(castle.Left + castle.Width * 0.45, castle.Top + castle.Height * 0.55, 20 * k / 3.0, Sandcastle.TurnActor.Player, out _);
                    for (int i = 0; i < 30; i++) play.Update(1 / 60.0);
                }
                else if (shot == 3)
                {
                    // 지는 순간 (깃발 쓰러짐 및 와르르 붕괴)
                    play = new Sandcastle(field, castle, k, new Random(15), Sandcastle.TurnActor.Player);
                    game.Open(play, Session.Fake(field), events: false);
                    play.TryExecuteScoop(play.FlagBaseX, play.FlagBaseY + 10, 48 * k / 3.0, Sandcastle.TurnActor.Player, out _);
                }
            }

            shotFrames++;
            game.Frame(1 / 60.0);

            return shot switch
            {
                0 => shotFrames >= 10,
                1 => shotFrames >= 15 && play!.Grid.ActiveFallingCount > 0,
                2 => shotFrames >= 20,
                _ => shotFrames >= 45 && play!.Done,
            };
        });
    }

    // ── 속도 재기 (--mode-bench sandcastle) ──
    public static string BenchRun()
    {
        var host = new SceneHost();
        var game = new SandcastleGame(host);

        // 1. 라이벌끼리 여러 판 (승률 및 평균 차례 수)
        int totalPlays = 20;
        int firstTurnWins = 0;
        int totalTurns = 0;
        var rnd = new Random(1234);

        var benchField = new Rectangle(0, 0, 1920, 1080);
        var benchCastle = new Rectangle(500, 300, 480, 300);

        for (int p = 0; p < totalPlays; p++)
        {
            var firstActor = (p % 2 == 0) ? Sandcastle.TurnActor.Player : Sandcastle.TurnActor.Cpu;
            var w = new Sandcastle(benchField, benchCastle, 3, rnd, firstActor);

            while (!w.Done && w.TurnsCount < 40)
            {
                var (cx, cy, cr) = w.PlanCpuMove();
                if (!w.StepTurnSim(rnd, cx, cy, cr))
                {
                    w.StepTurnSim(rnd, w.FlagBaseX, w.FlagBaseY + 10, 35);
                }
            }

            totalTurns += w.TurnsCount;
            var winner = (w.Stage == Sandcastle.Phase.Won) ? Sandcastle.TurnActor.Player : Sandcastle.TurnActor.Cpu;
            if (winner == firstActor) firstTurnWins++;
        }

        double firstWinRate = (double)firstTurnWins / totalPlays * 100.0;
        double avgTurns = (double)totalTurns / totalPlays;

        // 2. 대형 창(1400x900) 프레임 시간 벤치마크 (목표: 평균 4ms 안)
        var bigCastle = new Rectangle(200, 100, 1400, 900);
        var bigGame = new Sandcastle(benchField, bigCastle, 3, new Random(55), Sandcastle.TurnActor.Player);
        game.Open(bigGame, Session.Fake(benchField), events: false);

        // 미리 모래를 파내어 흘러내림과 처마가 활성화된 상태
        bigGame.TryExecuteScoop(bigCastle.Left + 250, bigCastle.Top + 250, 40, Sandcastle.TurnActor.Player, out _);

        using var ov = host.Overlay(game);

        int frameCounter = 0;
        bool BotStep()
        {
            if (++frameCounter > 120 || bigGame.Done) return false;
            if (frameCounter == 40)
            {
                bigGame.TryExecuteScoop(bigCastle.Right - 300, bigCastle.Bottom - 300, 35, Sandcastle.TurnActor.Cpu, out _);
            }
            return true;
        }

        string perf = Bench.Run("sandcastle_1400x900", ov, game, benchField, BotStep, () =>
        {
            bigGame.Update(1 / 60.0);
            game.Sync();
            game.Track();
        }, maxFrames: 120);

        return $"sandcastle: {totalPlays}판 시뮬레이션 (평균 {avgTurns:0.0}차례, 선공 승률 {firstWinRate:0.0}%)\n" +
               $"1400x900 대형 창 성능: {perf}";
    }
}
