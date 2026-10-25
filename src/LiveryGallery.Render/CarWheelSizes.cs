using ForzaToolkit.LiveryRender.Model;

namespace LiveryGallery.Render;

/// <summary>
/// The stock wheels of the car list (<see cref="CarWheels"/>, from the game database) as the renderer's sizes. The renderer
/// otherwise places the wheels from the car's art files, which put them several centimetres off on some cars (the
/// Corvette Stingray '20 stood 10 cm high with small rims); it falls back to them for a car the list has no wheels for.
/// </summary>
internal static class CarWheelSizes
{
    public static Func<int, string, WheelSizing?> From(Func<int, CarWheels?> wheels) =>
        (carId, _) => wheels(carId) is { } w ? ToSizing(w) : null;

    public static WheelSizing ToSizing(CarWheels wheels) => new(Axle(wheels.Front), Axle(wheels.Rear));

    private static AxleSizing Axle(CarAxleWheels a) => new(
        (float)a.TireWidthMillimetres, (float)a.TireAspectPercent, (float)a.RimDiameterInches,
        (float)a.TrackOuterMetres, (float)a.RideHeightMetres);
}
