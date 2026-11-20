using System.Windows;
using NimCap.Screenshot.Models;
using NimCap.Screenshot.Services;
using L = NimCap.Screenshot.Localization.UiText;
using Size = System.Windows.Size;

namespace NimCap.Screenshot.Views;

public partial class SelectionWindow
{
    private CancellationTokenSource? _summaryOperation;
    private string _summarySettingsCategory = "Shield";
    internal Func<string, AppSettings, CancellationToken, Task<string>> SummarizeSelectionText { get; set; }
        = LocalTranslationService.Summarize;
    public event Action<string>? SettingsRequested;

    private async Task SummarizeInPlace()
    {
        if (!_editing || _editor is null || _scrollingSelection || !RedactCaptureButton.IsEnabled) return;
        if (_summaryOperation is not null) { SummaryResultPanel.Visibility = Visibility.Visible; return; }
        if (_textRecognitionActive && _textRecognitionTask?.IsCompleted != false &&
            string.IsNullOrWhiteSpace(TextSelectionLayer.Selection?.Text)) ExitTextRecognition();
        using var operation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _summaryOperation = operation;
        SummaryResultPanel.Visibility = Visibility.Visible;
        SummaryResultText.Clear();
        SummaryProgressText.Text = L.Get("CaptureTextRecognizing");
        SummaryProgressBar.Visibility = Visibility.Visible;
        ConfigureSummaryButton.Visibility = Visibility.Collapsed;
        CopySummaryButton.IsEnabled = RetrySummaryButton.IsEnabled = false;
        PositionSummaryPanel();
        try
        {
            // A summary reuses the current OCR result, including an in-flight recognition.
            // Starting directly with J does not replace the user's clipboard with source text.
            if (!_textRecognitionActive) _textRecognitionTask = LoadTextInPlace(copyAll: false);
            if (_textRecognitionTask is { } recognition) await recognition;
            if (!IsCurrent()) return;
            operation.Token.ThrowIfCancellationRequested();
            var text = TextSelectionLayer.Selection?.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                SummaryProgressText.Text = TextRecognitionStatus.Text;
                if (TextSelectionLayer.Selection is null)
                {
                    _summarySettingsCategory = "ScanText";
                    ConfigureSummaryButton.Visibility = Visibility.Visible;
                }
                return;
            }
            _summarySettingsCategory = "Shield";
            SummaryProgressText.Text = L.Get("CaptureSummaryProcessing");
            var summary = await SummarizeSelectionText(text, PrecisionPreferences, operation.Token);
            if (!IsCurrent()) return;
            operation.Token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(summary)) throw new InvalidDataException(L.Get("CaptureSummaryEmptyResult"));
            SummaryResultText.Text = summary;
            CopySummaryButton.IsEnabled = true;
            SummaryProgressText.Text = L.Get("CaptureSummaryComplete");
        }
        catch (OperationCanceledException)
        {
            if (IsCurrent()) SummaryProgressText.Text = L.Get("CaptureSummaryTimedOut");
        }
        catch (Exception ex)
        {
            if (IsCurrent())
            {
                SummaryProgressText.Text = L.Get("ProcessingFailed") + ex.GetBaseException().Message;
                ConfigureSummaryButton.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            if (IsCurrent())
            {
                _summaryOperation = null;
                SummaryProgressBar.Visibility = Visibility.Collapsed;
                RetrySummaryButton.IsEnabled = true;
                PositionSummaryPanel();
            }
        }

        bool IsCurrent() => ReferenceEquals(_summaryOperation, operation);
    }

    private void PositionSummaryPanel()
    {
        var viewport = ChromeViewport(new Point(_selection.X + _selection.Width / 2, _selection.Y));
        SummaryResultPanel.Width = Math.Max(120, Math.Min(380, viewport.Width - 24));
        SummaryResultText.MaxHeight = Math.Max(90, Math.Min(270, viewport.Height - 230));
        SummaryResultPanel.Measure(new Size(SummaryResultPanel.Width, viewport.Height));
        var size = SummaryResultPanel.DesiredSize;
        var x = _selection.Right + 12;
        var y = _selection.Top;
        if (x + size.Width > viewport.Right - 12)
        {
            x = _selection.Left - size.Width - 12;
            if (x < viewport.Left + 12)
            {
                x = viewport.Right - size.Width - 12;
                if (_selection.Bottom + size.Height + 24 <= viewport.Bottom) y = _selection.Bottom + 12;
            }
        }
        var maxX = Math.Max(viewport.Left + 12, viewport.Right - size.Width - 12);
        var maxY = Math.Max(viewport.Top + 12, viewport.Bottom - size.Height - 12);
        CanvasPosition(SummaryResultPanel, Math.Clamp(x, viewport.Left + 12, maxX),
            Math.Clamp(y, Math.Min(viewport.Top + 110, maxY), maxY));
    }

    private void CloseSummary()
    {
        _summaryOperation?.Cancel();
        _summaryOperation = null;
        SummaryResultPanel.Visibility = Visibility.Collapsed;
        SummaryResultText.Clear();
        CopySummaryButton.IsEnabled = false;
    }

    private void CloseSummary_Click(object sender, RoutedEventArgs e) => CloseSummary();

    private void CopySummary_Click(object sender, RoutedEventArgs e)
    {
        if (!CopySummaryButton.IsEnabled || string.IsNullOrWhiteSpace(SummaryResultText.Text)) return;
        try
        {
            CopyRecognizedText(SummaryResultText.Text);
            SummaryProgressText.Text = L.Get("CaptureSummaryCopied");
        }
        catch (System.Runtime.InteropServices.ExternalException) { SummaryProgressText.Text = L.Get("CopyFailed"); }
    }

    private void ConfigureSummary_Click(object sender, RoutedEventArgs e)
    {
        if (_summaryOperation is not null) return;
        SettingsRequested?.Invoke(_summarySettingsCategory);
    }
}
