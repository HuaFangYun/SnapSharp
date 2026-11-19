using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NimCap.Screenshot.Models;
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NimCap.Screenshot.ViewModels;

public partial class EditorViewModel : ObservableObject
{
    private sealed record EditorSnapshot(BitmapSource Image, Annotation[] Annotations);
    private static Annotation? _annotationClipboard;
    public const int MaximumHistorySteps = 100;
    private readonly BoundedHistory<EditorSnapshot> _undo = new(MaximumHistorySteps);
    private readonly BoundedHistory<EditorSnapshot> _redo = new(MaximumHistorySteps);

    public EditorViewModel(BitmapSource image) => _image = image;

    [ObservableProperty] private BitmapSource _image;
    public ObservableCollection<Annotation> Annotations { get; } = [];
    [ObservableProperty] private AnnotationTool _selectedTool = AnnotationTool.Rectangle;
    [ObservableProperty] private Color _selectedColor = Color.FromRgb(232, 74, 60);
    [ObservableProperty] private double _strokeThickness = 4;
    [ObservableProperty] private Color? _fillColor;
    [ObservableProperty] private double _fontSize = 96;
    [ObservableProperty] private string _fontFamilyName = "Segoe UI";
    [ObservableProperty] private Color? _textOutlineColor = Colors.White;
    [ObservableProperty] private double _textOutlineThickness = 8;
    [ObservableProperty] private AnnotationStrokeStyle _strokeStyle;
    [ObservableProperty] private AnnotationArrowStyle _arrowStyle;
    [ObservableProperty] private double _effectStrength = 8;
    [ObservableProperty] private double _spotlightDarkness = 0.58;
    [ObservableProperty] private double _magnifierZoom = 3;
    [ObservableProperty] private int _nextNumber = 1;
    [ObservableProperty] private AnnotationNumberStyle _numberStyle;
    [ObservableProperty] private bool _continuousNumbering = true;
    [ObservableProperty] private bool _hasPreview;
    [ObservableProperty] private Annotation? _selectedAnnotation;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public AnnotationTool? ContinuationTool { get; private set; }

    partial void OnSelectedToolChanged(AnnotationTool value) => ContinuationTool = null;

    public void ActivateTool(AnnotationTool tool)
    {
        ContinuationTool = null;
        SelectedTool = tool;
    }

    public bool ResumeDrawing()
    {
        if (ContinuationTool is not { } tool) return false;
        SelectedAnnotation = null;
        SelectedTool = tool;
        return true;
    }

    public void Add(Annotation annotation)
    {
        SaveUndo();
        Annotations.Add(annotation);
        SelectedAnnotation = annotation;
        NotifyHistoryChanged();
    }

    public void CompleteAnnotation(Annotation annotation)
    {
        var tool = SelectedTool;
        Add(annotation);
        SelectedTool = AnnotationTool.Select;
        ContinuationTool = tool is AnnotationTool.Select or AnnotationTool.Eraser or AnnotationTool.Watermark
            ? null : tool;
    }

    public void Replace(Annotation oldValue, Annotation newValue)
    {
        var index = Annotations.IndexOf(oldValue);
        if (index < 0 || Equals(oldValue, newValue)) return;
        SaveUndo();
        Annotations[index] = newValue;
        SelectedAnnotation = newValue;
        NotifyHistoryChanged();
    }

    public void Remove(Annotation annotation)
    {
        if (!Annotations.Contains(annotation)) return;
        SaveUndo();
        Annotations.Remove(annotation);
        if (ReferenceEquals(SelectedAnnotation, annotation) || Equals(SelectedAnnotation, annotation)) SelectedAnnotation = null;
        NotifyHistoryChanged();
    }

    public void ApplySelectedStyle()
    {
        if (SelectedAnnotation is null) return;
        Replace(SelectedAnnotation, AnnotationEditing.Restyle(SelectedAnnotation,
            SelectedColor, StrokeThickness, FillColor, FontSize, StrokeStyle));
    }

    public void SetSelectedStrokeThickness(double thickness)
    {
        if (SelectedAnnotation is not { } selected) return;
        Replace(selected, AnnotationEditing.WithStrokeThickness(selected, thickness));
    }

    public void SetSelectedStrokeStyle(AnnotationStrokeStyle strokeStyle)
    {
        if (SelectedAnnotation is not { } selected) return;
        var updated = selected switch
        {
            ShapeAnnotation x => x with { StrokeStyle = strokeStyle },
            PenAnnotation x => x with { StrokeStyle = strokeStyle },
            PolygonAnnotation x => x with { StrokeStyle = strokeStyle },
            BubbleAnnotation x => x with { StrokeStyle = strokeStyle },
            _ => selected
        };
        Replace(selected, updated);
    }

    public void SetSelectedArrowStyle(AnnotationArrowStyle arrowStyle)
    {
        if (SelectedAnnotation is not ShapeAnnotation { Tool: AnnotationTool.Arrow } selected) return;
        Replace(selected, selected with { ArrowStyle = arrowStyle });
    }

    public void SetSelectedColor(Color color, bool recolorFill = true)
    {
        if (SelectedAnnotation is not { } selected) return;
        Replace(selected, AnnotationEditing.WithColor(selected, color, recolorFill));
    }

    public void SetSelectedFill(Color? fill)
    {
        if (SelectedAnnotation is not { } selected) return;
        var updated = selected switch
        {
            ShapeAnnotation x => x with { FillColor = fill },
            PolygonAnnotation x => x with { FillColor = fill },
            BubbleAnnotation x => x with { FillColor = fill ?? Colors.Transparent },
            _ => selected
        };
        Replace(selected, updated);
    }

    public void SetSelectedFontSize(double fontSize)
    {
        if (SelectedAnnotation is not { } selected) return;
        var updated = selected switch
        {
            TextAnnotation x => x with { FontSize = fontSize },
            WatermarkAnnotation x => x with { FontSize = fontSize },
            BubbleAnnotation x => x with { FontSize = fontSize },
            _ => selected
        };
        Replace(selected, updated);
    }

    public void SetSelectedTextOutline(Color? color, double thickness)
    {
        if (SelectedAnnotation is not { } selected) return;
        thickness = Math.Clamp(thickness, 0.5, 12);
        var updated = selected switch
        {
            TextAnnotation x => x with { TextOutlineColor = color, TextOutlineThickness = thickness },
            BubbleAnnotation x => x with { TextOutlineColor = color, TextOutlineThickness = thickness },
            _ => selected
        };
        Replace(selected, updated);
    }

    public void SetSelectedFontFamily(string fontFamilyName)
    {
        if (SelectedAnnotation is not { } selected || string.IsNullOrWhiteSpace(fontFamilyName)) return;
        var updated = selected switch
        {
            TextAnnotation x => x with { FontFamilyName = fontFamilyName },
            BubbleAnnotation x => x with { FontFamilyName = fontFamilyName },
            _ => selected
        };
        Replace(selected, updated);
    }

    public void ReplaceImage(BitmapSource image)
    {
        SaveUndo();
        Image = image;
        Annotations.Clear();
        SelectedAnnotation = null;
        NotifyHistoryChanged();
    }

    public void ApplyImageEdit(BitmapSource image, IEnumerable<Annotation> annotations)
    {
        SaveUndo();
        Image = image;
        Annotations.Clear();
        foreach (var annotation in annotations) Annotations.Add(annotation);
        SelectedAnnotation = null;
        NotifyHistoryChanged();
    }

    public void ReplaceAll(IEnumerable<Annotation> annotations, bool append = false)
    {
        SaveUndo();
        if (!append) Annotations.Clear();
        foreach (var annotation in annotations) Annotations.Add(annotation);
        SelectedAnnotation = Annotations.LastOrDefault();
        NotifyHistoryChanged();
    }

    public void MoveLayer(int offset)
    {
        if (SelectedAnnotation is null) return;
        var oldIndex = Annotations.IndexOf(SelectedAnnotation);
        var newIndex = Math.Clamp(oldIndex + offset, 0, Annotations.Count - 1);
        MoveLayerTo(newIndex);
    }

    public void MoveLayerTo(int index)
    {
        if (SelectedAnnotation is null) return;
        var oldIndex = Annotations.IndexOf(SelectedAnnotation);
        index = Math.Clamp(index, 0, Annotations.Count - 1);
        if (oldIndex < 0 || oldIndex == index) return;
        SaveUndo();
        Annotations.Move(oldIndex, index);
        NotifyHistoryChanged();
    }

    public void CopySelected() => _annotationClipboard = SelectedAnnotation;

    public void PasteSelected()
    {
        if (_annotationClipboard is null) return;
        Add(AnnotationEditing.Move(_annotationClipboard, new System.Windows.Vector(12, 12)));
        _annotationClipboard = SelectedAnnotation;
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        _redo.Push(Snapshot());
        Restore(_undo.Pop());
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        _undo.Push(Snapshot());
        Restore(_redo.Pop());
    }

    private void SaveUndo()
    {
        _undo.Push(Snapshot());
        _redo.Clear();
    }

    private EditorSnapshot Snapshot() => new(Image, Annotations.ToArray());

    private void Restore(EditorSnapshot state)
    {
        Image = state.Image;
        Annotations.Clear();
        foreach (var annotation in state.Annotations) Annotations.Add(annotation);
        SelectedAnnotation = null;
        NotifyHistoryChanged();
    }

    private void NotifyHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }
}
