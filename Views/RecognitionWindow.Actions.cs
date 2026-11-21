using NimCap.Screenshot.Models;
using NimCap.Screenshot.Services;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;

namespace NimCap.Screenshot.Views;

public partial class RecognitionWindow
{
    private void ImageTools_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_watermarkReady) return;
        UpdateWatermarkMode();
    }

    private string CommonOutputMode => Mode is "Watermark" or "Cutout" ? Mode
        : ReferenceEquals(ResultTabs.SelectedItem, DocumentResultTab) ? "Document"
        : ReferenceEquals(ResultTabs.SelectedItem, TableResultTab) ? "Table" : "Text";

    private void RefreshCommonActions()
    {
        if (CommonCopyButton is null) return;
        var mode = CommonOutputMode;
        var hasResult = mode switch
        {
            "Watermark" => _watermarkResult is not null,
            "Cutout" => _cutout is not null,
            "Document" => _identityDocument is not null,
            "Table" => _cells.Count > 0,
            _ => !string.IsNullOrEmpty(ActiveTextEditor.Text)
        };
        CommonCopyButton.IsEnabled = CommonExportButton.IsEnabled = !_busy && hasResult;
        CommonPinButton.IsEnabled = !_busy && hasResult && mode is "Watermark" or "Cutout";
        CommonUndoButton.IsEnabled = !_busy && (mode switch
        {
            "Watermark" => _watermarkResult is not null,
            "Cutout" => _cutout is not null,
            "Document" => DocumentEditor.CanUndo,
            "Text" => ActiveTextEditor.CanUndo,
            _ => false
        });
    }

    private void CommonUndo_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        switch (CommonOutputMode)
        {
            case "Watermark": WatermarkUndoRepair_Click(sender, e); break;
            case "Cutout": CutoutUndo_Click(sender, e); break;
            case "Document" when DocumentEditor.CanUndo: DocumentEditor.Undo(); break;
            case "Text" when ActiveTextEditor.CanUndo: ActiveTextEditor.Undo(); break;
        }
        RefreshCommonActions();
    }

    private void CommonCopy_Click(object sender, RoutedEventArgs e)
    {
        if (!CommonCopyButton.IsEnabled || _busy) return;
        switch (CommonOutputMode)
        {
            case "Watermark": WatermarkOutput_Click(new Button { Tag = "Copy" }, e); break;
            case "Cutout": CutoutOutput_Click(new Button { Tag = "Copy" }, e); break;
            case "Document": CopyDocument_Click(sender, e); break;
            case "Table":
                try
                {
                    if (!CellsGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !CellsGrid.CommitEdit(DataGridEditingUnit.Row, true)) return;
                    OutputActions.CopyText(Encoding.UTF8.GetString(RecognitionExport.Csv(_cells)));
                    _tableBaseline = TableSnapshot;
                }
                catch (Exception ex) { Status.Text = ex.Message; }
                break;
            default: CopyText_Click(sender, e); break;
        }
    }

    private void CommonExport_Click(object sender, RoutedEventArgs e)
    {
        if (!CommonExportButton.IsEnabled || _busy) return;
        switch (CommonOutputMode)
        {
            case "Watermark": WatermarkOutput_Click(new Button { Tag = "Save" }, e); break;
            case "Cutout": CutoutOutput_Click(new Button { Tag = "Save" }, e); break;
            case "Document": ExportDocument_Click(sender, e); break;
            case "Table":
                CommonExportButton.ContextMenu.PlacementTarget = CommonExportButton;
                CommonExportButton.ContextMenu.IsOpen = true;
                break;
            default: ExportText_Click(sender, e); break;
        }
    }

    private void CommonPin_Click(object sender, RoutedEventArgs e)
    {
        if (!CommonPinButton.IsEnabled || _busy) return;
        if (Mode == "Watermark") WatermarkOutput_Click(new Button { Tag = "Pin" }, e);
        else if (Mode == "Cutout") CutoutOutput_Click(new Button { Tag = "Pin" }, e);
    }

    private void CommonDocument_Changed(object sender, TextChangedEventArgs e) => RefreshCommonActions();
}
