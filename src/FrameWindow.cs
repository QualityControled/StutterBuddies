using System;

namespace StutterBuddies
{
    /// <summary>A fixed-size, allocation-free recorder for the latest valid frame times.</summary>
    /// <remarks>Use from one thread. Summarize sorts a separate, preallocated scratch array.</remarks>
    public sealed class FrameWindow
    {
        private readonly double[] samples;
        private readonly double[] scratch;
        private int next;
        private int count;

        public FrameWindow(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");

            samples = new double[capacity];
            scratch = new double[capacity];
        }

        public int Count => count;

        public void Add(double milliseconds)
        {
            if (milliseconds <= 0 || double.IsNaN(milliseconds) || double.IsInfinity(milliseconds))
                return;

            samples[next] = milliseconds;
            next = (next + 1) % samples.Length;
            if (count < samples.Length)
                count++;
        }

        public void Clear()
        {
            next = 0;
            count = 0;
        }

        public FrameSummary Summarize()
        {
            if (count == 0)
                return default(FrameSummary);

            Array.Copy(samples, scratch, count);
            Array.Sort(scratch, 0, count);

            double mean = 0;
            int over50 = 0;
            for (int i = 0; i < count; i++)
            {
                // Incremental mean avoids overflowing a sum of finite, positive samples.
                mean += (scratch[i] - mean) / (i + 1);
                if (scratch[i] > 50)
                    over50++;
            }

            return new FrameSummary(count, mean, Percentile(0.95), Percentile(0.99), over50);
        }

        private double Percentile(double fraction)
        {
            // Nearest rank: rank = ceiling(percentile * sample count), starting at 1.
            return scratch[(int)Math.Ceiling(fraction * count) - 1];
        }
    }

    public readonly struct FrameSummary
    {
        public FrameSummary(int count, double averageMs, double p95Ms, double p99Ms, int over50MsCount)
        {
            Count = count;
            AverageMs = averageMs;
            P95Ms = p95Ms;
            P99Ms = p99Ms;
            Over50MsCount = over50MsCount;
        }

        public int Count { get; }
        public double AverageMs { get; }
        public double P95Ms { get; }
        public double P99Ms { get; }
        public double AverageFps => AverageMs > 0 ? 1000.0 / AverageMs : 0;
        public int Over50MsCount { get; }
    }
}
