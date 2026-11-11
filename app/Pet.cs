namespace Bolok;

// 볼록이 상태 모델 — 규칙 전부. 화면과 무관하게 돈다.
// preview/model.js 를 옮긴 것이고, `Bolok.exe --selfcheck` 로 같은 일주일 시뮬레이션을 돌려 맞춘다.
// 시간 단위는 "활동 분": PC 를 쓰는 시간만 센다. 앱이 꺼져 있거나 자리를 비우면 멈춘다.

// 수치. 디버그 때는 run/tuning.json 으로 덮어써서 앱을 끄지 않고 맞춘다
public sealed class Tuning
{
    public double FullPerH { get; set; } = 20;      // 100 → 0 까지 5시간
    public double MoodPerH { get; set; } = 25;      // 4시간
    public double EnergyPerH { get; set; } = 15;    // 6.7시간
    public double RegenPerH { get; set; } = 200;    // 낮잠 30분이면 기운이 다 찬다
    public double FeedFull { get; set; } = 35;
    public double FeedMood { get; set; } = 3;
    public double PetMood { get; set; } = 8;
    public double PlayMood { get; set; } = 40;
    public double PlayEnergy { get; set; } = 8;
    public double PlayFull { get; set; } = 5;
    public double WakeMood { get; set; } = -5;      // 억지로 깨우면 삐진다
    public double CallAt { get; set; } = 25;        // 이 아래로 내려가면 말풍선으로 부른다
    public double GraceMin { get; set; } = 15;      // 0 에서 15분 방치 → 돌봄 실수 (P1 의 15분 규칙)
    public double MissEveryMin { get; set; } = 60;  // 이후 1시간마다 하나 더
    public double BiteEveryMin { get; set; } = 10;  // 기분 0 에서 15분 뒤부터 10분마다 한 입
    public int MaxBites { get; set; } = 12;
    public double SpitMood { get; set; } = 30;      // 기분이 풀리면 3초에 한 입씩 뱉는다
    public double SpitMin { get; set; } = 0.05;
    public double EggMin { get; set; } = 5;
    public double BabyMin { get; set; } = 65;
    public double ChildMin { get; set; } = 38 * 60; // 월요일에 부화해서 금요일 오후에 어른 (하루 8시간)
    public int GrumpyBites { get; set; } = 12;
    public int GrumpyMiss { get; set; } = 8;
    public double SweetRatio { get; set; } = 3;

    // 화면 쪽
    public int Scale { get; set; } = 0;             // 스프라이트 배율. 0 이면 DPI 에 맞춘다
    public double AfkMin { get; set; } = 10;        // 입력이 없으면 자리 비움
    public double WalkSpeed { get; set; } = 70;     // 배율 3 기준 초당 픽셀
    public double RoamLow { get; set; } = 0.75;     // 화면 아래쪽에서 돌아다닐 확률 — 일하는 곳을 덜 가린다
    public double RoamOtherScreen { get; set; } = 0.12; // 돌아다닐 때 다른 모니터로 건너갈 확률
    public int FloorGap { get; set; } = 50;         // 화면 맨 아래에서 띄울 픽셀
    public double PetCooldownSec { get; set; } = 1.5; // 연타해도 이 간격으로만 수치에 들어간다

    // 던지기 (속도는 배율 3 기준 초당 픽셀)
    public double ThrowMinSpeed { get; set; } = 500;  // 이보다 느리게 놓으면 그 자리에 내려놓기
    public double ThrowMaxSpeed { get; set; } = 4000;
    public double DizzySpeed { get; set; } = 2500;    // 이보다 세게 던지면 어지러워한다
    public double Gravity { get; set; } = 2400;
    public double Bounce { get; set; } = 0.55;        // 튕길 때 남는 속도 비율
    public double RestSpeed { get; set; } = 160;      // 바닥에서 이보다 느리면 멈춘다
    public double TossMood { get; set; } = 4;         // 살살 던지면 즐겁다
    public double CrashMood { get; set; } = -4;       // 세게 던지면 싫다
    public double TossCooldownSec { get; set; } = 6;  // 연속으로 던져도 이 간격으로만 수치에 들어간다

    // 잡고 흔들기
    public int ShakeReversals { get; set; } = 3;      // 0.8초 안에 방향이 이만큼 뒤집히면 흔드는 것
    public double ShakeDizzySec { get; set; } = .6;   // 이만큼 흔들리면 놓았을 때 어지러워한다

    public double NapAt { get; set; } = 45;           // 기운이 이 아래면 창 위로 자러 간다
    public double NapChance { get; set; } = .15;      // 혼자 놀다가 낮잠을 잘 확률 (기운 80 이하)
    public double GameLayerOpacity { get; set; } = .01; // 게임판 진하기. 클릭만 받고 눈에는 안 보이게
    public int GameWindowProps { get; set; } = 8;     // 창 윗변에 앉힐 소품 수 최대
    public double PreySpeed { get; set; } = 330;      // 총쏘기 게임에서 도망가는 속도 (배율 3 기준)
    public double TossSec { get; set; } = .42;        // 씨앗이 날아오는 시간
    public double ClickHoldSec { get; set; } = 3;     // 누른 뒤 이만큼은 제자리에 있는다 — 움직이면 뒤 창을 누르게 된다
    public double CallEverySec { get; set; } = 30;    // 배고픔·심심함을 그림으로 알리는 간격

    // 활성 창 피하기와 혼자 놀기
    public double RoamOnWindow { get; set; } = .6;    // 돌아다닐 때 비활성 창 위를 고를 확률
    public double SoloChance { get; set; } = .65;     // 쉬다가 돌아다니는 대신 혼자 놀 확률
    public double EscapeSpeed { get; set; } = 480;    // 활성 창 위에 있을 때 빠져나가는 속도
    public double StartleCooldownSec { get; set; } = 6; // 이 안에 또 활성화되면 반응 없이 바로 비켜난다
    public double KnockMin { get; set; } = 700;       // 창에 맞았을 때 날아가는 속도
    public double KnockMax { get; set; } = 2200;
    public double KnockGain { get; set; } = .4;       // 창 속도가 날아가는 속도에 더해지는 비율

    // 창 뒤로 숨기
    public double HideMinSec { get; set; } = 60;      // 숨바꼭질 간격
    public double HideMaxSec { get; set; } = 240;
    public double HideStayMinSec { get; set; } = 40;  // 숨어 있는 시간
    public double HideStayMaxSec { get; set; } = 150;
    public double FoundMood { get; set; } = 5;        // 찾아주면 놀아준 셈이다
}

public sealed class Pet
{
    public string Stage { get; set; } = "egg";
    public double AgeMin { get; set; }
    public double Full { get; set; } = 80;
    public double Mood { get; set; } = 80;
    public double Energy { get; set; } = 100;
    public bool Asleep { get; set; }
    public int Bites { get; set; }
    public double SulkMin { get; set; }
    public double BiteIn { get; set; }
    public double SpitIn { get; set; }
    public double ZeroFull { get; set; }
    public double ZeroMood { get; set; }
    public int Miss { get; set; }
    public int Feeds { get; set; }
    public int Pets { get; set; }
    public int Plays { get; set; }
    public int BitesTotal { get; set; }

    static double Clamp(double v) => Math.Clamp(v, 0, 100);

    string AdultForm(Tuning k)
    {
        if (BitesTotal >= k.GrumpyBites || Miss >= k.GrumpyMiss) return "adult_grumpy";
        return Pets >= Feeds * k.SweetRatio ? "adult_sweet" : "adult_chubby";
    }

    // m 활동 분이 지났다. afk 면 자리 비움. 일어난 일을 돌려준다
    public List<string> Tick(Tuning k, double m, bool afk = false)
    {
        var ev = new List<string>();
        if (Stage == "egg")
        {
            if (afk) return ev;
            AgeMin += m;
            if (AgeMin >= k.EggMin) { Stage = "baby"; ev.Add("hatch"); }
            return ev;
        }
        if (afk && !Asleep) { Asleep = true; ev.Add("sleep"); }
        if (Asleep) // 자는 동안 배고픔·기분·나이는 멈춘다. 주말이 지나도 안 죽는다
        {
            Energy = Clamp(Energy + k.RegenPerH * m / 60);
            if (!afk && Energy >= 100) { Asleep = false; ev.Add("wake"); }
            return ev;
        }

        AgeMin += m;
        Full = Clamp(Full - k.FullPerH * m / 60);
        Mood = Clamp(Mood - k.MoodPerH * m / 60);
        Energy = Clamp(Energy - k.EnergyPerH * m / 60);
        if (Energy <= 0) { Asleep = true; ev.Add("sleep"); }

        ZeroFull = Full > 0 ? 0 : ZeroFull + m;
        if (ZeroFull >= k.GraceMin) { Miss++; ZeroFull -= k.MissEveryMin; ev.Add("miss"); }
        ZeroMood = Mood > 0 ? 0 : ZeroMood + m;
        if (ZeroMood >= k.GraceMin) { Miss++; ZeroMood -= k.MissEveryMin; ev.Add("miss"); }

        SulkMin = Mood > 0 ? 0 : SulkMin + m;
        if (SulkMin >= k.GraceMin)
        {
            BiteIn -= m;
            if (BiteIn <= 0 && Bites < k.MaxBites)
            {
                Bites++; BitesTotal++; BiteIn = k.BiteEveryMin; ev.Add("bite");
            }
        }
        else BiteIn = 0;

        if (Bites > 0 && Mood >= k.SpitMood)
        {
            SpitIn -= m;
            if (SpitIn <= 0) { Bites--; SpitIn = k.SpitMin; ev.Add("spit"); }
        }

        if (Stage == "baby" && AgeMin >= k.BabyMin) { Stage = "child"; ev.Add("grow"); }
        if (Stage == "child" && AgeMin >= k.ChildMin) { Stage = AdultForm(k); ev.Add("grow"); }
        return ev;
    }

    // 돌봄. 안 먹히면 false — 도리도리로 보여준다
    public bool Act(Tuning k, string what)
    {
        if (Stage == "egg") return false;
        if (Asleep && what != "wake") return false;
        switch (what)
        {
            case "feed":
                if (Full >= 100) return false;
                Full = Clamp(Full + k.FeedFull); Mood = Clamp(Mood + k.FeedMood); Feeds++;
                return true;
            case "pet":
                Mood = Clamp(Mood + k.PetMood); Pets++;
                return true;
            case "play":
                if (Energy < k.PlayEnergy) return false;
                Mood = Clamp(Mood + k.PlayMood); Energy = Clamp(Energy - k.PlayEnergy);
                Full = Clamp(Full - k.PlayFull); Plays++;
                return true;
            case "found":
                Mood = Clamp(Mood + k.FoundMood);
                return true;
            case "toss":
                Mood = Clamp(Mood + k.TossMood);
                return true;
            case "crash":
                Mood = Clamp(Mood + k.CrashMood);
                return true;
            case "sleep":
                if (Energy > 80) return false;
                Asleep = true;
                return true;
            case "wake":
                if (!Asleep) return false;
                Asleep = false; Mood = Clamp(Mood + k.WakeMood);
                return true;
        }
        return false;
    }

    public string? Need(Tuning k) => Stage == "egg" || Asleep ? null
        : Full < k.CallAt ? "hungry" : Mood < k.CallAt ? "bored" : Energy < k.CallAt ? "sleepy" : null;

    public string RestPose(Tuning k) => Asleep ? "sleep" : Full < k.CallAt ? "hungry" : Mood < k.CallAt ? "sad" : "idle";

    public int Cheek(Tuning k) => (int)Math.Ceiling(Bites / (k.MaxBites / 3.0));
}
