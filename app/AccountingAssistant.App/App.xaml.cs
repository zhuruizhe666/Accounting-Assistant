namespace AccountingAssistant.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        System.Windows.EventManager.RegisterClassHandler(
            typeof(System.Windows.Window),
            System.Windows.FrameworkElement.LoadedEvent,
            new System.Windows.RoutedEventHandler(OnWindowLoaded));

        base.OnStartup(e);
    }

    private static void OnWindowLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.Window window)
        {
            Services.WindowAppearanceService.ApplyDarkTitleBar(window);
        }
    }
}
