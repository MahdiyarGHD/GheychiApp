using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Gheychi.App.Localization;
using Gheychi.App.Theming;

namespace Gheychi.App.Ui;

/// <summary>A day and a time of day, picked on the app's own sheets: a month to tap a day on, and two columns to step a time.</summary>
public static class DateTimeSheets
{
    private static readonly string[] PersianMonths =
        ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];

    // Indexed by DayOfWeek, Sunday first.
    private static readonly string[] PersianDays = ["ی", "د", "س", "چ", "پ", "ج", "ش"];

    /// <summary>Persian clocks are 24-hour; elsewhere the phone's own setting decides.</summary>
    public static bool Is24Hour =>
        LocalizedNumbers.IsPersian || global::Android.Text.Format.DateFormat.Is24HourFormat(Platform.AppContext);

    private static string Digits(int value) => LocalizedNumbers.Number(value);

    // ---- Day -------------------------------------------------------------------------------------

    /// <summary>The day picked (as the calendar of the language: Jalali in Persian), or null. Days before today cannot be picked.</summary>
    public static Task<DateTime?> PickDateAsync(DateTimeOffset now)
    {
        var loc = LocalizationManager.Instance;
        var persian = LocalizedNumbers.IsPersian;
        System.Globalization.Calendar calendar = persian ? new PersianCalendar() : new GregorianCalendar();
        var culture = CultureInfo.CurrentUICulture;
        var today = now.LocalDateTime.Date;
        var weekStart = persian ? DayOfWeek.Saturday : culture.DateTimeFormat.FirstDayOfWeek;

        DateTime FirstOfMonth(DateTime date) => calendar.ToDateTime(calendar.GetYear(date), calendar.GetMonth(date), 1, 0, 0, 0, 0);

        var firstAllowed = FirstOfMonth(today);
        return Sheet.ShowAsync<DateTime?>(finish =>
        {
            var selected = today;
            var shown = firstAllowed;

            var title = Sheet.Label(string.Empty, 17, Sheet.Ink, bold: true);
            title.TextAlignment = TextAlignment.Center;
            title.HorizontalAlignment = HorizontalAlignment.Center;
            title.VerticalAlignment = VerticalAlignment.Center;

            var grid = new UniformGrid { Columns = 7 };
            Border? previous = null;

            void Render()
            {
                title.Text = persian
                    ? $"{PersianMonths[calendar.GetMonth(shown) - 1]} {Digits(calendar.GetYear(shown))}"
                    : shown.ToString("MMMM yyyy", culture);
                if (previous is not null)
                    previous.Opacity = shown > firstAllowed ? 1 : 0.25;

                grid.Children.Clear();
                var lead = ((int)shown.DayOfWeek - (int)weekStart + 7) % 7;
                for (var i = 0; i < lead; i++)
                    grid.Children.Add(new Border { Height = 44 });

                var days = calendar.GetDaysInMonth(calendar.GetYear(shown), calendar.GetMonth(shown));
                for (var d = 1; d <= days; d++)
                    grid.Children.Add(Cell(shown.AddDays(d - 1), d));
            }

            Control Cell(DateTime date, int dayOfMonth)
            {
                var allowed = date >= today;
                var isSelected = date == selected;
                var label = Sheet.Label(Digits(dayOfMonth), 15, isSelected ? Palette.Brush("#FFFFFF") : Sheet.Ink, bold: isSelected || date == today);
                label.TextWrapping = TextWrapping.NoWrap;
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.VerticalAlignment = VerticalAlignment.Center;

                var disc = new Border
                {
                    Width = 40,
                    Height = 40,
                    CornerRadius = new CornerRadius(20),
                    Background = isSelected ? Palette.Accent(AccentRole.Solid) : Palette.Transparent,
                    BorderThickness = new Thickness(date == today && !isSelected ? 1.5 : 0),
                    BorderBrush = Palette.Accent(AccentRole.Text),
                    Opacity = allowed ? 1 : 0.3,
                    Child = label
                };
                var cell = new Border { Height = 44, Background = Palette.Transparent, Child = disc };
                if (allowed)
                {
                    cell.Tapped += (_, e) =>
                    {
                        e.Handled = true;
                        selected = date;
                        Render();
                    };
                }

                return cell;
            }

            previous = Arrow(isBack: true, () =>
            {
                if (shown > firstAllowed)
                {
                    shown = calendar.AddMonths(shown, -1);
                    Render();
                }
            });
            var next = Arrow(isBack: false, () =>
            {
                shown = calendar.AddMonths(shown, 1);
                Render();
            });

            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(12, 0) };
            Grid.SetColumn(title, 1);
            Grid.SetColumn(next, 2);
            header.Children.Add(previous);
            header.Children.Add(title);
            header.Children.Add(next);

            var week = new UniformGrid { Columns = 7, Margin = new Thickness(16, 6, 16, 0) };
            for (var i = 0; i < 7; i++)
            {
                var day = (DayOfWeek)(((int)weekStart + i) % 7);
                var name = persian ? PersianDays[(int)day] : culture.DateTimeFormat.GetAbbreviatedDayName(day);
                var label = Sheet.Label(name, 12, Sheet.Muted, bold: true);
                label.TextWrapping = TextWrapping.NoWrap;
                label.HorizontalAlignment = HorizontalAlignment.Center;
                week.Children.Add(label);
            }

            grid.Margin = new Thickness(16, 4, 16, 8);
            Render();

            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            stack.Children.Add(Sheet.Grabber());
            stack.Children.Add(Sheet.Heading(loc["Picker_DateTitle"]));
            stack.Children.Add(header);
            stack.Children.Add(week);
            stack.Children.Add(grid);
            stack.Children.Add(new Border
            {
                Margin = new Thickness(20, 4, 20, 0),
                Child = Sheet.Button(loc["Picker_Ok"], filled: true, () => finish(selected))
            });
            return stack;
        }, null);
    }

    private static Border Arrow(bool isBack, Action tap)
    {
        var icon = new Icon
        {
            Data = IconCatalog.Find(isBack ? "back.png" : "chevron_right.png"),
            Width = 20,
            Height = 20,
            Foreground = Sheet.Ink,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        return Sheet.Tappable(icon, tap, new Thickness(0), 44).WithSize(44);
    }

    private static Border WithSize(this Border border, double size)
    {
        border.Width = size;
        border.Height = size;
        border.CornerRadius = new CornerRadius(size / 2);
        return border;
    }

    // ---- Time ------------------------------------------------------------------------------------

    /// <summary>The hour (0-23) and minute picked, or null. Holding a step button runs the value on.</summary>
    public static Task<(int Hour, int Minute)?> PickTimeAsync(int hour, int minute, bool is24Hour)
    {
        var loc = LocalizationManager.Instance;
        return Sheet.ShowAsync<(int Hour, int Minute)?>(finish =>
        {
            var hourText = Sheet.Label(string.Empty, 40, Sheet.Ink, bold: true);
            var minuteText = Sheet.Label(string.Empty, 40, Sheet.Ink, bold: true);
            var amPm = new Border[2];

            void Render()
            {
                var shownHour = is24Hour ? hour : hour % 12 == 0 ? 12 : hour % 12;
                hourText.Text = LocalizedNumbers.Digits(shownHour.ToString("00", CultureInfo.InvariantCulture));
                minuteText.Text = LocalizedNumbers.Digits(minute.ToString("00", CultureInfo.InvariantCulture));
                if (!is24Hour)
                {
                    for (var i = 0; i < 2; i++)
                    {
                        var on = (hour >= 12) == (i == 1);
                        amPm[i].Background = on ? Palette.Accent(AccentRole.Solid) : Sheet.Field;
                        ((TextBlock)amPm[i].Child!).Foreground = on ? Palette.Brush("#FFFFFF") : Sheet.Soft;
                    }
                }
            }

            Control Column(TextBlock text, Action<int> step)
            {
                text.TextWrapping = TextWrapping.NoWrap;
                text.HorizontalAlignment = HorizontalAlignment.Center;
                text.VerticalAlignment = VerticalAlignment.Center;
                var well = new Border
                {
                    Width = 92,
                    Height = 76,
                    CornerRadius = new CornerRadius(18),
                    Background = Sheet.Field,
                    Child = text
                };
                var column = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
                column.Children.Add(Stepper(up: true, () => step(1)));
                column.Children.Add(well);
                column.Children.Add(Stepper(up: false, () => step(-1)));
                return column;
            }

            var hours = Column(hourText, d =>
            {
                hour = (hour + d + 24) % 24;
                Render();
            });
            var minutes = Column(minuteText, d =>
            {
                minute = (minute + d + 60) % 60;
                Render();
            });

            var colon = Sheet.Label(":", 36, Sheet.Muted, bold: true);
            colon.VerticalAlignment = VerticalAlignment.Center;
            colon.Margin = new Thickness(0, 0, 0, 4);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center };
            // Time reads left to right in every language, so the columns keep their order in a mirrored layout.
            row.FlowDirection = FlowDirection.LeftToRight;
            row.Children.Add(hours);
            row.Children.Add(colon);
            row.Children.Add(minutes);

            if (!is24Hour)
            {
                var names = new[] { CultureInfo.CurrentUICulture.DateTimeFormat.AMDesignator, CultureInfo.CurrentUICulture.DateTimeFormat.PMDesignator };
                var toggle = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                for (var i = 0; i < 2; i++)
                {
                    var isPm = i == 1;
                    var label = Sheet.Label(names[i], 14, Sheet.Soft, bold: true);
                    label.TextWrapping = TextWrapping.NoWrap;
                    label.HorizontalAlignment = HorizontalAlignment.Center;
                    label.VerticalAlignment = VerticalAlignment.Center;
                    amPm[i] = new Border { Width = 56, Height = 44, CornerRadius = new CornerRadius(14), Child = label };
                    amPm[i].Tapped += (_, e) =>
                    {
                        e.Handled = true;
                        if ((hour >= 12) != isPm)
                            hour = (hour + 12) % 24;
                        Render();
                    };
                    toggle.Children.Add(amPm[i]);
                }

                row.Children.Add(toggle);
            }

            Render();
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            stack.Children.Add(Sheet.Grabber());
            stack.Children.Add(Sheet.Heading(loc["Picker_TimeTitle"]));
            stack.Children.Add(new Border { Margin = new Thickness(0, 6, 0, 18), Child = row });
            stack.Children.Add(new Border
            {
                Margin = new Thickness(20, 0),
                Child = Sheet.Button(loc["Picker_Ok"], filled: true, () => finish((hour, minute)))
            });
            return stack;
        }, null);
    }

    private static Control Stepper(bool up, Action step)
    {
        var chevron = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(up ? "M 0,8 L 8,0 L 16,8" : "M 0,0 L 8,8 L 16,0"),
            Stroke = Sheet.Soft,
            StrokeThickness = 2.5,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.None
        };
        var button = new Border
        {
            Width = 92,
            Height = 40,
            CornerRadius = new CornerRadius(14),
            Background = Palette.Transparent,
            Child = chevron
        };

        // One step at once, then faster the longer the finger stays.
        var repeat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
        repeat.Tick += (_, _) =>
        {
            repeat.Interval = TimeSpan.FromMilliseconds(70);
            step();
        };

        void Stop(object? s, EventArgs e)
        {
            repeat.Stop();
            button.Background = Palette.Transparent;
        }

        button.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            button.Background = Palette.Accent(AccentRole.Soft, 0x66);
            step();
            repeat.Interval = TimeSpan.FromMilliseconds(380);
            repeat.Start();
        };
        button.PointerReleased += Stop;
        button.PointerCaptureLost += Stop;
        button.PointerExited += Stop;
        return button;
    }
}
