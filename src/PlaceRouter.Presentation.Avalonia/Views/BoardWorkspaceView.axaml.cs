using Avalonia.Controls;

namespace PlaceRouter.Presentation.Views;

public partial class BoardWorkspaceView : UserControl
{
    public BoardWorkspaceView()
    {
        InitializeComponent();
    }

    private void Fit_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Viewport.Fit();
}
