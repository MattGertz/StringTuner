using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;
using StringTuner.Audio;
using StringTuner.Services;
using StringTuner.ViewModels;

namespace StringTuner;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.AddAudio()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});
		builder.Services.AddSingleton<IAudioCapture, PluginAudioCapture>();
		builder.Services.AddSingleton<IAudioToneGenerator, PluginAudioToneGenerator>();
		builder.Services.AddSingleton<IInstrumentCatalogProvider, JsonInstrumentCatalogProvider>();
		builder.Services.AddSingleton<TunerViewModel>();
		builder.Services.AddSingleton<MainPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
