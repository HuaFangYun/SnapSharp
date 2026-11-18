using System.Drawing;
using System.IO;
using System.Windows.Threading;
using SnapSharp.Models;
using SnapSharp.Services;

namespace SnapSharp.RegressionChecks;

internal static partial class Program
{
    private static void RecordingChecks()
    {
        var assembly = typeof(RecordingService).Assembly;
        Require(assembly.GetName().Name == "SnapSharp.Recording" &&
            new[] { typeof(RecordingDevice), typeof(RecordingDeviceService), typeof(RecordingClock),
                typeof(RecordingEncoderProbe), typeof(RecordingOutputPath), typeof(StreamingGifWriter) }
                .All(type => type.Assembly == assembly), "Recording implementation escaped its library boundary.");
        Require(assembly.GetReferencedAssemblies().All(name => name.Name != "SnapSharp"),
            "The recording library must not depend on the application.");

        void Reject<T>(Action command) where T : Exception
        {
            try { command(); }
            catch (T) { return; }
            throw new InvalidOperationException($"Recording command did not reject invalid input with {typeof(T).Name}.");
        }

        Reject<ArgumentNullException>(() => new RecordingService(null!));
        var context = new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher, DispatcherPriority.Background);
        var service = new RecordingService(context);
        var notifications = 0;
        service.Changed += () => notifications++;
        service.Stop(); service.PauseOrResume();
        Require(service.State == RecordingState.Ready && !service.IsActive && !service.Completion.IsCompleted &&
            service.Elapsed == TimeSpan.Zero && service.FrameCount == 0 && notifications == 0,
            "Commands on an idle recording session changed its state.");

        var directory = Path.Combine(Root, "recording");
        var path = Path.Combine(directory, "invalid-session.mp4");
        var region = new Rectangle(0, 0, 160, 96);
        Task.Run(() =>
        {
            Reject<InvalidOperationException>(service.Stop);
            Reject<InvalidOperationException>(service.PauseOrResume);
            Reject<InvalidOperationException>(() => service.Start(region, region, "unused", path, new(), null, null, null));
        }).GetAwaiter().GetResult();
        Reject<ArgumentException>(() => service.Start(region, region, "unused", path,
            new RecordingPreferences { FrameRate = 0 }, null, null, null));
        Reject<ArgumentException>(() => service.Start(new Rectangle(0, 0, 15, 96), region, "unused", path, new(), null, null, null));
        Reject<ArgumentException>(() => service.Start(new Rectangle(-1, 0, 160, 96), region, "unused", path, new(), null, null, null));
        Reject<IOException>(() => service.Start(region, region, "unused", "relative.mp4", new(), null, null, null));
        Reject<IOException>(() => service.Start(region, region, "unused", Path.ChangeExtension(path, ".gif"), new(), null, null, null));
        foreach (var (microphone, speaker) in new[] { ("microphone", (string?)null), (null, "speaker") })
            Reject<ArgumentException>(() => service.Start(region, region, "unused", Path.ChangeExtension(path, ".gif"),
                new RecordingPreferences { Format = "GIF" }, microphone, speaker, null));
        Require(!Directory.Exists(directory), "Rejected recordings created output directories.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, "existing recording");
        Reject<IOException>(() => service.Start(region, region, "unused", path, new(), null, null, null));
        Require(File.ReadAllText(path) == "existing recording" && service.State == RecordingState.Ready &&
            !service.Completion.IsCompleted && notifications == 0, "Rejected recordings modified existing data or session state.");

        var date = new DateTime(2026, 11, 18, 12, 0, 0);
        var first = RecordingOutputPath.Create(directory, "MP4", date);
        File.WriteAllText(first, "keep");
        var second = RecordingOutputPath.Create(directory, "MP4", date);
        Directory.CreateDirectory(second);
        var third = RecordingOutputPath.Create(directory, "MP4", date);
        Require(first != second && second != third && File.ReadAllText(first) == "keep" && !File.Exists(third) && !Directory.Exists(third),
            "Recording output naming did not preserve file and directory collisions.");
        Require(RecordingOutputPath.Create(directory, "GIF", date).EndsWith(".gif"), "Wrong GIF output extension.");

        Require(RecordingSelfTest.Run(directory), "Synthetic recording self-test failed.");
        Console.WriteLine("PASS: recording library boundary, owner-thread commands, input validation, output collisions, clock, GIF round-trip and synthetic native MP4 encoding. No desktop, audio or camera capture.");
    }
}
