using System.Windows.Media.Imaging;

namespace SnapSharp.Plugins;

public enum PluginCapability
{
    ImageProcessor,
    Recognizer,
    Output,
    WorkflowAction
}

public sealed record PluginMetadata(
    string Id,
    string Name,
    Version Version,
    string Publisher,
    IReadOnlyList<PluginCapability> Capabilities,
    string? Description = null);

public sealed record PluginExecutionContext(
    IServiceProvider Services,
    IReadOnlyDictionary<string, object?> Parameters);

public sealed record RecognitionResult(
    string Kind,
    string Text,
    IReadOnlyDictionary<string, object?> Metadata);

public sealed record PluginOutputResult(bool Succeeded, string? Location = null, string? Message = null);

public interface ISnapSharpPlugin
{
    PluginMetadata Metadata { get; }
    void Register(IPluginRegistry registry);
}

public interface IPluginRegistry
{
    void RegisterImageProcessor(IImageProcessorPlugin plugin);
    void RegisterRecognizer(IRecognitionPlugin plugin);
    void RegisterOutput(IOutputPlugin plugin);
    void RegisterWorkflowAction(IWorkflowActionPlugin plugin);
}

public interface IImageProcessorPlugin
{
    string Id { get; }
    ValueTask<BitmapSource> ProcessAsync(BitmapSource image, PluginExecutionContext context,
        CancellationToken cancellationToken = default);
}

public interface IRecognitionPlugin
{
    string Id { get; }
    ValueTask<IReadOnlyList<RecognitionResult>> RecognizeAsync(BitmapSource image,
        PluginExecutionContext context, CancellationToken cancellationToken = default);
}

public interface IOutputPlugin
{
    string Id { get; }
    ValueTask<PluginOutputResult> OutputAsync(BitmapSource image, PluginExecutionContext context,
        CancellationToken cancellationToken = default);
}

public interface IWorkflowActionPlugin
{
    string Id { get; }
    ValueTask<IReadOnlyDictionary<string, object?>> ExecuteAsync(
        IReadOnlyDictionary<string, object?> input, PluginExecutionContext context,
        CancellationToken cancellationToken = default);
}
