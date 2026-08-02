using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;

namespace CodexVsCodeSwitcher;

internal enum FirstLaunchProjectAction
{
    Cancel,
    LastProject,
    ChooseFolder,
    ChooseWorkspaceFile,
    OpenEmpty,
}

internal sealed class FirstLaunchProjectDialog : Window
{
    private FirstLaunchProjectDialog(bool hasLastProject, Localizer localizer)
    {
        Title = localizer["FirstLaunchProjectTitle"];
        Icon = AppIcons.WindowIcon;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("WindowBackgroundBrush");
        Foreground = (Brush)Application.Current.FindResource("StrongTextBrush");

        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(new TextBlock
        {
            Text = localizer["FirstLaunchProjectTitle"],
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 18),
        });
        if (hasLastProject)
        {
            stack.Children.Add(CreateButton(localizer["LastProject"], FirstLaunchProjectAction.LastProject, true));
        }
        stack.Children.Add(CreateButton(localizer["ChooseFolder"], FirstLaunchProjectAction.ChooseFolder, !hasLastProject));
        stack.Children.Add(CreateButton(localizer["ChooseWorkspaceFile"], FirstLaunchProjectAction.ChooseWorkspaceFile, false));
        stack.Children.Add(CreateButton(localizer["OpenEmptyWindow"], FirstLaunchProjectAction.OpenEmpty, false));
        Content = stack;
    }

    public FirstLaunchProjectAction SelectedAction { get; private set; }

    public static FirstLaunchProjectAction Show(
        Window? owner,
        IntPtr nativeOwner,
        bool hasLastProject,
        Localizer localizer)
    {
        var dialog = new FirstLaunchProjectDialog(hasLastProject, localizer);
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

    private Button CreateButton(string text, FirstLaunchProjectAction action, bool primary)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 40,
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
