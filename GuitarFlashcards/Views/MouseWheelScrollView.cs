#if WINDOWS
using WinUiScrollViewer = Microsoft.UI.Xaml.Controls.ScrollViewer;
using WinUiWheelEventArgs = Microsoft.UI.Xaml.Input.PointerRoutedEventArgs;
#endif

namespace GuitarFlashcards.Views;

public sealed class MouseWheelScrollView : ScrollView
{
#if WINDOWS
    private WinUiScrollViewer? _nativeScrollViewer;

    protected override void OnHandlerChanged()
    {
        if (_nativeScrollViewer is not null)
        {
            _nativeScrollViewer.PointerWheelChanged -= OnPointerWheelChanged;
            _nativeScrollViewer = null;
        }

        base.OnHandlerChanged();
        _nativeScrollViewer = Handler?.PlatformView as WinUiScrollViewer;
        if (_nativeScrollViewer is not null)
        {
            _nativeScrollViewer.PointerWheelChanged += OnPointerWheelChanged;
        }
    }

    private void OnPointerWheelChanged(object sender, WinUiWheelEventArgs eventArgs)
    {
        if (_nativeScrollViewer is null || _nativeScrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        var delta = eventArgs.GetCurrentPoint(_nativeScrollViewer).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        var nextOffset = Math.Clamp(_nativeScrollViewer.VerticalOffset - delta / 2d, 0, _nativeScrollViewer.ScrollableHeight);
        _nativeScrollViewer.ChangeView(null, nextOffset, null, disableAnimation: true);
        eventArgs.Handled = true;
    }
#endif
}