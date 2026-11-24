using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DynamicIsland.Services;

namespace DynamicIsland.Panels;

/// The last notifications from the watched app, newest first.
public partial class MessagesPanel : UserControl
{
    const int Show = 6;

    static readonly CultureInfo Culture = new("pt-BR");

    static readonly Brush Accent = Freeze(Color.FromRgb(0x25, 0xD3, 0x66));

    readonly NotificationService _notifications;

    /// Raised when the list grew or shrank, so the window can resize to it.
    public event Action? LayoutChanged;

    public MessagesPanel(NotificationService notifications)
    {
        _notifications = notifications;
        InitializeComponent();
    }

    public void Refresh()
    {
        int before = List.Children.Count;

        List.Children.Clear();

        var messages = _notifications.Recent.Take(Show).ToList();
        Empty.Visibility = messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var message in messages) List.Children.Add(Row(message));

        if (before != List.Children.Count) LayoutChanged?.Invoke();
    }

    UIElement Row(IncomingMessage message)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var avatar = new Grid { Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Top };
        avatar.Children.Add(new Ellipse { Fill = Accent });
        avatar.Children.Add(new TextBlock
        {
            Text = message.Sender.Length > 0 ? message.Sender[..1].ToUpper(Culture) : "?",
            Foreground = Brushes.Black,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        var text = new StackPanel { Margin = new Thickness(11, 0, 8, 0) };
        text.Children.Add(new TextBlock
        {
            Text = message.Sender,
            Foreground = (Brush)FindResource("TextPrimary"),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        text.Children.Add(new TextBlock
        {
            Text = message.Preview,
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 11.5,
            MaxHeight = 32,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0)
        });

        var when = new TextBlock
        {
            Text = message.At.ToString("HH:mm", Culture),
            Foreground = (Brush)FindResource("TextFaint"),
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Top
        };

        Grid.SetColumn(text, 1);
        Grid.SetColumn(when, 2);

        row.Children.Add(avatar);
        row.Children.Add(text);
        row.Children.Add(when);
        return row;
    }

    static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
