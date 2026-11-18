using System.IO;
using System.Reflection;

namespace SnapSharp.StabilityChecks;

internal static partial class Program
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(".artifacts", "stability-checks", Guid.NewGuid().ToString("N")));
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    [STAThread]
    private static int Main()
    {
        Directory.CreateDirectory(Root);
        try { StabilityChecks(); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
