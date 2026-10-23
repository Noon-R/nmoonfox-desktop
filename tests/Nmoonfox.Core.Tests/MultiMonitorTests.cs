using Nmoonfox.Core;

internal static class MultiMonitorTests
{
    private static readonly DesktopDisplay Primary = new("primary", new(0, 0, 1920, 1040), 1, true);
    private static readonly DesktopDisplay Left = new("left", new(-1600, -200, 1600, 1000), 1.5);
    private static readonly DesktopDisplay Upper = new("upper", new(0, -1240, 2200, 1200), 2);
    private static DesktopDisplay[] Layout => [Left, Upper, Primary];

    public static void Run()
    {
        (string Name, Action Test)[] tests =
        [
            ("initial distribution and physical dimensions", Distribution),
            ("drag between negative and upper displays preserves grab point", DragAcrossDisplays),
            ("gap pointer chooses nearest real display", GapSelection),
            ("primary-only mode and switching while paused", DisplayMode),
            ("spread and targeted gather preserve actor identities", GatherSpread),
            ("reordered enumeration does not move actors", Reorder),
            ("same display relocation retains local offset", Relocate),
            ("removed display recovers onto nearest survivor", Disconnect),
            ("DPI and work-area resizing keep complete pet visible", ResizeAndDpi),
            ("very small work area scales complete pet to fit", TinyWorkArea),
            ("long-running roaming never enters gaps", LongRoaming),
            ("retreat stays on originating monitor", Retreat),
            ("speed scales consistently with display DPI", ScaledSpeed),
            ("multi-display setting survives persistence and migration", Persistence),
            ("drag animation advances while paused without moving the pet", DragAnimationClock),
            ("drag tilt follows movement, respects DPI, and settles", DragTilt),
        ];
        foreach (var test in tests)
        {
            try { test.Test(); }
            catch (Exception ex) { throw new InvalidOperationException($"Multi-monitor: {test.Name}", ex); }
            Console.WriteLine($"  PASS multi-monitor: {test.Name}");
        }
    }

    private static PetWorld World(int count = 6, bool roam = true) =>
        new(Layout, new PetSettings { Count = count, RoamEverywhere = roam }, 431);

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Near(double expected, double actual, string message, double tolerance = 1e-7) =>
        Check(double.IsFinite(actual) && Math.Abs(expected - actual) <= tolerance,
            $"{message}: expected {expected:R}, actual {actual:R}");

    private static void Contained(PetWorld world)
    {
        foreach (var actor in world.Pets)
        {
            Check(world.ActiveDisplays.Any(d => d.Id == actor.DisplayId), "actor belongs to an active display");
            var area = world.DisplayFor(actor).WorkArea;
            var width = world.WidthFor(actor);
            var height = world.HeightFor(actor);
            Check(double.IsFinite(actor.X) && double.IsFinite(actor.Y) && width > 0 && height > 0,
                "finite position and positive size");
            Check(actor.X >= area.X - 1e-7 && actor.Y >= area.Y - 1e-7 &&
                  actor.X + width <= area.X + area.Width + 1e-7 &&
                  actor.Y + height <= area.Y + area.Height + 1e-7,
                $"actor {actor.Id} fully contained by {actor.DisplayId}");
        }
    }

    private static void Distribution()
    {
        var world = World();
        Check(new PetSettings().UseAllDisplays, "all displays enabled by default");
        Check(world.Displays.Count == 3 && world.ActiveDisplays.Count == 3, "all displays available");
        Check(world.Pets[0].DisplayId == Primary.Id, "primary assigned first even if enumeration differs");
        foreach (var display in Layout)
        {
            Check(world.Pets.Count(p => p.DisplayId == display.Id) == 2, "even initial distribution");
            foreach (var actor in world.Pets.Where(p => p.DisplayId == display.Id))
            {
                Near(display.Scale, world.ScaleFor(actor), "per-display scale");
                Near(144 * display.Scale, world.WidthFor(actor), "physical width");
                Near((144 * 208.0 / 192 + 24) * display.Scale, world.HeightFor(actor), "physical height");
            }
        }
        Contained(world);
    }

    private static void DragAcrossDisplays()
    {
        var world = World(1);
        var pet = world.Pets[0];
        world.BeginDrag(pet.Id);
        world.DragPointer(pet.Id, -700, 200, .25, .6);
        Check(pet.DisplayId == Left.Id && pet.State == PetState.Dragged, "negative display acquired while dragging");
        Near(-700, pet.X + world.WidthFor(pet) * .25, "left grab X");
        Near(200, pet.Y + world.HeightFor(pet) * .6, "left grab Y");
        world.DragPointer(pet.Id, 700, -700, .25, .6);
        Check(pet.DisplayId == Upper.Id, "upper display acquired");
        Near(700, pet.X + world.WidthFor(pet) * .25, "upper grab X");
        Near(-700, pet.Y + world.HeightFor(pet) * .6, "upper grab Y");
        world.EndDrag(pet.Id);
        Check(pet.DisplayId == Upper.Id && pet.State == PetState.Petting, "release stays on destination and pets");
        Contained(world);
    }

    private static void GapSelection()
    {
        var world = new PetWorld(new[] { Primary, new DesktopDisplay("remote", new(2500, 1500, 1200, 900), 1.5) }, new PetSettings { Count = 1 });
        Check(world.DisplayAt(2400, 1600).Id == "remote", "L-shaped gap chooses nearest rectangle");
        Check(world.DisplayAt(2100, 500).Id == Primary.Id, "horizontal gap near primary");
        var pet = world.Pets[0];
        world.BeginDrag(pet.Id);
        world.DragPointer(pet.Id, 2400, 1600, .5, .5);
        Check(pet.DisplayId == "remote", "drag snaps to nearest real screen");
        Contained(world);
    }

    private static void DisplayMode()
    {
        var world = World();
        world.Paused = true;
        world.ApplySettings(world.Settings with { UseAllDisplays = false });
        Check(world.Displays.Count == 3 && world.ActiveDisplays.Count == 1, "detected screens retained in primary-only mode");
        Check(world.Pets.All(p => p.DisplayId == Primary.Id), "primary-only mode recovers everyone while paused");
        var pet = world.Pets[0];
        world.BeginDrag(pet.Id);
        world.DragPointer(pet.Id, -800, 200, .5, .5);
        Check(pet.DisplayId == Primary.Id, "drag respects primary-only setting");
        world.EndDrag(pet.Id);
        world.ApplySettings(world.Settings with { UseAllDisplays = true });
        Check(world.Pets.Select(p => p.DisplayId).Distinct().Count() == 3, "enabling all displays redistributes");
        Contained(world);
    }

    private static void GatherSpread()
    {
        var world = World();
        var ids = world.Pets.Select(p => p.Id).ToArray();
        world.Gather(Upper.Id);
        Check(world.Pets.All(p => p.DisplayId == Upper.Id), "targeted gather");
        Contained(world);
        world.Spread();
        Check(Layout.All(d => world.Pets.Count(p => p.DisplayId == d.Id) == 2), "spread distributes evenly");
        world.Gather();
        Check(world.Pets.All(p => p.DisplayId == Primary.Id), "default gather uses primary");
        Check(ids.SequenceEqual(world.Pets.Select(p => p.Id)), "commands preserve identity/order");
        Contained(world);
    }

    private static void Reorder()
    {
        var world = World();
        for (var i = 0; i < 300; i++) world.Tick(1.0 / 60);
        var positions = world.Pets.Select(p => (p.Id, p.DisplayId, p.X, p.Y)).ToArray();
        world.ResizeDisplays(Layout.Reverse());
        Check(positions.SequenceEqual(world.Pets.Select(p => (p.Id, p.DisplayId, p.X, p.Y))), "enumeration-only change leaves exact positions");
        Contained(world);
    }

    private static void Relocate()
    {
        var world = World(1);
        var pet = world.Pets[0];
        world.BeginDrag(pet.Id);
        world.DragPointer(pet.Id, -1000, 200, 0, 0);
        world.EndDrag(pet.Id);
        var x = pet.X;
        var y = pet.Y;
        var moved = Left with { WorkArea = Left.WorkArea with { X = -2300, Y = -1500 } };
        world.ResizeDisplays(new[] { Primary, Upper, moved });
        Check(pet.DisplayId == Left.Id, "same display identity preserved");
        Near(x - 700, pet.X, "moved display X offset");
        Near(y - 1300, pet.Y, "moved display Y offset");
        Contained(world);
    }

    private static void Disconnect()
    {
        var world = World(1);
        var pet = world.Pets[0];
        world.BeginDrag(pet.Id);
        world.DragPointer(pet.Id, -1200, 400, .5, .5);
        world.EndDrag(pet.Id);
        world.Paused = true;
        world.ResizeDisplays(new[] { Upper, Primary });
        Check(pet.DisplayId == Primary.Id, "removed left display recovers onto nearest primary, not first enumerated upper");
        Contained(world);
        world.ResizeDisplays(new[] { Upper with { IsPrimary = true } });
        Check(pet.DisplayId == Upper.Id, "second disconnect recovers even while paused");
        Contained(world);
    }

    private static void ResizeAndDpi()
    {
        var world = World();
        world.Gather(Left.Id);
        var resized = Left with { Scale = 2, WorkArea = new(-600, -300, 600, 500) };
        world.ResizeDisplays(new[] { Primary, Upper, resized });
        Check(world.Pets.All(p => p.DisplayId == Left.Id), "resize retains surviving display");
        foreach (var pet in world.Pets) Near(288, world.WidthFor(pet), "DPI refresh width");
        world.ApplySettings(world.Settings with { Size = 240 });
        Contained(world);
    }

    private static void TinyWorkArea()
    {
        var world = new PetWorld(new[] { new DesktopDisplay("tiny", new(-5, -8, 30, 20), 2, true) }, new PetSettings { Count = 3, Size = 240 });
        foreach (var pet in world.Pets)
            Near(240 / (240 * 208.0 / 192 + 24), world.WidthFor(pet) / world.HeightFor(pet), "tiny work area preserves aspect ratio");
        Contained(world);
        for (var i = 0; i < 600; i++) world.Tick(1.0 / 60);
        Contained(world);
    }

    private static void LongRoaming()
    {
        foreach (var roam in new[] { false, true })
        {
            var world = World(12, roam);
            var assignments = world.Pets.Select(p => p.DisplayId).ToArray();
            for (var i = 0; i < 18000; i++)
            {
                world.Tick(1.0 / 30);
                Contained(world);
                Check(assignments.SequenceEqual(world.Pets.Select(p => p.DisplayId)), "roaming does not cross gaps or change screen");
            }
        }
    }

    private static void Retreat()
    {
        foreach (var display in Layout)
        {
            var world = World(1);
            var pet = world.Pets[0];
            world.BeginDrag(pet.Id);
            world.DragPointer(pet.Id, display.WorkArea.X + display.WorkArea.Width * .5,
                display.WorkArea.Y + display.WorkArea.Height * .5, .5, .5);
            world.EndDrag(pet.Id);
            Check(pet.DisplayId == display.Id, "retreat setup screen");
            var pointerX = pet.X + world.WidthFor(pet) * .5 - 20 * display.Scale;
            var pointerY = pet.Y + world.HeightFor(pet) * .5;
            var before = Math.Sqrt(Math.Pow(pet.X + world.WidthFor(pet) * .5 - pointerX, 2) +
                                   Math.Pow(pet.Y + world.HeightFor(pet) * .5 - pointerY, 2));
            Check(world.MakeSpace(pointerX, pointerY) == 1 && pet.IsYielding, "nearby retreat triggers on every scale");
            for (var i = 0; i < 180; i++)
            {
                world.Tick(1.0 / 60);
                Check(pet.DisplayId == display.Id, "retreat retains assigned screen");
                Contained(world);
            }
            var after = Math.Sqrt(Math.Pow(pet.X + world.WidthFor(pet) * .5 - pointerX, 2) +
                                  Math.Pow(pet.Y + world.HeightFor(pet) * .5 - pointerY, 2));
            Check(after > before + 50 * display.Scale, "retreat increases physical distance at each scale");
        }
    }

    private static void ScaledSpeed()
    {
        var a = new PetWorld(new[] { new DesktopDisplay("a", new(0, 0, 1600, 1000), 1, true) }, new PetSettings { Count = 1, RoamEverywhere = true }, 922);
        var b = new PetWorld(new[] { new DesktopDisplay("b", new(0, 0, 3200, 2000), 2, true) }, a.Settings, 922);
        for (var i = 0; i < 3600; i++)
        {
            a.Tick(1.0 / 60);
            b.Tick(1.0 / 60);
            Near(a.Pets[0].X * 2, b.Pets[0].X, "DPI-independent X path", 1e-5);
            Near(a.Pets[0].Y * 2, b.Pets[0].Y, "DPI-independent Y path", 1e-5);
        }
    }

    private static void Persistence()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "nmoonfox-multi-tests-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "settings.json");
            var store = new SettingsStore(path);
            store.Save(new PetSettings { UseAllDisplays = false });
            Check(!store.Load().UseAllDisplays, "primary-only choice persists");
            store.Save(new PetSettings { UseAllDisplays = true });
            Check(store.Load().UseAllDisplays, "all-displays choice persists");
            File.WriteAllText(path, "{\"Count\": 4, \"YieldToMouse\": true}");
            Check(store.Load().UseAllDisplays && store.Load().Count == 4, "legacy settings use all-displays default without losing count");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void DragAnimationClock()
    {
        var world = World(2);
        var pet = world.Pets[0];
        var other = world.Pets[1];
        world.Paused = true;
        var otherBefore = (other.X, other.Y, other.State, other.StateTime);
        world.BeginDrag(pet.Id);
        var position = (pet.X, pet.Y);
        var firstFrame = pet.Frame;
        for (var i = 0; i < 30; i++) world.Tick(1.0 / 60);
        Near(.5, pet.StateTime, "drag clock advances during pause");
        Check(pet.Frame != firstFrame, "drag expression frame changes");
        Check((pet.X, pet.Y) == position, "animation does not change drag position");
        Check((other.X, other.Y, other.State, other.StateTime) == otherBefore, "ordinary actor remains paused");
        Check(pet.State == PetState.Dragged && pet.Affection == 0, "drag has its own motion without petting hearts");
        world.EndDrag(pet.Id);
        Check(pet.State == PetState.Petting && pet.Affection > 0, "release returns to happy petting");
        Near(0, pet.DragTilt, "release resets tilt");
        world.Paused = false;
        world.Tick(.2); world.Tick(.2);
        Check(pet.Frame > 0, "released petting resumes normally");
        Contained(world);
    }

    private static void DragTilt()
    {
        var a = new PetWorld(new[] { new DesktopDisplay("a", new(0, 0, 1600, 1000), 1, true) }, new PetSettings { Count = 1 }, 27);
        var b = new PetWorld(new[] { new DesktopDisplay("b", new(0, 0, 3200, 2000), 2, true) }, new PetSettings { Count = 1 }, 27);
        var first = a.Pets[0];
        var second = b.Pets[0];
        a.BeginDrag(first.Id); b.BeginDrag(second.Id);
        for (var i = 0; i < 50; i++)
        {
            var x = 300 + i * 16;
            a.DragPointer(first.Id, x, 400, .5, .5);
            b.DragPointer(second.Id, x * 2, 800, .5, .5);
            a.Tick(1.0 / 60); b.Tick(1.0 / 60);
            Near(first.DragTilt, second.DragTilt, "same DIP movement gives same tilt");
            Check(Math.Abs(first.DragTilt) <= 6 && Math.Abs(second.DragTilt) <= 6, "tilt bounded during motion");
        }
        Check(first.DragTilt < -1, "rightward movement tilts against travel direction");
        for (var i = 0; i < 30; i++)
        {
            a.DragPointer(first.Id, 1100 - i * 20, 400, .5, .5);
            a.Tick(1.0 / 60);
        }
        Check(first.DragTilt > 1 && first.DragTilt <= 6, "leftward movement reverses tilt within bounds");
        var position = (first.X, first.Y);
        for (var i = 0; i < 180; i++) a.Tick(1.0 / 60);
        Near(0, first.DragTilt, "tilt settles after pointer stops", 1e-6);
        Check((first.X, first.Y) == position, "settling does not move pet");
        Contained(a); Contained(b);
    }
}
