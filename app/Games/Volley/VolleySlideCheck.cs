using System.Drawing;

namespace Bolok;

// 배구 뛰면 스파이크 · 슬라이딩 수비 자가 검사 (docs/volley-slide.md 8장)
static partial class VolleyCheck
{
    // ── 뛰면 스파이크 ──
    static string CheckAutoSpike()
    {
        var court = new Rectangle(400, 200, 800, 500);
        var P = VolleySim.Actor.Player; var R = VolleySim.Actor.Rival;
        var now = VolleyTune.Now;
        var ref0 = new VolleySim(court, false, 3, new Random(1), now);
        double apex = ref0.JumpVy * ref0.JumpVy / (2 * ref0.Gravity);

        // 뛰어서 공 옆·위에 닿은 한 장면. fromNet: 네트에서 떨어진 거리, h: 몸이 뜬 높이, last: 마지막으로 친 쪽
        VolleySim Jumper(VolleySim.Actor who, double fromNet, double h, int seed, VolleySim.Actor? last = null, VolleySim.Touch lastTouch = VolleySim.Touch.None)
        {
            var w = Quiet(court, now, seed);
            Away(w, who == P ? R : P);
            int d = D(who);
            double x = w.NetX - d * fromNet, y = w.FloorY - h;
            if (who == P) (w.PlayerX, w.PlayerOnGround, w.PlayerY, w.PlayerVy) = (x, false, y, 0);
            else (w.RivalX, w.RivalOnGround, w.RivalY, w.RivalVy) = (x, false, y, 0);
            w.SetLast(last, lastTouch);
            return w;
        }

        // A. Ctrl 없이 높은 공에 닿으면 스파이크: 캡·기둥에 안 닿고 상대 코트에 떨어진다 (양쪽 거울). Ctrl 을 눌러도 똑같다
        int solved = 0;
        foreach (var who in new[] { P, R })
        {
            int d = D(who);
            for (int dist = 60; dist <= 200; dist += 20)
                for (int hb = 150; hb <= 215; hb += 25)         // 몸이 뜬 높이 (공은 머리 위 가운데 높이, 네트 100px 위쪽)
                    foreach (bool ctrl in new[] { false, true })
                    {
                        var w = Jumper(who, dist, hb, 21, who == P ? R : P, VolleySim.Touch.Normal);
                        double bx = (who == P ? w.PlayerX : w.RivalX), by = (who == P ? w.PlayerY : w.RivalY) - w.PetR;
                        (w.BallX, w.BallY, w.BallVx, w.BallVy) = (bx + d * 8, by - (w.BallR + w.PetR) + 2, 0, 0);
                        if (w.BallY > w.NetTopY) continue;
                        bool net = false;
                        w.OnEvent = (e, a) => { if (e == "bounce" && a is string s && s.StartsWith("net")) net = true; };
                        if (who == P) w.Update(Dt, 0, false, ctrl); else w.Update(Dt);
                        Expect(w.LastTouch == VolleySim.Touch.Spike && w.LastHitActor == who, $"Ctrl 없이 뛰어 높은 공에 닿으면 스파이크 ({who}, 네트에서 {dist}px, 높이 {hb}px, Ctrl {ctrl}, {w.LastTouch})");
                        for (int i = 0; i < 240 && w.Stage == VolleySim.Phase.InPlay; i++) { if (who == P) w.Update(Dt); else w.Update(Dt); }
                        int mine = who == P ? w.ScorePlayer : w.ScoreRival;
                        Expect(!net && mine == 1 && w.LastPoint == VolleySim.PointKind.Spike, $"자동 스파이크가 네트에 닿았거나 상대 코트에 안 떨어졌다 ({who}, {dist}px, {hb}px, 네트 {net}, {w.LastPoint})");
                        solved++;
                    }
        }
        Expect(solved >= 24, $"자동 스파이크 시험 자리가 너무 적다 ({solved})");

        // B. 네트보다 낮은 공 · 몸 뒤의 공은 일반 타구 (낮은 공을 네트에 꽂지 않는다)
        {
            var low = Jumper(P, 100, 30, 22, R, VolleySim.Touch.Normal);
            var lev = new List<string>(); low.OnEvent = (e, _) => lev.Add(e);
            (low.BallX, low.BallY, low.BallVx, low.BallVy) = (low.PlayerX + low.PetR + low.BallR - 4, low.PlayerY - low.PetR, 0, 0);
            Expect(low.BallY > low.NetTopY, "낮은 공 시험 자리");
            low.Update(Dt);
            Expect(lev.Contains("hit") && !lev.Contains("spike") && low.LastTouch == VolleySim.Touch.Normal && low.BallVy < 0, $"네트보다 낮은 공은 스파이크가 아니라 머리로 받는다 ({string.Join(",", lev)} {low.BallVx:F0}/{low.BallVy:F0})");

            var behind = Jumper(P, 150, apex, 23, R, VolleySim.Touch.Normal);
            var bev = new List<string>(); behind.OnEvent = (e, _) => bev.Add(e);
            (behind.BallX, behind.BallY, behind.BallVx, behind.BallVy) = (behind.PlayerX - 50, behind.PlayerY - behind.PetR - 40, 0, 0);
            behind.Update(Dt);
            Expect(bev.Contains("hit") && !bev.Contains("spike") && behind.BallVx > 0 && behind.BallVy < 0, $"몸 뒤의 공은 일반 타구 ({string.Join(",", bev)})");

            var ground = Quiet(court, now, 24); Away(ground, R);
            var gev = new List<string>(); ground.OnEvent = (e, _) => gev.Add(e);
            (ground.BallX, ground.BallY, ground.BallVx, ground.BallVy) = (ground.PlayerX + 5 * ground.K, ground.PlayerY - ground.PetR, 0, 0);
            ground.Update(Dt);
            Expect(!gev.Contains("spike") && ground.LastTouch == VolleySim.Touch.Normal, "땅에서는 스파이크가 아니다");
        }

        // C. 블로킹 점프: 상대 스파이크는 블로킹, 상대의 평범한 공·내가 올린 공은 스파이크 (양쪽 거울)
        foreach (var who in new[] { P, R })
        {
            var other = who == P ? R : P;
            var sp = BlockSetup(now, who, 60, 0, false, 31, other, VolleySim.Touch.Spike);
            var e1 = new List<string>(); sp.OnEvent = (e, _) => e1.Add(e);
            sp.Update(Dt);
            Expect(e1.Contains("block") && !e1.Contains("spike"), $"{who}: 블로킹 점프에 상대 스파이크가 닿으면 블로킹 ({string.Join(",", e1)})");
            var late = BlockSetup(now, who, 60, -400, false, 32, other, VolleySim.Touch.Spike);
            var e2 = new List<string>(); late.OnEvent = (e, _) => e2.Add(e);
            late.Update(Dt);
            Expect(e2.Contains("blockmiss") && !e2.Contains("spike"), $"{who}: 어긋난 블로킹도 상대 스파이크는 블로킹 쪽 ({string.Join(",", e2)})");
            var plain = BlockSetup(now, who, 60, 0, false, 33, other, VolleySim.Touch.Normal);
            var e3 = new List<string>(); plain.OnEvent = (e, _) => e3.Add(e);
            plain.Update(Dt);
            Expect(e3.Contains("spike") && !e3.Contains("block"), $"{who}: 상대의 평범한 공은 블로킹 점프여도 스파이크 ({string.Join(",", e3)})");
            var own = BlockSetup(now, who, 60, 0, false, 34, who, VolleySim.Touch.Normal);
            var e4 = new List<string>(); own.OnEvent = (e, _) => e4.Add(e);
            own.Update(Dt);
            Expect(e4.Contains("spike") && !e4.Contains("block"), $"{who}: 내가 올린 공은 스파이크 ({string.Join(",", e4)})");
        }

        // D. Ctrl 은 읽지 않는다: 같은 씨·같은 입력이면 Ctrl 을 눌러도 판이 같다 (1200프레임, 라이벌 봇은 그대로 돈다)
        {
            double Run(bool ctrl, out int sc)
            {
                var w = new VolleySim(court, false, 3, new Random(77), now) { Birds = true, BirdRng = new Random(5) };
                w.ResetMatch(P);
                var rng = new Random(9);
                int axis = 0; double acc = 0;
                for (int i = 0; i < 1200 && !w.Done; i++)
                {
                    if (i % 20 == 0) axis = rng.Next(3) - 1;
                    w.Update(Dt, axis, rng.NextDouble() < .06, ctrl);
                    acc += w.BallX * 1.3 + w.BallY * .7 + w.PlayerX * 3 + w.RivalX * 5 + w.Clock;
                }
                sc = w.ScorePlayer * 10 + w.ScoreRival;
                return acc;
            }
            double a = Run(false, out int sa), b = Run(true, out int sb);
            Expect(a == b && sa == sb, "Ctrl 을 눌러도 판이 달라지지 않는다");
        }

        return $"자동 스파이크 {solved}곳 Ctrl 없이도(눌러도) 캡·기둥에 안 닿고 상대 코트에 떨어짐·양쪽 거울·낮은 공·뒤의 공·땅은 일반 타구·블로킹 점프(상대 스파이크만 블로킹)·Ctrl 안 읽음";
    }

    // ── 슬라이딩 수비 · 디그 ──
    sealed class SlideRun
    {
        public int Slides, Digs, SlideFrame = -1, DigFrame = -1, Dir;
        public bool Touched, Cap;   // Cap: 공이 네트 캡에 맞았다 (예측이 모르는 튕김)
        public double EndX, StartX, DigVx, DigVy;
        public string FirstHit = "";
    }

    // 공 하나를 떨어뜨려 who 가 axisToward(+1 네트 쪽 · -1 반대 · 0 없음)를 쥐고 있을 때. 모든 자리는 네트 기준 거울
    // fromX: 선수가 네트에서 떨어진 거리 · ballDx: 선수에서 공까지(네트 쪽 +) · h: 공이 바닥 위 얼마나 높이 · vx: 공의 가로 속도(네트 쪽 +)
    static SlideRun SlideCase(VolleyTune tune, VolleySim.Actor who, double fromX, double ballDx, double h, double vx, int axisToward, bool jump = false, bool serving = false, bool preJump = false, bool tripped = false)
    {
        var w = Quiet(Wide2, tune, 3);
        Away(w, who == VolleySim.Actor.Player ? VolleySim.Actor.Rival : VolleySim.Actor.Player);
        int d = D(who);
        double px = w.NetX - d * fromX;
        if (who == VolleySim.Actor.Player) w.PlayerX = px; else w.RivalX = px;
        var run = new SlideRun { StartX = px };
        int frame = 0;
        w.OnEvent = (e, a) =>
        {
            if (e == "slide" && a is VolleySim.Actor sa && sa == who) { run.Slides++; if (run.SlideFrame < 0) (run.SlideFrame, run.Dir) = (frame, w.SlideDirOf(who)); }
            else if (e == "bounce" && a is "net_cap") run.Cap = true;
            else if (e is "hit" or "spike" or "fumble" && run.FirstHit == "") run.FirstHit = $"{e}@{frame} 공({w.BallX:0},{w.BallY:0}) 선수 {(who == VolleySim.Actor.Player ? w.PlayerX : w.RivalX):0}";
            else if (e == "dig" && a is VolleySim.Actor da && da == who) { run.Digs++; if (run.DigFrame < 0) (run.DigFrame, run.DigVx, run.DigVy) = (frame, w.BallVx, w.BallVy); }
        };
        (w.BallX, w.BallY, w.BallVx, w.BallVy) = (px + d * ballDx, w.FloorY - h, d * vx, 0);
        w.Ball = VolleySim.BallKind.Volleyball;
        if (preJump) w.DebugJump(who, px);
        if (tripped) w.DebugTrip(who);
        int axis = d * axisToward;
        for (frame = 0; frame < 220 && (w.Stage == VolleySim.Phase.InPlay || serving && w.Stage == VolleySim.Phase.Serving); frame++)
        {
            if (serving) w.Stage = VolleySim.Phase.Serving;
            if (who == VolleySim.Actor.Player) w.Update(Dt, axis, jump, false);
            else { w.ForceAxis = axis; w.ForceJump = jump; w.Update(Dt); }
        }
        run.Touched = w.LastHitActor == who;
        run.EndX = who == VolleySim.Actor.Player ? w.PlayerX : w.RivalX;
        return run;
    }

    static readonly Rectangle Wide2 = new(0, 0, 800, 500);

    static string CheckSlides()
    {
        var P = VolleySim.Actor.Player; var R = VolleySim.Actor.Rival;
        var now = VolleyTune.Now;

        // A. 걸어서 닿는 공에는 방향키를 쥐고 있어도 한 번도 안 나간다. 나갈 때는 쥔 쪽으로, 받는 일이 많다 · 좌우 거울
        int cases = 0, slid = 0, dug = 0, walkable = 0;
        foreach (double fx in new[] { 100.0, 200, 300 })
            foreach (double h in new[] { 60.0, 110, 180, 260 })
                foreach (double vx in new[] { 0.0, 150, -150 })
                    for (int dx = -300; dx <= 300; dx += 10)
                    {
                        double bx = 400 - fx + dx;                        // 공 자리 (왼쪽 기준): 코트 안(벽·네트에서 떨어진 곳)만
                        if (dx == 0 || bx < 40 || bx > 370) continue;
                        int ax = Math.Sign(dx);
                        var walk = SlideCase(VolleyTune.Before, P, fx, dx, h, vx, ax);
                        var a = SlideCase(now, P, fx, dx, h, vx, ax);
                        var b = SlideCase(now, R, fx, dx, h, vx, ax);
                        cases++;
                        if (walk.Touched && !walk.Cap) { walkable++;   // (캡에 맞고 튄 공은 예측 밖이라 뺀다)
                             Expect(a.Slides == 0, $"걸어서 닿는 공에 나갔다 [{walk.FirstHit}] [나간 {a.SlideFrame}] (선수 {fx}·공 {dx}px·높이 {h}·속도 {vx})"); }
                        if (a.Slides > 0)
                        {
                            slid++; if (a.Digs > 0) dug++;
                            Expect(a.Dir == ax, $"쥔 방향과 다른 쪽으로 나갔다 ({dx}px)");
                        }
                        Expect(a.SlideFrame == b.SlideFrame && a.DigFrame == b.DigFrame && a.Slides == b.Slides && a.Digs == b.Digs && Math.Abs((a.EndX - 400) + (b.EndX - 400)) < 1e-6 &&
                               Math.Abs(a.DigVy - b.DigVy) < 1e-9 && Math.Abs(a.DigVx + b.DigVx) < 1e-9 && a.Dir == -b.Dir,
                            $"슬라이드가 좌우 거울이 아니다 (선수 {fx}·공 {dx}px·높이 {h}·속도 {vx}: {a.SlideFrame}/{b.SlideFrame} {a.DigFrame}/{b.DigFrame} {a.EndX:F2}/{b.EndX:F2})");
                    }
        Expect(slid >= 60 && dug >= slid * .3 && walkable >= 100, $"격자: 나간 {slid}번·받은 {dug}번·걸어서 닿는 {walkable}곳 / {cases}");

        // B. 나가는 조건: 가까운 낮은 공 한 장면은 나가고, 반대 키·키 없음·점프·공중·서브 대기·넘어짐·너무 먼 공은 안 나간다
        {
            var go = SlideCase(now, P, 300, 170, 60, 0, 1);
            Expect(go.Slides == 1 && go.Digs == 1 && go.Dir == 1, $"못 닿을 낮은 공 + 공 쪽 방향키면 나가서 받는다 ({go.Slides}/{go.Digs})");
            Expect(SlideCase(now, P, 300, 170, 60, 0, -1).Slides == 0, "반대쪽 방향키는 안 나간다");
            Expect(SlideCase(now, P, 300, 170, 60, 0, 0).Slides == 0, "키가 없으면 안 나간다");
            Expect(SlideCase(now, P, 300, 170, 60, 0, 1, jump: true).Slides == 0, "↑ 를 같이 누르면 점프가 먼저");
            Expect(SlideCase(now, P, 300, 170, 60, 0, 1, preJump: true).Slides == 0, "공중에서는 안 나간다");
            Expect(SlideCase(now, P, 300, 170, 60, 0, 1, serving: true).Slides == 0, "서브 대기에는 안 나간다");
            Expect(SlideCase(now, P, 300, 170, 60, 0, 1, tripped: true).Slides == 0, "넘어져 있을 때는 안 나간다");
            Expect(SlideCase(now, P, 357, 330, 60, 0, 1).Slides == 0, "터무니없이 먼 공에는 안 던진다");
            Expect(SlideCase(VolleyTune.WithAutoSpike, P, 300, 170, 60, 0, 1).Slides == 0, "슬라이드를 끄면 안 나간다");
            Expect(SlideCase(now, R, 300, 170, 60, 0, 1).Slides == 1, "라이벌 쪽도 나간다");

            // 미끄러지는 중·누워 있는 동안은 또 안 나간다 (같은 키를 쥐고 있어도)
            var w = Quiet(Wide2, now, 4); Away(w, R);
            w.PlayerX = 100; (w.BallX, w.BallY, w.BallVx, w.BallVy) = (270, w.FloorY - 60, 0, 0);
            int slides = 0; w.OnEvent = (e, _) => { if (e == "slide") slides++; };
            bool started = false;
            for (int i = 0; i < 200 && w.Stage == VolleySim.Phase.InPlay; i++)
            {
                w.Update(Dt, 1, false, false);
                if (w.IsSliding(P) || w.IsLying(P)) started = true;
                if (started && (w.IsSliding(P) || w.IsLying(P))) Expect(slides == 1, "슬라이드·누워 있는 동안 또 나갔다");
            }
            Expect(slides >= 1, "한 번은 나갔다");
        }

        // C. 물리: 간 거리·걸린 시간·누워 있기·입력 버림·벽·네트·이득 없음
        {
            var w = Quiet(Wide2, now, 5); Away(w, R); Park(w);
            w.PlayerX = 150;
            double x0 = w.PlayerX, slideDist = w.SlideDist, speed = w.SlideSpeed;
            Expect(Math.Abs(slideDist - 1.5 * 2 * w.PetR) < 1e-9 && Math.Abs(w.SlideTime - slideDist / speed) < 1e-9 && Math.Abs(speed - 520) < 1e-9, $"창: 거리 1.5몸({slideDist:0}px)·빠르기 520·시간 {w.SlideTime:0.000}초");
            int slideFrames = 0, lieFrames = 0; double xSlideEnd = -1, xLieEnd = -1; bool poseOk = true, inputIgnored = true; double tStart = w.Clock;
            w.DebugSlide(P, 1);
            for (int i = 0; i < 120; i++)
            {
                Park(w);
                double xb = w.PlayerX;
                w.Update(Dt, i % 2 == 0 ? -1 : 1, i % 7 == 3, false);      // 던지는 중·누워 있는 중 누르는 키는 버려진다
                if (w.IsSliding(P)) { slideFrames++; xSlideEnd = w.PlayerX; poseOk &= w.PlayerPose == VolleySim.DivePose && w.PlayerPose == "dive" && w.PlayerOnGround; }
                else if (w.IsLying(P)) { if (lieFrames == 0) xSlideEnd = w.PlayerX; lieFrames++; xLieEnd = w.PlayerX; poseOk &= w.PlayerPose == VolleySim.DivePose && w.PlayerOnGround; inputIgnored &= w.PlayerX == xSlideEnd; }
                else if (xLieEnd > 0) break;
            }
            // 던진 걸음 수는 거리 ÷ (속도 × 프레임), 누워 있는 걸음 수는 0.30초
            Expect(Math.Abs(xSlideEnd - x0 - slideDist) < speed * Dt + 1e-6 && Math.Abs(slideFrames * Dt - w.SlideTime) <= Dt * 1.01, $"간 거리가 SlideDist ({xSlideEnd - x0:0.0} / {slideDist:0.0}), 걸린 걸음 {slideFrames}");
            Expect(Math.Abs(lieFrames * Dt - .30) <= Dt * 1.01 && poseOk && inputIgnored, $"누워 있기 0.30초({lieFrames * Dt:0.000}초)·자세 dive·그동안 좌우·점프 안 먹음");
            Expect(!w.IsSliding(P) && !w.IsLying(P) && w.PlayerPose != VolleySim.DivePose, "누워 있기가 끝나면 dive 가 아니다");
            // 일어난 뒤 첫 프레임부터 걷는다·뛴다
            double xr = w.PlayerX; Park(w); w.Update(Dt, 1, false, false);
            Expect(w.PlayerX > xr + 1 && w.PlayerPose == "walk", "일어난 뒤 바로 걷는다");
            Park(w); w.Update(Dt, 0, true, false);
            Expect(!w.PlayerOnGround, "일어난 뒤 바로 뛸 수 있다");

            // 이득 없음: 던지고 일어날 때까지(0.54초) 간 거리가 걷기(240px/초)보다 길지 않다
            Expect(slideDist <= w.WalkSpeed * (w.SlideTime + .30) + 1e-9, $"0.54초 동안 간 거리가 걷기보다 길지 않다 ({slideDist:0} ≤ {w.WalkSpeed * (w.SlideTime + .30):0})");
            // 바탕화면 판은 한쪽 코트의 절반을 넘지 않는다
            var desk = new VolleySim(new Rectangle(0, 0, 600, 336), true, 3, new Random(1), now);
            Expect(Math.Abs(desk.SlideDist - 106.5) < .6 && desk.SlideDist <= .5 * desk.StoneSpanSec * desk.WalkSpeed + 1e-9, $"바탕화면 판 슬라이드 거리 {desk.SlideDist:0.0}px (코트 절반 안)");

            // 벽에서 멈춘다 · 네트를 못 넘는다
            var wl = Quiet(Wide2, now, 6); Away(wl, R); Park(wl);
            wl.PlayerX = wl.WallLeft + wl.PetR + 20; wl.DebugSlide(P, -1);
            for (int i = 0; i < 40; i++) { Park(wl); wl.Update(Dt); }
            Expect(wl.PlayerX >= wl.WallLeft + wl.PetR - 1e-9 && wl.PlayerX <= wl.WallLeft + wl.PetR + 1e-6, $"벽 앞에서 멈춘다 ({wl.PlayerX})");
            var nt = Quiet(Wide2, now, 7); Away(nt, R); Park(nt);
            nt.PlayerX = nt.NetX - nt.PetR - 2 - 30; nt.DebugSlide(P, 1);
            int lieAtNet = 0;
            for (int i = 0; i < 60; i++) { Park(nt); nt.Update(Dt); Expect(nt.PlayerX <= nt.NetX - nt.PetR - 2 + 1e-9, "그물을 넘지 않는다"); if (nt.IsLying(P)) lieAtNet++; }
            Expect(Math.Abs(nt.PlayerX - (nt.NetX - nt.PetR - 2)) < 1e-6 && lieAtNet > 10, $"네트 앞에서 멈추고 누워 있는다 ({nt.PlayerX}, {lieAtNet}걸음)");

            // 돌 위를 지나면 넘어짐으로 바뀐다 (돌 96px 앞, 던지면 126px)
            var st = Quiet(Wide, now with { StoneChance = 1 }, 8); Away(st, R); Park(st);
            st.PlayerX = 300; st.DebugStone(P);
            st.DebugSlide(P, 1);
            for (int i = 0; i < 30; i++) { Park(st); st.Update(Dt); }
            Expect(st.TripCount[0] == 1 && st.IsTripped(P) && !st.IsSliding(P) && st.PlayerPose == VolleySim.TripPose, $"던지다 돌에 걸리면 넘어짐으로 바뀐다 ({st.TripCount[0]}, {st.PlayerPose})");
        }

        // D. 디그: 누운 몸에 닿은 공은 정해진 값으로 높이 뜬다 · 꼭지 실수 없음 · 한 번만 · 일어나서 다시 칠 수 있다 · 좌우 거울
        {
            foreach (var who in new[] { P, R })
            {
                int d = D(who);
                var w = Quiet(Wide2, now, 9, false) ;
                Away(w, who == P ? R : P);
                w.Ball = VolleySim.BallKind.Apple;
                double px = w.NetX - d * 200;
                if (who == P) w.PlayerX = px; else w.RivalX = px;
                w.SetLast(who == P ? R : P, VolleySim.Touch.Normal);
                var evs = new List<string>(); w.OnEvent = (e, _) => evs.Add(e);
                w.DebugSlide(who, d);
                Park(w); if (who == P) w.Update(Dt); else w.Update(Dt);
                for (int i = 0; i < 40 && !w.IsLying(who); i++) { Park(w); if (who == P) w.Update(Dt); else w.Update(Dt); }
                Expect(w.IsLying(who), "누운 상태까지 간다");
                double bodyX = (who == P ? w.PlayerX : w.RivalX) + d * .5 * w.PetR, bodyY = w.FloorY - w.SlideBodyR;
                (w.BallX, w.BallY, w.BallVx, w.BallVy, w.BallSpin) = (bodyX, bodyY - (w.BallR + w.SlideBodyR) + 4, 0, 200, Math.PI);   // 꼭지가 몸 쪽 (서 있으면 실수)
                int hits0 = w.RallyHits;
                double lieEnd = w.Clock;
                for (int i = 0; i < 12; i++) { if (who == P) w.Update(Dt); else w.Update(Dt); }
                Expect(evs.Contains("dig") && !evs.Contains("fumble") && !evs.Contains("hit") && w.LastTouch == VolleySim.Touch.Dig && w.LastHitActor == who, $"{who}: 누운 몸에 닿으면 디그 ({string.Join(",", evs)})");
                Expect(w.RallyHits - hits0 == 1 && w.DigCount[(int)who] == 1, $"{who}: 디그 한 번에 타구 수 1 ({w.RallyHits - hits0})");
                Expect(w.DigFlashActive || w.Clock - lieEnd > 0, "디그 빛");
            }
            // 값: 위로 DigVy · 네트 쪽 DigVx (정해진 값, 거울)
            var c = SlideCase(now, P, 300, 170, 60, 0, 1); var cr = SlideCase(now, R, 300, 170, 60, 0, 1);
            var r0 = new VolleySim(Wide2, false, 3, new Random(1), now);
            Expect(Math.Abs(c.DigVy + now.DigVy * r0.ScaleRatio) < 1e-9 && Math.Abs(c.DigVx - now.DigVx * r0.ScaleRatio) < 1e-9 && Math.Abs(cr.DigVx + now.DigVx * r0.ScaleRatio) < 1e-9, $"디그한 공은 위로 {now.DigVy}·네트 쪽 {now.DigVx} ({c.DigVy:0}/{c.DigVx:0})");

            // 디그한 공이 바닥에 닿기 전에 일어나서 다시 칠 수 있다: 누워 있기가 끝났을 때 공이 아직 머리 위
            var u = Quiet(Wide2, now, 10); Away(u, R);
            u.PlayerX = 100; (u.BallX, u.BallY, u.BallVx, u.BallVy) = (270, u.FloorY - 60, 0, 0);
            bool dugOnce = false, upAfter = false; double ballAtUp = 0;
            for (int i = 0; i < 200 && u.Stage == VolleySim.Phase.InPlay; i++)
            {
                u.Update(Dt, dugOnce ? 0 : 1, false, false);
                if (u.LastTouch == VolleySim.Touch.Dig) dugOnce = true;
                if (dugOnce && !upAfter && !u.IsSliding(P) && !u.IsLying(P)) { upAfter = true; ballAtUp = u.BallY; break; }
            }
            Expect(dugOnce && upAfter && ballAtUp < u.FloorY - 2 * u.PetR - u.BallR - 20 && u.Stage == VolleySim.Phase.InPlay, $"디그 뒤 일어났을 때 공이 아직 머리 위 ({ballAtUp:0}, 바닥 {u.FloorY})");
            // 그 공을 다시 받아 넘길 수 있다: 일어난 뒤 공 아래로 걸어가 뛴다
            int overAfter = u.DigOver[0];
            for (int i = 0; i < 240 && u.Stage == VolleySim.Phase.InPlay; i++)
            {
                double dxx = u.BallX - u.PlayerX; int ax = Math.Abs(dxx) > 6 ? Math.Sign(dxx) : 0;
                bool jmp = u.PlayerOnGround && u.BallVy > 0 && Math.Abs(dxx) < u.PetR * 1.2 && u.BallY < u.FloorY - u.PetR * 1.8 && u.BallY > u.NetTopY - 40 * u.K;
                u.Update(Dt, ax, jmp, false);
            }
            Expect(u.LastHitActor == P && u.RallyHits >= 2, $"디그 뒤 일어나서 다시 칠 수 있다 (타구 {u.RallyHits}, 마지막 {u.LastHitActor})");

            // 상대 스파이크를 받을 때 DigSpikeK 가 곱해진다
            var sk = SlideCase(now with { DigSpikeK = .8 }, P, 300, 170, 60, 0, 1);
            Expect(Math.Abs(sk.DigVy - c.DigVy) < 1e-9, "DigSpikeK 는 상대 스파이크일 때만 곱한다");
        }

        // E. 봇: SlideProb 0 이면 한 번도 안 던지고 1 이면 던진다. 슬라이드가 켜진 판은 같은 씨 = 같은 판
        {
            var never = BotSkill.Human with { SlideProb = 0 };
            var wn = PlayMatch(Wide2, false, now with { Rival = BotSkill.Rival with { SlideProb = 0 } }, never, 3);
            Expect(wn.SlideCount[0] + wn.SlideCount[1] == 0, $"SlideProb 0 이면 안 던진다 ({wn.SlideCount[0]}/{wn.SlideCount[1]})");
            int always = 0;
            for (int seed = 1; seed <= 3; seed++) { var wa = PlayMatch(Wide2, false, now with { Rival = BotSkill.Rival with { SlideProb = 1 } }, BotSkill.Human with { SlideProb = 1 }, seed); always += wa.SlideCount[0] + wa.SlideCount[1]; }
            Expect(always > 0, "SlideProb 1 이면 던진다");
            for (int seed = 1; seed <= 6; seed++)
                Expect(Fingerprint(seed, BotSkill.Human, now) == Fingerprint(seed, BotSkill.Human, now), $"슬라이드가 켜진 판도 같은 씨 {seed} 는 같은 판");
        }

        return $"슬라이드 격자 {cases}곳(걸어서 닿는 {walkable}곳엔 0번 · 나간 {slid}번 중 받은 {dug}번)·좌우 거울·조건(반대 키/무키/↑/공중/서브/넘어짐/먼 공 안 나감)·물리(거리·시간·누워 0.30초·벽·네트·돌)·디그(값·꼭지 실수 없음·한 번만·일어나 다시 침)·봇 SlideProb";
    }
}
