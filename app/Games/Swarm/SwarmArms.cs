namespace Bolok;

// 버그 떼 무기 (YELL 2026-11-16: 판마다 무기 하나를 무작위로 받아 5분을 버틴다). ==무기 숫자는 모두 여기 한곳==
// 단위는 배율 3 기준 px·초 (규칙에서 × sf). 피해·체력은 정수 (보통 벌레 체력 10)
// Arm 하나 = 이번 판에 든 무기의 「지금 값」: 레벨이 오르면 Grow 로 세 갈래(빠르게·넓게·세게) 가운데 하나가 자란다
sealed class Arm
{
    public enum Id { Twin, Long, Pistols, Shotgun, Rifle, Bazooka, Flame }
    public enum Sort { Melee, Bullets, Rocket, Flame }   // 공격 갈래 (규칙의 Attack 이 이걸로 나눈다)

    public const int Kinds = 7;
    static readonly string[] names = { "twin", "long", "pistols", "shotgun", "rifle", "bazooka", "flame" };   // 디버그 명령·기록에 쓰는 이름 (enum 번호가 아니라 글)
    public static string Name(Id id) => names[(int)id];
    public static bool Find(string? name, out Id id)
    {
        int at = name == null ? -1 : Array.IndexOf(names, name);
        id = (Id)Math.Max(0, at);
        return at >= 0;
    }

    public Id Which;
    public Sort Kind;
    public int Dmg;            // 한 번(한 발·한 알·한 틱)의 피해
    public double Cool;        // 다음 공격까지 (화염은 틱 간격)
    public double Range;       // 닿는 거리
    public double ArcHalf;     // 근접·화염: 부채꼴의 절반 각, 산탄: 퍼짐의 절반, 소총: 흔들림
    public int Count = 1;      // 쌍권총: 표적 수, 산탄: 알 수
    public int Pierce;         // 뚫고 지나가는 벌레 수 (0 이면 처음 맞은 놈에서 멈춘다)
    public double Knock, Stun; // 밀침(px), 멈칫(초). 보스는 밀침 ¼·멈칫 없음
    public double Blast;       // 바주카: 폭발 반지름
    public double Speed;       // 탄속
    public int BurnDmg;        // 화염: 불붙은 벌레가 0.5초마다 받는 피해
    public double BurnSec;     // 불길을 벗어난 뒤 불이 남는 시간
    public double BotGap;      // 자동 조종이 벌레와 지키고 싶은 틈 (무기마다 다르다: 근접은 붙어야 친다)
    public double Guard = 1;   // 닿았을 때 받는 피해 배율 (근접이 너무 아프면 낮추는 손잡이)
    public bool Spin;          // 쌍검: 넷째 휘두름마다 한 바퀴 회전 베기 (세게 3단계부터)

    public readonly int[] Ups = new int[3];   // 갈래마다 자란 횟수 (Swarm.Up.Fast·Wide·Power 순서)
    int[] caps = { 5, 5, 5 };                 // 갈래마다 끝
    public int Cap(Swarm.Up up) => caps[(int)up];
    public bool Full(Swarm.Up up) => Ups[(int)up] >= caps[(int)up];
    public bool Maxed => Full(Swarm.Up.Fast) && Full(Swarm.Up.Wide) && Full(Swarm.Up.Power);

    const double Deg = Math.PI / 180;

    // ── 처음 값 (docs/swarm-weapons.md 1장 표. 자동 조종 표를 보고 맞춘 값) ──
    public static Arm New(Id id) => id switch
    {
        // 쌍검: 가장 빠르고 가장 짧다. 좌·우 번갈아 벤다
        Id.Twin => new Arm { Which = id, Kind = Sort.Melee, Dmg = 16, Cool = .30, Range = 46, ArcHalf = 55 * Deg, Knock = 7, Stun = .10, BotGap = 26 },
        // 장검: 크고 느린 한 번. 맞은 놈은 멀리 밀려난다
        Id.Long => new Arm { Which = id, Kind = Sort.Melee, Dmg = 40, Cool = .86, Range = 66, ArcHalf = 70 * Deg, Knock = 28, Stun = .20, BotGap = 44 },
        // 쌍권총: 가까운 둘에게 한 발씩
        Id.Pistols => new Arm { Which = id, Kind = Sort.Bullets, Dmg = 10, Cool = .59, Range = 230, Count = 2, Knock = 3, Speed = 520, BotGap = 90, caps = new[] { 5, 2, 5 } },
        // 산탄총: 가까운 놈 쪽 부채꼴로 알 다섯
        Id.Shotgun => new Arm { Which = id, Kind = Sort.Bullets, Dmg = 10, Cool = .85, Range = 150, ArcHalf = 20 * Deg, Count = 5, Pierce = 1, Knock = 12, Speed = 480, BotGap = 55, caps = new[] { 5, 4, 5 } },
        // 돌격소총: 끊김 없는 연사, 하나를 뚫고 둘째까지
        Id.Rifle => new Arm { Which = id, Kind = Sort.Bullets, Dmg = 6, Cool = .12, Range = 290, ArcHalf = 3 * Deg, Pierce = 1, Knock = 1, Stun = .03, Speed = 620, BotGap = 90, caps = new[] { 5, 3, 5 } },
        // 바주카포: 벌레가 가장 몰린 곳으로 느린 로켓
        Id.Bazooka => new Arm { Which = id, Kind = Sort.Rocket, Dmg = 60, Cool = 2.00, Range = 300, Blast = 48, Knock = 22, Stun = .25, Speed = 330, BotGap = 90 },
        // 화염방사기: 끊기지 않는 불길과 불붙음. 밀침·멈칫이 없다
        _ => new Arm { Which = Id.Flame, Kind = Sort.Flame, Dmg = 6, Cool = .11, Range = 100, ArcHalf = 24 * Deg, BurnDmg = 3, BurnSec = 2, BotGap = 60 },
    };

    // ── 레벨 업: 그 갈래를 한 단계 키운다 (다 찼으면 false) ──
    public bool Grow(Swarm.Up up)
    {
        if (up > Swarm.Up.Power || Full(up)) return false;
        int n = ++Ups[(int)up];
        switch (Which, up)
        {
            case (Id.Twin, Swarm.Up.Fast): Cool *= .90; break;
            case (Id.Twin, Swarm.Up.Wide): (Range, ArcHalf) = (Range + 5, ArcHalf + 4 * Deg); break;
            case (Id.Twin, Swarm.Up.Power): Dmg += 4; if (n >= 3) Spin = true; break;

            case (Id.Long, Swarm.Up.Fast): Cool *= .92; break;
            case (Id.Long, Swarm.Up.Wide): (Range, ArcHalf) = (Range + 4, Math.Min(Math.PI, ArcHalf + 22 * Deg)); break;
            case (Id.Long, Swarm.Up.Power): (Dmg, Knock) = (Dmg + 9, Knock + 3); break;

            case (Id.Pistols, Swarm.Up.Fast): Cool *= .90; break;
            case (Id.Pistols, Swarm.Up.Wide): Count++; break;
            case (Id.Pistols, Swarm.Up.Power): Dmg += 2; if (n >= 3) Pierce = 1; break;

            case (Id.Shotgun, Swarm.Up.Fast): Cool *= .90; break;
            case (Id.Shotgun, Swarm.Up.Wide): (Count, ArcHalf) = (Count + 1, ArcHalf + 3 * Deg); break;
            case (Id.Shotgun, Swarm.Up.Power): (Dmg, Knock, Range) = (Dmg + 2, Knock + 2, Range + 12); break;

            case (Id.Rifle, Swarm.Up.Fast): Cool *= .92; break;
            case (Id.Rifle, Swarm.Up.Wide): Pierce++; break;
            case (Id.Rifle, Swarm.Up.Power): Dmg += 2; break;

            case (Id.Bazooka, Swarm.Up.Fast): Cool *= .90; break;
            case (Id.Bazooka, Swarm.Up.Wide): Blast += 8; break;
            case (Id.Bazooka, Swarm.Up.Power): Dmg += 16; break;

            case (Id.Flame, Swarm.Up.Fast): Cool *= .92; break;
            case (Id.Flame, Swarm.Up.Wide): (Range, ArcHalf) = (Range + 10, ArcHalf + 4 * Deg); break;
            case (Id.Flame, Swarm.Up.Power): (Dmg, BurnSec) = (Dmg + 1, BurnSec + .4); break;
        }
        return true;
    }
}
