using System.Text.Json.Serialization;
using Gheychi.Core.Models;
using Gheychi.Core.Scheduling;

namespace Gheychi.App.Services;

// Read while the app starts (the drafts on the UI thread, before the first frame). The reflection serializer builds its
// readers at run time (generated and JIT-compiled code, even in an AOT build); these are compiled in. Same JSON as before.
[JsonSerializable(typeof(List<MessageDraft>))]
[JsonSerializable(typeof(List<ScheduledMessage>))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
