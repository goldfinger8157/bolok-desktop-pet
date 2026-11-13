namespace Bolok;

// 총쏘기 게임에서 볼록이가 도망 다니는 법. 볼록이는 꼭두각시(Mode.Puppet)고, 자리·방향·자세·그림 속도를 프레임마다 여기서 정한다.
// Doom 의 몬스터처럼 후다닥 달리고 멈추기를 되풀이한다. 멈춘 틈이 쏠 기회다. 달리기 직전엔 움츠려 예고한다.
// 커서가 가까우면 창 가장자리 뒤로 숨는다 (숨은 창 안쪽 몸은 안 그리고 안 맞는다)
sealed class Prey
{
    public const double Speed = 330;    // 도망가는 속도 (배율 3 기준 px/s)

    readonly Brain pet;
    readonly Rectangle arena;           // 도망 다니는 창 (창이 없으면 모니터)
    readonly List<Cover> covers;        // 숨을 수 있는 창들
    readonly Random rng;
    public double Clock;
    double hurtUntil, freezeUntil, phaseUntil, coverUntil, nextCover, dashX, dashY;
    bool dashing, ducking;
    (double X, double Y, Cover C)? cover;

    public Cover? Shield { get; private set; }      // 숨은 창. 그 안쪽 몸은 안 그리고 안 맞는다
    public bool Frozen => Clock < freezeUntil;      // 맞은 순간 멈칫
    public bool Shielded(int x, int y) => Shield?.Contains(x, y) == true;

    public Prey(Brain pet, Rectangle arena, IEnumerable<Cover>? covers, Random rng)
    {
        (this.pet, this.arena, this.rng) = (pet, arena, rng);
        this.covers = covers?.ToList() ?? new();
        (nextCover, phaseUntil) = (1, .3);
        pet.X = Math.Clamp(pet.X, MinX, MaxX);
        pet.Y = Math.Clamp(pet.Y, MinY, MaxY);
        pet.Pose = "walk";
    }

    double S => pet.Scale / 3.0;
    int Cell => pet.CellPx;
    double MinX => arena.Left + Cell / 2.0;
    double MaxX => arena.Right - Cell / 2.0;
    double MinY => arena.Top + Cell;
    double MaxY => arena.Bottom - pet.K.FloorGap;
    double Rand(double a, double b) => a + rng.NextDouble() * (b - a);
    bool Hurt => Clock < hurtUntil;

    void Hide(Cover? c)
    {
        Shield = c;
        pet.PuppetClip = c?.Mask();
    }

    bool Walk(double gx, double gy, double v, double dt)
    {
        double dx = gx - pet.X, dy = gy - pet.Y, d = Math.Sqrt(dx * dx + dy * dy);
        if (d < 3) return true;
        double s = Math.Min(v * S * dt, d);
        pet.X += dx / d * s;
        pet.Y += dy / d * s;
        if (Math.Abs(dx) > 2) pet.Dir = dx > 0 ? 1 : -1;
        return false;
    }

    // 맞음: 0.06초 멈칫(손맛), 하얗게 번쩍, 총알 방향으로 밀려나고, 곧바로 달아난다
    public void Hit(double fromX, double fromY)
    {
        hurtUntil = Clock + .35;
        freezeUntil = Clock + .06;
        pet.FlashUntil = pet.Now + .08;
        double dx = pet.X - fromX, dy = pet.Y - Cell * .45 - fromY, d = Math.Sqrt(dx * dx + dy * dy) + 1;
        pet.X = Math.Clamp(pet.X + dx / d * 26 * S, MinX, MaxX);
        pet.Y = Math.Clamp(pet.Y + dy / d * 26 * S, MinY, MaxY);
        (cover, ducking) = (null, false);
        Hide(null);
        phaseUntil = Clock + .06; // 멈칫이 끝나면 바로 다음 달리기
        dashing = false;
    }

    // 아깝게 빗나가면 가끔 놀린다
    public void NearMiss(double x, double y)
    {
        double dx = pet.X - x, dy = pet.Y - Cell * .45 - y, near = 150 * S;
        if (dx * dx + dy * dy < near * near && !dashing && rng.NextDouble() < .35) { pet.Emote("note", .6); pet.Pose = "giggle"; phaseUntil = Math.Max(phaseUntil, Clock + .35); }
    }

    // 커서와 반대쪽 창 가장자리. 창이 커서와 볼록이 사이에 오게 한다
    (double X, double Y, Cover C)? PickCover(Point mouse)
    {
        (double X, double Y, Cover C)? best = null;
        double bestScore = double.MinValue, half = Cell / 2.0;
        foreach (var c in covers)
        {
            var r = c.R;
            if (r.Contains(mouse)) continue;
            double y = Math.Clamp(pet.Y, r.Top + Cell * .9, Math.Min(r.Bottom - 4, MaxY));
            double x = Math.Clamp(pet.X, r.Left + half, r.Right - half);
            for (int n = 0; n < 3; n++)
            {
                var (sx, sy) = n switch { 0 => (r.Left + Cell * .12, y), 1 => (r.Right - Cell * .12, y), _ => (x, r.Top + Cell * .55) };
                if (sx - half < arena.Left || sx + half > arena.Right || sy - Cell < arena.Top) continue;
                double away = Math.Sqrt((sx - mouse.X) * (sx - mouse.X) + (sy - mouse.Y) * (sy - mouse.Y)), trip = Math.Sqrt((sx - pet.X) * (sx - pet.X) + (sy - pet.Y) * (sy - pet.Y));
                double score = away - trip * .6;
                if (score > bestScore) { bestScore = score; best = (sx, sy, c); }
            }
        }
        return best;
    }

    public void Update(double dt, Point mouse)
    {
        Clock += dt;
        Step(dt, mouse);
        pet.PuppetMs = pet.Pose == "walk" ? 70 : 0; // 도망칠 땐 발을 더 빨리 구른다
    }

    void Step(double dt, Point mouse)
    {
        if (Clock < freezeUntil) return; // 맞은 순간 멈칫
        double cy = pet.Y - Cell * .45, ax = pet.X - mouse.X, ay = cy - mouse.Y, d = Math.Sqrt(ax * ax + ay * ay) + 1;

        // 엄폐: 창 가장자리 뒤에서 빼꼼
        if (cover is { } cv)
        {
            if (!ducking)
            {
                pet.Pose = Hurt ? "hurt" : "walk";
                if (Walk(cv.X, cv.Y, Speed * 1.8, dt))
                {
                    (ducking, coverUntil) = (true, Clock + Rand(.6, 1.4));
                    Hide(cv.C);
                    pet.Dir = cv.X <= cv.C.R.Left + Cell ? -1 : cv.X >= cv.C.R.Right - Cell ? 1 : pet.Dir; // 창 바깥을 내다본다
                }
                return;
            }
            pet.Pose = Hurt ? "hurt" : cv.Y < cv.C.R.Top + Cell ? "idle" : "peek";
            if (Clock < coverUntil) return;
            (cover, ducking) = (null, false);
            Hide(null);
            (nextCover, phaseUntil) = (Clock + Rand(1.5, 3), Clock);
        }

        if (dashing)
        {
            pet.Pose = Hurt ? "hurt" : "walk";
            if (Walk(dashX, dashY, Speed * (Hurt ? 2.1 : 1.7), dt) || Clock > phaseUntil)
            {
                dashing = false;
                phaseUntil = Clock + Rand(.4, .9) * (Hurt ? .4 : 1); // 멈춤 — 쏠 틈
            }
            return;
        }

        if (pet.Pose != "giggle" || Clock > phaseUntil) pet.Pose = Hurt ? "hurt" : "idle";
        bool close = d < 170 * S;
        if (Clock < phaseUntil - .08 && !close) return; // 쉬는 중
        if (Clock < phaseUntil) { pet.Pose = "land"; return; } // 0.08초 움츠림: 곧 달린다는 예고
        if (covers.Count > 0 && Clock > nextCover && d < 320 * S && PickCover(mouse) is { } spot) { cover = spot; nextCover = Clock + 1; return; }

        // 커서에서 먼 곳 몇 군데 중 하나로 달린다
        (double X, double Y) best = (pet.X, pet.Y);
        double bestScore = double.MinValue;
        for (int i = 0; i < 8; i++)
        {
            double tx = Rand(MinX, MaxX), ty = Rand(MinY, MaxY), trip = Math.Sqrt((tx - pet.X) * (tx - pet.X) + (ty - pet.Y) * (ty - pet.Y));
            double away = Math.Sqrt((tx - mouse.X) * (tx - mouse.X) + (ty - Cell * .45 - mouse.Y) * (ty - Cell * .45 - mouse.Y));
            double score = away - Math.Abs(trip - 280 * S) * .5; // 너무 멀지도 가깝지도 않게
            if (score > bestScore) (bestScore, best) = (score, (tx, ty));
        }
        (dashX, dashY, dashing) = (best.X, best.Y, true);
        phaseUntil = Clock + 1.2;
        pet.Dir = dashX > pet.X ? 1 : -1;
    }
}
