using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ShutdownClock.Controls;

/// <summary>
/// 单个翻页数字牌。上半片固定显示"新值"，下半片固定显示"旧值"，
/// 用一块绕水平中轴翻转的卡片完成翻页动画（经典 flip clock 效果）。
/// </summary>
public sealed class FlipCard : Grid
{
    private readonly Border _upperStatic;
    private readonly Border _lowerStatic;
    private readonly Border _flap;
    private readonly TextBlock _upperText;
    private readonly TextBlock _lowerText;
    private readonly TextBlock _flapText;

    private string _value = "0";
    private string _previous = "0";

    public FlipCard()
    {
        ClipToBounds = true;

        _upperStatic = MakeHalf(top: true);
        _lowerStatic = MakeHalf(top: false);

        _upperText = (TextBlock)_upperStatic.Child;
        _lowerText = (TextBlock)_lowerStatic.Child;

        _flap = MakeHalf(top: true);
        _flapText = (TextBlock)_flap.Child;

        Children.Add(_upperStatic);
        Children.Add(_lowerStatic);
        Children.Add(_flap);

        RenderTransformOrigin = new Point(0.5, 0.5);
        _flap.RenderTransformOrigin = new Point(0.5, 1.0); // 绕下边缘（即中轴）翻转
        _flap.RenderTransform = new ScaleTransform(1, 1);

        SetValue("0", animate: false);
    }

    private static Border MakeHalf(bool top)
    {
        var tb = new TextBlock
        {
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Consolas, Segoe UI, Microsoft YaHei"),
            FontWeight = FontWeights.Bold
        };
        var bd = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22)),
            Child = tb,
            Margin = new Thickness(2)
        };
        return bd;
    }

    /// <summary>设置显示的数字文本（0-9 或两位以上）。</summary>
    public void SetValue(string value, bool animate = true)
    {
        if (value == _value && !animate) { ApplyLayout(); return; }

        _previous = _value;
        _value = value;

        if (!animate || _previous == _value)
        {
            ApplyLayout();
            return;
        }

        // 上半静止片立即换成新值；下半静止片保持旧值
        _upperText.Text = _value;
        _lowerText.Text = _previous;
        _flapText.Text = _previous;   // 翻转卡从旧值翻下来

        _flap.Visibility = Visibility.Visible;
        var scale = (ScaleTransform)_flap.RenderTransform;
        scale.ScaleY = 1;

        var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        anim.Completed += (_, _) =>
        {
            _lowerText.Text = _value;
            _flap.Visibility = Visibility.Collapsed;
        };
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    private void ApplyLayout()
    {
        _upperText.Text = _value;
        _lowerText.Text = _value;
        _flap.Visibility = Visibility.Collapsed;
    }

    public double FontSize
    {
        get => _upperText.FontSize;
        set
        {
            _upperText.FontSize = value;
            _lowerText.FontSize = value;
            _flapText.FontSize = value;
        }
    }

    public Brush CardBackground
    {
        set
        {
            _upperStatic.Background = value;
            _lowerStatic.Background = value;
            _flap.Background = value;
        }
    }

    public Brush ForegroundBrush
    {
        set
        {
            _upperText.Foreground = value;
            _lowerText.Foreground = value;
            _flapText.Foreground = value;
        }
    }
}

/// <summary>一组翻页时钟（时 / 分 / 秒），自动适配剩余时间。</summary>
public sealed class FlipClock : StackPanel
{
    private readonly FlipCard _h1 = new();
    private readonly FlipCard _h2 = new();
    private readonly FlipCard _m1 = new();
    private readonly FlipCard _m2 = new();
    private readonly FlipCard _s1 = new();
    private readonly FlipCard _s2 = new();
    private readonly TextBlock _dayText = new();

    private string _lastText = string.Empty;

    public FlipClock()
    {
        Orientation = Orientation.Horizontal;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;

        _dayText.Visibility = Visibility.Collapsed;
        _dayText.VerticalAlignment = VerticalAlignment.Center;
        _dayText.Margin = new Thickness(0, 0, 10, 0);
        _dayText.FontWeight = FontWeights.Bold;

        Children.Add(_dayText);
        Children.Add(MakeGroup(_h1, _h2));
        Children.Add(MakeColon());
        Children.Add(MakeGroup(_m1, _m2));
        Children.Add(MakeColon());
        Children.Add(MakeGroup(_s1, _s2));
    }

    private static UIElement MakeGroup(FlipCard a, FlipCard b)
    {
        var g = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(1) };
        g.Children.Add(a);
        g.Children.Add(b);
        return g;
    }

    private static UIElement MakeColon() => new TextBlock
    {
        Text = ":",
        Foreground = Brushes.White,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(4, 0, 4, 10),
        FontWeight = FontWeights.Bold,
        TextAlignment = TextAlignment.Center
    };

    /// <summary>按剩余时间刷新显示。animate=false 用于首次布局。</summary>
    public void Update(TimeSpan remaining, bool animate = true)
    {
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        var days = (int)remaining.TotalDays;
        var hh = remaining.Hours;
        var mm = remaining.Minutes;
        var ss = remaining.Seconds;

        var key = $"{days}|{hh:D2}|{mm:D2}|{ss:D2}";
        if (key == _lastText && animate) return;
        _lastText = key;

        _h1.SetValue((hh / 10).ToString(), animate);
        _h2.SetValue((hh % 10).ToString(), animate);
        _m1.SetValue((mm / 10).ToString(), animate);
        _m2.SetValue((mm % 10).ToString(), animate);
        _s1.SetValue((ss / 10).ToString(), animate);
        _s2.SetValue((ss % 10).ToString(), animate);

        if (days > 0)
        {
            _dayText.Text = $"{days} 天";
            _dayText.Visibility = Visibility.Visible;
        }
        else
        {
            _dayText.Visibility = Visibility.Collapsed;
        }
    }

    public void ApplyScale(double unit)
    {
        if (unit <= 0) unit = 1;
        var fs = 42 * unit;

        foreach (var c in new[] { _h1, _h2, _m1, _m2, _s1, _s2 })
        {
            c.FontSize = fs;
            c.Width = 30 * unit;
            c.Height = 54 * unit;
        }

        _dayText.FontSize = 20 * unit;
        _dayText.Foreground = Brushes.White;

        if (Children.Count >= 6)
        {
            foreach (var child in Children)
            {
                if (child is TextBlock tb && tb.Text == ":")
                {
                    tb.FontSize = fs * 0.8;
                }
            }
        }
    }
}
