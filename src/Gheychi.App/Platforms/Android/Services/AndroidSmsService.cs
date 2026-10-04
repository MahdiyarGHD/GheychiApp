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
            var canonicalAddresses = LoadCanonicalAddresses(context);

            var convUri = global::Android.Net.Uri.Parse("content://mms-sms/conversations?simple=true");
            if (convUri != null)
            {
                try
                {
                    using var cursor = context.ContentResolver?.Query(
                        convUri,
                        ["_id", "date", "message_count", "unread_count", "recipient_ids", "snippet", "last_sim_id", "error"],
                        null,
                        null,
                        "date DESC");

                    if (cursor != null && cursor.Count > 0)
                    {
                        var idCol = cursor.GetColumnIndex("_id");
                        var dateCol = cursor.GetColumnIndex("date");
                        var countCol = cursor.GetColumnIndex("message_count");
                        var unreadCol = cursor.GetColumnIndex("unread_count");
                        var recipCol = cursor.GetColumnIndex("recipient_ids");
                        var snippetCol = cursor.GetColumnIndex("snippet");
                        var simCol = cursor.GetColumnIndex("last_sim_id");
                        var errorCol = cursor.GetColumnIndex("error");

                        var list = new List<SmsThread>(cursor.Count);
                        while (cursor.MoveToNext())
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var threadId = idCol >= 0 ? cursor.GetLong(idCol) : 0;
                            if (threadId <= 0) continue;

                            var dateMs = dateCol >= 0 ? cursor.GetLong(dateCol) : 0;
                            var msgCount = countCol >= 0 ? cursor.GetInt(countCol) : 0;
                            var unreadCount = unreadCol >= 0 ? cursor.GetInt(unreadCol) : 0;
                            var recipRaw = recipCol >= 0 ? cursor.GetString(recipCol) : null;
                            var snippet = snippetCol >= 0 ? cursor.GetString(snippetCol) ?? string.Empty : string.Empty;
                            var subId = simCol >= 0 && !cursor.IsNull(simCol) ? cursor.GetInt(simCol) : 1;
                            var hasFailed = errorCol >= 0 && cursor.GetInt(errorCol) != 0;

                            var address = string.Empty;
                            if (!string.IsNullOrEmpty(recipRaw))
                            {
                                var recipParts = recipRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                if (recipParts.Length > 0 && long.TryParse(recipParts[0], out var rId))
                                {
                                    canonicalAddresses.TryGetValue(rId, out address);
                                }
                            }
                            address ??= string.Empty;

                            string? contactName = null;
                            var key = PhoneNumberNormalizer.ToLookupKey(address);
                            if (contactMap.TryGetValue(key, out var foundName))
                                contactName = foundName;
                            else if (PhoneNumberNormalizer.IsAlphanumeric(address))
                                contactName = address;

                            list.Add(new SmsThread(
                                threadId,
                                address,
                                contactName,
                                snippet,
                                DateTimeOffset.FromUnixTimeMilliseconds(dateMs).LocalDateTime,
                                msgCount,
                                unreadCount,
                                hasFailed,
                                subId));
                        }

                        if (list.Count > 0)
                            return list;
                    }
                }
                catch (System.OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                }
            }

            return GetThreadsLegacy(context, contactMap, cancellationToken);
        }, cancellationToken);

    private static IReadOnlyList<SmsThread> GetThreadsLegacy(Context context, Dictionary<string, string> contactMap, CancellationToken cancellationToken)
    {
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
                "date DESC LIMIT 500");

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

                var threadId = threadIdCol >= 0 ? cursor.GetLong(threadIdCol) : 0;
                if (threadId <= 0) continue;
                var read = readCol >= 0 ? cursor.GetInt(readCol) : 1;
                var type = typeCol >= 0 ? cursor.GetInt(typeCol) : 1;
                var status = statusCol >= 0 ? cursor.GetInt(statusCol) : 0;

                if (!dict.TryGetValue(threadId, out var builder))
                {
                    var address = addressCol >= 0 ? cursor.GetString(addressCol) ?? string.Empty : string.Empty;
                    var body = bodyCol >= 0 ? cursor.GetString(bodyCol) ?? string.Empty : string.Empty;
                    var dateMs = dateCol >= 0 ? cursor.GetLong(dateCol) : 0;
                    var subId = subIdCol >= 0 && !cursor.IsNull(subIdCol) ? cursor.GetInt(subIdCol) : 1;

                    builder = new ThreadBuilder
                    {
                        ThreadId = threadId,
                        Address = address,
                        Snippet = body,
                        Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(dateMs).LocalDateTime,
                        TotalCount = 1,
                        UnreadCount = (read == 0 && type == (int)SmsMessageType.Inbox) ? 1 : 0,
                        HasFailed = SmsStatusHelper.HasFailed(type, status),
                        SubId = subId
                    };
                    dict[threadId] = builder;
                }
                else
                {
                    builder.TotalCount++;
                    if (read == 0 && type == (int)SmsMessageType.Inbox)
                        builder.UnreadCount++;
                    if (SmsStatusHelper.HasFailed(type, status))
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
    }

    public Task<IReadOnlyList<SearchResultChat>> SearchChatsAsync(SearchQuery query, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<SearchResultChat>>(async () =>
        {
            var context = Microsoft.Maui.ApplicationModel.Platform.AppContext;
            var contactMap = LoadContacts(context);
            var results = new Dictionary<long, SearchResultBuilder>();
            var archivedIds = GetArchivedThreadIds();

            HashSet<long>? allStarredIds = null;
            if (_metadataRepo != null)
            {
                try
                {
                    var sIds = await _metadataRepo.GetAllStarredMessageIdsAsync();
                    if (sIds != null && sIds.Count > 0)
                        allStarredIds = new HashSet<long>(sIds);
                }
                catch { }
            }

            var cleanText = query.Text?.Trim() ?? string.Empty;
            var hasText = !string.IsNullOrWhiteSpace(cleanText);

            // 1. Text search: use Android native FTS search URI: content://mms-sms/search?pattern=...
            if (hasText)
            {
                try
                {
                    var searchUri = global::Android.Net.Uri.Parse("content://mms-sms/search?pattern=" + global::Android.Net.Uri.Encode(cleanText));
                    if (searchUri != null)
                    {
                        using var cursor = context.ContentResolver?.Query(
                            searchUri,
                            ["_id", "thread_id", "body", "date"],
                            null,
                            null,
                            null);

                        if (cursor != null)
                        {
                            var idCol = cursor.GetColumnIndex("_id");
                            var threadCol = cursor.GetColumnIndex("thread_id");
                            var bodyCol = cursor.GetColumnIndex("body");
                            var dateCol = cursor.GetColumnIndex("date");

                            while (cursor.MoveToNext())
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var threadId = threadCol >= 0 ? cursor.GetLong(threadCol) : 0;
                                if (threadId <= 0) continue;

                                var isArchived = archivedIds.Contains(threadId);
                                if (isArchived && !query.IncludeArchivedAndSpam)
                                    continue;

                                if (results.Count >= 60 && !results.ContainsKey(threadId))
                                    continue;

                                var msgId = idCol >= 0 ? cursor.GetLong(idCol) : 0;
                                var dateMs = dateCol >= 0 ? cursor.GetLong(dateCol) : 0;
                                var body = bodyCol >= 0 ? cursor.GetString(bodyCol) ?? string.Empty : string.Empty;

                                if (!results.TryGetValue(threadId, out var builder))
                                {
                                    results[threadId] = new SearchResultBuilder
                                    {
                                        ThreadId = threadId,
                                        MessageId = msgId,
                                        Snippet = body,
                                        Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(dateMs).LocalDateTime,
                                        TotalMatches = 1,
                                        IsArchived = isArchived,
                                        IsStarred = allStarredIds != null && allStarredIds.Contains(msgId)
                                    };
                                }
                                else
                                {
                                    builder.TotalMatches++;
                                }
                            }
                        }
                    }
                }
                catch (System.OperationCanceledException)
                {
                    throw;
                }
                catch { }

                // 2. Also match by Contact Name or Phone number from active threads
                try
                {
                    var allThreads = await GetThreadsAsync(cancellationToken);
                    foreach (var t in allThreads)
                    {
                        if (results.ContainsKey(t.ThreadId))
                            continue;

                        var nameMatches = !string.IsNullOrEmpty(t.ContactName) && t.ContactName.Contains(cleanText, StringComparison.OrdinalIgnoreCase);
                        var phoneMatches = !string.IsNullOrEmpty(t.Address) && t.Address.Contains(cleanText, StringComparison.OrdinalIgnoreCase);

                        if (nameMatches || phoneMatches)
                        {
                            var isArchived = archivedIds.Contains(t.ThreadId);
                            if (isArchived && !query.IncludeArchivedAndSpam)
                                continue;

                            results[t.ThreadId] = new SearchResultBuilder
                            {
                                ThreadId = t.ThreadId,
                                MessageId = 0,
                                Address = t.Address,
                                ContactName = t.ContactName,
                                Snippet = t.Snippet,
                                Timestamp = t.Timestamp,
                                SubId = t.SubId,
                                IsRead = t.UnreadCount == 0,
                                TotalMatches = 1,
                                IsArchived = isArchived,
                                IsKnown = !string.IsNullOrWhiteSpace(t.ContactName)
                            };

                            if (results.Count >= 60)
                                break;
                        }
                    }
                }
                catch { }

                // 3. Fallback: if 0 results found, try fast LIKE search on content://sms:
                if (results.Count == 0)
                {
                    try
                    {
                        var smsUri = Telephony.Sms.ContentUri;
                        if (smsUri != null)
                        {
                            using var cursor = context.ContentResolver?.Query(
                                smsUri,
                                SmsProjection,
                                $"{Telephony.Sms.InterfaceConsts.Body} LIKE ?",
                                [$"%{cleanText}%"],
                                "date DESC LIMIT 150");

                            if (cursor != null)
                            {
                                PopulateResultsFromCursor(cursor, results, archivedIds, query.IncludeArchivedAndSpam, allStarredIds, cancellationToken);
                            }
                        }
                    }
                    catch { }
                }
            }
            else
            {
                // NO TEXT query - filter by specific FilterKind:
                if (query.FilterKind == SearchFilterKind.Starred && allStarredIds != null && allStarredIds.Count > 0)
                {
                    try
                    {
                        var idList = allStarredIds.Take(200).ToList();
                        var placeholders = string.Join(",", idList);
                        var smsUri = Telephony.Sms.ContentUri;
                        if (smsUri != null)
                        {
                            using var cursor = context.ContentResolver?.Query(
                                smsUri,
                                SmsProjection,
                                $"{Telephony.Sms.InterfaceConsts.Id} IN ({placeholders})",
                                null,
                                "date DESC LIMIT 150");

                            if (cursor != null)
                            {
                                PopulateResultsFromCursor(cursor, results, archivedIds, query.IncludeArchivedAndSpam, allStarredIds, cancellationToken);
                            }
                        }
                    }
                    catch { }
                }
                else if (query.FilterKind == SearchFilterKind.Unread)
                {
                    try
                    {
                        var smsUri = Telephony.Sms.ContentUri;
                        if (smsUri != null)
                        {
                            using var cursor = context.ContentResolver?.Query(
                                smsUri,
                                SmsProjection,
                                $"{Telephony.Sms.InterfaceConsts.Read} = 0 AND {Telephony.Sms.InterfaceConsts.Type} = 1",
                                null,
                                "date DESC LIMIT 150");

                            if (cursor != null)
                            {
                                PopulateResultsFromCursor(cursor, results, archivedIds, query.IncludeArchivedAndSpam, allStarredIds, cancellationToken);
                            }
                        }
                    }
                    catch { }
                }
                else if (query.FilterKind == SearchFilterKind.Sim && query.SimSlot.HasValue)
                {
                    try
                    {
                        var slotMap = await GetSimSlotMapAsync();
                        var matchingSubIds = slotMap.Where(kv => kv.Value == query.SimSlot.Value).Select(kv => kv.Key).ToList();
                        if (matchingSubIds.Count > 0)
                        {
                            var subList = string.Join(",", matchingSubIds);
                            var smsUri = Telephony.Sms.ContentUri;
                            if (smsUri != null)
                            {
                                using var cursor = context.ContentResolver?.Query(
                                    smsUri,
                                    SmsProjection,
                                    $"sub_id IN ({subList})",
                                    null,
                                    "date DESC LIMIT 150");

                                if (cursor != null)
                                {
                                    PopulateResultsFromCursor(cursor, results, archivedIds, query.IncludeArchivedAndSpam, allStarredIds, cancellationToken);
                                }
                            }
                        }
                    }
                    catch { }
                }
                else
                {
                    // Known, Unknown, or All: get from threads directly
                    try
                    {
                        var allThreads = await GetThreadsAsync(cancellationToken);
                        foreach (var t in allThreads)
                        {
                            var isArchived = archivedIds.Contains(t.ThreadId);
                            if (isArchived && !query.IncludeArchivedAndSpam)
                                continue;

                            var isKnown = !string.IsNullOrWhiteSpace(t.ContactName);
                            if (query.FilterKind == SearchFilterKind.Known && !isKnown)
                                continue;
                            if (query.FilterKind == SearchFilterKind.Unknown && isKnown)
                                continue;

                            results[t.ThreadId] = new SearchResultBuilder
                            {
                                ThreadId = t.ThreadId,
                                MessageId = 0,
                                Address = t.Address,
                                ContactName = t.ContactName,
                                Snippet = t.Snippet,
                                Timestamp = t.Timestamp,
                                SubId = t.SubId,
                                IsRead = t.UnreadCount == 0,
                                TotalMatches = 1,
                                IsArchived = isArchived,
                                IsKnown = isKnown
                            };

                            if (results.Count >= 50)
                                break;
                        }
                    }
                    catch { }
                }
            }

            // Fill in missing address/contactName for any result
            foreach (var b in results.Values)
            {
                if (string.IsNullOrEmpty(b.Address))
                {
                    var threadUri = global::Android.Net.Uri.Parse($"content://mms-sms/conversations/{b.ThreadId}");
                    if (threadUri != null)
                    {
                        try
                        {
                            using var threadCursor = context.ContentResolver?.Query(threadUri, ["address", "read", "sub_id"], null, null, "date DESC LIMIT 1");
                            if (threadCursor?.MoveToNext() == true)
                            {
                                var addrCol = threadCursor.GetColumnIndex("address");
                                if (addrCol >= 0)
                                    b.Address = threadCursor.GetString(addrCol) ?? string.Empty;
                                var rCol = threadCursor.GetColumnIndex("read");
                                if (rCol >= 0)
                                    b.IsRead = threadCursor.GetInt(rCol) != 0;
                                var sCol = threadCursor.GetColumnIndex("sub_id");
                                if (sCol >= 0 && !threadCursor.IsNull(sCol))
                                    b.SubId = threadCursor.GetInt(sCol);
                            }
                        }
                        catch { }
                    }
                }

                if (string.IsNullOrEmpty(b.ContactName) && !string.IsNullOrEmpty(b.Address))
                {
                    var key = PhoneNumberNormalizer.ToLookupKey(b.Address);
                    if (contactMap.TryGetValue(key, out var foundName))
                        b.ContactName = foundName;
                    else if (PhoneNumberNormalizer.IsAlphanumeric(b.Address))
                        b.ContactName = b.Address;
                }

                b.IsKnown = !string.IsNullOrWhiteSpace(b.ContactName);
            }

            return results.Values
                .OrderByDescending(r => r.Timestamp)
                .Select(b => new SearchResultChat(
                    b.ThreadId,
                    b.MessageId,
                    b.Address,
                    b.ContactName,
                    b.Snippet,
                    b.Timestamp,
                    b.SubId,
                    b.TotalMatches,
                    b.IsRead,
                    b.IsArchived,
                    b.IsSpam,
                    b.IsStarred,
                    b.IsKnown))
                .ToList();
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

                    var isOutgoing = SmsStatusHelper.IsOutgoingType(type);

                    var hasFailed = SmsStatusHelper.HasFailed(type, status);
                    var isDelivered = SmsStatusHelper.IsDelivered(type, status);
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
                        var target = ReactionHelper.FindReactionTarget(messages, msg, parsed.Snippet);

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
        Task.Run(async () =>
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

            SmsSendTracker.EnsureRegistered(context);

            bool sentOk;
            try
            {
                var parts = smsManager.DivideMessage(text);
                sentOk = parts != null && parts.Count > 1
                    ? await SmsSendTracker.SendMultipartAsync(smsManager, address, parts, context, cancellationToken).ConfigureAwait(false)
                    : await SmsSendTracker.SendSingleAsync(smsManager, address, text, context, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                sentOk = false;
            }

            var values = new ContentValues();
            values.Put(Telephony.Sms.InterfaceConsts.Address, address);
            values.Put(Telephony.Sms.InterfaceConsts.Body, text);
            values.Put(Telephony.Sms.InterfaceConsts.Date, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            values.Put(Telephony.Sms.InterfaceConsts.Read, 1);
            values.Put(Telephony.Sms.InterfaceConsts.Type, sentOk ? (int)SmsMessageType.Sent : (int)SmsMessageType.Failed);
            values.Put(Telephony.Sms.InterfaceConsts.Status, sentOk ? 0 : 64);
            if (subId > 0)
                values.Put("sub_id", subId);

            var sentUri = Telephony.Sms.Sent.ContentUri;
            if (sentUri != null)
                context.ContentResolver?.Insert(sentUri, values);
            return sentOk;
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

    public Task<bool> MarkThreadAsUnreadAsync(long threadId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var context = Microsoft.Maui.ApplicationModel.Platform.AppContext;
            var smsUri = Telephony.Sms.ContentUri;
            if (smsUri == null)
                return false;

            try
            {
                var values = new ContentValues();
                values.Put(Telephony.Sms.InterfaceConsts.Read, 0);
                values.Put(Telephony.Sms.InterfaceConsts.Seen, 0);

                var rows = context.ContentResolver?.Update(
                    smsUri,
                    values,
                    $"{Telephony.Sms.InterfaceConsts.ThreadId} = ?",
                    [threadId.ToString()]);

                return (rows ?? 0) > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }, cancellationToken);

    public Task<bool> DeleteThreadsAsync(IReadOnlyList<long> threadIds, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            if (threadIds == null || threadIds.Count == 0)
                return false;

            var context = Microsoft.Maui.ApplicationModel.Platform.AppContext;
            var smsUri = Telephony.Sms.ContentUri;
            if (smsUri == null)
                return false;

            try
            {
                var idStrings = threadIds.Select(id => id.ToString()).ToArray();
                var placeholders = string.Join(",", threadIds.Select(_ => "?"));
                var deleted = context.ContentResolver?.Delete(
                    smsUri,
                    $"{Telephony.Sms.InterfaceConsts.ThreadId} IN ({placeholders})",
                    idStrings) ?? 0;

                if (_metadataRepo != null)
                {
                    await _metadataRepo.DeleteForThreadsAsync(threadIds);
                }

                return deleted > 0;
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

    private static readonly object CanonicalAddressesLock = new();
    private static Dictionary<long, string>? _cachedCanonicalAddresses;
    private static long _lastCanonicalAddressesTick;

    private static Dictionary<long, string> LoadCanonicalAddresses(Context context)
    {
        lock (CanonicalAddressesLock)
        {
            var now = System.Environment.TickCount64;
            if (_cachedCanonicalAddresses != null && now - _lastCanonicalAddressesTick < 60000)
                return _cachedCanonicalAddresses;
        }

        var map = new Dictionary<long, string>();
        try
        {
            var uri = global::Android.Net.Uri.Parse("content://mms-sms/canonical-addresses");
            if (uri != null)
            {
                using var cursor = context.ContentResolver?.Query(uri, ["_id", "address"], null, null, null);
                if (cursor != null)
                {
                    var idCol = cursor.GetColumnIndex("_id");
                    var addrCol = cursor.GetColumnIndex("address");
                    while (cursor.MoveToNext())
                    {
                        if (idCol >= 0 && addrCol >= 0)
                        {
                            var id = cursor.GetLong(idCol);
                            var addr = cursor.GetString(addrCol);
                            if (!string.IsNullOrEmpty(addr))
                                map[id] = addr;
                        }
                    }
                }
            }
        }
        catch { }

        lock (CanonicalAddressesLock)
        {
            _cachedCanonicalAddresses = map;
            _lastCanonicalAddressesTick = System.Environment.TickCount64;
        }

        return map;
    }

    private static readonly object ContactsLock = new();
    private static Dictionary<string, string>? _cachedContactMap;
    private static long _lastContactsLoadTick;

    private static Dictionary<string, string> LoadContacts(Context context)
    {
        lock (ContactsLock)
        {
            var now = System.Environment.TickCount64;
            if (_cachedContactMap != null && now - _lastContactsLoadTick < 60000)
                return _cachedContactMap;
        }

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
                var rawNum = numCol >= 0 ? cursor.GetString(numCol) : null;
                var name = nameCol >= 0 ? cursor.GetString(nameCol) : null;
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

        lock (ContactsLock)
        {
            _cachedContactMap = map;
            _lastContactsLoadTick = System.Environment.TickCount64;
        }

        return map;
    }

    private static void PopulateResultsFromCursor(
        ICursor cursor,
        Dictionary<long, SearchResultBuilder> results,
        HashSet<long> archivedIds,
        bool includeArchivedAndSpam,
        HashSet<long>? allStarredIds,
        CancellationToken cancellationToken)
    {
        var idCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Id);
        var threadIdCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.ThreadId);
        var addressCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Address);
        var bodyCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Body);
        var dateCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Date);
        var readCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Read);
        var subIdCol = cursor.GetColumnIndex("sub_id");

        while (cursor.MoveToNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var threadId = threadIdCol >= 0 ? cursor.GetLong(threadIdCol) : 0;
            if (threadId <= 0) continue;

            var isArchived = archivedIds.Contains(threadId);
            if (isArchived && !includeArchivedAndSpam)
                continue;

            if (results.Count >= 60 && !results.ContainsKey(threadId))
                continue;

            var msgId = idCol >= 0 ? cursor.GetLong(idCol) : 0;
            var dateMs = dateCol >= 0 ? cursor.GetLong(dateCol) : 0;
            var read = readCol >= 0 ? cursor.GetInt(readCol) : 1;
            var subId = subIdCol >= 0 && !cursor.IsNull(subIdCol) ? cursor.GetInt(subIdCol) : 1;
            var address = addressCol >= 0 ? cursor.GetString(addressCol) ?? string.Empty : string.Empty;
            var body = bodyCol >= 0 ? cursor.GetString(bodyCol) ?? string.Empty : string.Empty;

            if (!results.TryGetValue(threadId, out var builder))
            {
                results[threadId] = new SearchResultBuilder
                {
                    ThreadId = threadId,
                    MessageId = msgId,
                    Address = address,
                    Snippet = body,
                    Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(dateMs).LocalDateTime,
                    SubId = subId,
                    IsRead = read != 0,
                    TotalMatches = 1,
                    IsArchived = isArchived,
                    IsStarred = allStarredIds != null && allStarredIds.Contains(msgId)
                };
            }
            else
            {
                builder.TotalMatches++;
            }
        }
    }

    private static HashSet<long> GetArchivedThreadIds()
    {
        try
        {
            var raw = Microsoft.Maui.Storage.Preferences.Default.Get<string>("archived_threads_v1", string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
                return [];

            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => long.TryParse(s, out var id) ? id : -1)
                      .Where(id => id > 0)
                      .ToHashSet();
        }
        catch
        {
            return [];
        }
    }

    private sealed class SearchResultBuilder
    {
        public long ThreadId { get; set; }
        public long MessageId { get; set; }
        public string Address { get; set; } = string.Empty;
        public string? ContactName { get; set; }
        public string Snippet { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public int SubId { get; set; }
        public bool IsRead { get; set; }
        public int TotalMatches { get; set; }
        public bool IsArchived { get; set; }
        public bool IsSpam { get; set; }
        public bool IsStarred { get; set; }
        public bool IsKnown { get; set; }
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
