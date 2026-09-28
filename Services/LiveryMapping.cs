using ForzaToolkit.Formats;
using ForzaToolkit.LiveryRender.Livery;

namespace LiveryGallery.Services;

internal static class LiveryMapping
{
    public static ParseResult<LiveryDrawing> ToDrawing(uint targetCarId, ReadOnlySpan<byte> cLivery)
    {
        var shapes = NativeHeaderParser.TryExtractLiveryShapes(cLivery);
        if (!shapes.HasValue) return ParseResult<LiveryDrawing>.Fail(shapes.Error!);

        var paints = new Dictionary<ulong, PaintMaterial>();
        var parsed = NativeHeaderParser.TryParseCLivery(cLivery);
        if (parsed.HasValue)
            foreach (var p in parsed.Value!.PaintRecords)
                paints[p.MaterialHash] = new PaintMaterial(
                    p.PrimaryColorEnabled ? Rgba.FromBgra(p.PrimaryB, p.PrimaryG, p.PrimaryR, 255) : null,
                    p.SecondaryColorEnabled ? Rgba.FromBgra(p.SecondaryB, p.SecondaryG, p.SecondaryR, 255) : null,
                    (int)p.FinishCode);

        var list = new List<DrawShape>(shapes.Value!.Count);
        foreach (var s in shapes.Value)
            list.Add(new DrawShape(s.SectionIndex, s.ShapeId,
                s.WorldA, s.WorldB, s.WorldC, s.WorldD, s.WorldTx, s.WorldTy,
                Rgba.FromBgra(s.ColorB, s.ColorG, s.ColorR, s.ColorA),
                s.IsMask, s.IsRasterLogo));

        var drawing = new LiveryDrawing((int)targetCarId, list, paints);
        return shapes.Status == ParseStatus.Partial
            ? ParseResult<LiveryDrawing>.Partial(drawing, shapes.Error!)
            : ParseResult<LiveryDrawing>.Ok(drawing);
    }
}
