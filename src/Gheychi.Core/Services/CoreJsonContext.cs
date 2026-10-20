using System.Text.Json.Serialization;
using Gheychi.Core.Folders;
using Gheychi.Core.Models;

namespace Gheychi.Core.Services;

// Read on every launch, some of it on the UI thread before the first frame. The reflection serializer builds its
// readers at run time (generated and JIT-compiled code, even in an AOT build); these are compiled in. Same JSON as before.
[JsonSerializable(typeof(List<SmsThread>))]
[JsonSerializable(typeof(FolderBook.Saved))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
