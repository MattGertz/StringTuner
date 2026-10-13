using System.Globalization;
using GuitarFlashcards.Core.Domain;
using Microsoft.Maui.Controls.Shapes;

namespace GuitarFlashcards.Views;

public sealed class FingeringDiagramView : ContentView
{
    private const int MaximumStandardFrets = 24;
    private const double FretRowHeight = 34;
    private const double VisibleFretHeight = FretRowHeight * FretboardScrollPosition.VisibleFretCount;
    private static readonly Color Ink = Color.FromArgb("#202A27");
    private static readonly Color Accent = Color.FromArgb("#146C67");
    private static readonly Color Rule = Color.FromArgb("#CFD5D0");
    private readonly GuitarFingering _fingering;
    private readonly int _selectedFinger;
    private readonly bool _interactive;
    private readonly MouseWheelScrollView _fretScrollView;

    public FingeringDiagramView(
        GuitarFingering fingering,
        int selectedFinger,
        bool interactive,
        Action<int, int>? onFretTapped = null,
        Action<int, StringNoteState>? onStateTapped = null,
        double initialScrollY = 0,
        bool autoScrollToChord = false,
        bool showScrollBar = true)
    {
        _fingering = fingering;
        _selectedFinger = selectedFinger;
        _interactive = interactive;
        _fretScrollView = new MouseWheelScrollView
        {
            Orientation = ScrollOrientation.Vertical,
            VerticalScrollBarVisibility = showScrollBar ? ScrollBarVisibility.Always : ScrollBarVisibility.Never,
            HeightRequest = VisibleFretHeight
        };
        Content = BuildDiagram(onFretTapped, onStateTapped);
        var targetScrollY = autoScrollToChord ? GetPracticeScrollOffset(fingering) : initialScrollY;
        if (targetScrollY > 0)
        {
            Loaded += async (_, _) => await _fretScrollView.ScrollToAsync(0, targetScrollY, animated: false);
        }
    }

    public double CurrentScrollY => _fretScrollView.ScrollY;

    private View BuildDiagram(Action<int, int>? onFretTapped, Action<int, StringNoteState>? onStateTapped)
    {
        var diagram = new Grid
        {
            Padding = new Thickness(8, 8),
            BackgroundColor = Colors.White,
            ColumnSpacing = 0,
            RowSpacing = 0,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto) },
            RowDefinitions =
            {
                new RowDefinition(new GridLength(27)),
                new RowDefinition(new GridLength(35)),
                new RowDefinition(new GridLength(VisibleFretHeight))
            }
        };
        diagram.ColumnDefinitions[0].Width = new GridLength(32);
        for (var index = 0; index < 6; index++)
        {
            diagram.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        var labels = new[] { "E", "A", "D", "G", "B", "E" };
        for (var stringIndex = 0; stringIndex < 6; stringIndex++)
        {
            var capturedStringIndex = stringIndex;
            diagram.Add(new Label
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
            diagram.Add(stateMarkers, stringIndex + 1, 1);
        }

        var highestFret = Math.Max(MaximumStandardFrets,
            _fingering.Strings.Where(item => item.State == StringNoteState.Fretted).Select(item => item.Fret ?? 0).DefaultIfEmpty(0).Max());
        var fretGrid = new Grid
        {
            ColumnSpacing = 0,
            RowSpacing = 0,
            ColumnDefinitions = { new ColumnDefinition(new GridLength(32)) },
            VerticalOptions = LayoutOptions.Start
        };
        for (var index = 0; index < 6; index++)
        {
            fretGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        for (var fret = 1; fret <= highestFret; fret++)
        {
            var gridRow = fret - 1;
            fretGrid.RowDefinitions.Add(new RowDefinition(new GridLength(FretRowHeight)));
            fretGrid.Add(new Label
            {
                Text = fret.ToString(CultureInfo.InvariantCulture),
                FontSize = 12,
                TextColor = Ink,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }, 0, gridRow);

            for (var stringIndex = 0; stringIndex < 6; stringIndex++)
            {
                var capturedStringIndex = stringIndex;
                var capturedFret = fret;
                var current = _fingering.Strings[stringIndex];
                var position = current.State == StringNoteState.Fretted && current.Fret == fret;
                var cell = new Grid { BackgroundColor = Colors.Transparent };
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

                fretGrid.Add(cell, stringIndex + 1, gridRow);
            }
        }

        var inferredBarres = BarreInference.Infer(_fingering);
        if (inferredBarres.Count > 0)
        {
            var barreOverlay = new GraphicsView
            {
                Drawable = new BarreOverlayDrawable(inferredBarres),
                InputTransparent = true,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            };
            fretGrid.Add(barreOverlay, 0, 0);
            Grid.SetColumnSpan(barreOverlay, fretGrid.ColumnDefinitions.Count);
            Grid.SetRowSpan(barreOverlay, highestFret);
        }

        _fretScrollView.Content = fretGrid;
        diagram.Add(_fretScrollView, 0, 2);
        Grid.SetColumnSpan(_fretScrollView, diagram.ColumnDefinitions.Count);

        return new Border
        {
            Stroke = Rule,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 3 },
            Padding = 1,
            Content = diagram,
            HeightRequest = 250,
            HorizontalOptions = LayoutOptions.Fill
        };
    }

    private static double GetPracticeScrollOffset(GuitarFingering fingering) =>
        (FretboardScrollPosition.FirstVisibleFret(fingering) - 1) * FretRowHeight;

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

    private sealed class BarreOverlayDrawable(IReadOnlyList<BarreSegment> segments) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            const float stringLabelWidth = 32;
            const float noteLabelHeight = 27;
            const float openMutedRowHeight = 35;
            const float fretRowHeight = 34;
            const float markerRadius = 13.5f;
            var stringWidth = (dirtyRect.Width - stringLabelWidth) / 6;
            canvas.StrokeColor = Color.FromArgb("#0E514D");
            canvas.StrokeSize = 4;
            canvas.StrokeLineCap = LineCap.Round;

            foreach (var segment in segments)
            {
                var leftCenter = stringLabelWidth + (segment.StartStringIndex + 0.5f) * stringWidth;
                var rightCenter = stringLabelWidth + (segment.EndStringIndex + 0.5f) * stringWidth;
                var y = noteLabelHeight + openMutedRowHeight + (segment.Fret - 0.5f) * fretRowHeight;
                var start = leftCenter + markerRadius;
                var end = rightCenter - markerRadius;
                if (end > start)
                {
                    canvas.DrawLine(start, y, end, y);
                }
            }
        }
    }
}