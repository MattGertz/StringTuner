using StringTuner.ViewModels;
using StringTuner.Views;

namespace StringTuner;

public partial class MainPage : ContentPage
{
	private readonly TunerViewModel _viewModel;

	public MainPage(TunerViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		BindingContext = viewModel;
		TuningGauge.Drawable = new TuningGaugeDrawable(() => viewModel.GaugeCents, () => viewModel.TuningStateColor);
		viewModel.PropertyChanged += OnViewModelPropertyChanged;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await _viewModel.InitializeAsync();
	}

	protected override async void OnDisappearing()
	{
		await _viewModel.StopAudioAsync();
		base.OnDisappearing();
	}

	private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
	{
		if (eventArgs.PropertyName is nameof(TunerViewModel.GaugeCents) or nameof(TunerViewModel.TuningStateColor))
		{
			TuningGauge.Invalidate();
		}
	}
}
