using System.Diagnostics;
using Nmoonfox.Core;

internal static class Program
{
    private static readonly DesktopBounds Screen = new(20, 30, 1280, 720);
    private static int Main()
    {
        (string Name, Action Test)[] tests =
        [
            ("settings defaults and normalization", Normalize),
            ("multi-monitor independent regression suite", MultiMonitorTests.Run),
            ("zero through twelve pets and independent identities", Counts),
            ("same seed and inputs produce same simulation", Determinism),
            ("30fps and 60fps elapsed-time consistency", FrameRate),
            ("invalid and huge deltas stay safe", BadDelta),
            ("pause freezes position and state clocks", Pause),
            ("bounds containment including narrow screens", Bounds),
            ("petting does not restart during its reaction", Petting),
            ("drag overrides autonomous movement and clamps", Drag),
            ("explicit sleep and wake", SleepWake),
            ("animation frames stay inside declared clips", Frames),
            ("pointer cardinal directions and invalid coordinates", PointerDirections),
            ("settings persistence, corruption, and normalization", Persistence),
            ("intentional horizontal and vertical mouse shakes", MouseShakes),
            ("ordinary movement and discontinuities do not shake", MouseFalsePositives),
            ("nearby petting actors make space while distant actors stay", YieldNearby),
            ("yielding escapes every screen corner without approaching", YieldCorners),
            ("yielding exclusions, cooldown, and explicit cancellation", YieldLifecycle),
            ("yielding remains bounded after display and size changes", YieldBounds),
        ];
        var failed = 0;
        foreach (var test in tests)
        {
            try { test.Test(); Console.WriteLine($"PASS {test.Name}"); }
            catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {ex}"); }
        }
        Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed");
        return failed == 0 ? 0 : 1;
    }

    private static PetWorld World(int count = 3, int seed = 123, bool roam = true) =>
        new(Screen, new PetSettings { Count = count, RoamEverywhere = roam }, seed);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Close(double expected, double actual, double epsilon, string message) =>
        Check(double.IsFinite(actual) && Math.Abs(expected - actual) <= epsilon,
            $"{message}; expected {expected:R}, actual {actual:R}, tolerance {epsilon:R}");

    private static void Normalize()
    {
        var defaults = new PetSettings().Normalize();
        Check(defaults.Count == 3 && defaults.Size == 144 && defaults.Speed == 55, "documented defaults");
        Check(defaults.AlwaysOnTop && !defaults.RoamEverywhere && !defaults.ClickThrough, "default switches");
        var low = new PetSettings { Count = -20, Size = -10, Speed = -5 }.Normalize();
        Check(low.Count == 0 && low.Size == 96 && low.Speed == 20, "lower limits");
        var high = new PetSettings { Count = int.MaxValue, Size = 10000, Speed = 10000 }.Normalize();
        Check(high.Count == 12 && high.Size == 240 && high.Speed == 140, "upper limits");
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var normalized = new PetSettings { Size = invalid, Speed = invalid }.Normalize();
            Check(double.IsFinite(normalized.Size) && normalized.Size is >= 96 and <= 240, "invalid size fallback");
            Check(double.IsFinite(normalized.Speed) && normalized.Speed is >= 20 and <= 140, "invalid speed fallback");
            Check(normalized == normalized.Normalize(), "normalization is idempotent");
        }
    }

    private static void Counts()
    {
        var world = World(0);
        for (var count = 0; count <= 12; count++)
        {
            world.ApplySettings(world.Settings with { Count = count });
            Check(world.Pets.Count == count, $"count {count}");
            Check(world.Pets.Select(p => p.Id).Distinct().Count() == count, "unique actor ids");
            if (count > 1) Check(!ReferenceEquals(world.Pets[0], world.Pets[1]), "independent objects");
            world.Tick(1d / 30);
        }
        world.ApplySettings(world.Settings with { Count = 999 });
        Check(world.Pets.Count == 12 && world.Settings.Count == 12, "world applies normalization");
        world.Remove(world.Pets[4].Id);
        Check(world.Pets.Count == 11 && world.Settings.Count == 11, "remove updates count");
        world.ApplySettings(world.Settings with { Count = -1 });
        Check(world.Pets.Count == 0, "zero supported after removal");
        world.Pet(int.MinValue); world.BeginDrag(int.MinValue); world.DragTo(int.MinValue, 0, 0);
        world.EndDrag(int.MinValue); world.Remove(int.MinValue); world.Gather(); world.SleepAll(); world.WakeAll();
    }

    private static void Determinism()
    {
        var a = World(seed: 825); var b = World(seed: 825);
        for (var i = 0; i < 900; i++)
        {
            if (i == 40) { a.Pet(a.Pets[0].Id); b.Pet(b.Pets[0].Id); }
            if (i == 200) { a.Gather(); b.Gather(); }
            a.Tick(1d / 30); b.Tick(1d / 30);
        }
        Compare(a, b, 1e-9, true);
    }

    private static void FrameRate()
    {
        var a = World(seed: 971); var b = World(seed: 971);
        for (var i = 0; i < 360; i++) a.Tick(1d / 30);
        for (var i = 0; i < 720; i++) b.Tick(1d / 60);
        Compare(a, b, 1e-8, true);
    }

    private static void Compare(PetWorld a, PetWorld b, double positionTolerance, bool clocks)
    {
        Check(a.Pets.Count == b.Pets.Count, "same count");
        for (var i = 0; i < a.Pets.Count; i++)
        {
            var x = a.Pets[i]; var y = b.Pets[i];
            Check(x.Id == y.Id && x.State == y.State, $"matching id/state {i}: {x.State}/{y.State}");
            Close(x.X, y.X, positionTolerance, $"X {i}"); Close(x.Y, y.Y, positionTolerance, $"Y {i}");
            if (clocks) { Close(x.StateTime, y.StateTime, 1e-9, "state clock"); Close(x.Affection, y.Affection, 1e-9, "affection clock"); }
        }
    }

    private static void BadDelta()
    {
        var world = World(); var before = Snapshot(world);
        foreach (var dt in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0d }) world.Tick(dt);
        Check(before == Snapshot(world), "invalid/nonpositive elapsed time is ignored");
        var watch = Stopwatch.StartNew(); world.Tick(double.MaxValue); watch.Stop();
        Check(watch.Elapsed.TotalSeconds < 2, "huge delta must be capped, not iterated unboundedly");
        AssertContained(world);
    }

    private static string Snapshot(PetWorld world) => string.Join("|", world.Pets.Select(p =>
        $"{p.Id},{p.X:R},{p.Y:R},{p.State},{p.StateTime:R},{p.Affection:R},{p.LookDirection},{p.Frame}"));

    private static void Pause()
    {
        var world = World(); world.Tick(.5); world.Pet(world.Pets[0].Id); world.Tick(.1);
        world.Paused = true; var before = Snapshot(world);
        for (var i = 0; i < 100; i++) world.Tick(.1);
        Check(before == Snapshot(world), "paused snapshots must stay identical including reaction clocks");
        world.Paused = false; world.Tick(.1);
        Check(before != Snapshot(world), "resume progresses clocks");
    }

    private static void Bounds()
    {
        foreach (var roam in new[] { false, true })
        {
            var world = World(12, roam: roam);
            for (var i = 0; i < 3000; i++) { world.Tick(1d / 30); AssertContained(world); }
            world.ApplySettings(world.Settings with { Size = 240 }); AssertContained(world);
            world.ResizeBounds(new(-1500, -600, 800, 400)); AssertContained(world);
            world.Gather(); AssertContained(world);
            world.ResizeBounds(new(-50, 70, 20, 10)); AssertContained(world);
            for (var i = 0; i < 100; i++) { world.Tick(.1); AssertContained(world); }
            world.ResizeBounds(new(double.NaN, double.PositiveInfinity, double.NaN, -1)); AssertContained(world);
        }
    }

    private static void AssertContained(PetWorld world)
    {
        foreach (var pet in world.Pets)
        {
            var bounds = world.DisplayFor(pet).WorkArea;
            var maxX = bounds.X + Math.Max(0, bounds.Width - world.WidthFor(pet));
            var maxY = bounds.Y + Math.Max(0, bounds.Height - world.HeightFor(pet));
            Check(double.IsFinite(pet.X) && pet.X >= bounds.X - 1e-7 && pet.X <= maxX + 1e-7, $"X in bounds ({pet.X}, {bounds.X}..{maxX})");
            Check(double.IsFinite(pet.Y) && pet.Y >= bounds.Y - 1e-7 && pet.Y <= maxY + 1e-7, $"Y in bounds ({pet.Y}, {bounds.Y}..{maxY})");
            Check(double.IsFinite(pet.StateTime) && double.IsFinite(pet.Affection), "finite actor clocks");
        }
    }

    private static void Petting()
    {
        var world = World(2); var pet = world.Pets[0]; var other = world.Pets[1];
        world.Pet(pet.Id); Check(pet.State == PetState.Petting, "petting state");
        Check(other.State != PetState.Petting, "affects selected actor only");
        world.Tick(.1); var time = pet.StateTime; var affection = pet.Affection;
        for (var i = 0; i < 10; i++) world.Pet(pet.Id);
        Close(time, pet.StateTime, 1e-9, "repeated petting does not restart animation");
        Close(affection, pet.Affection, 1e-9, "repeated petting does not prolong hearts");
        for (var i = 0; i < 100; i++) world.Tick(.1);
        world.Pet(pet.Id); Check(pet.State == PetState.Petting, "new reaction after cooldown");
    }

    private static void Drag()
    {
        var world = World(1); var pet = world.Pets[0];
        world.BeginDrag(pet.Id); Check(pet.State == PetState.Dragged, "drag state");
        world.DragTo(pet.Id, 300, 200); Close(300, pet.X, 1e-9, "drag X"); Close(200, pet.Y, 1e-9, "drag Y");
        world.Tick(1); Close(300, pet.X, 1e-9, "drag stops wandering X"); Close(200, pet.Y, 1e-9, "drag stops wandering Y");
        world.DragTo(pet.Id, -100000, 100000); AssertContained(world);
        var beforeInvalid = Snapshot(world);
        world.DragTo(pet.Id, double.NaN, 0); world.DragTo(pet.Id, 0, double.PositiveInfinity);
        Check(beforeInvalid == Snapshot(world), "invalid drag input is ignored");
        world.EndDrag(pet.Id); Check(pet.State == PetState.Petting, "release gets happy grounded reaction");
    }

    private static void SleepWake()
    {
        var world = World(); world.SleepAll();
        Check(world.Pets.All(p => p.State == PetState.Sleep), "sleep all immediately");
        var positions = world.Pets.Select(p => (p.X, p.Y)).ToArray();
        for (var i = 0; i < 200; i++) world.Tick(.1);
        for (var i = 0; i < positions.Length; i++)
        {
            Check(world.Pets[i].State == PetState.Sleep, "explicit sleep stays asleep");
            Close(positions[i].X, world.Pets[i].X, 1e-9, "sleep X"); Close(positions[i].Y, world.Pets[i].Y, 1e-9, "sleep Y");
        }
        world.WakeAll(); Check(world.Pets.All(p => p.State != PetState.Sleep), "wake all immediately");
    }

    private static void Frames()
    {
        foreach (var state in Enum.GetValues<PetState>())
        {
            var clip = AnimationCatalog.Get(state);
            Check(clip.Row is >= 0 and < 9 && clip.Frames is > 0 and <= 8 && clip.Fps > 0, $"valid clip {state}");
        }
        var world = World(12);
        for (var i = 0; i < 3600; i++)
        {
            world.Tick(1d / 30);
            foreach (var pet in world.Pets)
            {
                Check(pet.Frame >= 0 && pet.Frame < AnimationCatalog.Get(pet.State).Frames, $"valid frame {pet.State}/{pet.Frame}");
                Check(pet.LookDirection is >= 0 and < 16, "look direction range");
            }
        }
    }

    private static void PointerDirections()
    {
        var world = World(1); var pet = world.Pets[0];
        var cx = pet.X + world.Settings.Size / 2;
        var cy = pet.Y + (world.Settings.Size * 208d / 192 + 24) * .45;
        (double X, double Y, int Direction)[] cases =
        [(cx + 100, cy, 0), (cx, cy - 100, 4), (cx - 100, cy, 8), (cx, cy + 100, 12)];
        foreach (var item in cases)
        {
            world.ObservePointer(item.X, item.Y);
            Check(pet.LookDirection == item.Direction, $"expected direction {item.Direction}, got {pet.LookDirection}");
        }
        var direction = pet.LookDirection;
        world.ObservePointer(double.NaN, 0); world.ObservePointer(0, double.PositiveInfinity);
        Check(pet.LookDirection == direction, "invalid pointer ignored");
    }

    private static void Persistence()
    {
        var testRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "core-test-data"));
        var folder = Path.Combine(testRoot, "nmoonfox-core-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "nested", "settings.json"); var store = new SettingsStore(path);
            Check(store.Load() == new PetSettings(), "missing file defaults");
            Check(new PetSettings().YieldToMouse, "mouse yielding enabled by default");
            var settings = new PetSettings { Count = 7, Size = 200, Speed = 87, AlwaysOnTop = false, RoamEverywhere = true, ClickThrough = true, YieldToMouse = false };
            store.Save(settings); Check(store.Load() == settings, "all settings round-trip");
            File.WriteAllText(path, "{\"Count\":4,\"Size\":144,\"Speed\":55}");
            Check(store.Load().Count == 4 && store.Load().YieldToMouse, "legacy settings opt into new default");
            store.Save(settings with { YieldToMouse = true });
            Check(store.Load().YieldToMouse, "enabled yielding persists");
            store.Save(settings with { Count = 1 }); Check(store.Load().Count == 1, "replace existing file");
            File.WriteAllText(path, "{ broken JSON"); Check(store.Load() == new PetSettings(), "corruption defaults");
            File.WriteAllText(path, "null"); Check(store.Load() == new PetSettings(), "null defaults");
            File.WriteAllText(path, "{\"Count\":999,\"Size\":5000,\"Speed\":-8}");
            var normalized = store.Load();
            Check(normalized.Count == 12 && normalized.Size == 240 && normalized.Speed == 20, "out-of-range file normalized");
            store.Save(new PetSettings { Count = -9, Size = double.NaN, Speed = double.PositiveInfinity });
            var safe = store.Load(); Check(safe == safe.Normalize(), "save normalizes invalid input before serialization");
            Check(Directory.GetFiles(Path.GetDirectoryName(path)!).Length == 1, "successful save leaves no temp files");
        }
        finally
        {
            Check(Path.GetFullPath(folder).StartsWith(testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "cleanup stays in test artifacts");
            Directory.Delete(folder, true);
        }
    }

    private static bool Feed(MouseShakeDetector detector, double[] offsets, bool vertical = false,
        double interval = .05, double start = 0)
    {
        var detected = false;
        for (var i = 0; i < offsets.Length; i++)
            detected |= detector.Sample(500 + (vertical ? 0 : offsets[i]), 400 + (vertical ? offsets[i] : 0), start + i * interval);
        return detected;
    }

    private static void MouseShakes()
    {
        double[] shake = [0, 60, 120, 60, 0, 60, 120];
        Check(Feed(new(), shake), "deliberate horizontal two-reversal shake");
        Check(Feed(new(), shake, vertical: true), "deliberate vertical two-reversal shake");
        Check(Feed(new(), [0, 40, 80, 82, 40, 0, -2, 40, 80], interval: 1d / 30),
            "natural shake can decelerate for one sample at each turning point");
        foreach (var fps in new[] { 30, 60 })
            Check(Feed(new(), Enumerable.Range(0, fps).Select(i => Math.Sin(i * 2 * Math.PI * 3 / fps) * 60).ToArray(),
                interval: 1d / fps), $"smooth hand-like oscillation at {fps} samples/second");
        var detector = new MouseShakeDetector();
        Feed(detector, [0, 60, 120, 60, 0]);
        detector.Reset();
        Check(!Feed(detector, [0, 60, 120], start: .25), "Reset discards prior reversals");
        Check(Feed(detector, shake, start: 2), "a later deliberate gesture still works");
    }

    private static void MouseFalsePositives()
    {
        Check(!Feed(new(), Enumerable.Repeat(0d, 40).ToArray()), "stationary pointer");
        Check(!Feed(new(), [0, 60, 120, 180, 240, 300]), "fast one-way swipe");
        Check(!Feed(new(), [0, 60, 120, 60, 0]), "one reversal alone");
        Check(!Feed(new(), [0, 1, -1, 1, -1, 1, -1]), "tiny tremor");
        Check(!Feed(new(), [0, 60, 120, 60, 0, 60, 120], interval: .3), "slow oscillation");
        Check(!Feed(new(), Enumerable.Range(0, 30).Select(i => Math.Sin(i * 2 * Math.PI * 3 / 30) * 12).ToArray(),
            interval: 1d / 30), "slow continuous oscillation at normal polling interval");
        Check(!Feed(new(), [0, 1600, 0, 1600, 0]), "coordinate warps");
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var detector = new MouseShakeDetector();
            Feed(detector, [0, 60, 120, 60, 0]);
            Check(!detector.Sample(invalid, 400, .21), "invalid coordinate rejected");
            Check(!Feed(detector, [0, 60, 120], start: .25), "invalid coordinate resets history");
            detector.Reset(); Feed(detector, [0, 60, 120, 60, 0]);
            Check(!detector.Sample(500, 400, invalid), "invalid time rejected");
            Check(!Feed(detector, [0, 60, 120], start: .25), "invalid time resets history");
        }
        foreach (var restart in new[] { 0d, 4d })
        {
            var detector = new MouseShakeDetector();
            Feed(detector, [0, 60, 120, 60, 0]);
            Check(!Feed(detector, [0, 60, 120], start: restart), "backwards time or long gap clears history");
        }
    }

    private static void Place(PetWorld world, PetActor actor, double x, double y)
    {
        world.BeginDrag(actor.Id); world.DragTo(actor.Id, x, y); world.EndDrag(actor.Id);
    }

    private static (double X, double Y) Center(PetWorld world, PetActor actor) =>
        (actor.X + world.Settings.Size / 2, actor.Y + world.PetHeight / 2);

    private static double Distance(PetWorld world, PetActor actor, double x, double y)
    {
        var center = Center(world, actor);
        return Math.Sqrt(Math.Pow(center.X - x, 2) + Math.Pow(center.Y - y, 2));
    }

    private static void YieldNearby()
    {
        var world = World(2, roam: false); var near = world.Pets[0]; var far = world.Pets[1];
        Place(world, near, 400, 300); Place(world, far, 1100, 300);
        var cursor = Center(world, near); cursor.X -= 20;
        Check(near.State == PetState.Petting, "hover/drag-release reaction precedes shake");
        Check(world.MakeSpace(cursor.X, cursor.Y) == 1, "only the nearby actor responds");
        Check(near.IsYielding && !far.IsYielding, "yield flag identifies actor");
        var previous = Distance(world, near, cursor.X, cursor.Y);
        var initial = previous;
        for (var i = 0; i < 90; i++)
        {
            world.Pet(near.Id);
            world.Tick(1d / 30);
            Check(near.State != PetState.Petting, "petting cannot interrupt making space");
            var distance = Distance(world, near, cursor.X, cursor.Y);
            Check(distance + 1e-6 >= previous, "escape never moves closer to gesture point");
            previous = distance; AssertContained(world);
        }
        Check(previous > initial + 100, "escape opens useful working space");
    }

    private static void YieldCorners()
    {
        foreach (var right in new[] { false, true })
        foreach (var bottom in new[] { false, true })
        {
            var world = World(1, roam: false); var actor = world.Pets[0];
            Place(world, actor, right ? Screen.X + Screen.Width - world.Settings.Size : Screen.X,
                bottom ? Screen.Y + Screen.Height - world.PetHeight : Screen.Y);
            var cursor = Center(world, actor);
            cursor.X += right ? -15 : 15;
            var start = Distance(world, actor, cursor.X, cursor.Y);
            Check(world.MakeSpace(cursor.X, cursor.Y) == 1, $"corner escape exists {right}/{bottom}");
            var previous = start;
            for (var i = 0; i < 90; i++)
            {
                world.Tick(1d / 30); AssertContained(world);
                var distance = Distance(world, actor, cursor.X, cursor.Y);
                Check(distance + 1e-6 >= previous, "corner escape never approaches cursor");
                previous = distance;
            }
            Check(previous > start + 100, "corner fallback creates space");

            // At a corner with the cursor diagonally inward, every feasible straight
            // first step is closer. Choose a tangential escape rather than getting stuck.
            Place(world, actor, right ? Screen.X + Screen.Width - world.Settings.Size : Screen.X,
                bottom ? Screen.Y + Screen.Height - world.PetHeight : Screen.Y);
            cursor = Center(world, actor);
            cursor.X += right ? -15 : 15; cursor.Y += bottom ? -15 : 15;
            start = Distance(world, actor, cursor.X, cursor.Y);
            Check(world.MakeSpace(cursor.X, cursor.Y) == 1, "diagonal corner has a fallback escape");
            for (var i = 0; i < 90; i++)
            {
                world.Tick(1d / 30); AssertContained(world);
                Check(Distance(world, actor, cursor.X, cursor.Y) > start * .6,
                    "corner fallback avoids crossing directly over the pointer");
            }
            Check(Distance(world, actor, cursor.X, cursor.Y) > start + 100,
                "diagonal corner fallback ends safely farther away");
        }
    }

    private static void YieldLifecycle()
    {
        var world = World(1); var actor = world.Pets[0];
        Place(world, actor, 400, 300); var cursor = Center(world, actor);
        Check(world.MakeSpace(double.NaN, cursor.Y) == 0 && world.MakeSpace(cursor.X, double.PositiveInfinity) == 0,
            "invalid gesture position ignored");
        world.Paused = true; Check(world.MakeSpace(cursor.X, cursor.Y) == 0, "pause excludes yielding"); world.Paused = false;
        world.ApplySettings(world.Settings with { YieldToMouse = false });
        Check(world.MakeSpace(cursor.X, cursor.Y) == 0 && !actor.IsYielding, "setting off excludes yielding");
        world.ApplySettings(world.Settings with { YieldToMouse = true });
        world.SleepAll(); Check(world.MakeSpace(cursor.X, cursor.Y) == 0, "sleep excludes yielding"); world.WakeAll();
        world.BeginDrag(actor.Id); Check(world.MakeSpace(cursor.X, cursor.Y) == 0, "drag excludes yielding"); world.EndDrag(actor.Id);
        Check(world.MakeSpace(cursor.X, cursor.Y) == 1, "wake/release eligible");
        Check(world.MakeSpace(cursor.X, cursor.Y) == 0, "active retreat does not restart");
        for (var i = 0; i < 116; i++) world.Tick(.05);
        Check(actor.IsYielding, "cooldown remains active at 5.8 seconds");
        cursor = Center(world, actor);
        Check(world.MakeSpace(cursor.X, cursor.Y) == 0, "arrival does not permit premature retrigger");
        for (var i = 0; i < 6; i++) world.Tick(.05);
        Check(!actor.IsYielding, "cooldown ends after six seconds");
        cursor = Center(world, actor);
        Check(world.MakeSpace(cursor.X, cursor.Y) == 1, "next gesture can trigger after cooldown");
        Action[] cancel = [() => world.SleepAll(), () => world.WakeAll(), () => world.Gather(),
            () => world.BeginDrag(actor.Id), () => world.ApplySettings(world.Settings with { YieldToMouse = false })];
        foreach (var action in cancel)
        {
            world.ApplySettings(world.Settings with { YieldToMouse = true });
            world.WakeAll(); Place(world, actor, 400, 300); cursor = Center(world, actor);
            Check(world.MakeSpace(cursor.X, cursor.Y) == 1, "test cancellation begins from retreat");
            action(); Check(!actor.IsYielding, "explicit action cancels retreat");
        }
    }

    private static void YieldBounds()
    {
        var world = World(12); world.Gather();
        foreach (var actor in world.Pets) { var cursor = Center(world, actor); world.MakeSpace(cursor.X, cursor.Y); }
        world.ApplySettings(world.Settings with { Size = 240 });
        world.ResizeBounds(new(-1400, -300, 500, 300));
        for (var i = 0; i < 180; i++) { world.Tick(1d / 30); AssertContained(world); }
        world.ResizeBounds(new(0, 0, 20, 10));
        var point = Center(world, world.Pets[0]); world.MakeSpace(point.X, point.Y);
        for (var i = 0; i < 180; i++) { world.Tick(1d / 30); AssertContained(world); }
    }
}
