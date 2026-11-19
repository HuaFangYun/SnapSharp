using L = SnapSharp.Localization.UiText;
using SnapSharp.Models;
using SnapSharp.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Button = System.Windows.Controls.Button;

namespace SnapSharp.Services;

/// <summary>Shared screenshot/pin action. One undo restores both image and annotations.</summary>
internal sealed class SensitiveRedactionAction
{
    private CancellationTokenSource? _pending;
    private readonly Func<BitmapSource, AppSettings, CancellationToken, Task<RecognitionDocument>> _recognize;

    internal SensitiveRedactionAction(Func<BitmapSource, AppSettings, CancellationToken, Task<RecognitionDocument>>? recognize = null) =>
        _recognize = recognize ?? ((image, settings, token) => new LocalRecognitionService().Recognize(image, settings, token));

    public void Cancel() => _pending?.Cancel();

    public async Task Run(Window owner, Button button, EditorViewModel editor, AppSettings settings, Func<bool> isCurrent)
    {
        if (_pending is not null) return;
        using var cancellation = new CancellationTokenSource();
        _pending = cancellation;
        var oldTooltip = button.ToolTip;
        var oldCursor = button.Cursor;
        var oldContent = button.Content;
        button.IsEnabled = false;
        button.ToolTip = L.Get("RedactionProcessing");
        button.Content = new TextBlock { Text = "…", FontSize = 20, VerticalAlignment = VerticalAlignment.Center };
        button.Cursor = System.Windows.Input.Cursors.Wait;
        void Closed(object? sender, EventArgs args) => cancellation.Cancel();
        owner.Closed += Closed;
        try
        {
            var source = editor.Image;
            var annotations = editor.Annotations.ToArray();
            var images = new SkiaImageService();
            var rendered = images.Render(source, annotations);
            var result = await Task.Run(async () =>
            {
                var document = await _recognize(rendered, settings, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                var regions = SensitiveTextDetection.FindRegions(document, rendered.PixelWidth, rendered.PixelHeight);
                return (Regions: regions, Image: regions.Count > 0 ? Render(images, rendered, regions) : null);
            }, cancellation.Token);
            if (cancellation.IsCancellationRequested || !isCurrent()) return;
            if (!ReferenceEquals(editor.Image, source) || !editor.Annotations.SequenceEqual(annotations))
            {
                MessageBox.Show(owner, L.Get("RedactionImageChanged"), L.Get("OneClickRedact"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (result.Image is null)
            {
                MessageBox.Show(owner, L.Get("RedactionNoMatches"), L.Get("OneClickRedact"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            editor.ApplyImageEdit(result.Image, []);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested && isCurrent())
                MessageBox.Show(owner, L.Format("RedactionFailed", ex.Message), L.Get("OneClickRedact"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            owner.Closed -= Closed;
            button.ToolTip = oldTooltip;
            button.Cursor = oldCursor;
            button.Content = oldContent;
            button.IsEnabled = true;
            _pending = null;
        }
    }

    internal static BitmapSource Render(SkiaImageService images, BitmapSource source, IReadOnlyList<Rect> regions) =>
        images.Render(source, regions.Select(r => new RegionEffectAnnotation(AnnotationTool.Mosaic,
            r, Math.Clamp(r.Height * .8, 12, 40))));
}
