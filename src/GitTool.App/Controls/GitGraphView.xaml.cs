using GitTool.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace GitTool.App.Controls;

public sealed partial class GitGraphView : UserControl
{
    private const double LaneStart = 14;
    private const double LaneSpacing = 20;
    private const double RowCenter = 32;
    private const double NodeSize = 12;
    private const double HeadRingSize = 19;

    private static readonly Color[] LaneColors =
    [
        Color.FromArgb(255, 0, 120, 212),
        Color.FromArgb(255, 191, 90, 242),
        Color.FromArgb(255, 0, 153, 135),
        Color.FromArgb(255, 255, 140, 0),
        Color.FromArgb(255, 232, 17, 35),
        Color.FromArgb(255, 0, 183, 195)
    ];

    public static readonly DependencyProperty RowProperty = DependencyProperty.Register(
        nameof(Row),
        typeof(GitHistoryGraphRow),
        typeof(GitGraphView),
        new PropertyMetadata(null, OnRowChanged));

    public GitGraphView()
    {
        InitializeComponent();
        Loaded += (_, _) => Render();
    }

    public GitHistoryGraphRow? Row
    {
        get => (GitHistoryGraphRow?)GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    private static void OnRowChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        ((GitGraphView)dependencyObject).Render();
    }

    private void Render()
    {
        GraphCanvas.Children.Clear();
        if (Row is null)
        {
            return;
        }

        var bottom = Height > 0 ? Height : 64;

        foreach (var lane in Row.ContinuingLanes)
        {
            AddLine(lane, 0, lane, bottom, lane);
        }

        if (Row.HasIncomingEdge)
        {
            AddLine(Row.NodeLane, 0, Row.NodeLane, RowCenter, Row.NodeLane);
        }

        foreach (var parentLane in Row.ParentLanes)
        {
            if (parentLane == Row.NodeLane)
            {
                AddLine(Row.NodeLane, RowCenter, parentLane, bottom, parentLane);
            }
            else
            {
                AddCurvedEdge(Row.NodeLane, parentLane, bottom);
            }
        }

        if (Row.Commit.IsHead)
        {
            AddNodeRing(Row.NodeLane);
        }

        AddNode(Row.NodeLane);
    }

    private void AddLine(
        int startLane,
        double startY,
        int endLane,
        double endY,
        int colorLane)
    {
        GraphCanvas.Children.Add(new Line
        {
            X1 = GetLaneX(startLane),
            Y1 = startY,
            X2 = GetLaneX(endLane),
            Y2 = endY,
            Stroke = GetLaneBrush(colorLane),
            StrokeThickness = 2
        });
    }

    private void AddCurvedEdge(int nodeLane, int parentLane, double bottom)
    {
        var start = new Point(GetLaneX(nodeLane), RowCenter);
        var end = new Point(GetLaneX(parentLane), bottom);
        var path = new Microsoft.UI.Xaml.Shapes.Path
        {
            Stroke = GetLaneBrush(parentLane),
            StrokeThickness = 2,
            Data = new PathGeometry
            {
                Figures =
                {
                    new PathFigure
                    {
                        StartPoint = start,
                        Segments =
                        {
                            new BezierSegment
                            {
                                Point1 = new Point(start.X, RowCenter + 12),
                                Point2 = new Point(end.X, bottom - 12),
                                Point3 = end
                            }
                        }
                    }
                }
            }
        };
        GraphCanvas.Children.Add(path);
    }

    private void AddNodeRing(int lane)
    {
        var ring = new Ellipse
        {
            Width = HeadRingSize,
            Height = HeadRingSize,
            Fill = GetCardBackgroundBrush(),
            Stroke = GetAccentBrush(),
            StrokeThickness = 2
        };
        Canvas.SetLeft(ring, GetLaneX(lane) - (HeadRingSize / 2));
        Canvas.SetTop(ring, RowCenter - (HeadRingSize / 2));
        GraphCanvas.Children.Add(ring);
    }

    private void AddNode(int lane)
    {
        var node = new Ellipse
        {
            Width = NodeSize,
            Height = NodeSize,
            Fill = GetLaneBrush(lane),
            Stroke = GetCardBackgroundBrush(),
            StrokeThickness = 2
        };
        Canvas.SetLeft(node, GetLaneX(lane) - (NodeSize / 2));
        Canvas.SetTop(node, RowCenter - (NodeSize / 2));
        GraphCanvas.Children.Add(node);
    }

    private static double GetLaneX(int lane) => LaneStart + (lane * LaneSpacing);

    private static SolidColorBrush GetLaneBrush(int lane) =>
        new(LaneColors[Math.Abs(lane) % LaneColors.Length]);

    private static Brush GetAccentBrush() =>
        GetApplicationBrush(
            "AccentFillColorDefaultBrush",
            new SolidColorBrush(LaneColors[0]));

    private static Brush GetCardBackgroundBrush() =>
        GetApplicationBrush(
            "CardBackgroundFillColorDefaultBrush",
            new SolidColorBrush(Color.FromArgb(255, 32, 32, 32)));

    private static Brush GetApplicationBrush(string key, Brush fallback)
    {
        try
        {
            return Application.Current.Resources[key] as Brush ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
