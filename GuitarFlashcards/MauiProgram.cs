using Plugin.Maui.Audio;
using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;
using GuitarFlashcards.Core.Audio;
using GuitarFlashcards.Services;
using GuitarFlashcards.ViewModels;
using GuitarFlashcards.Views;

namespace GuitarFlashcards;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .AddAudio();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<LocalConfigurationStore>();
        builder.Services.AddSingleton<ChordWaveformSynthesizer>();
        builder.Services.AddSingleton<FlashcardAudioPlayer>();
        builder.Services.AddSingleton<FlashcardsViewModel>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<LibraryPage>();
        builder.Services.AddTransient<ChordEditorPage>();
        return builder.Build();
    }
}