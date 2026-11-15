using System.Reflection;
using System.Runtime.Loader;

namespace SnapSharp.Plugins;

public sealed record PluginLoadOptions(
    string PluginDirectory,
    bool AllowExternalPlugins = false,
    bool RequireSignedAssemblies = true);

public sealed record PluginLoadFailure(string Source, string Message);

public sealed class PluginCatalog : IPluginRegistry
{
    private readonly PluginLoadOptions _options;
    private readonly List<ISnapSharpPlugin> _plugins = [];
    private readonly List<IImageProcessorPlugin> _imageProcessors = [];
    private readonly List<IRecognitionPlugin> _recognizers = [];
    private readonly List<IOutputPlugin> _outputs = [];
    private readonly List<IWorkflowActionPlugin> _workflowActions = [];
    private readonly List<PluginLoadFailure> _failures = [];
    private readonly List<AssemblyLoadContext> _loadContexts = [];

    public PluginCatalog(PluginLoadOptions options) => _options = options;

    public IReadOnlyList<ISnapSharpPlugin> Plugins => _plugins;
    public IReadOnlyList<IImageProcessorPlugin> ImageProcessors => _imageProcessors;
    public IReadOnlyList<IRecognitionPlugin> Recognizers => _recognizers;
    public IReadOnlyList<IOutputPlugin> Outputs => _outputs;
    public IReadOnlyList<IWorkflowActionPlugin> WorkflowActions => _workflowActions;
    public IReadOnlyList<PluginLoadFailure> Failures => _failures;

    public void RegisterBuiltIn(ISnapSharpPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        RegisterPlugin(plugin);
    }

    public void DiscoverExternalPlugins()
    {
        if (!_options.AllowExternalPlugins || !Directory.Exists(_options.PluginDirectory)) return;
        foreach (var assemblyPath in Directory.EnumerateFiles(_options.PluginDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var name = AssemblyName.GetAssemblyName(assemblyPath);
                if (_options.RequireSignedAssemblies && (name.GetPublicKeyToken()?.Length ?? 0) == 0)
                    throw new InvalidOperationException("Unsigned external plugins are disabled by policy.");
                var context = new PluginLoadContext(assemblyPath);
                var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
                var pluginTypes = assembly.GetTypes().Where(type => !type.IsAbstract &&
                    typeof(ISnapSharpPlugin).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null);
                foreach (var pluginType in pluginTypes)
                    RegisterPlugin((ISnapSharpPlugin)Activator.CreateInstance(pluginType)!);
                _loadContexts.Add(context);
            }
            catch (Exception ex)
            {
                _failures.Add(new PluginLoadFailure(assemblyPath, ex.GetBaseException().Message));
            }
        }
    }

    public bool TryGetImageProcessor(string id, out IImageProcessorPlugin? plugin) =>
        TryGet(_imageProcessors, id, out plugin);

    public bool TryGetRecognizer(string id, out IRecognitionPlugin? plugin) =>
        TryGet(_recognizers, id, out plugin);

    public bool TryGetOutput(string id, out IOutputPlugin? plugin) =>
        TryGet(_outputs, id, out plugin);

    public bool TryGetWorkflowAction(string id, out IWorkflowActionPlugin? plugin) =>
        TryGet(_workflowActions, id, out plugin);

    public void RegisterImageProcessor(IImageProcessorPlugin plugin) => RegisterCapability(_imageProcessors, plugin, plugin.Id);
    public void RegisterRecognizer(IRecognitionPlugin plugin) => RegisterCapability(_recognizers, plugin, plugin.Id);
    public void RegisterOutput(IOutputPlugin plugin) => RegisterCapability(_outputs, plugin, plugin.Id);
    public void RegisterWorkflowAction(IWorkflowActionPlugin plugin) => RegisterCapability(_workflowActions, plugin, plugin.Id);

    private void RegisterPlugin(ISnapSharpPlugin plugin)
    {
        if (_plugins.Any(item => string.Equals(item.Metadata.Id, plugin.Metadata.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Duplicate plugin id '{plugin.Metadata.Id}'.");
        var staging = new StagingRegistry();
        plugin.Register(staging);
        ValidateCapabilities(_imageProcessors, staging.ImageProcessors.Select(item => (item.Id, (object)item)));
        ValidateCapabilities(_recognizers, staging.Recognizers.Select(item => (item.Id, (object)item)));
        ValidateCapabilities(_outputs, staging.Outputs.Select(item => (item.Id, (object)item)));
        ValidateCapabilities(_workflowActions, staging.WorkflowActions.Select(item => (item.Id, (object)item)));
        _imageProcessors.AddRange(staging.ImageProcessors);
        _recognizers.AddRange(staging.Recognizers);
        _outputs.AddRange(staging.Outputs);
        _workflowActions.AddRange(staging.WorkflowActions);
        _plugins.Add(plugin);
    }

    private static void ValidateCapabilities<T>(IEnumerable<T> existing,
        IEnumerable<(string Id, object Plugin)> pending) where T : class
    {
        var ids = new HashSet<string>(existing.Select(GetId), StringComparer.OrdinalIgnoreCase);
        foreach (var (id, _) in pending)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Plugin capability id is required.");
            if (!ids.Add(id)) throw new InvalidOperationException($"Duplicate plugin capability id '{id}'.");
        }
    }

    private static void RegisterCapability<T>(ICollection<T> target, T plugin, string id) where T : class
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Plugin capability id is required.", nameof(plugin));
        if (target.Any(item => string.Equals(GetId(item), id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Duplicate plugin capability id '{id}'.");
        target.Add(plugin);
    }

    private static bool TryGet<T>(IEnumerable<T> source, string id, out T? plugin) where T : class
    {
        plugin = source.FirstOrDefault(item => string.Equals(GetId(item), id, StringComparison.OrdinalIgnoreCase));
        return plugin is not null;
    }

    private static string GetId<T>(T plugin) => plugin switch
    {
        IImageProcessorPlugin value => value.Id,
        IRecognitionPlugin value => value.Id,
        IOutputPlugin value => value.Id,
        IWorkflowActionPlugin value => value.Id,
        _ => throw new ArgumentOutOfRangeException(nameof(plugin))
    };

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath) : base(isCollectible: false) =>
            _resolver = new AssemblyDependencyResolver(pluginPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == typeof(ISnapSharpPlugin).Assembly.GetName().Name) return null;
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }

    private sealed class StagingRegistry : IPluginRegistry
    {
        public List<IImageProcessorPlugin> ImageProcessors { get; } = [];
        public List<IRecognitionPlugin> Recognizers { get; } = [];
        public List<IOutputPlugin> Outputs { get; } = [];
        public List<IWorkflowActionPlugin> WorkflowActions { get; } = [];

        public void RegisterImageProcessor(IImageProcessorPlugin plugin) => ImageProcessors.Add(plugin);
        public void RegisterRecognizer(IRecognitionPlugin plugin) => Recognizers.Add(plugin);
        public void RegisterOutput(IOutputPlugin plugin) => Outputs.Add(plugin);
        public void RegisterWorkflowAction(IWorkflowActionPlugin plugin) => WorkflowActions.Add(plugin);
    }
}
