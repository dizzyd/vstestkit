using SkiaSharp;
using VsTestkit.Testing;

var dir = Directory.CreateTempSubdirectory("vstk-images-").FullName;
try
{
    var black = Path.Combine(dir, "black.png");
    var white = Path.Combine(dir, "white.png");
    Save(black, SKColors.Black);
    Save(white, SKColors.White);
    Assert.Equal(1d, Visual.Compare(black, white, 12).Fraction);
    foreach (var region in new[] {
        default(VisualRegion), new VisualRegion(0, 0, -1, 4),
        new VisualRegion(4, 0, 1, 4), new VisualRegion(-5, 0, 1, 4),
        new VisualRegion(int.MaxValue, 0, int.MaxValue, 4)
    })
        Assert.Throws<AssertionException>(() => Visual.Compare(black, white, 12, region));

    var partial = Visual.Compare(black, white, 12, new VisualRegion(-2, 0, 3, 4));
    Assert.Equal(4L, partial.Total);
    Assert.Equal(1d, partial.Fraction);
    var wide = Visual.Compare(black, white, 12, new VisualRegion(2, 0, int.MaxValue, 4));
    Assert.Equal(8L, wide.Total);
    Assert.Equal(1d, wide.Fraction);
    Console.WriteLine("PASS: empty/off-image regions fail; partial regions compare their exact intersection");
}
finally { Directory.Delete(dir, true); }

static void Save(string path, SKColor color)
{
    using var bitmap = new SKBitmap(4, 4);
    bitmap.Erase(color);
    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var file = File.Create(path);
    data.SaveTo(file);
}
