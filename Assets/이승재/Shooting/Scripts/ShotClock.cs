namespace Reinforcement.Shooting
{
    // Simulation ticks drive a weapon even when an input decision spans multiple ticks.
    internal sealed class ShotClock
    {
        private int lastTick = -1;
        private double readyAt;
        public double Time { get; private set; } = double.NegativeInfinity;

        public bool BeginTick(int tick, double time)
        {
            if (tick <= lastTick || time < Time || time < 0 || double.IsNaN(time) || double.IsInfinity(time))
                return false;
            lastTick = tick;
            Time = time;
            return true;
        }

        public double Remaining(double time)
        {
            // Unity fixedDeltaTime and the interval are floats. Allow accumulated rounding at a boundary.
            double remaining = readyAt - time;
            return remaining > 0.000001 ? remaining : 0d;
        }

        public void Commit(float interval) => readyAt = Time + interval;

        public void Reset()
        {
            lastTick = -1;
            Time = double.NegativeInfinity;
            readyAt = 0;
        }
    }
}
