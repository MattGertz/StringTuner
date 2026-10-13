#if WINDOWS
using WinUiToggleSwitch = Microsoft.UI.Xaml.Controls.ToggleSwitch;
#endif

namespace GuitarFlashcards.Views;

public sealed class SettingSwitch : Switch
{
#if WINDOWS
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler?.PlatformView is WinUiToggleSwitch toggleSwitch)
        {
            toggleSwitch.IsTabStop = false;
        }
    }
#endif
}