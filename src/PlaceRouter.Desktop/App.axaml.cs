using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PlaceRouter.Geometry;
using PlaceRouter.Infrastructure.Composition;
using PlaceRouter.Application.Lifecycle;
using PlaceRouter.Presentation.Project;
using PlaceRouter.Presentation.ViewModels;
using PlaceRouter.Presentation.Workspace;
using PlaceRouter.Desktop.Views;

namespace PlaceRouter.Desktop;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WTK.PlaceRouter");
            var projects = new ProjectCoordinator(
                PlaceRouterComposition.CreateProjectService(),
                new ConstraintEvaluationService(),
                new FileRecoveryJournal(Path.Combine(appData, "recovery")));
            var shell = new PlaceRouterShellViewModel(projects, new PlaceRouterLayoutService(Path.Combine(appData, "workspace-layout.json")));
            desktop.MainWindow = new MainWindow { DataContext = shell };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
