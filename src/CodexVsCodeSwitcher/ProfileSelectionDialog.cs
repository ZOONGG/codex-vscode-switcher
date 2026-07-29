using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CodexVsCodeSwitcher.Core.Models;
using Button = System.Windows.Controls.Button;

namespace CodexVsCodeSwitcher;

internal sealed class ProfileSelectionDialog : Window
{
    private readonly ListBox profileList = new();

    private ProfileSelectionDialog(IReadOnlyList<ProfileInfo> profiles, Localizer localizer)
    {
        Title = localizer["ChooseProfileTitle"];
        Icon = AppIcons.WindowIcon;
        Width = 520;
        Height = Math.Clamp(300 + (profiles.Count * 54), 380, 620);
        WindowStartupLocation = WindowStartupLocation.Manual;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Foreground = Brush("StrongTextBrush");
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        var chrome = new Border
        {
            Background = Brush("OverlayBackgroundBrush"),
            BorderBrush = Brush("OverlayBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(22),
        };
        WindowDragHelper.Enable(this, chrome);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = localizer["ChooseProfileTitle"],
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("StrongTextBrush"),
        });
        var close = new Button
        {
            Content = "×",
            Width = 30,
            Height = 30,
            MinHeight = 30,
            Padding = new Thickness(0),
            IsCancel = true,
        };
        close.Click += (_, _) => CloseWithoutSelection();
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        root.Children.Add(header);

        var description = new TextBlock
        {
            Text = localizer["ChooseProfileToLaunch"],
            Foreground = Brush("MutedTextBrush"),
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 21,
            Margin = new Thickness(0, 0, 0, 14),
        };
        Grid.SetRow(description, 1);
        root.Children.Add(description);

        profileList.Background = Brushes.Transparent;
        profileList.BorderThickness = new Thickness(0);
        profileList.Margin = new Thickness(0, 0, 0, 18);
        AutomationProperties.SetName(profileList, localizer["Profiles"]);
        foreach (ProfileInfo profile in profiles)
        {
            var label = new StackPanel();
            label.Children.Add(new TextBlock
            {
                Text = profile.DisplayName,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("StrongTextBrush"),
            });
            label.Children.Add(new TextBlock
            {
                Text = localizer["ProfileAvailable"],
                Margin = new Thickness(0, 3, 0, 0),
                FontSize = 12.5,
                Foreground = Brush("MutedTextBrush"),
            });
            var item = new ListBoxItem
            {
                Tag = profile,
                Content = label,
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 6),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetName(item, profile.DisplayName);
            profileList.Items.Add(item);
        }

        profileList.SelectedIndex = 0;
        profileList.MouseDoubleClick += (_, _) => AcceptSelection();
        profileList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                AcceptSelection();
                e.Handled = true;
            }
        };
        Grid.SetRow(profileList, 2);
        root.Children.Add(profileList);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Button launch = new()
        {
            Content = localizer["LaunchWithProfile"],
            MinWidth = 170,
            MinHeight = 38,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Style = (Style)Application.Current.FindResource("PrimaryButtonStyle"),
        };
        launch.Click += (_, _) => AcceptSelection();
        buttons.Children.Add(launch);
        buttons.Children.Add(new Button
        {
            Content = localizer["Cancel"],
            MinWidth = 96,
            MinHeight = 38,
            IsCancel = true,
        });
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        chrome.Child = root;
        Content = chrome;
        Loaded += (_, _) =>
        {
            Activate();
            profileList.Focus();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                CloseWithoutSelection();
            }
        };
    }

    public string? SelectedProfileId { get; private set; }

    public static string? Show(
        Window? owner,
        IntPtr nativeOwner,
        IReadOnlyList<ProfileInfo> profiles,
        Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(localizer);
        if (profiles.Count == 0)
        {
            return null;
        }

        var dialog = new ProfileSelectionDialog(profiles, localizer);
        if (owner is not null)
        {
            dialog.Owner = owner;
        }
        else if (nativeOwner != IntPtr.Zero)
        {
            new WindowInteropHelper(dialog).Owner = nativeOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        DialogPlacement.CenterOnOwnerScreen(dialog, owner, nativeOwner);
        return dialog.ShowDialog() == true ? dialog.SelectedProfileId : null;
    }

    private void AcceptSelection()
    {
        if (profileList.SelectedItem is not ListBoxItem { Tag: ProfileInfo profile })
        {
            return;
        }

        SelectedProfileId = profile.Name;
        DialogResult = true;
        Close();
    }

    private void CloseWithoutSelection()
    {
        DialogResult = false;
        Close();
    }

    private static Brush Brush(string key)
        => (Brush)Application.Current.FindResource(key);
}
