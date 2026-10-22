namespace Nmoonfox.Core;

public readonly record struct DesktopBounds(double X, double Y, double Width, double Height);

public sealed class PetActor
{
    public int Id { get; internal set; }
    public double X { get; internal set; }
    public double Y { get; internal set; }
    public PetState State { get; internal set; }
    public double StateTime { get; internal set; }
    public double Affection { get; internal set; }
    public int LookDirection { get; internal set; }
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
    public DesktopBounds Bounds { get; private set; }
    public IReadOnlyList<PetActor> Pets => view;
    public bool Paused { get; set; }
    public double PetHeight => Settings.Size * 208 / 192 + 24;

    public PetWorld(DesktopBounds bounds, PetSettings settings, int seed = 1)
    {
        random = new(seed);
        view = pets.AsReadOnly();
        Settings = settings.Normalize();
        Bounds = Sanitize(bounds);
        ApplySettings(Settings);
    }

    public void ApplySettings(PetSettings settings)
    {
        Settings = settings.Normalize();
        while (pets.Count > Settings.Count) pets.RemoveAt(pets.Count - 1);
        while (pets.Count < Settings.Count)
        {
            var actor = new PetActor { Id = ++nextId, X = Bounds.X + random.NextDouble() * AvailableX,
                Y = Bounds.Y + AvailableY };
            Enter(actor, PetState.Wave, 1.2 + random.NextDouble());
            pets.Add(actor);
        }
        ClampAll();
    }

    public void ResizeBounds(DesktopBounds bounds) { Bounds = Sanitize(bounds); ClampAll(); }

    // Fixed steps keep random choices and movement independent of renderer refresh rate.
    // Do not fast-forward after suspension or a blocked UI thread.
    public void Tick(double seconds)
    {
        if (Paused || !double.IsFinite(seconds) || seconds <= 0) return;
        accumulator += Math.Min(seconds, .25);
        while (accumulator + 1e-10 >= Step)
        {
            accumulator -= Step;
            foreach (var actor in pets) Advance(actor, Step);
        }
    }

    public void Pet(int id)
    {
        var actor = Find(id);
        if (actor == null || actor.State == PetState.Dragged || actor.State == PetState.Petting || actor.PetCooldown > 0) return;
        actor.SleepLocked = false;
        actor.PetCooldown = 2.8;
        actor.Affection = 2;
        Enter(actor, PetState.Petting, 1.6);
    }

    public void BeginDrag(int id)
    {
        var actor = Find(id);
        if (actor == null) return;
        actor.SleepLocked = false;
        Enter(actor, PetState.Dragged, double.PositiveInfinity);
    }

    public void DragTo(int id, double x, double y)
    {
        var actor = Find(id);
        if (actor == null || actor.State != PetState.Dragged || !double.IsFinite(x) || !double.IsFinite(y)) return;
        actor.X = x; actor.Y = y; Clamp(actor);
    }

    public void EndDrag(int id)
    {
        var actor = Find(id);
        if (actor == null || actor.State != PetState.Dragged) return;
        Enter(actor, PetState.Idle, 2);
        actor.PetCooldown = 0;
        Pet(id);
    }

    public void SleepAll()
    {
        foreach (var actor in pets)
        {
            actor.SleepLocked = true;
            actor.Affection = 0;
            Enter(actor, PetState.Sleep, double.PositiveInfinity);
        }
    }

    public void WakeAll()
    {
        foreach (var actor in pets) { actor.SleepLocked = false; Enter(actor, PetState.Wave, 1.4); }
    }

    public void Gather()
    {
        for (var i = 0; i < pets.Count; i++)
        {
            var actor = pets[i];
            actor.X = Bounds.X + AvailableX * (i + 1) / (pets.Count + 1);
            actor.Y = Bounds.Y + AvailableY;
            actor.SleepLocked = false;
            Enter(actor, PetState.Wave, 1.2 + random.NextDouble());
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
            var angle = Math.Atan2(actor.Y + PetHeight * .45 - y, x - actor.X - Settings.Size / 2);
            actor.LookDirection = ((int)Math.Round(angle / (2 * Math.PI) * 16) + 16) % 16;
        }
    }

    private void Advance(PetActor actor, double delta)
    {
        if (actor.State == PetState.Dragged) return;
        actor.StateTime += delta;
        actor.PetCooldown = Math.Max(0, actor.PetCooldown - delta);
        actor.Affection = Math.Max(0, actor.Affection - delta);
        if (actor.State is PetState.WalkLeft or PetState.WalkRight)
        {
            var dx = actor.TargetX - actor.X;
            var dy = actor.TargetY - actor.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            var travel = Settings.Speed * delta;
            if (distance <= travel)
            {
                actor.X = actor.TargetX; actor.Y = actor.TargetY;
                Enter(actor, PetState.Idle, 1.5 + random.NextDouble() * 3);
            }
            else
            {
                actor.X += dx / distance * travel;
                actor.Y += dy / distance * travel;
            }
            Clamp(actor);
        }
        if (actor.StateTime < actor.Duration) return;
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
        actor.TargetX = Bounds.X + random.NextDouble() * AvailableX;
        actor.TargetY = Bounds.Y + (Settings.RoamEverywhere ? random.NextDouble() * AvailableY : AvailableY);
        Enter(actor, actor.TargetX < actor.X ? PetState.WalkLeft : PetState.WalkRight, 25);
    }

    private static void Enter(PetActor actor, PetState state, double duration)
    { actor.State = state; actor.StateTime = 0; actor.Duration = duration; }
    private PetActor? Find(int id) => pets.Find(p => p.Id == id);
    private double AvailableX => Math.Max(0, Bounds.Width - Settings.Size);
    private double AvailableY => Math.Max(0, Bounds.Height - PetHeight);
    private void Clamp(PetActor actor)
    {
        actor.X = Math.Clamp(actor.X, Bounds.X, Bounds.X + AvailableX);
        actor.Y = Math.Clamp(actor.Y, Bounds.Y, Bounds.Y + AvailableY);
        actor.TargetX = Math.Clamp(actor.TargetX, Bounds.X, Bounds.X + AvailableX);
        actor.TargetY = Math.Clamp(actor.TargetY, Bounds.Y, Bounds.Y + AvailableY);
    }
    private void ClampAll() { foreach (var actor in pets) Clamp(actor); }
    private static DesktopBounds Sanitize(DesktopBounds b) => new(
        double.IsFinite(b.X) ? b.X : 0, double.IsFinite(b.Y) ? b.Y : 0,
        double.IsFinite(b.Width) ? Math.Max(1, b.Width) : 1920,
        double.IsFinite(b.Height) ? Math.Max(1, b.Height) : 1080);
}
