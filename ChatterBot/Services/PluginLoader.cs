using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace ChatterBot.Services;

/// <summary>
/// DLLからプラグインを動的に読み込むサービス
/// </summary>
public class PluginLoader
{
    private readonly string _pluginDirectory;
    private readonly ILogger<PluginLoader> _logger;

    public PluginLoader(string pluginDirectory, ILogger<PluginLoader> logger)
    {
        _pluginDirectory = pluginDirectory;
        _logger = logger;
    }

    /// <summary>
    /// プラグインディレクトリからDLLをスキャンしてTypeを読み込む
    /// </summary>
    public List<Type> LoadPluginTypes()
    {
        var pluginTypes = new List<Type>();

        if (!Directory.Exists(_pluginDirectory))
        {
            _logger.LogInformation("Plugin directory does not exist: {Directory}", _pluginDirectory);
            return pluginTypes;
        }

        var dllFiles = Directory.GetFiles(_pluginDirectory, "*.dll");
        _logger.LogInformation("Scanning {Count} DLL(s) in {Directory}", dllFiles.Length, _pluginDirectory);

        foreach (var dllPath in dllFiles)
        {
            try
            {
                var assemblyName = Path.GetFileNameWithoutExtension(dllPath);
                var assembly = Assembly.LoadFrom(dllPath);
                var types = GetPluginTypes(assembly);

                foreach (var type in types)
                {
                    pluginTypes.Add(type);
                    _logger.LogInformation("Found plugin type: {TypeName} from {Assembly}", type.Name, assemblyName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load assembly: {DllPath}", dllPath);
            }
        }

        return pluginTypes;
    }

    /// <summary>
    /// アセンブリからKernelFunction属性を持つメソッドを含むクラスを検出
    /// </summary>
    private IEnumerable<Type> GetPluginTypes(Assembly assembly)
    {
        var pluginTypes = new List<Type>();

        try
        {
            var types = assembly.GetExportedTypes();

            foreach (var type in types)
            {
                // publicクラスのみ対象
                if (!type.IsClass || !type.IsPublic)
                    continue;

                // KernelFunction属性を持つメソッドが1つ以上あるかチェック
                var hasKernelFunction = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Any(m => m.GetCustomAttributes(typeof(KernelFunctionAttribute), false).Length > 0);

                if (hasKernelFunction)
                {
                    pluginTypes.Add(type);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get types from assembly: {Assembly}", assembly.FullName);
        }

        return pluginTypes;
    }
}
