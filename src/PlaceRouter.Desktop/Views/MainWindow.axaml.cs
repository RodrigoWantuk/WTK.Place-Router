using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PlaceRouter.Presentation.Docking;
using PlaceRouter.Presentation.ViewModels;

namespace PlaceRouter.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open PRDX project",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PRDX Project") { Patterns = ["*.prdx"] }]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            shell.OpenProject(path);
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
            shell.ImportDsn(path);
        }
    }

    private async void SaveProject_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not PlaceRouterShellViewModel shell)
        {
            return;
        }

        if (!shell.SaveProject())
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

    private async Task SaveProjectAs(PlaceRouterShellViewModel shell)
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
            shell.SaveProjectAs(path);
        }
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
