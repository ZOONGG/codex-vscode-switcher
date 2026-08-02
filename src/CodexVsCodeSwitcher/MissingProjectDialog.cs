using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;

namespace CodexVsCodeSwitcher;

internal enum MissingProjectAction
{
    Cancel,
    ChooseAnother,
    OpenEmpty,
    RemoveFromRecent,
}

internal sealed class MissingProjectDialog : Window
{
    private MissingProjectDialog(string projectPath, Localizer localizer)
    {
        Title = localizer["LastProjectMissingTitle"];
        Icon = AppIcons.WindowIcon;
        Width = 580;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.FindResource("WindowBackgroundBrush");
        Foreground = (Brush)Application.Current.FindResource("StrongTextBrush");

        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(new TextBlock
        {
            Text = localizer.Format("LastProjectMissingMessage", projectPath),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 18),
        });
        stack.Children.Add(CreateButton(
            localizer["ChooseAnotherFolder"],
            MissingProjectAction.ChooseAnother,
            primary: true));
        stack.Children.Add(CreateButton(
            localizer["OpenEmptyWindow"],
            MissingProjectAction.OpenEmpty,
            primary: false));
        stack.Children.Add(CreateButton(
            localizer["RemoveFromRecent"],
            MissingProjectAction.RemoveFromRecent,
            primary: false));
        Content = stack;
    }

    public MissingProjectAction SelectedAction { get; private set; }

    public static MissingProjectAction Show(
        Window? owner,
        IntPtr nativeOwner,
        string projectPath,
        Localizer localizer)
    {
        var dialog = new MissingProjectDialog(projectPath, localizer);
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

    private Button CreateButton(string text, MissingProjectAction action, bool primary)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 38,
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 8, 14, 8),
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
