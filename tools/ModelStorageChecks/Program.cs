using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Threading;

namespace SnapSharp.RegressionChecks;

internal static partial class Program
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(".artifacts", "model-storage-checks", Guid.NewGuid().ToString("N")));
    private static readonly BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    [STAThread]
    private static int Main()
    {
        Directory.CreateDirectory(Root);
        try { ModelStorageChecks(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Console.WriteLine("Fixtures and previews: " + Root); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void PumpUntil(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Model storage UI did not finish refreshing.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }
}
