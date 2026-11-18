using System.Drawing;

namespace Bolok;

// 배구 다듬기(docs/volley-tune.md 11장) 자가 검사: 숫자·스파이크 보조 · 돌부리 · 블로킹 · 같은 씨 = 같은 판 · 공평함 · 메모리
static partial class VolleyCheck
{
    const double Dt = 1 / 60.0;
    static readonly Rectangle Wide = new(0, 0, 1400, 900);   // 한쪽 코트 길이 2.5초: 돌 시간이 줄지 않는 넓은 코트

    // 선수·네트와 안 닿게 공을 천장 가까이 세워 두고 InPlay 로 (봇은 멈춰 둔다)
    static VolleySim Quiet(Rectangle court, VolleyTune tune, int seed, bool desktop = false) => new(court, desktop, 3, new Random(seed), tune) { BotsOff = true, Stage = VolleySim.Phase.InPlay };
    static void Park(VolleySim w) { w.Stage = VolleySim.Phase.InPlay; w.BallX = w.NetX; w.BallY = w.CeilingY + w.BallR + 20; w.BallVx = 0; w.BallVy = 0; }
    static void Away(VolleySim w, VolleySim.Actor who) { if (who == VolleySim.Actor.Player) (w.PlayerY, w.PlayerOnGround) = (w.FloorY + 5000, true); else (w.RivalY, w.RivalOnGround) = (w.FloorY + 5000, true); } // 몸을 바닥 밑으로 치워 공과 안 닿게

    static int D(VolleySim.Actor a) => a == VolleySim.Actor.Player ? 1 : -1;

    static string CheckRules()
    {
        var court = new Rectangle(400, 200, 800, 500);

        // ── 숫자: 점프 ×1.18 · 네트 30% · 꼭지 15° ──
        {
            var oldS = new VolleySim(court, false, 3, new Random(1), VolleyTune.Old);
            var nowS = new VolleySim(court, false, 3, new Random(1));
            double hOld = oldS.JumpVy * oldS.JumpVy / (2 * oldS.Gravity), hNow = nowS.JumpVy * nowS.JumpVy / (2 * nowS.Gravity);
            Expect(hNow / hOld is >= 1.15 and <= 1.20, $"점프 꼭대기 높이가 옛 값의 1.15~1.20배 ({hNow / hOld:0.000}, {hOld:0}→{hNow:0}px)");
            foreach (var r in new[] { new Rectangle(0, 0, 600, 336), new Rectangle(0, 0, 800, 500), new Rectangle(0, 0, 1400, 900), new Rectangle(0, 0, 420, 240) })
            {
                var a = new VolleySim(r, false, 3, new Random(1));
                var b = new VolleySim(r, false, 3, new Random(1), VolleyTune.Old);
                Expect(a.NetH == Math.Clamp((int)(r.Height * .30), 24 * 3, 40 * 3), $"네트는 코트 높이의 30% (24칸~40칸): {r.Height} → {a.NetH}");
                Expect(b.NetH == Math.Clamp((int)(r.Height * .36), 24 * 3, 50 * 3), $"옛 네트는 36% (24칸~50칸): {r.Height} → {b.NetH}");
            }
            var fum = new VolleySim(court, false, 3, new Random(1)) { Ball = VolleySim.BallKind.Apple };
            fum.BallSpin = Math.PI - 14 * Math.PI / 180;
            Expect(fum.StemHitsHead(0, -1), "꼭지가 머리 쪽 14° 면 실수");
            fum.BallSpin = Math.PI - 16 * Math.PI / 180;
            Expect(!fum.StemHitsHead(0, -1), "꼭지가 머리 쪽 16° 면 정상");
        }

        // ── 옛 규칙·옛 봇으로 돌린 판이 고치기 전과 같다 (같은 씨 20판: 타구 449 · 랠리 89 · 왼쪽 승 0 · 점수 9:100 · 시간 938.766667초) ──
        {
            var (h, r, wn, clk, sp, sr) = LegacyBench();
            Expect(h == 449 && r == 89 && wn == 0 && Math.Abs(clk - 938.766667) < 1e-5 && sp == 9 && sr == 100, $"VolleyTune.Old 는 고치기 전과 같은 판 ({h}/{r}/{wn}/{clk:F6}/{sp}:{sr})");
        }

        // ── 스파이크 보조 ──
        string assist = CheckAssist();

        // ── 돌부리 ──
        string stones = CheckStones();

        // ── 블로킹 ──
        string blocks = CheckBlocks();

        // ── 그 밖: 같은 씨 = 같은 판 · 메모리 · 자세 · 보스 키 · 다시 칠하기 ──
        string misc = CheckMisc();

        return $"{assist}, {stones}, {blocks}, {misc}";
    }

    // ── 스파이크 보조 ──
    static string CheckAssist()
    {
        // A. 닿은 자리 격자(네트에서 30~200px, 네트 위 10~120px)마다 캡·기둥에 닿지 않고 상대 코트에 떨어진다. 좌우 거울도 같다
        int solved = 0, total = 0, wall = 0;
        foreach (var (r, desktop) in new[] { (new Rectangle(0, 0, 800, 500), false), (new Rectangle(0, 0, 600, 336), true) })
            for (int dist = 30; dist <= 200; dist += 10)
                for (int h = 10; h <= 120; h += 10)
                {
                    var res = new (bool Ok, double Vx, double Vy)[2];
                    foreach (var who in new[] { VolleySim.Actor.Player, VolleySim.Actor.Rival })
                    {
                        int d = D(who);
                        var w = Quiet(r, VolleyTune.Now, 3, desktop);
                        Away(w, VolleySim.Actor.Player); Away(w, VolleySim.Actor.Rival);
                        double x0 = w.NetX - d * dist, y0 = w.NetTopY - h;
                        bool ok = w.AimOver(x0, y0, d, w.SpikeSpeed, w.T.SpikeMinSpeed * w.ScaleRatio, w.SpikeVy, out double vx, out double vy);
                        res[who == VolleySim.Actor.Player ? 0 : 1] = (ok, vx, vy);
                        total++;
                        if (!ok) continue;
                        solved++;
                        // 그 속도로 공만 굴려 본다
                        bool net = false, wallBounce = false;
                        w.OnEvent = (e, a) => { if (e == "bounce" && a is string s) { if (s.StartsWith("net")) net = true; else if (s == "wall") wallBounce = true; } };
                        (w.BallX, w.BallY, w.BallVx, w.BallVy) = (x0, y0, vx, vy);
                        w.SetLast(who, VolleySim.Touch.Spike);
                        for (int i = 0; i < 240 && w.Stage == VolleySim.Phase.InPlay; i++) w.Update(Dt);
                        int mine = who == VolleySim.Actor.Player ? w.ScorePlayer : w.ScoreRival;
                        Expect(!net, $"보조 스파이크가 네트에 닿았다 ({r.Width}×{r.Height}, {who}, 네트에서 {dist}px, 위 {h}px, {vx:F0}/{vy:F0})");
                        Expect(mine == 1 && w.LastPoint == VolleySim.PointKind.Spike, $"보조 스파이크가 상대 코트에 떨어지지 않았다 ({r.Width}×{r.Height}, {who}, {dist}px, {h}px, {vx:F0}/{vy:F0}, {w.LastPoint})");
                        if (wallBounce) wall++;
                    }
                    Expect(res[0].Ok == res[1].Ok && Math.Abs(res[0].Vx + res[1].Vx) < 1e-6 && Math.Abs(res[0].Vy - res[1].Vy) < 1e-6, $"보조 스파이크 계산이 좌우 거울이 아니다 ({r.Width}, {dist}px, {h}px)");
                }
        Expect(solved >= total * .70, $"보조가 풀 수 있는 자리가 너무 적다 ({solved}/{total})");

        // B. 닿는 길 그대로: 높은 공은 옛 값 그대로 · 공이 네트보다 낮으면 옛 값 · 공이 선수 뒤면 스파이크가 아니다 · 스파이크 한 번에 RallyHits 1
        var court = new Rectangle(400, 200, 800, 500);
        VolleySim Jumper(double fromNet, double h, int seed)
        {
            var w = Quiet(court, VolleyTune.Now, seed);
            Away(w, VolleySim.Actor.Rival);
            w.PlayerX = w.NetX - fromNet;
            (w.PlayerOnGround, w.PlayerY, w.PlayerVy) = (false, w.FloorY - h, 0);
            return w;
        }
        var ref0 = new VolleySim(court, false, 3, new Random(1));
        double apex = ref0.JumpVy * ref0.JumpVy / (2 * ref0.Gravity);

        var high = Jumper(150, apex, 11);                 // 머리 위의 공 (바닥 위 219px, 네트 150px 앞)
        high.BallX = high.PlayerX + 10; high.BallY = high.PlayerY - high.PetR - (high.BallR + high.PetR) + 2; high.BallVx = high.BallVy = 0;
        high.Update(Dt, 0, false, true);
        Expect(high.LastTouch == VolleySim.Touch.Spike && Math.Abs(high.BallVx - high.SpikeSpeed) < 1e-6 && Math.Abs(high.BallVy - high.SpikeVy) < 1e-6, $"높은 공의 보조 스파이크는 쳐 보는 값 그대로 (넘기고도 코트 안) ({high.BallVx:F1}/{high.BallVy:F1})");

        var low = Jumper(100, 30, 12);                    // 공 가운데가 네트 꼭대기보다 낮다 (보조 안 함)
        low.BallX = low.PlayerX + low.PetR + low.BallR - 4; low.BallY = low.PlayerY - low.PetR; low.BallVx = low.BallVy = 0;
        Expect(low.BallY > low.NetTopY, "낮은 공 시험 자리");
        low.Update(Dt, 0, false, true);
        Expect(low.LastTouch == VolleySim.Touch.Spike && Math.Abs(low.BallVx - low.SpikeSpeed) < 1e-6 && Math.Abs(low.BallVy - low.SpikeVy) < 1e-6, $"네트보다 낮은 공은 보조 없이 그대로 ({low.BallVx:F1}/{low.BallVy:F1})");

        var mid = Jumper(166, 98, 13);                    // 네트 100px 앞 바닥 위 140px(네트 위 20px): 센 스파이크는 벽에 닿으니 느리고 살짝 든다
        mid.BallX = mid.PlayerX + mid.PetR + mid.BallR - 4; mid.BallY = mid.PlayerY - mid.PetR; mid.BallVx = mid.BallVy = 0;
        Expect(mid.BallY <= mid.NetTopY, "낮게 걸친 공 시험 자리");
        mid.Update(Dt, 0, false, true);
        Expect(mid.LastTouch == VolleySim.Touch.Spike && mid.BallVx is > 400 and < 1299 && mid.BallVy < 0, $"낮게 걸친 공은 느리고 살짝 든다 ({mid.BallVx:F0}/{mid.BallVy:F0})");

        var behind = Jumper(150, apex, 14);               // 공이 선수 뒤: 스파이크가 아니라 일반 타구
        var bev = new List<string>(); behind.OnEvent = (e, _) => bev.Add(e);
        behind.BallX = behind.PlayerX - 50; behind.BallY = behind.PlayerY - behind.PetR - 40; behind.BallVx = behind.BallVy = 0;
        behind.Update(Dt, 0, false, true);
        Expect(bev.Contains("hit") && !bev.Contains("spike") && behind.BallVx > 0 && behind.BallVy < 0, $"공이 선수 뒤면 스파이크가 아니라 위·앞으로 뜬다 ({string.Join(",", bev)} {behind.BallVx:F0}/{behind.BallVy:F0})");

        var once = Jumper(150, apex, 15);                 // 머리 위(살짝 뒤)의 공을 앞으로 칠 때 몸에 다시 닿지 않는다
        int spikes = 0; once.OnEvent = (e, _) => { if (e == "spike") spikes++; };
        once.BallX = once.PlayerX - 6; once.BallY = once.PlayerY - once.PetR - (once.BallR + once.PetR) + 2; once.BallVx = once.BallVy = 0;
        for (int i = 0; i < 12; i++) once.Update(Dt, 0, false, true);
        Expect(spikes == 1 && once.RallyHits == 1, $"스파이크 한 번에 타구 수는 1 ({spikes}번, RallyHits {once.RallyHits})");

        return $"보조 스파이크 격자 {solved}/{total}곳(벽 맞음 {wall}) 캡·기둥에 안 닿고 상대 코트에 떨어짐·좌우 거울·낮은 공은 보조 없이·뒤의 공 일반 타구·다시 닿기 막기";
    }

    // 한 쪽이 sec 초 동안 axis 로 달린다 (플레이어는 입력, 라이벌은 ForceAxis). 돌·넘어짐 사건의 시각을 모은다
    sealed class Run
    {
        public double StoneAt = -1, TripAt = -1, StoneX, StoneDx, TripEndAt = -1;
        public int Stones, Trips;
    }

    static Run Attach(VolleySim w, VolleySim.Actor who)
    {
        var run = new Run();
        w.OnEvent = (e, a) =>
        {
            if (e == "stone" && a is VolleySim.Actor sa && sa == who) { run.Stones++; run.StoneAt = w.Clock; run.StoneX = w.StoneX; run.StoneDx = w.StoneX - (who == VolleySim.Actor.Player ? w.PlayerX : w.RivalX); }
            else if (e == "trip" && a is VolleySim.Actor ta && ta == who) { run.Trips++; run.TripAt = w.Clock; }
        };
        return run;
    }

    static void Step(VolleySim w, VolleySim.Actor who, int axis, bool jump = false)
    {
        Park(w);
        (w.ForceAxis, w.ForceJump) = (who == VolleySim.Actor.Rival ? axis : 0, who == VolleySim.Actor.Rival && jump);
        if (who == VolleySim.Actor.Player) w.Update(Dt, axis, jump, false);
        else w.Update(Dt);
    }

    static string CheckStones()
    {
        var tune = VolleyTune.Now with { StoneChance = 1 };
        var P = VolleySim.Actor.Player; var R = VolleySim.Actor.Rival;

        // A. 0.5초 미만 달리기에는 안 솟는다 · 방향을 바꾸면 처음부터
        {
            var w = Quiet(Wide, tune, 5); w.PlayerX = 200; var run = Attach(w, P);
            for (int i = 0; i < 27; i++) Step(w, P, 1);   // 0.45초
            for (int i = 0; i < 40; i++) Step(w, P, 0);
            for (int i = 0; i < 18; i++) Step(w, P, -1);  // 0.3 + 0.3 + 0.3 (방향 바꿈)
            for (int i = 0; i < 18; i++) Step(w, P, 1);
            for (int i = 0; i < 18; i++) Step(w, P, -1);
            Expect(run.Stones == 0 && !w.StoneOn, "0.5초 미만 달리기에는 돌이 안 솟는다");
        }

        // B. 긴 달리기: 0.5초에 솟는다 · 달리는 방향 WalkSpeed × 0.4초 앞 · 그대로 달리면 걸린다 · 0.6초 입력이 안 먹고 slide · 돌은 가라앉는다
        {
            var w = Quiet(Wide, tune, 6); w.PlayerX = 100; var run = Attach(w, P);
            double xTrip = -1, xEnd = -1; bool slid = true, resumed = false; double stoneGone = -1;
            for (int i = 0; i < 240; i++)
            {
                Step(w, P, 1);
                if (w.Clock < .49) Expect(!w.StoneOn, "0.49초까지는 돌이 없다");
                if (run.TripAt > 0 && Math.Abs(w.Clock - run.TripAt) < 1e-9) xTrip = w.PlayerX;
                if (run.TripAt > 0 && w.Clock > run.TripAt && w.Clock < run.TripAt + .55)
                {
                    slid &= w.PlayerPose == VolleySim.TripPose && w.IsTripped(P) && Math.Abs(w.PlayerX - xTrip) < 1e-6;
                }
                if (run.TripAt > 0 && w.Clock > run.TripAt + .7 && !resumed) { resumed = w.PlayerX > xTrip + 1; xEnd = w.PlayerX; }
                if (run.TripAt > 0 && stoneGone < 0 && w.StoneFrame < 0) stoneGone = w.Clock - run.TripAt;
            }
            Expect(run.Stones == 1 && run.StoneAt is >= .5 and <= .56, $"돌은 0.5초 달린 뒤에 한 번 솟는다 (솟은 때 {run.StoneAt:0.000}초)");
            Expect(Math.Abs(run.StoneDx - 96) < 1e-6, $"돌은 달리는 방향 WalkSpeed×0.4초 = 96px 앞 ({run.StoneDx:0.0})");
            Expect(run.Trips == 1 && run.TripAt - run.StoneAt is >= .28 and <= .42, $"그대로 달리면 곧 걸린다 ({run.TripAt - run.StoneAt:0.000}초 뒤)");
            Expect(slid, "넘어져 있는 0.6초 동안 입력이 안 먹고 자세는 slide");
            Expect(resumed, $"0.6초 뒤에는 다시 달린다 ({xTrip:0}→{xEnd:0})");
            Expect(stoneGone is > 0 and <= .35, $"걸린 돌은 곧 가라앉는다 ({stoneGone:0.00}초)");
        }

        // C. 뛰어넘으면 안 걸린다
        {
            var w = Quiet(Wide, tune, 7); w.PlayerX = 100; var run = Attach(w, P);
            bool jumped = false;
            for (int i = 0; i < 150; i++)
            {
                bool jump = run.StoneAt > 0 && !jumped; jumped |= jump;
                Step(w, P, 1, jump);
            }
            Expect(run.Stones == 1 && run.Trips == 0 && w.PlayerX > run.StoneX, $"돌에 닿기 전에 뛰면 안 걸린다 (걸림 {run.Trips}번)");
        }

        // D. 한 랠리에 하나 · 같은 쪽 두 번 연속 없음 · 서브 대기에는 안 솟는다
        {
            var w = Quiet(Wide, tune, 8); w.PlayerX = 100; var rp = Attach(w, P);
            var rr = new Run();
            var prev = w.OnEvent;
            w.OnEvent = (e, a) => { prev!(e, a); if (e == "stone" && a is VolleySim.Actor.Rival) rr.Stones++; };
            void Go(VolleySim.Actor who, int axis, int frames) { for (int i = 0; i < frames; i++) Step(w, who, axis); }
            Go(P, 1, 60);                                  // 첫 달리기: 돌 + 걸림
            Expect(w.StoneCount[0] == 1 && w.TripCount[0] == 1, "첫 달리기에서 돌 하나에 걸린다");
            Go(P, 0, 50);
            Go(P, 1, 60);
            Expect(w.StoneCount[0] == 1, "한 랠리에 돌은 하나");
            w.ResetPositionsForServe(VolleySim.Actor.Player); Park(w); w.BotsOff = true;
            Go(P, -1, 60);
            Expect(w.StoneCount[0] == 1, "같은 쪽에 두 번 연속 솟지 않는다");
            Go(P, 0, 80);
            Go(R, -1, 60);
            Expect(w.StoneCount[1] == 1, "다음 랠리에는 상대 쪽에 솟는다");
            w.ResetPositionsForServe(VolleySim.Actor.Player); Park(w); w.BotsOff = true; w.ForceAxis = 0;
            Go(R, -1, 60);
            Expect(w.StoneCount[1] == 1, "상대 쪽도 두 번 연속 안 솟는다");
            Go(R, 0, 80);
            Go(P, 1, 60);
            Expect(w.StoneCount[0] == 2, "다시 이쪽에 솟는다 (번갈아)");

            var s = Quiet(Wide, tune, 9); s.PlayerX = 100;
            for (int i = 0; i < 120; i++) { Park(s); s.Stage = VolleySim.Phase.Serving; s.Update(Dt, 1, false, false); }
            Expect(s.StoneCount[0] == 0 && !s.StoneOn, "서브 대기 중에는 돌이 안 솟는다");
        }

        // E. 공평함: 거울 상태(x 뒤집기, 역할 바꿈)에서 같은 씨면 솟는 때·자리·걸리는 때·넘어진 시간이 같다
        int sameCount = 0;
        for (int seed = 1; seed <= 60; seed++)
        {
            var t = VolleyTune.Now with { StoneChance = .5 };
            var a = Quiet(Wide, t, seed); a.PlayerX = 100; var ra = Attach(a, P);
            var b = Quiet(Wide, t, seed); b.RivalX = a.WallLeft + a.WallRight - 100; var rb = Attach(b, R);
            int tripFramesA = 0, tripFramesB = 0;
            for (int i = 0; i < 90; i++)
            {
                Step(a, P, 1); Step(b, R, -1);
                if (a.IsTripped(P)) tripFramesA++;
                if (b.IsTripped(R)) tripFramesB++;
            }
            Expect(ra.Stones == rb.Stones && Math.Abs(ra.StoneAt - rb.StoneAt) < 1e-9 && Math.Abs(ra.TripAt - rb.TripAt) < 1e-9 && tripFramesA == tripFramesB, $"돌부리가 좌우 거울이 아니다 (씨 {seed}: {ra.Stones}/{rb.Stones} {ra.StoneAt:0.000}/{rb.StoneAt:0.000} {ra.TripAt:0.000}/{rb.TripAt:0.000} {tripFramesA}/{tripFramesB})");
            if (ra.Stones == 1) Expect(Math.Abs(ra.StoneX - (a.WallLeft + a.WallRight - rb.StoneX)) < 1e-6, "돌 자리가 좌우 거울");
            sameCount += ra.Stones;
        }
        // 씨 2000개: 긴 달리기 하나가 돌을 뽑는 비율이 양쪽 다 10% ± 3%
        int hitP = 0, hitR = 0;
        for (int seed = 1; seed <= 2000; seed++)
        {
            var a = Quiet(Wide, VolleyTune.Now, seed); a.PlayerX = 100;
            var b = Quiet(Wide, VolleyTune.Now, seed); b.RivalX = b.WallLeft + b.WallRight - 100;
            for (int i = 0; i < 40; i++) { Step(a, P, 1); Step(b, R, -1); }
            hitP += a.StoneCount[0]; hitR += b.StoneCount[1];
        }
        Expect(hitP is >= 140 and <= 260 && hitR is >= 140 and <= 260, $"긴 달리기에 돌이 솟는 비율이 10% ± 3% (왼쪽 {hitP / 20.0:0.0}%, 오른쪽 {hitR / 20.0:0.0}%)");

        // F. 바탕화면 판(좁은 코트)에서도 솟는다: 달리기·예고가 코트 길이에 비례해 줄어든다
        {
            var desk = new VolleySim(new Rectangle(0, 0, 600, 336), true, 3, new Random(1));
            Expect(Math.Abs(desk.StoneSpanSec - .89) < .01 && Math.Abs(desk.StoneRunNeed - .29) < .01 && Math.Abs(desk.StoneWarnTime - .40) < .01, $"바탕화면 판 배율 3: 달리기 {desk.StoneRunNeed:0.00}초 · 예고 {desk.StoneWarnTime:0.00}초 (코트 {desk.StoneSpanSec:0.00}초)");
            var wide = new VolleySim(Wide, false, 3, new Random(1));
            Expect(Math.Abs(wide.StoneRunNeed - .5) < 1e-9 && Math.Abs(wide.StoneWarnTime - .4) < 1e-9, "넓은 창은 0.5초 / 0.4초 그대로");
            var w = Quiet(new Rectangle(0, 0, 600, 336), VolleyTune.Now with { StoneChance = 1 }, 3, true); w.PlayerX = 50; var run = Attach(w, P);
            for (int i = 0; i < 40; i++) Step(w, P, 1);
            Expect(run.Stones == 1, "바탕화면 판에서도 긴 달리기에는 돌이 솟는다");
        }

        return $"돌부리 0.5초 미만 안 솟음·솟는 때와 자리·걸림 0.6초 slide·뛰면 피함·랠리당 하나·번갈아·서브 대기 없음·좌우 거울({sameCount}번 같음, 씨 2000개 왼쪽 {hitP / 20.0:0.0}%/오른쪽 {hitR / 20.0:0.0}%)·좁은 코트 비례";
    }

    // 블로킹 시험 자리: who 가 네트 쪽 bodyX 에서 뛰어 높이 apex(꼭대기)·세로 속도 vy 에 있고, 공이 머리 위에 닿아 있다
    static VolleySim BlockSetup(VolleyTune tune, VolleySim.Actor who, double fromNet, double vy, bool ballSide, int seed, VolleySim.Actor? last, VolleySim.Touch lastTouch, double? bodyH = null)
    {
        var w = Quiet(new Rectangle(400, 200, 800, 500), tune, seed);
        if (ballSide) fromNet = 100;                          // 몸 옆 공이 네트 기둥에 닿지 않게 조금 떨어져서
        var other = who == VolleySim.Actor.Player ? VolleySim.Actor.Rival : VolleySim.Actor.Player;
        Away(w, other);
        int d = D(who);
        double h = bodyH ?? w.JumpVy * w.JumpVy / (2 * w.Gravity);
        w.DebugJump(who, w.NetX - d * fromNet);
        double x = who == VolleySim.Actor.Player ? w.PlayerX : w.RivalX, y = w.FloorY - h, cy = y - w.PetR;
        if (who == VolleySim.Actor.Player) (w.PlayerY, w.PlayerVy) = (y, vy); else (w.RivalY, w.RivalVy) = (y, vy);
        w.SetLast(last, lastTouch);
        if (ballSide) (w.BallX, w.BallY) = (x + d * (w.PetR + w.BallR - 4), cy);          // 몸 옆에 닿은 공 (낮은 자리)
        else (w.BallX, w.BallY) = (x + d * 8, cy - (w.BallR + w.PetR) + 2);                // 머리 위에 닿은 공
        (w.BallVx, w.BallVy) = (0, 0);
        return w;
    }

    static string CheckBlocks()
    {
        var P = VolleySim.Actor.Player; var R = VolleySim.Actor.Rival;
        var now = VolleyTune.Now;

        // A. 네트 1.5 몸(126px) 안에서 뛰면 자세 block, 밖이면 hop. 지금은 hop 을 내는 자세와 같은 길
        {
            var w = Quiet(new Rectangle(400, 200, 800, 500), now, 1);
            Away(w, R);
            w.DebugJump(P, w.NetX - 120); Park(w); w.Update(Dt);
            Expect(w.PlayerPose == VolleySim.BlockPose && w.IsBlocking(P), $"네트 120px 안에서 뛰면 block ({w.PlayerPose})");
            var o = Quiet(new Rectangle(400, 200, 800, 500), now, 1);
            Away(o, R);
            o.DebugJump(P, o.NetX - 140); Park(o); o.Update(Dt);
            Expect(o.PlayerPose == "hop" && !o.IsBlocking(P), $"네트 140px 밖에서 뛰면 hop ({o.PlayerPose})");
            // 점프하는 순간만 본다: 뛰는 중에 구역으로 걸어 들어가도 블로킹이 아니다
            var f = Quiet(new Rectangle(400, 200, 800, 500), now, 1);
            Away(f, R);
            f.DebugJump(P, f.NetX - 160);
            for (int i = 0; i < 12; i++) { Park(f); f.Update(Dt, 1, false, false); }
            Expect(!f.IsBlocking(P) && f.PlayerX > f.NetX - 126, "점프한 뒤에 구역으로 들어가도 블로킹 점프가 아니다");
        }

        // B. Ctrl 을 누르면 스파이크가 먼저
        {
            var w = BlockSetup(now, P, 60, 0, false, 2, R, VolleySim.Touch.Spike);
            var ev = new List<string>(); w.OnEvent = (e, _) => ev.Add(e);
            w.Update(Dt, 0, false, true);
            Expect(ev.Contains("spike") && !ev.Contains("block"), $"블로킹 점프 중 Ctrl 이면 스파이크가 먼저 ({string.Join(",", ev)})");
        }

        // C. 꼭대기 ±0.15초 안에 상대 공이 닿으면 맞은 블로킹: 번쩍 · 공이 상대 코트에 떨어진다
        {
            var w = BlockSetup(now, P, 60, 0, false, 3, R, VolleySim.Touch.Spike);
            var ev = new List<string>(); w.OnEvent = (e, _) => ev.Add(e);
            w.Update(Dt);
            Expect(ev.Contains("block") && !ev.Contains("blockmiss") && w.LastTouch == VolleySim.Touch.Block && w.LastHitActor == P && w.BlockFlashActive && w.RallyHits == 1, $"꼭대기에서 막으면 맞은 블로킹 ({string.Join(",", ev)})");
            Expect(w.BallVx > 0 && w.BallVx <= 300 + 1e-6 && w.BallVy > -100, $"막은 공은 네트 바로 뒤로 느리게 ({w.BallVx:F0}/{w.BallVy:F0})");
            Away(w, R);
            for (int i = 0; i < 300 && w.Stage == VolleySim.Phase.InPlay; i++) w.Update(Dt);
            Expect(w.ScorePlayer == 1 && w.LastPoint == VolleySim.PointKind.Block && w.Points[(int)VolleySim.PointKind.Block] == 1, $"막은 공은 상대 코트에 떨어져 블로킹 득점 ({w.ScorePlayer}:{w.ScoreRival}, {w.LastPoint})");
            Expect(w.BlockHit[0] == 1, "블로킹 맞음 횟수");
        }

        // D. 시간 밖(올라가는 중)이나 공이 낮으면 어긋난 블로킹: 상대 스파이크일 때만 자기 쪽으로 힘없이 (BlockMissOnlySpike)
        {
            var late = BlockSetup(now, P, 60, -400, false, 4, R, VolleySim.Touch.Spike);
            var ev = new List<string>(); late.OnEvent = (e, _) => ev.Add(e);
            late.Update(Dt);
            Expect(ev.Contains("blockmiss") && late.LastTouch == VolleySim.Touch.BlockMiss && late.BallVx < 0 && late.BallVy < 0 && !late.BlockFlashActive, $"시간 밖의 블로킹은 자기 쪽으로 약하게, 번쩍 없음 ({string.Join(",", ev)} {late.BallVx:F0}/{late.BallVy:F0})");
            Expect(late.PlayerPose != "shock" && !late.IsTripped(P), "어긋난 블로킹에는 움찔이 없다");

            var low = BlockSetup(now, P, 60, 0, true, 5, R, VolleySim.Touch.Spike, bodyH: 40);
            var evl = new List<string>(); low.OnEvent = (e, _) => evl.Add(e);
            Expect(low.BallY > low.NetTopY, "낮은 공 시험 자리");
            low.Update(Dt);
            Expect(evl.Contains("blockmiss"), $"네트보다 낮은 공을 막으면 어긋난 블로킹 ({string.Join(",", evl)})");

            var plain = BlockSetup(now, P, 60, -400, false, 6, R, VolleySim.Touch.Normal);
            var evp = new List<string>(); plain.OnEvent = (e, _) => evp.Add(e);
            plain.Update(Dt);
            Expect(evp.Contains("hit") && !evp.Contains("blockmiss") && plain.LastTouch == VolleySim.Touch.Normal, $"평범한 공은 지금처럼 튕긴다 ({string.Join(",", evp)})");

            var strict = BlockSetup(now with { BlockMissOnlySpike = false }, P, 60, -400, false, 6, R, VolleySim.Touch.Normal);
            var evs = new List<string>(); strict.OnEvent = (e, _) => evs.Add(e);
            strict.Update(Dt);
            Expect(evs.Contains("blockmiss"), "BlockMissOnlySpike 를 끄면 평범한 공도 어긋난 블로킹");
        }

        // E. 자기가 친 공·서브 공은 블로킹이 아니다
        {
            foreach (var last in new VolleySim.Actor?[] { P, null })
            {
                var w = BlockSetup(now, P, 60, 0, false, 7, last, VolleySim.Touch.Normal);
                var ev = new List<string>(); w.OnEvent = (e, _) => ev.Add(e);
                w.Update(Dt);
                Expect(ev.Contains("hit") && !ev.Contains("block") && !ev.Contains("blockmiss"), $"{(last == null ? "서브 공" : "자기가 친 공")}은 블로킹이 아니다 ({string.Join(",", ev)})");
            }
        }

        // F. 공평함: 거울 상태에서 튕긴 속도가 좌우만 뒤집힌 같은 값 (맞은 블로킹·어긋난 블로킹 둘 다)
        foreach (var (vy, side, kind) in new[] { (0.0, false, VolleySim.Touch.Spike), (-400.0, false, VolleySim.Touch.Spike), (0.0, true, VolleySim.Touch.Spike), (0.0, false, VolleySim.Touch.Normal) })
            for (int seed = 1; seed <= 8; seed++)
            {
                var a = BlockSetup(now, P, 60, vy, side, seed, R, kind, side ? 40 : null);
                var b = BlockSetup(now, R, 60, vy, side, seed, P, kind, side ? 40 : null);
                a.Update(Dt); b.Update(Dt);
                Expect(a.LastTouch == b.LastTouch && Math.Abs(a.BallVx + b.BallVx) < 1e-6 && Math.Abs(a.BallVy - b.BallVy) < 1e-6 && Math.Abs((a.BallX - a.NetX) + (b.BallX - b.NetX)) < 1e-6,
                    $"블로킹이 좌우 거울이 아니다 (vy {vy}, 낮은 공 {side}, {kind}, 씨 {seed}: {a.LastTouch}/{b.LastTouch} {a.BallVx:F1}/{b.BallVx:F1} {a.BallVy:F1}/{b.BallVy:F1})");
            }

        return "블로킹 1.5몸 안 점프=block·밖=hop·Ctrl 먼저·꼭대기 ±0.15초 맞음(번쩍·상대 코트 낙하·블로킹 득점)·어긋남(약하게·상대 스파이크일 때만)·자기 공·서브 공 아님·좌우 거울";
    }

    // 한 판을 처음부터 끝까지 (표와 같은 길): 점수·시간·득점 종류가 같으면 같은 판
    static (int, int, double, string) Fingerprint(int seed, in BotSkill left)
    {
        var w = PlayMatch(new Rectangle(0, 0, 800, 500), false, VolleyTune.Now, left, seed);
        return (w.ScorePlayer, w.ScoreRival, w.Clock, string.Join(",", w.Points) + "/" + string.Join(",", w.StoneCount) + string.Join(",", w.TripCount) + string.Join(",", w.BlockHit) + string.Join(",", w.BlockMissCount));
    }

    static string CheckMisc()
    {
        // 같은 씨 = 같은 판 (돌·블로킹·봇·새 포함, 두 번 돌려 점수·Clock·득점 종류가 같다)
        for (int seed = 1; seed <= 6; seed++)
        {
            var human = BotSkill.Human;
            Expect(Fingerprint(seed, human) == Fingerprint(seed, human), $"같은 씨 {seed} 는 같은 판이어야 한다");
        }
        Expect(Fingerprint(1, BotSkill.Human) != Fingerprint(2, BotSkill.Human), "씨가 다르면 다른 판");

        // 자세 block · slide 가 모든 시험 종·단계에 있다
        var sp = Sprites.Embedded();
        foreach (var form in new[] { "child", "adult_grumpy" })
            foreach (var pose in new[] { VolleySim.BlockPose, VolleySim.TripPose, "shock", "walk" })
                Expect(sp.Has($"{form}/1/{pose}"), $"자세 {form}/1/{pose} 가 있어야 한다");

        // 돌 그림: 글자 그림이 모양을 지키고 네 장이 다르게 (아래부터 ¼·½·¾·전부)
        var rows = VolleyFruits.Stone;
        Expect(rows.Length == 6 && rows.All(r => r.Length == 10 && r.All(c => c == '.' || VolleyFruits.StonePal.ContainsKey(c))), "돌 그림 10×6 칸, 색이 팔레트 안");
        Expect(Enumerable.Range(0, 4).Select(VolleyFruits.StoneRows).SequenceEqual(new[] { 2, 3, 5, 6 }), "돌이 솟는 네 장: 2·3·5·6 줄");

        // 메모리: 돌·넘어짐·블로킹이 나오는 판에서 규칙 한 프레임이 새로 잡는 바이트 0
        long bytes = RuleBytesPerFrame(out int stones, out int trips, out int blocks);
        Expect(stones > 0 && trips > 0 && blocks > 0, $"메모리 시험 판에 돌·넘어짐·블로킹이 나와야 한다 ({stones}/{trips}/{blocks})");
        Expect(bytes == 0, $"규칙 한 프레임이 새로 잡는 메모리는 0B ({bytes}B, 돌 {stones}·넘어짐 {trips}·블로킹 {blocks})");

        // 보스 키·허락 밖 키: 돌·넘어짐·블로킹 중에도 즉시 끝. 다시 칠할 자리가 돌·먼지·번쩍을 덮는다
        {
            var field = new Rectangle(0, 0, 1920, 1080);
            var host = new SceneHost();
            var game = new VolleyGame(host);
            var w = Quiet(new Rectangle(400, 200, 800, 500), VolleyTune.Now with { StoneChance = 1 }, 3);
            game.Open(w, Session.Fake(field), events: false);
            w.DebugStone(VolleySim.Actor.Player);
            w.DebugJump(VolleySim.Actor.Player, w.NetX - 60);
            w.BlockFlashUntil = w.Clock + 1;
            Expect(w.StoneFrame >= 0 && w.BlockFlashActive, "돌·번쩍이 켜져 있다");
            game.Stop(true);
            Expect(!game.Running, "돌·블로킹 중에도 Stop(true) 가 즉시 끝낸다");
        }
        {
            var field = new Rectangle(0, 0, 1920, 1080);
            var host = new SceneHost();
            var game = new VolleyGame(host) { showMe = true };
            var w = Quiet(new Rectangle(400, 200, 800, 500), VolleyTune.Now with { StoneChance = 1 }, 3);
            w.PlayerX = 560;
            bool right = true; int n = 0;
            game.Open(w, Session.Fake(field, key: k => k == Keys.Right && right, active: () => true), events: false);
            using var ov = host.Overlay(game);
            var (bad, first) = SceneKit.Repaint(ov, game, field, () =>
            {
                Park(w);
                right = n < 70;                                         // 달려서 돌이 솟고 걸려 넘어지고 먼지
                if (n == 150) { w.DebugJump(VolleySim.Actor.Player, w.NetX - 60); w.BlockFlashUntil = w.Clock + .25; } // 블로킹 자세와 번쩍
                game.Frame(Dt);                                         // 규칙을 바꾸는 일은 Frame 안에서 (바뀐 자리를 Frame 이 알린다)
                host.Pet.Hush();
                return ++n < 260;
            }, 260, every: 3);
            Expect(bad == 0, $"바뀐 자리만 다시 칠해도 돌·먼지·번쩍이 맞다: {bad}번 다름 (처음 {first}프레임)");
        }

        return $"같은 씨 6판 같음·자세 block/slide 있음·규칙 메모리 {bytes}B/프레임(돌 {stones}·넘어짐 {trips}·블로킹 {blocks})·보스 키·다시 칠하기·옛 판 재현";
    }
}
