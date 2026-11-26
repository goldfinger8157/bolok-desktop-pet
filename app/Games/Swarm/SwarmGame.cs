using System.Text.Json;

namespace Bolok;

// 버그 떼 모듈: 앱에 붙이기(판·조작·볼록이), 그리기, 기록, 디버그 명령. 규칙은 Swarm.cs, 무기 값은 SwarmArms.cs, 무기 그림은 SwarmArt.cs, 자가 검사·장면·속도는 SwarmCheck.cs
// 판은 볼록이를 가운데 두고 220×150 칸(배율 3 이면 660×450)만 쓴다. 모서리 표시만 그리고 바탕은 작업 화면 그대로
// 조작: 방향키로 움직이고 Ctrl 로 수류탄을 던진다 (둘 다 판이 앞 창일 때만 읽는다, Ctrl 은 누르는 순간만 — 꾹 눌러도 한 번). 무기는 자동 (판마다 하나를 무작위로 받는다). Esc 끝. 본체 창은 숨기고 작은 볼록이를 판 위에 그린다
// 표시·효과·소리·조작은 공통 도구(app/Games/Kit)를 쓴다
// 단계 (docs/swarm-levels.md): 5:00 을 버티면 다음 판은 한 단계 어려워진다. 시작 깃발·기록 줄 깃발·결과 화면 깃발(↑)과 왕관 색이 깬 가장 높은 단계(등급)를 보여 준다
sealed partial class SwarmGame : IGameMode
{
    readonly IGameHost host;
    Swarm? swarm;
    Session? ses;
    int bestSec, bestKills, wins, clears;      // 최고 기록, 5:00 을 버틴 판 수, 보스 넷을 다 잡고 버틴 판 수
    Arm.Id? lastWeapon, pick;                  // 지난 판 무기 (다음 판에 같은 무기를 안 뽑는다), 디버그로 정한 다음 판 무기
    Dictionary<string, int> best = new();      // 무기 이름 → 그 무기로 버틴 최고 초
    int stage = 1, maxStage;                   // 다음 판에 할 단계, 깬 가장 높은 단계 (0 = 아직 없음)
    Dictionary<string, int> stageBest = new(); // 무기 이름 → 그 무기로 깬 가장 높은 단계
    int? forceStage;                           // 디버그로 정한 다음 판 단계
    bool shiftWas, swapUsed;                   // 지난 프레임에 Shift 가 눌려 있었다 (누르는 순간만), 이번 판에서 다시 뽑기를 썼다 (2~5단계 보상: 한 판에 한 번)
    bool auto, practice;                       // practice: 디버그로 단계를 정한 판 (자동 조종 판처럼 기록·단계에 안 남는다)
    Watch hud, bossHud, rushHud, flagHud, nadeHud, specHud, laneHud;
    readonly List<Rectangle> laneRects = new(), lastLanes = new();   // 돌진 띠: 이번 프레임에 다시 칠할 자리, 지난번에 그린 자리 (사라질 때 지운다)
    bool ctrlWas;                              // 지난 프레임에 Ctrl 이 눌려 있었다 (누르는 순간만 던지게)
    readonly Random pickRng = new();
    internal Func<Rectangle, Session>? FakeOpen;   // 자가 검사: 판을 열지 않고 Start 를 돌려 본다

    static readonly Dictionary<string, Sound> Sounds = new()
    {
        ["kill"] = Sound.Hit, ["bigkill"] = Sound.Boom, ["boom"] = Sound.Boom, ["swing"] = Sound.Tap, ["hurt"] = Sound.Hurt, ["over"] = Sound.Hurt,
        ["level"] = Sound.Pick, ["heart"] = Sound.Pick, ["magnet"] = Sound.Pick, ["shoe"] = Sound.Pick, ["win"] = Sound.Pick,
        ["grenade"] = Sound.Pick, ["throw"] = Sound.Tap, ["noammo"] = Sound.Tap, ["nade"] = Sound.Boom,   // 수류탄: 줍기, 던지기, 빈 손(톡), 터짐
        ["swap"] = Sound.Pick, ["cluster"] = Sound.Boom,   // 바주카 새끼 폭탄: 첫 것이 터질 때만 한 번 더 쾅 (특수기 "special" 자체는 소리 없음: 산탄총은 저절로 두 번, 난사는 네 번 울린다)
        ["mound"] = Sound.Tap,                             // 가만히 있기 막기: 흙더미가 놓일 때 톡 (글자 없이 소리와 그림으로)
    };
    // 시작 안내 (줄 순서 규칙: 마우스 → 키 → Esc)
    static readonly string[] GuideKeys = { "note.swarm_move", "note.swarm_auto", "note.swarm_grenade", "note.esc" };
    static readonly string[] GuideReroll = { "note.swarm_move", "note.swarm_auto", "note.swarm_grenade", "note.swarm_reroll", "note.esc" };   // 2단계를 깼으면 (다시 뽑기)
    static readonly string[] GuideChoose = { "note.swarm_move", "note.swarm_auto", "note.swarm_grenade", "note.swarm_choose", "note.esc" };   // 6단계를 깼으면 (무기 고르기)
    string[] GuideNow => maxStage >= 6 ? GuideChoose : maxStage >= 2 ? GuideReroll : GuideKeys;
    const double RerollSec = 1.5, ChooseSec = 3.0;   // 시작해서 Shift 가 먹는 시간
    // 쏘는 소리는 무기마다 (Arm.Id 순서. 근접은 swing, 바주카는 쏠 때 톡·터질 때 쾅, 화염은 톡)
    static readonly Sound[] FireSound = { Sound.Tap, Sound.Tap, Sound.Shot, Sound.ShotGun, Sound.ShotMg, Sound.Tap, Sound.Tap };

    public SwarmGame(IGameHost host) => this.host = host;
    public string Id => "swarm";
    public string MenuKey => "menu.swarm";
    public bool Running => swarm != null;

    // 무기 뽑기: 지난 판과 다른 무기를 무작위로
    static Arm.Id PickWeapon(Random rng, Arm.Id? last)
    {
        int n = rng.Next(last == null ? Arm.Kinds : Arm.Kinds - 1);
        return (Arm.Id)(last != null && n >= (int)last ? n + 1 : n);
    }

    public void Start()
    {
        if (Running) return;
        var b = host.Pet;
        var area = b.Area;
        int k = b.Scale, w = Math.Min(area.Width, 220 * k), h = Math.Min(area.Height, 150 * k);
        int x = (int)Math.Clamp(b.X - w / 2.0, area.Left, area.Right - w), y = (int)Math.Clamp(b.Y - 20 * k / 3.0 - h / 2.0, area.Top, area.Bottom - h);
        var field = new Rectangle(x, y, w, h);
        var weapon = pick ?? PickWeapon(pickRng, lastWeapon);
        pick = null;
        (practice, int play) = (forceStage != null, forceStage ?? stage);
        forceStage = null;
        swapUsed = false;
        Begin(NewRun(field, k, weapon, play));
        ses = FakeOpen?.Invoke(field) ?? Session.Open(host, field, () => Stop(true));
        ses.Guide(GuideNow);
        b.StartPuppet();
        host.ShowPet(false);
        Dbg.Log("mode-start", ("id", Id), ("field", field.ToString()), ("weapon", Arm.Name(weapon)), ("stage", play));
    }

    // 새 판 하나: 4단계를 깬 사람은 자석 하나로 시작한다 (시작·다시 뽑기·디버그 모두 이 길로)
    Swarm NewRun(Rectangle field, int k, Arm.Id? weapon, int play)
    {
        var s = new Swarm(field, k, new Random(), weapon, play) { CampOn = true };   // 가만히 있기 막기는 진짜 판에서만
        if (maxStage >= 4) s.Magnets = 1;
        return s;
    }

    // 새 판을 건다 (처음이거나, 하는 중에 디버그로 무기를 바꿔 같은 자리에서 다시)
    void Begin(Swarm s)
    {
        swarm = s;
        s.OnEvent = e =>
        {
            if (e == "fire") { if (FireAllowed(s)) Snd.Play(FireSound[(int)s.Weapon.Which]); }   // 팡 직후 40ms 는 쏘는 소리가 못 자른다
            else if (e == "kill" && Feel.On) { }                                                  // 죽는 소리는 팡 소리가 낸다 (SwarmFeel: 45ms 에 한 번)
            else
            {
                if (HighSound(e)) lastHighAt = s.Clock;                                           // 쾅·다침·레벨 뒤 150ms 는 팡이 못 자른다
                Snd.Play(Sounds, e);
            }
            if (e is "level" or "hurt" or "big" or "bigkill" or "magnet" or "shoe" or "over" or "win" or "rush" or "grenade" or "throw" or "nade")
                Dbg.Log("swarm", ("event", e), ("stage", s.Stage), ("sec", (int)s.Time), ("weapon", Arm.Name(s.Weapon.Which)), ("level", s.Level), ("kills", s.Kills), ("hp", s.Hp), ("foes", s.Foes.Count), ("shoes", s.Shoes), ("magnets", s.Magnets), ("boss", s.BossesKilled), ("grenades", s.Grenades), ("nadekills", s.NadeKills));
        };
        hud = bossHud = rushHud = flagHud = nadeHud = specHud = laneHud = default;
        laneRects.Clear();
        lastLanes.Clear();
        (ctrlWas, shiftWas) = (true, true);   // Ctrl·Shift 를 누른 채 시작해도 안 나간다·안 바뀐다 (떼었다 다시 눌러야 한다)
        gemRects.Clear();
        flakes.Trim(0);
        flameRect = Rectangle.Empty;
        FeelReset();
    }

    public void Frame(double dt)
    {
        var (s, io) = (swarm!, ses!);
        io.Tick();
        if (auto) s.BotPlay();
        else
        {
            s.Steer(io.AxisX, io.AxisY); // 다른 창으로 넘어가면 방향키는 뗀 걸로 읽힌다
            bool ctrl = io.Active && io.Ctrl;   // Ctrl 도 판이 앞 창일 때만 (다른 창에서 Ctrl+C 를 누를 때 아까운 수류탄이 나가면 안 된다)
            if (ctrl && !ctrlWas) s.Throw();    // 누르는 순간만: 꾹 눌러도 한 번 (시작 안내·깃발이 떠 있는 동안에도 던질 수 있다)
            ctrlWas = ctrl;
            bool shift = io.Active && io.Shift, prevShift = shiftWas;   // Shift 도 판이 앞 창일 때만 (한/영 전환·대문자로 누르는 일이 판 밖에서 먹지 않게). 시작 몇 초 안에만 의미가 있다
            shiftWas = shift;
            if (shift && !prevShift && CanSwap(s)) { Swap(s); return; }   // 새 판으로 바꿨으면 옛 판은 더 돌리지 않는다 (다음 프레임부터 새 판)
        }
        double sim = dt;
        if (Feel.On && stopLeft > 0) { stopLeft -= dt; sim = dt * Feel.StopSlow; StopFrames++; }   // 보스를 잡은 순간 아주 짧은 멈춤: 규칙의 시간만 느려진다 (조작은 계속 읽는다)
        DtTap?.Invoke(sim);
        s.Update(sim);
        Track();
        (host.Pet.X, host.Pet.Y) = (s.X, s.Feet); // 본체는 숨었지만 끝나면 여기서 나타난다
        if (s.Done || Ended(s) && s.EndedFor > .8 && io.LeftHit) Stop(false); // 끝 화면은 0.8초 뒤부터 눌러서 닫을 수 있다
    }

    // 보상 (docs/swarm-levels.md 2-2): 풀림은 깬 가장 높은 단계(maxStage)로만 — 2 다시 뽑기(시작 1.5초 안에 Shift 한 번) · 6 고르기(시작 3초 안에 Shift 를 누를 때마다 다음 무기, 횟수 제한 없음)
    bool Choosing => maxStage >= 6;
    bool CanSwap(Swarm s) => s.State == Swarm.Phase.Play && !auto && maxStage >= 2 && (Choosing ? s.Clock < ChooseSec : s.Clock < RerollSec && !swapUsed);

    void Swap(Swarm s)
    {
        var next = Choosing ? (Arm.Id)(((int)s.Weapon.Which + 1) % Arm.Kinds) : PickWeapon(pickRng, s.Weapon.Which);
        Begin(NewRun(s.Field, s.K, next, s.Stage));   // 새 판이다: 시계 0, 볼록이 가운데, 수류탄 1, 벌레·보석 없음 (판 자리·단계·연습 여부는 그대로)
        swapUsed = true;
        if (!Choosing) ses?.Guide(GuideKeys);          // 다시 뽑기는 한 번이라 안내에서 Shift 줄을 뺀다 (고르기는 줄을 그대로: 판 시계가 0 이라 4초 동안 다시 뜬다)
        Snd.Play(Sounds, "swap");
        Dbg.Log("swarm-swap", ("weapon", Arm.Name(next)), ("stage", s.Stage), ("choose", Choosing));
    }

    // 머리 위 큰 무기 그림이 떠 있는 시간 (고르기는 3초, 그 밖에는 시작 1.5초)
    double IntroWeaponSec => Choosing && !auto ? ChooseSec : IntroSec;
    bool RerollOpen(Swarm s) => s.State == Swarm.Phase.Play && !Choosing && !auto && maxStage >= 2 && !swapUsed && s.Clock < RerollSec;

    // quit(보스 키·디버그 stop): 결과 화면·표정 없이 바로 끝낸다. 기록은 조용히 남긴다
    public void Stop(bool quit)
    {
        if (swarm is not { } s) return;
        swarm = null;
        if (!auto) Keep(s, quit);
        ses?.Close(host.Pet, quit, s.Won || s.Time >= 100, "swarm", ("stage", s.Stage), ("sec", (int)s.Time), ("kills", s.Kills), ("level", s.Level), ("weapon", Arm.Name(s.Weapon.Which)), ("boss", s.BossesKilled), ("thrown", s.Thrown), ("nadekills", s.NadeKills), ("specials", s.Specials), ("speckills", s.SpecKills), ("esc", quit));
        ses = null;
        host.ShowPet(true);
        Dbg.Log("mode-end", ("id", Id));
        auto = practice = false;
        host.Save();
    }

    // 기록: 최고 시간·마릿수, 버틴 판·다 잡은 판, 무기별 최고 초, 지난 판 무기. 5:00 을 버텼으면 깬 단계(왕관 색)·무기별 최고 단계, 그 판이 지금 단계였으면 다음 단계로.
    // 디버그로 단계를 정한 판(practice)은 아무것도 안 남기고, 보스 키(Esc)로 끝낸 판은 단계를 안 바꾼다
    void Keep(Swarm s, bool quit = false)
    {
        if (practice) return;
        string name = Arm.Name(s.Weapon.Which);
        (bestSec, bestKills, lastWeapon) = (Math.Max(bestSec, (int)s.Time), Math.Max(bestKills, s.Kills), s.Weapon.Which);
        if (s.Won) wins++;
        if (s.Cleared) clears++;
        best[name] = Math.Max(best.GetValueOrDefault(name), (int)s.Time);
        if (!s.Won) return;   // 5:00 을 버텼으면 결과 화면에서 Esc 를 눌러도 단계는 오른다 (Esc 로 판 도중에 끝내면 Won 이 아니다)
        maxStage = Math.Max(maxStage, s.Stage);
        stageBest[name] = Math.Max(stageBest.GetValueOrDefault(name), s.Stage);
        if (s.Stage == stage) stage = Math.Min(MaxStageNo, s.Stage + 1);
    }

    const int MaxStageNo = 999;
    int Honor(Swarm s) => Math.Max(maxStage, s.Won && !practice && !auto ? s.Stage : 0);          // 등급을 정하는 단계: 깬 가장 높은 단계 (방금 이긴 판 포함)
    bool Advances(Swarm s) => s.Won && !practice && !auto && s.Stage == stage;                     // 이 판을 이겨 다음 단계로 올라간다

    // ── 그리기: 판 모서리, 보석·아이템, 벌레·보스, 탄·불길, 칼 자국, 터짐, 작은 볼록이와 손에 든 무기, 표시 (글자 없이 숫자·그림만) ──
    static bool Ended(Swarm s) => s.State is Swarm.Phase.Win or Swarm.Phase.Over;
    static int SecLeft(Swarm s) => (int)Math.Ceiling(Swarm.WinSec - s.Time);
    const double IntroSec = 1.5, RingSec = .6, FeetBarSec = 1.2, FlakeSec = .35;

    // 자주 찍는 그림은 한 번만 찾아 둔다 (개발 중 그림이 바뀌면 다시)
    Sprites? artOf;
    int artK;
    readonly Stamp[][] foe = new Stamp[4][];   // Swarm.Kind 순서
    readonly Stamp[][] elite = new Stamp[3][]; // 엘리트(금색 테두리): 보통·말벌·딱정벌레
    Stamp[] steel = null!, eliteSteel = null!;  // 강철 딱정벌레(5단계부터)와 그 엘리트
    Stamp[] queen = null!;                     // 여왕: 보스 그림을 한 배율 크게
    readonly Stamp[] rocket = new Stamp[2];    // 오른쪽·왼쪽
    Stamp gem = null!, bigGem = null!, heart = null!, magnet = null!, shoe = null!, flash = null!, plus = null!, crown = null!, bigCrown = null!, spark = null!, bossIcon = null!;
    readonly Stamp[] itemIcon = new Stamp[5];  // Swarm.Item 순서 (주웠을 때 머리 위에 뜨는 그림: 자석·신발·수류탄만)
    readonly Stamp[] nadeFly = new Stamp[4];   // 날아가는 수류탄 네 장 (도는 차례)
    readonly Stamp[] bladeFly = new Stamp[4];  // 날아가는 칼 네 장 (쌍검 특수기)
    readonly Stamp[] fireFloor = new Stamp[2]; // 불 자리 두 장 (화염 특수기, 0.16초마다 번갈아)
    readonly Stamp[] mound = new Stamp[3];     // 흙더미 셋 (가만히 있기 막기: 펴짐 → 갈라짐 → 부풀어 떨림, 0.8초 안에)
    readonly Stamp[] burst = new Stamp[2];     // 벌레가 올라온 직후 튀는 흙덩이 둘
    readonly Stamp[] handLit = new Stamp[2];   // 특수기 번쩍: 밝은 손 (오른쪽·왼쪽)
    Stamp bomb = null!, iconLit = null!;       // 새끼 폭탄, 밝은 머리 그림
    Stamp nade = null!, nadeBright = null!, nadeGray = null!, nadeGrayBig = null!, shadeBig = null!, shadeSmall = null!;   // 수류탄: 줍는 것·표시(작은 배율), 번쩍, 회색(0개·빈 손), 머리 위 회색, 그림자
    readonly Stamp[] upIcon = new Stamp[4];    // Swarm.Up 순서
    readonly Stamp[] hand = new Stamp[2];      // 이번 판 무기: 오른쪽·왼쪽
    Stamp weaponIcon = null!, weaponBig = null!;
    Arm.Id artWeapon;

    void Art(Overlay o, Swarm s)
    {
        int k = s.K;
        var id = s.Weapon.Which;
        if (ReferenceEquals(artOf, o.Art) && artK == k && artWeapon == id) return;
        var st = Stamps.Of(o.Art);
        int small = GameHud.Small(k), hs = GameHud.HeartScale(k);
        (artOf, artK, artWeapon) = (o.Art, k, id);
        (foe[(int)Swarm.Kind.Bug], foe[(int)Swarm.Kind.Wasp]) = (st.Row("game/bug", small), st.Row("game/wasp", small));
        (foe[(int)Swarm.Kind.Beetle], foe[(int)Swarm.Kind.Boss], queen) = (st.Row("game/beetle", small), st.Row("game/bugboss", small), st.Row("game/bugboss", small + 1));
        (gem, bigGem, heart, flash) = (st.Get("game/gem", 0, small), st.Get("game/gem", 0, k), st.Get("icon/heart", 0, hs), st.Get("game/flash", 0, small));
        (plus, magnet, shoe) = (st.Get("num/+", 0, k), st.Get("game/magnet", 0, small), st.Get("game/shoe", 0, small));
        (crown, bigCrown, spark, bossIcon) = (st.Get("game/crown", 0, small), st.Get("game/crown", 0, k + 1), st.Get("game/spark", 0, small), st.Get("game/bugboss", 0, Math.Max(1, small - 1)));
        (rocket[0], rocket[1]) = (st.Get("game/rocket", 0, small), st.Get("game/rocket", 0, small, true));
        (itemIcon[(int)Swarm.Item.Magnet], itemIcon[(int)Swarm.Item.Shoe], itemIcon[(int)Swarm.Item.Grenade]) = (st.Get("game/magnet", 0, k), st.Get("game/shoe", 0, k), SwarmArt.Nade(k));
        (nade, nadeBright, nadeGray, nadeGrayBig) = (SwarmArt.Nade(small), SwarmArt.NadeBright(small), SwarmArt.NadeGray(small), SwarmArt.NadeGray(k));
        for (int i = 0; i < nadeFly.Length; i++) nadeFly[i] = SwarmArt.NadeFly(i, k);   // 날아가는 그림은 한 칸 크게 (든 수류탄과 비슷한 크기로 보이게)
        (shadeBig, shadeSmall) = (SwarmArt.NadeShade(false, k), SwarmArt.NadeShade(true, k));
        for (int i = 0; i < bladeFly.Length; i++) bladeFly[i] = SwarmArt.BladeFly(i, k);
        (bomb, fireFloor[0], fireFloor[1]) = (SwarmArt.Bomb(k), SwarmArt.FireFloor(0, k), SwarmArt.FireFloor(1, k));
        for (int i = 0; i < mound.Length; i++) mound[i] = SwarmArt.Mound(i, small);
        for (int i = 0; i < burst.Length; i++) burst[i] = SwarmArt.Burst(i, small);
        (handLit[0], handLit[1], iconLit) = (SwarmArt.HandBright(id, small, false), SwarmArt.HandBright(id, small, true), SwarmArt.IconBright(id, small));
        for (var u = Swarm.Up.Fast; u <= Swarm.Up.Power; u++) upIcon[(int)u] = SwarmArt.UpIcon(u, k);
        upIcon[(int)Swarm.Up.Heal] = heart;
        (hand[0], hand[1], weaponIcon, weaponBig) = (SwarmArt.Hand(id, small, false), SwarmArt.Hand(id, small, true), SwarmArt.Icon(id, small), SwarmArt.Icon(id, k + 1));
        for (int kind = 0; kind < elite.Length; kind++)
        {
            elite[kind] = new Stamp[foe[kind].Length];
            for (int i = 0; i < elite[kind].Length; i++) elite[kind][i] = SwarmArt.Elite(foe[kind][i], kind * 2 + i, small);
        }
        var beetle = foe[(int)Swarm.Kind.Beetle];
        (steel, eliteSteel) = (new Stamp[beetle.Length], new Stamp[beetle.Length]);
        for (int i = 0; i < beetle.Length; i++) (steel[i], eliteSteel[i]) = (SwarmArt.Steel(beetle[i], i, small), SwarmArt.Elite(SwarmArt.Steel(beetle[i], i, small), 6 + i, small));
        feelArt = FeelArt.Of(o.Art, small, new[]   // 벌레 줄 열 (FeelArt.Row 순서): 맞은 흰 판·찌그러짐·터짐 그림은 이 줄들에서 만든다
        {
            foe[(int)Swarm.Kind.Bug], foe[(int)Swarm.Kind.Wasp], foe[(int)Swarm.Kind.Beetle], foe[(int)Swarm.Kind.Boss], queen,
            elite[0], elite[1], elite[2], steel, eliteSteel,
        });
    }

    Stamp FoeArt(Swarm s, Swarm.Foe f)
    {
        var row = f.Tier >= Swarm.Bosses ? queen : f.Heavy ? (f.Elite ? eliteSteel : steel) : f.Elite ? elite[(int)f.Kind] : foe[(int)f.Kind];
        return row[((int)(s.Clock * (f.Kind == Swarm.Kind.Wasp ? 12 : 5)) + (int)f.Speed) % row.Length];
    }

    // 벌레가 그려지는 자리: 돌진 예고로 떨 때는 좌우로 한 칸씩, 여왕은 머리 위 작은 왕관까지
    static int FoeX(Swarm s, Swarm.Foe f) => (int)f.X + (s.Shaking(f) ? (((int)(s.Clock * 30) & 1) == 0 ? 1 : -1) * GameHud.Small(s.K) : 0);
    static int CrownY(Swarm s, Swarm.Foe f) => (int)(f.Y - f.R) - 3 * GameHud.Small(s.K);
    bool Sparkle(Swarm s, Swarm.Foe f) => f.Elite && ((((int)(s.Clock * 4)) + (int)f.Speed) & 1) == 0;   // 엘리트는 머리 위 반짝이가 깜빡
    Rectangle FoeRect(Swarm s, Swarm.Foe f)
    {
        int small = GameHud.Small(s.K), x = FoeX(s, f);
        var r = Rectangle.Inflate(FoeArt(s, f).Box(x, (int)f.Y + RiseY(s, f)), 2 + small, 2 + small);
        if (Rising(s, f)) r = Rectangle.Union(r, Rectangle.Inflate(burst[0].Box(x, (int)f.Y + 6 * small), 1, 1));   // 흙덩이 (솟는 동안만)
        if (f.Elite) return Rectangle.Union(r, Rectangle.Inflate(spark.Box(x, CrownY(s, f)), 1, 1));
        return f.Tier >= Swarm.Bosses ? Rectangle.Union(r, Rectangle.Inflate(crown.Box(x, CrownY(s, f)), 1, 1)) : r;
    }

    // 흙더미: 놓인 지 얼마나 됐는지에 따라 셋 중 하나 (마지막 단계는 한 칸씩 좌우로 떨린다). 올라온 벌레는 0.25초 동안 땅에서 솟고 흙덩이가 튄다
    static int MoundStage(Swarm s, in Swarm.Mound m) => Math.Min(2, (int)((s.Clock - m.At) / Swarm.MoundSec * 3));
    static int MoundShake(Swarm s, in Swarm.Mound m) => MoundStage(s, m) == 2 ? (((int)(s.Clock * 30) & 1) == 0 ? 1 : -1) * GameHud.Small(s.K) : 0;
    const double RiseSec = .25;
    static bool Rising(Swarm s, Swarm.Foe f) => f.Dirt && s.Clock - f.BornAt < RiseSec;
    static int RiseY(Swarm s, Swarm.Foe f) => Rising(s, f) ? (int)((1 - (s.Clock - f.BornAt) / RiseSec) * 6 * GameHud.Small(s.K)) : 0;   // 땅에서 솟는 동안 그려지는 아래 쪽 어긋남
    int BurstFrame(Swarm s, Swarm.Foe f) => s.Clock - f.BornAt < RiseSec / 2 ? 0 : 1;

    Stamp GemArt(Swarm.Gem m) => m.Item switch { Swarm.Item.Magnet => magnet, Swarm.Item.Shoe => shoe, Swarm.Item.Grenade => nade, Swarm.Item.Heart => heart, _ => m.Xp > 1 ? bigGem : gem };

    // 떨어져 있는 자석·신발·수류탄은 눈에 띄게 한 칸씩 까딱인다 (그려지는 높이)
    static double ShowY(Swarm s, Swarm.Gem m) => m.Item >= Swarm.Item.Magnet && ((int)(s.Clock * 2.5) & 1) == 1 ? m.Y - GameHud.Small(s.K) : m.Y;
    static Rectangle GemRect(Stamp art, double x, double y) => Rectangle.Inflate(art.Box((int)x, (int)y), 1, 1);

    // 레벨(별 + 숫자)과 그 아래 보석 막대: 위쪽 가운데. 별 왼쪽에 이번 판 무기
    static (int X, int Y) LevelAt(Rectangle f, int k) => (f.Left + f.Width / 2 - 8 * k, f.Top + 7 * k);
    static Rectangle XpBar(Rectangle f, int k) => new(f.Left + f.Width / 2 - 13 * k, f.Top + 13 * k, 26 * k, 2 * k);
    static (int X, int Y) WeaponAt(Rectangle f, int k) => (f.Left + f.Width / 2 - 30 * k, f.Top + 7 * k);

    // 신발: 레벨 숫자 오른쪽에 주운 켤레만큼 작은 신발 (세 켤레까지)
    static (int X, int Y) ShoeAt(Rectangle f, int k, int i) => (f.Left + f.Width / 2 + (12 + 9 * i) * k, f.Top + 7 * k);

    // 자석: 보석 막대 바로 아래에 자석 그림과 주운 수
    static (int X, int Y) MagnetAt(Rectangle f, int k) => (f.Left + f.Width / 2 - 5 * k, f.Top + 21 * k);

    // 생명: 오른쪽 위에 하트 그림 하나 + 막대. 싸우는 중엔 구석을 못 보니 맞거나 회복한 뒤 잠깐은 발밑에도 작은 막대
    static Rectangle HpBar(Rectangle f, int k) => new(f.Right - 42 * k, f.Top + 6 * k, 36 * k, 3 * k);
    static (int X, int Y) HpHeartAt(Rectangle f, int k) => (f.Right - 44 * k - 10 * GameHud.HeartScale(k), f.Top + 7 * k);
    static Rectangle FeetBar(Swarm s) => new((int)s.X - 7 * s.K, (int)s.Feet + 2 * s.K, 14 * s.K, 2 * s.K);

    // 수류탄: 오른쪽 위 생명 막대 바로 아래에 그림 + 든 수 (배율 1 에서도 하트와 안 겹치게). 왼쪽 「남은 시간 아래 깃발 + 단계」와 짝
    static (int X, int Y) GrenadeAt(Rectangle f, int k) => (f.Right - 38 * k, f.Top + Math.Max(17 * k, 7 * k + 12 * GameHud.HeartScale(k) + 6 * GameHud.Small(k) + 2));
    const double NadeFlashSec = .3, NadeBlinkSec = .075;   // 표시 번쩍(주웠거나 던졌다)이 보이는 시간, 빈 손으로 눌렀을 때 깜빡이는 시간·간격
    static bool NadeFlash(Swarm s) => s.Grenades > 0 && s.Clock - s.GrenadeAt < NadeFlashSec;
    static bool EmptyOn(Swarm s) => s.Clock - s.EmptyAt < NadeFlashSec && ((int)((s.Clock - s.EmptyAt) / NadeBlinkSec) & 1) == 0;   // 빈 손 깜빡임에서 회색 그림이 보이는 차례
    static bool EmptyHand(Swarm s) => s.Clock - s.EmptyAt < NadeFlashSec;

    // 날아가는 수류탄의 바닥 자리와 높이 (포물선). u 는 0~1 (던진 순간 → 떨어지는 순간)
    static (double X, double Y, double H) NadeAt(Swarm s, in Swarm.Nade d)
    {
        double u = Math.Clamp((s.Clock - d.At) / Swarm.ThrowSec, 0, 1);
        return (d.X0 + (d.X1 - d.X0) * u, d.Y0 + (d.Y1 - d.Y0) * u, 4 * Swarm.ThrowArc * s.K / 3.0 * u * (1 - u));
    }

    static int NadeFrame(Swarm s, in Swarm.Nade d) => (d.Spin * (int)((s.Clock - d.At) * 12)) & 3;   // 도는 장 (왼쪽으로 던지면 거꾸로)

    // 특수기 그림 (글자 없이): 번쩍(0.25초: 위쪽 무기 그림과 손에 든 무기가 밝은 판), 그 앞 0.12초는 무기 그림 오른쪽 위에 반짝이
    static bool SpecFlash(Swarm s) => s.State == Swarm.Phase.Play && s.Clock - s.SpecialAt < Arm.Spec.FlashSec;
    static bool SpecSpark(Swarm s) => s.State == Swarm.Phase.Play && s.Clock - s.SpecialAt < Arm.Spec.SparkSec;
    (int X, int Y) SparkAt(Rectangle f, int k) { var (wx, wy) = WeaponAt(f, k); return (wx + weaponIcon.Bmp.Width / 2, wy - weaponIcon.Bmp.Height / 2); }
    static int BladeFrame(Swarm s) => (int)((s.Clock - s.BladeAt) * 16) & 3;   // 도는 장 (Clock 으로만: 같은 씨 = 같은 판)
    static int FireFrame(Swarm s) => (int)(s.Clock / .16) & 1;
    // 불 자리는 꺼지기 0.4초 전부터 0.1초마다 깜빡이며 꺼진다 (true 면 이번 프레임은 그리지 않는다)
    static bool FireHidden(Swarm s, in Swarm.FireSpot f) => s.Clock - f.At >= Arm.Spec.FloorSec - .4 && ((int)((s.Clock - f.At) * 10) & 1) == 1;

    // 날아가는 새끼 폭탄의 바닥 자리와 높이 (포물선: 높이는 그림 전용)
    static (double X, double Y, double H) BombAt(Swarm s, in Swarm.Bomb b)
    {
        double u = Math.Clamp((s.Clock - b.At) / b.Fly, 0, 1);
        return (b.X0 + (b.X1 - b.X0) * u, b.Y0 + (b.Y1 - b.Y0) * u, 4 * Arm.Spec.BombArc * s.K / 3.0 * u * (1 - u));
    }

    // 보스 생명: 판 아래 가운데에 보스 그림 + 막대 (여럿이면 가장 오래된 보스)
    static Rectangle BossBar(Rectangle f, int k) => new(f.Left + f.Width / 2 - 20 * k, f.Bottom - 9 * k, 50 * k, 3 * k);
    static (int X, int Y) BossIconAt(Rectangle f, int k) => (f.Left + f.Width / 2 - 28 * k, f.Bottom - 8 * k);
    // 단계 표시: 기록 줄은 남은 시간 바로 아래(작은 깃발 + 숫자), 시작 1.5초는 위쪽 가운데에 깃발 + 숫자를 크게 (가운데 볼록이·머리 위 무기와 안 겹치게)
    static (int X, int Y) RecordAt(Rectangle f, int k) => (f.Left + 8 * k, f.Top + 16 * k);
    static Rectangle RecordRect(Rectangle f, int k, int digits) { var (x, y) = RecordAt(f, k); int sm = GameHud.Small(k); return new(x - 6 * sm, y - 6 * sm, (9 + 5 * digits + 1) * sm, 12 * sm); }
    static (int X, int Y) StartAt(Rectangle f) => (f.Left + f.Width / 2, f.Top + (int)(f.Height * .27));
    static Rectangle StartRect(Rectangle f, int k, int digits) { var (x, y) = StartAt(f); int big = k + 1; return Overlay.Around(x, y, (7 + 5 * digits / 2 + 2) * big, 7 * big); }
    static Swarm.Foe? Boss(Swarm s) { foreach (var f in s.Foes) if (f.Kind == Swarm.Kind.Boss) return f; return null; }

    // 예고: 들어올 변 안쪽에 빨간 세모 (변 위 u 자리). 보스는 그 자리로, 러시는 변 가운데(.5)
    static (int X, int Y) WarnAt(Rectangle f, int k, int side, double u)
    {
        int along = side < 2 ? f.Top + (int)(u * f.Height) : f.Left + (int)(u * f.Width);
        return side switch { 0 => (f.Left + 8 * k, along), 1 => (f.Right - 8 * k, along), 2 => (along, f.Top + 8 * k), _ => (along, f.Bottom - 8 * k) };
    }

    // 러시 예고 (35~40초): 판 안쪽 가장자리에 흐린 주황 느낌표 여섯이 제각각 깜빡. 첫 물결이 들어올 변 쪽에만 (둘러싸기 Ring 이면 사방).
    // YELL 2026-11-17: 붉은 테두리 띠·붉은 모서리는 너무 눈에 띈다 → 느낌표 시안 2번, 모서리는 원래 색. 자리는 러시마다 한 번 정한다 (rng 를 안 쓴다: 같은 씨 = 같은 판)
    const int Marks = 6;
    readonly (int X, int Y)[] marks = new (int, int)[Marks];
    readonly int[] markSides = new int[Marks];   // 느낌표마다 서 있는 변 (검사용)
    int marksFor = -1;

    void PlaceMarks(Swarm s)
    {
        int key = (int)(s.Clock / 60) * 64 + s.RushSide * 8 + (int)s.RushShape;
        if (marksFor == key) return;
        marksFor = key;
        var r = new Random(key * 7919 + 17);   // 러시마다 한 번 (프레임마다 만들지 않는다)
        var f = s.Field;
        int k = s.K;
        for (int i = 0; i < Marks; i++)
        {
            int side = s.RushShape == Swarm.Shape.Ring ? r.Next(4) : s.RushShape == Swarm.Shape.Pincer ? s.RushSide ^ (i & 1) : s.RushSide;   // 양쪽 줄은 마주 보는 두 변에 셋씩
            markSides[i] = side;
            double u = .1 + .8 * r.NextDouble();
            int inset = (5 + r.Next(9)) * k + (side == 2 ? 22 * k : 0);   // 위 변은 기록 줄 아래로
            marks[i] = side switch
            {
                0 => (f.Left + inset, f.Top + (int)(u * f.Height)),
                1 => (f.Right - inset, f.Top + (int)(u * f.Height)),
                2 => (f.Left + (int)(u * f.Width), f.Top + inset),
                _ => (f.Left + (int)(u * f.Width), f.Bottom - inset),
            };
        }
    }

    // 느낌표마다 다른 박자로 깜빡 (켜진 것의 비트)
    static int MarkMask(Swarm s)
    {
        if (!s.Warning) return 0;
        int mask = 0;
        for (int i = 0; i < Marks; i++) if ((((int)(s.Clock * 3.2 + i * .53)) & 1) == 0) mask |= 1 << i;
        return mask;
    }

    static int HpPx(Rectangle bar, int hp) => bar.Width * Math.Clamp(hp, 0, Swarm.MaxHp) / Swarm.MaxHp;
    static bool HpTrail(Swarm s) => s.Clock - s.HpAt < Swarm.HpTrailSec && s.HpFrom > s.Hp;
    static bool HeartHidden(Swarm s) => s.State == Swarm.Phase.Play && s.Hp < Swarm.MaxHp * 3 / 10 && ((int)(s.Clock * 8) & 1) == 0;   // 30% 아래면 4Hz 로 깜빡

    // 불길 조각 (그림 전용: 규칙과 무관해 Random 을 따로 쓴다)
    struct Flake { public double X, Y, Vx, Vy, Born; }
    readonly Bag<Flake> flakes = new(64);
    readonly Random flakeRng = new(7);
    Rectangle flameRect;

    // 프레임마다 한 번: 표시 값이 바뀌었는지, 보석이 움직이거나 사라졌는지 (가만히 있는 것은 다시 칠하지 않는다), 불길 조각
    readonly List<Rectangle> gemRects = new();

    void Track()
    {
        var s = swarm!;
        int k = s.K;
        var bar = HpBar(s.Field, k);
        hud.Set(HashCode.Combine(SecLeft(s), HpPx(bar, s.Hp), HpTrail(s) ? HpPx(bar, s.HpFrom) : -1, HeartHidden(s), s.Level, s.Xp, (int)s.State, s.Shoes * 8 + s.Magnets));
        var boss = Boss(s);
        bossHud.Set(boss == null ? 0 : 1 + boss.Tier + 8 * (BossBar(s.Field, k).Width * boss.Hp / boss.MaxHp)); // 막대가 한 픽셀 줄 때만
        if (s.Warning) PlaceMarks(s);
        rushHud.Set((s.Warning ? 1 : 0) | (MarkMask(s) << 1));
        flagHud.Set(SwarmArt.Tier(maxStage) == SwarmArt.RainbowTier ? (int)(s.Clock * 2) % 6 : -1);   // 무지개 깃발은 0.5초마다 색이 바뀐다
        for (int i = 0; i < s.Nades.Count; i++)   // 폭발 그림은 크기·모양마다 처음 한 번 그리느라 느리다: 날아가는 동안 (떨어질 자리가 정해져 있다) 프레임마다 한 장씩 미리 그려 터지는 프레임이 안 튀게
        {
            ref var d = ref s.Nades[i];
            int step = (int)((s.Clock - d.At) / Swarm.ThrowSec * (BlastArt.Frames + 1));
            if (step < BlastArt.Frames) BlastArt.Burst(BlastArt.Variant(d.X1, d.Y1), step, (int)(Swarm.NadeBlast * (k / 3.0) * 1.2), GameHud.Small(k));
        }
        nadeHud.Set(HashCode.Combine(s.Grenades, NadeFlash(s), EmptyHand(s) ? (EmptyOn(s) ? 2 : 1) : 0));   // 수 · 번쩍 · 빈 손 깜빡임 차례
        for (int i = 0; i < s.Bombs.Count; i++)   // 새끼 폭발도 날아가는 동안 한 장씩 미리 그린다 (크기가 새로라 처음 터질 때 프레임이 튀지 않게)
        {
            ref var b = ref s.Bombs[i];
            int step = (int)((s.Clock - b.At) / b.Fly * (BlastArt.Frames + 1));
            if (step < BlastArt.Frames) BlastArt.Burst(BlastArt.Variant(b.X1, b.Y1), step, (int)(s.Weapon.Blast * Arm.Spec.BombBlast * (k / 3.0) * 1.2), GameHud.Small(k));
        }
        specHud.Set(SpecFlash(s) ? (SpecSpark(s) ? 2 : 1) : 0);   // 특수기 번쩍: 없음 · 밝은 판 · 밝은 판 + 반짝이
        int laneKey = s.LaneCount;   // 돌진 띠: 있는 수·뻗은 칸·자리가 바뀔 때만 다시 칠한다 (뻗는 동안만 프레임마다, 다 뻗으면 가만히)
        for (int i = 0; i < s.LaneCount; i++) laneKey = HashCode.Combine(laneKey, LaneGrown(s, s.Lanes[i]), s.Lanes[i].Side, (int)s.Lanes[i].A);
        laneHud.Set(laneKey);
        laneRects.Clear();
        if (laneHud.Changed)
        {
            laneRects.AddRange(lastLanes);   // 사라진 것의 자리도 지운다
            lastLanes.Clear();
            for (int i = 0; i < s.LaneCount; i++) { LaneRects(s, s.Lanes[i], laneRects); LaneRects(s, s.Lanes[i], lastLanes); }
        }

        int keep = 0;
        for (int i = 0; i < flakes.Count; i++) if (s.Clock - flakes[i].Born < FlakeSec) flakes[keep++] = flakes[i];
        flakes.Trim(keep);
        if (s.FlameOn && s.State == Swarm.Phase.Play)
            for (int i = 0; i < 3 && flakes.Count < 64; i++)
            {
                double a = s.AimAngle + (flakeRng.NextDouble() * 2 - 1) * s.Weapon.ArcHalf, v = s.RangePx / FlakeSec * (.65 + .35 * flakeRng.NextDouble());
                double ca = Math.Cos(s.AimAngle), sa = Math.Sin(s.AimAngle);
                flakes.Add() = new Flake { X = s.X + ca * 8 * k, Y = s.Y + 2 * k + sa * 8 * k, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, Born = s.Clock };
            }
        flameRect = Rectangle.Empty;
        if (flakes.Count > 0)
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            for (int i = 0; i < flakes.Count; i++)
            {
                ref var fl = ref flakes[i];
                double age = s.Clock - fl.Born, x = fl.X + fl.Vx * age, y = fl.Y + fl.Vy * age;
                (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
            }
            flameRect = Rectangle.FromLTRB((int)x0 - 3 * k, (int)y0 - 3 * k, (int)x1 + 3 * k + 2, (int)y1 + 3 * k + 2);
        }

        gemRects.Clear();
        if (artOf == null) return; // 아직 한 번도 안 그렸다: 첫 프레임에 판을 통째로 칠한다
        foreach (var m in s.Gone) if (!double.IsNaN(m.ShownX)) gemRects.Add(GemRect(GemArt(m), m.ShownX, m.ShownY));
        foreach (var m in s.Gems)
        {
            var (gx, gy) = GemAt(s, m);   // 톡 튀는 동안은 프레임마다 자리가 바뀐다
            if (m.ShownX == gx && m.ShownY == gy) continue;
            var art = GemArt(m);
            if (!double.IsNaN(m.ShownX)) gemRects.Add(GemRect(art, m.ShownX, m.ShownY));
            gemRects.Add(GemRect(art, gx, gy));
            (m.ShownX, m.ShownY) = (gx, gy);
        }
        FeelTrack(s);
    }

    public void Dirty(Overlay o, List<Rectangle> rs)
    {
        if (swarm is not { } s) return;
        int k = s.K, small = GameHud.Small(k), cell = o.Art.Cell * small;
        var f0 = s.Field;
        Art(o, s);
        if (hud.Fresh) rs.Add(o.Client(f0)); // 판이 막 열렸다: 한 번은 통째로
        foreach (var f in s.Foes) rs.Add(o.Client(FoeRect(s, f)));
        for (int i = 0; i < s.Shots.Count; i++)
        {
            ref var t = ref s.Shots[i];
            int half = t.Ammo == Swarm.Ammo.Rocket ? 12 * k : 2 * k + 3;
            rs.Add(o.Client(Overlay.Around(t.X, t.Y, half, half)));
        }
        for (int i = 0; i < s.Nades.Count; i++)   // 공중 수류탄: 그림자와 수류탄 두 자리 (포물선이라 서로 떨어진다)
        {
            ref var d = ref s.Nades[i];
            var (nx, ny, nh) = NadeAt(s, d);
            rs.Add(o.Client(Rectangle.Inflate(nadeFly[NadeFrame(s, d)].Box((int)nx, (int)(ny - nh)), 1, 1)));
            rs.Add(o.Client(Rectangle.Inflate(ShadeOf(s, nh).Box((int)nx, (int)(ny + 8 * k / 3.0)), 1, 1)));
        }
        for (int i = 0; i < s.Bombs.Count; i++)   // 새끼 폭탄: 폭탄과 그림자 두 자리 (공중인 동안 프레임마다)
        {
            ref var b = ref s.Bombs[i];
            var (bx, by, bh) = BombAt(s, b);
            rs.Add(o.Client(Rectangle.Inflate(bomb.Box((int)bx, (int)(by - bh)), 1, 1)));
            rs.Add(o.Client(Rectangle.Inflate(shadeSmall.Box((int)bx, (int)(by + 8 * k / 3.0)), 1, 1)));
        }
        if (s.BladeOut) rs.Add(o.Client(Rectangle.Inflate(bladeFly[BladeFrame(s)].Box((int)s.BladeX, (int)s.BladeY), 1, 1)));   // 날아가는 칼 (공중인 동안 프레임마다)
        for (int i = 0; i < s.Mounds.Count; i++)   // 흙더미 (있는 동안 프레임마다: 장이 바뀌고 떨린다. 사라지는 프레임도 지운다)
        {
            ref var m = ref s.Mounds[i];
            rs.Add(o.Client(Rectangle.Inflate(mound[2].Box((int)m.X, (int)m.Y), 2 * small, 1)));
        }
        for (int i = 0; i < s.Fires.Count; i++) rs.Add(o.Client(Rectangle.Inflate(fireFloor[0].Box((int)s.Fires[i].X, (int)s.Fires[i].Y), 1, 1)));   // 불 자리 (있는 동안 프레임마다: 장이 바뀌고 깜빡이며 꺼진다)
        if (!flameRect.IsEmpty) rs.Add(o.Client(flameRect));
        foreach (var r in gemRects) rs.Add(o.Client(r));
        for (int i = 0; i < s.Pops.Count; i++) { ref var p = ref s.Pops[i]; rs.Add(o.Client(BlastArt.PopRect(p.X, p.Y, p.R * 1.2, small))); }
        FeelDirty(o, rs, s);   // 죽는 벌레의 찌그러짐·고리·조각, 흔들림
        rs.Add(o.Client(new Rectangle((int)(s.X - cell / 2.0) - 8 * k, (int)(s.Feet - cell) - 12 * k, cell + 16 * k, cell + 18 * k))); // 볼록이·무기·머리 위 그림·발밑 막대
        if (s.Clock - s.SwingAt < s.SwingLen + .05) rs.Add(o.Client(Overlay.Around(s.X, s.Y, s.RangePx + 3 * k, s.RangePx + 3 * k)));
        if (s.Clock - s.MagnetAt < RingSec) rs.Add(o.Client(Overlay.Around(s.X, s.Y, s.PickPx + 3 * k, s.PickPx + 3 * k)));   // 새 줍는 둘레 (점선 원)
        if (s.BossWarning) { var (wx, wy) = WarnAt(f0, k, s.BossSide, s.BossU); rs.Add(o.Client(Overlay.Around(wx, wy, 8 * k, 8 * k))); }
        if (s.BossTwin) { var (wx, wy) = WarnAt(f0, k, s.BossSide ^ 1, 1 - s.BossU); rs.Add(o.Client(Overlay.Around(wx, wy, 8 * k, 8 * k))); }   // 둘째 보스 세모
        foreach (var r in laneRects) rs.Add(o.Client(r));   // 돌진 띠 (뻗는 동안·사라질 때)
        if (rushHud.Changed) // 러시 예고 느낌표: 깜빡일 때만
            for (int i = 0; i < Marks; i++) rs.Add(o.Client(Rectangle.Inflate(SwarmArt.Bang(k).Box(marks[i].X, marks[i].Y), 1, 1)));
        if (s.Clock < IntroSec + .1) rs.Add(o.Client(StartRect(f0, k, GameHud.Digits(s.Stage))));   // 시작 깃발 (사라질 때까지)
        if (flagHud.Changed || hud.Fresh) rs.Add(o.Client(RecordRect(f0, k, GameHud.Digits(s.Stage))));   // 기록 줄 깃발: 처음 한 번 (무지개는 색이 바뀔 때마다)
        if (Ended(s))
        {
            rs.Add(o.Client(ResultFlagRect(s)));                                                       // 결과의 깃발 줄 (↑·반짝이가 바뀐다)
            if (SwarmArt.Tier(Honor(s)) == SwarmArt.RainbowTier) rs.Add(o.Client(GameHud.CenterRect(f0, k)));   // 무지개 왕관
            if (hud.Changed || s.EndedFor < 1.5 || SwarmArt.Tier(Honor(s)) == SwarmArt.RainbowTier) rs.Add(o.Client(RowRect(s)));   // 무기 줄: 끝난 프레임·올리는 칸이 반짝이는 동안·무지개 숫자
        }
        else if (s.State == Swarm.Phase.Done && hud.Changed)   // 결과가 닫히는 프레임: 줄·깃발 자리도 지운다 (상자 자리보다 아래까지 내려온다)
        {
            rs.Add(o.Client(ResultFlagRect(s)));
            rs.Add(o.Client(RowRect(s)));
        }
        if (hud.Changed)
        {
            var (lx, ly) = LevelAt(f0, k);
            var (hx, hy) = HpHeartAt(f0, k);
            var (mx, my) = MagnetAt(f0, k);
            rs.Add(o.Client(GameHud.ScoreRect(f0, k)));                                   // 남은 시간
            rs.Add(o.Client(Rectangle.Union(Rectangle.Inflate(heart.Box(hx, hy), 1, 1), GameHud.BarRect(HpBar(f0, k), k))));   // 생명
            rs.Add(o.Client(GameHud.StarRect(lx, ly, small)));                            // 레벨
            rs.Add(o.Client(GameHud.BarRect(XpBar(f0, k), k)));
            var (sx0, sy0) = ShoeAt(f0, k, 0);
            var (sx1, sy1) = ShoeAt(f0, k, Swarm.MaxShoes - 1);
            rs.Add(o.Client(Rectangle.Inflate(Rectangle.Union(shoe.Box(sx0, sy0), shoe.Box(sx1, sy1)), 1, 1)));  // 신발
            rs.Add(o.Client(Rectangle.Union(Rectangle.Inflate(magnet.Box(mx, my), 1, 1), Overlay.Around(mx + 9 * k, my, 4 * small, 5 * small))));   // 자석 수
            if (Ended(s)) rs.Add(o.Client(GameHud.CenterRect(f0, k)));                    // 끝: 잡은 수·왕관
        }
        else if (Ended(s) && s.Cleared) rs.Add(o.Client(GameHud.CenterRect(f0, k)));      // 큰 왕관 둘레 반짝이
        if (nadeHud.Changed)   // 수류탄 표시: 그림(번쩍이면 한 칸 위까지)과 숫자
        {
            var (gx, gy) = GrenadeAt(f0, k);
            rs.Add(o.Client(Rectangle.Union(Rectangle.Union(Rectangle.Inflate(nade.Box(gx, gy), 1, 1), Rectangle.Inflate(nadeBright.Box(gx, gy - small), 1, 1)), Overlay.Around(gx + 9 * k, gy, 4 * small, 5 * small))));
        }
        if (specHud.Changed)   // 특수기 번쩍: 위쪽 무기 그림과 오른쪽 위 반짝이 (손에 든 무기는 볼록이 둘레 자리 안이다)
        {
            var (wx, wy) = WeaponAt(f0, k);
            var (sx, sy) = SparkAt(f0, k);
            rs.Add(o.Client(Rectangle.Inflate(Rectangle.Union(weaponIcon.Box(wx, wy), spark.Box(sx, sy)), 1, 1)));
        }
        if (bossHud.Changed)
        {
            var (bx, by) = BossIconAt(f0, k);
            rs.Add(o.Client(Rectangle.Union(Rectangle.Inflate(bossIcon.Box(bx, by), 1, 1), GameHud.BarRect(BossBar(f0, k), k))));
        }
        ses?.DirtyGuide(o, rs, s.Clock, s.X, s.Feet);
    }

    // 그림자: 낮을 때 큰 것, 높이가 꼭대기의 절반을 넘으면 작은 것
    Stamp ShadeOf(Swarm s, double h) => h > Swarm.ThrowArc * s.K / 3.0 / 2 ? shadeSmall : shadeBig;

    // 아래쪽 벌레가 위에 겹치게 그리는 차례 (프레임마다 목록을 새로 만들지 않는다)
    readonly List<Swarm.Foe> order = new();

    public void Draw(Overlay o, Graphics g)
    {
        if (swarm is not { } s) return;
        int k = s.K, small = GameHud.Small(k);
        var fl = s.Field;
        var arm = s.Weapon;
        Art(o, s);
        GameHud.Corners(o, g, fl, k);
        if (s.Warning)   // 러시 예고: 판 안쪽 가장자리의 느낌표가 제각각 깜빡
        {
            int mask = MarkMask(s);
            var bang = SwarmArt.Bang(k);
            for (int i = 0; i < Marks; i++) if ((mask & 1 << i) != 0) bang.Put(o, g, marks[i].X, marks[i].Y);
        }
        for (int i = 0; i < s.LaneCount; i++) DrawLane(o, g, s, s.Lanes[i]);   // 돌진 띠: 흐린 주황 점선 두 줄 (깜빡임·빨강 없음)

        var (shx, shy) = ShakeAt(s);   // 흔들림: 판 안 그림(보석·벌레·탄·터짐)만 한 칸씩 (모서리·표시 줄·볼록이는 안 움직인다)
        if (shx != 0 || shy != 0) g.TranslateTransform(shx, shy);
        for (int pass = 0; pass < 2; pass++) // 자석·신발은 보석 더미에 묻히지 않게 위에
            foreach (var m in s.Gems)
                if (m.Item >= Swarm.Item.Magnet == (pass == 1)) { var (gx, gy) = GemAt(s, m); GemArt(m).Put(o, g, (int)gx, (int)gy); }
        for (int i = 0; i < s.Mounds.Count; i++)   // 흙더미: 바닥에 깔린다 (보석 다음·벌레 앞). 글자 없이 그림만
        {
            ref var m = ref s.Mounds[i];
            mound[MoundStage(s, m)].Put(o, g, (int)m.X + MoundShake(s, m), (int)m.Y);
        }
        for (int i = 0; i < s.Fires.Count; i++)   // 불 자리: 바닥에 깔린다 (보석 다음·그림자와 벌레 앞). 꺼지기 0.4초 전부터 깜빡
        {
            ref var fs = ref s.Fires[i];
            if (!FireHidden(s, fs)) fireFloor[FireFrame(s)].Put(o, g, (int)fs.X, (int)fs.Y);
        }
        for (int i = 0; i < s.Nades.Count; i++)   // 날아가는 수류탄의 그림자: 바닥에 깔린다 (보석 다음·벌레 앞)
        {
            ref var d = ref s.Nades[i];
            var (nx, ny, nh) = NadeAt(s, d);
            ShadeOf(s, nh).Put(o, g, (int)nx, (int)(ny + 8 * k / 3.0));
        }
        for (int i = 0; i < s.Bombs.Count; i++)   // 새끼 폭탄 그림자
        {
            ref var b = ref s.Bombs[i];
            var (bx, by, _) = BombAt(s, b);
            shadeSmall.Put(o, g, (int)bx, (int)(by + 8 * k / 3.0));
        }
        order.Clear();
        order.AddRange(s.Foes);
        for (int i = 1; i < order.Count; i++) // 끼워 넣기 정렬: 같은 높이면 먼저 온 벌레가 아래 (차례가 바뀌어 깜빡이지 않는다)
        {
            var f = order[i];
            int j = i - 1;
            for (; j >= 0 && order[j].Y > f.Y; j--) order[j + 1] = order[j];
            order[j + 1] = f;
        }
        foreach (var f in order)
        {
            int fx = FoeX(s, f);
            DrawFoe(o, g, s, f, fx, small);   // 맞으면 흰 판으로 번쩍(맛 끄면 옛 모습) + 맞은 쪽 반대로 한 칸
            if (Rising(s, f)) burst[BurstFrame(s, f)].Put(o, g, fx, (int)f.Y + 6 * small);   // 땅에서 올라온 직후: 흙덩이
            if (f.Tier >= Swarm.Bosses) crown.Put(o, g, fx, CrownY(s, f));        // 여왕은 왕관
            if (Sparkle(s, f)) spark.Put(o, g, fx, CrownY(s, f));                 // 엘리트는 머리 위 반짝이
            if (s.Clock < f.BurnUntil)                                           // 불붙은 벌레: 등에 불씨가 깜빡
            {
                var p = o.ToClient(f.X, f.Y - f.R * .5);
                int heat = (int)(s.Clock * 10) & 1;
                Fx.Ember(g, p.X, p.Y, 2 * small, 1 + heat);
                Fx.Ember(g, p.X, p.Y - small, small, heat);
            }
        }
        order.Clear();
        DrawSquash(o, g, s);   // 죽은 벌레는 찌그러진 흰 판으로 (연쇄에서는 차례를 기다리는 동안)
        for (int i = 0; i < s.Shots.Count; i++) DrawShot(o, g, s, ref s.Shots[i]);
        if (s.BladeOut) bladeFly[BladeFrame(s)].Put(o, g, (int)s.BladeX, (int)s.BladeY);   // 날아가는 칼 (탄 위·불길 아래)
        for (int i = 0; i < flakes.Count; i++) // 불길: 멀어질수록 크고 붉게
        {
            ref var f = ref flakes[i];
            double age = s.Clock - f.Born, u = age / FlakeSec;
            var p = o.ToClient(f.X + f.Vx * age, f.Y + f.Vy * age);
            Fx.Ember(g, p.X, p.Y, (int)((1.5 + 3 * u) * k), u < .3 ? 0 : u < .65 ? 1 : 2);
        }
        DrawFeel(o, g, s);     // 고리·별·반짝이와 조각 (불덩이 아래)
        for (int i = 0; i < s.Pops.Count; i++) { ref var p = ref s.Pops[i]; BlastArt.Pop(o, g, p.X, p.Y, p.R * 1.2, s.Clock - p.At, small); }
        if (shx != 0 || shy != 0) g.TranslateTransform(-shx, -shy);

        // 작은 볼록이: 움직이면 걷고, 맞으면 아파하고 잠깐 깜빡인다. 손에 이번 판 무기를 든다 (양손 무기는 양쪽에)
        bool blink = s.State == Swarm.Phase.Play && Fx.Blink(s.Clock, s.SafeUntil);
        string pose = s.State switch { Swarm.Phase.Win => "happy", Swarm.Phase.Over => "sad", _ => Fx.Hurting(s.Clock, s.HurtAt) ? "hurt" : s.Moving ? "walk" : "idle" };
        int cell = o.Art.Cell * small;
        if (!blink && s.State != Swarm.Phase.Done)
        {
            Fx.Pet(o, g, host.Sprites, host.Pet.P.Stage, pose, s.Clock, small, s.X, s.Feet, s.Dir < 0);
            if (s.State == Swarm.Phase.Play)
            {
                int hy = (int)(s.Y + 2 * k);
                var (hr, hl) = SpecFlash(s) ? (handLit[0], handLit[1]) : (hand[0], hand[1]);   // 특수기 번쩍: 밝은 손
                if (SwarmArt.Dual(arm.Which))
                {
                    if (!(s.BladeOut && s.BladeHand > 0)) hr.Put(o, g, (int)(s.X + 9 * k), hy);   // 칼을 던진 손은 빈다
                    if (!(s.BladeOut && s.BladeHand < 0)) hl.Put(o, g, (int)(s.X - 9 * k), hy);
                }
                else (s.AimDir < 0 ? hl : hr).Put(o, g, (int)(s.X + s.AimDir * 8 * k), hy);
                if (arm.Kind is Arm.Sort.Bullets or Arm.Sort.Rocket && s.Clock - s.FireAt < .07)
                    flash.Put(o, g, (int)(s.X + s.AimDir * (8 * k + hand[0].Bmp.Width / 2 + 2 * small)), hy - small);
            }
        }
        DrawSwing(o, g, s);
        if (s.Clock - s.MagnetAt < RingSec && s.State == Swarm.Phase.Play) DrawRing(o, g, s);
        if (s.BossWarning && ((int)((s.Clock - s.BossWarnAt) * 6) & 1) == 0)
        {
            var (bwx, bwy) = WarnAt(fl, k, s.BossSide, s.BossU);
            DrawWarn(o, g, k, s.BossSide, bwx, bwy);
            if (s.BossTwin) { var (twx, twy) = WarnAt(fl, k, s.BossSide ^ 1, 1 - s.BossU); DrawWarn(o, g, k, s.BossSide ^ 1, twx, twy); }   // 둘째 보스가 들어올 마주 보는 변
        }
        for (int i = 0; i < s.Nades.Count; i++)   // 날아가는 수류탄: 볼록이·칼 자국 다음 (공중이라 맨 위), 표시보다는 아래
        {
            ref var d = ref s.Nades[i];
            var (nx, ny, nh) = NadeAt(s, d);
            nadeFly[NadeFrame(s, d)].Put(o, g, (int)nx, (int)(ny - nh));
        }
        for (int i = 0; i < s.Bombs.Count; i++)   // 날아가는 새끼 폭탄 (수류탄과 같이 공중)
        {
            ref var b = ref s.Bombs[i];
            var (bx, by, bh) = BombAt(s, b);
            bomb.Put(o, g, (int)bx, (int)(by - bh));
        }

        // 시작할 때 머리 위에 이번 무기를 크게. 레벨이 오르거나 자석·신발을 주우면 + 와 그 그림이 잠깐 뜬다 (겹치면 나중 것)
        double popAt = Math.Max(s.UpAt, s.ItemAt);
        var pop = s.ItemAt > s.UpAt ? itemIcon[(int)s.LastItem] : s.LastUp is { } up ? upIcon[(int)up] : null;
        if (pop != null && s.Clock - popAt < 1.4 && s.State == Swarm.Phase.Play)
        {
            double rise = Math.Min(1, (s.Clock - popAt) / .25) * 4 * k;
            int ux = (int)s.X + 4 * k, uy = (int)(s.Feet - cell + 5 * k - rise);
            plus.Put(o, g, ux - 10 * k, uy);
            pop.Put(o, g, ux, uy);
        }
        else if (EmptyOn(s) && s.State == Swarm.Phase.Play)   // 빈 손: 같은 자리에 회색 수류탄만 0.3초 깜빡 (+ 없음)
            nadeGrayBig.Put(o, g, (int)s.X + 4 * k, (int)(s.Feet - cell + 5 * k - 4 * k));
        else if (s.Clock < IntroWeaponSec && s.State == Swarm.Phase.Play)
        {
            int iconY = (int)(s.Feet - cell + 5 * k - Math.Min(1, s.Clock / .25) * 4 * k);
            weaponBig.Put(o, g, (int)s.X, iconY);
            if (RerollOpen(s)) SwarmArt.Swap((int)(s.Clock / .25), small).Put(o, g, (int)s.X + weaponBig.Bmp.Width / 2 + 6 * k, iconY);   // 다시 뽑기가 열려 있다: 도는 화살표 (글자 없이)
            else if (Choosing && !auto) DrawChoice(o, g, s.X, iconY - weaponBig.Bmp.Height / 2 - 4 * k, (int)s.Weapon.Which, 2 * k, k);   // 고르기: 무기 일곱 점 (그림 위), 지금 것만 주황
        }
        if (s.Clock - s.HpAt < FeetBarSec && s.State == Swarm.Phase.Play)                      // 발밑 생명 막대
            GameHud.Bar(o, g, FeetBar(s), k, s.Hp / (double)Swarm.MaxHp, Pal.Red);

        GameHud.Score(o, g, fl, k, SecLeft(s));                                              // 남은 시간 (왼쪽 위, 초)
        var bar = HpBar(fl, k);                                                              // 생명 (오른쪽 위): 하트 + 막대, 방금 잃은 만큼은 잠깐 흰색
        var (hx, hy0) = HpHeartAt(fl, k);
        if (!HeartHidden(s)) heart.Put(o, g, hx, hy0);
        GameHud.Bar(o, g, bar, k, HpPx(bar, s.Hp) / (double)bar.Width, Pal.Red);
        if (HpTrail(s))
        {
            var c = o.Client(bar);
            int now = HpPx(bar, s.Hp);
            g.FillRectangle(Pal.White, c.X + now, c.Y, HpPx(bar, s.HpFrom) - now, c.Height);
        }
        var (wx, wy) = WeaponAt(fl, k);
        (SpecFlash(s) ? iconLit : weaponIcon).Put(o, g, wx, wy);                              // 이번 판 무기 (특수기를 쓴 0.25초는 밝은 판, 처음 0.12초는 오른쪽 위에 반짝이)
        if (SpecSpark(s)) { var (spx, spy) = SparkAt(fl, k); spark.Put(o, g, spx, spy); }
        DrawStage(o, g, s);
        var (lx, ly) = LevelAt(fl, k);
        GameHud.Star(o, g, lx, ly, small, s.Level);                                          // 레벨: 별 + 숫자, 아래에 보석 막대
        for (int i = 0; i < s.Shoes; i++)                                                    // 신발: 주운 켤레만큼
        {
            var (sx, sy) = ShoeAt(fl, k, i);
            shoe.Put(o, g, sx, sy);
        }
        GameHud.Bar(o, g, XpBar(fl, k), k, Math.Min(s.Xp, s.Need) / (double)s.Need, Pal.Cyan);
        if (s.Magnets > 0)                                                                   // 자석: 그림 + 주운 수
        {
            var (mx, my) = MagnetAt(fl, k);
            magnet.Put(o, g, mx, my);
            GameHud.Number(o, g, s.Magnets, mx + 9 * k, my, small, -1);
        }
        var (gx0, gy0) = GrenadeAt(fl, k);                                                   // 수류탄: 그림 + 든 수. 0개면 회색, 주웠거나 던진 0.3초는 밝은 그림이 한 칸 위에, 빈 손으로 누르면 회색이 깜빡
        if (s.Grenades > 0) (NadeFlash(s) ? nadeBright : nade).Put(o, g, gx0, NadeFlash(s) ? gy0 - small : gy0);
        else if (!EmptyHand(s) || EmptyOn(s)) nadeGray.Put(o, g, gx0, gy0);
        GameHud.Number(o, g, s.Grenades, gx0 + 9 * k, gy0, small, -1);
        if (Boss(s) is { } boss)                                                             // 보스 생명
        {
            var (bx, by) = BossIconAt(fl, k);
            var bb = BossBar(fl, k);
            bossIcon.Put(o, g, bx, by);
            GameHud.Bar(o, g, bb, k, (bb.Width * boss.Hp / boss.MaxHp) / (double)bb.Width, Pal.Spit);
        }
        if (Ended(s)) DrawResult(o, g, s);
        ses?.DrawGuide(o, g, s.Clock, s.X, s.Feet);
    }

    // 단계 표시: 기록 줄(남은 시간 아래, 작은 깃발 + 숫자: 한 판 내내)과 시작 1.5초의 큰 깃발 + 숫자. 깃발 색은 깬 가장 높은 단계(등급)
    void DrawStage(Overlay o, Graphics g, Swarm s)
    {
        int k = s.K, small = GameHud.Small(k), big = k + 1, tier = SwarmArt.Tier(maxStage), phase = (int)(s.Clock * 2);
        var (rx, ry) = RecordAt(s.Field, k);
        SwarmArt.Flag(tier, phase, small).Put(o, g, rx, ry);
        GameHud.Number(o, g, s.Stage, rx + 9 * small, ry, small, -1);
        if (s.Clock >= IntroSec || s.State != Swarm.Phase.Play) return;
        var (cx, cy) = StartAt(s.Field);
        int digits = GameHud.Digits(s.Stage), w = (12 + 5 * digits) * big, left = cx - w / 2;
        SwarmArt.Flag(tier, phase, big).Put(o, g, left + 5 * big, cy);
        GameHud.Number(o, g, s.Stage, left + 12 * big + 5 * big / 2, cy, big, -1);
    }

    // 결과 화면의 깃발 줄: 상자 바로 아래 (왼쪽부터 깃발, 단계 숫자, 이겨서 올라가면 위 화살표)
    (int X, int Y, int Scale) ResultFlagAt(Swarm s)
    {
        int k = s.K, big = k + 1, small = GameHud.Small(k);
        var fl = s.Field;
        int cy = (int)(fl.Top + fl.Height * .45), row2 = cy + 5 * big + 2 * k + 4 * small;
        bool second = !(s.Kills > bestKills || s.Cleared) && bestKills > 0;   // GameHud.Result 가 최고 기록 줄을 그리는 판
        int bottom = (second ? row2 + 4 * small : cy + 5 * big) + 3 * k;
        return (fl.Left + fl.Width / 2, bottom + 3 * k + 5 * k, k);
    }

    Rectangle ResultFlagRect(Swarm s)
    {
        var (x, y, sc) = ResultFlagAt(s);
        return Rectangle.Union(Overlay.Around(x, y, 26 * sc, 9 * sc), Overlay.Around(x + 39 * sc, y, 14 * sc, 9 * sc));   // 오른쪽은 방금 풀린 보상 그림 자리
    }

    // 끝: 벌레 × 잡은 수. 새 기록이거나 보스 넷을 다 잡고 버텼으면 큰 왕관(다 잡았으면 반짝이까지), 5:00 을 버티기만 했으면 상자 위에 작은 왕관.
    // 왕관은 등급 색 (깬 가장 높은 단계: 동·은·금·보라·무지개). 상자 아래에 깃발 + 단계: 이겼으면 0.6초 뒤 위 화살표와 함께 다음 단계로 바뀌며 반짝인다
    void DrawResult(Overlay o, Graphics g, Swarm s)
    {
        int k = s.K, big = k + 1, small = GameHud.Small(k), tier = SwarmArt.Tier(Honor(s)), phase = (int)(s.Clock * 2);
        var fl = s.Field;
        bool top = s.Kills > bestKills || s.Cleared;
        GameHud.Result(o, g, fl, k, s.Kills, bestKills, top, "game/bug");
        int cx = fl.Left + fl.Width / 2, cy = (int)(fl.Top + fl.Height * .45), boxTop = cy - 5 * big - 3 * k;
        if (top && s.Won) SwarmArt.Crown(bigCrown, tier, phase, big).Put(o, g, cx, boxTop - 7 * big);   // 금색 큰 왕관 위에 같은 자리 등급 색
        if (s.Won && !top) SwarmArt.Crown(crown, tier, phase, small).Put(o, g, cx, boxTop - 4 * small);
        var (fx, fy, sc) = ResultFlagAt(s);
        bool up = Advances(s) && s.EndedFor >= .6;
        int shown = up ? s.Stage + 1 : s.Stage, digits = GameHud.Digits(shown), w = (12 + 5 * digits + (up ? 9 : 0)) * sc, left = fx - w / 2;
        SwarmArt.Flag(tier, phase, sc).Put(o, g, left + 5 * sc, fy);
        GameHud.Number(o, g, shown, left + 12 * sc + 5 * sc / 2, fy, sc, -1);
        if (up)
        {
            SwarmArt.Arrow(sc).Put(o, g, left + (14 + 5 * digits) * sc + 4 * sc, fy);
            if (s.EndedFor < 1.4 && ((int)(s.EndedFor * 10) & 1) == 0)   // 올라간 순간 숫자 둘레가 반짝
            {
                spark.Put(o, g, left + (21 + 5 * digits) * sc, fy - 6 * sc);
                spark.Put(o, g, left + 11 * sc, fy + 6 * sc);
            }
        }
        DrawRow(o, g, s, fx, fy);
        if (Unlocked(s) is var unlock and > 0 && (s.EndedFor >= 1.4 || ((int)(s.EndedFor * 5) & 1) == 0)) DrawUnlock(o, g, unlock, fx + 30 * sc, fy, sc);   // 방금 풀린 보상 그림 (1.4초 깜빡인 뒤 켜져 있다)
        if (!s.Cleared) return;
        for (int i = 0; i < 4; i++)
            if ((((int)(s.Clock * 5) + i) & 1) == 0)
                spark.Put(o, g, cx + (i < 2 ? -1 : 1) * ((i & 1) == 0 ? 11 : 8) * big, boxTop - 7 * big + ((i & 1) == 0 ? -3 : 2) * big);
    }

    // ── 방금 풀린 보상 (docs/swarm-levels.md 2-2): 이긴 판이 2·4·6단계를 처음 깼으면 깃발 줄 오른쪽에 그 보상 그림 ──
    // 2 = 도는 화살표(다시 뽑기) · 4 = 자석 · 6 = 점 일곱(고르기). 연습·자동 조종 판은 아무것도 안 풀린다. 한 번에 둘이 풀리면 높은 쪽
    int Unlocked(Swarm s)
    {
        if (!s.Won || practice || auto) return 0;
        for (int n = 6; n >= 2; n -= 2) if (maxStage < n && s.Stage >= n) return n;
        return 0;
    }

    void DrawUnlock(Overlay o, Graphics g, int n, int x, int y, int sc)
    {
        int small = GameHud.Small(sc);
        if (n == 2) SwarmArt.Swap((int)(swarm!.Clock / .25), small).Put(o, g, x, y);
        else if (n == 4) magnet.Put(o, g, x, y);
        else DrawChoice(o, g, x + 9 * sc, y, -1, 2 * sc, sc, centered: true);
    }

    // 점 일곱 (무기마다 하나): 지금 무기만 주황 #E0A858, 나머지는 판 선 색. 한 점 dot px, 4k 간격. x 는 가운데, y 는 점 줄의 위쪽(centered 면 가운데)
    static void DrawChoice(Overlay o, Graphics g, double x, int y, int cur, int dot, int k, bool centered = false)
    {
        int step = 2 * dot, left = (int)x - ((Arm.Kinds - 1) * step + dot) / 2, top = centered ? y - dot / 2 : y;
        for (int i = 0; i < Arm.Kinds; i++)
            g.FillRectangle(i == cur || cur < 0 ? SwarmArt.Amber : Pal.Line, o.Client(new Rectangle(left + i * step, top, dot, dot)));
    }

    // ── 돌진 띠 (docs/swarm-levels.md 2-1 ①): 띠의 두 가장자리에 점선 두 줄이 들어오는 변에서 0.3초에 걸쳐 뻗어 나온다. 점 2k×k, 6k 간격, 흐린 주황 (느낌표와 같은 색) ──
    const double LaneGrowSec = .3;
    static int LaneGrown(Swarm s, in Swarm.Lane ln) => (int)(Math.Clamp((s.Clock - ln.At) / LaneGrowSec, 0, 1) * 12);   // 뻗은 정도 (12칸으로 쪼개 바뀔 때만 다시 칠한다)

    // 한 줄의 점 하나하나 (들어오는 변에서 멀어지는 쪽으로)
    void DrawLane(Overlay o, Graphics g, Swarm s, in Swarm.Lane ln)
    {
        int k = s.K, pitch = 6 * k, dot = 2 * k, span = ln.Side < 2 ? s.Field.Width : s.Field.Height;
        int cells = (int)(span / (double)pitch * LaneGrown(s, ln) / 12.0) + 1;
        var f = s.Field;
        for (int line = 0; line < 2; line++)
        {
            int at = (int)(line == 0 ? ln.A : ln.B);
            for (int c = 0; c < cells; c++)
            {
                int d = c * pitch;
                if (d + dot > span) break;
                var r = ln.Side switch
                {
                    0 => new Rectangle(f.Left + d, f.Top + at - k / 2, dot, k),
                    1 => new Rectangle(f.Right - d - dot, f.Top + at - k / 2, dot, k),
                    2 => new Rectangle(f.Left + at - k / 2, f.Top + d, k, dot),
                    _ => new Rectangle(f.Left + at - k / 2, f.Bottom - d - dot, k, dot),
                };
                g.FillRectangle(SwarmArt.Amber, o.Client(r));
            }
        }
    }

    // 다시 칠할 자리: 줄마다 판을 가로지르는 가는 띠 둘
    static void LaneRects(Swarm s, in Swarm.Lane ln, List<Rectangle> into)
    {
        int k = s.K;
        var f = s.Field;
        foreach (double at in new[] { ln.A, ln.B })
            into.Add(ln.Side < 2 ? new Rectangle(f.Left, f.Top + (int)at - k, f.Width, 2 * k) : new Rectangle(f.Left + (int)at - k, f.Top, 2 * k, f.Height));
    }

    // ── 결과 화면 무기 줄 (docs/swarm-levels.md 2-3, YELL 2026-11-23 시안 2번): 결과 상자 아래 어두운 판 위에 무기 일곱의 단계 기록 (stageBest) ──
    // 등급 색 숫자 = 그 무기로 깬 가장 높은 단계, 못 깬 무기는 어두운 그림. 이 판이 기록을 올리면(이겼고 · 연습·자동 조종이 아니고 · 이번 단계가 기록보다 높다)
    // 깃발 ↑ 와 같은 때(0.6초 뒤) 새 숫자로 바뀌고 1.4초까지 반짝인다. 이번 판 무기 밑에 주황 밑줄
    const int RowPitch = 14;   // 무기 간격 (칸 수, ×k 가 px: 배율 3 이면 42px)

    int RowBest(Arm.Id id) => stageBest.GetValueOrDefault(Arm.Name(id));
    bool RowRaises(Swarm s) => s.Won && !practice && !auto && s.Stage > RowBest(s.Weapon.Which);
    int RowShown(Swarm s, Arm.Id id) => id == s.Weapon.Which && RowRaises(s) && s.EndedFor >= .6 ? s.Stage : RowBest(id);
    bool RowSparkle(Swarm s) => RowRaises(s) && s.EndedFor is >= .6 and < 1.4 && ((int)(s.EndedFor * 10) & 1) == 0;

    // 다시 칠할 자리: 판 + 테두리 + 반짝이
    Rectangle RowRect(Swarm s)
    {
        var (fx, fy, k) = ResultFlagAt(s);
        return Overlay.Around(fx, fy + 17 * k, 52 * k, 11 * k);
    }

    // 등급 색 숫자: x 는 첫 글자 가운데 (center 면 전체 가운데)
    void RowNumber(Overlay o, Graphics g, int n, int x, int y, int scale, int tier, int phase, bool center)
    {
        var glyphs = Stamps.Of(o.Art).Digits(scale);
        int len = GameHud.Digits(n), step = 5 * scale, x0 = center ? x - (len - 1) * step / 2 : x;
        for (int i = 0, div = (int)Math.Pow(10, len - 1); i < len; i++, div /= 10)
        {
            int d = n / div % 10;
            SwarmArt.Digit(glyphs[d], d, tier, phase, scale).Put(o, g, x0 + i * step, y);
        }
    }

    void DrawRow(Overlay o, Graphics g, Swarm s, int fx, int fy)
    {
        int k = s.K, small = GameHud.Small(k), phase = (int)(s.Clock * 2), n = Arm.Kinds, cur = (int)s.Weapon.Which;
        int iy = fy + 13 * k, dy = fy + 21 * k, sx = 0, sy = 0;   // 그림 가운데, 숫자 가운데, 반짝이 자리
        GameHud.Box(g, o.Client(new Rectangle(fx - 51 * k, fy + 7 * k, 102 * k, 20 * k)), k);
        for (int i = 0; i < n; i++)
        {
            var id = (Arm.Id)i;
            int cx = fx + (i - n / 2) * RowPitch * k, shown = RowShown(s, id);
            (shown > 0 ? SwarmArt.Icon(id, small) : SwarmArt.IconDim(id, small)).Put(o, g, cx, iy);
            if (shown > 0) RowNumber(o, g, shown, cx, dy, k, SwarmArt.Tier(shown), phase, true);
            if (i != cur) continue;
            g.FillRectangle(SwarmArt.Amber, o.Client(new Rectangle(cx - 4 * k, fy + 25 * k, 8 * k, k)));   // 이번 판 무기 밑줄
            (sx, sy) = (cx + GameHud.Digits(shown) * 5 * k / 2 + 2 * k, dy - 5 * k);
        }
        if (!RowSparkle(s)) return;
        spark.Put(o, g, sx, sy);
        spark.Put(o, g, sx + 3 * k, sy + 9 * k);   // 숫자 오른쪽 아래 (밑줄은 숫자 폭 안쪽이라 안 겹친다)
    }

    void DrawShot(Overlay o, Graphics g, Swarm s, ref Swarm.Shot t)
    {
        int k = s.K;
        var p = o.ToClient(t.X, t.Y);
        switch (t.Ammo)
        {
            case Swarm.Ammo.Rocket: // 로켓 그림 + 뒤에 연기 둘
                double v = Math.Sqrt(t.Vx * t.Vx + t.Vy * t.Vy);
                for (int i = 1; i <= 2; i++) Fx.Puff(g, p.X - (int)(t.Vx / v * (3 + 3 * i) * k), p.Y - (int)(t.Vy / v * (3 + 3 * i) * k), (4 - i) * k, .5);
                rocket[t.Vx < 0 ? 1 : 0].Draw(g, p.X, p.Y);
                break;
            case Swarm.Ammo.Pellet: // 산탄 알은 한 칸 작게
                int h = Math.Max(1, k - 1);
                g.FillRectangle(Pal.Line, p.X - h - 1, p.Y - h - 1, 2 * h + 2, 2 * h + 2);
                g.FillRectangle(Pal.Shot, p.X - h, p.Y - h, 2 * h, 2 * h);
                break;
            case Swarm.Ammo.Round: // 소총탄은 날아가는 쪽으로 두 칸
                double sp = Math.Sqrt(t.Vx * t.Vx + t.Vy * t.Vy);
                int bx = p.X - (int)(t.Vx / sp * k), by = p.Y - (int)(t.Vy / sp * k), hk = Math.Max(1, k * 2 / 3);
                g.FillRectangle(Pal.Line, bx - hk - 1, by - hk - 1, 2 * hk + 2, 2 * hk + 2);
                g.FillRectangle(Pal.Line, p.X - hk - 1, p.Y - hk - 1, 2 * hk + 2, 2 * hk + 2);
                g.FillRectangle(Pal.Flame[1], bx - hk, by - hk, 2 * hk, 2 * hk);
                g.FillRectangle(Pal.Shot, p.X - hk, p.Y - hk, 2 * hk, 2 * hk);
                break;
            default:
                g.FillRectangle(Pal.Line, p.X - k - 1, p.Y - k - 1, 2 * k + 2, 2 * k + 2);
                g.FillRectangle(Pal.Shot, p.X - k, p.Y - k, 2 * k, 2 * k);
                break;
        }
    }

    // 칼 자국: 겨눈 쪽 부채꼴 가장자리를 따라 초승달이 한쪽 끝에서 다른 끝으로 지나간다 (배율 칸에 맞춘 네모들).
    // 장검은 가운데가 두툼하고, 쌍검은 얇고 손마다 도는 쪽이 반대다. 회전 베기는 한 바퀴
    static void DrawSwing(Overlay o, Graphics g, Swarm s)
    {
        double t = (s.Clock - s.SwingAt) / s.SwingLen;
        if (t is < 0 or >= 1) return;
        bool twin = s.Weapon.Which == Arm.Id.Twin, thin = twin && !s.SwingSpin;
        double arc = s.SwingArc, reach = s.RangePx;
        int k = s.K, n = Math.Max(8, (int)(4 * arc * reach / k));
        for (int i = (int)(n * Math.Max(0, t * 2 - 1)); i <= n * Math.Min(1, t * 2); i++)
        {
            double u = (double)i / n, a = s.SwingDir + (twin && s.SwingHand < 0 ? 1 : -1) * (arc - 2 * arc * u);
            int deep = thin ? (int)Math.Round(1.4 * Math.Sin(Math.PI * u)) : 1 + (int)Math.Round(3 * Math.Sin(Math.PI * u)); // 장검: 끝은 두 칸, 가운데는 다섯 칸
            for (int d = 0; d <= deep; d++)
            {
                var p = o.ToClient(s.X + Math.Cos(a) * (reach - d * k), s.Y + Math.Sin(a) * (reach - d * k));
                g.FillRectangle(thin ? (d == 0 ? Pal.White : Pal.Slash) : d == 0 || d == deep ? Pal.Slash : Pal.White, p.X / k * k, p.Y / k * k, k, k);
            }
        }
    }

    // 자석을 주운 순간: 새 줍는 둘레를 점선 원으로 한 번 보여 준다 (배율 칸 네모)
    static void DrawRing(Overlay o, Graphics g, Swarm s)
    {
        int k = s.K, n = Math.Max(12, (int)(Math.PI * s.PickPx / (4 * k)) / 2 * 2);
        for (int i = 0; i < n; i += 2)
        {
            double a = i * 2 * Math.PI / n;
            var p = o.ToClient(s.X + Math.Cos(a) * s.PickPx, s.Y + Math.Sin(a) * s.PickPx);
            g.FillRectangle(Pal.Line, p.X - k - 1, p.Y - k - 1, 2 * k + 2, 2 * k + 2);
            g.FillRectangle(Pal.Gold, p.X - k, p.Y - k, 2 * k, 2 * k);
        }
    }

    // 예고: 판 안쪽을 가리키는 빨간 세모 (갈색 테두리). side: 세모가 서 있는 변
    static void DrawWarn(Overlay o, Graphics g, int k, int side, int x, int y)
    {
        var c = o.ToClient(x, y);
        int ax = side == 0 ? 1 : side == 1 ? -1 : 0, ay = side == 2 ? 1 : side == 3 ? -1 : 0;
        for (int pass = 0; pass < 2; pass++) // 테두리(한 칸 큰 세모) 다음에 속
            for (int i = 0; i < (pass == 0 ? 7 : 5); i++)
            {
                int along = (pass == 0 ? 4 : 3) - i; // 꼭짓점에서 i 칸 뒤의 줄 (폭 2i+1 칸)
                int w = (2 * i + 1) * k, px = c.X + ax * along * k - k / 2, py = c.Y + ay * along * k - k / 2;
                var r = ax != 0 ? new Rectangle(px, c.Y - w / 2, k, w) : new Rectangle(c.X - w / 2, py, w, k);
                g.FillRectangle(pass == 0 ? Pal.Line : Pal.Red, r);
            }
    }

    // 디버그: swarm / swarm weapon <id> / swarm auto [id] (알아서 한다, 기록 안 남김) / swarm at <초> / swarm boss [1~4] / swarm up [n] / swarm hp <n> / swarm item <heart|magnet|shoe|grenade> / swarm grenade (지금 던진다: 0개면 하나 채우고 쿨다운을 지운다) / swarm stop /
    //   swarm level <n> (n 단계로, 하는 중이면 같은 무기로 새 판: 연습 판이라 기록·단계가 안 바뀐다) / swarm level set <n> (저장된 단계를 n 으로, 깬 단계는 n-1) /
    //   swarm rush [wall|ring|dart|pincer] (지금 예고를 띄우고 1초 뒤 그 모양으로, 안 주면 차례대로) / swarm elite [bug|wasp|beetle|steel] (볼록이 옆에 엘리트 하나) / swarm steel (강철 딱정벌레 하나) / swarm boss twin (3번 보스 + 마주 보는 둘째) /
//   swarm special (다음 공격에 특수기가 나온다: 산탄총·쌍검·쌍권총·바주카포·화염방사기만) / swarm feel [on|off] (타격감) / swarm feel shake|stop on|off / swarm pop <bug|wasp|beetle|elite|steel|boss> [수]
    public bool Command(string[] a)
    {
        if (a.Length == 0 || a[0] != Id) return false;
        string sub = a.Length > 1 ? a[1] : "", arg = a.Length > 2 ? a[2] : "";
        int.TryParse(arg, out int n);
        var s = swarm;
        switch (sub)
        {
            case "stop": Stop(true); break;
            case "weapon": if (Arm.Find(arg, out var id)) StartWith(id); break;
            case "auto":
                if (Arm.Find(arg, out var autoId)) StartWith(autoId);
                else if (!Running) StartWith(null);
                auto = Running;
                break;
            case "at": if (s != null && double.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double sec)) s.SetClock(sec); break;
            case "boss": if (arg == "twin") s?.CallTwin(); else s?.CallBoss(n > 0 ? n : Math.Min(Swarm.Bosses, s.BossesKilled + 1)); break;
            case "level":
                if (arg == "set") { if (int.TryParse(a.Length > 3 ? a[3] : "", out int set)) { stage = Math.Clamp(set, 1, MaxStageNo); maxStage = stage - 1; host.Save(); } }
                else if (n >= 1) StartLevel(Math.Min(n, MaxStageNo));
                break;
            case "rush": s?.CallRush(arg switch { "wall" => Swarm.Shape.Wall, "ring" => Swarm.Shape.Ring, "dart" => Swarm.Shape.Dart, "pincer" => Swarm.Shape.Pincer, _ => null }); break;
            case "elite": if (arg == "steel") s?.CallSteel(true); else s?.CallElite(arg switch { "wasp" => Swarm.Kind.Wasp, "beetle" => Swarm.Kind.Beetle, _ => Swarm.Kind.Bug }); break;
            case "steel": s?.CallSteel(false); break;
            case "up": for (int i = 0; s != null && i < Math.Clamp(n, 1, 60); i++) s.LevelUp(); break;
            case "hp": if (s != null && n > 0) { s.Hp = Math.Min(Swarm.MaxHp, n); s.Heal(0); } break;
            case "item":
                if (s != null && arg is "heart" or "magnet" or "shoe" or "grenade") s.Drop(arg == "heart" ? Swarm.Item.Heart : arg == "magnet" ? Swarm.Item.Magnet : arg == "grenade" ? Swarm.Item.Grenade : Swarm.Item.Shoe);
                break;
            case "grenade": s?.ThrowNow(); break;
            case "special": s?.ChargeNow(); break;
            case "feel":   // 타격감 (docs/swarm-feel.md 6장): swarm feel [on|off] · swarm feel shake|stop on|off
                bool feelOn = (a.Length > 3 ? a[3] : arg) != "off";
                if (arg == "shake") Feel.Shake = feelOn ? 1 : 0;
                else if (arg == "stop") Feel.StopBoss = feelOn ? .05 : 0;
                else Feel.On = feelOn;
                break;
            case "pop":    // swarm pop <bug|wasp|beetle|elite|steel|boss> [수]: 볼록이 옆에 그만큼 놓고 바로 터뜨린다
                if (s != null)
                {
                    int count = a.Length > 3 && int.TryParse(a[3], out int pn) ? pn : 1;
                    if (arg == "wasp") s.CallPop(Swarm.Kind.Wasp, count);
                    else if (arg == "beetle") s.CallPop(Swarm.Kind.Beetle, count);
                    else if (arg == "elite") s.CallPop(Swarm.Kind.Bug, count, elite: true);
                    else if (arg == "steel") s.CallPop(Swarm.Kind.Beetle, count, steel: true);
                    else if (arg == "boss") s.CallPop(Swarm.Kind.Boss, 1);
                    else s.CallPop(Swarm.Kind.Bug, count);
                }
                break;
            default: StartWith(null); break;
        }
        return true;
    }

    // 그 무기로 시작한다 (null 이면 무작위). 하는 중이면 그 무기로 같은 자리에서 새 판 (단계는 그대로)
    void StartWith(Arm.Id? weapon)
    {
        if (swarm is { } s) { if (weapon is { } w) Begin(NewRun(s.Field, s.K, w, s.Stage)); }
        else if (!host.Busy) { pick = weapon; Start(); }
    }

    // n 단계 연습 판: 하는 중이면 같은 자리·같은 무기로 새 판, 아니면 무작위 무기로 시작. 기록·단계는 안 바뀐다
    void StartLevel(int n)
    {
        if (swarm is { } s) { practice = true; Begin(NewRun(s.Field, s.K, s.Weapon.Which, n)); }
        else if (!host.Busy) { (pick, forceStage) = (null, n); Start(); }
    }

    public IEnumerable<GameStat> Stats()
    {
        bool played = bestSec > 0 || bestKills > 0;
        yield return new GameStat(L.T("game.swarm"), played ? L.T("game.swarm_val2", $"{bestSec / 60}:{bestSec % 60:00}", bestKills, stage) : L.T("game.never"), played);
    }

    // 기록. 옛 기록 {"Sec":120,"Kills":300} 도 그대로 읽힌다 (없는 칸은 0·null). 무기는 enum 번호가 아니라 글 이름으로 (무기를 넣고 빼도 안 깨진다)
    // 단계 칸(Stage·MaxStage·StageBest)은 옛 기록에 없으면 0·null → 1단계부터. 옛 Wins 는 쉬운 규칙에서 딴 것이라 단계로 바꿔 주지 않는다
    sealed record Record(int Sec, int Kills, int Wins = 0, int Clears = 0, string? Last = null, Dictionary<string, int>? Best = null,
        int Stage = 0, int MaxStage = 0, Dictionary<string, int>? StageBest = null);
    public string? SaveState() => bestSec > 0 || bestKills > 0 || lastWeapon != null || stage > 1 || maxStage > 0
        ? JsonSerializer.Serialize(new Record(bestSec, bestKills, wins, clears, lastWeapon is { } w ? Arm.Name(w) : null, best.Count > 0 ? best : null, stage, maxStage, stageBest.Count > 0 ? stageBest : null)) : null;

    public void LoadState(string? json)
    {
        if (json == null) return;
        try
        {
            if (JsonSerializer.Deserialize<Record>(json) is not { } r) return;
            (bestSec, bestKills, wins, clears) = (r.Sec, r.Kills, r.Wins, r.Clears);
            lastWeapon = Arm.Find(r.Last, out var last) ? last : null;   // 모르는 무기 이름은 버린다
            best = r.Best != null ? new(r.Best) : new();                 // 무기별 기록의 모르는 이름은 그냥 둔다
            (stage, maxStage) = (Math.Clamp(r.Stage, 1, MaxStageNo), Math.Clamp(r.MaxStage, 0, MaxStageNo));
            stageBest = r.StageBest != null ? new(r.StageBest) : new();
        }
        catch (JsonException) { /* 깨진 기록은 버린다 */ }
    }
}
