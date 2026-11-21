using NimCap.Screenshot.Models;
using NimCap.Screenshot.Services;
using System.Windows;
using System.Windows.Controls;
using TextBox = System.Windows.Controls.TextBox;
using L = NimCap.Screenshot.Localization.UiText;

namespace NimCap.Screenshot.Views;

public partial class RecognitionWindow
{
    private readonly Dictionary<string, RecognitionDocument?> _toolDocuments = new();
    private readonly Dictionary<string, string> _toolStatuses = new();
    private readonly Dictionary<string, string> _toolTextBaselines = new();
    private string? _workspaceMode;

    private TextBox ActiveTextEditor => Mode switch
    {
        "Summary" => SummaryEditor, "Barcode" => BarcodeEditor, "Formula" => FormulaEditor, _ => TextEditor
    };

    private bool HasToolTextEdits => SummaryEditor.Text != _toolTextBaselines.GetValueOrDefault("Summary", "")
        || BarcodeEditor.Text != _toolTextBaselines.GetValueOrDefault("Barcode", "")
        || FormulaEditor.Text != _toolTextBaselines.GetValueOrDefault("Formula", "");

    private bool CurrentToolHasEdits => Mode switch
    {
        "OCR" => _sourceText != _baseline,
        "Table" => TableSnapshot != _tableBaseline,
        "Document" => IdentitySnapshot != _identityBaseline,
        "Summary" or "Barcode" or "Formula" => ActiveTextEditor.Text != _toolTextBaselines.GetValueOrDefault(Mode, ""),
        _ => false
    };

    private string ToolHint => L.Get(Mode switch
    {
        "OCR" => "RecognitionOcrHint", "Summary" => "RecognitionSummaryHint",
        "Barcode" => "RecognitionBarcodeHint", "Formula" => "RecognitionFormulaHint",
        "Document" => "DocumentFieldsHint", "Table" => "RecognitionTableHint",
        _ => "RecognitionChooseTool"
    });

    private void UpdateToolWorkspace()
    {
        if (_workspaceMode != Mode)
        {
            if (_workspaceMode is not null)
            {
                _toolDocuments[_workspaceMode] = _document;
                _toolStatuses[_workspaceMode] = Status.Text;
            }
            _document = _toolDocuments.GetValueOrDefault(Mode);
            _workspaceMode = Mode;
            Status.Text = _toolStatuses.GetValueOrDefault(Mode, ToolHint);
        }
        var selected = Mode switch
        {
            "Summary" => SummaryResultTab, "Document" => DocumentResultTab, "Table" => TableResultTab,
            "Barcode" => BarcodeResultTab, "Formula" => FormulaResultTab, "Cutout" => CutoutResultTab,
            "Watermark" => WatermarkResultTab, _ => TextResultTab
        };
        foreach (var tab in ResultTabs.Items.OfType<TabItem>())
            tab.Visibility = ReferenceEquals(tab, selected) ? Visibility.Visible : Visibility.Collapsed;
        ResultTabs.SelectedItem = selected;
        RunButton.Content = L.Get(Mode switch
        {
            "Summary" => "CaptureSummary", "Watermark" => "WatermarkRemoval",
            "Cutout" => "RecognitionCutoutCompact", _ => "RecognitionRunCompact"
        });
        RefreshCommonActions();
    }

    private void ShowToolResult(RecognitionDocument document)
    {
        _document = document;
        SetPicture(_image);
        if (Mode == "OCR") { SetSourceText(document.Text); _baseline = _sourceText; ResetTextUndo(TextEditor); }
        else if (Mode is "Barcode" or "Formula") SetToolText(document.Text);
        else if (Mode == "Summary")
        {
            SummarySourceEditor.Text = document.Text;
            SetToolText("");
        }
        if (Mode == "Table")
        {
            _cells = new System.Collections.ObjectModel.ObservableCollection<RecognizedCell>(_pluginCells ?? RecognitionExport.Draft(document));
            CellsGrid.ItemsSource = _cells; _tableBaseline = TableSnapshot;
        }
        _pluginCells = null;
        RefreshCommonActions();
    }

    private static void ResetTextUndo(TextBox editor)
    {
        editor.IsUndoEnabled = false;
        editor.IsUndoEnabled = true;
    }

    private void SetToolText(string text)
    {
        ActiveTextEditor.Text = text;
        ResetTextUndo(ActiveTextEditor);
        _toolTextBaselines[Mode] = text;
        RefreshCommonActions();
    }

    private void AcceptToolTextEdits()
    {
        if (Mode == "OCR")
        {
            if (CurrentTextView == "Source") _baseline = _sourceText;
        }
        else _toolTextBaselines[Mode] = ActiveTextEditor.Text;
    }

    private void ResetToolResults()
    {
        _toolDocuments.Clear(); _toolStatuses.Clear(); _toolTextBaselines.Clear();
        ResetTextUndo(TextEditor); ResetTextUndo(DocumentEditor);
        SummaryEditor.Clear(); ResetTextUndo(SummaryEditor); SummarySourceEditor.Clear();
        BarcodeEditor.Clear(); ResetTextUndo(BarcodeEditor); FormulaEditor.Clear(); ResetTextUndo(FormulaEditor);
    }
}
