using System.Drawing;

namespace Bolok;

// 배구 다듬기 장면 (--mode-scene volley x new / stone)
static partial class VolleyCheck
{
    // 돌부리 그림 네 장 (솟을 때 아래부터 ¼·½·¾·전부, 처음 두 장에는 흙 알갱이), 위아래 확대
    static void StoneStrip(string path, int k)
    {
        int zoom = Math.Max(k, 6);
        var frames = Enumerable.Range(0, 4).Select(i => VolleyGame.MakeStone(i, zoom)).ToArray();
        try
        {
            int w = frames.Sum(f => f.Width + 12 * zoom / 3) + 12 * zoom / 3, h = frames[0].Height + 16 * zoom / 3;
            using var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(150, 160, 170));
            using var floor = new SolidBrush(Color.FromArgb(126, 142, 120));
            int x = 12 * zoom / 3, floorY = h - 8 * zoom / 3;
            g.FillRectangle(floor, 0, floorY, w, h - floorY);
            foreach (var f in frames)
            {
                g.DrawImageUnscaled(f, x, floorY - f.Height);
                x += f.Width + 12 * zoom / 3;
            }
            bmp.Save(path);
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    // 장면 넷 (바탕화면 판): ① 돌이 솟아 보이는 예고 ② 걸려 넘어진 순간(slide·먼지) ③ 블로킹(block 자세·번쩍) ④ 캡을 넘는 보조 스파이크
    static void NewScenes(string path, int k)
    {
        var host = new SceneHost(scale: k);
        var game = new VolleyGame(host) { showMe = true };
        int fw = Math.Min(1920, 1280 * k / 3), fh = Math.Min(1040, 720 * k / 3);
        var field = new Rectangle(0, 0, fw, fh);
        var court = VolleyGame.MakeDesktopCourt(field, fw * 0.5, fh * 0.75, k);
        var view = Rectangle.Intersect(field, Rectangle.FromLTRB(court.Left - 30, court.Top - 30, court.Right + 30, court.Bottom + 70));
        bool right = false, ctrl = false;
        var ses = Session.Fake(field, key: key => (key == Keys.Right && right) || (key == Keys.ControlKey && ctrl), active: () => true);
        using var ov = host.Overlay(game);

        VolleySim? w = null;
        double stoneAt = -1, tripAt = -1, blockAt = -1;
        int shotNow = -1, frames = 0;
        bool dropped = false;
        var stoneTune = VolleyTune.Now with { StoneChance = 1 };
        var P = VolleySim.Actor.Player;

        SceneKit.Shots(path, ov, view, 4, shot =>
        {
            if (shot != shotNow)
            {
                // 돌 장면(0·1)은 같은 판을 이어서 쓴다
                if (shot == 0 || shot >= 2)
                {
                    w = new VolleySim(court, true, k, new Random(7 + shot), shot == 0 ? stoneTune : VolleyTune.Now) { BotsOff = true, Ball = VolleySim.BallKind.Orange, BallSpin = 2 };
                    w.OnEvent = (e, _) => { if (e == "stone") stoneAt = w.Clock; else if (e == "trip") tripAt = w.Clock; else if (e == "block") blockAt = w.Clock; };
                    w.ScorePlayer = 2; w.ScoreRival = 1;
                    game.Open(w, ses, events: false);
                }
                (shotNow, frames, dropped) = (shot, 0, false);
                (right, ctrl) = (false, false);
                var q = w!;
                if (shot == 0) q.PlayerX = q.WallLeft + q.PetR + 8;
                if (shot == 2) q.DebugJump(P, q.NetX - q.PetR - 8);   // 네트 앞에서 뛴다 (블로킹 자세)
                if (shot == 3)                                       // 머리 위 공을 꼭대기 근처에서 스파이크: 보조가 캡을 넘긴다
                {
                    q.PlayerX = q.NetX - 150 * k / 3.0;
                    (q.PlayerOnGround, q.PlayerY, q.PlayerVy) = (false, q.FloorY - 105 * k / 3.0, 0);
                    (q.BallX, q.BallY, q.BallVx, q.BallVy) = (q.PlayerX + 8 * k / 3.0, q.PlayerY - q.PetR - (q.BallR + q.PetR) + 2, 0, 0);
                    q.SetLast(VolleySim.Actor.Rival, VolleySim.Touch.Normal);
                    ctrl = true;
                }
            }
            var s = w!;
            frames++;
            if (shot is 0 or 1) { Park(s); right = true; }
            else if (shot == 2)
            {
                // 꼭대기에서 상대 스파이크가 머리 위로 내려온다
                if (blockAt < 0 && !dropped && Math.Abs(s.PlayerVy) < 40 && !s.PlayerOnGround && frames > 4)
                {
                    dropped = true;
                    s.SetLast(VolleySim.Actor.Rival, VolleySim.Touch.Spike);
                    (s.BallX, s.BallY, s.BallVx, s.BallVy) = (s.PlayerX + 8, s.PlayerY - s.PetR - (s.BallR + s.PetR) - 8, 0, 160);
                }
                else if (!dropped) Park(s);
            }
            else if (shot == 3 && frames > 1) ctrl = false;
            s.Stage = VolleySim.Phase.InPlay;
            game.Frame(Dt);
            return shot switch
            {
                0 => stoneAt > 0 && s.Clock - stoneAt >= .18,
                1 => tripAt > 0 && s.Clock - tripAt >= .15,
                2 => blockAt > 0 && s.Clock - blockAt >= .08,
                _ => frames > 3 && s.BallX >= s.NetX - 6,
            };
        });
    }
}
