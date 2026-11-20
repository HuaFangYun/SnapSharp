using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using NimCap.Screenshot.Models;
using NimCap.Screenshot.Services;
using L = NimCap.Screenshot.Localization.UiText;
using Size = System.Windows.Size;

namespace NimCap.Screenshot.Views;

public partial class SelectionWindow
{
    private bool _textRecognitionActive;
    private CancellationTokenSource? _textRecognitionOperation;
    private Task? _textRecognitionTask;
    private Visibility _textPreviousSecondaryVisibility;
    private bool _textPreviousHitTest, _textPreviousMoving;
    internal Func<BitmapSource, AppSettings, CancellationToken, Task<RecognitionDocument>> RecognizeSelectionText { get; set; }
        = ScreenWordService.RecognizeImage;
    internal Action<string> CopyRecognizedText { get; set; } = OutputActions.CopyText;

    private void InitializeTextRecognition()
    {
        TextSelectionLayer.CopyRequested += CopyTextInPlace;
        Closed += (_, _) => ExitTextRecognition();
    }

    private Task RecognizeTextInPlace() => _textRecognitionTask = LoadTextInPlace(copyAll: true);

    private async Task LoadTextInPlace(bool copyAll)
    {
        if (!_editing || _editor is null || _scrollingSelection || _textRecognitionActive || !RedactCaptureButton.IsEnabled) return;
        // Take the same image (including masks and annotations) that saving would produce.
        var image = RenderCaptureImage();
        using var operation = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(PrecisionPreferences.RecognitionTimeoutSeconds, 15, 120)));
        _textRecognitionOperation = operation;
        EnterTextRecognition();
        try
        {
            var document = await RecognizeSelectionText(image, PrecisionPreferences, operation.Token);
            if (!IsCurrent()) return;
            operation.Token.ThrowIfCancellationRequested();
            TextSelectionLayer.SetDocument(document, new Size(image.PixelWidth, image.PixelHeight));
            var hasText = !string.IsNullOrWhiteSpace(TextSelectionLayer.Selection?.Text);
            CopyAllRecognizedTextButton.IsEnabled = hasText;
            TextRecognitionStatus.Text = L.Get(hasText ? "CaptureTextSelectionHint" : "CaptureTextNoText");
            if (hasText && copyAll) CopyTextInPlace(TextSelectionLayer.Selection!.Text, allText: true);
            PositionRecognitionActions();
            TextSelectionLayer.Focus();
        }
        catch (OperationCanceledException)
        {
            if (IsCurrent()) TextRecognitionStatus.Text = L.Get("CaptureTextTimedOut");
        }
        catch (Exception ex)
        {
            if (IsCurrent()) TextRecognitionStatus.Text = L.Get("ProcessingFailed") + ex.GetBaseException().Message;
        }
        finally
        {
            if (ReferenceEquals(_textRecognitionOperation, operation)) _textRecognitionOperation = null;
        }

        bool IsCurrent() => _textRecognitionActive && ReferenceEquals(_textRecognitionOperation, operation);
    }

    private void EnterTextRecognition()
    {
        _textPreviousSecondaryVisibility = SecondaryToolPanel.Visibility;
        _textPreviousHitTest = AnnotationView.IsHitTestVisible;
        _textPreviousMoving = _moving;
        _textRecognitionActive = true;
        _moving = false;
        AnnotationView.IsHitTestVisible = false;
        ToolPanel.Visibility = SecondaryToolPanel.Visibility = Handles.Visibility = Visibility.Collapsed;
        CaptureRecognitionCommands.Visibility = Visibility.Collapsed;
        TextSelectionCommands.Visibility = TextRecognitionStatus.Visibility = Visibility.Visible;
        CopyAllRecognizedTextButton.IsEnabled = false;
        TextRecognitionStatus.Text = L.Get("CaptureTextRecognizing");
        PositionTextSelectionLayer();
        TextSelectionLayer.Visibility = Visibility.Visible;
        PositionRecognitionActions();
        UpdatePrecisionGuides(_lastPointer);
        TextSelectionLayer.Focus();
    }

    private void PositionTextSelectionLayer()
    {
        TextSelectionLayer.Width = AnnotationView.Width;
        TextSelectionLayer.Height = AnnotationView.Height;
        TextSelectionLayer.RenderTransform = AnnotationView.RenderTransform;
        TextSelectionLayer.RenderTransformOrigin = AnnotationView.RenderTransformOrigin;
        TextSelectionLayer.Clip = AnnotationView.Clip;
    }

    private void ExitTextRecognition()
    {
        if (!_textRecognitionActive) { CloseSummary(); return; }
        _textRecognitionActive = false;
        CloseSummary();
        _textRecognitionOperation?.Cancel();
        _textRecognitionOperation = null;
        _textRecognitionTask = null;
        TextSelectionLayer.SetDocument(null, new Size());
        TextSelectionLayer.Visibility = Visibility.Collapsed;
        TextSelectionCommands.Visibility = TextRecognitionStatus.Visibility = Visibility.Collapsed;
        CaptureRecognitionCommands.Visibility = Visibility.Visible;
        AnnotationView.IsHitTestVisible = _textPreviousHitTest;
        _moving = _textPreviousMoving;
        if (_editing)
        {
            ToolPanel.Visibility = Handles.Visibility = Visibility.Visible;
            SecondaryToolPanel.Visibility = _textPreviousSecondaryVisibility;
            PositionChrome();
        }
        UpdatePrecisionGuides(_lastPointer);
        Focus();
    }

    private void HandleTextRecognitionKey(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key != Key.Escape && e.OriginalSource is DependencyObject source && SummaryResultPanel.IsAncestorOf(source) &&
            source is System.Windows.Controls.Primitives.TextBoxBase) return;
        e.Handled = ProcessTextRecognitionKey(key, Keyboard.Modifiers, e.IsRepeat);
    }

    internal bool ProcessTextRecognitionKey(Key key, ModifierKeys modifiers, bool isRepeat = false)
    {
        if (key == Key.Escape) ExitTextRecognition();
        else if (modifiers == ModifierKeys.Control && key == Key.A) TextSelectionLayer.SelectAll();
        else if (modifiers == ModifierKeys.Control && key == Key.C && !isRepeat) TextSelectionLayer.CopySelection();
        else if (modifiers == ModifierKeys.None && key == Key.O && !isRepeat) CopyAllRecognizedText_Click(this, new RoutedEventArgs());
        else if (modifiers == ModifierKeys.None && key == Key.J && !isRepeat) Summary_Click(this, new RoutedEventArgs());
        // Tab and Enter/Space continue to work on the two action buttons.
        else if (key is Key.Tab or Key.Enter or Key.Space) return false;
        return true;
    }

    private void CopyTextInPlace(string text) => CopyTextInPlace(text, allText: false);

    private void CopyTextInPlace(string text, bool allText)
    {
        try
        {
            CopyRecognizedText(text);
            TextRecognitionStatus.Text = L.Get(allText ? "CaptureTextAllCopied" : "CaptureTextCopied");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            TextRecognitionStatus.Text = L.Get("CopyFailed");
        }
        TextSelectionLayer.Focus();
    }

    private void CopyAllRecognizedText_Click(object sender, RoutedEventArgs e)
    {
        if (TextSelectionLayer.Selection?.Text is { Length: > 0 } text) CopyTextInPlace(text, allText: true);
    }
    private void ExitTextRecognition_Click(object sender, RoutedEventArgs e) => ExitTextRecognition();
}
