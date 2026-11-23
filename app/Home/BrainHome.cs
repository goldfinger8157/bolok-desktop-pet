namespace Bolok;

// 방석·집 자리 (HomeLife 가 채워 준다 — Brain 은 화면을 모른다. 자가 검사에서는 가짜 자리로 돌린다). 길이는 모두 화면 픽셀
public struct HomeSpot
{
    public bool On;                 // false 면 없음 (안 받았거나, 숨겼거나, 게임 중이거나, 아기·알일 때)
    public bool Moving;             // 끄는 중: 펫은 따라 움직이지만, 다른 행동이 이 물건을 목적지로 잡지 않는다
    public double X, Y;             // 서 있는 자리(발): 방석은 앉는 자리, 집은 문 앞(빼꼼) 자리. Y 는 펫의 바닥(MaxY)과 같다
    public double SleepX, SleepDy;  // 잘 때: 몸통 가운데에 맞춰 옮기는 X, 더 내려가는 픽셀 (집 — 문 안에서 더 낮다)
    public double SitDy;            // 방석에 앉아 있을 때(idle 계열) 위로 올리는 픽셀 (음수): 서 있는 그림이 누운 그림보다 두 칸 낮아서
    public bool HasTop;             // 캣타워처럼 위에 올라가 자는 자리가 있다 (어른만)
    public double TopX, TopY;
}

public enum HomeUse { None, Sit, Nap, Peek, Sleep }
public enum HomeClipKind { None, Front, Inside } // 펫을 가릴 자리: 방석·캣타워 위는 앞 겹만, 집 안은 문 안 말고 전부 + 앞 겹

// 행동 쪽 방석·집 (docs/home.md 6장). 새 모드 둘: GoHome(가는 중)·Home(붙어 있음).
public sealed partial class Brain
{
    public HomeSpot Cushion, House;
    public Region? HomeClip;           // 본체에서 안 그릴 자리 (화면 좌표, HomeLife 가 만들어 준다). PetForm 이 PuppetClip 처럼 뺀다
    public Func<bool> NightNow = () => DateTime.Now.Hour is >= 21 or < 6;  // 밤 21~06시 (집을 더 고른다). 자가 검사는 바꿔 쓴다

    HomeUse homeUse;
    bool homeHouse, homeTop, homeWalkAsleep, homeFast, homeCheer;
    int homeStep;                      // 0 빼꼼·앉기, 1 캣타워 오르기, 2 자는 중, 3 깨서 내다보기·기지개
    double homeUntil, homeStay, homeYoff, homeClimbT, homePoseAt, homeCheerUntil;
    string homePose = "idle";

    public HomeUse AtHome => Mode == Mode.Home ? homeUse : HomeUse.None;
    public bool HomeIsHouse => homeHouse;

    // 지금 펫을 어떻게 가려야 하나 (HomeLife 가 Region 을 만든다)
    public HomeClipKind HomeClipNow => Mode != Mode.Home ? HomeClipKind.None : homeHouse && !homeTop ? HomeClipKind.Inside : HomeClipKind.Front;

    ref HomeSpot SpotOf(bool house) { if (house) return ref House; return ref Cushion; }

    bool Idleish => Mode is Mode.Wander or Mode.Anim;

    // 갈 수 있으면 true. fast: 받는 순간의 달려가기(×1.3), stay: 앉기·빼꼼 시간(0 이면 무작위), cheer: 도착해서 기뻐하기(하트)
    public bool GoHome(bool house, HomeUse use, bool fast = false, double stay = 0, bool cheer = false)
    {
        ref var s = ref SpotOf(house);
        if (!s.On || s.Moving || P.Stage is "egg" or "baby" || !Idleish) return false;
        if (house && !Pet.Grown(P.Stage) && use == HomeUse.Sleep) return false; // 집에서 자는 것은 다 자란 뒤
        (homeHouse, homeUse, homeFast, homeStay, homeCheer) = (house, use, fast, stay, cheer);
        homeWalkAsleep = P.Asleep;
        homeStep = 0;
        homeTop = false;
        homeYoff = 0;
        then = null;
        (solo, tailSpin, fleeing) = (false, false, false);
        Mode = Mode.GoHome;
        Dbg.Log("home-go", ("house", house), ("use", use.ToString()), ("fast", fast));
        return true;
    }

    // 걷지 않고 바로 거기서 (오래 꺼져 있다 켰을 때 집 안에서 시작)
    public bool EnterHome(bool house, HomeUse use)
    {
        ref var s = ref SpotOf(house);
        if (!s.On) return false;
        (homeHouse, homeUse, homeFast, homeStay, homeCheer, homeWalkAsleep) = (house, use, false, 0, false, false);
        Mode = Mode.GoHome;
        ArriveHome(true);
        return true;
    }

    // 디버그: 지금 방석·집에서 나온다 (자던 중이면 깨서 기지개·내다보기를 거쳐)
    public void HomeOut()
    {
        if (Mode != Mode.Home) return;
        if (P.Asleep) P.Asleep = false;
        else homeUntil = Now;
    }

    // 방석·집에서 나온다 (끄기·없어졌을 때): 자던 중이면 그 자리에서 계속 잔다
    void LeaveHome()
    {
        Mode = Mode.Wander;
        next = Now + 1;
        Y = Math.Clamp(Y, MinY, MaxY);
        Dbg.Log("home-out", ("house", homeHouse));
    }

    void ExitHome()
    {
        ref var s = ref SpotOf(homeHouse);
        if (homeHouse && !homeTop)
        {
            double dir = X < Area.Left + Area.Width / 2.0 ? 1 : -1; // 화면 가운데 쪽으로 0.9칸 걸어 나온다
            Y = s.Y;
            LeaveHome();
            GoTo(Math.Clamp(X + dir * CellPx * .9, MinX, MaxX), Y);
            return;
        }
        if (homeTop) { LeaveHome(); JumpDown(); return; } // 캣타워 위: 깨면 폴짝 뛰어내린다
        LeaveHome();
        next = Now + Rand(.6, 2);
    }

    void ArriveHome(bool instant)
    {
        ref var s = ref SpotOf(homeHouse);
        bool top = homeUse is HomeUse.Sleep or HomeUse.Nap && s.HasTop && Pet.Grown(P.Stage);
        Mode = Mode.Home;
        Dir = 1;
        homeTop = top;
        homeCheerUntil = homeCheer ? Now + 1.2 : 0;
        if (homeCheer) Emote("heart", 1.2);
        X = top ? s.TopX : homeUse is HomeUse.Sleep or HomeUse.Nap ? s.SleepX : s.X;
        Y = s.Y;
        Dbg.Log("home-in", ("house", homeHouse), ("use", homeUse.ToString()), ("top", top));
        if (homeUse is HomeUse.Sleep or HomeUse.Nap)
        {
            bool already = P.Asleep && (homeWalkAsleep || instant); // 이미 자던 채 걸어왔다: 빼꼼 없이 바로
            if (top) { homeStep = 1; homeClimbT = 0; return; }
            if (homeHouse && !already) { homeStep = 0; homeUntil = Now + Rand(K.PeekMinSec, K.PeekMaxSec); return; } // 문 앞에서 내다본 뒤 잔다
            FallAsleepHome(instant);
            return;
        }
        homeStep = 0;
        homeUntil = Now + (homeStay > 0 ? homeStay : homeUse == HomeUse.Peek ? 2 : Rand(K.SitMinSec, K.SitMaxSec)) + (homeCheer ? 1.2 : 0);
        homePoseAt = 0;
    }

    void FallAsleepHome(bool instant)
    {
        if (!P.Asleep) { P.Asleep = true; Emote("zzz"); }
        homeStep = 2;
        Pose = "sleep";
        ref var s = ref SpotOf(homeHouse);
        if (instant) homeYoff = homeTop ? 0 : s.SleepDy;
    }

    void UpdateGoHome(double dt)
    {
        ref var s = ref SpotOf(homeHouse);
        if (!s.On || P.Stage == "egg" || (homeWalkAsleep && !P.Asleep)) { Mode = Mode.Wander; next = Now + 1; return; } // 없어졌다·걷다가 깼다
        bool top = homeUse is HomeUse.Sleep or HomeUse.Nap && s.HasTop && Pet.Grown(P.Stage);
        bool sleepy = homeUse is HomeUse.Sleep or HomeUse.Nap;
        Pose = "walk";
        double speed = homeFast ? K.HomeSpeed * 1.3 : P.Asleep ? K.HomeSleepySpeed : K.HomeSpeed;
        double gx = top ? s.TopX : sleepy ? s.SleepX : s.X;
        if (World.NextArea(Area, World.At(gx, s.Y), CellPx) == null && World.At(gx, s.Y) != Area) { Mode = Mode.Wander; next = Now + 1; return; } // 이어지지 않은 모니터
        if (Go(gx, s.Y, speed, dt)) ArriveHome(false);
    }

    void UpdateHome(double dt)
    {
        ref var s = ref SpotOf(homeHouse);
        if (!s.On || P.Stage == "egg") { LeaveHome(); return; }
        Dir = 1;
        bool sleepUse = homeUse is HomeUse.Sleep or HomeUse.Nap;
        double baseX = homeTop ? s.TopX : sleepUse && homeStep >= 1 ? s.SleepX : s.X;
        X = baseX;
        switch (homeStep)
        {
            case 0: // 앉기·빼꼼
                Pose = homeUse == HomeUse.Sit ? SitPose() : "idle";
                if (Now < homeCheerUntil) Pose = "happy";
                if (sleepUse && P.Asleep) { FallAsleepHome(false); break; } // 내다보는 사이 억지로 재웠다
                if (Now >= homeUntil)
                {
                    if (sleepUse) { FallAsleepHome(false); break; }
                    ExitHome();
                    return;
                }
                break;
            case 1: // 캣타워 오르기 (어른은 상자 위 방석에서 잔다)
                homeClimbT += dt / .45;
                Pose = "hop";
                if (homeClimbT >= 1) { homeClimbT = 1; FallAsleepHome(true); homeYoff = 0; }
                Y = s.Y + (s.TopY - s.Y) * Math.Min(1, homeClimbT) - Math.Sin(Math.Min(1, homeClimbT) * Math.PI) * 14 * S;
                return;
            case 2: // 자는 중: 문 안에서 0.25초에 걸쳐 더 내려간다
                Pose = "sleep";
                if (!P.Asleep) { homeStep = 3; homeUntil = Now + (homeHouse && !homeTop ? K.HouseExitPeekSec : 1.2); break; }
                break;
            case 3: // 깼다: 집은 빼꼼 0.8초, 방석은 기지개 1.2초
                Pose = homeHouse && !homeTop ? "idle" : "stretch";
                if (Now >= homeUntil) { ExitHome(); return; }
                break;
        }
        double target = homeStep == 2 && !homeTop ? s.SleepDy : 0;
        double rate = Math.Max(Math.Abs(s.SleepDy), 1) / K.HomeSleepDownSec * dt;
        homeYoff += Math.Clamp(target - homeYoff, -rate, rate);
        Y = homeTop ? s.TopY : s.Y + homeYoff + (!homeHouse && homeUse == HomeUse.Sit ? s.SitDy : 0);
    }

    // 제 방석·집 안에서 쓰다듬으면 튀어 나가지 않고 하트만 (수치는 쓰다듬기대로)
    void HomePet()
    {
        if (Now >= nextPet) { P.Act(K, "pet"); nextPet = Now + K.PetCooldownSec; }
        Emote("heart", 1.0);
        lastPoke = Now;
    }

    // 앉아 있는 동안 털 고르기·생각·가만히를 번갈아
    string SitPose()
    {
        if (Now >= homePoseAt)
        {
            homePoseAt = Now + Rand(2.5, 4);
            homePose = rng.Next(3) switch { 0 => "groom", 1 => "think", _ => "idle" };
        }
        return homePose;
    }

    // 쉬러 간다 (낮잠 갈 때 — 기운이 낮을 때·혼자 놀기·메뉴 「재우기」): 방석 3 : 창 위 1 : 집 1 (밤엔 집 6) 무게로 하나.
    // 다른 모니터 것은 무게 ÷3, 눈·비로 얌전한 중엔 1.5칸보다 먼 것은 빼고, 활성 창 밑인 것도 뺀다. 아무것도 없으면 지금처럼 창 위 → 없으면 false
    public bool TryRest()
    {
        if (P.Stage == "egg" || P.Asleep) return false;
        if (Mode == Mode.Home && homeUse is HomeUse.Sit or HomeUse.Peek) // 앉아 있다가 재운다: 그 자리에서
        {
            homeUse = homeHouse ? HomeUse.Sleep : HomeUse.Nap;
            FallAsleepHome(false);
            return true;
        }
        if (!Idleish) return TryNap();
        double night = NightNow() ? K.NightHouse : K.NapHouse;
        double wc = RestWeight(Cushion, false) * K.NapCushion, wh = RestWeight(House, true) * night;
        double wp = HideSpots.FindPerch(Windows(), World.Areas, CellPx, rng).Count > 0 ? K.NapPerch : 0;
        double sum = wc + wh + wp;
        if (sum <= 0) return TryNap();
        double r = rng.NextDouble() * sum;
        if (r < wc) { if (GoHome(false, HomeUse.Nap)) return true; }
        else if (r < wc + wh) { if (GoHome(true, HomeUse.Sleep)) return true; }
        return TryNap();
    }

    // 그 자리로 갈 수 있나 (1 = 같은 모니터, 1/3 = 다른 모니터, 0 = 못 간다)
    double RestWeight(HomeSpot s, bool house)
    {
        if (!s.On || s.Moving || (house && !Pet.Grown(P.Stage))) return 0;
        var to = World.At(s.X, s.Y);
        double w = 1;
        if (to != Area) { if (World.NextArea(Area, to, CellPx) == null) return 0; w = 1.0 / K.NapOtherDiv; }
        if (OnActive(s.X, s.Y)) return 0;
        if (Calm && Math.Abs(s.X - X) > CellPx * 1.5) return 0;
        return w;
    }

    // 혼자 놀다가 방석에 그냥 앉아 있기 (수치는 안 바뀐다): 방석까지 화면 폭의 0.6 안일 때만
    bool TrySit()
    {
        if (!Cushion.On || Cushion.Moving || P.Stage is "egg" or "baby" || P.Asleep) return false;
        if (rng.NextDouble() >= (P.Energy <= 65 ? K.SitChanceTired : K.SitChance)) return false;
        if (Math.Abs(Cushion.X - X) > Area.Width * K.SitReach || World.At(Cushion.X, Cushion.Y) != Area || OnActive(Cushion.X, Cushion.Y)) return false;
        return GoHome(false, HomeUse.Sit);
    }

    // 자리 비움으로 잠들 때: 집(없으면 방석)이 있으면 졸린 걸음으로 가서 들어가 잔다. 기운 0 으로 쓰러진 잠은 그 자리 그대로 (갈 힘이 없다)
    void SleepAtHome()
    {
        if (Mode != Mode.Wander || P.Energy <= 0) return;
        if (House.On && !House.Moving && Pet.Grown(P.Stage) && RestWeight(House, true) > 0) GoHome(true, HomeUse.Sleep);
        else if (Cushion.On && !Cushion.Moving && RestWeight(Cushion, false) > 0) GoHome(false, HomeUse.Nap);
    }
}
