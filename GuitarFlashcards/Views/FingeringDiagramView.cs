using System.Globalization;
using GuitarFlashcards.Core.Domain;
using Microsoft.Maui.Controls.Shapes;

namespace GuitarFlashcards.Views;

public sealed class FingeringDiagramView : ContentView
{
    private static readonly Color Ink = Color.FromArgb("#202A27");
    private static readonly Color Accent = Color.FromArgb("#146C67");
    private static readonly Color Rule = Color.FromArgb("#CFD5D0");
    private readonly GuitarFingering _fingering;
    private readonly int _selectedFinger;
    private readonly bool _interactive;

    public FingeringDiagramView(
        GuitarFingering fingering,
        int selectedFinger,
        bool interactive,
        Action<int, int>? onFretTapped = null,
        Action<int, StringNoteState>? onStateTapped = null)
    {
        _fingering = fingering;
        _selectedFinger = selectedFinger;
        _interactive = interactive;
        Content = BuildDiagram(onFretTapped, onStateTapped);
    }

    private View BuildDiagram(Action<int, int>? onFretTapped, Action<int, StringNoteState>? onStateTapped)
    {
        var grid = new Grid
        {
            Padding = new Thickness(8, 8),
            BackgroundColor = Colors.White,
            ColumnSpacing = 0,
            RowSpacing = 0,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto) },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) }
        };
        grid.ColumnDefinitions[0].Width = new GridLength(32);
        for (var index = 0; index < 6; index++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        grid.RowDefinitions[0].Height = new GridLength(27);
        grid.RowDefinitions[1].Height = new GridLength(35);
        for (var fret = 1; fret <= 5; fret++)
        {
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(34)));
        }

        var labels = new[] { "E", "A", "D", "G", "B", "E" };
        for (var stringIndex = 0; stringIndex < 6; stringIndex++)
        {
            var capturedStringIndex = stringIndex;
            grid.Add(new Label
            {
                Text = labels[stringIndex],
                FontAttributes = FontAttributes.Bold,
                TextColor = Ink,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }, stringIndex + 1, 0);

            var current = _fingering.Strings[stringIndex];
            var stateMarkers = new HorizontalStackLayout
            {
                Spacing = 1,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            if (_interactive)
            {
                stateMarkers.Add(CreateStateButton("O", current.State == StringNoteState.Open, () => onStateTapped?.Invoke(capturedStringIndex, StringNoteState.Open)));
                stateMarkers.Add(CreateStateButton("X", current.State == StringNoteState.Muted, () => onStateTapped?.Invoke(capturedStringIndex, StringNoteState.Muted)));
            }
            else
            {
                var stateText = current.State switch
                {
                    StringNoteState.Open => "O",
                    StringNoteState.Muted => "X",
                    _ => string.Empty
                };
                stateMarkers.Add(new Label
                {
                    Text = stateText,
                    FontSize = 13,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Ink,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                    WidthRequest = 34,
                    HeightRequest = 26
                });
            }
            grid.Add(stateMarkers, stringIndex + 1, 1);

            for (var fret = 1; fret <= 5; fret++)
            {
                var capturedFret = fret;
                var position = current.State == StringNoteState.Fretted && current.Fret == fret;
                var cell = new Grid
                {
                    BackgroundColor = Colors.Transparent,
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.Fill
                };
                cell.Children.Add(new Border
                {
                    Stroke = Rule,
                    StrokeThickness = fret == 1 ? 3 : 0.5,
                    BackgroundColor = Colors.Transparent,
                    InputTransparent = true
                });
                if (position)
                {
                    cell.Children.Add(new Border
                    {
                        Stroke = Colors.Transparent,
                        BackgroundColor = Accent,
                        StrokeShape = new Ellipse(),
                        WidthRequest = 27,
                        HeightRequest = 27,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center,
                        InputTransparent = true,
                        Content = new Label
                        {
                            Text = current.Finger?.ToString(CultureInfo.InvariantCulture),
                            FontSize = 13,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Colors.White,
                            HorizontalTextAlignment = TextAlignment.Center,
                            VerticalTextAlignment = TextAlignment.Center,
                            InputTransparent = true
                        }
                    });
                }

                if (_interactive)
                {
                    cell.GestureRecognizers.Add(new TapGestureRecognizer
                    {
                        Command = new Command(() => onFretTapped?.Invoke(capturedStringIndex, capturedFret))
                    });
                }
                else
                {
                    cell.InputTransparent = true;
                }
                grid.Add(cell, stringIndex + 1, fret + 1);
            }
        }

        for (var fret = 1; fret <= 5; fret++)
        {
            grid.Add(new Label
            {
                Text = fret.ToString(CultureInfo.InvariantCulture),
                FontSize = 12,
                TextColor = Ink,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }, 0, fret + 1);
        }

        return new Border
        {
            Stroke = Rule,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 3 },
            Padding = 1,
            Content = grid,
            HeightRequest = 250,
            HorizontalOptions = LayoutOptions.Fill
        };
    }

    private Button CreateStateButton(string text, bool selected, Action tapped) => new()
    {
        Text = text,
        FontSize = 13,
        FontAttributes = FontAttributes.Bold,
        TextColor = selected ? Colors.White : Ink,
        BackgroundColor = selected ? Accent : Colors.White,
        BorderColor = selected ? Accent : Rule,
        BorderWidth = 1,
        CornerRadius = 3,
        WidthRequest = 32,
        HeightRequest = 26,
        Padding = 0,
        IsEnabled = _interactive,
        Command = new Command(tapped)
    };
}