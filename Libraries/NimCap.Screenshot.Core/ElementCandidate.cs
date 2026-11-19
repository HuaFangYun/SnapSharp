using System.Drawing;

namespace NimCap.Screenshot.Models;

/// <summary>A visible element in physical desktop pixels; no cross-process objects escape detection.</summary>
public sealed record ElementCandidate(Rectangle Bounds, string ControlType, string Name = "");
