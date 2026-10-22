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
        var bounds = world.Bounds;
        var maxX = bounds.X + Math.Max(0, bounds.Width - world.Settings.Size);
        var maxY = bounds.Y + Math.Max(0, bounds.Height - (world.Settings.Size * 208d / 192 + 24));
        foreach (var pet in world.Pets)
        {
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
            var settings = new PetSettings { Count = 7, Size = 200, Speed = 87, AlwaysOnTop = false, RoamEverywhere = true, ClickThrough = true };
            store.Save(settings); Check(store.Load() == settings, "all settings round-trip");
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
}
