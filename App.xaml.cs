namespace StringTuner;

public partial class App : Application
{
	private readonly MainPage _mainPage;

	public App(MainPage mainPage)
	{
		InitializeComponent();
		_mainPage = mainPage;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(_mainPage);
#if WINDOWS
		window.Width = 560;
		window.Height = 750;
#endif
		return window;
	}
}