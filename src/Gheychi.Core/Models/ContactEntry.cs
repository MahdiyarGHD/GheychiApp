namespace Gheychi.Core.Models;

/// <summary>One phone number of a contact. A contact with several numbers yields several entries.</summary>
public sealed record ContactEntry(string Name, string Number, string Label);
