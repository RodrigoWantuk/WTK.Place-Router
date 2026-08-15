using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PlaceRouter.Geometry;
using PlaceRouter.Infrastructure.Composition;
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
            var projects = new ProjectCoordinator(PlaceRouterComposition.CreateProjectService(), new ConstraintEvaluationService());
            var shell = new PlaceRouterShellViewModel(projects, new PlaceRouterLayoutService());
            desktop.MainWindow = new MainWindow { DataContext = shell };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
