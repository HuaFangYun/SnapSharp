using System.IO;

namespace SnapSharp.RegressionChecks;

internal static partial class Program
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(".artifacts", "scrolling-checks", Guid.NewGuid().ToString("N")));
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    [STAThread]
    private static int Main()
    {
        Directory.CreateDirectory(Root);
        try { ScrollingChecks(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
