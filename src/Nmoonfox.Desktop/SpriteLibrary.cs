using System;
using System.Windows;
using System.Windows.Media.Imaging;
using Nmoonfox.Core;

namespace Nmoonfox.Desktop;

public sealed class SpriteLibrary
{
    private readonly BitmapSource[,] frames = new BitmapSource[11, 8];
    public BitmapSource Sleeping { get; }
    public SpriteLibrary()
    {
        var atlas = Load("nmoonfox-v2.png");
        if (atlas.PixelWidth != 1536 || atlas.PixelHeight != 2288) throw new InvalidOperationException("スプライトの寸法が異なります。");
        for (var row = 0; row < 11; row++)
        for (var col = 0; col < 8; col++)
        {
            var frame = new CroppedBitmap(atlas, new Int32Rect(col * 192, row * 208, 192, 208));
            frame.Freeze(); frames[row, col] = frame;
        }
        Sleeping = Load("nmoonfox-sleep.png");
    }
    public BitmapSource Frame(PetState state, int frame) => frames[AnimationCatalog.Get(state).Row, frame];
    public BitmapSource Look(int mathematicalDirection)
    {
        var atlasDirection = (4 - mathematicalDirection + 16) % 16;
        return frames[9 + atlasDirection / 8, atlasDirection % 8];
    }
    private static BitmapSource Load(string name)
    {
        var image = new BitmapImage(new Uri("pack://application:,,,/Assets/" + name));
        image.Freeze(); return image;
    }
}
