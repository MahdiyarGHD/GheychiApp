namespace Gheychi.Core.Spam;

/// <summary>A published spam model that can be downloaded.</summary>
/// <param name="Size">Size of the model file in bytes.</param>
/// <param name="Manifest">The release's manifest as published; it is installed alongside the model file.</param>
public sealed record SpamModelRelease(int Version, long Size, string Manifest);
