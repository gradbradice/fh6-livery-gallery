using Avalonia;
using Avalonia.Rendering;
using ForzaToolkit.LiveryRender.Avalonia;

namespace LiveryGallery.Views;

public sealed class InteractiveLiveryViewer : LiveryViewer, ICustomHitTest
{
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);
}
