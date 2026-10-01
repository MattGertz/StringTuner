using StringTuner.ViewModels;
using StringTuner.Views;

namespace StringTuner;

public partial class MainPage : ContentPage
{
	private readonly TunerViewModel _viewModel;
	private readonly InstrumentEditorPage _editorPage;

	public MainPage(TunerViewModel viewModel, InstrumentEditorPage editorPage)
	{
		InitializeComponent();
		_viewModel = viewModel;
		_editorPage = editorPage;
		BindingContext = viewModel;
		TuningGauge.Drawable = new TuningGaugeDrawable(() => viewModel.GaugeCents, () => viewModel.TuningStateColor);
		viewModel.PropertyChanged += OnViewModelPropertyChanged;
	}

	private async void OnEditInstrumentsClicked(object? sender, EventArgs eventArgs)
	{
		await _editorPage.PrepareForOpeningAsync();
		await Navigation.PushModalAsync(_editorPage);
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await _viewModel.ReloadCatalogAsync();
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
