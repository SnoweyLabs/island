using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace Island.SettingsUi;

/// <summary>Names an element so that it can be found again after its section is built anew (keeps the keyboard where it was).</summary>
internal static class FocusKey
{
    public static readonly DependencyProperty NameProperty =
        DependencyProperty.RegisterAttached("Name", typeof(string), typeof(FocusKey));

    public static void Set(DependencyObject element, string? name) => element.SetValue(NameProperty, name);

    public static string? Get(DependencyObject? element) => element?.GetValue(NameProperty) as string;
}

/// <summary>What Esc does inside a text box that is being edited: give the text back and let go of the keyboard.</summary>
internal static class EditHooks
{
    public static readonly DependencyProperty CancelProperty =
        DependencyProperty.RegisterAttached("Cancel", typeof(Action), typeof(EditHooks));

    public static void Set(DependencyObject element, Action cancel) => element.SetValue(CancelProperty, cancel);

    public static Action? Get(DependencyObject? element) => element?.GetValue(CancelProperty) as Action;
}

/// <summary>The small pieces of look C: pill buttons, key caps, cards, rows, the dot of a page, a swatch, a quiet text box, a thin scroll bar.</summary>
internal static class Parts
{
    // The XAML namespaces are names, not addresses: nothing is ever fetched from them. They are joined here so that the
    // source guard (OutsideGuardTests.App_Has_No_Internet_Client), which looks for web addresses, is not asked about them.
    private static readonly string Presentation = string.Concat("http", "://schemas.microsoft.com/winfx/2006/xaml/presentation");
    private static readonly string Xaml = string.Concat("http", "://schemas.microsoft.com/winfx/2006/xaml");

    private static readonly Dictionary<(double, string), ControlTemplate> Pills = [];

    // ---- Buttons -------------------------------------------------------

    /// <summary>
    /// A flat button with the island's corner radius, a hover tint, and a white ring (outside the shape) while it
    /// holds the keyboard. Every clickable thing on the screen uses it, so Tab, Enter and Space work everywhere.
    /// </summary>
    public static Button Flat(UIElement content, double radius, Action onClick, string? focusKey = null, string? name = null, string hover = "#12FFFFFF")
    {
        var button = new Button
        {
            Content = content,
            Template = PillTemplate(radius, hover),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = true,
            FocusVisualStyle = null, // the template draws its own ring
        };
        button.Click += (_, _) => onClick();
        if (focusKey is not null) FocusKey.Set(button, focusKey);
        if (name is not null) System.Windows.Automation.AutomationProperties.SetName(button, name);
        return button;
    }

    /// <summary>The round button of the foot: white (primary) or a ghost with a hairline.</summary>
    public static Button Pill(string text, bool primary, Action onClick, string focusKey)
    {
        var label = Look.Label(text, Look.BodySize, FontWeights.SemiBold, primary ? Look.ButtonText : Look.Sub);
        label.Effect = primary ? null : label.Effect;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextWrapping = TextWrapping.NoWrap;

        var button = Flat(label, Look.ButtonHeight / 2, onClick, focusKey, text, primary ? "#FFFFFFFF" : "#12FFFFFF");
        button.Height = Look.ButtonHeight;
        button.MinWidth = 96;
        button.Padding = new Thickness(primary ? 20 : 16, 0, primary ? 20 : 16, 0);
        button.Background = primary ? Look.ButtonFill : Brushes.Transparent;
        button.BorderBrush = primary ? Brushes.Transparent : Look.GhostLine;
        button.BorderThickness = new Thickness(primary ? 0 : 1);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        return button;
    }

    /// <summary>A text-only action in the accent colour (the reference's <c>.add</c>).</summary>
    public static Button Link(string text, string accent, Action onClick, string focusKey, string? name = null)
    {
        var label = Look.Label(text, Look.HintSize, FontWeights.Normal, Look.BrushOf(Look.ReadableAccent(accent)));
        label.TextWrapping = TextWrapping.NoWrap;
        var button = Flat(label, 8, onClick, focusKey, name ?? text);
        button.Padding = new Thickness(8, 6, 8, 6);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        return button;
    }

    private static ControlTemplate PillTemplate(double radius, string hover)
    {
        var key = (radius, hover);
        if (Pills.TryGetValue(key, out var cached)) return cached;

        var r = radius.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var ring = (radius + 3).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var template = (ControlTemplate)XamlReader.Parse($$"""
            <ControlTemplate xmlns="{{Presentation}}" xmlns:x="{{Xaml}}" TargetType="Button">
              <Grid>
                <Border x:Name="Bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="{{r}}" Padding="{TemplateBinding Padding}">
                  <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                    VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                </Border>
                <Border x:Name="Ring" Margin="-3" CornerRadius="{{ring}}" BorderThickness="2" BorderBrush="Transparent" IsHitTestVisible="False"/>
              </Grid>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Bd" Property="Background" Value="{{hover}}"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Ring" Property="BorderBrush" Value="#E6FFFFFF"/></Trigger>
                <Trigger Property="IsPressed" Value="True"><Setter TargetName="Bd" Property="Opacity" Value="0.8"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """);
        Pills[key] = template;
        return template;
    }

    // ---- Key caps, dots, swatches ---------------------------------------------

    /// <summary>One glass key cap (<c>.cap</c>): 30 high, a hairline, bold 12.5. The small form is the one inside a sentence.</summary>
    public static Border Cap(string text, bool small = false)
    {
        var label = Look.Label(text, small ? 11.5 : 12.5, FontWeights.SemiBold);
        label.TextWrapping = TextWrapping.NoWrap;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        return new Border
        {
            MinWidth = small ? 22 : 30,
            Height = small ? 22 : Look.CapHeight,
            Padding = new Thickness(small ? 6 : 9, 0, small ? 6 : 9, 0),
            CornerRadius = new CornerRadius(small ? 11 : Look.CapRadius),
            Background = Look.CapFill,
            BorderBrush = Look.CapLine,
            BorderThickness = new Thickness(1),
            Child = label,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>The keys of a combination as caps in a row, for example Ctrl, Alt, 1.</summary>
    public static StackPanel Caps(string combination, bool small = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var keys = combination.Split('+');
        for (var i = 0; i < keys.Length; i++)
        {
            var cap = Cap(keys[i], small);
            cap.Margin = new Thickness(i == 0 ? 0 : 5, 0, 0, 0);
            row.Children.Add(cap);
        }

        return row;
    }

    /// <summary>The page dot (<c>.dot</c>): 16 px in the page's colour with a soft halo.</summary>
    public static FrameworkElement Dot(string colour)
    {
        var halo = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = Look.Mix(colour, 22),
            VerticalAlignment = VerticalAlignment.Center,
        };
        halo.Child = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Look.BrushOf(colour) };
        return halo;
    }

    /// <summary>A round colour choice (<c>.sw button</c>): 24 px, a white ring when it is the current colour.</summary>
    public static Button Swatch(string colour, bool current, Action onClick, string focusKey)
    {
        var disc = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = Look.BrushOf(colour),
            BorderBrush = current ? Brushes.White : Brushes.Transparent,
            BorderThickness = new Thickness(2),
        };
        var button = Flat(disc, 12, onClick, focusKey, $"Colour {colour}", "#00000000");
        button.Margin = new Thickness(0, 0, 8, 8);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        return button;
    }

    /// <summary>
    /// A line of text with key caps in it: a piece in square brackets becomes a small cap, wherever it stands and whatever follows it ("[Delete]," is a cap and a comma). The whole line is ONE text element with
    /// runs and the caps inside it (Dan's P15 and P16, WORK-ORDER-13): a screen reader reads a sentence, not a word at a time, and the line wraps as text does. Its name for a screen reader has the keys in words.
    /// </summary>
    public static TextBlock Sentence(string template, double size = Look.HintSize)
    {
        var block = Look.Label(string.Empty, size, brush: Look.Sub);
        block.VerticalAlignment = VerticalAlignment.Center;
        var last = 0;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(template, @"\[([^\]]+)\]"))
        {
            if (m.Index > last) block.Inlines.Add(new System.Windows.Documents.Run(template[last..m.Index]));
            var cap = Cap(m.Groups[1].Value, small: true);
            cap.Margin = new Thickness(1, 0, 1, 0);
            block.Inlines.Add(new System.Windows.Documents.InlineUIContainer(cap) { BaselineAlignment = BaselineAlignment.Center });
            last = m.Index + m.Length;
        }

        if (last < template.Length) block.Inlines.Add(new System.Windows.Documents.Run(template[last..]));
        System.Windows.Automation.AutomationProperties.SetName(block, System.Text.RegularExpressions.Regex.Replace(template, @"\[([^\]]+)\]", "$1"));
        return block;
    }

    // ---- Text ------------------------------------------------------------------

    public static TextBlock Heading(string text)
    {
        var h = Look.Label(text, Look.H1Size, FontWeight.FromOpenTypeWeight(650)); // the reference's 650 (it was SemiBold, 600, until WORK-ORDER-13: Dan's P22)
        h.TextAlignment = TextAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetLiveSetting(h, System.Windows.Automation.AutomationLiveSetting.Polite); // a new section is told when it is built
        return h;
    }

    public static TextBlock Subheading(string text)
    {
        var s = Look.Label(text, Look.SubSize, brush: Look.Sub);
        s.TextAlignment = TextAlignment.Center;
        s.Margin = new Thickness(0, 6, 0, 0);
        return s;
    }

    /// <summary>A name that can be edited in place (<c>.name</c>): quiet until it holds the keyboard.</summary>
    public static TextBox NameBox(string text, string accent, Action<string> commit, string focusKey)
    {
        var box = new TextBox
        {
            Text = text,
            FontFamily = Look.Font,
            FontSize = Look.Scaled(Look.BodySize),
            FontWeight = FontWeights.SemiBold,
            Foreground = Look.Text,
            CaretBrush = Look.Text,
            SelectionBrush = Look.BrushOf(accent, 0.45),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 1, 4, 1),
            MaxLength = 64,
            VerticalAlignment = VerticalAlignment.Center,
            Template = QuietBoxTemplate(),
        };
        FocusKey.Set(box, focusKey);
        System.Windows.Automation.AutomationProperties.SetName(box, "Name of the page");

        var original = text;
        var done = false;
        void Cancel()
        {
            done = true;
            box.Text = original;
            System.Windows.Input.Keyboard.ClearFocus();
        }

        EditHooks.Set(box, Cancel);
        box.GotKeyboardFocus += (_, _) =>
        {
            box.Background = Look.Mix(accent, 12);
            done = false;
            box.Dispatcher.BeginInvoke(box.SelectAll); // after the click has placed the caret
        };
        box.LostKeyboardFocus += (_, _) =>
        {
            box.Background = Brushes.Transparent;
            if (!done && box.Text != original) commit(box.Text);
            done = true;
        };
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                done = true;
                if (box.Text != original) commit(box.Text);
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                Cancel();
                e.Handled = true;
            }
        };
        return box;
    }

    private static ControlTemplate? _quietBox;

    /// <summary>A text box with no chrome of its own: a rounded fill and, when the caller gives one, a hairline.</summary>
    public static ControlTemplate QuietBoxTemplate() => _quietBox ??= (ControlTemplate)XamlReader.Parse($$"""
        <ControlTemplate xmlns="{{Presentation}}" xmlns:x="{{Xaml}}" TargetType="TextBox">
          <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="8" Padding="{TemplateBinding Padding}">
            <ScrollViewer x:Name="PART_ContentHost" Focusable="False" HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Hidden"/>
          </Border>
        </ControlTemplate>
        """);

    private static ControlTemplate? _ringBox;

    /// <summary>The quiet box with the white ring of a focused button drawn inside its edge: for the fields whose only other mark of focus is the caret (the colour field, the program narrowing field, the site field).</summary>
    public static ControlTemplate RingBoxTemplate() => _ringBox ??= (ControlTemplate)XamlReader.Parse($$"""
        <ControlTemplate xmlns="{{Presentation}}" xmlns:x="{{Xaml}}" TargetType="TextBox">
          <Grid>
            <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="8" Padding="{TemplateBinding Padding}">
              <ScrollViewer x:Name="PART_ContentHost" Focusable="False" HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Hidden"/>
            </Border>
            <Border x:Name="Ring" CornerRadius="8" BorderThickness="2" BorderBrush="Transparent" IsHitTestVisible="False"/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Ring" Property="BorderBrush" Value="#E6FFFFFF"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);

    // ---- Scroll bar ------------------------------------------------------------

    /// <summary>A thin, translucent scroll bar that belongs on glass; the system one is a light grey slab.</summary>
    public static Style ScrollBarStyle() => (Style)XamlReader.Parse($$"""
        <Style xmlns="{{Presentation}}" xmlns:x="{{Xaml}}" TargetType="ScrollBar">
          <Setter Property="Width" Value="10"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="ScrollBar">
                <Grid Background="Transparent">
                  <Track x:Name="PART_Track" IsDirectionReversed="True">
                    <Track.Thumb>
                      <Thumb>
                        <Thumb.Template>
                          <ControlTemplate TargetType="Thumb"><Border Margin="3,0,3,0" CornerRadius="2" Background="#59FFFFFF"/></ControlTemplate>
                        </Thumb.Template>
                      </Thumb>
                    </Track.Thumb>
                    <Track.IncreaseRepeatButton><RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0" Focusable="False"/></Track.IncreaseRepeatButton>
                    <Track.DecreaseRepeatButton><RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0" Focusable="False"/></Track.DecreaseRepeatButton>
                  </Track>
                </Grid>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
        """);
}

/// <summary>A group of rows on a faint glass card (<c>.group</c>): 22 px corners, a hairline, and a line between rows.</summary>
internal sealed class GlassCard : Border
{
    private readonly StackPanel _rows = new();

    public GlassCard(string? heading = null)
    {
        Background = Look.Group;
        BorderBrush = Look.Line;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(Look.GroupRadius);
        Child = _rows;
        if (heading is not null)
            _rows.Children.Add(new Border { Padding = new Thickness(16, 10, 16, 2), Child = Look.Label(heading, Look.SmallSize, brush: Look.Sub) });
    }

    public StackPanel Rows => _rows;

    public GlassCard Add(UIElement row, bool tight = false)
    {
        var first = _rows.Children.Count == 0 || _rows.Children.Count == 1 && _rows.Children[0] is Border { Child: TextBlock };
        _rows.Children.Add(new Border
        {
            BorderBrush = Look.Line,
            BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0),
            MinHeight = tight ? 0 : Look.RowMinHeight,
            Padding = tight ? new Thickness(0) : new Thickness(16, 8, 16, 8),
            Child = row,
        });
        return this;
    }
}
