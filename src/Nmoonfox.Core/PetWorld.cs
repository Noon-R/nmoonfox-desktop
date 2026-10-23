namespace Nmoonfox.Core;

public readonly record struct DesktopBounds(double X, double Y, double Width, double Height);

public sealed class PetActor
{
    public int Id { get; internal set; }
    public string DisplayId { get; internal set; } = "primary";
    public double X { get; internal set; }
    public double Y { get; internal set; }
    public PetState State { get; internal set; }
    public double StateTime { get; internal set; }
    public double Affection { get; internal set; }
    public int LookDirection { get; internal set; }
    public double DragTilt { get; internal set; }
    public bool IsYielding => YieldRemaining > 0;
    public int Frame
    {
        get
        {
            var clip = AnimationCatalog.Get(State);
            var index = (int)(StateTime * clip.Fps);
            return State is PetState.Petting ? Math.Min(index, clip.Frames - 1) : index % clip.Frames;
        }
    }
    internal double Duration;
    internal double TargetX;
    internal double TargetY;
    internal double PetCooldown;
    internal bool SleepLocked;
    internal double YieldRemaining;
    internal double LastDragX;
}

public sealed class PetWorld
{
    private readonly Random random;
    private readonly List<PetActor> pets = [];
    private readonly IReadOnlyList<PetActor> view;
    private int nextId;
    private double accumulator;
    private const double Step = 1.0 / 60;
    public PetSettings Settings { get; private set; }
    public DesktopBounds Bounds => Displays[0].WorkArea;
    public IReadOnlyList<DesktopDisplay> Displays { get; private set; } = Array.Empty<DesktopDisplay>();
    public IReadOnlyList<DesktopDisplay> ActiveDisplays => Settings.UseAllDisplays ? Displays : new[] { Displays[0] };
    public IReadOnlyList<PetActor> Pets => view;
    public bool Paused { get; set; }
    public double PetHeight => Settings.Size * 208 / 192 + 24;

    public PetWorld(DesktopBounds bounds, PetSettings settings, int seed = 1)
        : this(new[] { new DesktopDisplay("primary", bounds, 1, true) }, settings, seed) { }

    public PetWorld(IEnumerable<DesktopDisplay> displays, PetSettings settings, int seed = 1)
    {
        random = new(seed);
        view = pets.AsReadOnly();
        Settings = settings.Normalize();
        Displays = NormalizeDisplays(displays);
        ApplySettings(Settings);
    }

    public void ApplySettings(PetSettings settings)
    {
        var scopeChanged = Settings.UseAllDisplays != settings.UseAllDisplays;
        Settings = settings.Normalize();
        if (!Settings.YieldToMouse)
            foreach (var actor in pets) CancelYield(actor);
        while (pets.Count > Settings.Count) pets.RemoveAt(pets.Count - 1);
        while (pets.Count < Settings.Count)
        {
            var display = ActiveDisplays[pets.Count % ActiveDisplays.Count];
            var actor = new PetActor { Id = ++nextId, DisplayId = display.Id };
            actor.X = display.WorkArea.X + random.NextDouble() * AvailableX(actor);
            actor.Y = display.WorkArea.Y + AvailableY(actor);
            Enter(actor, PetState.Wave, 1.2 + random.NextDouble());
            pets.Add(actor);
        }
        if (scopeChanged) Spread();
        else ClampAll();
    }

    public void ResizeBounds(DesktopBounds bounds) => ResizeDisplays(new[] { new DesktopDisplay("primary", bounds, 1, true) });

    public void ResizeDisplays(IEnumerable<DesktopDisplay> displays)
    {
        var previous = Displays;
        Displays = NormalizeDisplays(displays);
        foreach (var actor in pets)
        {
            var old = previous.FirstOrDefault(d => d.Id == actor.DisplayId);
            var current = ActiveDisplays.FirstOrDefault(d => d.Id == actor.DisplayId);
            if (current != null && old != null)
            {
                var dx = current.WorkArea.X - old.WorkArea.X;
                var dy = current.WorkArea.Y - old.WorkArea.Y;
                actor.X += dx; actor.Y += dy; actor.TargetX += dx; actor.TargetY += dy;
            }
            else
            {
                actor.DisplayId = DisplayAt(actor.X, actor.Y).Id;
                CancelYield(actor);
            }
            Clamp(actor);
        }
    }

    public DesktopDisplay DisplayFor(PetActor actor) => ActiveDisplays.FirstOrDefault(d => d.Id == actor.DisplayId) ?? ActiveDisplays[0];
    public DesktopDisplay DisplayAt(double x, double y) => ActiveDisplays.MinBy(d => DistanceSquared(d.WorkArea, x, y))!;
    public double ScaleFor(PetActor actor)
    {
        var display = DisplayFor(actor);
        return Math.Min(display.Scale, Math.Min(display.WorkArea.Width / Settings.Size, display.WorkArea.Height / PetHeight));
    }
    public double WidthFor(PetActor actor) => Settings.Size * ScaleFor(actor);
    public double HeightFor(PetActor actor) => PetHeight * ScaleFor(actor);

    public void DragPointer(int id, double x, double y, double anchorX, double anchorY)
    {
        var actor = Find(id);
        if (actor == null || actor.State != PetState.Dragged || !double.IsFinite(x) || !double.IsFinite(y) ||
            !double.IsFinite(anchorX) || !double.IsFinite(anchorY)) return;
        actor.DisplayId = DisplayAt(x, y).Id;
        actor.X = x - Math.Clamp(anchorX, 0, 1) * WidthFor(actor);
        actor.Y = y - Math.Clamp(anchorY, 0, 1) * HeightFor(actor);
        Clamp(actor);
    }

    // Fixed steps keep random choices and movement independent of renderer refresh rate.
    // Do not fast-forward after suspension or a blocked UI thread.
    public void Tick(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        accumulator += Math.Min(seconds, .25);
        while (accumulator + 1e-10 >= Step)
        {
            accumulator -= Step;
            foreach (var actor in pets)
                if (!Paused || actor.State == PetState.Dragged) Advance(actor, Step);
        }
    }

    public void Pet(int id)
    {
        var actor = Find(id);
        if (actor == null || actor.IsYielding || actor.State == PetState.Dragged || actor.State == PetState.Petting || actor.PetCooldown > 0) return;
        actor.SleepLocked = false;
        actor.PetCooldown = 2.8;
        actor.Affection = 2;
        Enter(actor, PetState.Petting, 1.6);
    }

    public void BeginDrag(int id)
    {
        var actor = Find(id);
        if (actor == null) return;
        CancelYield(actor);
        actor.SleepLocked = false;
        actor.Affection = 0; actor.DragTilt = 0; actor.LastDragX = actor.X;
        Enter(actor, PetState.Dragged, double.PositiveInfinity);
    }

    public void DragTo(int id, double x, double y)
    {
        var actor = Find(id);
        if (actor == null || actor.State != PetState.Dragged || !double.IsFinite(x) || !double.IsFinite(y)) return;
        actor.DisplayId = DisplayAt(x + WidthFor(actor) / 2, y + HeightFor(actor) / 2).Id;
        actor.X = x; actor.Y = y; Clamp(actor);
    }

    public void EndDrag(int id)
    {
        var actor = Find(id);
        if (actor == null || actor.State != PetState.Dragged) return;
        actor.DragTilt = 0;
        Enter(actor, PetState.Idle, 2);
        actor.PetCooldown = 0;
        Pet(id);
    }

    public void SleepAll()
    {
        foreach (var actor in pets)
        {
            CancelYield(actor);
            actor.SleepLocked = true;
            actor.Affection = 0;
            Enter(actor, PetState.Sleep, double.PositiveInfinity);
        }
    }

    public void WakeAll()
    {
        foreach (var actor in pets) { CancelYield(actor); actor.SleepLocked = false; Enter(actor, PetState.Wave, 1.4); }
    }

    public void Gather(string? displayId = null)
    {
        var display = ActiveDisplays.FirstOrDefault(d => d.Id == displayId) ?? ActiveDisplays[0];
        for (var i = 0; i < pets.Count; i++)
        {
            var actor = pets[i];
            CancelYield(actor);
            actor.DisplayId = display.Id;
            actor.X = display.WorkArea.X + AvailableX(actor) * (i + 1) / (pets.Count + 1);
            actor.Y = display.WorkArea.Y + AvailableY(actor);
            actor.SleepLocked = false;
            Enter(actor, PetState.Wave, 1.2 + random.NextDouble());
        }
    }

    public void Spread()
    {
        for (var i = 0; i < pets.Count; i++)
        {
            var actor = pets[i];
            var display = ActiveDisplays[i % ActiveDisplays.Count];
            actor.DisplayId = display.Id;
            var count = (pets.Count - 1 - i % ActiveDisplays.Count) / ActiveDisplays.Count + 1;
            actor.X = display.WorkArea.X + AvailableX(actor) * (i / ActiveDisplays.Count + 1) / (count + 1);
            actor.Y = display.WorkArea.Y + AvailableY(actor);
            CancelYield(actor); actor.SleepLocked = false;
            Enter(actor, PetState.Wave, 1.2 + random.NextDouble());
            Clamp(actor);
        }
    }

    public void Remove(int id)
    {
        var actor = Find(id);
        if (actor == null) return;
        pets.Remove(actor);
        Settings = Settings with { Count = pets.Count };
    }

    public void ObservePointer(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return;
        foreach (var actor in pets)
        {
            var angle = Math.Atan2(actor.Y + HeightFor(actor) * .45 - y, x - actor.X - WidthFor(actor) / 2);
            actor.LookDirection = ((int)Math.Round(angle / (2 * Math.PI) * 16) + 16) % 16;
        }
    }

    public int MakeSpace(double x, double y)
    {
        if (Paused || !Settings.YieldToMouse || !double.IsFinite(x) || !double.IsFinite(y)) return 0;
        var count = 0;
        foreach (var actor in pets)
        {
            if (actor.IsYielding || actor.State is PetState.Dragged or PetState.Sleep) continue;
            var bounds = DisplayFor(actor).WorkArea;
            var scale = ScaleFor(actor);
            var centerX = actor.X + WidthFor(actor) / 2;
            var centerY = actor.Y + HeightFor(actor) / 2;
            var awayX = centerX - x; var awayY = centerY - y;
            var originalDistance = awayX * awayX + awayY * awayY;
            var radius = (Settings.Size * .65 + 140) * scale;
            if (originalDistance > radius * radius) continue;
            var retreat = Math.Max(240, Settings.Size * 1.8) * scale;
            var bestScore = originalDistance + 16;
            var targetX = actor.X; var targetY = actor.Y;
            var fallbackScore = double.NegativeInfinity;
            var fallbackX = actor.X; var fallbackY = actor.Y;
            for (var i = 0; i < 16; i++)
            {
                var angle = i * Math.PI / 8;
                var candidateX = Math.Clamp(actor.X + Math.Cos(angle) * retreat, bounds.X, bounds.X + AvailableX(actor));
                var candidateY = Math.Clamp(actor.Y + Math.Sin(angle) * retreat, bounds.Y, bounds.Y + AvailableY(actor));
                var moveX = candidateX - actor.X; var moveY = candidateY - actor.Y;
                var movementSquared = moveX * moveX + moveY * moveY;
                var endX = candidateX + WidthFor(actor) / 2 - x;
                var endY = candidateY + HeightFor(actor) / 2 - y;
                var score = endX * endX + endY * endY;
                if (movementSquared < 16 || score <= originalDistance + 16) continue;
                var projection = moveX * awayX + moveY * awayY;
                var closestT = Math.Clamp(-projection / movementSquared, 0, 1);
                var closestSquared = originalDistance + 2 * closestT * projection + closestT * closestT * movementSquared;
                var clearanceScore = closestSquared * 1000 + score;
                if (clearanceScore > fallbackScore)
                { fallbackScore = clearanceScore; fallbackX = candidateX; fallbackY = candidateY; }
                // Do not cross the cursor while trying to get farther away at an edge.
                if (projection < -1e-6) continue;
                if (score <= bestScore) continue;
                bestScore = score; targetX = candidateX; targetY = candidateY;
            }
            if (bestScore <= originalDistance + 16)
            {
                // In a boxed-in corner every exit can initially get closer. Prefer
                // the path with greatest cursor clearance instead of becoming stuck.
                if (double.IsNegativeInfinity(fallbackScore)) continue;
                targetX = fallbackX; targetY = fallbackY;
            }
            actor.TargetX = targetX; actor.TargetY = targetY;
            actor.YieldRemaining = 6; actor.PetCooldown = 6; actor.Affection = 0;
            Enter(actor, targetX < actor.X ? PetState.WalkLeft : PetState.WalkRight, 25);
            count++;
        }
        return count;
    }

    private void Advance(PetActor actor, double delta)
    {
        if (actor.State == PetState.Dragged)
        {
            actor.StateTime += delta;
            var movement = (actor.X - actor.LastDragX) / ScaleFor(actor);
            var target = Math.Clamp(-movement * .35, -6, 6);
            actor.DragTilt += (target - actor.DragTilt) * (1 - Math.Exp(-12 * delta));
            actor.LastDragX = actor.X;
            return;
        }
        actor.StateTime += delta;
        actor.PetCooldown = Math.Max(0, actor.PetCooldown - delta);
        actor.YieldRemaining = Math.Max(0, actor.YieldRemaining - delta);
        actor.Affection = Math.Max(0, actor.Affection - delta);
        if (actor.State is PetState.WalkLeft or PetState.WalkRight)
        {
            var dx = actor.TargetX - actor.X;
            var dy = actor.TargetY - actor.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            var speed = actor.IsYielding ? Math.Clamp(Settings.Speed * 2, 160, 260) : Settings.Speed;
            var travel = speed * ScaleFor(actor) * delta;
            if (distance <= travel)
            {
                actor.X = actor.TargetX; actor.Y = actor.TargetY;
                Enter(actor, PetState.Idle, actor.IsYielding ? actor.YieldRemaining + .2 : 1.5 + random.NextDouble() * 3);
            }
            else
            {
                actor.X += dx / distance * travel;
                actor.Y += dy / distance * travel;
            }
            Clamp(actor);
        }
        if (actor.IsYielding || actor.StateTime < actor.Duration) return;
        var choice = random.NextDouble();
        if (choice < .57) Walk(actor);
        else if (choice < .70) Enter(actor, PetState.Idle, 2 + random.NextDouble() * 3);
        else if (choice < .80) Enter(actor, PetState.Wave, 1.8);
        else if (choice < .89) Enter(actor, PetState.Curious, 2.4);
        else if (choice < .95) Enter(actor, PetState.Thinking, 2.4);
        else Enter(actor, PetState.Sleep, 8 + random.NextDouble() * 12);
    }

    private void Walk(PetActor actor)
    {
        var bounds = DisplayFor(actor).WorkArea;
        actor.TargetX = bounds.X + random.NextDouble() * AvailableX(actor);
        actor.TargetY = bounds.Y + (Settings.RoamEverywhere ? random.NextDouble() * AvailableY(actor) : AvailableY(actor));
        Enter(actor, actor.TargetX < actor.X ? PetState.WalkLeft : PetState.WalkRight, 25);
    }

    private static void Enter(PetActor actor, PetState state, double duration)
    { actor.State = state; actor.StateTime = 0; actor.Duration = duration; }
    private static void CancelYield(PetActor actor)
    {
        if (!actor.IsYielding) return;
        actor.YieldRemaining = 0; actor.PetCooldown = 0;
        Enter(actor, PetState.Idle, 1.5);
    }
    private PetActor? Find(int id) => pets.Find(p => p.Id == id);
    private double AvailableX(PetActor actor) => Math.Max(0, DisplayFor(actor).WorkArea.Width - WidthFor(actor));
    private double AvailableY(PetActor actor) => Math.Max(0, DisplayFor(actor).WorkArea.Height - HeightFor(actor));
    private void Clamp(PetActor actor)
    {
        var display = DisplayFor(actor);
        actor.DisplayId = display.Id;
        var bounds = display.WorkArea;
        actor.X = Math.Clamp(actor.X, bounds.X, bounds.X + AvailableX(actor));
        actor.Y = Math.Clamp(actor.Y, bounds.Y, bounds.Y + AvailableY(actor));
        actor.TargetX = Math.Clamp(actor.TargetX, bounds.X, bounds.X + AvailableX(actor));
        actor.TargetY = Math.Clamp(actor.TargetY, bounds.Y, bounds.Y + AvailableY(actor));
    }
    private void ClampAll() { foreach (var actor in pets) Clamp(actor); }
    private static IReadOnlyList<DesktopDisplay> NormalizeDisplays(IEnumerable<DesktopDisplay> displays)
    {
        var result = displays.Where(d => !string.IsNullOrWhiteSpace(d.Id)).DistinctBy(d => d.Id)
            .Select(d => d with { WorkArea = Sanitize(d.WorkArea), Scale = double.IsFinite(d.Scale) ? Math.Clamp(d.Scale, .5, 5) : 1 })
            .OrderByDescending(d => d.IsPrimary).ThenBy(d => d.Id, StringComparer.Ordinal).ToList();
        if (result.Count == 0) result.Add(new("primary", new(0, 0, 1920, 1080), 1, true));
        return result.AsReadOnly();
    }
    private static double DistanceSquared(DesktopBounds b, double x, double y)
    {
        var dx = Math.Max(b.X - x, Math.Max(0, x - b.X - b.Width));
        var dy = Math.Max(b.Y - y, Math.Max(0, y - b.Y - b.Height));
        return dx * dx + dy * dy;
    }
    private static DesktopBounds Sanitize(DesktopBounds b) => new(
        double.IsFinite(b.X) ? b.X : 0, double.IsFinite(b.Y) ? b.Y : 0,
        double.IsFinite(b.Width) ? Math.Max(1, b.Width) : 1920,
        double.IsFinite(b.Height) ? Math.Max(1, b.Height) : 1080);
}
