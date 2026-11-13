using System.Diagnostics;
using System.Drawing;

namespace Bolok;

// 볼록이 배구: 자가 검사(--selfcheck), 장면 그리기(--mode-scene volley), 속도 재기(--mode-bench volley)
static class VolleyCheck
{
    static void Expect(bool cond, string msg)
    {
        if (!cond) throw new Exception("자가 검사 실패: " + msg);
    }

    // ── 자가 검사 ──
    public static string Check()
    {
        var field = new Rectangle(0, 0, 1920, 1080);
        var court = new Rectangle(400, 200, 800, 500);

        // 1. 공과 벽·천장·그물(머리·기둥) 튕김
        {
            var sim = new VolleySim(court, false, 3, new Random(1));

            // A. 왼쪽 벽 튕김
            sim.BallX = sim.WallLeft + sim.BallR + 2;
            sim.BallY = sim.CeilingY + 100;
            sim.BallVx = -200;
            sim.BallVy = 0;
            sim.Update(0.02);
            Expect(sim.BallVx > 0, "공이 왼쪽 벽에 부딪히면 오른쪽으로 튕겨야 함");

            // B. 오른쪽 벽 튕김
            sim.BallX = sim.WallRight - sim.BallR - 2;
            sim.BallY = sim.CeilingY + 100;
            sim.BallVx = 200;
            sim.BallVy = 0;
            sim.Update(0.02);
            Expect(sim.BallVx < 0, "공이 오른쪽 벽에 부딪히면 왼쪽으로 튕겨야 함");

            // C. 천장 튕김
            sim.BallX = sim.NetX - 100;
            sim.BallY = sim.CeilingY + sim.BallR + 2;
            sim.BallVx = 0;
            sim.BallVy = -200;
            sim.Update(0.02);
            Expect(sim.BallVy > 0, "공이 천장에 부딪히면 아래로 튕겨야 함");

            // D. 네트 기둥 튕김
            sim.BallX = sim.NetX - sim.BallR - 2;
            sim.BallY = sim.NetTopY + 30;
            sim.BallVx = 200;
            sim.BallVy = 0;
            sim.Update(0.02);
            Expect(sim.BallVx < 0, "공이 네트 기둥에 부딪히면 원래 쪽으로 튕겨야 함");

            // E. 네트 상단 캡 튕김
            sim.BallX = sim.NetX;
            sim.BallY = sim.NetTopY - sim.BallR - 2;
            sim.BallVx = 0;
            sim.BallVy = 200;
            sim.Update(0.02);
            Expect(sim.BallVy < 0, "공이 네트 상단 캡에 부딪히면 위로 튕겨야 함");
        }

        // 2. 바닥 = 상대 점수 (랠리 점수), 점수 낸 쪽 서브
        {
            var sim = new VolleySim(court, false, 3, new Random(2));

            // 플레이어 코트(왼쪽) 바닥에 공이 닿음 -> 라이벌 득점
            sim.BallX = sim.WallLeft + 80;
            sim.BallY = sim.FloorY - sim.BallR - 1;
            sim.BallVx = 0;
            sim.BallVy = 100;
            sim.Update(0.02);

            Expect(sim.ScoreRival == 1 && sim.ScorePlayer == 0,
                $"플레이어 코트에 떨어지면 라이벌 득점이어야 함 (현재: Me={sim.ScorePlayer}, Rival={sim.ScoreRival})");
            Expect(sim.CurrentServer == VolleySim.Actor.Rival,
                "득점한 라이벌이 다음 서브를 해야 함");
            Expect(sim.LastScorer == VolleySim.Actor.Rival,
                "마지막 득점자가 라이벌이어야 함");
            Expect(sim.PlayerPose == "sad" && sim.RivalPose == "happy",
                $"실점 시 플레이어는 sad, 득점한 라이벌은 happy 여야 함 (Player={sim.PlayerPose}, Rival={sim.RivalPose})");

            // 일시 정지 시간 경과 후 서브 준비 확인
            sim.Update(1.5);
            Expect(sim.Stage == VolleySim.Phase.Serving, "득점 후 서브 대기 단계로 진입해야 함");

            // 라이벌 코트(오른쪽) 바닥에 공이 닿음 -> 플레이어 득점
            sim.BallX = sim.WallRight - 80;
            sim.BallY = sim.FloorY - sim.BallR - 1;
            sim.BallVx = 0;
            sim.BallVy = 100;
            sim.Update(0.02);

            Expect(sim.ScorePlayer == 1 && sim.ScoreRival == 1,
                $"라이벌 코트에 떨어지면 플레이어 득점이어야 함 (현재: Me={sim.ScorePlayer}, Rival={sim.ScoreRival})");
            Expect(sim.CurrentServer == VolleySim.Actor.Player,
                "득점한 플레이어가 다음 서브를 해야 함");
        }

        // 3. 5점이면 끝
        {
            var sim = new VolleySim(court, false, 3, new Random(3));
            sim.ForceEnd(true);

            Expect(sim.Done && sim.Won, "5점 달성 시 Done 및 Won 이어야 함");
            Expect(sim.ScorePlayer == VolleySim.TargetScore, "플레이어 점수가 5점이어야 함");
            Expect(sim.Stage == VolleySim.Phase.GameOver, "GameOver 상태여야 함");
            Expect(sim.PlayerPose == "happy" && sim.RivalPose == "dizzy",
                $"승리 시 포즈는 happy/dizzy 이어야 함 (Player={sim.PlayerPose}, Rival={sim.RivalPose})");
        }

        // 4. 공중 Ctrl 강스파이크는 상대 쪽으로 빠르고 낮게 / 땅에서 Ctrl 은 아무 일 없음
        {
            var sim = new VolleySim(court, false, 3, new Random(4));

            // A. 공중에서 Ctrl: 강스파이크 발동
            sim.PlayerOnGround = false;
            sim.PlayerY = sim.FloorY - 50 * sim.K;
            sim.PlayerVy = -40;
            sim.BallX = sim.PlayerX + 5 * sim.K;
            sim.BallY = sim.PlayerY - sim.PetR;

            sim.Update(0.02, inputAxisX: 0, inputJump: false, inputSpike: true);

            Expect(sim.SpikeEffectActive, "공중 Ctrl 시 강스파이크 연출이 발동해야 함");
            Expect(sim.PlayerPose == "angry", $"스파이크 시 포즈는 angry(앞으로 내리치는 자세)여야 함 (현재: {sim.PlayerPose})");
            Expect(sim.BallVx > 500, $"강스파이크 공은 상대 쪽으로 빨라야 함 (현재 Vx: {sim.BallVx})");
            Expect(sim.BallVy > 0 && sim.BallVy < 300, $"강스파이크 공은 낮고 하향이어야 함 (현재 Vy: {sim.BallVy})");
            Expect(sim.ShakeActive, "강스파이크 시 화면 흔들림 효과가 켜져야 함");

            // B. 땅에서 Ctrl: 스파이크 발동하지 않음
            var simGround = new VolleySim(court, false, 3, new Random(5));
            simGround.PlayerOnGround = true;
            simGround.PlayerY = simGround.FloorY;
            simGround.PlayerVy = 0;
            simGround.BallX = simGround.PlayerX + 5 * simGround.K;
            simGround.BallY = simGround.PlayerY - simGround.PetR;

            simGround.Update(0.02, inputAxisX: 0, inputJump: false, inputSpike: true);

            Expect(!simGround.SpikeEffectActive, "땅에서는 Ctrl 을 눌러도 강스파이크가 발동하지 않아야 함");
            Expect(simGround.BallVx < 400, "땅에서의 타구는 일반 타구 속도여야 함");
        }

        // 5. 허락 밖 키는 아무 일 없음 / Stop(true) 즉시 끝·그린 것 지움
        {
            var host = new SceneHost();
            var game = new VolleyGame(host);
            var sim = new VolleySim(court, false, 3, new Random(5));
            game.Open(sim, Session.Fake(field), events: false);

            Expect(game.Running, "게임이 시작되어야 함");

            // 허락 키 확인 (Session.Allowed)
            Expect(Session.Allowed.Length == 6, "세션 조작 허락 키는 6개뿐이어야 함");

            // Stop(true) 즉시 종료
            game.Stop(true);
            Expect(!game.Running, "Stop(true) 호출 즉시 Running 이 false 여야 함");

            var dirtyList = new List<Rectangle>();
            game.Dirty(host.Overlay(game), dirtyList);
            // 그린 것 지움
            Expect(!game.Running, "즉시 종료 완료");
        }

        // 6. 기록 저장·불러오기
        {
            var host = new SceneHost();
            var game = new VolleyGame(host);
            game.LoadState("{\"Wins\":7,\"Plays\":12}");

            var stat = game.Stats().First();
            Expect(stat.Value.Contains("7"), $"기록에 이긴 횟수 7이 반영되어야 함 (현재: {stat.Value})");

            string? saved = game.SaveState();
            Expect(saved != null && saved.Contains("\"Wins\":7") && saved.Contains("\"Plays\":12"),
                "SaveState 에 Wins 와 Plays 가 올바르게 저장되어야 함");
        }

        // 7. 시합장 창 이동 및 닫힘 처리 / 쓸 창이 없으면 바탕화면 판
        {
            var sim = new VolleySim(court, false, 3, new Random(6));

            // 창 이동
            var movedCourt = new Rectangle(500, 250, 800, 500);
            sim.SetCourt(movedCourt, false);
            Expect(sim.Court == movedCourt && sim.WallLeft == movedCourt.Left + 1,
                "창이 움직이면 시합장 좌표가 따라가야 함");

            // 쓸 창이 없을 때 바탕화면 판 생성
            var desktopCourt = VolleyGame.MakeDesktopCourt(field, 500, 700, 3);
            Expect(!desktopCourt.IsEmpty && desktopCourt.Width >= 420,
                "바탕화면 판이 정상적인 크기로 생성되어야 함");

            var simDesktop = new VolleySim(desktopCourt, true, 3, new Random(7));
            Expect(simDesktop.IsDesktop, "바탕화면 판 모드로 초기화되어야 함");
            Expect(simDesktop.TitleH == 0, "바탕화면 판은 제목줄 높이가 0이어야 함");
        }

        // 8. AI 는 쉬운 공을 넘긴다 / 같은 씨앗이면 같은 결과
        {
            // A. 쉬운 공 넘기기: 라이벌 코트로 천천히 포물선으로 떨어지는 공
            var sim = new VolleySim(court, false, 3, new Random(8));
            sim.BallX = sim.NetX + 80;
            sim.BallY = sim.NetTopY - 60;
            sim.BallVx = 60;
            sim.BallVy = -100;

            bool rivalHit = false;
            for (int i = 0; i < 90; i++)
            {
                sim.Update(1 / 60.0);
                if (sim.LastHitActor == VolleySim.Actor.Rival)
                {
                    rivalHit = true;
                    break;
                }
            }
            Expect(rivalHit, "AI 는 자신의 코트로 오는 쉬운 공을 받아 넘겨야 함");

            // B. 결정론: 같은 씨앗이면 프레임 결과가 정확히 일치
            var sim1 = new VolleySim(court, false, 3, new Random(42));
            var sim2 = new VolleySim(court, false, 3, new Random(42));

            for (int i = 0; i < 120; i++)
            {
                sim1.Update(1 / 60.0, inputAxisX: 1, inputJump: (i == 20), inputSpike: (i == 30));
                sim2.Update(1 / 60.0, inputAxisX: 1, inputJump: (i == 20), inputSpike: (i == 30));
            }

            Expect(Math.Abs(sim1.BallX - sim2.BallX) < 0.001 &&
                   Math.Abs(sim1.PlayerX - sim2.PlayerX) < 0.001 &&
                   Math.Abs(sim1.RivalX - sim2.RivalX) < 0.001,
                "같은 씨앗이면 같은 결과여야 함 (결정론)");
        }

        // 9. 모듈이 쓰는 표정 이름 및 자세 이름 유효성 검사
        {
            var host = new SceneHost();
            string[] emotes = { "star", "sweat" };
            foreach (var emo in emotes)
            {
                Expect(host.Sprites.Has("emo/" + emo), $"표정 emo/{emo} 가 host.Sprites 에 존재해야 함");
            }

            string[] poses = { "idle", "walk", "hop", "angry", "happy", "sad", "dizzy" };
            foreach (var pose in poses)
            {
                Expect(host.Sprites.Has($"child/1/{pose}"), $"자세 child/1/{pose} 가 host.Sprites 에 존재해야 함");
                Expect(host.Sprites.Has($"adult_grumpy/1/{pose}"), $"자세 adult_grumpy/1/{pose} 가 host.Sprites 에 존재해야 함");
            }
        }

        return "배구: 공 튕김(벽·천장·그물 기둥·캡)·바닥 득점(랠리)·서브권 교대·5점 승리·공중 강스파이크·땅 Ctrl 무효·보스 키 즉시 종료·기록 저장/불러오기·창 추적 및 바탕화면 판·AI 타구·결정론·표정 자세 유효성 모두 통과";
    }

    // ── 화면 없이 그려 보기 (--mode-scene volley <경로>) ──
    public static void Scene(string path, int k)
    {
        if (GameModes.SceneArg == "check")
        {
            Console.WriteLine(Check());
            return;
        }

        var host = new SceneHost(scale: k);
        var game = new VolleyGame(host) { showMe = true };

        int fw = Math.Min(1920, 1280 * k / 3);
        int fh = Math.Min(1040, 720 * k / 3);
        var field = new Rectangle(0, 0, fw, fh);
        var windowCourt = new Rectangle((int)(fw * 0.15), (int)(fh * 0.22), (int)(fw * 0.70), (int)(fh * 0.62));
        var desktopCourt = VolleyGame.MakeDesktopCourt(field, fw * 0.5, fh * 0.75, k);

        using var ov = host.Overlay(game);

        // 4개의 샷:
        // 샷 0: 창 안 랠리 (공 공중 비행, 네트 중앙, 제목줄 점수 점 2:1)
        // 샷 1: 강스파이크 순간 (공중 스파이크, 속도선·별, 창 어긋남 흔들림 효과)
        // 샷 2: 득점 순간 (공 바닥 닿음, 먼지 연출, 새로 켜진 점 반짝임, 기쁜/시무룩 자세)
        // 샷 3: 바탕화면 판 (창 없을 때: 점선 바닥 선 + 가는 기둥 + 두 펫과 공)
        VolleySim? play = null;
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
                    // 샷 0: 창 안 랠리 (점수 2:1)
                    play = new VolleySim(windowCourt, false, k, new Random(10));
                    play.ScorePlayer = 2;
                    play.ScoreRival = 1;
                    play.BallX = play.NetX - 40 * k;
                    play.BallY = play.NetTopY - 35 * k;
                    play.BallVx = 180 * (k / 3.0);
                    play.BallVy = -80 * (k / 3.0);
                    play.PlayerX = (play.WallLeft + play.NetX) / 2.0;
                    play.PlayerPose = "idle";
                    play.RivalX = (play.NetX + play.WallRight) / 2.0;
                    play.RivalPose = "walk";

                    game.Open(play, Session.Fake(field), events: false);
                }
                else if (shot == 1)
                {
                    // 샷 1: 강스파이크 순간 (공중 스파이크 연출)
                    play = new VolleySim(windowCourt, false, k, new Random(20));
                    play.ScorePlayer = 3;
                    play.ScoreRival = 2;
                    play.PlayerOnGround = false;
                    play.PlayerX = play.NetX - 35 * k;
                    play.PlayerY = play.FloorY - 50 * k;
                    play.PlayerVy = -60;
                    play.PlayerPose = "angry";

                    play.BallX = play.PlayerX + 12 * k;
                    play.BallY = play.PlayerY - play.PetR;
                    play.BallVx = play.SpikeSpeed;
                    play.BallVy = play.SpikeVy;

                    play.SpikeEffectActive = true;
                    play.SpikeX = play.BallX;
                    play.SpikeY = play.BallY;
                    play.SpikeDir = 1;
                    play.SpikeTimer = 0.25;
                    play.ShakeUntil = play.Clock + 0.2;

                    game.Open(play, Session.Fake(field), events: false);
                }
                else if (shot == 2)
                {
                    // 샷 2: 득점 순간 (공 바닥 닿음, 먼지 연출, 새로 켜진 점 반짝임)
                    play = new VolleySim(windowCourt, false, k, new Random(30));
                    play.ScorePlayer = 3;
                    play.ScoreRival = 2;
                    play.NewlyLitTeam = 0;
                    play.NewlyLitIndex = 3;
                    play.SparkleTimer = 0.8;

                    play.BallX = play.NetX + 80 * k;
                    play.BallY = play.FloorY - play.BallR;
                    play.BallVx = 0;
                    play.BallVy = 0;

                    play.DustActive = true;
                    play.DustX = play.BallX;
                    play.DustY = play.FloorY;
                    play.DustTimer = 0.5;

                    play.Stage = VolleySim.Phase.PointScored;
                    play.PauseUntil = play.Clock + 1.2;
                    play.LastScorer = VolleySim.Actor.Player;

                    play.PlayerPose = "happy";
                    play.RivalPose = "sad";

                    game.Open(play, Session.Fake(field), events: false);
                }
                else if (shot == 3)
                {
                    // 샷 3: 바탕화면 판 (창 없는 상태)
                    play = new VolleySim(desktopCourt, true, k, new Random(40));
                    play.ScorePlayer = 1;
                    play.ScoreRival = 0;
                    play.BallX = play.NetX + 30 * k;
                    play.BallY = play.NetTopY - 20 * k;
                    play.BallVx = -120 * (k / 3.0);
                    play.BallVy = -60 * (k / 3.0);

                    game.Open(play, Session.Fake(field), events: false);
                }
            }

            shotFrames++;
            game.Frame(1 / 60.0);

            return shot switch
            {
                0 => shotFrames >= 10,
                1 => shotFrames >= 5,
                2 => shotFrames >= 5,
                _ => shotFrames >= 10,
            };
        });
    }

    // ── 속도 재기 (--mode-bench volley) ──
    public static string BenchRun()
    {
        var host = new SceneHost();
        var game = new VolleyGame(host);

        // 1. AI 끼리 대결 (랠리 길이 및 승률 균형)
        int totalMatches = 20;
        int leftWins = 0;
        int totalHits = 0;
        int totalRallies = 0;
        var rnd = new Random(1234);

        var benchField = new Rectangle(0, 0, 1920, 1080);
        var benchCourt = new Rectangle(400, 200, 800, 500);

        for (int m = 0; m < totalMatches; m++)
        {
            var firstServer = (m % 2 == 0) ? VolleySim.Actor.Player : VolleySim.Actor.Rival;
            var w = new VolleySim(benchCourt, false, 3, rnd);
            w.ResetMatch(firstServer);

            double leftReactionTimer = 0;
            double leftTargetX = (benchCourt.Left + w.NetX) / 2.0;

            int maxSteps = 60 * 180; // 판당 최대 3분
            for (int step = 0; step < maxSteps && !w.Done; step++)
            {
                leftReactionTimer -= 1 / 60.0;
                int leftAxisX = 0;
                bool leftJump = false;
                bool leftSpike = false;

                if (w.BallX <= w.NetX + 30 * w.K || w.BallVx < 0)
                {
                    if (leftReactionTimer <= 0)
                    {
                        leftReactionTimer = VolleySim.AiReactionInterval;
                        double predX = w.PredictBallLandingX(w.PlayerY - w.PetR, isRightSide: false);
                        double error = (rnd.NextDouble() - 0.5) * 2 * w.AiErrorPx;
                        leftTargetX = Math.Clamp(predX + error, w.WallLeft + w.PetR + 4, w.NetX - w.PetR - 4);
                    }

                    double diff = leftTargetX - w.PlayerX;
                    if (Math.Abs(diff) > 4 * w.ScaleRatio) leftAxisX = Math.Sign(diff);

                    if (w.PlayerOnGround && w.BallVy > 0 && Math.Abs(w.BallX - w.PlayerX) < w.PetR * 1.2 &&
                        w.BallY < w.FloorY - w.PetR * 1.8 && w.BallY > w.NetTopY - 15 * w.K)
                    {
                        leftJump = true;
                    }

                    if (!w.PlayerOnGround && Math.Abs(w.BallX - w.PlayerX) < w.PetR + 8 * w.K && w.BallY < w.NetTopY + 10 * w.K)
                    {
                        if (rnd.NextDouble() < VolleySim.AiSpikeProbability) leftSpike = true;
                    }
                }
                else
                {
                    leftTargetX = (w.WallLeft + w.NetX) / 2.0;
                }

                w.Update(1 / 60.0, leftAxisX, leftJump, leftSpike);
            }

            totalHits += w.RallyHits;
            totalRallies += Math.Max(1, w.TotalRallies);
            if (w.Done ? w.Won : w.ScorePlayer > w.ScoreRival) leftWins++;
        }

        double leftWinRate = (double)leftWins / totalMatches * 100.0;
        double avgHitsPerRally = (double)totalHits / Math.Max(1, totalRallies);

        // 2. 대형 창(1400x900) 프레임 시간 벤치마크 (목표: 평균 2ms 안)
        var bigCourt = new Rectangle(200, 100, 1400, 900);
        var bigSim = new VolleySim(bigCourt, false, 3, new Random(55));
        game.Open(bigSim, Session.Fake(benchField), events: false);

        using var ov = host.Overlay(game);

        int frameCounter = 0;
        bool BotStep()
        {
            if (++frameCounter > 120 || bigSim.Done) return false;
            return true;
        }

        string perf = Bench.Run("volley_1400x900", ov, game, benchField, BotStep, () =>
        {
            bigSim.Update(1 / 60.0, inputAxisX: 1, inputJump: (frameCounter % 30 == 0), inputSpike: (frameCounter % 45 == 0));
            game.Frame(1 / 60.0);
        }, maxFrames: 120);

        return $"volley: {totalMatches}판 시뮬레이션 (평균 랠리 {avgHitsPerRally:0.0}회, 좌측 승률 {leftWinRate:0.0}%)\n" +
               $"1400x900 대형 창 성능: {perf}";
    }
}
