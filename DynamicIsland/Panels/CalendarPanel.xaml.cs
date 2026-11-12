using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Interop;
using DynamicIsland.Services;

namespace DynamicIsland.Panels;

public partial class CalendarPanel : UserControl
{
    const double CellHeight = 32;
    const int MaxListedEvents = 3;

    static readonly CultureInfo Culture = new("pt-BR");

    readonly CalendarService _calendar;
    readonly Brush _accent = AccentColor.Brush();

    DateTime _month = FirstOfMonth(DateTime.Today);
    DateTime _selected = DateTime.Today;
    IReadOnlyList<CalendarEvent> _events = [];

    /// Raised when the grid gained or lost a week, so the window can resize to
    /// the new height instead of leaving a gap or clipping the last row.
    public event Action? LayoutChanged;

    public CalendarPanel(GoogleCalendar google)
    {
        _calendar = new CalendarService(google);
        InitializeComponent();
        BuildWeekdayHeader();
        Rebuild();
    }

    public async Task RefreshAsync()
    {
        var (gridStart, weeks) = GridRange(_month);
        _events = await _calendar.LoadRangeAsync(gridStart, weeks * 7);
        Rebuild();
    }

    // ---- layout ---------------------------------------------------------

    static DateTime FirstOfMonth(DateTime date) => new(date.Year, date.Month, 1);

    /// The visible grid always starts on the culture's first weekday and holds
    /// whole weeks, so a month needs four, five or six rows depending on where
    /// the first falls.
    static (DateTime Start, int Weeks) GridRange(DateTime month)
    {
        var firstDay = Culture.DateTimeFormat.FirstDayOfWeek;
        int offset = ((int)month.DayOfWeek - (int)firstDay + 7) % 7;

        DateTime start = month.AddDays(-offset);
        int days = offset + DateTime.DaysInMonth(month.Year, month.Month);

        return (start, (int)Math.Ceiling(days / 7.0));
    }

    void BuildWeekdayHeader()
    {
        WeekdayHeader.Children.Clear();

        var firstDay = Culture.DateTimeFormat.FirstDayOfWeek;
        var names = Culture.DateTimeFormat.AbbreviatedDayNames;

        for (int i = 0; i < 7; i++)
        {
            var day = (DayOfWeek)(((int)firstDay + i) % 7);

            WeekdayHeader.Children.Add(new TextBlock
            {
                Text = names[(int)day][..1].ToUpper(Culture),
                Foreground = (Brush)FindResource("TextFaint"),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }
    }

    void Rebuild()
    {
        MonthLabel.Text = Culture.TextInfo.ToTitleCase(_month.ToString("MMMM yyyy", Culture));

        var (gridStart, weeks) = GridRange(_month);
        DayGrid.Rows = weeks;
        DayGrid.Children.Clear();

        var withEvents = _events
            .Select(e => e.Start.Date)
            .ToHashSet();

        for (int i = 0; i < weeks * 7; i++)
        {
            DayGrid.Children.Add(BuildCell(gridStart.AddDays(i), withEvents));
        }

        BuildEventList();

        // Not only when the week count changed: the event list (or an error line)
        // under the grid also changes the height.
        LayoutChanged?.Invoke();
    }

    UIElement BuildCell(DateTime date, HashSet<DateTime> withEvents)
    {
        bool inMonth = date.Month == _month.Month;
        bool isToday = date == DateTime.Today;
        bool isSelected = date == _selected.Date;

        var number = new TextBlock
        {
            Text = date.Day.ToString(Culture),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, withEvents.Contains(date.Date) ? 3 : 0)
        };

        number.Foreground = isToday
            ? Brushes.Black
            : (Brush)FindResource(inMonth ? "TextPrimary" : "TextFaint");

        if (isToday) number.FontWeight = FontWeights.Bold;

        var stack = new Grid();
        stack.Children.Add(number);

        if (withEvents.Contains(date.Date))
        {
            stack.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 3.5,
                Height = 3.5,
                Fill = isToday ? Brushes.Black : _accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 5)
            });
        }

        var cell = new Border
        {
            Height = CellHeight,
            Margin = new Thickness(1.5),
            CornerRadius = new CornerRadius(7),
            Background = isToday ? _accent : Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = stack,
            Tag = date
        };

        if (isSelected && !isToday)
        {
            cell.BorderBrush = (Brush)FindResource("Track");
            cell.BorderThickness = new Thickness(1);
        }

        cell.MouseLeftButtonUp += Day_Click;
        return cell;
    }

    void BuildEventList()
    {
        EventList.Children.Clear();

        bool isToday = _selected.Date == DateTime.Today;
        EventHeading.Text = isToday
            ? "Hoje"
            : Culture.TextInfo.ToTitleCase(_selected.ToString("dddd, d 'de' MMMM", Culture));

        var ofDay = _events.Where(e => e.Start.Date == _selected.Date).ToList();

        if (_calendar.GoogleError is { } googleError)
        {
            EventList.Children.Add(new TextBlock
            {
                Text = "Google Agenda: " + googleError,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 5)
            });
        }

        if (ofDay.Count == 0)
        {
            EventList.Children.Add(new TextBlock
            {
                Text = _calendar.Available ? "Nada marcado" : "Nenhuma conta conectada",
                Foreground = (Brush)FindResource("TextFaint"),
                FontSize = 11.5
            });
            return;
        }

        foreach (var item in ofDay.Take(MaxListedEvents))
        {
            EventList.Children.Add(BuildEventRow(item));
        }

        int hidden = ofDay.Count - MaxListedEvents;
        if (hidden > 0)
        {
            EventList.Children.Add(new TextBlock
            {
                Text = $"+{hidden} " + (hidden == 1 ? "outro" : "outros"),
                Foreground = (Brush)FindResource("TextFaint"),
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0)
            });
        }
    }

    UIElement BuildEventRow(CalendarEvent item)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var time = new TextBlock
        {
            Text = item.AllDay ? "dia" : item.Start.ToString("HH:mm", Culture),
            Foreground = _accent,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Width = 36
        };

        var subject = new TextBlock
        {
            Text = item.Subject,
            Foreground = (Brush)FindResource("TextPrimary"),
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0, 0, 0)
        };

        Grid.SetColumn(subject, 1);
        row.Children.Add(time);
        row.Children.Add(subject);
        return row;
    }

    // ---- input ----------------------------------------------------------

    void Day_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: DateTime date }) return;

        e.Handled = true;
        _selected = date;

        // Stepping onto a neighbouring month from an edge cell should follow.
        if (date.Month != _month.Month)
        {
            _month = FirstOfMonth(date);
            _ = RefreshAsync();
            return;
        }

        Rebuild();
    }

    void PreviousMonth_Click(object sender, MouseButtonEventArgs e) => Shift(e, -1);

    void NextMonth_Click(object sender, MouseButtonEventArgs e) => Shift(e, 1);

    void Shift(MouseButtonEventArgs e, int months)
    {
        e.Handled = true;
        _month = _month.AddMonths(months);
        _selected = _month;
        _ = RefreshAsync();
    }

    void Today_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _month = FirstOfMonth(DateTime.Today);
        _selected = DateTime.Today;
        _ = RefreshAsync();
    }
}
