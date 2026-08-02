using System.Windows;
using System.Windows.Controls;

namespace CodexVsCodeSwitcher;

internal sealed class DiagnosticReportWindow : Window
{
    public DiagnosticReportWindow(string title, string report, Localizer localizer)
    {
        Title = title;
        Icon = AppIcons.WindowIcon;
        Width = 860;
        Height = 620;
        MinWidth = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var text = new TextBox
        {
            Text = report,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"),
            FontSize = 13,
            Margin = new Thickness(16),
        };
        var copy = new Button
        {
            Content = localizer["Copy"],
            MinWidth = 120,
            Margin = new Thickness(0, 0, 8, 0),
        };
        copy.Click += (_, _) => Clipboard.SetText(text.Text);
        var close = new Button { Content = localizer["Close"], MinWidth = 120 };
        close.Click += (_, _) => Close();
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 0, 16, 16),
        };
        buttons.Children.Add(copy);
        buttons.Children.Add(close);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(text);
        Content = root;
    }
}
