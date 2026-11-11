namespace Bolok;

// 무기. Bloom 은 한 발마다 탄이 퍼지는 반지름이 커지는 양, MaxBloom 은 그 한계 (배율 3 기준 px). Kick 은 총이 들썩이는 정도.
// Pellets 는 한 번에 나가는 탄알 수, Spread 는 산탄이 퍼지는 반지름
public sealed record Weapon(string Name, int Mag, double ReloadSec, double Interval, double Bloom, double MaxBloom, double Kick, bool Auto,
    int Pellets = 1, double Spread = 0);

// 총쏘기 게임 한 판: 3·2·1 → 10초 → 조준경 폭발 → 결과(클릭하면 닫힘). 화면과 무관한 규칙이라 --selfcheck 로 검사한다.
// Doom 처럼 조준점은 마우스에서 벗어나지 않는다. 반동은 총이 들썩이고 탄이 퍼지는 것으로만 보여 준다
public sealed class Game
{
    public enum Phase { Count, Play, Boom, Result, Done }

    public static readonly Weapon Pistol = new("pistol", 6, .7, .16, 9, 18, 1, false);
    public static readonly Weapon MachineGun = new("mg", 30, 1.4, .09, 4, 34, .55, true);
    public static readonly Weapon Shotgun = new("shotgun", 5, 1.1, .55, 6, 12, 1.7, false, 8, 46); // 대충 겨눠도 맞는다
    public static readonly Weapon[] Guns = { Pistol, MachineGun, Shotgun };
    public const int MaxHoles = 120;
    public const double CountSec = 1.8, PlaySec = 10, BoomSec = .9, ResultMinSec = .8, ResultSec = 3.5;

    public readonly Rectangle Arena;    // 볼록이가 도망 다니는 모니터
    public readonly Weapon Gun;
    public readonly string Theme;       // 소품 묶음 (Props.Sets)
    public readonly int Scale;
    public Point GunPivot;              // 총 손잡이 자리: 게임 창 아래 테두리 가운데
    public List<Prop> Decor = new();    // 창 윗변에 앉힌 작은 소품
    public double GunKick, GunRise;     // 쏠 때 총이 뒤로 밀리는 거리, 총구가 들리는 각도. 금방 돌아온다
    public double FlashUntil, HitUntil; // 총구 불꽃, 명중 표시(조준경이 빨개진다)
    public Phase Stage = Phase.Count;
    public double T, Clock;             // 이 단계에 들어온 뒤 흐른 초, 게임 전체 시간
    public int Ammo, Hits, Shots, Best;
    public bool NewBest, Trigger;
    public double ReloadLeft, Bloom, AimX, AimY, BoomX, BoomY;
    public readonly List<(int X, int Y, int Kind)> Holes = new();             // 빗나간 자리의 탄흔
    public readonly List<(int X, int Y, bool Hit, double At)> Marks = new();  // 쏜 자리 표시 (잠깐)
    public int Version;                 // 탄흔이 바뀌면 오른다
    public Action<string, int, int>? OnShot;
    double lastShot = -9, nextAuto;
    readonly Random rng;

    public Game(Rectangle arena, int best, int scale, Random rng, Weapon? gun = null, string? theme = null)
    {
        (Arena, Best, Scale, this.rng) = (arena, best, scale, rng);
        Gun = gun ?? Guns[rng.Next(Guns.Length)];
        Theme = theme ?? Props.Sets.Keys.ElementAt(rng.Next(Props.Sets.Count));
        Ammo = Gun.Mag;
    }

    double Sf => Scale / 3.0;
    public double TimeLeft => Stage switch { Phase.Count => PlaySec, Phase.Play => Math.Max(0, PlaySec - T), _ => 0 };
    public int CountNumber => Math.Clamp(3 - (int)(T / (CountSec / 3)), 1, 3);
    public int Loaded => ReloadLeft > 0 ? (int)((1 - ReloadLeft / Gun.ReloadSec) * Gun.Mag) : Ammo; // 장전 중엔 차오르게 보인다
    public bool Aiming => Stage is Phase.Count or Phase.Play;

    // 오른쪽 아래. 가운데 아래는 총 자리다
    public Rectangle ExitButton()
    {
        int s = 14 * Scale;
        return new Rectangle(Arena.Right - s - 16 * Scale, Arena.Bottom - s - 8 * Scale, s, s);
    }

    // 게임할 창: 메뉴를 열기 전 쓰던 창 → 볼록이 모니터에서 가장 큰 창 → 볼록이 모니터 전체. 화면 밖으로 나간 부분은 자른다
    public static (Rectangle Arena, IntPtr Window) ChooseArena(Win? used, IReadOnlyList<Win> wins, Rectangle petArea, IReadOnlyList<Rectangle> screens, int cell)
    {
        Rectangle Fit(Rectangle r) => Rectangle.Intersect(r, screens.FirstOrDefault(s => s.Contains(r.Left + r.Width / 2, r.Top + r.Height / 2), petArea));
        bool Big(Rectangle r) => r.Width >= cell * 3 && r.Height >= cell * 2.5;
        if (used is { } u && !u.Topmost && Big(Fit(u.R))) return (Fit(u.R), u.H);
        var largest = wins.Where(w => !w.Topmost && w.R.IntersectsWith(petArea)).Select(w => (w.H, R: Rectangle.Intersect(w.R, petArea)))
            .Where(w => Big(w.R)).OrderByDescending(w => w.R.Width * (long)w.R.Height).FirstOrDefault();
        return largest.H != IntPtr.Zero ? (largest.R, largest.H) : (petArea, IntPtr.Zero);
    }

    // 총이 조준점을 향하는 각도(라디안). 손잡이보다 아래는 못 겨눈다
    public double GunAngle()
    {
        double a = Math.Atan2(AimY - GunPivot.Y, AimX - GunPivot.X);
        if (a > 0) a = AimX >= GunPivot.X ? -.05 : -Math.PI + .05;
        return Math.Clamp(a, -Math.PI + .05, -.05);
    }

    public void Update(double dt, Point mouse, Func<int, int, bool> hitsPet)
    {
        T += dt;
        Clock += dt;
        if (ReloadLeft > 0 && (ReloadLeft -= dt) <= 0) { ReloadLeft = 0; Ammo = Gun.Mag; OnShot?.Invoke("loaded", 0, 0); }
        GunKick *= Math.Exp(-dt / .07);
        GunRise *= Math.Exp(-dt / .09);
        if (!(Trigger && Gun.Auto)) Bloom *= Math.Exp(-dt / .18); // 쏘기를 멈추면 퍼짐이 금방 모인다
        (AimX, AimY) = (mouse.X, mouse.Y);
        Marks.RemoveAll(m => Clock - m.At > (m.Hit ? .3 : .12));

        switch (Stage)
        {
            case Phase.Count when T >= CountSec:
                (Stage, T) = (Phase.Play, 0);
                break;
            case Phase.Play:
                if (Trigger && Gun.Auto && Clock >= nextAuto) Shoot(hitsPet);
                if (T >= PlaySec) { (Stage, T, Trigger, BoomX, BoomY) = (Phase.Boom, 0, false, AimX, AimY); OnShot?.Invoke("boom", (int)AimX, (int)AimY); } // 조준경이 터지며 끝을 알린다
                break;
            case Phase.Boom when T >= BoomSec:
                ToResult();
                break;
            case Phase.Result when T >= ResultSec:
                Stage = Phase.Done;
                break;
        }
    }

    void ToResult()
    {
        (Stage, T, Trigger) = (Phase.Result, 0, false);
        if (Hits > Best) { Best = Hits; NewBest = true; }
    }

    // 나가기 버튼 또는 Esc
    public void Finish()
    {
        if (Stage is Phase.Result or Phase.Boom) Stage = Phase.Done;
        else if (Stage != Phase.Done) ToResult();
    }

    // 왼쪽 누름. 나가기 버튼이 먼저고, 결과 화면에서는 닫기다
    public string Press(int x, int y, Func<int, int, bool> hitsPet)
    {
        if (Stage == Phase.Result) { if (T >= ResultMinSec) Stage = Phase.Done; return "close"; } // 결과를 잠깐은 보여 준다
        if (ExitButton().Contains(x, y)) { Finish(); return "exit"; }
        if (Stage != Phase.Play) return "wait";
        Trigger = true;
        (AimX, AimY) = (x, y);
        return Shoot(hitsPet);
    }

    public void Release() => Trigger = false;

    public void Reload()
    {
        if (Stage == Phase.Play && ReloadLeft == 0 && Ammo < Gun.Mag) { ReloadLeft = Gun.ReloadSec; OnShot?.Invoke("reload", 0, 0); }
    }

    string Shoot(Func<int, int, bool> hitsPet)
    {
        if (Clock - lastShot < Gun.Interval * .9) return "wait"; // 권총을 아무리 빨리 눌러도 연사 속도 이상은 안 나간다
        nextAuto = Clock + Gun.Interval;
        if (ReloadLeft > 0 || Ammo == 0) { OnShot?.Invoke("empty", (int)AimX, (int)AimY); return "empty"; }
        Ammo--;
        Shots++;
        lastShot = Clock;
        double spread = (2 + Bloom + Gun.Spread) * Sf;
        bool hit = false;
        (int X, int Y) hitAt = ((int)AimX, (int)AimY);
        for (int i = 0; i < Gun.Pellets; i++) // 산탄은 여러 알이 원 안에 고르게 퍼진다. 한 알이라도 맞으면 명중 하나
        {
            double a = rng.NextDouble() * Math.PI * 2, r = Math.Sqrt(rng.NextDouble()) * spread;
            int x = (int)(AimX + Math.Cos(a) * r), y = (int)(AimY + Math.Sin(a) * r);
            bool h = hitsPet(x, y);
            if (h && !hit) { hit = true; hitAt = (x, y); }
            else if (!h)
            {
                Holes.Add((x, y, rng.Next(3)));
                if (Holes.Count > MaxHoles) Holes.RemoveAt(0);
                Version++;
            }
            Marks.Add((x, y, h, Clock));
        }
        if (hit) { Hits++; HitUntil = Clock + .12; }
        Bloom = Math.Min(Gun.MaxBloom, Bloom + Gun.Bloom);
        GunKick = 22 * Gun.Kick * Sf;
        GunRise = .22 * Gun.Kick;
        FlashUntil = Clock + (Gun.Auto ? .07 : .09); // 화면이 다시 그려질 틈을 줄 만큼은 보인다
        OnShot?.Invoke(hit ? "hit" : "miss", hit ? hitAt.X : (int)AimX, hit ? hitAt.Y : (int)AimY);
        if (Ammo == 0) { ReloadLeft = Gun.ReloadSec; OnShot?.Invoke("reload", 0, 0); } // 다 쏘면 알아서 장전한다
        return hit ? "hit" : "miss";
    }

    // 스프라이트 픽셀로 명중 판정. 반지름만큼은 봐준다
    public static bool HitBitmap(Bitmap bmp, int x, int y, int radius)
    {
        for (int dy = -radius; dy <= radius; dy += Math.Max(1, radius / 3))
            for (int dx = -radius; dx <= radius; dx += Math.Max(1, radius / 3))
            {
                int px = x + dx, py = y + dy;
                if (px >= 0 && py >= 0 && px < bmp.Width && py < bmp.Height && bmp.GetPixel(px, py).A > 0) return true;
            }
        return false;
    }
}
