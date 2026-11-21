using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using NimCap.Screenshot.Models;
using NimCap.Screenshot.Services;
using NimCap.Screenshot.Views;
using DataGrid = System.Windows.Controls.DataGrid;
using Panel = System.Windows.Controls.Panel;
using TabControl = System.Windows.Controls.TabControl;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using TextBox = System.Windows.Controls.TextBox;

namespace NimCap.Screenshot.WindowChecks;

internal static partial class Program
{
    private static void RecognitionWorkspaceChecks(BitmapSource source)
    {
        var window = new RecognitionWindow(source, new SettingsService(Path.Combine(Root, "workspace-settings.json")));
        try
        {
            var nav = (ListBox)window.FindName("ModeBox");
            var tabs = (TabControl)window.FindName("ResultTabs");
            var copy = (Button)window.FindName("CommonCopyButton");
            TextBox Editor(string name) => (TextBox)window.FindName(name);
            void Select(string mode, string tab)
            {
                nav.SelectedItem = nav.Items.OfType<ListBoxItem>().Single(i => i.Tag?.ToString() == mode);
                Require(tabs.Items.OfType<TabItem>().Count(t => t.Visibility == Visibility.Visible) == 1
                    && ReferenceEquals(tabs.SelectedItem, window.FindName(tab)), "Tool must expose only its own result panel: " + mode);
            }
            void Result(string text) => Invoke(window, "ShowToolResult", Lines(text));
            bool Edited(string property) => (bool)typeof(RecognitionWindow).GetProperty(property, PrivateInstance)!.GetValue(window)!;
            Select("OCR", "TextResultTab");
            Result("OCR source");
            Editor("TextEditor").AppendText(" edited");
            var ocrDocument = typeof(RecognitionWindow).GetField("_document", PrivateInstance)!.GetValue(window);
            Select("Summary", "SummaryResultTab");
            Require(!copy.IsEnabled && !Edited("CurrentToolHasEdits"), "An empty summary must not expose OCR output or inherit its edits.");
            Result("Summary original");
            Invoke(window, "SetToolText", "Summary output");
            Require(Editor("SummarySourceEditor").Text == "Summary original" && Editor("SummarySourceEditor").IsReadOnly
                && Editor("SummaryEditor").Text == "Summary output" && Editor("TextEditor").Text == "OCR source edited",
                "Summary must preserve its source and OCR's separate editable result.");
            Editor("SummaryEditor").AppendText(" edited");
            Select("Barcode", "BarcodeResultTab");
            Require(!copy.IsEnabled, "Barcode must not copy a previous tool's text.");
            Result("https://example.test/code");
            Require(Editor("BarcodeEditor").Text == "https://example.test/code" && copy.IsEnabled, "Decoded barcode was routed to the wrong editor.");
            Select("Formula", "FormulaResultTab");
            Require(!copy.IsEnabled, "Formula must start with its own empty output.");
            Result(@"x^2 + y^2 = z^2");
            Require(Editor("FormulaEditor").Text == @"x^2 + y^2 = z^2" && Editor("FormulaEditor").TextWrapping == TextWrapping.NoWrap,
                "Formula output must retain editable LaTeX.");
            Select("Table", "TableResultTab"); Result("Table cell");
            var cells = ((DataGrid)window.FindName("CellsGrid")).ItemsSource;
            Select("Document", "DocumentResultTab"); Result("Document source");
            Invoke(window, "ShowDocument", new object?[] { null });
            var documentFields = Editor("DocumentEditor").Text;
            foreach (var (mode, tab) in new[] { ("Cutout", "CutoutResultTab"), ("Watermark", "WatermarkResultTab") })
                Select(mode, tab);
            Select("OCR", "TextResultTab");
            Require(Editor("TextEditor").Text == "OCR source edited" && Edited("CurrentToolHasEdits")
                && ReferenceEquals(ocrDocument, typeof(RecognitionWindow).GetField("_document", PrivateInstance)!.GetValue(window)),
                "Returning to OCR must restore its text and source regions.");
            Select("Summary", "SummaryResultTab");
            Require(Editor("SummaryEditor").Text == "Summary output edited" && Edited("CurrentToolHasEdits") && copy.IsEnabled,
                "Returning to summary must preserve edits and enable its output actions.");
            Require(ReferenceEquals(typeof(RecognitionWindow).GetProperty("ActiveTextEditor", PrivateInstance)!.GetValue(window), Editor("SummaryEditor")),
                "Copy, export and undo must target the selected tool's editor.");
            Invoke(window, "SetBusy", true);
            Require(Editor("SummaryEditor").IsReadOnly && !copy.IsEnabled && !((Button)window.FindName("SummarySettingsButton")).IsEnabled,
                "Processing must lock summary editing, output and model configuration.");
            Invoke(window, "SetBusy", false);
            Invoke(window, "AcceptToolTextEdits");
            Require(!Edited("CurrentToolHasEdits") && Edited("HasEdits"), "Saving summary must leave OCR edits tracked.");
            Select("Table", "TableResultTab");
            Require(ReferenceEquals(cells, ((DataGrid)window.FindName("CellsGrid")).ItemsSource), "Other tools overwrote table cells.");
            Select("Document", "DocumentResultTab");
            Require(Editor("DocumentEditor").Text == documentFields, "Other tools overwrote document fields.");
            Select("OCR", "TextResultTab"); Invoke(window, "AcceptToolTextEdits");
            var rotate = ((Panel)window.FindName("ImageTransformTools")).Children.OfType<Button>().Single(b => b.Tag?.ToString() == "Right");
            rotate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            foreach (var (mode, tab, editor) in new[] { ("OCR", "TextResultTab", "TextEditor"), ("Summary", "SummaryResultTab", "SummaryEditor"),
                ("Barcode", "BarcodeResultTab", "BarcodeEditor"), ("Formula", "FormulaResultTab", "FormulaEditor"), ("Document", "DocumentResultTab", "DocumentEditor") })
            {
                Select(mode, tab);
                Require(Editor(editor).Text.Length == 0 && !copy.IsEnabled
                    && typeof(RecognitionWindow).GetField("_document", PrivateInstance)!.GetValue(window) is null,
                    "Changing image orientation must invalidate all cached tool outputs: " + mode);
            }
            Require(Editor("SummarySourceEditor").Text.Length == 0 && ((DataGrid)window.FindName("CellsGrid")).Items.Count <= 1,
                "Image transformation retained summary source or table cells.");
            Console.WriteLine("PASS: distinct tool panels, result routing, retained edits, action targets, busy state and image invalidation.");
        }
        finally
        {
            Invoke(window, "ResetToolResults");
            typeof(RecognitionWindow).GetField("_baseline", PrivateInstance)!.SetValue(window, typeof(RecognitionWindow).GetField("_sourceText", PrivateInstance)!.GetValue(window));
            typeof(RecognitionWindow).GetField("_identityBaseline", PrivateInstance)!.SetValue(window, EditorSnapshot("IdentitySnapshot"));
            typeof(RecognitionWindow).GetField("_tableBaseline", PrivateInstance)!.SetValue(window, EditorSnapshot("TableSnapshot"));
            window.Close();
            object? EditorSnapshot(string name) => typeof(RecognitionWindow).GetProperty(name, PrivateInstance)!.GetValue(window);
        }
    }
}
