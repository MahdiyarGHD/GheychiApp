using Android;
using Android.App;
using Android.App.Roles;
using Android.Content;
using Android.Content.PM;
using Android.Database;
using Android.OS;
using Android.Provider;
using Android.Telephony;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Gheychi.App.Platforms.Android.Permissions;
using Gheychi.Core.Models;
using Gheychi.Core.Services;
using SmsMessage = Gheychi.Core.Models.SmsMessage;

namespace Gheychi.App.Platforms.Android.Services;

public sealed class AndroidSmsService : ISmsService
{
    private readonly IMessageMetadataRepository? _metadataRepo;

    public AndroidSmsService(IMessageMetadataRepository? metadataRepo = null)
    {
        _metadataRepo = metadataRepo;
    }

    private static readonly string[] SmsProjection =
    [
        Telephony.Sms.InterfaceConsts.Id,
        Telephony.Sms.InterfaceConsts.ThreadId,
        Telephony.Sms.InterfaceConsts.Address,
        Telephony.Sms.InterfaceConsts.Body,
        Telephony.Sms.InterfaceConsts.Date,
        Telephony.Sms.InterfaceConsts.Read,
        Telephony.Sms.InterfaceConsts.Type,
        Telephony.Sms.InterfaceConsts.Status,
        "sub_id"
    ];

    public Task<bool> EnsureDefaultSmsAppAsync()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            var roleManager = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.GetSystemService(Context.RoleService) as RoleManager;
            if (roleManager != null && !roleManager.IsRoleHeld(RoleManager.RoleSms))
            {
                var intent = roleManager.CreateRequestRoleIntent(RoleManager.RoleSms);
                Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.StartActivity(intent);
                return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }

        var defaultSms = Telephony.Sms.GetDefaultSmsPackage(Microsoft.Maui.ApplicationModel.Platform.AppContext);
        if (defaultSms != Microsoft.Maui.ApplicationModel.Platform.AppContext.PackageName)
        {
            var intent = new Intent(Telephony.Sms.Intents.ActionChangeDefault);
            intent.PutExtra(Telephony.Sms.Intents.ExtraPackageName, Microsoft.Maui.ApplicationModel.Platform.AppContext.PackageName);
            Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.StartActivity(intent);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    public async Task<bool> EnsurePermissionsAsync()
    {
        var smsStatus = await Microsoft.Maui.ApplicationModel.Permissions.CheckStatusAsync<SmsPermission>();
        if (smsStatus != PermissionStatus.Granted)
            smsStatus = await Microsoft.Maui.ApplicationModel.Permissions.RequestAsync<SmsPermission>();

        var contactStatus = await Microsoft.Maui.ApplicationModel.Permissions.CheckStatusAsync<ContactsPermission>();
        if (contactStatus != PermissionStatus.Granted)
            await Microsoft.Maui.ApplicationModel.Permissions.RequestAsync<ContactsPermission>();

        return smsStatus == PermissionStatus.Granted;
    }

    public Task<IReadOnlyList<SmsThread>> GetThreadsAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<SmsThread>>(() =>
        {
            var context = Microsoft.Maui.ApplicationModel.Platform.AppContext;
            var contactMap = LoadContacts(context);
            var dict = new Dictionary<long, ThreadBuilder>();

            var smsUri = Telephony.Sms.ContentUri;
            if (smsUri == null)
                return Array.Empty<SmsThread>();

            try
            {
                using var cursor = context.ContentResolver?.Query(
                    smsUri,
                    SmsProjection,
                    null,
                    null,
                    "date DESC");

                if (cursor == null)
                    return Array.Empty<SmsThread>();

                var idCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Id);
                var threadIdCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.ThreadId);
                var addressCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Address);
                var bodyCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Body);
                var dateCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Date);
                var readCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Read);
                var typeCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Type);
                var statusCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Status);
                var subIdCol = cursor.GetColumnIndex("sub_id");

                while (cursor.MoveToNext())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var threadId = cursor.GetLong(threadIdCol);
                    var read = cursor.GetInt(readCol);
                    var type = cursor.GetInt(typeCol);
                    var status = cursor.GetInt(statusCol);

                    if (!dict.TryGetValue(threadId, out var builder))
                    {
                        var address = cursor.GetString(addressCol) ?? string.Empty;
                        var body = cursor.GetString(bodyCol) ?? string.Empty;
                        var dateMs = cursor.GetLong(dateCol);
                        var subId = subIdCol >= 0 && !cursor.IsNull(subIdCol) ? cursor.GetInt(subIdCol) : 1;

                        builder = new ThreadBuilder
                        {
                            ThreadId = threadId,
                            Address = address,
                            Snippet = body,
                            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(dateMs).LocalDateTime,
                            TotalCount = 1,
                            UnreadCount = (read == 0 && type == (int)SmsMessageType.Inbox) ? 1 : 0,
                            HasFailed = (type == (int)SmsMessageType.Failed || status == 64),
                            SubId = subId
                        };
                        dict[threadId] = builder;
                    }
                    else
                    {
                        builder.TotalCount++;
                        if (read == 0 && type == (int)SmsMessageType.Inbox)
                            builder.UnreadCount++;
                        if (type == (int)SmsMessageType.Failed || status == 64)
                            builder.HasFailed = true;
                    }
                }
            }
            catch (Exception)
            {
                return Array.Empty<SmsThread>();
            }

            var list = new List<SmsThread>(dict.Count);
            foreach (var b in dict.Values)
            {
                string? contactName = null;
                var key = PhoneNumberNormalizer.ToLookupKey(b.Address);
                if (contactMap.TryGetValue(key, out var foundName))
                    contactName = foundName;
                else if (PhoneNumberNormalizer.IsAlphanumeric(b.Address))
                    contactName = b.Address;

                list.Add(new SmsThread(
                    b.ThreadId,
                    b.Address,
                    contactName,
                    b.Snippet,
                    b.Timestamp,
                    b.TotalCount,
                    b.UnreadCount,
                    b.HasFailed,
                    b.SubId));
            }

            return list;
        }, cancellationToken);

    public Task<IReadOnlyList<SmsMessage>> GetMessagesAsync(long threadId, int? limit = null, int offset = 0, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<SmsMessage>>(() =>
        {
            var context = Microsoft.Maui.ApplicationModel.Platform.AppContext;
            var smsUri = Telephony.Sms.ContentUri;
            if (smsUri == null)
                return Array.Empty<SmsMessage>();

            var sortOrder = limit.HasValue
                ? $"date DESC LIMIT {limit.Value} OFFSET {offset}"
                : "date ASC";

            try
            {
                using var cursor = context.ContentResolver?.Query(
                    smsUri,
                    SmsProjection,
                    $"{Telephony.Sms.InterfaceConsts.ThreadId} = ?",
                    [threadId.ToString()],
                    sortOrder);

                if (cursor == null)
                    return Array.Empty<SmsMessage>();

                var idCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Id);
                var addressCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Address);
                var bodyCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Body);
                var dateCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Date);
                var readCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Read);
                var typeCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Type);
                var statusCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Status);
                var subIdCol = cursor.GetColumnIndex("sub_id");

                var messages = new List<SmsMessage>(cursor.Count);
                while (cursor.MoveToNext())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var id = cursor.GetLong(idCol);
                    var address = cursor.GetString(addressCol) ?? string.Empty;
                    var body = cursor.GetString(bodyCol) ?? string.Empty;
                    var dateMs = cursor.GetLong(dateCol);
                    var read = readCol >= 0 ? cursor.GetInt(readCol) : 1;
                    var type = cursor.GetInt(typeCol);
                    var status = cursor.GetInt(statusCol);
                    var subId = subIdCol >= 0 && !cursor.IsNull(subIdCol) ? cursor.GetInt(subIdCol) : 1;

                    var isOutgoing = type is (int)SmsMessageType.Sent
                                         or (int)SmsMessageType.Outbox
                                         or (int)SmsMessageType.Failed
                                         or (int)SmsMessageType.Queued;

                    var isDelivered = status == 0 || (isOutgoing && type == (int)SmsMessageType.Sent && status == -1);
                    var hasFailed = type == (int)SmsMessageType.Failed || status == 64;
                    var isRead = read != 0 || isOutgoing;

                    messages.Add(new SmsMessage(
                        id,
                        threadId,
                        address,
                        body,
                        DateTimeOffset.FromUnixTimeMilliseconds(dateMs).LocalDateTime,
                        isOutgoing,
                        isDelivered,
                        hasFailed,
                        subId,
                        isRead));
                }

                if (_metadataRepo != null && messages.Count > 0)
                {
                    try
                    {
                        var metaMap = _metadataRepo.GetForMessagesAsync(messages.Select(m => m.Id)).GetAwaiter().GetResult();
                        for (var i = 0; i < messages.Count; i++)
                        {
                            var msg = messages[i];
                            if (metaMap.TryGetValue(msg.Id, out var meta))
                            {
                                messages[i] = msg with
                                {
                                    IsStarred = meta.IsStarred,
                                    Reaction = meta.Reaction
                                };
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                for (var i = messages.Count - 1; i >= 0; i--)
                {
                    var msg = messages[i];
                    var parsed = ReactionHelper.TryParseReaction(msg.Body);
                    if (parsed.IsReaction && !string.IsNullOrEmpty(parsed.Snippet) && !string.IsNullOrEmpty(parsed.Emoji))
                    {
                        // Normalized, order-independent matching: unify quote styles and
                        // whitespace (newlines included) on both sides, prefer an exact
                        // match, and break ties toward the NEWEST message so a snippet
                        // repeated in several bubbles links to the latest one.
                        var normalizedSnippet = ReactionHelper.NormalizeForMatch(parsed.Snippet);
                        SmsMessage? bestExact = null;
                        SmsMessage? bestPartial = null;
                        foreach (var candidate in messages)
                        {
                            if (candidate.Id == msg.Id)
                                continue;
                            var normalizedBody = ReactionHelper.NormalizeForMatch(candidate.Body);
                            if (normalizedBody.Length == 0 || normalizedSnippet.Length == 0)
                                continue;
                            if (normalizedBody.Equals(normalizedSnippet, StringComparison.OrdinalIgnoreCase))
                            {
                                if (bestExact == null || candidate.Timestamp > bestExact.Timestamp)
                                    bestExact = candidate;
                            }
                            else if (normalizedBody.Contains(normalizedSnippet, StringComparison.OrdinalIgnoreCase) ||
                                     normalizedSnippet.Contains(ReactionHelper.NormalizeForMatch(ReactionHelper.GetSnippet(candidate.Body)), StringComparison.OrdinalIgnoreCase))
                            {
                                if (bestPartial == null || candidate.Timestamp > bestPartial.Timestamp)
                                    bestPartial = candidate;
                            }
                        }

                        var target = bestExact ?? bestPartial;

                        if (target != null)
                        {
                            var targetIdx = messages.IndexOf(target);
                            messages[targetIdx] = target with { Reaction = parsed.Emoji };
                            if (_metadataRepo != null)
                            {
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await _metadataRepo.SetReactionAsync(target.Id, threadId, parsed.Emoji, msg.IsOutgoing);
                                    }
                                    catch
                                    {
                                    }
                                });
                            }
                            messages.RemoveAt(i);
                        }
                    }
                }

                if (limit.HasValue)
                    messages.Reverse();

                return (IReadOnlyList<SmsMessage>)messages;
            }
            catch (Exception)
            {
                return Array.Empty<SmsMessage>();
            }
        }, cancellationToken);

    public Task<bool> SendSmsAsync(string address, string text, int subId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var context = Microsoft.Maui.ApplicationModel.Platform.AppContext;
            SmsManager? smsManager = null;

#pragma warning disable CA1422
            if (subId > 0)
            {
                try
                {
                    if (OperatingSystem.IsAndroidVersionAtLeast(31))
                    {
                        var baseManager = context.GetSystemService(Java.Lang.Class.FromType(typeof(SmsManager))) as SmsManager;
                        smsManager = baseManager?.CreateForSubscriptionId(subId);
                    }
                    else
                    {
                        smsManager = SmsManager.GetSmsManagerForSubscriptionId(subId);
                    }
                }
                catch
                {
                    smsManager = SmsManager.Default;
                }
            }

            smsManager ??= SmsManager.Default;
#pragma warning restore CA1422
            if (smsManager == null)
                return false;

            var parts = smsManager.DivideMessage(text);
            if (parts != null && parts.Count > 1)
            {
                smsManager.SendMultipartTextMessage(address, null, parts, null, null);
            }
            else
            {
                smsManager.SendTextMessage(address, null, text, null, null);
            }

            var values = new ContentValues();
            values.Put(Telephony.Sms.InterfaceConsts.Address, address);
            values.Put(Telephony.Sms.InterfaceConsts.Body, text);
            values.Put(Telephony.Sms.InterfaceConsts.Date, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            values.Put(Telephony.Sms.InterfaceConsts.Read, 1);
            values.Put(Telephony.Sms.InterfaceConsts.Type, (int)SmsMessageType.Sent);
            values.Put(Telephony.Sms.InterfaceConsts.Status, 0);
            if (subId > 0)
                values.Put("sub_id", subId);

            var sentUri = Telephony.Sms.Sent.ContentUri;
            if (sentUri != null)
                context.ContentResolver?.Insert(sentUri, values);
            return true;
        }, cancellationToken);

    public Task<bool> MarkThreadAsReadAsync(long threadId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var context = Microsoft.Maui.ApplicationModel.Platform.AppContext;
            var smsUri = Telephony.Sms.ContentUri;
            if (smsUri == null)
                return false;

            try
            {
                var values = new ContentValues();
                values.Put(Telephony.Sms.InterfaceConsts.Read, 1);
                values.Put(Telephony.Sms.InterfaceConsts.Seen, 1);

                var rows = context.ContentResolver?.Update(
                    smsUri,
                    values,
                    $"{Telephony.Sms.InterfaceConsts.ThreadId} = ? AND {Telephony.Sms.InterfaceConsts.Read} = 0",
                    [threadId.ToString()]);

                return rows > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }, cancellationToken);

    public Task<bool> DeleteMessagesAsync(IReadOnlyList<long> messageIds, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            if (messageIds == null || messageIds.Count == 0)
                return false;

            var context = Microsoft.Maui.ApplicationModel.Platform.AppContext;
            var smsUri = Telephony.Sms.ContentUri;
            if (smsUri == null)
                return false;

            try
            {
                var idStrings = messageIds.Select(id => id.ToString()).ToArray();
                var placeholders = string.Join(",", messageIds.Select(_ => "?"));
                var deleted = context.ContentResolver?.Delete(
                    smsUri,
                    $"{Telephony.Sms.InterfaceConsts.Id} IN ({placeholders})",
                    idStrings) ?? 0;

                if (_metadataRepo != null)
                {
                    await _metadataRepo.DeleteAsync(messageIds);
                }

                return deleted > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }, cancellationToken);

    private static IReadOnlyList<SimCardInfo>? _cachedSimList;
    private static IReadOnlyDictionary<int, int>? _cachedSimSlotMap;
    private static IReadOnlyDictionary<int, string>? _cachedSimCarrierMap;
    private static bool? _cachedIsDualSim;
    private static readonly object SimLock = new();

    private static string? GetSystemProperty(string key)
    {
        try
        {
            var process = Java.Lang.Runtime.GetRuntime()?.Exec(["/system/bin/getprop", key]);
            if (process != null)
            {
                using var reader = new System.IO.StreamReader(process.InputStream!);
                var line = reader.ReadLine()?.Trim();
                if (!string.IsNullOrWhiteSpace(line))
                    return line;
            }
        }
        catch
        {
        }
        return null;
    }

    private static void EnsureSimInfoInternal()
    {
        lock (SimLock)
        {
            if (_cachedSimList != null && _cachedSimSlotMap != null && _cachedSimCarrierMap != null && _cachedIsDualSim.HasValue)
                return;
        }

        var context = Microsoft.Maui.ApplicationModel.Platform.AppContext;
        var simList = new List<SimCardInfo>();
        var slotMap = new Dictionary<int, int>();
        var carrierMap = new Dictionary<int, string>();
        var isDualSim = false;

        // 1. Get operator names from system properties without any permissions
        var simOperators = GetSystemProperty("gsm.sim.operator.alpha");
        if (string.IsNullOrWhiteSpace(simOperators))
            simOperators = GetSystemProperty("gsm.operator.alpha");

        var multiSimConfig = GetSystemProperty("persist.radio.multisim.config");
        if (multiSimConfig is "dsds" or "dsda" or "tsts")
        {
            isDualSim = true;
        }

        var operatorNames = !string.IsNullOrWhiteSpace(simOperators)
            ? simOperators.Split(',', StringSplitOptions.TrimEntries)
            : Array.Empty<string>();

        if (operatorNames.Length >= 2)
        {
            isDualSim = true;
        }

        var telephony = context.GetSystemService(Context.TelephonyService) as TelephonyManager;
#pragma warning disable CA1422
        var phoneCount = OperatingSystem.IsAndroidVersionAtLeast(30)
            ? telephony?.ActiveModemCount ?? 1
            : telephony?.PhoneCount ?? 1;
#pragma warning restore CA1422
        if (phoneCount >= 2)
            isDualSim = true;

        // 2. Discover subscription IDs from SMS messages and standard slots
        var knownSubIds = new HashSet<int>();
        try
        {
            var smsUri = Telephony.Sms.ContentUri;
            if (smsUri != null)
            {
                using var cursor = context.ContentResolver?.Query(
                    smsUri,
                    ["sub_id"],
                    "sub_id > 0",
                    null,
                    "date DESC LIMIT 100");

                if (cursor != null)
                {
                    var col = cursor.GetColumnIndex("sub_id");
                    while (cursor.MoveToNext())
                    {
                        var sId = cursor.GetInt(col);
                        if (sId > 0)
                            knownSubIds.Add(sId);
                    }
                }
            }
        }
        catch
        {
        }

        knownSubIds.Add(1);
        knownSubIds.Add(2);

        // Map each subId to its real slot index and carrier name
        foreach (var subId in knownSubIds)
        {
            var slotIndex = -1;
            try
            {
                slotIndex = global::Android.Telephony.SubscriptionManager.GetSlotIndex(subId);
            }
            catch
            {
            }

            // GetSlotIndex returns 0-based slot index (0 = SIM 1, 1 = SIM 2, etc.)
            var slotNumber = slotIndex >= 0 ? slotIndex + 1 : subId;
            slotMap[subId] = slotNumber;

            string? carrier = null;
            try
            {
                var pinnedTelephony = telephony?.CreateForSubscriptionId(subId);
                carrier = pinnedTelephony?.SimOperatorName;
                if (string.IsNullOrWhiteSpace(carrier))
                    carrier = pinnedTelephony?.NetworkOperatorName;
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(carrier) && slotNumber - 1 >= 0 && slotNumber - 1 < operatorNames.Length)
            {
                carrier = operatorNames[slotNumber - 1];
            }

            if (!string.IsNullOrWhiteSpace(carrier))
            {
                carrierMap[subId] = carrier;
            }
        }

        // 3. Build simList for all active slots
        var distinctSlots = slotMap.Values.Distinct().OrderBy(s => s).ToList();
        if (distinctSlots.Count < operatorNames.Length)
        {
            for (var i = 1; i <= operatorNames.Length; i++)
            {
                if (!distinctSlots.Contains(i))
                    distinctSlots.Add(i);
            }
            distinctSlots.Sort();
        }

        foreach (var slot in distinctSlots)
        {
            var matchingSubId = slotMap.FirstOrDefault(kvp => kvp.Value == slot).Key;
            if (matchingSubId <= 0)
                matchingSubId = slot;

            string? name = null;
            if (carrierMap.TryGetValue(matchingSubId, out var cName) && !string.IsNullOrWhiteSpace(cName))
                name = cName;
            else if (slot - 1 >= 0 && slot - 1 < operatorNames.Length && !string.IsNullOrWhiteSpace(operatorNames[slot - 1]))
                name = operatorNames[slot - 1];

            if (!string.IsNullOrWhiteSpace(name))
            {
                simList.Add(new SimCardInfo(slot, matchingSubId, name));
            }
        }

        if (simList.Count >= 2)
            isDualSim = true;

        lock (SimLock)
        {
            _cachedIsDualSim = isDualSim;
            _cachedSimList = simList;
            _cachedSimSlotMap = slotMap;
            _cachedSimCarrierMap = carrierMap;
        }
    }

    public Task<IReadOnlyList<SimCardInfo>> GetActiveSimsAsync()
    {
        lock (SimLock)
        {
            if (_cachedSimList != null)
                return Task.FromResult(_cachedSimList);
        }

        return Task.Run(() =>
        {
            EnsureSimInfoInternal();
            return _cachedSimList ?? (IReadOnlyList<SimCardInfo>)Array.Empty<SimCardInfo>();
        });
    }

    public Task<bool> IsDualSimAsync()
    {
        lock (SimLock)
        {
            if (_cachedIsDualSim.HasValue)
                return Task.FromResult(_cachedIsDualSim.Value);
        }

        return Task.Run(() =>
        {
            EnsureSimInfoInternal();
            return _cachedIsDualSim ?? false;
        });
    }

    public Task<IReadOnlyDictionary<int, int>> GetSimSlotMapAsync()
    {
        lock (SimLock)
        {
            if (_cachedSimSlotMap != null)
                return Task.FromResult(_cachedSimSlotMap);
        }

        return Task.Run(() =>
        {
            EnsureSimInfoInternal();
            return _cachedSimSlotMap ?? (IReadOnlyDictionary<int, int>)new Dictionary<int, int>();
        });
    }

    public Task<IReadOnlyDictionary<int, string>> GetSimCarrierMapAsync()
    {
        lock (SimLock)
        {
            if (_cachedSimCarrierMap != null)
                return Task.FromResult(_cachedSimCarrierMap);
        }

        return Task.Run(() =>
        {
            EnsureSimInfoInternal();
            return _cachedSimCarrierMap ?? (IReadOnlyDictionary<int, string>)new Dictionary<int, string>();
        });
    }

    private static Dictionary<string, string> LoadContacts(Context context)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (ContextCompat.CheckSelfPermission(context, Manifest.Permission.ReadContacts) != Permission.Granted)
            return map;

        var contactsUri = ContactsContract.CommonDataKinds.Phone.ContentUri;
        if (contactsUri == null)
            return map;

        try
        {
            using var cursor = context.ContentResolver?.Query(
                contactsUri,
                [
                    ContactsContract.CommonDataKinds.Phone.Number,
                    ContactsContract.IContactsColumns.DisplayName
                ],
                null,
                null,
                null);

            if (cursor == null)
                return map;

            var numCol = cursor.GetColumnIndex(ContactsContract.CommonDataKinds.Phone.Number);
            var nameCol = cursor.GetColumnIndex(ContactsContract.IContactsColumns.DisplayName);

            while (cursor.MoveToNext())
            {
                var rawNum = cursor.GetString(numCol);
                var name = cursor.GetString(nameCol);
                if (!string.IsNullOrEmpty(rawNum) && !string.IsNullOrEmpty(name))
                {
                    var key = PhoneNumberNormalizer.ToLookupKey(rawNum);
                    map.TryAdd(key, name);
                }
            }
        }
        catch (Exception)
        {
            return map;
        }

        return map;
    }

    private sealed class ThreadBuilder
    {
        public long ThreadId { get; init; }
        public string Address { get; init; } = string.Empty;
        public string Snippet { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; }
        public int TotalCount { get; set; }
        public int UnreadCount { get; set; }
        public bool HasFailed { get; set; }
        public int SubId { get; init; }
    }
}
