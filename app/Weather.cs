using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Bolok;

// 날씨 (YELL 2026-11-13: "팔랑팔랑은.. 눈송이도 이쁠것 같은데?? 하는김에 비도 오게 할까?? 비오면 큰 잎사귀를 쓰고 비를 맞거나 피하거나 랜덤하게 처리")
// 진짜 날씨는 묻지 않는다 (네트워크 없음). 일을 방해하지 않게 조금만:
//   언제: 한가한 1분마다 1/120 — 하루(9시간) 평균 세 번쯤, 많아야 네 번. 한 번에 2.5~5.5분, 끝나고 40분은 맑다. 처음·끝 15초는 차츰 늘고 준다.
//   무엇: 11~2월엔 눈만, 3~10월엔 비만 (YELL 2026-11-13).
//   어디: 볼록이 머리 위 좁은 구역만 (가로 3칸 × 위로 3칸, 볼록이를 따라다닌다 — YELL 2026-11-13: 화면 전체면 너무 티 난다). 눈 6송이·비 10줄기쯤, 볼록이보다 한 단계 작은 그림.
//         일하는 창(활성 창) 위에는 한 알도 안 그린다. 우산 잎에 닿은 비는 잎 위에서 튀고 그 아래로는 안 떨어진다.
//   멈춤: 게임 중·잠·알·자리 비움에는 시작하지 않고, 오던 것도 그동안 안 보이고 반응도 거둔다 (시간은 그대로 흐른다).
// 볼록이: 눈이 오면 좋아한다 (처음에 방긋·음표, 가끔 깡충(눈송이 잡기)·하트). 밖에서 눈을 맞으면 머리에 눈이 세 단계로 쌓이고,
//   다 쌓이면 잠깐 가만히 섰다가 도리도리 털어 낸다 (눈 조각이 날아가고 처음부터 다시). 비가 오면 그때마다 셋 중 하나 —
//   큰 잎 우산을 쓰고 다닌다 / 그냥 맞는다 (시무룩, 가끔 부르르·땀방울, 머리에 물방울) / 창 뒤로 비를 피한다 (숨바꼭질 자리에서 그칠 때까지. 숨을 창이 없으면 우산).
//   말은 하지 않는다 (그림만).
// 규칙·알갱이 자리(시각의 순수 함수)·볼록이 반응은 여기 (--selfcheck 로 검사). 그리기는 Overlay(알갱이·우산 잎)·PetForm(물방울)
public sealed class Weather
{
    public enum Sky { Clear, Snow, Rain }
    public enum Cover { None, Umbrella, Wet, Shelter } // 비 올 때 볼록이: 큰 잎 우산 / 그냥 맞기 / 창 뒤로 피하기

    public const double ChancePerMin = 1 / 120.0;
    public const int MaxPerDay = 4;
    public const double MinSec = 150, MaxSec = 330;
    public const double GapSec = 40 * 60;
    public const double FadeSec = 15;

    public static bool Winter(int month) => month is 11 or 12 or 1 or 2; // 눈 오는 달. 나머지 달은 비

    // 날씨를 멈출 때: 게임 중·자리 비움·잠·알
    public static bool Quiet(bool inGame, Pet p, bool afk) => inGame || afk || p.Asleep || p.Stage == "egg";

    public Sky Now { get; private set; }
    public Cover Reaction { get; private set; } // 이번 비에 볼록이가 할 반응
    public double From, Until;          // 앱 시계(초)
    public bool On => Now != Sky.Clear;
    DateTime day;
    int count;
    double nextRoll, quietUntil;

    // 1초쯤마다 (1분에 한 번 굴린다). free: 새로 시작해도 될 때 (한가하고 Quiet 가 아님). 바뀌면 "start"·"end"
    public string? Tick(double now, DateTime local, bool free, Random rng)
    {
        if (On && now >= Until) { Stop(now); return "end"; }
        if (On || !free || now < nextRoll) return null;
        nextRoll = now + 60;
        if (local.Date != day) (day, count) = (local.Date, 0);
        if (count >= MaxPerDay || now < quietUntil || rng.NextDouble() >= ChancePerMin) return null;
        count++;
        Begin(Winter(local.Month) ? Sky.Snow : Sky.Rain, now, rng);
        return "start";
    }

    // cover·sec 를 주면 그대로 (디버그·검사), 아니면 무작위
    public void Begin(Sky sky, double now, Random rng, Cover cover = Cover.None, double sec = 0)
    {
        if (sky == Sky.Clear) { Stop(now); return; }
        (Now, From, Until) = (sky, now, now + (sec > 0 ? sec : MinSec + rng.NextDouble() * (MaxSec - MinSec)));
        Reaction = sky == Sky.Rain ? cover != Cover.None ? cover : PickCover(rng) : Cover.None;
        LeafDesign = rng.Next(LeafAnchors.Length); // 비 올 때마다 나뭇잎 우산 네 가지 중 하나 (YELL: 시안 1·6·9·17 무작위)
    }

    public void Stop(double now) { (Now, Reaction) = (Sky.Clear, Cover.None); quietUntil = now + GapSec; }

    public static Cover PickCover(Random rng) => (Cover)(1 + rng.Next(3));

    public double Density(double now) => On ? Math.Clamp(Math.Min(now - From, Until - now) / FadeSec, 0, 1) : 0;

    // ── 볼록이 반응 (프레임마다): 보이는 동안만. 그치거나 가려지면(게임·잠) 바로 거둔다 ──
    public bool UmbrellaUp { get; private set; } // 큰 잎 우산을 들었다 (Overlay 가 그린다)
    public bool Soaked { get; private set; }     // 비를 맞아 머리에 물방울 (PetForm 이 그린다)
    Sky seen;
    Cover acting;
    double nextFun;

    public void React(Brain b, bool shown, Random rng)
    {
        double dt = Math.Clamp(b.Now - lastT, 0, .5);
        lastT = b.Now;
        var sky = shown ? Now : Sky.Clear;
        var cover = sky == Sky.Rain ? Reaction : Cover.None;
        if (sky != seen || cover != acting)
        {
            Leave(b);
            (seen, acting) = (sky, cover);
            Enter(b, rng);
        }
        bool feet = OnFeet(b);
        UmbrellaUp = acting == Cover.Umbrella && feet;
        Soaked = acting == Cover.Wet && feet;
        PileTick(b, feet, dt, rng);
        if (seen == Sky.Clear || b.Now < nextFun) return;
        double t = b.Now;
        if (seen == Sky.Snow) // 눈: 가끔 방긋·하트, 깡충 뛰어 눈송이 잡기·음표
        {
            if (pilePhase != 0 || PileStage == 3) return; // 머리 눈을 털 참이다
            nextFun = t + 25 + rng.NextDouble() * 20;
            if (b.Mode != Mode.Wander) return;
            if (rng.Next(2) == 0) { b.Greet("happy", 1.4); b.Emote("heart", 1.6); }
            else { b.Greet("hop", 1.2); b.Emote("note", 1.4); }
            return;
        }
        switch (acting)
        {
            case Cover.Wet: // 부르르, 가끔 땀방울
                nextFun = t + 10 + rng.NextDouble() * 12;
                if (b.Mode != Mode.Wander) break;
                b.ShakeUntil = t + .4;
                if (rng.NextDouble() < .35) b.Emote("sweat", 1.2);
                break;
            case Cover.Umbrella: // 잎 우산 아래서 가끔 흥얼
                nextFun = t + 40 + rng.NextDouble() * 30;
                if (b.Mode == Mode.Wander && rng.NextDouble() < .3) b.Emote("note", 1.2);
                break;
            case Cover.Shelter: // 들켜서 나왔으면 다시 숨는다. 숨을 창이 없으면 우산
                nextFun = t + 4;
                if (b.Mode == Mode.Wander && b.P.Need(b.K) == null && !b.Shelter(true)) Reaction = acting = Cover.Umbrella;
                break;
        }
    }

    void Enter(Brain b, Random rng)
    {
        nextFun = b.Now + 8 + rng.NextDouble() * 8;
        if (seen == Sky.Snow) { b.Greet("happy", 1.6); b.Emote("note", 1.8); nextFun = b.Now + 20; return; }
        switch (acting)
        {
            case Cover.Wet: b.IdlePose = "sad"; b.Emote("sweat", 1.6); break;
            case Cover.Umbrella: b.Greet("happy", 1); break;
            case Cover.Shelter:
                b.Emote("sweat", 1.2);
                if (!b.Shelter(true)) { Reaction = acting = Cover.Umbrella; Dbg.Log("weather", ("event", "no-shelter")); }
                break;
        }
    }

    void Leave(Brain b)
    {
        if (acting == Cover.Wet) b.IdlePose = null;
        if (acting == Cover.Shelter) b.Shelter(false);
        UmbrellaUp = Soaked = false;
    }

    // ── 머리에 쌓이는 눈 (YELL 2026-11-13): 밖에서 눈을 맞으면 세 단계로 쌓이고, 다 쌓이면 잠깐 가만히 섰다가 도리도리 털어 낸다 ──
    //   숨어 있거나(창 뒤)·들려 있거나·게임·잠·비에는 안 쌓인다. 머리 장식(네잎클로버 등)은 눈이 있는 동안 잠깐 내려 둔다
    public const double PileStageSec = 20, PileStillSec = 1.5, PileShakeSec = .9;
    double pile, pileUntil, lastT;
    int pilePhase; // 0 쌓이는 중, 1 다 쌓여 가만히, 2 터는 중 (털기 시작 0.25초 뒤 눈이 날아간다)
    public int PileStage => pile >= 3 * PileStageSec ? 3 : (int)(pile / PileStageSec);
    public int PileShown { get; private set; } // 지금 머리에 그릴 단계 (0 이면 없음)
    public bool HidesHeadItem => UmbrellaUp || PileShown > 0;
    public void FillPile() => (pile, pilePhase) = (3 * PileStageSec, 0); // 디버그: 바로 다 쌓인 눈

    void PileTick(Brain b, bool feet, double dt, Random rng)
    {
        double t = b.Now;
        if (seen != Sky.Snow) { (pile, pilePhase, PileShown) = (0, 0, 0); return; }
        if (pilePhase == 0 && feet) pile = Math.Min(pile + dt, 3 * PileStageSec);
        if (pilePhase == 0 && PileStage == 3 && b.Mode == Mode.Wander && !b.P.Asleep) // 다 쌓였다: 잠깐 가만히
        {
            (pilePhase, pileUntil) = (1, t + PileStillSec);
            b.Greet("idle", PileStillSec);
        }
        else if (pilePhase == 1 && (t > pileUntil && b.Mode == Mode.Wander || t > pileUntil + 2)) // 도리도리 털기
        {
            (pilePhase, pileUntil) = (2, t + .25);
            b.Greet("giggle", PileShakeSec);
            b.ShakeUntil = t + PileShakeSec;
        }
        else if (pilePhase == 2 && t >= pileUntil) // 눈 조각이 날아가고 처음부터
        {
            BrushOff(b, rng);
            (pile, pilePhase) = (0, 0);
            nextFun = Math.Max(nextFun, t + 8);
        }
        PileShown = feet ? PileStage : 0;
    }

    static void BrushOff(Brain b, Random rng)
    {
        double s = b.Scale / 3.0, y = b.HeadY + 4 * s;
        for (int i = 0; i < 10; i++)
            b.Bits.Add(new Bit { X = b.X + (rng.NextDouble() * 2 - 1) * 22 * s, Y = y - rng.NextDouble() * 10 * s, Vx = (rng.NextDouble() * 2 - 1) * 230 * s, Vy = -(60 + rng.NextDouble() * 170) * s,
                G = 520 * s, Life = .35 + rng.NextDouble() * .35, C = i % 3 == 0 ? Color.FromArgb(200, 220, 246) : Color.FromArgb(250, 252, 255) });
    }

    // 머리 눈 모양: 단계마다 열의 높이 (가운데가 머리 가운데 열). 열마다 머리 윗면을 따라 얹는다
    static readonly string[] PileShape = { "", "11222222211", "1233444443321", "234556666655432" };
    readonly SolidBrush pileSnow = new(Color.FromArgb(252, 253, 255)), pileShade = new(Color.FromArgb(206, 222, 244)), pileEdge = new(Color.FromArgb(120, 148, 190));

    // 볼록이 창에 볼록이 다음에 그린다 (물방울과 같은 자리 잡기). ox·oy: 그림 칸의 왼쪽 위
    public void DrawPile(Graphics g, Sprites sp, string name, int frame, bool flip, int k, int ox, int oy)
    {
        if (PileShown == 0 || !sp.Has(name)) return;
        var h = HeadOf(sp, name, frame, flip);
        string shape = PileShape[PileShown];
        int half = shape.Length / 2;
        int X(int i) => h.Col + (flip ? half - i : i - half);
        int Base(int i) => Math.Clamp(h.Tops[Math.Clamp(X(i), 0, PetArt.Cell - 1)], h.Surface, h.Surface + 4); // 귀가 솟은 열은 머리 높이에
        int Top(int i) => i < 0 || i >= shape.Length ? int.MaxValue : Base(i) - (shape[i] - '0'); // 테두리 칸
        for (int i = 0; i < shape.Length; i++)
        {
            int x = X(i);
            if (x < 0 || x >= PetArt.Cell) continue;
            int bse = Base(i), top = Top(i), side = Math.Min(Math.Max(Top(i - 1), Top(i + 1)), bse + 1);
            g.FillRectangle(pileSnow, ox + x * k, oy + (top + 1) * k, k, (bse - top) * k);
            g.FillRectangle(pileShade, ox + x * k, oy + bse * k, k, k);
            if (side > top + 1) g.FillRectangle(pileEdge, ox + x * k, oy + (top + 1) * k, k, (side - top - 1) * k); // 옆 열보다 높으면 옆 테두리
            g.FillRectangle(pileEdge, ox + x * k, oy + top * k, k, k);
        }
    }

    static bool OnFeet(Brain b) => !b.P.Asleep && b.P.Stage != "egg" && b.Mode is Mode.Wander or Mode.Anim or Mode.GoBite or Mode.Chase
                                   && b.Pose is not ("fly" or "roll" or "flop" or "sleep");

    // ── 알갱이: 시각 t 의 순수 함수 ──
    public struct Drop { public int X, Y; public byte Art, Frame; } // Art: 0~2 눈송이, 3 가까운 빗줄기, 4 튀는 물, 5 먼 빗줄기. (X, Y) 가 그림 가운데

    // 그림 칸 크기 (tools/sprites.py 의 FLAKES·RAIN·SPLASH 와 같아야 한다 — 자가 검사가 본다)
    static readonly (int W, int H)[] Size = { (7, 7), (7, 7), (7, 7), (2, 7), (5, 2), (1, 4) }; // 가까운 빗줄기 2×7 (YELL: 조금 더 크게), 먼 빗줄기 1×4
    static readonly string[] FlakeNames = { "weather/flake0", "weather/flake1", "weather/flake2" };
    public const string RainName = "weather/rain", LeafName = "weather/leaf", WetName = "weather/wet";
    public const int Pics = 9; // 그림 조각 수: 눈송이 셋 × 두 장, 가까운 빗줄기, 튀는 물, 먼 빗줄기

    public static (string Name, int Frame, int W, int H) PicArt(int pic) =>
        pic < 6 ? (FlakeNames[pic / 2], pic % 2, 7, 7) : pic == 6 ? (RainName, 0, 2, 7) : pic == 7 ? (RainName, 1, 5, 2) : (RainName, 2, 1, 4);
    public static int Pic(in Drop d) => d.Art < 3 ? d.Art * 2 + d.Frame : d.Art == 3 ? 6 : d.Art == 4 ? 7 : 8;
    public static Rectangle Src(int pic, int s) { var (_, _, w, h) = PicArt(pic); return new((PetArt.Cell - w) / 2 * s, (PetArt.Cell - h) / 2 * s, w * s, h * s); } // 공통 그림 칸에서 잘라 올 자리
    public static int DropScale(int k) => Math.Max(1, k - 1);
    public static Rectangle Box(in Drop d, int s) { var (w, h) = Size[d.Art]; return new(d.X - w * s / 2, d.Y - h * s / 2, w * s, h * s); }
    public static int Count(Sky sky, int width) => sky == Sky.Snow ? Math.Max(6, width / 140) : Math.Max(10, width / 80); // 볼록이 머리 위 좁은 구역(가로 3칸)이면 눈 6·비 10

    // 0~1 무작위 같은 값 (알갱이 i 의 salt 번째). 같은 값을 넣으면 늘 같다
    static double Hash(int i, int salt)
    {
        unchecked
        {
            uint h = (uint)i * 0x9E3779B1u ^ (uint)salt * 0x85EBCA77u;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return h / 4294967296.0;
        }
    }

    // 둥근 잎 지붕의 높이 (잎 자리 roof 안의 x 에서)
    static double RoofY(Rectangle roof, double x)
    {
        double half = roof.Width / 2.0, dx = (x - roof.Left - half) / half;
        return roof.Bottom - roof.Height * Math.Sqrt(Math.Max(0, 1 - dx * dx));
    }

    // 지금 보일 알갱이를 into 에 채운다 (다시 쓰는 목록: 프레임마다 메모리를 잡지 않는다).
    // area: 내릴 모니터(작업 영역), avoid: 일하는 창 (닿는 알갱이는 빼다), roof: 우산 잎 (그 아래로는 안 떨어지고 잎 위에서 튄다)
    public static void Fill(List<Drop> into, Sky sky, double t, double density, Rectangle area, int k, Rectangle avoid, Rectangle roof)
    {
        into.Clear();
        if (sky == Sky.Clear || density <= 0 || area.Width <= 0 || area.Height <= 0) return;
        bool snow = sky == Sky.Snow;
        int s = DropScale(k), n = Count(sky, area.Width);
        double u = k / 3.0, top = area.Top - 30 * u, floor = area.Bottom, splash = snow ? 0 : 30 * u;
        double span = floor - top + splash;
        for (int i = 0; i < n; i++)
        {
            if ((i + .5) / n > density) break; // 처음·끝엔 몇 개만
            double depth = Hash(i, 8); // 비의 거리감 (YELL 2026-11-13: 앞은 빠르고 뒤는 느리게): 가까운 비는 크고 빠르게, 먼 비는 작고 옅고 느리게
            double speed = snow ? (30 + 16 * Hash(i, 1)) * u : (230 + 310 * depth) * u;
            double run = t * speed + Hash(i, 2) * span;
            double lap = Math.Floor(run / span);
            double y = top + (run - lap * span);
            double x = area.Left + 12 * u + Hash(i, unchecked(3 + (int)(long)lap * 5)) * (area.Width - 24 * u); // 한 바퀴마다 다른 자리
            byte art = (byte)(!snow && depth < .45 ? 5 : 3), frame = 0;
            if (snow)
            {
                x += Math.Sin(t * (.7 + .6 * Hash(i, 4)) + Hash(i, 5) * 6.283) * (8 + 10 * Hash(i, 6)) * u; // 살랑살랑
                x = Math.Clamp(x, area.Left + 4 * s, area.Right - 4 * s);
                art = (byte)(i % 3);
                frame = (byte)((int)Math.Floor(t * 1.5 + Hash(i, 7) * 2) & 1); // 팔랑: 「+」↔「×」
            }
            if (art != 5 && !roof.IsEmpty && x >= roof.Left && x < roof.Right && y + Size[art].H * s / 2.0 >= RoofY(roof, x)) // 먼 비는 잎 뒤로 지나간다
            {
                double ry = RoofY(roof, x);
                if (snow || y - ry >= splash * .5) continue; // 잎에 닿은 건 그 아래로 안 떨어진다
                (art, y) = (4, ry - s);
            }
            else if (!snow && y >= floor) { if (art == 5) continue; (art, y) = (4, floor - s); } // 바닥에 닿아 튄다 (먼 비는 튀지 않고 사라진다)
            var d = new Drop { X = (int)x, Y = (int)y, Art = art, Frame = frame };
            var box = Box(d, s);
            if (box.IntersectsWith(avoid) || box.Right <= area.Left || box.Left >= area.Right) continue;
            into.Add(d);
        }
    }

    // ── 이번 프레임에 그릴 것 (PetForm 이 채우고 Overlay 가 그린다) ──
    public readonly List<Drop> Drops = new(64);
    public int Scale = 2;                   // 알갱이 그림 배율
    public Rectangle Leaf, Stem;            // 우산 잎·줄기 (화면 좌표, 비었으면 없음). Stem 은 줄기가 지나는 곳 전체 (다시 칠할 자리)
    public Point StemFrom, StemTo;          // 줄기: 몸 옆(손)에서 잎 가운데 밑까지 비스듬히 (YELL 이 보낸 개구리 우산 그림처럼)
    public Point LeafAt;                    // 잎 그림 칸의 왼쪽 위
    public int LeafK = 3, LeafFrame;
    public bool LeafFlip;
    public int LeafVersion;                 // 잎 자리·그림이 바뀔 때마다 늘어난다 (가만히 있으면 다시 칠하지 않는다)

    public void Layout(bool shown, double t, Rectangle area, int k, Rectangle avoid)
    {
        Scale = DropScale(k);
        Fill(Drops, shown ? Now : Sky.Clear, t, shown ? Density(t) : 0, area, k, avoid, Leaf);
    }

    // 머리 자리 (그림마다 한 번 잰다): 열마다 가장 위 칸, 머리 가운데 열, 그 열의 꼭대기
    sealed class Head { public int Col, Surface; public readonly int[] Tops = new int[PetArt.Cell]; }
    static readonly ConditionalWeakTable<Bitmap, Head> heads = new();
    static readonly ConditionalWeakTable<Bitmap, Head>.CreateValueCallback measure = Measure;
    static Head HeadOf(Sprites sp, string name, int frame, bool flip) => heads.GetValue(sp.Get(name, frame, 1, flip), measure);

    // 프레임마다 잎 자리를 잡을 때: 그림이 그대로면 지난 값을 쓴다 (Sprites.Get 은 부를 때마다 조금씩 메모리를 잡는다)
    Sprites? headOf;
    string? headName;
    int headFrame;
    bool headFlip;
    Head? head;
    Head HeadNow(Sprites sp, string name, int frame, bool flip)
    {
        if (head == null || headOf != sp || headFrame != frame || headFlip != flip || !string.Equals(headName, name, StringComparison.Ordinal))
            (head, headOf, headName, headFrame, headFlip) = (HeadOf(sp, name, frame, flip), sp, name, frame, flip);
        return head;
    }

    static Head Measure(Bitmap one)
    {
        var h = new Head();
        int w = Math.Min(one.Width, PetArt.Cell), top = one.Height;
        for (int x = 0; x < w; x++)
        {
            h.Tops[x] = one.Height;
            for (int y = 0; y < one.Height; y++) if (one.GetPixel(x, y).A > 0) { h.Tops[x] = y; break; }
        }
        for (int x = w * 3 / 10; x < w * 6 / 10; x++) top = Math.Min(top, h.Tops[x]); // DrawClover·DrawCap 과 같은 방법
        int row = Math.Min(one.Height - 1, top + 4), x0 = w, x1 = 0;
        for (int x = 0; x < w; x++) if (one.GetPixel(x, row).A > 0) (x0, x1) = (Math.Min(x0, x), Math.Max(x1, x));
        h.Col = x1 >= x0 ? (x0 + x1) / 2 : w / 2;
        h.Surface = Math.Min(h.Tops[h.Col], one.Height - 1);
        return h;
    }

    // 그림마다 한 번: 잎 그림이 찬 자리 (배율 1, 모든 장을 합쳐), 날씨 그림이 있는지 (Sprites.Has 는 부를 때마다 메모리를 잡아 프레임마다 묻지 않는다)
    sealed class Art { public Rectangle[] Leaves = new Rectangle[LeafAnchors.Length]; public bool HasLeaf, HasWet; }
    static readonly ConditionalWeakTable<Sprites, Art> arts = new();
    static Art ArtOf(Sprites sp)
    {
        if (arts.TryGetValue(sp, out var a)) return a;
        a = new Art { HasLeaf = sp.Has(LeafName), HasWet = sp.Has(WetName) };
        for (int f = 0; a.HasLeaf && f < Math.Min(sp.Frames(LeafName), 2 * LeafAnchors.Length); f++) // 디자인마다 두 장 (맺힘·떨어짐)
        {
            var o = LeafBody(sp.Get(LeafName, f, 1)); // 끝에 맺힌·떨어지는 물방울은 빼고 잎몸만 (물방울까지 치면 우산이 펫에서 떠 보였다)
            ref var r = ref a.Leaves[f / 2];
            r = r.IsEmpty ? o : Rectangle.Union(r, o);
        }
        arts.AddOrUpdate(sp, a);
        return a;
    }
    public static bool HasLeaf(Sprites sp) => ArtOf(sp).HasLeaf;

    // 잎 그림에서 물방울(빗물 색)을 뺀 잎몸 자리
    static Rectangle LeafBody(Bitmap bmp)
    {
        int x0 = bmp.Width, y0 = bmp.Height, x1 = -1, y1 = -1;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.A == 0 || (c.R, c.G, c.B) is (150, 196, 240) or (235, 248, 255)) continue;
                (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
            }
        return x1 < 0 ? Rectangle.Empty : Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
    }

    // 나뭇잎 우산 네 가지 (sprites.py UMBRELLAS — 시안 1 긴 타원 잎·6 몬스테라·9 토란잎·17 비스듬한 연잎): 줄기가 붙는 자리, 머리 위에 올 가운데, 밑동이 오른쪽인가
    static readonly (int StemX, int StemY, int MidX, bool Right)[] LeafAnchors = { (7, 10, 20, false), (10, 12, 20, false), (10, 10, 20, false), (27, 13, 20, true) };
    public int LeafDesign;
    public const int LeafSink = 3; // 잎을 머리 꼭대기보다 몇 칸 아래로 (머리 뒤로 살짝)
    public int StemSide = -1; // 줄기가 나오는 쪽 (몸 옆, 화면 기준 -1 왼쪽 / 1 오른쪽)

    // 우산 잎 자리. ox·oy: 볼록이 그림 칸의 왼쪽 위 (화면 좌표). 잎은 덮는 열들의 가장 위 칸보다 한 칸 위에 (귀·뿔과 안 겹친다),
    // 줄기는 머리 가운데 열을 따라 머리 꼭대기 밑까지 — 잎·줄기는 볼록이 창 뒤(Overlay)에 그려서 머리 뒤로 들어간 것처럼 보인다
    public void PlaceLeaf(Sprites sp, string? name, int frame, bool flip, int k, int ox, int oy, double t)
    {
        var was = (Leaf, Stem, LeafFrame, LeafFlip, LeafK);
        Leaf = Stem = Rectangle.Empty;
        Place(sp, name, frame, flip, k, ox, oy, t);
        if (was != (Leaf, Stem, LeafFrame, LeafFlip, LeafK)) LeafVersion++;
    }

    void Place(Sprites sp, string? name, int frame, bool flip, int k, int ox, int oy, double t)
    {
        var art = ArtOf(sp);
        if (!UmbrellaUp || name == null || !art.HasLeaf) return;
        var h = HeadNow(sp, name, frame, flip);
        int design = Math.Clamp(LeafDesign, 0, LeafAnchors.Length - 1);
        var (stemX, stemY, midX, right) = LeafAnchors[design];
        var r = art.Leaves[design];
        if (r.IsEmpty) r = art.Leaves[0];
        int rx = flip ? PetArt.Cell - r.Right : r.X, sx = flip ? PetArt.Cell - 1 - midX : midX; // 잎 가운데를 머리 위에
        int cx = h.Col - sx, high = PetArt.Cell;
        for (int x = Math.Max(0, cx + rx); x < Math.Min(PetArt.Cell, cx + rx + r.Width); x++) high = Math.Min(high, h.Tops[x]);
        int cy = high - 1 - r.Bottom + LeafSink, stemTop = cy + stemY; // 잎은 머리를 살짝 가려도 된다 (YELL 2026-11-13: 손잡이를 짧게) — 잎 아래 가장자리가 머리 뒤로 숨는다
        LeafAt = new Point(ox + cx * k, oy + cy * k);
        Leaf = new Rectangle(LeafAt.X + rx * k, LeafAt.Y + r.Y * k, r.Width * k, r.Height * k);
        int sd = right != flip ? 1 : -1, ex = h.Col; // 잎 밑동이 있는 쪽 몸 옆에서 든다
        StemSide = sd;
        while (ex + sd >= 0 && ex + sd < PetArt.Cell && h.Tops[ex + sd] < PetArt.Cell) ex += sd;
        int fromY = Math.Min(PetArt.Cell - 5, (h.Surface + 37) / 2 + 2);
        StemFrom = new Point(ox + (ex + sd) * k, oy + fromY * k);
        StemTo = new Point(LeafAt.X + (flip ? PetArt.Cell - 1 - stemX : stemX) * k, oy + stemTop * k);
        Stem = Rectangle.Inflate(Rectangle.FromLTRB(Math.Min(StemFrom.X, StemTo.X), Math.Min(StemFrom.Y, StemTo.Y), Math.Max(StemFrom.X, StemTo.X) + 2 * k, Math.Max(StemFrom.Y, StemTo.Y) + 2 * k), 5 * k, 6 * k); // 휜 줄기·손잡이까지
        (LeafK, LeafFlip, LeafFrame) = (k, flip, design * 2 + (int)(t / .8) % 2);
    }

    public static int WetFrame(double t) => (int)(t / .45) % 2;

    // 비 맞은 물방울: 머리 꼭대기 가운데에 (볼록이 창에 볼록이 다음에 그린다). ox·oy: 그림 칸의 왼쪽 위
    public void DrawWet(Graphics g, Sprites sp, string name, int frame, bool flip, int k, int ox, int oy, double t)
    {
        if (!Soaked || !ArtOf(sp).HasWet || !sp.Has(name)) return;
        var h = HeadOf(sp, name, frame, flip);
        g.DrawImageUnscaled(sp.Get(WetName, WetFrame(t), k, flip), ox + (h.Col - 20) * k, oy + (h.Surface - 20) * k);
    }

    // ── 자가 검사 ──
    public static string Check()
    {
        static void Expect(bool ok, string what) { if (!ok) throw new Exception("자가 검사 실패: 날씨 — " + what); }
        var rng = new Random(7);

        // 언제: 눈은 11~2월만, 하루 몇 번
        int[] snow = new int[13], rain = new int[13];
        int days = 0, most = 0, total = 0;
        for (int m = 1; m <= 12; m++)
        {
            var w = new Weather();
            double now = 0;
            for (int d = 0; d < 20; d++, days++)
            {
                var morning = new DateTime(2026, m, 1 + d, 9, 0, 0);
                int today = 0;
                for (int min = 0; min < 9 * 60; min++, now += 60)
                    if (w.Tick(now, morning.AddMinutes(min), true, rng) == "start")
                    {
                        today++;
                        if (w.Now == Sky.Snow) snow[m]++; else rain[m]++;
                        Expect(w.Until - w.From is >= MinSec and <= MaxSec, "한 번에 몇 분");
                    }
                now += 15 * 3600; // 밤사이 꺼져 있다
                (most, total) = (Math.Max(most, today), total + today);
            }
        }
        for (int m = 3; m <= 10; m++) Expect(snow[m] == 0 && rain[m] > 0, $"{m}월은 비만 온다 (눈 {snow[m]}, 비 {rain[m]})");
        foreach (int m in new[] { 11, 12, 1, 2 }) Expect(rain[m] == 0 && snow[m] > 0, $"{m}월은 눈만 온다 (눈 {snow[m]}, 비 {rain[m]})");
        double perDay = total / (double)days;
        Expect(most <= MaxPerDay && perDay is >= 1.5 and <= 4, $"하루 몇 번만 ({perDay:0.0}번, 많아야 {most}번)");

        // 게임·잠에는 안 온다
        var awake = new Pet { Stage = "child" };
        Expect(Quiet(true, awake, false) && Quiet(false, new Pet { Stage = "child", Asleep = true }, false) && Quiet(false, new Pet(), false) && !Quiet(false, awake, false),
            "게임 중·잠·알이면 멈춘다");
        var never = new Weather();
        for (int min = 0; min < 3000; min++) Expect(never.Tick(min * 60, new DateTime(2026, 1, 5, 9, 0, 0).AddMinutes(min), false, rng) == null, "한가하지 않으면 시작하지 않는다");

        // 알갱이: 일하는 창 위에는 하나도 없다, 우산 잎 아래로는 안 떨어진다, 모니터 안
        var area = new Rectangle(0, 0, 1920, 1040);
        var avoid = new Rectangle(700, 200, 900, 600);
        var roof = new Rectangle(300, 880, 96, 36);
        var list = new List<Drop>();
        int seen = 0, splashes = 0;
        foreach (var sky in new[] { Sky.Snow, Sky.Rain })
            for (double t = 0; t < 400; t += .37)
            {
                Fill(list, sky, t, 1, area, 3, avoid, roof);
                seen += list.Count;
                foreach (var d in list)
                {
                    var box = Box(d, DropScale(3));
                    Expect(!box.IntersectsWith(avoid), $"일하는 창 위에 알갱이가 없다 ({box})");
                    Expect(box.Right > area.Left && box.Left < area.Right, "모니터 안에만");
                    Expect(d.Art == 5 || d.X < roof.Left || d.X >= roof.Right || box.Top < roof.Bottom - 2 || d.Y < roof.Top, "우산 잎 아래로는 안 떨어진다 (먼 비는 잎 뒤로 지나간다)");
                    if (d.Art == 4 && d.Y < roof.Bottom) splashes++;
                }
            }
        Expect(seen > 0 && splashes > 0, "잎 위에서 빗물이 튄다");
        Fill(list, Sky.Rain, 100, 1, area, 3, Rectangle.Empty, Rectangle.Empty);
        int full = list.Count;
        Fill(list, Sky.Rain, 100, .3, area, 3, Rectangle.Empty, Rectangle.Empty);
        Expect(full == Count(Sky.Rain, 1920) && list.Count < full / 2 && list.Count > 0, $"처음·끝엔 몇 줄기만 ({list.Count}/{full})");
        Expect(Count(Sky.Snow, 1920) <= 16 && Count(Sky.Rain, 1920) <= 26, "조금만 내린다");

        // 비가 오면 셋 중 하나
        var covers = new int[4];
        for (int i = 0; i < 300; i++) covers[(int)PickCover(rng)]++;
        Expect(covers[0] == 0 && covers.Skip(1).All(c => c > 60), $"우산·맞기·비 피하기 모두 고른다 ({string.Join("/", covers.Skip(1))})");

        // 반응은 비와 함께 시작하고 끝난다
        var screen = new Rectangle(0, 0, 1920, 1040);
        var july = new DateTime(2026, 7, 1, 10, 0, 0);
        Brain Fresh(Rectangle? win)
        {
            var b = new Brain(new Pet { Stage = "child", Full = 95, Mood = 95, Energy = 95 }, new Tuning(), screen) { X = 300, Y = 1000 };
            if (win is { } r) { b.Windows = () => new List<Win> { new(new IntPtr(1), r, false) }; b.Probe = _ => new WinState(true, false, r); }
            return b;
        }
        int Run(Weather w, Brain b, double sec, Func<bool> watch, bool shown = true)
        {
            int hits = 0;
            for (double end = b.Now + sec; b.Now < end;)
            {
                b.Update(1 / 30.0, false);
                w.Tick(b.Now, july, true, rng);
                w.React(b, shown && w.On, rng);
                if (watch()) hits++;
            }
            return hits;
        }

        var wu = new Weather();
        var bu = Fresh(null);
        wu.Begin(Sky.Rain, bu.Now, rng, Cover.Umbrella, 60);
        int up = Run(wu, bu, 59, () => wu.UmbrellaUp);
        Expect(up > 59 * 30 * .6, $"우산: 비가 오는 동안 잎을 쓴다 ({up}프레임)");
        Expect(Run(wu, bu, .2, () => wu.UmbrellaUp, shown: false) == 0, "우산: 게임·잠으로 가려지면 바로 내린다");
        Run(wu, bu, 2, () => false);
        Expect(!wu.On && Run(wu, bu, 20, () => wu.UmbrellaUp) == 0, "우산: 그치면 내린다");

        var ww = new Weather();
        var bw = Fresh(null);
        ww.Begin(Sky.Rain, bw.Now, rng, Cover.Wet, 60);
        int sad = Run(ww, bw, 59, () => bw.Pose == "sad"), soaked = 0;
        Expect(bw.IdlePose == "sad" && sad > 30, $"맞기: 시무룩하게 쉰다 ({sad}프레임)");
        Expect(ww.Soaked || Run(ww, bw, .5, () => ww.Soaked) > 0, "맞기: 머리에 물방울");
        Run(ww, bw, 2, () => false);
        soaked = Run(ww, bw, 20, () => ww.Soaked || bw.Pose == "sad");
        Expect(!ww.On && bw.IdlePose == null && soaked == 0, "맞기: 그치면 물방울도 시무룩도 끝");

        var win = new Rectangle(500, 300, 900, 600);
        var ws = new Weather();
        var bs = Fresh(win);
        ws.Begin(Sky.Rain, bs.Now, rng, Cover.Shelter, 220);
        Run(ws, bs, 40, () => false);
        Expect(bs.Mode == Mode.Hiding && bs.Behind && bs.Sheltering, $"비 피하기: 창 뒤로 들어간다 ({bs.Mode})");
        Run(ws, bs, new Tuning().HideStayMaxSec + 20, () => false);
        Expect(bs.Mode == Mode.Hiding && bs.Behind, "비 피하기: 비가 오는 동안은 숨바꼭질 시간이 지나도 나오지 않는다");
        Run(ws, bs, ws.Until - bs.Now + .1, () => false);
        Run(ws, bs, 8, () => false);
        Expect(!ws.On && !bs.Sheltering && bs.Mode != Mode.Hiding && !bs.Behind, $"비 피하기: 그치면 나온다 ({bs.Mode})");

        var wn = new Weather();
        var bn = Fresh(null);
        wn.Begin(Sky.Rain, bn.Now, rng, Cover.Shelter, 30);
        Run(wn, bn, 3, () => false);
        Expect(wn.Reaction == Cover.Umbrella && wn.UmbrellaUp, "비 피하기: 숨을 창이 없으면 우산");

        var wg = new Weather();
        var bg = Fresh(win);
        wg.Begin(Sky.Rain, bg.Now, rng, Cover.Shelter, 120);
        Run(wg, bg, 30, () => false);
        bg.StartPuppet(); // 게임 시작
        wg.React(bg, false, rng);
        wg.Layout(false, bg.Now, screen, 3, Rectangle.Empty);
        Expect(!bg.Sheltering && wg.Drops.Count == 0 && !wg.UmbrellaUp, "게임 중엔 비도 반응도 멈춘다");

        var wsn = new Weather();
        var bsn = Fresh(null);
        wsn.Begin(Sky.Snow, bsn.Now, rng);
        bool liked = false;
        Run(wsn, bsn, 60, () => liked |= bsn.Emoting && bsn.EmoteName is "heart" or "note");
        Expect(liked && !wsn.UmbrellaUp && !wsn.Soaked, "눈: 좋아한다 (하트·음표)");

        // 머리에 쌓이는 눈: 세 단계로 쌓이고 → 가만히 → 도리도리 털고 → 처음부터
        var wp = new Weather();
        var bp = Fresh(null);
        wp.Begin(Sky.Snow, bp.Now, rng, Cover.None, 600);
        int maxStage = 0, order = 0, cycles = 0, bits = 0;
        var stages = new HashSet<int>();
        Run(wp, bp, 240, () =>
        {
            stages.Add(wp.PileShown);
            maxStage = Math.Max(maxStage, wp.PileStage);
            if (order == 0 && wp.PileStage == 3) order = 1;                                                                  // 다 쌓였다
            else if (order == 1 && bp.Mode == Mode.Anim && bp.Pose == "idle" && wp.PileShown == 3) order = 2;              // 가만히
            else if (order == 2 && bp.Now < bp.ShakeUntil && bp.Pose == "giggle") order = 3;                              // 도리도리
            else if (order == 3 && wp.PileStage == 0) { order = 0; cycles++; bits = Math.Max(bits, bp.Bits.Count); }    // 털어 냈다
            return false;
        });
        Expect(stages.IsSupersetOf(new[] { 1, 2, 3 }) && maxStage == 3, $"눈: 머리에 세 단계로 쌓인다 ({string.Join(",", stages)})");
        Expect(cycles >= 2 && bits >= 5, $"눈: 다 쌓이면 가만히 → 도리도리 → 눈 조각이 날아가고 처음부터, 눈이 오는 동안 되풀이 ({cycles}번, 조각 {bits})");
        wp.FillPile();
        Run(wp, bp, 15, () => false);
        Expect(wp.PileStage < 3, "디버그: 바로 다 쌓인 눈도 털어 낸다");

        int Grown(Weather w, Brain b, double sec, bool shown = true) { int most = 0; Run(w, b, sec, () => { most = Math.Max(most, w.PileShown + w.PileStage); return false; }, shown); return most; }
        foreach (var c in new[] { Cover.Umbrella, Cover.Wet, Cover.Shelter })
        {
            var wr = new Weather();
            var br = Fresh(c == Cover.Shelter ? win : null);
            wr.Begin(Sky.Rain, br.Now, rng, c, 90);
            Expect(Grown(wr, br, 80) == 0, $"비({c})에는 눈이 안 쌓인다");
        }
        var wq = new Weather();
        var bq = Fresh(null);
        wq.Begin(Sky.Snow, bq.Now, rng, Cover.None, 200);
        Expect(Grown(wq, bq, 80, shown: false) == 0, "게임·잠으로 멈춘 동안엔 눈이 안 쌓인다");
        var wh = new Weather();
        var bh = Fresh(win);
        Expect(bh.TryHide(), "숨을 창");
        Run(wh, bh, 15, () => false);
        wh.Begin(Sky.Snow, bh.Now, rng, Cover.None, 200);
        bool hidGrow = false;
        Run(wh, bh, 20, () => hidGrow |= bh.Mode is Mode.Hiding or Mode.SlipIn && (wh.PileStage > 0 || wh.PileShown > 0));
        Expect(bh.Mode == Mode.Hiding && !hidGrow, "창 뒤에 숨어 있으면 눈이 안 쌓인다");

        // 그림: 칸 크기가 맞다 (알갱이 다시 칠할 자리가 그림을 다 덮는다)
        var sp = Sprites.Embedded();
        for (int p = 0; p < Pics; p++)
        {
            var (name, f, _, _) = PicArt(p);
            Expect(sp.Has(name), $"그림 {name}");
            var o = Ui.Opaque(sp.Get(name, f, 1), 0);
            Expect(Src(p, 1).Contains(o), $"그림 {name}#{f} 가 칸({Src(p, 1)}) 밖으로 나간다 ({o})");
        }
        Expect(sp.Has(LeafName) && sp.Has(WetName), "우산 잎·물방울 그림");

        // 우산 잎은 어느 모습에서도 볼록이와 안 겹치고 줄기가 머리에 닿는다
        var art = PetArt.Embedded;
        foreach (var id in new[] { "hamster", "deer", "drake", "unicorn" })
        {
            var ps = Sprites.Drawn(art, id, null);
            var wl = new Weather { UmbrellaUp = true };
            for (int design = 0; design < LeafAnchors.Length; design++) // 우산 네 가지 모두
            foreach (var form in Showcase.Forms)
                foreach (bool flip in new[] { false, true })
                {
                    wl.LeafDesign = design;
                    string name = $"{form}/0/idle";
                    wl.PlaceLeaf(ps, name, 0, flip, 1, 0, 0, 0);
                    var one = ps.Get(name, 0, 1, flip);
                    var h = HeadOf(ps, name, 0, flip);
                    Expect(!wl.Leaf.IsEmpty && wl.Stem.Bottom > h.Surface && wl.Stem.Top < h.Surface, $"{id} {form}: 줄기가 머리에 닿는다");
                    var (eyeTop, _) = PetArt.Embedded.EyeRows(id, form);
                    Expect(wl.Leaf.Bottom <= eyeTop, $"{id} {form} 우산 {design}: 잎은 머리를 살짝만 가리고 눈은 안 가린다 (잎 아래 {wl.Leaf.Bottom}, 눈 위 {eyeTop})"); // YELL 2026-11-13: 살짝 가려도 괜찮다
                }
        }

        // 다시 칠할 자리가 모자라지 않다 (지나간 빗줄기가 화면에 남지 않는다)
        var (bad, frames) = Repaint();
        Expect(bad == 0, $"다시 칠할 자리가 모자라 지나간 알갱이가 남는다 ({bad}번)");

        { // 눈·비엔 얌전히 (YELL 2026-11-13): 옆·아래로만, 몸 크기 200% 안
            var calm = new Brain(new Pet { Stage = "child", Full = 95, Mood = 95, Energy = 95 }, new Tuning(), new Rectangle(0, 0, 1920, 1040)) { X = 900, Y = 600 };
            calm.Calm = true;
            for (int i = 0; i < 300; i++) // 여러 번 움직여도 처음 자리에서 양쪽 1칸 안 (몸 크기 200%)
            {
                var (cx, cy) = calm.CalmRoam();
                Expect(cy == 600 && Math.Abs(cx - 900) <= calm.CellPx + 1e-6, $"눈·비엔 옆으로만, 처음 자리에서 몸 200% 안 ({cx - 900:F0},{cy - 600:F0})");
                calm.X = cx;
            }
        }
        return $"날씨: 하루 {perDay:0.0}번(많아야 {most}), 11~2월 눈만 ({snow[11] + snow[12] + snow[1] + snow[2]}번)·3~10월 비만 ({rain.Sum()}번), 일하는 창 위 0알, " +
               $"우산·맞기·비 피하기 {string.Join("/", covers.Skip(1))}, 그치면 반응도 끝, 머리 눈 세 단계 → 가만히 → 털기 4분에 {cycles}번 (비·숨기·게임엔 안 쌓임), {frames}프레임 다시 칠하기 맞음";
    }

    // 실제 창처럼 지난·이번 프레임 자리만(64px 칸으로) 지우고 다시 그린 화면이 통째로 새로 그린 화면과 같은지 (비·우산, 볼록이가 걸어 다닌다)
    static (int Bad, int Frames) Repaint()
    {
        var area = new Rectangle(0, 0, 960, 540);
        var sp = Sprites.Embedded();
        var b = new Brain(new Pet { Stage = "child", Full = 95, Mood = 95, Energy = 95 }, new Tuning(), area) { X = 300, Y = 520 };
        var w = new Weather();
        var rng = new Random(3);
        w.Begin(Sky.Rain, 0, rng, Cover.Umbrella, 600);
        using var ov = new Overlay(b, () => sp, area) { Sky = () => w };
        var back = Color.FromArgb(150, 160, 170);
        using var screen = new Bitmap(area.Width, area.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var fresh = new Bitmap(area.Width, area.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var gs = Graphics.FromImage(screen);
        using var gf = Graphics.FromImage(fresh);
        using var clip = new Region();
        gs.Clear(back);
        var last = new List<Rectangle>();
        var cells = new HashSet<(int, int)>();
        int bad = 0, frames = 150;
        b.GoTo(800, 520);
        for (int i = 0; i < frames; i++)
        {
            b.Update(1 / 30.0, false);
            w.React(b, true, rng);
            w.PlaceLeaf(sp, b.SpriteName(), i / 4, b.Dir < 0, b.Scale, (int)b.X - b.CellPx / 2, (int)b.Y - b.CellPx, b.Now);
            w.Layout(true, b.Now, area, b.Scale, Rectangle.Empty);
            var now = ov.Dirty();
            cells.Clear();
            clip.MakeEmpty();
            foreach (var list in new[] { last, now })
                foreach (var r0 in list)
                {
                    var r = Rectangle.Intersect(r0, new Rectangle(0, 0, area.Width, area.Height));
                    if (r.IsEmpty) continue;
                    for (int y = r.Top / 64; y <= (r.Bottom - 1) / 64; y++)
                        for (int x = r.Left / 64; x <= (r.Right - 1) / 64; x++)
                            if (cells.Add((x, y))) clip.Union(new Rectangle(x * 64, y * 64, 64, 64));
                }
            last = now.ToList();
            gs.Clip = clip;
            gs.Clear(back);
            ov.Render(gs);
            if (i % 10 != 9) continue;
            gf.Clear(back);
            ov.Render(gf);
            if (!Same(screen, fresh)) bad++;
        }
        return (bad, frames);
    }

    static bool Same(Bitmap a, Bitmap b)
    {
        var r = new Rectangle(0, 0, a.Width, a.Height);
        var da = a.LockBits(r, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var db = b.LockBits(r, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            int[] ra = new int[r.Width], rb = new int[r.Width];
            for (int y = 0; y < r.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(da.Scan0 + y * da.Stride, ra, 0, ra.Length);
                System.Runtime.InteropServices.Marshal.Copy(db.Scan0 + y * db.Stride, rb, 0, rb.Length);
                if (!ra.AsSpan().SequenceEqual(rb)) return false;
            }
            return true;
        }
        finally { a.UnlockBits(da); b.UnlockBits(db); }
    }

    // ── 속도 재기 (화면을 띄우지 않는다): Bolok.exe --weather-bench ──
    // 1920×1080 모니터, 오른쪽 절반은 일하는 창. 프레임마다 규칙(반응·잎 자리·알갱이) + 다시 칠할 자리 + 그 자리만 다시 그리기를
    // 날씨 없이 / 눈 / 비(우산) / 비(맞기) 로 잰다 (30초 데운 뒤 60초). 새로 잡는 메모리도 잰다 (재는 틀이 잡는 것은 뺀다)
    public static string Bench()
    {
        var area = new Rectangle(0, 0, 1920, 1080);
        var sp = Sprites.Embedded();
        var active = new Win(new IntPtr(9), new Rectangle(960, 0, 960, 1040), false);
        var lines = new List<string>();
        foreach (var (label, sky, cover) in new[] { ("날씨 없음", Sky.Clear, Cover.None), ("눈", Sky.Snow, Cover.None), ("비·우산", Sky.Rain, Cover.Umbrella), ("비·맞기", Sky.Rain, Cover.Wet) })
        {
            var b = new Brain(new Pet { Stage = "child", Full = 95, Mood = 95, Energy = 95 }, new Tuning(), area) { X = 500, Y = 1060, Foreground = () => active };
            var w = new Weather();
            var rng = new Random(5);
            if (sky != Sky.Clear) w.Begin(sky, 0, rng, cover, 3600);
            using var ov = new Overlay(b, () => sp, area) { Sky = () => w };
            using var bmp = new Bitmap(area.Width, area.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(bmp);
            using var clip = new Region();
            var last = new List<Rectangle>();
            var cells = new HashSet<(int, int)>();
            long tRule = 0, tDirty = 0, tDraw = 0, bytes = 0, area2 = 0, worst = 0;
            int frames = 0;
            for (int i = 0; i < 2700; i++)
            {
                b.Update(1 / 30.0, false);
                string name = b.SpriteName();
                bool flip = b.Dir < 0;
                int ox = (int)b.X - b.CellPx / 2, oy = (int)b.Y - b.CellPx;
                long a0 = GC.GetAllocatedBytesForCurrentThread(), t0 = Stopwatch.GetTimestamp();
                w.React(b, w.On, rng);
                w.PlaceLeaf(sp, name, i / 4, flip, b.Scale, ox, oy, b.Now);
                w.Layout(w.On, b.Now, b.Area, b.Scale, b.Active?.R ?? Rectangle.Empty);
                long t1 = Stopwatch.GetTimestamp();
                var now = ov.Dirty();
                long t2 = Stopwatch.GetTimestamp(), a1 = GC.GetAllocatedBytesForCurrentThread();
                cells.Clear();
                clip.MakeEmpty();
                foreach (var list in new[] { last, now })
                    foreach (var r0 in list)
                    {
                        var r = Rectangle.Intersect(r0, area);
                        if (r.IsEmpty) continue;
                        for (int y = r.Top / 64; y <= (r.Bottom - 1) / 64; y++)
                            for (int x = r.Left / 64; x <= (r.Right - 1) / 64; x++)
                                if (cells.Add((x, y))) clip.Union(new Rectangle(x * 64, y * 64, 64, 64));
                    }
                last = now.ToList();
                g.Clip = clip;
                long a2 = GC.GetAllocatedBytesForCurrentThread(), t3 = Stopwatch.GetTimestamp();
                g.Clear(Color.Magenta);
                ov.Render(g);
                long t4 = Stopwatch.GetTimestamp(), a3 = GC.GetAllocatedBytesForCurrentThread();
                if (i < 900) continue; // 처음 30초는 뺀다: JIT, 자세·장마다 처음 한 번 그림 만들기·머리 재기
                frames++;
                (tRule, tDirty, tDraw) = (tRule + t1 - t0, tDirty + t2 - t1, tDraw + t4 - t3);
                worst = Math.Max(worst, t1 - t0 + t2 - t1 + t4 - t3);
                bytes += a1 - a0 + a3 - a2;
                area2 += cells.Count;
            }
            double Us(long ticks) => ticks * 1e6 / Stopwatch.Frequency / Math.Max(1, frames);
            lines.Add($"{label}: 규칙 {Us(tRule):0.0}µs, 칠할 자리 {Us(tDirty):0.0}µs, 다시 그리기 {Us(tDraw):0.0}µs (합 {Us(tRule + tDirty + tDraw):0.0}µs), 가장 느린 {worst * 1e3 / Stopwatch.Frequency:0.00}ms, " +
                      $"메모리 {bytes / Math.Max(1, frames)}B/프레임, 다시 칠한 넓이 {area2 * 64.0 * 64 / Math.Max(1, frames) / 1000:0}천px²/프레임, 알갱이 {w.Drops.Count}개");
        }
        return string.Join("\n", lines);
    }

    // ── 화면 없이 그려 보기: Bolok.exe --weather-look 그림.png ──
    // 위: 모니터 반쪽 크기(960×540) 두 장 — 비(우산)·눈, 오른쪽 흰 창이 일하는 창 (그 위엔 안 내린다).
    // 아래: 종·모습마다 머리 눈 1·2·3단계(3단계는 가만히) / 털기 / 우산 / 맞기 / 비 피하기(창 뒤에서 빼꼼), 맨 위 줄은 날씨 그림을 크게
    public static void Look(string path)
    {
        var art = PetArt.Embedded;
        const int k = 3, pw = 210, ph = 220;
        string[] species = { "hamster", "deer" }, forms = { "baby", "child", "adult_chubby", "adult_grumpy" };
        string[] kinds = { "pile1", "pile2", "pile3", "shake", "umbrella", "wet", "shelter" };
        var desk = Color.FromArgb(118, 138, 160);
        using var winBrush = new SolidBrush(Color.FromArgb(246, 247, 250));
        using var barBrush = new SolidBrush(Color.FromArgb(96, 130, 190));
        using var edge = new Pen(Color.FromArgb(70, 80, 95));
        void Window(Graphics g, Rectangle r) { g.FillRectangle(winBrush, r); g.FillRectangle(barBrush, r.X, r.Y, r.Width, 22); g.DrawRectangle(edge, r); }

        int rows = species.Length * forms.Length, wide = Math.Max(960 * 2 + 30, kinds.Length * (pw + 10) + 10);
        using var bmp = new Bitmap(wide, 130 + 550 + rows * (ph + 10) + 10);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        var common = Sprites.Embedded();

        // 그림 크게: 눈송이 셋 × 두 장, 빗줄기, 튀는 물, 잎 두 장, 물방울 두 장
        int at = 10;
        (g.InterpolationMode, g.PixelOffsetMode) = (System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor, System.Drawing.Drawing2D.PixelOffsetMode.Half);
        void Big(string name, int f, int z, int w0)
        {
            using var back = new SolidBrush(desk);
            g.FillRectangle(back, at, 10, w0, 110);
            g.DrawImage(common.Get(name, f, 1), new Rectangle(at + w0 / 2 - 20 * z, 65 - 20 * z, 40 * z, 40 * z));
            at += w0 + 6;
        }
        for (int i = 0; i < 6; i++) Big(PicArt(i).Name, i % 2, 6, 60);
        Big(RainName, 0, 6, 40); Big(RainName, 1, 6, 50);
        Big(LeafName, 0, 3, 120); Big(LeafName, 1, 3, 120); Big(WetName, 0, 3, 90); Big(WetName, 1, 3, 90);
        (g.InterpolationMode, g.PixelOffsetMode) = (System.Drawing.Drawing2D.InterpolationMode.Default, System.Drawing.Drawing2D.PixelOffsetMode.Default);

        // 모니터 반쪽 두 장
        var half = new Rectangle(0, 0, 960, 540);
        var work = new Rectangle(560, 60, 380, 420);
        foreach (var (sky, x0) in new[] { (Sky.Rain, 10), (Sky.Snow, 980) })
        {
            var sp = Sprites.Drawn(art, "hamster", null);
            var b = new Brain(new Pet { Stage = "child", Full = 95, Mood = 95, Energy = 95 }, new Tuning(), half) { X = 220, Y = 530 };
            var w = new Weather();
            var rng = new Random(4);
            w.Begin(sky, 0, rng, Cover.Umbrella, 600);
            b.Now = 100;
            w.React(b, true, rng);
            string name = sky == Sky.Snow ? "child/0/happy" : "child/0/walk";
            int ox = (int)b.X - b.CellPx / 2, oy = (int)b.Y - b.CellPx;
            w.PlaceLeaf(sp, name, 1, false, k, ox, oy, 100);
            w.Layout(true, 100, half, k, work);
            using var ov = new Overlay(b, () => sp, half) { Sky = () => w };
            using var frame = new Bitmap(half.Width, half.Height);
            using (var fg = Graphics.FromImage(frame))
            {
                fg.Clear(desk);
                Window(fg, work);
                ov.Render(fg);
                fg.DrawImageUnscaled(sp.Get(name, 1, k), ox, oy);
            }
            g.DrawImage(frame, x0, 130);
        }

        // 종·모습마다
        var panel = new Rectangle(0, 0, pw, ph);
        int row = 0;
        foreach (var id in species)
        {
            var sp = Sprites.Drawn(art, id, null);
            foreach (var form in forms)
            {
                for (int c = 0; c < kinds.Length; c++)
                {
                    string kind = kinds[c];
                    var b = new Brain(new Pet { Stage = form, Full = 95, Mood = 95, Energy = 95 }, new Tuning(), panel) { X = pw / 2, Y = ph - 10 };
                    var w = new Weather();
                    var rng = new Random(11 + c);
                    bool snowy = kind.StartsWith("pile") || kind == "shake";
                    w.Begin(snowy ? Sky.Snow : Sky.Rain, 0, rng, kind switch { "umbrella" => Cover.Umbrella, "wet" => Cover.Wet, _ => Cover.Umbrella }, 600);
                    b.Now = 60 + c * 7 + row * 3;
                    if (kind != "shelter") w.React(b, true, rng);
                    if (kind.StartsWith("pile")) { w.pile = (kind[4] - '0') * PileStageSec; w.lastT = b.Now; w.React(b, true, rng); }
                    if (kind == "pile1") b.Emote("heart", 9);
                    else if (kind is "pile2" or "pile3" or "shake") b.EmoteUntil = 0; // 머리 눈이 잘 보이게
                    int shake = 0;
                    if (kind == "shake") // 터는 중: 눈 조각이 날아간다 (볼록이는 옆으로 흔들린 자리)
                    {
                        BrushOff(b, rng);
                        foreach (var bit in b.Bits) { bit.X += bit.Vx * .12; bit.Y += bit.Vy * .12 + bit.G * .0072; }
                        shake = 5 * k / 3;
                    }
                    string pose = kind switch { "pile1" => "happy", "pile2" => "walk", "shake" => "giggle", "wet" => "sad", "shelter" => "peek", _ => "idle" };
                    string name = $"{form}/0/{pose}";
                    bool flip = kind is "shelter" or "pile2";
                    var win = new Rectangle(pw / 2, 40, pw / 2 + 20, ph - 34);
                    int ox = (int)b.X - b.CellPx / 2 + shake, oy = (int)b.Y - b.CellPx;
                    w.PlaceLeaf(sp, name, 0, flip, k, ox, oy, b.Now);
                    w.Layout(true, b.Now, panel, k, Rectangle.Empty);
                    using var ov = new Overlay(b, () => sp, panel) { Sky = () => w };
                    using var frame = new Bitmap(pw, ph);
                    using (var fg = Graphics.FromImage(frame))
                    {
                        fg.Clear(desk);
                        if (kind == "shelter") { fg.DrawImageUnscaled(sp.Get(name, 0, k, flip), ox, oy); Window(fg, win); } // 창 뒤에 숨어 빼꼼 (창이 몸을 가린다)
                        ov.Render(fg);
                        if (kind != "shelter") fg.DrawImageUnscaled(sp.Get(name, 0, k, flip), ox, oy);
                        w.DrawWet(fg, sp, name, 0, flip, k, ox, oy, b.Now);
                        w.DrawPile(fg, sp, name, 0, flip, k, ox, oy);
                    }
                    g.DrawImage(frame, 10 + c * (pw + 10), 130 + 550 + row * (ph + 10));
                }
                row++;
            }
        }
        bmp.Save(path);
    }
}
