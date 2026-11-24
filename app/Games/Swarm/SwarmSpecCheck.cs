namespace Bolok;

// 버그 떼 특수기 자가 검사 (docs/swarm-specials.md 9장). SwarmGame.Check() 끝에서 부른다.
// 숫자는 Arm.Spec·Weapon.Every 에서 읽는다 (손잡이를 바꿔도 검사가 안 깨지게). 죽지 않는 과녁(SpecDummy)을 제자리에 세워 두고 규칙만 돌린다
sealed partial class SwarmGame
{
    static readonly Arm.Id[] SpecWeapons = { Arm.Id.Twin, Arm.Id.Pistols, Arm.Id.Shotgun, Arm.Id.Bazooka, Arm.Id.Flame };
    const int Tough = 99999999;   // 죽지 않는 과녁의 체력

    // 특수기를 켠 빈 판 (벌레는 직접 놓고, 안 맞게 한다)
    static Swarm SpecOn(int seed, Arm.Id id, int stage = 1) => new(TestField, 3, new Random(seed), id, stage) { Spawning = false, ShoeChance = 0, MagnetChance = 0, HeartChance = 0, GrenadeChance = 0, SafeUntil = 1e9 };

    static Swarm.Foe SpecDummy(Swarm s, Swarm.Kind kind, double dx, double dy, int hp = Tough, int tier = 1)
    {
        var f = s.Add(kind, s.X + dx, s.Y + dy, tier);
        (f.Hp, f.MaxHp, f.Speed, f.DashAt, f.SpawnDue) = (hp, hp, 0, 1e9, 1e9);
        return f;
    }

    static void SpecRun(Swarm s, double sec) { for (int i = 0; i < sec * 60 && !s.Done; i++) s.Update(Tick); }

    // 조건이 될 때까지 (최대 sec 초) 한 프레임씩
    static bool SpecUntil(Swarm s, double sec, Func<bool> done)
    {
        for (int i = 0; i < sec * 60 && !s.Done; i++) { if (done()) return true; s.Update(Tick); }
        return done();
    }

    static double Wrap(double r) => Math.IEEERemainder(r, 2 * Math.PI);
    static double ToDeg(double rad) => rad * 180 / Math.PI;
    static string Ko(Arm.Id id) => KoNames[(int)id];

    static string SpecCheck(SwarmGame game, SceneHost host)
    {
        SpecRhythm();
        SpecStage();
        SpecShotgun();
        SpecTwin();
        SpecPistols();
        SpecBazooka();
        SpecFlame();
        SpecEnd(host);
        SpecSame();
        SpecAlloc();
        SpecRepaint(game, host);
        return "특수기: 박자·산탄총 두 발·쌍검 칼·쌍권총 난사·바주카 새끼 폭탄·화염 불 바닥·끝·씨·메모리·다시 칠하기";
    }

    // ── 박자: 공격 N 번째마다 꼭 한 번 (N·2N·3N 번째). 레벨을 40번 올려도 N 은 그대로, 발동 사이 시간만 쿨만큼 줄어든다 ──
    static void SpecRhythm()
    {
        double Gap(Swarm s, Swarm.Foe pin, out int[] at)   // pin: 맞아서 밀려난 과녁을 프레임마다 제자리로 (닿는 거리 안에 계속 둔다)
        {
            int attacks = 0, prev = s.Charge, was = s.Specials;
            var list = new List<int>();
            var times = new List<double>();
            for (int i = 0; i < 60 * 600 && list.Count < 3; i++)
            {
                (pin.X, pin.Y) = (s.X + 30, s.Y);
                s.Update(Tick);
                if (s.Specials > was) { attacks++; list.Add(attacks); times.Add(s.Clock); was = s.Specials; }
                else if (s.Charge > prev) attacks++;
                prev = s.Charge;
            }
            at = list.ToArray();
            return times.Count >= 2 ? times[1] - times[0] : 1e9;
        }
        foreach (var id in SpecWeapons)
        {
            var beat = SpecOn(50 + (int)id, id);
            int every = beat.Weapon.Every;
            double gap1 = Gap(beat, SpecDummy(beat, Swarm.Kind.Beetle, 30, 0), out var at1);
            Expect(every > 0 && at1.Length == 3 && at1[0] == every && at1[1] == 2 * every && at1[2] == 3 * every, $"특수기는 공격 N 번째마다 꼭 한 번 ({Ko(id)}, N={every}): {string.Join("·", at1)} [센 수 {beat.Charge}, 휘두름 {beat.SwingCount}, 발동 {beat.Specials}, 시계 {beat.Clock:0}, 상태 {beat.State}, 벌레 {beat.Foes.Count}]");
            var lv = SpecOn(50 + (int)id, id);
            var lvPin = SpecDummy(lv, Swarm.Kind.Beetle, 30, 0);
            for (int i = 0; i < 40; i++) lv.LevelUp();
            double gap2 = Gap(lv, lvPin, out var at2);
            Expect(lv.Weapon.Every == every && at2.Length == 3 && at2[0] == every && gap2 < gap1 * .9, $"레벨을 올려도 N 은 그대로, 발동 사이 시간은 줄었다 ({Ko(id)}): {gap1:0.00}초 → {gap2:0.00}초");
            var first = SpecOn(60 + (int)id, id);
            SpecDummy(first, Swarm.Kind.Beetle, 30, 0);
            first.ChargeNow();
            first.Update(Tick);
            Expect(first.Specials == 1 && first.Charge == 0, $"ChargeNow 뒤 첫 공격에 발동한다 ({Ko(id)})");
            var plain = SpecOn(60 + (int)id, id);
            plain.SpecialsOn = false;
            SpecDummy(plain, Swarm.Kind.Beetle, 30, 0);
            plain.ChargeNow();
            SpecRun(plain, 12);
            Expect(plain.Specials == 0 && !plain.SpecialBusy && plain.SpecShots == 0, $"끄면 아무 특수기도 안 나온다 ({Ko(id)})");
            var empty = SpecOn(70 + (int)id, id);
            SpecRun(empty, 4);
            Expect(empty.Charge == 0 && empty.Specials == 0, $"벌레가 없으면 세지 않는다 ({Ko(id)})");
        }
        Expect(SpecOn(1, Arm.Id.Pistols, 3).EveryNow == Arm.Spec.HiEvery[(int)Arm.Id.Pistols] && SpecOn(1, Arm.Id.Pistols, 1).EveryNow == Arm.New(Arm.Id.Pistols).Every, "2단계부터 쌍권총 박자가 당겨진다 (1단계는 그대로)");
        foreach (var id in new[] { Arm.Id.Long, Arm.Id.Rifle })
        {
            var none = SpecOn(80, id);
            SpecDummy(none, Swarm.Kind.Beetle, 30, 0);
            none.ChargeNow();
            SpecRun(none, 17);
            Expect(none.Weapon.Every == 0 && none.Specials == 0 && !none.SpecialBusy, $"특수기가 없는 무기: {Ko(id)}");
        }
    }

    // ── 단계: 특수기 피해 = Round(Dmg × 배율 × HpX × 높은 단계 배율), 엘리트는 그 ×1.5, 보스는 배율 없음. 산탄총 둘째 발이 실제로 그렇게 박힌다 ──
    static void SpecStage()
    {
        var st5 = SpecOn(81, Arm.Id.Shotgun, 5);
        double hi = Arm.Spec.Hi5Dmg[(int)Arm.Id.Shotgun] > 1 ? Arm.Spec.Hi5Dmg[(int)Arm.Id.Shotgun] : Arm.Spec.HiDmg[(int)Arm.Id.Shotgun];   // 5단계부터는 5단계 칸
        int d5 = st5.SpecDmg(Arm.Spec.DoubleDmg);
        Expect(d5 == Math.Max(1, (int)Math.Round(st5.Weapon.Dmg * Arm.Spec.DoubleDmg * st5.T.HpX * hi, MidpointRounding.AwayFromZero)) && st5.T.HpX > 1.2, $"5단계 특수기 피해는 무기 피해 × 배율 × 벌레 체력 배율: {d5}");
        Expect(Swarm.Versus(new Swarm.Foe { Elite = true }, d5) == (int)Math.Round(d5 * Arm.Spec.Elite, MidpointRounding.AwayFromZero) && Swarm.Versus(new Swarm.Foe(), d5) == d5 && Swarm.Versus(new Swarm.Foe { Kind = Swarm.Kind.Boss }, d5) == d5, "엘리트는 특수기 피해 ×1.5, 보통 벌레·보스는 그대로");
        (int Lost, int Per) Second(Swarm.Kind kind, bool elite)
        {
            var s = SpecOn(82, Arm.Id.Shotgun, 5);
            var f = SpecDummy(s, kind, 60, 0, Tough, kind == Swarm.Kind.Boss ? 1 : 0);
            f.Elite = elite;
            s.ChargeNow();
            SpecRun(s, .22);   // 첫 발은 맞았고 둘째 발은 아직 날아가는 중
            int hp1 = f.Hp;
            SpecRun(s, .3);
            return (hp1 - f.Hp, Swarm.Versus(f, s.SpecDmg(Arm.Spec.DoubleDmg)));
        }
        var (ln, pn) = Second(Swarm.Kind.Beetle, false);
        var (le, pe) = Second(Swarm.Kind.Beetle, true);
        var (lb, pb) = Second(Swarm.Kind.Boss, false);
        Expect(ln > 0 && ln % pn == 0 && le % pe == 0 && lb % pb == 0 && ln / pn == le / pe && pe == (int)Math.Round(pn * Arm.Spec.Elite, MidpointRounding.AwayFromZero) && pb == pn, $"5단계 둘째 발: 보통 {ln}(알당 {pn}) · 엘리트 {le}(알당 {pe}) · 보스 {lb}(알당 {pb})");
    }

    // ── 산탄총: N 번째 쏘기 뒤 0.12초에 알이 Count 개 더 (그 전엔 없다). 퍼짐 ×1.3, 피해 SpecDmg, 밀침·관통은 무기 값. 넓게 4 에서 9 + 9 ──
    static void SpecShotgun()
    {
        foreach (int wide in new[] { 0, 4 })
        {
            var sg = SpecOn(90, Arm.Id.Shotgun);
            for (int i = 0; i < wide; i++) sg.Weapon.Grow(Swarm.Up.Wide);
            SpecDummy(sg, Swarm.Kind.Beetle, 100, 0);
            sg.ChargeNow();
            sg.Update(Tick);
            double t0 = sg.Clock, firstMax = 0;
            int count = sg.Weapon.Count;
            bool plainFirst = sg.Shots.Count == count && sg.SpecShots == 0;
            for (int i = 0; i < sg.Shots.Count; i++) firstMax = Math.Max(firstMax, Math.Abs(Math.Atan2(sg.Shots[i].Vy, sg.Shots[i].Vx)));
            Expect(SpecUntil(sg, .3, () => sg.SpecShots > 0), "산탄총 둘째 발이 나온다");
            double dt = sg.Clock - t0, half = sg.Weapon.ArcHalf * Arm.Spec.DoubleSpread, worst = 0;
            int right = 0;
            for (int i = 0; i < sg.Shots.Count; i++)
            {
                ref var sh = ref sg.Shots[i];
                if (!sh.Special) continue;
                worst = Math.Max(worst, Math.Abs(Math.Atan2(sh.Vy, sh.Vx)));
                if (sh.Dmg == sg.SpecDmg(Arm.Spec.DoubleDmg) && Math.Abs(sh.Knock - sg.Weapon.Knock * Arm.Spec.SpecKnock) < 1e-9 && sh.Stun == 0 && sh.Pierce == sg.Weapon.Pierce && sh.Ammo == Swarm.Ammo.Pellet) right++;
            }
            Expect(plainFirst && sg.SpecShots == count && Math.Abs(dt - Arm.Spec.DoubleGap) <= Tick + 1e-9 && Math.Abs(worst - half) < 1e-6 && worst > firstMax + 1e-6 && right == count,
                $"산탄총 둘째 발 (넓게 {wide}): 알 {sg.SpecShots}/{count}, {dt:0.000}초 뒤, 퍼짐 ±{ToDeg(worst):0.0}° (첫 발 ±{ToDeg(firstMax):0.0}°), 피해·밀침·관통이 맞는 알 {right}");
        }
    }

    // ── 쌍검: 칼이 손에 있을 때만, 나갈 때 한 번·올 때 한 번 다 뚫는다. 나갈 때만 밀린다. 못 돌아오는 일이 없고, 공중인 동안엔 둘째 칼이 안 나간다 ──
    static void SpecTwin()
    {
        var tw = SpecOn(91, Arm.Id.Twin);
        var trig = SpecDummy(tw, Swarm.Kind.Beetle, 30, 0);
        var line1 = SpecDummy(tw, Swarm.Kind.Beetle, 80, 0);
        var line2 = SpecDummy(tw, Swarm.Kind.Beetle, 105, 0);
        var past = SpecDummy(tw, Swarm.Kind.Beetle, 150, 0);
        double reach = tw.Weapon.Range * Arm.Spec.BladeReach;
        tw.ChargeNow();
        tw.Update(Tick);
        Expect(tw.BladeOut && !tw.BladeBack && tw.BladeHand == tw.SwingHand && tw.Specials == 1, "쌍검 특수기: 휘두름과 함께 그 손의 칼이 날아간다");
        double farthest = 0, backAt = -1, line1Back = 0;
        while (tw.BladeOut && tw.Clock < 3)
        {
            farthest = Math.Max(farthest, tw.BladeX - tw.X);
            if (tw.BladeBack && backAt < 0) (backAt, line1Back) = (tw.Clock, line1.X);
            if (tw.Clock > .06 && tw.Charge == 0) tw.ChargeNow();   // 공중인 동안 발동이 다 차도 둘째 칼은 안 나간다
            tw.Update(Tick);
        }
        double both = 2 * tw.SpecDmg(Arm.Spec.BladeDmg);
        Expect(!tw.BladeOut && backAt > 0 && tw.Clock < Arm.Spec.BladeMax + .1 && Math.Abs(farthest - reach) < Arm.Spec.BladeOut * Tick + 1, $"칼은 {reach:0}px 까지 나갔다 돌아와 손에 잡힌다: 가장 먼 {farthest:0}px, {tw.Clock:0.00}초");
        Expect(tw.Specials == 1, $"공중인 동안엔 둘째 칼이 안 나간다: {tw.Specials}");
        Expect(line1.Hp == Tough - both && line2.Hp == Tough - both && past.Hp == Tough && trig.Hp < Tough, $"칼은 한 줄 선 둘을 나갈 때 한 번·올 때 한 번 (같은 길에 두 번 안 맞는다): {Tough - line1.Hp}·{Tough - line2.Hp} (한 번 {both / 2:0}), 먼 과녁 {Tough - past.Hp}");
        Expect(Math.Abs(line1.X - tw.X - 80 - tw.Weapon.Knock * Arm.Spec.SpecKnock) < 1 && Math.Abs(line1.X - line1Back) < 1 && line1.StunUntil <= tw.Clock,   // 특수기 밀침은 ×SpecKnock, 멈칫 없음   // 이웃과 겹쳐 서로 밀어내는 몫이 1px 안쪽으로 섞인다
             $"나갈 때만 밀린다 (올 때는 자리 그대로): {line1.X - tw.X - 80:0.0}px");
        // 던진 뒤 걸어가도 칼이 쫓아와 한도 안에 손에 잡힌다
        var walk = SpecOn(92, Arm.Id.Twin);
        SpecDummy(walk, Swarm.Kind.Beetle, 30, 0);
        walk.ChargeNow();
        walk.Update(Tick);
        walk.Steer(-1, 1);
        Expect(SpecUntil(walk, Arm.Spec.BladeMax - .2, () => !walk.BladeOut) && walk.Specials == 1, "던진 뒤 걸어가도 칼은 1.3초 안에 돌아온다");
        // 공중에 있는 동안 발동이 다 차면 다 찬 채 기다렸다가 칼이 돌아온 다음 휘두름에 나간다
        var wait = SpecOn(93, Arm.Id.Twin);
        SpecDummy(wait, Swarm.Kind.Beetle, 30, 0);
        wait.ChargeNow();
        wait.Update(Tick);
        wait.ChargeNow();
        SpecRun(wait, .36);
        Expect(wait.BladeOut && wait.Specials == 1 && wait.Charge == wait.EveryNow, $"칼이 공중이면 다 찬 채로 기다린다: 발동 {wait.Specials}, 센 수 {wait.Charge}");
        Expect(SpecUntil(wait, 1.2, () => wait.Specials == 2) && wait.BladeOut, "칼이 돌아온 다음 휘두름에 둘째 칼이 나간다");
        // 보스는 뚫고 지나간다: 나갈 때·올 때 한 번씩, 밀침 ¼·멈칫 없음
        var twb = SpecOn(94, Arm.Id.Twin);
        var pin = SpecDummy(twb, Swarm.Kind.Beetle, 30, 0);
        var boss = SpecDummy(twb, Swarm.Kind.Boss, 85, 0, Tough, 1);
        var behind = SpecDummy(twb, Swarm.Kind.Beetle, 125, 0);
        twb.ChargeNow();
        for (int i = 0; i < 60; i++) { (pin.X, pin.Y) = (twb.X + 30, twb.Y); twb.Update(Tick); }   // 휘두름에 밀려나는 과녁은 제자리로 (보스를 밀지 않게)
        Expect(boss.Hp == Tough - 2 * twb.SpecDmg(Arm.Spec.BladeDmg) && behind.Hp < Tough && Math.Abs(boss.X - twb.X - 85 - twb.Weapon.Knock * Arm.Spec.SpecKnock / 4) < .01 && boss.StunUntil < twb.Clock, $"칼은 보스를 뚫고 지나간다 (밀침 ¼, 멈칫 없음): 보스 {Tough - boss.Hp}, 뒤 {Tough - behind.Hp}, 밀림 {boss.X - twb.X - 85:0.00}");
    }

    // ── 쌍권총: 난사 = 물결 SprayVolleys 번 × 8방향, 물결마다 22.5° 돌려 (첫 물결 한 발은 겨눈 놈 쪽). 관통 +1, 등 뒤도 맞는다, 난사 중에도 평소 쏘기는 센다 ──
    static void SpecPistols()
    {
        Swarm Setup(bool specials)
        {
            var s = SpecOn(95, Arm.Id.Pistols);
            s.SpecialsOn = specials;
            SpecDummy(s, Swarm.Kind.Beetle, 60, 0);
            SpecDummy(s, Swarm.Kind.Beetle, 110, 0);
            SpecDummy(s, Swarm.Kind.Beetle, -90, 0);
            s.ChargeNow();
            return s;
        }
        var pi = Setup(true);
        pi.Update(Tick);
        int waves = Arm.Spec.SprayVolleys, dirs = Arm.Spec.SprayDirs, seen = 0;
        bool pierced = true;
        var angles = new List<double[]>();
        double start = pi.Clock, end = (waves - 1) * Arm.Spec.SprayGap + .06;
        for (int i = 0; i < 60; i++)
        {
            if (pi.SpecShots > seen)   // 이번 프레임에 나온 물결: Shots 의 맨 뒤
            {
                int add = pi.SpecShots - seen;
                var a = new double[add];
                for (int j = 0; j < add; j++)
                {
                    ref var sh = ref pi.Shots[pi.Shots.Count - add + j];
                    a[j] = Math.Atan2(sh.Vy, sh.Vx);
                    pierced &= sh.Special && sh.Pierce == pi.Weapon.Pierce + Arm.Spec.SprayPierce && sh.Dmg == pi.SpecDmg(Arm.Spec.SprayDmg);   // 나온 순간 (날아가며 관통 수가 줄기 전)
                }
                angles.Add(a);
                seen = pi.SpecShots;
            }
            if (pi.Clock - start >= end) break;
            pi.Update(Tick);
        }
        bool even = angles.Count == waves && angles.All(w => w.Length == dirs);
        bool spaced = even && angles.All(w => Enumerable.Range(0, dirs).All(m => w.Any(x => Math.Abs(Wrap(x - w[0] - m * 2 * Math.PI / dirs)) < 1e-6)));
        bool turned = even && angles.Skip(1).Select((w, k) => Math.Abs(Wrap(w[0] - angles[k][0]) - Math.PI / dirs) < 1e-6).All(b => b);
        bool aimed = even && angles[0].Any(x => Math.Abs(Wrap(x)) < 1e-6);   // 첫 물결에 겨눈 쪽(0) 한 발
        Expect(even && spaced && turned && aimed && pi.SpecShots == waves * dirs, $"난사: 물결 {angles.Count}번 × {(angles.Count > 0 ? angles[0].Length : 0)}발, 한 물결은 {360 / dirs}° 간격 {spaced}, 물결마다 {180.0 / dirs}° 돌림 {turned}, 겨눈 쪽 한 발 {aimed}");
        Expect(pierced, "난사 탄은 관통 +1, 피해 SpecDmg");
        // 같은 판을 특수기 없이 돌린 것과 견준다: 평소 쏘기는 똑같고 난사가 더 맞힌 몫이 보인다 (앞줄 뒤의 과녁은 관통으로, 등 뒤 과녁은 8방향으로)
        var control = Setup(false);
        SpecRun(pi, 1.2);
        while (control.Clock < pi.Clock - 1e-9) control.Update(Tick);
        int More(int i) => (Tough - pi.Foes[i].Hp) - (Tough - control.Foes[i].Hp);
        Expect(More(0) > 0 && More(1) > 0 && More(2) > 0, $"난사가 관통으로 한 줄 선 뒤 과녁과 등 뒤 과녁도 맞힌다: 앞 +{More(0)} · 뒤줄 +{More(1)} · 등 뒤 +{More(2)}");
        // 난사 중에도 평소 쏘기는 제 쿨대로 나가고 센다
        var fast = SpecOn(96, Arm.Id.Pistols);
        for (int i = 0; i < 5; i++) fast.Weapon.Grow(Swarm.Up.Fast);
        SpecDummy(fast, Swarm.Kind.Beetle, 60, 0);
        fast.ChargeNow();
        fast.Update(Tick);
        SpecRun(fast, fast.Weapon.Cool + .05);
        Expect(fast.Specials == 1 && fast.Charge >= 1, $"난사 중에도 평소 쏘기는 제 쿨대로 나가고 센다: 센 수 {fast.Charge}");
        // MaxShots: 탄이 꽉 찬 판에서도 안 터진다 (더 안 나갈 뿐)
        var full = SpecOn(97, Arm.Id.Pistols);
        SpecDummy(full, Swarm.Kind.Beetle, 60, 0);
        while (full.Shots.Count < Swarm.MaxShots - 5) full.Shots.Add() = new Swarm.Shot { X = full.X, Y = full.Y - 90, Until = 1e9, Ammo = Swarm.Ammo.Bullet };
        full.ChargeNow();
        SpecRun(full, .8);
        Expect(full.Shots.Count <= Swarm.MaxShots && full.Specials == 1, $"탄이 꽉 차도 난사는 MaxShots 안: {full.Shots.Count}");
    }

    // ── 바주카포: N 번째 로켓이 터지면 새끼 폭탄 셋 (1~N-1번째는 없다). 터진 자리에서 Blast + 14px 에 120° 간격, 하나씩 터진다 ──
    static void SpecBazooka()
    {
        var bz = SpecOn(98, Arm.Id.Bazooka);
        bz.SafeUntil = 0;
        var pin = SpecDummy(bz, Swarm.Kind.Beetle, 150, 0);
        int booms = 0;
        bool noEarly = true;
        bz.OnEvent = e => { if (e == "boom") { booms++; if (booms < bz.Weapon.Every) noEarly &= bz.Bombs.Count == 0; } };
        Expect(SpecUntil(bz, 90, () => { (pin.X, pin.Y) = (bz.X + 150, bz.Y); return booms == bz.Weapon.Every; })  && noEarly && bz.Bombs.Count == Arm.Spec.Bomblets, $"1~{bz.Weapon.Every - 1}번째 로켓은 새끼가 없고 {bz.Weapon.Every}번째에 {bz.Bombs.Count}개: 터진 {booms}번");
        double boomAt = bz.BoomAt, far = bz.Weapon.Blast + Arm.Spec.BombFar;
        var (cx, cy) = (bz.Bombs[0].X0, bz.Bombs[0].Y0);
        var land = new (double X, double Y, double Fly)[Arm.Spec.Bomblets];
        for (int i = 0; i < land.Length; i++) land[i] = (bz.Bombs[i].X1, bz.Bombs[i].Y1, bz.Bombs[i].Fly);
        bool ring = true;
        for (int i = 0; i < land.Length; i++)
        {
            double dist = Math.Sqrt((land[i].X - cx) * (land[i].X - cx) + (land[i].Y - cy) * (land[i].Y - cy));
            ring &= Math.Abs(dist - far) < .01 && TestField.Contains((int)land[i].X, (int)land[i].Y);
            if (i > 0) ring &= Math.Abs(Math.Abs(Wrap(Math.Atan2(land[i].Y - cy, land[i].X - cx) - Math.Atan2(land[i - 1].Y - cy, land[i - 1].X - cx))) - 2 * Math.PI / land.Length) < 1e-6;
        }
        Expect(ring && Math.Abs(land[0].Fly - Arm.Spec.BombFly) < 1e-9 && Math.Abs(land[2].Fly - (Arm.Spec.BombFly + 2 * Arm.Spec.BombStep)) < 1e-9, $"새끼 폭탄은 터진 자리에서 {far:0}px, 120° 간격, 판 안");
        // 떨어질 자리에 과녁 (하나는 반지름 바깥)
        var targets = land.Select(l => SpecDummy(bz, Swarm.Kind.Beetle, l.X - bz.X, l.Y - bz.Y)).ToArray();
        double outward = bz.Weapon.Blast * Arm.Spec.BombBlast + 12 + 22, ux = (land[0].X - cx) / far, uy = (land[0].Y - cy) / far;
        var outer = SpecDummy(bz, Swarm.Kind.Beetle, land[0].X + ux * outward - bz.X, land[0].Y + uy * outward - bz.Y);
        int hp0 = bz.Hp;
        double t0 = bz.Clock;
        var left = new List<int>();
        foreach (double at in new[] { .28, .33, .41, .49 })
        {
            SpecRun(bz, t0 + at - bz.Clock);
            left.Add(bz.Bombs.Count);
        }
        int dmg = bz.SpecDmg(Arm.Spec.BombDmg);
        Expect(left.SequenceEqual(new[] { 3, 2, 1, 0 }), $"새끼 폭탄은 하나씩 터진다: 남은 수 {string.Join("·", left)}");
        Expect(targets.All(t => Tough - t.Hp == dmg) && outer.Hp == Tough, $"새끼 폭발 반지름 안 과녁만 SpecDmg({dmg}) 만큼: {string.Join("·", targets.Select(t => Tough - t.Hp))}, 밖 {Tough - outer.Hp}");
        Expect(bz.BoomAt == boomAt && bz.Hp == hp0 && bz.DamageTaken == 0, "새끼가 터져도 BoomAt 은 로켓 때 값, 볼록이는 안 다친다");
        // 새끼 폭탄에 잡힌 벌레는 보석을 남기고 SpecKills 가 는다
        var weak = SpecDummy(bz, Swarm.Kind.Bug, land[1].X - bz.X + 3, land[1].Y - bz.Y, 1);
        int gems = bz.Gems.Count, kills = bz.SpecKills;
        bz.Bombs.Add() = new Swarm.Bomb { X0 = land[1].X, Y0 = land[1].Y - 30, X1 = land[1].X, Y1 = land[1].Y, At = bz.Clock, Fly = .1 };
        SpecRun(bz, .2);
        Expect(weak.Dead && bz.Gems.Count > gems && bz.SpecKills == kills + 1, $"새끼 폭탄에 잡힌 벌레는 보석을 남기고 SpecKills 가 는다: 보석 +{bz.Gems.Count - gems}, 잡은 수 +{bz.SpecKills - kills}");
        bz.OnEvent = null;
    }

    // ── 화염방사기: N 번째 틱에 겨눈 쪽 길에 불 자리 (거리 100 이면 셋). 볼록이가 떠나도 그 자리, 시간이 지나면 사라진다. 밟으면 SpecDmg + 불붙음 ──
    static void SpecFlame()
    {
        // 불 자리를 놓고 (불길은 그 한 틱뿐: 쿨을 크게) 목표는 멀리 치운다
        (Swarm S, List<(double X, double Y)> Spots, double At) Lay(int seed)
        {
            var f = SpecOn(seed, Arm.Id.Flame);
            var target = SpecDummy(f, Swarm.Kind.Beetle, 60, 0);
            f.ChargeNow();
            f.Update(Tick);
            f.Weapon.Cool = 99;
            var spots = new List<(double, double)>();
            for (int i = 0; i < f.Fires.Count; i++) spots.Add((f.Fires[i].X, f.Fires[i].Y));
            target.X = f.X + 500;
            return (f, spots, f.Clock);
        }
        var (fl, spots, laid) = Lay(99);
        int n = Math.Max(1, (int)(fl.Weapon.Range / Arm.Spec.FloorGap));
        bool onRay = spots.Count == n && fl.Specials == 1;
        for (int i = 0; onRay && i < n; i++) onRay &= Math.Abs(spots[i].X - (fl.X + fl.RangePx * (i + .7) / n)) < .5 && Math.Abs(spots[i].Y - fl.Y) < .5;
        Expect(onRay, $"불 자리 {spots.Count}개가 겨눈 쪽 길 위에 (거리 {fl.RangePx:0}px)");
        fl.Steer(0, 1);   // 볼록이는 떠난다
        SpecRun(fl, .5);
        bool stay = fl.Fires.Count == n;
        for (int i = 0; stay && i < n; i++) stay &= fl.Fires[i].X == spots[i].X && fl.Fires[i].Y == spots[i].Y;
        Expect(stay && fl.Y > fl.Fires[0].Y + 20, "볼록이가 떠나도 불 자리는 놓인 자리");
        SpecRun(fl, Arm.Spec.FloorSec - .5 + .1);
        Expect(fl.Fires.Count == 0 && fl.Clock - laid > Arm.Spec.FloorSec, $"불 자리는 {Arm.Spec.FloorSec:0.0}초 뒤 사라진다");

        // 밟으면: 두 자리에 걸친 과녁도 한 자리 과녁과 같은 만큼 (틱마다 한 번), 불 밖 과녁은 그대로, 불붙음이 걸린다
        var (fb, sp2, _) = Lay(100);
        var between = SpecDummy(fb, Swarm.Kind.Beetle, (sp2[0].X + sp2[1].X) / 2 - fb.X, 0);
        var single = SpecDummy(fb, Swarm.Kind.Beetle, sp2[^1].X - fb.X + 14, 0);
        var outside = SpecDummy(fb, Swarm.Kind.Beetle, sp2[1].X - fb.X, 70);
        fb.X -= 250;   // 볼록이는 불길이 안 닿게 멀리 (불 바닥 몫만 본다)
        int kd = fb.SpecDmg(Arm.Spec.FloorDmg);
        SpecRun(fb, Arm.Spec.FloorEvery + .05);
        Expect(Tough - single.Hp == kd && Tough - between.Hp == kd && single.BurnUntil > fb.Clock && outside.Hp == Tough, $"첫 틱: 불 위 과녁은 SpecDmg({kd}) 한 번 + 불붙음, 두 자리에 걸친 과녁도 한 번: {Tough - between.Hp}·{Tough - single.Hp}, 불 밖 {Tough - outside.Hp}");
        single.X += 400;   // 바닥만 밟고 나간 과녁은 BurnSec 동안 더 탄다
        int lit = single.Hp;
        SpecRun(fb, fb.Weapon.BurnSec + .6);
        int burned = lit - single.Hp;
        Expect(burned >= 3 * fb.Weapon.BurnDmg && burned <= 5 * fb.Weapon.BurnDmg, $"바닥만 밟고 나간 과녁은 {fb.Weapon.BurnSec:0}초 더 탄다: {burned}");
        Expect(outside.Hp == Tough && outside.BurnUntil < 0, "불 밖 과녁은 끝까지 그대로");

        // 불붙음은 하나뿐: 이미 타는 과녁은 불 바닥 틱이 BurnTick 을 되돌리지 않고 BurnUntil 만 늘린다 (불길 + 바닥이 두 배로 태우지 않는다)
        var (fo, sp3, _) = Lay(101);
        var hot = SpecDummy(fo, Swarm.Kind.Beetle, sp3[0].X - fo.X, 0);   // 첫 자리 한가운데: 둘째 자리에도 걸친다
        fo.X -= 250;
        (hot.BurnUntil, hot.BurnTick) = (fo.Clock + 100, fo.Clock + 100);
        double tickWas = hot.BurnTick, untilWas = hot.BurnUntil;
        SpecRun(fo, Arm.Spec.FloorEvery + .05);
        Expect(hot.BurnTick == tickWas && hot.BurnUntil == untilWas, "불 바닥은 이미 타는 과녁의 불붙음 시계(BurnTick)를 되돌리지 않는다");
        Expect(Tough - hot.Hp == kd, $"이미 타는 과녁도 두 자리에 걸쳐 틱마다 한 번만 맞는다: {Tough - hot.Hp}");

        // 불 위에서도 멈칫·밀림이 없다 (걸음 그대로)
        var (fw, sp4, _) = Lay(102);
        var walker = SpecDummy(fw, Swarm.Kind.Beetle, sp4[1].X - fw.X + 6, 0);
        fw.X -= 250;
        walker.Speed = 36;
        bool steady = true;
        for (int i = 0; i < 60; i++)
        {
            double was = walker.X;
            fw.Update(Tick);
            steady &= walker.StunUntil <= fw.Clock && walker.X < was - .3;
        }
        Expect(steady && walker.Hp < Tough, "불 위에서도 멈칫·밀림이 없다 (걸음 그대로)");
    }

    // ── 판이 끝나면 새끼 폭탄·불 자리·공중 칼·미뤄 둔 둘째 발·난사가 없다. 발동 직후 Esc(보스 키)는 바로 끝 ──
    static void SpecEnd(SceneHost host)
    {
        foreach (var id in SpecWeapons)
        {
            foreach (bool win in new[] { false, true })
            {
                var e = SpecOn(110 + (int)id, id);
                SpecDummy(e, Swarm.Kind.Beetle, 30, 0);
                e.ChargeNow();
                Expect(SpecUntil(e, 3, () => e.SpecialBusy), $"끝 검사: 특수기가 나온다 ({Ko(id)})");
                if (win) e.Clock = Swarm.WinSec - .05;
                else { (e.SafeUntil, e.Hp) = (0, 1); SpecDummy(e, Swarm.Kind.Bug, 0, 0); }
                SpecRun(e, .3);
                int shots = e.SpecShots;
                Expect(e.State != Swarm.Phase.Play && !e.SpecialBusy && e.Bombs.Count == 0 && e.Fires.Count == 0 && !e.BladeOut, $"판이 끝나면 특수기가 없다 ({Ko(id)}, {(win ? "5:00" : "쓰러짐")})");
                SpecRun(e, 1.5);
                Expect(e.SpecShots == shots, $"끝난 뒤에 특수기 탄이 안 나온다 ({Ko(id)})");
            }
            var esc = new SwarmGame(host) { FakeOpen = f => Session.Fake(f) };
            esc.Command(new[] { "swarm", "weapon", Arm.Name(id) });
            esc.swarm!.Spawning = false;
            SpecDummy(esc.swarm, Swarm.Kind.Beetle, 30, 0);
            esc.Command(new[] { "swarm", "special" });
            bool busy = false;
            for (int i = 0; i < 240 && !busy; i++) { esc.Frame(Tick); busy = esc.swarm!.SpecialBusy; }
            Expect(busy && esc.swarm!.Specials >= 1, $"swarm special: 다음 공격에 특수기 ({Ko(id)})");
            esc.Stop(true);
            Expect(!esc.Running && host.Pet.Mode == Mode.Wander && !host.Pet.Emoting, $"특수기 직후 보스 키(Esc)는 바로 끝난다 ({Ko(id)})");
        }
    }

    // ── 씨가 같으면 같은 판: 무기마다 같은 씨 두 판의 자리·잡은 수·특수기·생명이 같다 ──
    static void SpecSame()
    {
        var twins = new Swarm[SpecWeapons.Length * 2];
        Parallel.For(0, twins.Length, i => twins[i] = BotGame(SpecWeapons[i / 2], 7));
        for (int i = 0; i < SpecWeapons.Length; i++)
        {
            var (a, b) = (twins[i * 2], twins[i * 2 + 1]);
            Expect(a.X == b.X && a.Y == b.Y && a.Kills == b.Kills && a.Specials == b.Specials && a.SpecKills == b.SpecKills && a.Hp == b.Hp && a.Clock == b.Clock && a.Specials > 0, $"같은 씨면 같은 판 ({Ko(SpecWeapons[i])}): 특수기 {a.Specials}번·{b.Specials}번, 잡은 수 {a.Kills}·{b.Kills}");
        }
    }

    // ── 프레임마다 새로 만드는 것 없음: 죽지 않는 과녁 스무 마리 속에서 600프레임 (특수기를 자주 켠다). 바주카는 터질 때의 Pop 뿐 ──
    static void SpecAlloc()
    {
        foreach (var id in SpecWeapons)
        {
            long Bytes(bool specials, out int pops)
            {
                var s = SpecOn(120 + (int)id, id);
                s.OnEvent = null;
                s.SpecialsOn = specials;
                var crowd = new Swarm.Foe[20];
                for (int i = 0; i < crowd.Length; i++) crowd[i] = SpecDummy(s, Swarm.Kind.Beetle, 28 + i % 5 * 8, (i - 10) * 5);
                void Step(int i)
                {
                    for (int j = 0; j < crowd.Length; j++) (crowd[j].X, crowd[j].Y) = (s.X + 28 + j % 5 * 8, s.Y + (j - 10) * 5);   // 밀려난 과녁은 제자리로
                    if (i % 20 == 0) s.ChargeNow();
                    s.Update(Tick);
                }
                for (int i = 0; i < 400; i++) Step(i);   // 처음 만드는 것(JIT·목록 늘어남)은 여기서
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                int last = s.Pops.Count;
                pops = 0;
                for (int i = 0; i < 600; i++)
                {
                    Step(i);
                    if (s.Pops.Count > last) pops += s.Pops.Count - last;
                    last = s.Pops.Count;
                }
                return GC.GetAllocatedBytesForCurrentThread() - b0;
            }
            long on = Bytes(true, out int explosions), off = Bytes(false, out _);
            Expect(on <= off + (id == Arm.Id.Bazooka ? (explosions + 4) * 200 : 0), $"프레임마다 새로 만드는 것 없음 ({Ko(id)}): 특수기 {on}B · 끄고 {off}B (터짐 {explosions}번)");
        }
    }

    // ── 다시 칠할 자리: 다섯 무기 각각 8초 (2초마다 ChargeNow) + 불 자리가 남은 채 5:00 + 칼이 공중인 채 5:00. 바뀐 자리만 칠해도 화면이 맞다 ──
    static void SpecRepaint(SwarmGame game, SceneHost host)
    {
        void Paint(string what, Swarm p, double secs, Action<Swarm>? each = null)
        {
            (game.stage, game.maxStage, game.practice, game.auto) = (p.Stage, 0, false, false);
            game.Begin(p);
            p.OnEvent = null;
            game.ses = Session.Fake(TestField);
            using var ov = host.Overlay(game);
            double until = p.Clock + secs;
            var (bad, first) = SceneKit.Repaint(ov, game, TestField, () =>
            {
                if (p.Done || p.Clock >= until) return false;
                p.BotPlay();
                each?.Invoke(p);
                p.Update(Tick);
                if (p.State == Swarm.Phase.Play) p.Hp = Math.Max(p.Hp, 40);
                if (p.Done) return false;
                game.Track();
                return true;
            }, 60 * 40);
            Expect(bad == 0, $"바뀐 자리만 다시 칠해도 화면이 맞다 ({what}): {bad}번 다름 (처음 {first}프레임)");
            game.swarm = null;
        }
        foreach (var id in SpecWeapons)
        {
            int frame = 0, busy = 0;
            var rp = new Swarm(TestField, 3, new Random(130 + (int)id), id) { Clock = 100 };
            for (int i = 0; i < 6; i++) rp.LevelUp();
            Paint($"특수기 {Ko(id)}", rp, 8, p => { if (frame++ % 120 == 0) p.ChargeNow(); if (p.SpecialBusy) busy++; });
            Expect(busy > 8, $"다시 칠할 자리 검사에 특수기가 들어 있다 ({Ko(id)}): {busy}프레임");
        }
        Paint("불 자리가 남은 채 5:00", new Swarm(TestField, 3, new Random(140), Arm.Id.Flame) { SafeUntil = 1e9, Clock = Swarm.WinSec - 2.5 }, 5, p => { if (!p.SpecialBusy) p.ChargeNow(); });
        Paint("칼이 공중인 채 5:00", new Swarm(TestField, 3, new Random(141), Arm.Id.Twin) { SafeUntil = 1e9, Clock = Swarm.WinSec - 2.5 }, 5, p => { if (!p.BladeOut) p.ChargeNow(); });
    }

    // ── 그려 보기 (--mode-scene swarm 그림.png spec): 특수기 여섯 장 ──
    // 1 산탄총: 둘째 발이 나온 0.04초 뒤 (부채꼴 둘이 겹쳐 날아간다, 뒤 것이 넓다) · 2 쌍검: 칼이 가장 멀리 가 돌아서는 순간 (한 손이 비었다) · 3 쌍권총: 셋째 물결 직후 (탄 고리 셋이 바람개비로)
    // 4 바주카포: 새끼 폭탄이 공중 (그림자) 0.2초째 · 5 화염방사기: 불 자리가 놓인 0.5초 뒤 (볼록이는 비켜 걷는다) · 6 번쩍: 산탄총을 쏜 0.05초 뒤 (밝은 손·밝은 무기 그림·반짝이)
    static void SpecScene(string path, int k)
    {
        var host = new SceneHost(scale: k);
        var game = new SwarmGame(host);
        var field = FieldAt(k);
        double sf = k / 3.0;
        var order = new[] { Arm.Id.Shotgun, Arm.Id.Twin, Arm.Id.Pistols, Arm.Id.Bazooka, Arm.Id.Flame, Arm.Id.Shotgun };
        Swarm s = null!;
        double mark = -1;   // 장면마다 기억하는 때 (둘째 발·셋째 물결이 나온 때)
        void Bug(double dx, double dy) => SpecDummy(s, Swarm.Kind.Bug, dx * sf, dy * sf, 9999);
        void New(int shot)
        {
            s = new Swarm(field, k, new Random(5 + shot), order[shot]) { Spawning = false };   // 안 맞는 동안 깜빡이지 않게 SafeUntil 은 0 (벌레는 멀다)
            mark = -1;
            switch (shot)
            {
                case 0 or 5: foreach (var (x, y) in new[] { (110, -26), (122, 4), (100, 30), (140, -10), (134, 24), (152, 6) }) Bug(x, y); break;
                case 1: foreach (var (x, y) in new[] { (34, 0), (74, 0), (100, 2), (150, 0), (-30, 40) }) Bug(x, y); break;
                case 2: for (int i = 0; i < 12; i++) Bug(Math.Cos(i * Math.PI / 6) * (60 + i % 3 * 22), Math.Sin(i * Math.PI / 6) * (50 + i % 3 * 18)); break;
                case 3: foreach (var (x, y) in new[] { (140, 0), (160, -14), (150, 18), (128, -22), (172, 10) }) Bug(x, y); break;
                default: foreach (var (x, y) in new[] { (50, 0), (84, 4), (70, -22), (40, 24) }) Bug(x, y); break;
            }
            for (int i = 0; i < 3; i++) s.LevelUp();
            (s.UpAt, s.LastUp) = (-9, null);
            game.Begin(s);
            s.OnEvent = null;
            game.ses = null;
            s.ChargeNow();
        }
        using var ov = host.Overlay(game);
        int made = -1;
        SceneKit.Shots(path, ov, Rectangle.Inflate(field, 20, 20), order.Length, shot =>
        {
            if (made != shot) { made = shot; New(shot); }
            if (shot == 4 && s.Fires.Count > 0) s.Steer(0, 1);   // 볼록이는 비켜 걷는다
            s.Update(Tick);
            if (s.State == Swarm.Phase.Play) s.Hp = Swarm.MaxHp;
            game.Track();
            if (shot == 0 && mark < 0 && s.SpecShots > 0) mark = s.Clock;
            if (shot == 2 && mark < 0 && s.SpecShots >= 3 * Arm.Spec.SprayDirs) mark = s.Clock;
            return shot switch
            {
                0 => mark > 0 && s.Clock - mark >= .04,
                1 => s.BladeBack,
                2 => mark > 0 && s.Clock - mark >= .03,
                3 => s.Bombs.Count == Arm.Spec.Bomblets && s.Clock - s.Bombs[0].At >= .2,
                4 => s.Fires.Count > 0 && s.Clock - s.Fires[0].At >= .5,
                _ => s.Specials > 0 && s.Clock - s.SpecialAt >= .05,
            } || s.Clock > 20;
        }, 60 * 60);
    }

    // ── 그려 보기 (--mode-scene swarm 그림.png mound): 가만히 있기 막기 여섯 장 (구석에 선 볼록이 둘레만 잘라서) ──
    // 1 흙더미가 놓인 0.1초 (펴진 흙) · 2 0.4초 (갈라짐) · 3 0.7초 (부풀어 떨림, 곧 올라온다) · 4 벌레가 올라온 0.05초 (흙덩이가 튄다, 아직 땅 속) · 5 0.15초 (흙덩이가 흩어진다, 거의 올라옴) · 6 오래 서 있어 흙더미 셋과 올라온 벌레
    static void MoundScene(string path, int k)
    {
        var host = new SceneHost(scale: k);
        var game = new SwarmGame(host);
        var field = FieldAt(k);
        double sf = k / 3.0;
        Swarm s = null!;
        void New(int shot)
        {
            s = new Swarm(field, k, new Random(7 + shot), Arm.Id.Twin) { Spawning = false, CampOn = true, CampForce = true };
            (s.X, s.Y) = (field.Left + 12 * sf, field.Top + 26 * sf);   // 왼쪽 위 구석
            s.CampPrime(shot == 5 ? Swarm.CampAfter + 14 : Swarm.CampAfter - .02);
            game.Begin(s);
            s.OnEvent = null;
            game.ses = null;
        }
        var view = new Rectangle(field.Left - (int)(8 * sf), field.Top - (int)(8 * sf), (int)(172 * sf), (int)(132 * sf));
        using var ov = host.Overlay(game);
        int made = -1;
        SceneKit.Shots(path, ov, view, 6, shot =>
        {
            if (made != shot) { made = shot; New(shot); }
            s.Update(Tick);
            if (s.State == Swarm.Phase.Play) s.Hp = Swarm.MaxHp;
            game.Track();
            double bornAge = -1;
            foreach (var f in s.Foes) if (f.Dirt) bornAge = Math.Max(bornAge, s.Clock - f.BornAt);
            return shot switch
            {
                0 => s.Mounds.Count > 0 && s.Clock - s.Mounds[0].At >= .1,
                1 => s.Mounds.Count > 0 && s.Clock - s.Mounds[0].At >= .4,
                2 => s.Mounds.Count > 0 && s.Clock - s.Mounds[0].At >= .7,
                3 => bornAge >= .05 && bornAge < .1,
                4 => bornAge >= .15 && bornAge < .2,
                _ => s.Mounds.Count >= 3 && s.Foes.Count >= 1 && s.Clock - s.Mounds[^1].At >= .3,
            } || s.Clock > 40;
        }, 60 * 60, 3);
    }
}
