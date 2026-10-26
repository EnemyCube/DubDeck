using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Xml.Linq;
namespace DubDeck.App;

/// <summary>Small, cached vector icons from the bundled Lucide SVG sources.</summary>
public static class AppIcons
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached("Kind", typeof(string), typeof(AppIcons),
        new PropertyMetadata(null, (sender,args) =>
        {
            if(sender is ButtonBase button && args.NewValue is string kind)
            {
                Render(button,kind);
                if(!button.IsLoaded) { button.Loaded -= FinishStyle; button.Loaded += FinishStyle; }
            }
        }));
    private static void FinishStyle(object sender,RoutedEventArgs args)
    {
        var button=(ButtonBase)sender; button.Loaded-=FinishStyle;
        if(GetKind(button) is string kind) Render(button,kind);
    }
    public static string? GetKind(DependencyObject target) => (string?)target.GetValue(KindProperty);
    public static void SetKind(DependencyObject target,string value) => target.SetValue(KindProperty,value);
    private static readonly Dictionary<string,DrawingImage> Cache = [];
    private static readonly DependencyProperty RegisteredProperty = DependencyProperty.RegisterAttached(
        "Registered", typeof(bool), typeof(AppIcons), new PropertyMetadata(false));
    private static readonly List<WeakReference<ButtonBase>> Buttons = [];

    public static void RefreshTheme()
    {
        for (int index = Buttons.Count - 1; index >= 0; index--)
        {
            if (!Buttons[index].TryGetTarget(out var button)) { Buttons.RemoveAt(index); continue; }
            if (GetKind(button) is string kind) Render(button, kind);
        }
    }

    private static void Register(ButtonBase button)
    {
        if ((bool)button.GetValue(RegisteredProperty)) return;
        button.SetValue(RegisteredProperty, true);
        if (Buttons.Count % 128 == 0) Buttons.RemoveAll(reference => !reference.TryGetTarget(out _));
        Buttons.Add(new WeakReference<ButtonBase>(button));
    }
    public static void SetButton(Button button,string kind,string label)
    {
        if(GetKind(button)!=kind) SetKind(button,kind); else Render(button,kind);
        button.ToolTip=label; AutomationProperties.SetName(button,label);
    }
    public static void SetToggle(CheckBox toggle,string kind,string label)
    {
        if(GetKind(toggle)!=kind) SetKind(toggle,kind); else Render(toggle,kind);
        toggle.ToolTip=label; AutomationProperties.SetName(toggle,label);
    }
    private static void Render(ButtonBase button,string kind)
    {
        Register(button);
        var color=(button.Foreground as SolidColorBrush)?.Color ?? Color.FromRgb(235,239,247);
        string key=kind+color;
        if(!Cache.TryGetValue(key,out var source))
        {
            using var stream=Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Icons/"+kind+".svg"))!.Stream;
            var svg=XElement.Load(stream);
            var group=new DrawingGroup();
            group.Children.Add(new GeometryDrawing(Brushes.Transparent,null,new RectangleGeometry(new Rect(0,0,24,24))));
            var pen=new Pen(new SolidColorBrush(color),2){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
            foreach(var node in svg.Elements())
            {
                double N(string name,double fallback=0) => double.TryParse((string?)node.Attribute(name),NumberStyles.Float,CultureInfo.InvariantCulture,out var value)?value:fallback;
                Geometry geometry=node.Name.LocalName switch
                {
                    "path"=>Geometry.Parse((string)node.Attribute("d")!),
                    "rect"=>new RectangleGeometry(new Rect(N("x"),N("y"),N("width"),N("height")),N("rx"),N("ry",N("rx"))),
                    "circle"=>new EllipseGeometry(new Point(N("cx"),N("cy")),N("r"),N("r")),
                    "line"=>new LineGeometry(new Point(N("x1"),N("y1")),new Point(N("x2"),N("y2"))),
                    "polyline"=>Geometry.Parse("M"+(string)node.Attribute("points")!),
                    "polygon"=>Geometry.Parse("M"+(string)node.Attribute("points")!+"Z"),
                    _=>throw new InvalidOperationException("Unsupported icon geometry: "+node.Name)
                };
                group.Children.Add(new GeometryDrawing(null,pen,geometry));
            }
            source=new DrawingImage(group); source.Freeze(); Cache[key]=source;
        }
        if(button.Content is Image existing && ReferenceEquals(existing.Source,source)) return;
        button.Content=new Image{Source=source,Width=18,Height=18};
    }
}
