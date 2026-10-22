namespace Nmoonfox.Core;

public enum PetState { Idle, WalkLeft, WalkRight, Wave, Petting, Curious, Thinking, Review, Sleep, Dragged }
public readonly record struct AnimationClip(int Row, int Frames, double Fps);
public static class AnimationCatalog
{
    public const int CellWidth = 192;
    public const int CellHeight = 208;
    public static AnimationClip Get(PetState state) => state switch
    {
        PetState.WalkRight => new(1, 8, 9),
        PetState.WalkLeft => new(2, 8, 9),
        PetState.Wave => new(3, 4, 5),
        PetState.Petting => new(4, 5, 4),
        PetState.Curious => new(6, 6, 5),
        PetState.Thinking => new(7, 6, 5),
        PetState.Review => new(8, 6, 5),
        _ => new(0, 6, 5)
    };
}
