using System;
using System.Linq;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;

namespace SuiteApp.Controls;

/// <summary>
/// The MDI workspace's background logo (Assets/workspace-logo.svg), embossed into whatever is under it: centred over half the
/// workspace, its edges light on the top left and dark on the bottom right, so it shows the same on the light skin's grey and the
/// dark theme.
/// </summary>
public static class WorkspaceLogo
{
    public static IBrush Brush { get; } = Create();

    private static IBrush Create()
    {
        // ponytail: the first <path>'s d only (no transforms, no other shapes); a real SVG renderer if the logo ever needs more
        using var svg = AssetLoader.Open(new Uri("avares://SuiteApp/Assets/workspace-logo.svg"));
        XNamespace ns = "http://www.w3.org/2000/svg";
        Geometry face = StreamGeometry.Parse(XDocument.Load(svg).Descendants(ns + "path").First().Attribute("d")!.Value);
        // the face moved by an edge's width, less the face: a crescent along one side of every outline
        Geometry Shifted(double offset)
        {
            Geometry g = face.Clone();
            g.Transform = new TranslateTransform(offset, offset);
            return g;
        }
        var drawing = new DrawingGroup
        {
            Children =
            {
                new GeometryDrawing { Geometry = new CombinedGeometry(GeometryCombineMode.Exclude, Shifted(-1.2), face), Brush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)) },
                new GeometryDrawing { Geometry = new CombinedGeometry(GeometryCombineMode.Exclude, Shifted(1.2), face), Brush = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)) },
                new GeometryDrawing { Geometry = face, Brush = new SolidColorBrush(Color.FromArgb(14, 0, 0, 0)) },
            },
        };
        return new DrawingBrush(drawing)
        {
            Stretch = Stretch.Uniform,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            DestinationRect = new RelativeRect(0.25, 0.25, 0.5, 0.5, RelativeUnit.Relative),
            TileMode = TileMode.None,
        };
    }
}
