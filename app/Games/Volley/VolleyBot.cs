namespace Bolok;

// 배구 봇 (8장): 오른쪽 라이벌과 표의 왼쪽 봇이 같은 함수로 움직인다. 왼쪽은 거울 (x 를 뒤집고 역할을 바꾼 것).
// 실력 차이는 BotSkill 숫자뿐이다. 상태는 값 형식(BotState)이라 프레임마다 새로 잡는 메모리가 없다
sealed partial class VolleySim
{
    // 이번 프레임에 누를 키를 정한다. dt 는 지난 Update 의 값 (프레임 간격이 같으면 같다)
    public void BotInput(Actor who, in BotSkill sk, ref BotState st, Random rng, out int axis, out bool jump, out bool spike)
    {
        var b = Of(who);
        var o = Of(Other(who));
        int d = Dir(who);
        bool right = who == Actor.Rival;
        b.SmartSpike = sk.SmartSpike && !sk.Legacy;
        b.Bot = true;
        // 슬라이드: 공이 몸에 닿을 때마다 「이 공에 몸을 던질지」 한 번 뽑는다 (켜진 판에서만 — 꺼 두면 Rnd 를 안 써서 옛 판이 그대로)
        if (T.Slides && !sk.Legacy)
        {
            if (st.TouchSeen != TouchSerial) (st.TouchSeen, st.SlidePlan) = (TouchSerial, rng.NextDouble() < sk.SlideProb);
            b.SlideOk = st.SlidePlan;
        }
        else b.SlideOk = true;
        jump = false;
        spike = false;
        // 새 랠리가 시작되면 반응 시계를 비운다: 득점 뒤 쉬는 동안 한쪽 봇만 시계가 돌아 새 랠리를 늦게(또는 먼저) 시작하는 일이 없게
        if (!sk.Legacy && st.ServeSeen != ServeSerial) (st.ServeSeen, st.ReactionTimer, st.BlockPlan, st.Hop) = (ServeSerial, 0, false, false);
        st.ReactionTimer -= curDt;

        // 이번 점프에서 스파이크를 할지는 뛰는 순간 한 번만 뽑는다 (옛 방식은 닿는 프레임마다 뽑았다)
        bool air = !b.OnGround;
        if (air && !st.WasAir && !sk.Legacy) st.SpikePlan = rng.NextDouble() < sk.SpikeProb;
        st.WasAir = air;

        double lo = right ? NetX + PetR + 4 : WallLeft + PetR + 4;
        double hi = right ? WallRight - PetR - 4 : NetX - PetR - 4;

        // 공이 내 쪽으로 넘어오고 있거나 내 쪽에 있을 때
        if (right ? BallX >= NetX - 30 * K || BallVx > 0 : BallX <= NetX + 30 * K || BallVx < 0)
        {
            if (st.ReactionTimer <= 0)
            {
                st.ReactionTimer = sk.ReactionSec;
                double predX = PredictBallLandingX(b.Y - PetR, isRightSide: right);
                double error = (rng.NextDouble() - 0.5) * 2 * sk.ErrorCells * ScaleRatio;
                st.TargetX = Math.Clamp(predX + error, lo, hi);
            }

            double dx = BallX - b.X;
            double dy = BallY - (b.Y - PetR);

            // 점프 조건: 공이 하강 중이고 적절한 높이이며 가까울 때
            if (b.OnGround && BallVy > 0 && Math.Abs(dx) < PetR * 1.2 && BallY < FloorY - PetR * 1.8 && BallY > NetTopY - sk.JumpCells * K)
                jump = true;

            // 스파이크 조건: 공중에서 공과 닿을 수 있고 공이 네트 위쪽일 때
            if (!b.OnGround && Math.Sqrt(dx * dx + dy * dy) <= BallR + PetR + 8 * K && BallY < NetTopY + 10 * K)
            {
                if (sk.Legacy) spike = rng.NextDouble() < sk.SpikeProb;
                else spike = st.SpikePlan;   // SmartSpike 봇은 닿는 순간 다시 계산해 못 넘길 공이면 스파이크를 접는다 (CheckBodyCollision)
            }
        }
        else
        {
            st.TargetX = right ? (NetX + WallRight) / 2.0 : (WallLeft + NetX) / 2.0;
        }

        if (!sk.Legacy && Stage == Phase.InPlay)
        {
            // 돌: 자기 쪽에 솟는 순간 넘을지 한 번 정하고, 넘기로 했으면 돌에 가까워질 때 뛴다. 아니면 그대로 달려 걸린다
            if (StoneOn && StoneOwner == who)
            {
                if (st.StoneSeen != StoneSerial)
                {
                    st.StoneSeen = StoneSerial;
                    st.Hop = rng.NextDouble() < sk.StoneHopProb;
                }
                if (st.Hop && b.OnGround && Math.Abs(b.X - StoneX) <= WalkSpeed * .10)
                {
                    jump = true;
                    st.Hop = false;
                }
            }

            // 블로킹: 상대가 네트 가까이(구역의 두 배 안)에서 뛰고 공이 그 상대 가까이 내려오는 순간 한 번 정한다.
            // 하기로 했으면 네트 앞으로 가서 늦춤(0~BlockLateSec) 뒤에 뛴다 → 타이밍이 자주 어긋난다
            double zone = T.BlockZoneBodies * 2 * PetR;
            if (T.Blocks && sk.BlockProb > 0)
            {
                if (st.BlockSeen != o.JumpSerial && !o.OnGround && Math.Abs(o.X - NetX) <= 2 * zone)
                {
                    double bx = BallX - o.X, by = BallY - (o.Y - PetR);
                    if (bx * bx + by * by <= 4.0 * (BallR + PetR) * (BallR + PetR))
                    {
                        st.BlockSeen = o.JumpSerial;
                        st.BlockPlan = b.OnGround && rng.NextDouble() < sk.BlockProb;
                        if (st.BlockPlan) st.BlockJumpAt = Clock + rng.NextDouble() * sk.BlockLateSec;
                    }
                }
                if (st.BlockPlan)
                {
                    if (o.OnGround || !b.OnGround) st.BlockPlan = false;       // 상대가 이미 내려왔거나 내가 떴다
                    else
                    {
                        st.TargetX = NetX - d * (PetR + 4);
                        if (Clock >= st.BlockJumpAt && Math.Abs(b.X - NetX) <= zone * .9) { jump = true; st.BlockPlan = false; }
                    }
                }
            }
        }

        // 수평 이동
        double diff = st.TargetX - b.X;
        axis = Math.Abs(diff) > 4 * ScaleRatio ? Math.Sign(diff) : 0;
    }
}
