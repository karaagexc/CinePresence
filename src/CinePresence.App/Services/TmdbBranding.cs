using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace CinePresence.App.Services;

internal static class TmdbBranding
{
    // Render the unmodified, approved SVG's paths using WPF's equivalent vector primitives.
    public static ImageSource? Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "tmdb-logo.svg");
        if (!File.Exists(path)) return null;
        var svg = XDocument.Load(path);
        XNamespace ns = "http://www.w3.org/2000/svg";
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 17.76), EndPoint = new Point(273.42, 17.76), MappingMode = BrushMappingMode.Absolute };
        brush.GradientStops.Add(new((Color)ColorConverter.ConvertFromString("#90cea1"), 0));
        brush.GradientStops.Add(new((Color)ColorConverter.ConvertFromString("#3cbec9"), .56));
        brush.GradientStops.Add(new((Color)ColorConverter.ConvertFromString("#00b3e5"), 1));
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 273.42, 35.52))));
        foreach (var element in svg.Descendants(ns + "path"))
            group.Children.Add(new GeometryDrawing(brush, null, Geometry.Parse("F1 " + element.Attribute("d")!.Value)));
        var image = new DrawingImage(group); image.Freeze(); return image;
    }
}
