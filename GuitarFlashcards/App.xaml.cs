namespace GuitarFlashcards;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var mainPage = _services.GetRequiredService<MainPage>();
        var navigationPage = new NavigationPage(mainPage)
        {
            BarBackgroundColor = Color.FromArgb("#F4F3ED"),
            BarTextColor = Color.FromArgb("#202A27")
        };
        var window = new Window(navigationPage);
#if WINDOWS
    window.Width = 560;
    window.Height = 820;
#endif
        return window;
    }
}