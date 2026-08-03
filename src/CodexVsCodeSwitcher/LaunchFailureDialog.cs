using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;

namespace CodexVsCodeSwitcher;

internal enum LaunchFailureAction
{
    Close,
    Retry,
    ChooseAnotherProject,
    OpenWithoutProject,
    OpenDiagnostics,
    ResetLaunchState,
}

internal sealed class LaunchFailureDialog : Window
{
    private LaunchFailureDialog(string message, Localizer localizer)
    {
        Title = localizer["LaunchRecoveryTitle"];
        Icon = AppIcons.WindowIcon;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("WindowBackgroundBrush");
        Foreground = (Brush)Application.Current.FindResource("StrongTextBrush");

        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 18),
        });
        stack.Children.Add(CreateButton(localizer["RetryLaunch"], LaunchFailureAction.Retry, true));
        stack.Children.Add(CreateButton(localizer["ChooseAnotherProject"], LaunchFailureAction.ChooseAnotherProject));
        stack.Children.Add(CreateButton(localizer["OpenWithoutProject"], LaunchFailureAction.OpenWithoutProject));
        stack.Children.Add(CreateButton(localizer["OpenLaunchDiagnostics"], LaunchFailureAction.OpenDiagnostics));
        stack.Children.Add(CreateButton(localizer["ResetLaunchState"], LaunchFailureAction.ResetLaunchState));
        Content = stack;
    }

    public LaunchFailureAction SelectedAction { get; private set; }

    public static LaunchFailureAction Show(
        Window? owner,
        IntPtr nativeOwner,
        string message,
        Localizer localizer)
    {
        var dialog = new LaunchFailureDialog(message, localizer);
        if (owner is not null)
        {
            dialog.Owner = owner;
        }
        else if (nativeOwner != IntPtr.Zero)
        {
            new WindowInteropHelper(dialog).Owner = nativeOwner;
        }

        _ = dialog.ShowDialog();
        return dialog.SelectedAction;
    }

    private Button CreateButton(
        string text,
        LaunchFailureAction action,
        bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 38,
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(14, 8, 14, 8),
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        if (primary)
        {
            button.Style = (Style)Application.Current.FindResource("PrimaryButtonStyle");
        }

        button.Click += (_, _) =>
        {
            SelectedAction = action;
            DialogResult = true;
        };
        return button;
    }
}
