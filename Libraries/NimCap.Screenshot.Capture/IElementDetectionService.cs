using System.Drawing;
using NimCap.Screenshot.Models;

namespace NimCap.Screenshot.Services;

public interface IElementDetectionService
{
    Task<IReadOnlyList<ElementCandidate>> FindElementsAsync(
        WindowCandidate window, System.Drawing.Point screenPoint, CancellationToken cancellationToken);
}
