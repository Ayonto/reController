using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ReController.App.Infrastructure;
using ReController.App.ViewModels;
using ReController.Core.Input;
using Control = ReController.Core.Input.Control;

namespace ReController.App.Views;

/// <summary>
/// Vector drawing of a generic gamepad with a clickable hotspot per control and a leader line to a
/// label showing its current mapping. Everything lives on an 800×490 canvas inside a Viewbox.
/// </summary>
public partial class GamepadDiagram : UserControl
{
    private const double LeftLabelRight = 196;
    private const double RightLabelLeft = 604;
    private const double SideLabelWidth = 180;

    private const string BodyPath =
        "M 300,150 C 335,141 365,147 400,147 C 435,147 465,141 500,150 C 548,158 575,172 590,205 " +
        "C 610,252 628,318 632,362 C 636,404 610,424 586,419 C 561,414 546,390 531,364 C 516,340 497,328 470,328 " +
        "L 330,328 C 303,328 284,340 269,364 C 254,390 239,414 214,419 C 190,424 164,404 168,362 " +
        "C 172,318 190,252 210,205 C 225,172 252,158 300,150 Z";

    private const string LeftTriggerPath = "M 236,134 C 240,112 258,98 282,96 L 300,96 Q 310,96 310,106 L 310,128 C 284,128 262,134 248,142 Q 234,146 236,134 Z";
    private const string RightTriggerPath = "M 564,134 C 560,112 542,98 518,96 L 500,96 Q 490,96 490,106 L 490,128 C 516,128 538,134 552,142 Q 566,146 564,134 Z";
    private const string LeftBumperPath = "M 218,176 C 234,150 262,136 304,130 L 316,129 Q 324,129 323,136 L 322,140 C 280,145 250,158 232,182 Q 224,190 218,176 Z";
    private const string RightBumperPath = "M 582,176 C 566,150 538,136 496,130 L 484,129 Q 476,129 477,136 L 478,140 C 520,145 550,158 568,182 Q 576,190 582,176 Z";

    private static readonly BoolToVisibilityConverter BoolToVisibility = new();

    public GamepadDiagram()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Build();
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void Build()
    {
        Surface.Children.Clear();
        if (Vm is not { } vm) return;

        // Back to front: triggers, body, bumpers, sticks, d-pad, buttons.
        Part(vm[Control.LeftTrigger], Geometry.Parse(LeftTriggerPath), Control.LeftTrigger);
        Part(vm[Control.RightTrigger], Geometry.Parse(RightTriggerPath), Control.RightTrigger);

        Surface.Children.Add(new Path
        {
            Data = Geometry.Parse(BodyPath),
            Fill = (Brush)FindResource("PadBodyBrush"),
            Stroke = (Brush)FindResource("PadStrokeBrush"),
            StrokeThickness = 2,
            IsHitTestVisible = false,
        });

        Part(vm[Control.LeftBumper], Geometry.Parse(LeftBumperPath), Control.LeftBumper);
        Part(vm[Control.RightBumper], Geometry.Parse(RightBumperPath), Control.RightBumper);

        // Sticks: a recessed well plus a clickable cap bound to the stick's group state.
        Well(290, 222, 33);
        Part(vm.LeftStickGroup, Circle(290, 222, 22), Control.LeftStickClick, "Left stick");
        Well(452, 286, 33);
        Part(vm.RightStickGroup, Circle(452, 286, 22), Control.RightStickClick, "Right stick");

        // D-pad
        Surface.Children.Add(new Rectangle
        {
            Width = 24, Height = 24, RadiusX = 2, RadiusY = 2,
            Fill = (Brush)FindResource("PadPartBrush"), IsHitTestVisible = false,
        }.At(336, 274));
        Part(vm[Control.DPadUp], Rect(337, 251, 22, 26), Control.DPadUp);
        Part(vm[Control.DPadDown], Rect(337, 295, 22, 26), Control.DPadDown);
        Part(vm[Control.DPadLeft], Rect(313, 275, 26, 22), Control.DPadLeft);
        Part(vm[Control.DPadRight], Rect(357, 275, 26, 22), Control.DPadRight);

        // Face buttons
        FaceButton(vm[Control.Y], 510, 192, "Y");
        FaceButton(vm[Control.X], 482, 220, "X");
        FaceButton(vm[Control.B], 538, 220, "B");
        FaceButton(vm[Control.A], 510, 248, "A");

        // Centre buttons
        Part(vm[Control.Back], Circle(365, 220, 9), Control.Back);
        Part(vm[Control.Start], Circle(435, 220, 9), Control.Start);
        Part(vm[Control.Guide], Circle(400, 186, 16), Control.Guide);
        Glyph(vm[Control.Guide], 400, 186, "", icon: true);

        // Leaders + labels (left side)
        Leader(vm[Control.LeftTrigger], (248, 112), (228, 84), (200, 84));
        SideLabel(vm[Control.LeftTrigger], 84, left: true, Control.LeftTrigger);
        Leader(vm[Control.LeftBumper], (226, 172), (210, 134), (200, 134));
        SideLabel(vm[Control.LeftBumper], 134, left: true, Control.LeftBumper);
        Leader(vm.LeftStickGroup, (258, 222), (200, 222));
        SideLabel(vm.LeftStickGroup, 222, left: true, Control.LeftStickClick, "Text");
        Leader(vm.DPadGroup, (313, 286), (280, 330), (200, 330));
        SideLabel(vm.DPadGroup, 330, left: true, Control.DPadUp, "Text");

        // Top
        Leader(vm[Control.Back], (362, 211), (330, 82));
        TopLabel(vm[Control.Back], 330, 66, Control.Back);
        Leader(vm[Control.Guide], (400, 170), (400, 82));
        TopLabel(vm[Control.Guide], 400, 66, Control.Guide);
        Leader(vm[Control.Start], (438, 211), (470, 82));
        TopLabel(vm[Control.Start], 470, 66, Control.Start);

        // Right side
        Leader(vm[Control.RightTrigger], (552, 112), (572, 84), (600, 84));
        SideLabel(vm[Control.RightTrigger], 84, left: false, Control.RightTrigger);
        Leader(vm[Control.RightBumper], (574, 172), (590, 134), (600, 134));
        SideLabel(vm[Control.RightBumper], 134, left: false, Control.RightBumper);
        Leader(vm[Control.Y], (523, 192), (560, 174), (600, 174));
        SideLabel(vm[Control.Y], 174, left: false, Control.Y);
        Leader(vm[Control.B], (551, 220), (570, 212), (600, 212));
        SideLabel(vm[Control.B], 212, left: false, Control.B);
        Leader(vm[Control.A], (523, 250), (600, 250));
        SideLabel(vm[Control.A], 250, left: false, Control.A);
        Leader(vm[Control.X], (482, 233), (496, 288), (600, 288));
        SideLabel(vm[Control.X], 288, left: false, Control.X);
        Leader(vm.RightStickGroup, (468, 302), (500, 344), (600, 344));
        SideLabel(vm.RightStickGroup, 344, left: false, Control.RightStickClick, "Text");

        ExtendedRow(vm);
    }

    // ------------------------------------------------------------------ builders

    private void Part(object dataContext, Geometry geometry, Control target, string? tooltip = null)
    {
        var p = new Path { Data = geometry, Style = (Style)FindResource("Hotspot"), DataContext = dataContext };
        p.ToolTip = tooltip ?? (dataContext as ControlBindingViewModel)?.LongName;
        Click(p, target);
        Surface.Children.Add(p);
    }

    private void Well(double cx, double cy, double r) =>
        Surface.Children.Add(new Ellipse
        {
            Width = r * 2, Height = r * 2,
            Fill = (Brush)FindResource("Bg0Brush"),
            Stroke = (Brush)FindResource("PadStrokeBrush"),
            StrokeThickness = 1.2,
            IsHitTestVisible = false,
        }.At(cx - r, cy - r));

    private void FaceButton(ControlBindingViewModel vm, double cx, double cy, string letter)
    {
        Part(vm, Circle(cx, cy, 13), vm.Control);
        Glyph(vm, cx, cy, letter, icon: false);
    }

    private void Glyph(object dc, double cx, double cy, string text, bool icon)
    {
        var t = new TextBlock { Text = text, Style = (Style)FindResource("PadGlyph"), DataContext = dc, Width = 30 };
        if (icon) { t.FontFamily = (FontFamily)FindResource("IconFont"); t.FontWeight = FontWeights.Normal; t.FontSize = 14; }
        t.Loaded += (_, _) => Canvas.SetTop(t, cy - (t.ActualHeight / 2));
        Canvas.SetLeft(t, cx - 15);
        Canvas.SetTop(t, cy - 9);
        Surface.Children.Add(t);
    }

    private void Leader(object dc, params (double X, double Y)[] points)
    {
        var line = new Polyline { Style = (Style)FindResource("Leader"), DataContext = dc };
        foreach (var (x, y) in points) line.Points.Add(new Point(x, y));
        Surface.Children.Add(line);
        // Small dot where the leader meets the controller.
        var dot = new Ellipse { Width = 4, Height = 4, Fill = (Brush)FindResource("PadStrokeBrush"), IsHitTestVisible = false };
        Surface.Children.Add(dot.At(points[0].X - 2, points[0].Y - 2));
    }

    private void SideLabel(object dc, double centerY, bool left, Control target, string path = "Label")
    {
        var t = new TextBlock
        {
            Style = (Style)FindResource("PadLabel"),
            DataContext = dc,
            Width = SideLabelWidth,
            TextAlignment = left ? TextAlignment.Right : TextAlignment.Left,
            TextWrapping = TextWrapping.NoWrap,
        };
        t.SetBinding(TextBlock.TextProperty, new Binding(path));
        Canvas.SetLeft(t, left ? LeftLabelRight - SideLabelWidth : RightLabelLeft);
        Canvas.SetTop(t, centerY - 9);
        t.SizeChanged += (_, _) => Canvas.SetTop(t, centerY - (t.ActualHeight / 2));
        Click(t, target);
        Surface.Children.Add(t);
    }

    private void TopLabel(object dc, double centerX, double centerY, Control target)
    {
        var t = new TextBlock
        {
            Style = (Style)FindResource("PadLabel"),
            DataContext = dc,
            Width = 72,
            TextAlignment = TextAlignment.Center,
        };
        t.SetBinding(TextBlock.TextProperty, new Binding("Label"));
        Canvas.SetLeft(t, centerX - 36);
        Canvas.SetTop(t, centerY - 9);
        Click(t, target);
        Surface.Children.Add(t);
    }

    private void ExtendedRow(MainViewModel vm)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(new TextBlock
        {
            Text = "EXTRA",
            Style = (Style)FindResource("Caption"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            ToolTip = "Extra buttons on DualSense, Switch Pro and Elite-style controllers (read via SDL).",
        });
        foreach (var c in new[] { Control.Misc1, Control.Paddle1, Control.Paddle2, Control.Paddle3, Control.Paddle4, Control.Touchpad })
        {
            var cvm = vm[c];
            var name = new TextBlock { Text = cvm.ShortName, FontSize = 12, Foreground = (Brush)FindResource("TextMutedBrush") };
            var mapped = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 0, 0, 0) };
            mapped.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            mapped.SetBinding(TextBlock.TextProperty, new Binding(nameof(ControlBindingViewModel.Label)) { StringFormat = "→ {0}" });
            mapped.SetBinding(VisibilityProperty, new Binding(nameof(ControlBindingViewModel.IsRemapped)) { Converter = BoolToVisibility });
            var chip = new Border
            {
                Style = (Style)FindResource("ExtChip"),
                DataContext = cvm,
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = cvm.LongName,
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { name, mapped } },
            };
            Click(chip, c);
            row.Children.Add(chip);
        }
        var host = new Grid { Width = 800 };
        host.Children.Add(row);
        Canvas.SetTop(host, 448);
        Surface.Children.Add(host);
    }

    private void Click(UIElement e, Control target) =>
        e.MouseLeftButtonDown += (_, args) =>
        {
            Vm?.SelectControl(target);
            args.Handled = true;
        };

    private static Geometry Circle(double cx, double cy, double r) => new EllipseGeometry(new Point(cx, cy), r, r);

    private static Geometry Rect(double x, double y, double w, double h) => new RectangleGeometry(new Rect(x, y, w, h), 3, 3);
}

internal static class CanvasExtensions
{
    public static T At<T>(this T e, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        return e;
    }
}
