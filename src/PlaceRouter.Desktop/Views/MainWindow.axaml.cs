using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using PlaceRouter.Presentation.Docking;
using PlaceRouter.Presentation.ViewModels;

namespace PlaceRouter.Desktop.Views;

public partial class MainWindow : Window
{
    private bool _closeAccepted;
    private bool _closePromptInFlight;

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (DataContext is PlaceRouterShellViewModel shell)
            {
                shell.RestoreFloatingDocks(GetMonitorWorkAreas());
            }
        };
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_closeAccepted || _closePromptInFlight || DataContext is not PlaceRouterShellViewModel { HasDirtyProject: true } shell)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        _closePromptInFlight = true;
        _ = ConfirmSaveBeforeClose(shell).ContinueWith(task =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _closePromptInFlight = false;
                if (task.Status == TaskStatus.RanToCompletion && task.Result)
                {
                    _closeAccepted = true;
                    Close();
                }
            });
        });
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is PlaceRouterShellViewModel shell)
        {
            shell.PersistWorkspace(GetMonitorWorkAreas());
        }

        base.OnClosed(e);
    }

    private async void OpenProject_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not PlaceRouterShellViewModel shell)
        {
            return;
        }

        if (!await ConfirmSaveBeforeClose(shell).ConfigureAwait(true))
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open PRDX project",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PRDX Project") { Patterns = ["*.prdx"] }]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await shell.OpenProjectAsync(path).ConfigureAwait(true);
        }
    }

    private async void ImportDsn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not PlaceRouterShellViewModel shell)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import SPECCTRA DSN",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("SPECCTRA DSN") { Patterns = ["*.dsn"] }]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await shell.ImportDsnAsync(path).ConfigureAwait(true);
        }
    }

    private async void SaveProject_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not PlaceRouterShellViewModel shell)
        {
            return;
        }

        if (!await shell.SaveProjectAsync().ConfigureAwait(true))
        {
            await SaveProjectAs(shell);
        }
    }

    private async void SaveProjectAs_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is PlaceRouterShellViewModel shell)
        {
            await SaveProjectAs(shell);
        }
    }

    private async Task<bool> SaveProjectAs(PlaceRouterShellViewModel shell)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save PRDX project",
            SuggestedFileName = "project.prdx",
            DefaultExtension = "prdx",
            FileTypeChoices = [new FilePickerFileType("PRDX Project") { Patterns = ["*.prdx"] }]
        });
        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            return await shell.SaveProjectAsAsync(path).ConfigureAwait(true);
        }

        return false;
    }

    private async void CloseProject_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is PlaceRouterShellViewModel shell && await ConfirmSaveBeforeClose(shell).ConfigureAwait(true))
        {
            shell.CloseProject();
        }
    }

    private void Minimize_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeRestore_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Close();

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        BeginMoveDrag(e);
    }

    private async Task<bool> ConfirmSaveBeforeClose(PlaceRouterShellViewModel shell)
    {
        if (!shell.HasDirtyProject)
        {
            return true;
        }

        var choice = await DirtyProjectDialog.ShowAsync(this).ConfigureAwait(true);
        if (choice == DirtyProjectChoice.Cancel)
        {
            return false;
        }

        if (choice == DirtyProjectChoice.Discard)
        {
            return true;
        }

        return await shell.SaveProjectAsync().ConfigureAwait(true) || await SaveProjectAs(shell).ConfigureAwait(true);
    }

    private IReadOnlyList<PlaceRouterMonitorWorkArea> GetMonitorWorkAreas() => Screens.All
        .Select((screen, index) => new PlaceRouterMonitorWorkArea(
            screen.DisplayName ?? $"monitor-{index}",
            new Rect(
                screen.WorkingArea.X,
                screen.WorkingArea.Y,
                screen.WorkingArea.Width,
                screen.WorkingArea.Height),
            screen.IsPrimary))
        .ToArray();
}

internal enum DirtyProjectChoice
{
    Save,
    Discard,
    Cancel
}

internal sealed class DirtyProjectDialog : Window
{
    private readonly TaskCompletionSource<DirtyProjectChoice> _completion = new();

    private DirtyProjectDialog()
    {
        Width = 420;
        Height = 170;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Title = "Unsaved project";
        Content = BuildContent();
    }

    public static Task<DirtyProjectChoice> ShowAsync(Window owner)
    {
        var dialog = new DirtyProjectDialog();
        dialog.Closed += (_, _) => dialog._completion.TrySetResult(DirtyProjectChoice.Cancel);
        dialog.Show(owner);
        return dialog._completion.Task;
    }

    private Control BuildContent()
    {
        var text = new TextBlock
        {
            Text = "O projeto atual tem alterações não salvas.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Thickness(16, 16, 16, 10)
        };
        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(16)
        };
        AddButton(buttons, "Save", DirtyProjectChoice.Save);
        AddButton(buttons, "Discard", DirtyProjectChoice.Discard);
        AddButton(buttons, "Cancel", DirtyProjectChoice.Cancel);
        return new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children =
            {
                text,
                buttons
            }
        };
    }

    private void AddButton(StackPanel buttons, string text, DirtyProjectChoice choice)
    {
        var button = new Button { Content = text, MinWidth = 86 };
        button.Click += (_, _) =>
        {
            _completion.TrySetResult(choice);
            Close();
        };
        buttons.Children.Add(button);
        Grid.SetRow(buttons, 1);
    }
}
