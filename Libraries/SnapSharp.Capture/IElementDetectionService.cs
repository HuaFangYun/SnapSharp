using System.Drawing;
using SnapSharp.Models;

namespace SnapSharp.Services;

public interface IElementDetectionService
{
    Task<IReadOnlyList<ElementCandidate>> FindElementsAsync(
        WindowCandidate window, System.Drawing.Point screenPoint, CancellationToken cancellationToken);
}
