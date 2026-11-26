using System.Drawing.Drawing2D;
using System.Reflection;
using System.Drawing.Imaging;
using System.Text;

namespace Bolok;

// 버그 떼 타격감: 장면(--mode-scene swarm x.png feel · feel:busy · feel:boss)과 자가 검사 (docs/swarm-feel.md 8장)
sealed partial class SwarmGame
{
    // 작업 창 바탕 흉내: 밝은 창(흰 문서)과 어두운 창(편집기). 줄무늬는 화면 좌표라 어디를 잘라도 이어진다
    static void PaintBack(Graphics g, bool dark, Rectangle area)
    {
        g.FillRectangle(dark ? new SolidBrush(Color.FromArgb(0x1E, 0x1F, 0x26)) : Brushes.White, area);
        var rnd = new Random(5);
        var cols = dark ? new[] { Color.FromArgb(0x56, 0x9C, 0xD6), Color.FromArgb(0xCE, 0x91, 0x78), Color.FromArgb(0x6A, 0x99, 0x55), Color.FromArgb(0xC5, 0x86, 0xC0), Color.FromArgb(0xA0, 0xA4, 0xAE) } : new[] { Color.FromArgb(0xC3, 0xC9, 0xD3), Color.FromArgb(0x9A, 0xA3, 0xB2), Color.FromArgb(0x37, 0x41, 0x51) };
        for (int y = area.Top - area.Top % 22; y < area.Bottom; y += 22)
        {
            int x = area.Left - area.Left % 8 + 30;
            if (y / 22 % 11 is 5 or 6) continue;
            while (x < area.Right)
            {
                int len = 14 + rnd.Next(70);
                using var b = new SolidBrush(cols[rnd.Next(cols.Length)]);
                g.FillRectangle(b, x, y + 8, len, 6);
                x += len + 10;
            }
        }
    }

    // 벌레 하나를 (종류) 볼록이 옆에 세워 두고 맞은 순간·죽는 순간을 프레임 단위로 찍는다. kind: 0 보통 · 1 말벌 · 2 딱정벌레 · 3 엘리트 · 4 강철 · 5 보스
    static readonly string[] FeelNames = { "보통", "말벌", "딱정벌레", "엘리트", "강철", "보스" };

    sealed class FeelRig : IDisposable
    {
        public readonly SceneHost Host;
        public readonly SwarmGame Game;
        public readonly Overlay Ov;
        public Swarm S = null!;
        public readonly Bitmap Frame;
        readonly Graphics fg;
        public FeelRig(int k)
        {
            Host = new SceneHost(scale: k);
            Game = new SwarmGame(Host);
            Ov = Host.Overlay(Game);
            Frame = new Bitmap(Ov.Width, Ov.Height);
            fg = Graphics.FromImage(Frame);
        }
        public void Fresh(int k, int seed = 7, Arm.Id id = Arm.Id.Pistols, int stage = 1)
        {
            S = new Swarm(FieldAt(k), k, new Random(seed), id, stage) { Spawning = false, SafeUntil = 1e9, SpecialsOn = false, GemRate = 1, RushGemRate = 1, ShoeChance = 0, MagnetChance = 0, HeartChance = 0, GrenadeChance = 0 };
            S.Weapon.Range = 0;
            Game.Begin(S);
            S.OnEvent = null;
            Game.ses = null;
            fg.Clear(Color.Gray);
            Ov.Render(fg);   // 그림 묶음을 한 번 만든다 (Art)
        }
        public void Step(int frames = 1)
        {
            for (int i = 0; i < frames; i++) { S.Update(Tick); Game.Track(); }
        }
        public Bitmap Shot(Rectangle crop, bool dark, int zoom = 1, bool gray = false)
        {
            if (gray) fg.Clear(Color.FromArgb(150, 160, 170));
            else PaintBack(fg, dark, new Rectangle(0, 0, Ov.Width, Ov.Height));
            Ov.Render(fg);
            var o = new Bitmap(crop.Width * zoom, crop.Height * zoom);
            using var g = Graphics.FromImage(o);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(Frame, new Rectangle(0, 0, o.Width, o.Height), crop, GraphicsUnit.Pixel);
            return o;
        }
        public void Dispose() { fg.Dispose(); Frame.Dispose(); Ov.Dispose(); }
    }

    static Swarm.Foe FeelFoe(Swarm s, int kind, double dx = 150, double dy = 0)
    {
        var (x, y) = (s.X + dx, s.Y + dy);
        var f = kind switch
        {
            0 => s.Add(Swarm.Kind.Bug, x, y), 1 => s.Add(Swarm.Kind.Wasp, x, y), 2 => s.Add(Swarm.Kind.Beetle, x, y),
            3 => s.MakeElite(s.Add(Swarm.Kind.Bug, x, y)), 4 => s.MakeSteel(s.Add(Swarm.Kind.Beetle, x, y)), _ => s.Add(Swarm.Kind.Boss, x, y, 1),
        };
        f.Speed = 0;
        f.DashAt = 1e9;
        return f;
    }

    // feel:busy: 빽빽한 판 (러시 한가운데, 벌레 수십 마리에 열두 마리가 같이 터지는 순간 · 0.1초 뒤) 밝은 창 · 어두운 창 · 회색 바탕, 실제 크기와 가운데 두 배
    static void FeelBusyScene(string path, int k)
    {
        using var rig = new FeelRig(k);
        rig.Fresh(k, 21, Arm.Id.Rifle, 5);
        var s = rig.S;
        (s.Spawning, s.SpecialsOn, s.Clock) = (true, true, 160);
        s.Weapon.Range = 290;
        for (int i = 0; i < 6; i++) s.LevelUp();
        for (int i = 0; i < 60 * 80; i++)   // 러시 한가운데로
        {
            s.BotPlay();
            s.Update(Tick);
            s.Hp = Swarm.MaxHp;
            rig.Game.Track();
            if (s.Clock >= 190 && s.Foes.Count >= 105) break;
        }
        var near = s.Foes.OrderBy(f => (f.X - s.X) * (f.X - s.X) + (f.Y - s.Y) * (f.Y - s.Y)).Take(12).ToList();
        foreach (var f in near) { s.Strike(f, 9999, s.X, s.Y); s.Foes.Remove(f); }
        rig.Game.Track();
        for (int i = 0; i < 8; i++) { s.BotPlay(); rig.Step(); }   // 연쇄 한가운데 (약 130ms)
        var crop = Rectangle.Inflate(s.Field, 20, 20);
        using var white = rig.Shot(crop, false);
        using var dark = rig.Shot(crop, true);
        using var sheet = new Bitmap(crop.Width * 3 + 40, crop.Height + 20 + crop.Width / 2 * 2 / 3 * 0 + 400);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(244, 245, 247));
        g.DrawImageUnscaled(white, 10, 10);
        g.DrawImageUnscaled(dark, crop.Width + 20, 10);
        using (var gray = rig.Shot(crop, false, 1, true)) g.DrawImageUnscaled(gray, crop.Width * 2 + 30, 10);
        var mid = new Rectangle((int)near.Average(f => f.X) - 150, (int)near.Average(f => f.Y) - 95, 300, 190);
        using (var zoomed = rig.Shot(mid, false, 2)) g.DrawImageUnscaled(zoomed, 10, crop.Height + 20);
        using (var zoomed = rig.Shot(mid, true, 2)) g.DrawImageUnscaled(zoomed, 630, crop.Height + 20);
        sheet.Save(path);
    }

    // feel:boss: 보스 처치 (찌그러짐 80ms → 큰 팡/껍질 조각 + 금 조각, 보석 다섯 알이 사방으로) 50 · 130 · 300ms, 밝은 창 · 어두운 창
    static void FeelBossScene(string path, int k)
    {
        using var rig = new FeelRig(k);
        int[] frames = { 3, 8, 18, 30 };
        const int cell = 380;
        using var sheet = new Bitmap(frames.Length * cell + 20, 2 * cell + 20);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(244, 245, 247));
        for (int dark = 0; dark < 2; dark++)
            for (int c = 0; c < frames.Length; c++)
            {
                rig.Fresh(k, 4, Arm.Id.Pistols, 1);
                var boss = FeelFoe(rig.S, 5, 190, -10);
                rig.Step(10);
                var at = new Point((int)boss.X, (int)boss.Y);
                rig.S.Strike(boss, int.MaxValue / 2, rig.S.X, rig.S.Y);
                rig.S.Foes.Remove(boss);
                rig.Game.Track();
                rig.Step(frames[c]);
                using var b = rig.Shot(new Rectangle(at.X - cell / 4, at.Y - cell / 4 - 10, cell / 2, cell / 2), dark == 1, 2);
                g.DrawImageUnscaled(b, c * cell + 10, dark * cell + 10);
            }
        sheet.Save(path);
    }

    // feel: 차례 띠 (종류 여섯 × 때 여덟 — 맞음·죽음·터짐·조각) 밝은 창 · 어두운 창 (두 번째 장은 이름 뒤에 -dark). 보통 칸은 두 배로, 보스는 그대로 키워 본다
    static void FeelScene(string path, int k)
    {
        using var rig = new FeelRig(k);
        int[] frames = { -2, 0, 1, 2, 4, 8, 14, 24 };   // 죽은 프레임부터 센 프레임 (−2 = 맞은 직후 흰 판): 0 · 17ms · 33 · 67 · 133 · 233 · 400ms
        const int cellW = 240, cellH = 220, bossH = 230;
        for (int dark = 0; dark < 2; dark++)
        {
            using var sheet = new Bitmap(frames.Length * cellW + 20, 5 * (cellH + 6) + bossH + 18);
            using var g = Graphics.FromImage(sheet);
            g.Clear(Color.FromArgb(244, 245, 247));
            int y = 6;
            for (int kind = 0; kind < 6; kind++)
            {
                bool boss = kind == 5;
                for (int c = 0; c < frames.Length; c++)
                {
                    rig.Fresh(k);
                    var f = FeelFoe(rig.S, kind);
                    rig.Step(8);
                    int fr = frames[c];
                    var at = new Point((int)f.X, (int)f.Y);
                    if (fr < 0) { f.Hp = f.MaxHp = 99999; rig.S.Strike(f, 1, rig.S.X, rig.S.Y); rig.Step(2); }
                    else { rig.S.Strike(f, int.MaxValue / 2, rig.S.X, rig.S.Y); rig.S.Foes.Remove(f); rig.Game.Track(); rig.Step(fr); }
                    int zoom = boss ? 1 : 2, w = cellW / zoom, h = (boss ? bossH : cellH) / zoom;
                    using var b = rig.Shot(new Rectangle(at.X - w / 2, at.Y - h / 2 - 6, w, h), dark == 1, zoom);
                    g.DrawImageUnscaled(b, c * cellW + 10, y);
                }
                y += (boss ? bossH : cellH) + 6;
            }
            sheet.Save(dark == 0 ? path : Path.ChangeExtension(path, null) + "-dark.png");
        }
    }

    // ══ 자가 검사 (화면 없이 규칙 + 그림 묶음): 되쓰기 · 상한 · 연쇄 · 멈춤 · 흔들림 · 소리 · 맛이 규칙을 안 건드림 · 다시 칠하기 · 글자 없음 ══
    static string FeelCheck(SwarmGame game, SceneHost host)
    {
        const double tick = Tick;
        var T = TestField;
        bool onWas = Feel.On;
        double shakeWas = Feel.Shake, stopWas = Feel.StopBoss, stopEliteWas = Feel.StopElite;
        try
        {
            Feel.On = true;
            (Feel.Shake, Feel.StopBoss, Feel.StopElite) = (1, .05, 0);
            Swarm Quiet(int seed, Arm.Id id = Arm.Id.Pistols, int stage = 1) => new(T, 3, new Random(seed), id, stage) { Spawning = false, GemRate = 1, RushGemRate = 1, ShoeChance = 0, MagnetChance = 0, HeartChance = 0, GrenadeChance = 0, SpecialsOn = false, SafeUntil = 1e9 };

            // ── ① 되쓰기: Reset 이 모든 칸을 처음 값으로 (칸을 하나씩 바꿔 본다: 새 칸을 넣고 Reset 에 빠뜨리면 여기서 잡힌다), 탄은 번호로 기억 ──
            object Odd(Type t) => t == typeof(bool) ? true : t == typeof(int) ? 7 : t == typeof(double) ? 7.5 : t == typeof(sbyte) ? (sbyte)1 : t.IsEnum ? Enum.GetValues(t).GetValue(Enum.GetValues(t).Length - 1)! : throw new Exception("모르는 칸 모양 " + t);
            foreach (var type in new[] { typeof(Swarm.Foe), typeof(Swarm.Gem) })
            {
                var used = Activator.CreateInstance(type)!;
                var blank = Activator.CreateInstance(type)!;
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                foreach (var fi in fields) fi.SetValue(used, Odd(fi.FieldType));
                type.GetMethod("Reset")!.Invoke(used, null);
                foreach (var fi in fields)
                    if (fi.Name != "Id") Expect(Equals(fi.GetValue(used), fi.GetValue(blank)), $"{type.Name}.Reset 이 {fi.Name} 를 처음 값으로 되돌린다: {fi.GetValue(used)}");
            }
            var shot = new Swarm.Shot();
            var fa = new Swarm.Foe { Id = 5 };
            shot.Mark(fa);
            Expect(shot.Seen(fa) && !shot.Seen(new Swarm.Foe { Id = 6 }), "탄은 지나간 벌레를 번호로 기억한다");
            fa.Reset();
            fa.Id = 6;   // 되쓴 벌레는 새 번호
            Expect(!shot.Seen(fa), "되쓴 벌레는 「이미 맞은 놈」이 아니다");
            var recycle = Quiet(90);
            var first = recycle.Add(Swarm.Kind.Bug, recycle.X + 200, recycle.Y);
            first.Cut = 7; first.BurnUntil = 99; first.StunUntil = 99; first.Hit = 5;
            recycle.Strike(first, 9999, recycle.X, recycle.Y);
            recycle.Update(tick);   // 치워져 되쓸 묶음으로
            var second = recycle.Add(Swarm.Kind.Wasp, recycle.X - 200, recycle.Y);
            Expect(ReferenceEquals(first, second) && second.Id != 1 && second.Cut == 0 && second.BurnUntil < 0 && second.StunUntil < 0 && second.Hit < 0 && !second.Dead && second.Hits == 0 && second.Kind == Swarm.Kind.Wasp, "죽은 벌레는 되쓰이고 칼 번호·불붙음·멈칫·맞은 때가 새 벌레에 안 남는다");
            var gemSwarm = Quiet(91);
            var g1 = gemSwarm.Gems.Count == 0 ? null : gemSwarm.Gems[0];
            gemSwarm.Drop(Swarm.Item.Gem, 3);
            gemSwarm.Steer(0, 0);
            gemSwarm.Update(tick);
            Expect(gemSwarm.Gone.Count == 1, "주운 보석은 사라진 목록에");
            var gone = gemSwarm.Gone[0];
            gone.ShownX = 5;
            gemSwarm.Update(tick);
            gemSwarm.Drop(Swarm.Item.Heart, 40);
            Expect(ReferenceEquals(gemSwarm.Gems[^1], gone) && double.IsNaN(gone.ShownX) && gone.Item == Swarm.Item.Heart && g1 == null, "사라진 보석은 다음 Update 에서 되쓸 묶음으로 돌아가 처음 값으로 다시 나온다");
            var pooled = BotGame(Arm.Id.Rifle, 3, 5, p => { p.Hp = Swarm.MaxHp; return true; });
            Expect(pooled.FoesMade <= Swarm.MaxFoes + 30 && pooled.Born > pooled.FoesMade * 8 && pooled.GemsMade <= Swarm.MaxGems + 60, $"벌레·보석은 되쓴다: 나온 {pooled.Born}마리 중 새로 만든 {pooled.FoesMade}, 보석 {pooled.GemsMade}");

            // ── ② 죽음 기록: 한 프레임에 64 까지, 넘치면 못 적은 수만 센다. 5:00 에 남은 벌레는 고르게 줄여 적는다 ──
            var dth = Quiet(92);
            var crowd = new List<Swarm.Foe>();
            for (int i = 0; i < 100; i++) crowd.Add(dth.Add(Swarm.Kind.Bug, dth.X + 150 + i % 10 * 4, dth.Y - 200 + i * 4));
            foreach (var f in crowd) dth.Strike(f, 999, dth.X, dth.Y);
            Expect(dth.Deaths.Count == Swarm.MaxDeaths && dth.DeathsLost == 36 && dth.Kills == 100 && dth.Deaths[0].Dx > .5 && Math.Abs(dth.Deaths[0].Ox - dth.X) < .01, $"죽음 기록 {Swarm.MaxDeaths} (100마리가 한 프레임에): {dth.Deaths.Count}, 못 적은 {dth.DeathsLost}");
            dth.Update(tick);
            Expect(dth.Deaths.Count == Swarm.MaxDeaths, "죽음 기록은 읽는 쪽이 비울 때까지 남는다 (Update 가 지우지 않는다: 프레임 사이에 터뜨린 벌레도 맛이 본다)");
            dth.Deaths.Trim(0);
            var win = Quiet(93);
            win.Clock = Swarm.WinSec - .01;
            for (int i = 0; i < 150; i++) win.Add(Swarm.Kind.Bug, win.X + 100 + i % 30 * 10, win.Y - 140 + i * 2);
            win.Update(tick);
            int winDeaths = 0;
            for (int i = 0; i < win.Deaths.Count; i++) winDeaths += win.Deaths[i].Win ? 1 : 0;
            Expect(win.State == Swarm.Phase.Win && win.Foes.Count == 0 && winDeaths == win.Deaths.Count && win.Deaths.Count is >= 40 and <= Swarm.MaxDeaths, $"5:00 에 남은 벌레는 죽음 기록으로 (고르게 줄여): {win.Deaths.Count}/150");

            // ── ③ 그리는 쪽 상한·연쇄 ──
            using (var rig = new FeelRig(3))
            {
                var g = rig.Game;
                rig.Fresh(3);
                rig.S.CallPop(Swarm.Kind.Bug, 60);
                rig.Game.Track();
                Expect(g.LivePops == 60 && g.PopRequests == 1, $"한 프레임 60마리: 터짐 {g.LivePops}, 팡 소리 요청 {g.PopRequests} (한 번)");
                rig.Step(16);
                Expect(g.LiveBits > 100 && g.LiveBits <= Feel.MaxBits && g.LivePops <= Feel.MaxPops, $"조각 {g.LiveBits} ≤ {Feel.MaxBits}, 터짐 {g.LivePops} ≤ {Feel.MaxPops}");
                rig.Fresh(3);
                for (int i = 0; i < 6; i++) rig.S.CallPop(i switch { 0 => Swarm.Kind.Beetle, 1 => Swarm.Kind.Wasp, _ => Swarm.Kind.Beetle }, 10, elite: i % 2 == 1, steel: i == 4);
                rig.S.CallPop(Swarm.Kind.Boss, 1);
                rig.Game.Track();
                rig.Step(6);
                Expect(g.LiveBits <= Feel.MaxBits && g.LivePops <= Feel.MaxPops, "큰 놈이 섞인 61마리에서도 조각 상한 안");
                // 연쇄: 셋까지는 한꺼번에, 넷부터는 친 곳(볼록이)에서 가까운 순서로 25ms 씩, 마지막은 한 칸 크게
                rig.Fresh(3);
                for (int i = 0; i < 3; i++) FeelFoe(rig.S, 0, 60 + i * 30, i * 6);
                foreach (var f in rig.S.Foes.ToList()) { rig.S.Strike(f, 999, rig.S.X, rig.S.Y); rig.S.Foes.Remove(f); }
                rig.Game.Track();
                var dues3 = g.fpops.Where(p => p.Live).Select(p => p.Due).Distinct().Count();
                Expect(dues3 == 1 && g.fpops.Count(p => p.Live && p.Big) == 0, "셋까지는 연쇄 없이 한꺼번에");
                rig.Fresh(3);
                var far = new[] { 200.0, 60, 140, 100, 180, 80, 160, 120 };
                for (int i = 0; i < far.Length; i++) FeelFoe(rig.S, 0, far[i], (i % 3 - 1) * 10);
                foreach (var f in rig.S.Foes.ToList()) { rig.S.Strike(f, 999, rig.S.X, rig.S.Y); rig.S.Foes.Remove(f); }
                double t0 = rig.S.Clock;
                rig.Game.Track();
                var chain = g.fpops.Where(p => p.Live).OrderBy(p => p.Due).ToList();
                Expect(chain.Count == 8 && chain.Select((p, i) => Math.Abs(p.Due - t0 - i * Feel.ChainGap) < 1e-9).All(ok => ok) && chain.Last().Due - t0 <= Feel.ChainMax + 1e-9 && chain.Count(p => p.Big) == 1 && chain.Last().Big, $"8마리 연쇄: 25ms 씩 8번, 마지막만 크게");
                var byDist = chain.Select(p => Math.Abs(p.X - rig.S.X)).ToList();
                Expect(byDist.Zip(byDist.Skip(1), (a, b) => a <= b + 1).All(ok => ok), $"가까운 놈이 먼저 터진다: {string.Join("·", byDist.Select(d => (int)d))}");
                rig.S.CallPop(Swarm.Kind.Bug, 3);
                rig.Fresh(3);
                for (int i = 0; i < 40; i++) FeelFoe(rig.S, 0, 60 + i * 4, (i % 7 - 3) * 14);
                foreach (var f in rig.S.Foes.ToList()) { rig.S.Strike(f, 999, rig.S.X, rig.S.Y); rig.S.Foes.Remove(f); }
                rig.Game.Track();
                var want = Math.Min(Feel.ChainMax, 39 * Feel.ChainGap);
                Expect(Math.Abs(g.fpops.Where(p => p.Live).Max(p => p.Due) - rig.S.Clock - want) < 1e-9, $"40마리 연쇄도 {Feel.ChainMax}초 안에 다 터진다");
            }

            // ── ④ 맛은 규칙을 안 건드린다: 같은 판을 Frame 으로 (맛 켬 — 보스 처치 멈춤이 시간을 느리게 한다) 돌린 뒤, 규칙에 넘어간 시간 열 그대로 맛 없이 다시 돌리면 같다.
            //    멈춤이 없으면 맛을 켜든 끄든 Frame 한 판이 글자까지 같다 ──
            string Fp(Swarm p) => $"{p.Kills},{p.Born},{(int)(p.Clock * 1000)},{p.Level},{p.Xp},{p.Gems.Count},{p.DamageTaken},{p.Foes.Count},{p.NormHits},{(int)p.X},{(int)p.Y}";
            void Hook(Swarm p, int frame)
            {
                if (p.State == Swarm.Phase.Play) p.Hp = Swarm.MaxHp;
                if (frame == 300) p.CallPop(Swarm.Kind.Boss, 1);
                if (frame == 600) p.CallPop(Swarm.Kind.Bug, 30);
                if (frame == 330) p.CallPop(Swarm.Kind.Boss, 1);   // 0.5초 뒤 (1.5초 안): 멈춤은 없다
                if (frame == 900) p.CallPop(Swarm.Kind.Boss, 1);
            }
            (string Fp, List<double> Dts, int StopFrames, double StopSec) Run(bool feel, double stop)
            {
                Feel.On = feel;
                Feel.StopBoss = stop;
                var g = new SwarmGame(host);
                var p = new Swarm(T, 3, new Random(1234), Arm.Id.Rifle, 2) { SpecialsOn = true };
                g.Begin(p);
                g.auto = true;
                g.ses = Session.Fake(T);
                using var ov = host.Overlay(g);
                g.Dirty(ov, new List<Rectangle>());   // 그림 묶음을 만든다 (판을 열면 첫 프레임에 하는 일)
                var dts = new List<double>();
                g.DtTap = dts.Add;
                for (int i = 0; i < 60 * 25; i++) { Hook(p, i); g.Frame(tick); }
                g.swarm = null;
                return (Fp(p), dts, g.StopFrames, g.StopSeconds);
            }
            var on = Run(true, .05);
            var offRun = Run(false, .05);
            var onNoStop = Run(true, 0);
            Expect(onNoStop.Fp == offRun.Fp && offRun.StopFrames == 0, $"맛을 켜든 끄든 같은 판 (멈춤 없이): {onNoStop.Fp} · {offRun.Fp}");
            Expect(on.StopFrames is >= 6 and <= 8 && on.StopSec is > .09 and <= .1 + 1e-9, $"보스 처치 멈춤: 두 번(1.5초 안은 한 번), {on.StopFrames}프레임 {on.StopSec * 1000:0}ms");
            var replay = new Swarm(T, 3, new Random(1234), Arm.Id.Rifle, 2) { SpecialsOn = true };
            for (int i = 0; i < on.Dts.Count; i++) { Hook(replay, i); replay.BotPlay(); replay.Update(on.Dts[i]); }
            Expect(Fp(replay) == on.Fp, $"멈춤이 든 판도 규칙의 시간 열만 같으면 같다 (그리는 쪽 없이 다시 돌림): {on.Fp} · {Fp(replay)}");
            Feel.On = true;
            Feel.StopBoss = .05;

            // ── ⑤ 멈춤: 보스 처치에서만, 한 번 50ms, 1.5초에 한 번, 한 판 합쳐 1초 이하. 보통 처치 100번에 0번 ──
            {
                var g = new SwarmGame(host) { FakeOpen = f => Session.Fake(f) };
                g.Command(new[] { "swarm", "auto", "pistols" });
                var p = g.swarm!;
                using var ovStop = host.Overlay(g);
                g.Dirty(ovStop, new List<Rectangle>());
                p.Weapon.Range = 0;
                for (int i = 0; i < 100; i++) { if (i % 2 == 0) p.CallPop(Swarm.Kind.Bug, 4); g.Frame(tick); p.Hp = Swarm.MaxHp; }
                Expect(g.StopFrames == 0, $"보통 처치 100번에 멈춤 0번: {g.StopFrames}");
                p.CallPop(Swarm.Kind.Beetle, 5, elite: true);
                p.CallPop(Swarm.Kind.Bug, 5, steel: true);
                for (int i = 0; i < 30; i++) g.Frame(tick);
                Expect(g.StopFrames == 0, "엘리트·강철 처치에도 멈춤 없음 (기본)");
                for (int i = 0; i < 30; i++) { if (i % 6 == 0) p.CallPop(Swarm.Kind.Boss, 1); g.Frame(tick); p.Hp = Swarm.MaxHp; }
                Expect(g.StopFrames is >= 3 and <= 4, $"보스를 잡아도 1.5초에 한 번만: {g.StopFrames}프레임");
                for (int i = 0; i < 60 * 80; i++) { if (i % 150 == 0) p.CallPop(Swarm.Kind.Boss, 1); g.Frame(tick); if (p.State == Swarm.Phase.Play) p.Hp = Swarm.MaxHp; if (p.Done) break; }
                Expect(g.StopSeconds <= Feel.StopTotal + 1e-9 && g.StopSeconds >= .45, $"한 판 합쳐 멈춤은 1초 이하: {g.StopSeconds:0.00}초");
                g.Stop(true);
                Expect(!g.Running, "멈춤·연쇄 중에도 Esc(보스 키)는 바로 끝낸다");
            }

            // ── ⑥ 흔들림: 보스 처치·큰 폭발(7마리 넘게)에서만, 판 안 그림만 한 칸 (모서리·표시 줄은 그대로). 끄면 0 ──
            using (var rig = new FeelRig(3))
            {
                var g = rig.Game;
                rig.Fresh(3);
                for (int i = 0; i < 6; i++) FeelFoe(rig.S, 0, 80 + i * 10, i * 8 - 20);
                foreach (var f in rig.S.Foes.ToList()) { rig.S.Strike(f, 999, rig.S.X, rig.S.Y); rig.S.Foes.Remove(f); }
                rig.Game.Track();
                Expect(!g.Shaking(rig.S) && g.ShakeAt(rig.S) == (0, 0), "보통 처치에는 흔들림이 없다");
                rig.S.CallPop(Swarm.Kind.Boss, 1);
                rig.Game.Track();
                int amp = (int)Math.Round(Feel.Shake * GameHud.Small(3));
                var seen = new HashSet<(int, int)>();
                for (int i = 0; i < 9; i++) { seen.Add(g.ShakeAt(rig.S)); rig.Step(); }
                Expect(g.Shaking(rig.S) is false && seen.Count >= 3 && seen.All(v => Math.Abs(v.Item1) + Math.Abs(v.Item2) == amp || v == (0, 0)), $"보스 처치: 0.12초 동안 {amp}px 씩 방향을 바꿔 흔들리고 끝난다: {seen.Count}가지");
                Feel.Shake = 0;
                rig.S.CallPop(Swarm.Kind.Boss, 1);
                rig.Game.Track();
                Expect(!g.Shaking(rig.S), "흔들림 끄면 0");
                Feel.Shake = 1;
                // 폭발: 몸 일곱을 한 폭발이 잡으면 흔들린다 (수류탄)
                rig.Fresh(3);
                rig.S.Grenades = 3;
                for (int i = 0; i < 9; i++) FeelFoe(rig.S, 0, 120 + i % 3 * 12, (i / 3 - 1) * 14).Hp = 1;
                rig.S.Steer(1, 0);
                rig.S.Throw();
                for (int i = 0; i < 40 && !g.Shaking(rig.S); i++) rig.Step();
                Expect(g.Shaking(rig.S), "수류탄이 9마리를 잡으면 흔들린다");
                // 흔들리는 프레임에도 모서리·표시 줄은 안 움직인다 (판 가운데를 빼고 같은 그림)
                rig.Fresh(3);
                for (int i = 0; i < 8; i++) { var f = FeelFoe(rig.S, 0, -150 + i * 40, (i % 3 - 1) * 30); f.Hp = f.MaxHp = 9999; }
                rig.S.Drop(Swarm.Item.Gem, 90);
                rig.Step(3);
                var field = rig.S.Field;
                int k = 3;
                var still = rig.Shot(Rectangle.Inflate(field, 20, 20), false);
                g.shakeUntil = rig.S.Clock + 5;
                var shaken = rig.Shot(Rectangle.Inflate(field, 20, 20), false);
                g.shakeUntil = -9;
                Rectangle[] hudBands = { new(0, 0, still.Width, 24 * k + 20), new(0, still.Height - 12 * k - 20 + 20, still.Width, 12 * k) };   // 위 표시 줄, 아래 보스 막대 줄 (벌레는 이 안으로 안 간다)
                int diffBody = 0, diffHud = 0, diffCorner = 0;
                for (int y = 0; y < still.Height; y++)
                    for (int x = 0; x < still.Width; x++)
                    {
                        if (still.GetPixel(x, y) == shaken.GetPixel(x, y)) continue;
                        bool corner = (x < 24 + 8 * k || x > still.Width - 24 - 8 * k) && (y < 24 + 8 * k || y > still.Height - 24 - 8 * k);
                        if (corner) diffCorner++;
                        else if (y < 20 + 22 * k) diffHud++;
                        else diffBody++;
                    }
                still.Dispose(); shaken.Dispose();
                Expect(diffBody > 20 && diffHud == 0 && diffCorner == 0, $"흔들려도 모서리·위 표시 줄은 그대로, 판 안만 움직인다: 안 {diffBody} · 줄 {diffHud} · 모서리 {diffCorner}");
            }

            // ── ⑦ 소리: 한 프레임 40마리 처치에 팡 소리 요청 1번, 1초에 23번 이하. 연쇄에서 번호가 오르고 0.4초 뒤 0. 우선순위 ──
            using (var rig = new FeelRig(3))
            {
                var g = rig.Game;
                var steps = new List<(double At, int Step, bool Big)>();
                g.PopSink = (st, big) => steps.Add((rig.S.Clock, st, big));
                rig.Fresh(3);
                rig.S.CallPop(Swarm.Kind.Bug, 40);
                rig.Game.Track();
                Expect(g.PopRequests == 1, $"한 프레임 40마리 처치에 요청 1번: {g.PopRequests}");
                rig.Step(60);
                Expect(g.PopSounds <= 23 && g.PopSounds >= 3, $"1초에 23번 이하: {g.PopSounds}");
                Expect(steps.Zip(steps.Skip(1), (a, b) => b.Step == a.Step + 1 || b.Step == 5 && a.Step == 5).All(ok => ok) && steps[0].Step == 0 && steps.All(v => v.Step <= 5) && steps.Zip(steps.Skip(1), (a, b) => b.At - a.At >= Feel.PopSoundGap - 1e-9).All(ok => ok), $"연쇄는 번호가 한 칸씩 오르고 45ms 이상 간격: {string.Join(" ", steps.Select(v => $"{v.Step}@{v.At:0.000}"))}");
                Expect(steps.Last().Big, "연쇄 마지막은 큰 팡");
                steps.Clear();
                rig.Step(30);
                rig.S.CallPop(Swarm.Kind.Bug, 1);
                rig.Game.Track();
                Expect(steps.Count == 1 && steps[0].Step <= 2 && !steps[0].Big, $"혼자 죽으면 0~2: {steps.Count}번 {(steps.Count > 0 ? steps[0].Step : -1)}");
                steps.Clear();
                rig.Step(40);
                rig.S.CallPop(Swarm.Kind.Bug, 6);
                rig.Game.Track();
                rig.Step(2);
                Expect(steps.Count >= 1 && steps[0].Step == 0, "0.4초 넘게 처치가 없으면 다시 0 부터");
                rig.Step(30);
                steps.Clear();
                g.lastHighAt = rig.S.Clock;   // 쾅
                rig.S.CallPop(Swarm.Kind.Bug, 1);
                rig.Game.Track();
                Expect(steps.Count == 0, "쾅 뒤 150ms 는 팡이 못 자른다");
                rig.Step(12);
                rig.S.CallPop(Swarm.Kind.Bug, 1);
                rig.Game.Track();
                Expect(steps.Count == 1, "150ms 뒤에는 팡이 난다");
                Expect(!g.FireAllowed(rig.S), "팡 직후 40ms 는 쏘는 소리가 못 자른다");
                rig.Step(4);
                Expect(g.FireAllowed(rig.S), "40ms 뒤에는 쏘는 소리가 난다");
                Feel.On = false;
                Expect(g.FireAllowed(rig.S), "맛을 끄면 쏘는 소리는 그대로");
                Feel.On = true;
            }
            var fwav = Sfx.Make("pop0");
            Expect(Enumerable.Range(0, 6).All(i => Sfx.Make("pop" + i).Length > 44 + 2 * 1000 && Sfx.Make("pop" + i).Length < 44 + 2 * 1400) && Sfx.Make("popbig").Length > Sfx.Make("pop5").Length && fwav.Length > 44, "팡 소리 일곱 가지는 55ms·큰 것 90ms");

            // ── ⑧ 보석 튐: 0.2초 포물선으로 X·Y 에 떨어진다 (보스는 다섯 알이 사방으로, 값의 합은 같다) ──
            using (var rig = new FeelRig(3))
            {
                var g = rig.Game;
                rig.Fresh(3);
                FeelFoe(rig.S, 0, 200, 0);
                var f = rig.S.Foes[0];
                rig.S.Strike(f, 999, rig.S.X, rig.S.Y);
                var gem = rig.S.Gems.Last();
                var (x0, y0) = g.GemAt(rig.S, gem);
                Expect(Math.Abs(x0 - gem.X) < .01 && y0 <= gem.Y + .01, "막 떨어진 보석: 자리는 그 자리에서 (위로 뜬다)");
                rig.S.Foes.Remove(f);
                rig.Step(6);
                var (xm, ym) = g.GemAt(rig.S, gem);
                Expect(ym < gem.Y - 3, $"튀는 중에는 떠 있다: {gem.Y - ym:0.0}px");
                rig.Step(10);
                Expect(g.GemAt(rig.S, gem) == (gem.X, gem.Y) && Swarm.GemHopSec == .2, "0.2초 뒤에는 제자리에 놓인다");
                rig.Fresh(3);
                rig.S.CallPop(Swarm.Kind.Boss, 1);
                var five = rig.S.Gems.Where(m => m.Item == Swarm.Item.Gem).ToList();
                Expect(five.Count == 5 && five.Select(m => m.Xp).Distinct().Count() == 1 && five.All(m => Math.Abs(m.HopX) + Math.Abs(m.HopY) > 4), "보스 보석은 다섯 알, 보스 자리에서 사방으로 튄다");
            }

            // ── ⑨ 되쓰기 + 그리기: 메모리는 프레임마다 새로 안 잡는다 (규칙 + 칠할 자리 + 그림 + 맛, 10단계 몰아넣기) ──
            using (var rig = new FeelRig(3))
            {
                rig.Fresh(3, 15, Arm.Id.Rifle, 10);
                rig.S.Spawning = true;
                rig.S.SpecialsOn = true;
                rig.S.Weapon.Range = 290;
                rig.S.Clock = 100;
                var rects = new List<Rectangle>(512);
                using var gbmp = new Bitmap(rig.Ov.Width, rig.Ov.Height);
                using var gg = Graphics.FromImage(gbmp);
                void One() { rig.S.BotPlay(); rig.S.Update(tick); rig.S.Hp = Swarm.MaxHp; rig.Game.Track(); rects.Clear(); rig.Game.Dirty(rig.Ov, rects); rig.Ov.Render(gg); }
                for (int i = 0; i < 600; i++) One();
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1200; i++) One();
                long per = (GC.GetAllocatedBytesForCurrentThread() - b0) / 1200;
                Expect(per <= 64, $"프레임마다 새로 잡는 메모리 (규칙·칠할 자리·그리기·맛): {per}B/프레임");
            }

            // ── ⑩ 글자 없음: 맛을 그리는 코드가 글자 그리기(Graphics.DrawString·Overlay.Label)를 부르지 않는다 (IL 에서 부르는 곳을 찾는다) ──
            var drawers = new[] { "DrawFeel", "DrawSquash", "DrawFoe", "FeelDirty", "FeelTrack", "NewDeaths", "SpawnBits", "GemAt", "NewBit", "Aim", "PopSound", "ShakeAt" };
            int scanned = 0;
            foreach (var mi in typeof(SwarmGame).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(m => drawers.Contains(m.Name)))
            {
                var il = mi.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>();
                scanned++;
                for (int i = 0; i + 4 < il.Length; i++)
                {
                    if (il[i] != 0x28 && il[i] != 0x6F) continue;   // call · callvirt
                    try
                    {
                        var called = mi.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1));
                        Expect(!(called?.Name is "DrawString" or "MeasureString" or "Label" or "DrawText"), $"{mi.Name} 이 글자를 그린다: {called?.DeclaringType?.Name}.{called?.Name}");
                    }
                    catch (ArgumentException) { }
                }
            }
            Expect(scanned >= drawers.Length - 1, $"글자 없음 검사가 맛 그림 메서드를 다 훑었다: {scanned}");

            // ── ⑪ 다시 칠하기: 바뀐 자리만 칠해도 지나간 그림이 안 남는다 (러시 20초 × 무기 일곱·보스 처치·수류탄 연쇄·흔들림) ──
            void Paint(string what, Swarm p, double secs, Action<Swarm, int>? each = null)
            {
                (game.stage, game.maxStage, game.practice, game.auto) = (p.Stage, 0, false, false);
                game.Begin(p);
                p.OnEvent = null;
                game.ses = Session.Fake(T);
                using var ov = host.Overlay(game);
                double until = p.Clock + secs;
                int frame = 0;
                var (bad, firstBad) = SceneKit.Repaint(ov, game, T, () =>
                {
                    if (p.Done || p.Clock >= until) return false;
                    p.BotPlay();
                    each?.Invoke(p, frame++);
                    p.Update(tick);
                    if (p.State == Swarm.Phase.Play) p.Hp = Math.Max(p.Hp, 40);
                    if (p.Done) return false;
                    game.Track();
                    return true;
                }, 60 * 40, 7);
                Expect(bad == 0, $"바뀐 자리만 다시 칠해도 화면이 맞다 ({what}): {bad}번 다름 (처음 {firstBad}프레임)");
                game.swarm = null;
            }
            for (int w = 0; w < Arm.Kinds; w++) Paint($"러시 {KoNames[w]}", new Swarm(T, 3, new Random(300 + w), (Arm.Id)w, 3) { Clock = 42 }, 6);
            Paint("보스 처치·연쇄·흔들림", new Swarm(T, 3, new Random(310), Arm.Id.Rifle, 2) { Clock = 55 }, 6, (p, f) =>
            {
                if (f == 40) p.CallPop(Swarm.Kind.Boss, 1);
                if (f == 130) p.CallPop(Swarm.Kind.Bug, 30);
                if (f == 170) p.CallPop(Swarm.Kind.Beetle, 8, steel: true);
                if (f == 210) p.CallPop(Swarm.Kind.Bug, 12, elite: true);
            });
            var nade = new Swarm(T, 3, new Random(311), Arm.Id.Long, 2) { Clock = 45 };
            Paint("수류탄 연쇄", nade, 5, (p, f) => { if (f % 40 == 5) { p.Grenades = 3; p.ThrowAt = -9; p.Throw(); } });
            Paint("5:00 에 한꺼번에 터짐", new Swarm(T, 3, new Random(312), Arm.Id.Rifle, 3) { Clock = Swarm.WinSec - 4, SafeUntil = 1e9 }, 7);
            Feel.On = false;
            Paint("맛 끔", new Swarm(T, 3, new Random(313), Arm.Id.Rifle, 3) { Clock = 42 }, 3);
            Feel.On = true;
        }
        finally { (Feel.On, Feel.Shake, Feel.StopBoss, Feel.StopElite) = (onWas, shakeWas, stopWas, stopEliteWas); }
        return "맛: 되쓰기·죽음 기록·상한·연쇄·멈춤·흔들림·팡 소리·보석 튐·글자 없음·다시 칠하기·맛은 규칙을 안 건드림";
    }
}
