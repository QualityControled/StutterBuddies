using StutterBuddies;

int passed = 0;
var tests = new (string Name, Action Run)[]
{
    ("Empty window has zero statistics", Empty),
    ("Capacity must be positive", InvalidCapacity),
    ("Invalid values do not advance the ring", InvalidSamples),
    ("Nearest-rank percentiles and frame-weighted FPS", Percentiles),
    ("Hitches are strictly above 50 ms", HitchBoundary),
    ("Wraparound retains only the newest samples", Wraparound),
    ("Summary does not reorder the live ring", SummaryPreservesRing),
    ("Clear discards every earlier sample", Clear),
    ("A single-slot window is valid", SingleSlot),
    ("Finite samples cannot overflow the mean", LargeSamples),
    ("Recording and summary allocate no managed memory after warmup", NoAllocations),
    ("Lease restores original values and releases only once", LeaseRestores),
    ("Lease preserves external writes", LeaseExternalWrite),
    ("No-op apply neither writes nor owns the value", LeaseNoOp),
    ("Active lease requires release before reapplying", LeaseReapply),
};

foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
        passed++;
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"FAIL {test.Name}: {error.Message}");
    }
}

Console.WriteLine($"{passed}/{tests.Length} tests passed.");
return passed == tests.Length ? 0 : 1;

static void Equal(double expected, double actual, double tolerance = 1e-9)
{
    if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance)
        throw new Exception($"Expected {expected}, received {actual}.");
}

static void Summary(FrameWindow window, int count, double mean, double p95, double p99, int hitches)
{
    FrameSummary result = window.Summarize();
    Equal(count, window.Count);
    Equal(count, result.Count);
    Equal(mean, result.AverageMs);
    Equal(p95, result.P95Ms);
    Equal(p99, result.P99Ms);
    Equal(hitches, result.Over50MsCount);
    Equal(mean == 0 ? 0 : 1000 / mean, result.AverageFps);
}

static void Empty() => Summary(new FrameWindow(4), 0, 0, 0, 0, 0);

static void InvalidCapacity()
{
    foreach (int capacity in new[] { 0, -1 })
    {
        try
        {
            _ = new FrameWindow(capacity);
        }
        catch (ArgumentOutOfRangeException)
        {
            continue;
        }
        throw new Exception($"Capacity {capacity} should have thrown.");
    }
}

static void InvalidSamples()
{
    var window = new FrameWindow(2);
    window.Add(10);
    foreach (double invalid in new[] { 0.0, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        window.Add(invalid);
    window.Add(20);
    Summary(window, 2, 15, 20, 20, 0);
    window.Add(double.NaN);
    Summary(window, 2, 15, 20, 20, 0);
}

static void Percentiles()
{
    var window = new FrameWindow(100);
    // Reverse order ensures percentiles require sorting.
    for (int sample = 20; sample >= 1; sample--)
        window.Add(sample);
    Summary(window, 20, 10.5, 19, 20, 0);

    window.Clear();
    for (int sample = 100; sample >= 1; sample--)
        window.Add(sample);
    Summary(window, 100, 50.5, 95, 99, 50);
}

static void HitchBoundary()
{
    var window = new FrameWindow(4);
    foreach (double sample in new[] { 49.0, 50, 51, 100 })
        window.Add(sample);
    Summary(window, 4, 62.5, 100, 100, 2);
}

static void Wraparound()
{
    var window = new FrameWindow(4);
    foreach (double sample in new[] { 1.0, 2, 3, 4, 100, 200 })
        window.Add(sample);
    Summary(window, 4, 76.75, 200, 200, 2);
    for (int i = 0; i < 100; i++)
        window.Add(8);
    Summary(window, 4, 8, 8, 8, 0);
}

static void SummaryPreservesRing()
{
    var window = new FrameWindow(3);
    foreach (double sample in new[] { 30.0, 10, 20 })
        window.Add(sample);
    Summary(window, 3, 20, 30, 30, 0);
    Summary(window, 3, 20, 30, 30, 0);
    window.Add(60); // Must replace 30, not the sorted array's first value of 10.
    Summary(window, 3, 30, 60, 60, 1);
}

static void Clear()
{
    var window = new FrameWindow(3);
    for (int i = 0; i < 7; i++)
        window.Add(100);
    window.Clear();
    Summary(window, 0, 0, 0, 0, 0);
    window.Add(7);
    Summary(window, 1, 7, 7, 7, 0);
}

static void SingleSlot()
{
    var window = new FrameWindow(1);
    window.Add(100);
    window.Add(20);
    Summary(window, 1, 20, 20, 20, 0);
}

static void LargeSamples()
{
    var window = new FrameWindow(2);
    window.Add(double.MaxValue);
    window.Add(double.MaxValue);
    Equal(double.MaxValue, window.Summarize().AverageMs);
}

static void NoAllocations()
{
    var window = new FrameWindow(120);
    for (int i = 0; i < 1000; i++)
    {
        window.Add(10 + i % 30);
        _ = window.Summarize();
    }
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 1000; i++)
    {
        window.Add(10 + i % 30);
        _ = window.Summarize();
    }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Equal(0, allocated);
}

static void LeaseRestores()
{
    int current = 60;
    int writes = 0;
    var lease = new SettingLease<int>(() => current, value => { current = value; writes++; });
    lease.Apply(30);
    Equal(30, current);
    Equal(1, writes);
    if (!lease.Active || !lease.StillOwned)
        throw new Exception("Applied value should be owned.");
    lease.Release();
    Equal(60, current);
    Equal(2, writes);
    if (lease.Active)
        throw new Exception("Released lease should be inactive.");
    lease.Release();
    Equal(2, writes);
}

static void LeaseExternalWrite()
{
    int current = 60;
    int writes = 0;
    var lease = new SettingLease<int>(() => current, value => { current = value; writes++; });
    lease.Apply(30);
    current = 45; // Game or another mod has changed the setting.
    if (lease.StillOwned)
        throw new Exception("External value must not be claimed.");
    lease.Release();
    Equal(45, current);
    Equal(1, writes);
    if (lease.Active)
        throw new Exception("Yielded lease should be inactive.");

    lease.Apply(15);
    lease.Release();
    Equal(45, current); // Next lease snapshots the external setting as its baseline.
}

static void LeaseNoOp()
{
    int current = 30;
    int writes = 0;
    var lease = new SettingLease<int>(() => current, value => { current = value; writes++; });
    lease.Apply(30);
    Equal(0, writes);
    if (lease.Active)
        throw new Exception("An already matching setting must not become owned.");
    current = 45;
    lease.Release();
    Equal(45, current);
    Equal(0, writes);
    lease.Apply(15); // A no-op did not leave an active lease blocking a future apply.
    lease.Release();
    Equal(45, current);
}

static void LeaseReapply()
{
    int current = 60;
    var lease = new SettingLease<int>(() => current, value => current = value);
    lease.Apply(30);
    bool threw = false;
    try { lease.Apply(15); }
    catch (InvalidOperationException) { threw = true; }
    if (!threw)
        throw new Exception("Applying over an active lease must throw.");
    Equal(30, current);
    lease.Release();
    Equal(60, current);
    lease.Apply(15);
    Equal(15, current);
    lease.Release();
    Equal(60, current);
}
