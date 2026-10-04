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
            var contactsTask = Task.Run(() => LoadContacts(context), cancellationToken);
            var canonicalAddresses = LoadCanonicalAddresses(context);
            var contactMap = contactsTask.GetAwaiter().GetResult();

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

    private const int MaxSearchResults = 200;
    private const int MaxSearchMessageRows = 5000;

    private readonly record struct MessageRow(long Id, long ThreadId, string Address, string Body, long DateMs, bool IsRead, int SubId);

    private sealed class SearchMatch
    {
        public required MessageRow Newest { get; init; }
        public int Count { get; set; }
    }

    /// <summary>
    /// Searches conversations. Text is matched with a plain LIKE over the SMS table (the
    /// provider's FTS search URI only matches whole-word prefixes and is missing on many devices);
    /// contact name / number matches are added on top. The active filter combines with the text
    /// instead of replacing it.
    /// </summary>
    public Task<IReadOnlyList<SearchResultChat>> SearchChatsAsync(SearchQuery query, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<SearchResultChat>>(async () =>
        {
            var context = Microsoft.Maui.ApplicationModel.Platform.AppContext;
            var archivedIds = GetArchivedThreadIds();
            var variants = SearchTextHelper.BuildVariants(query.Text);
            var hasText = variants.Count > 0;
            var kind = query.FilterKind;

            var threads = new Dictionary<long, SmsThread>();
            foreach (var t in await GetThreadsAsync(cancellationToken).ConfigureAwait(false))
                threads[t.ThreadId] = t;

            var starredIds = await GetStarredMessageIdsAsync().ConfigureAwait(false);
            var starredThreadIds = starredIds.Count > 0
                ? QueryThreadIds(context, starredIds, cancellationToken)
                : [];

            var simSubIds = new HashSet<int>();
            if (kind == SearchFilterKind.Sim)
            {
                var slotMap = await GetSimSlotMapAsync().ConfigureAwait(false);
                foreach (var (subId, slot) in slotMap)
                {
                    if (query.SimSlot.HasValue && slot == query.SimSlot.Value)
                        simSubIds.Add(subId);
                }

                if (simSubIds.Count == 0)
                    return [];
            }

            if (kind == SearchFilterKind.Starred && starredIds.Count == 0)
                return [];

            var matches = new Dictionary<long, SearchMatch>();
            var messageLevel = hasText || kind is SearchFilterKind.Unread or SearchFilterKind.Starred or SearchFilterKind.Sim;
            if (messageLevel)
            {
                var conditions = new List<string>();
                var args = new List<string>();

                if (hasText)
                {
                    conditions.Add("(" + string.Join(" OR ", variants.Select(_ => $"{Telephony.Sms.InterfaceConsts.Body} LIKE ?")) + ")");
                    args.AddRange(variants.Select(v => $"%{v}%"));
                }

                if (kind == SearchFilterKind.Unread)
                    conditions.Add($"{Telephony.Sms.InterfaceConsts.Read} = 0 AND {Telephony.Sms.InterfaceConsts.Type} = {(int)SmsMessageType.Inbox}");

                if (kind == SearchFilterKind.Sim)
                    conditions.Add($"sub_id IN ({string.Join(",", simSubIds)})");

                var idChunks = new List<string?>();
                if (kind == SearchFilterKind.Starred)
                    idChunks.AddRange(starredIds.Chunk(400).Select(c => (string?)$"{Telephony.Sms.InterfaceConsts.Id} IN ({string.Join(",", c)})"));
                else
                    idChunks.Add(null);

                foreach (var idClause in idChunks)
                {
                    var all = new List<string>(conditions);
                    if (idClause is not null)
                        all.Add(idClause);
                    var selection = all.Count == 0 ? null : string.Join(" AND ", all.Select(c => $"({c})"));

                    ForEachMessage(context, selection, args.Count == 0 ? null : args.ToArray(), MaxSearchMessageRows, cancellationToken, row =>
                    {
                        if (archivedIds.Contains(row.ThreadId) && !query.IncludeArchivedAndSpam)
                            return true;

                        // LIKE treats % and _ as wildcards; confirm the real substring.
                        if (hasText && !SearchTextHelper.ContainsAny(row.Body, variants))
                            return true;

                        if (matches.TryGetValue(row.ThreadId, out var match))
                        {
                            match.Count++;
                            if (row.DateMs > match.Newest.DateMs)
                                matches[row.ThreadId] = new SearchMatch { Newest = row, Count = match.Count };
                        }
                        else
                        {
                            matches[row.ThreadId] = new SearchMatch { Newest = row, Count = 1 };
                        }
                        return true;
                    });
                }
            }

            var contactMap = LoadContacts(context);
            var candidates = new Dictionary<long, SmsThread>();

            foreach (var (threadId, match) in matches)
            {
                if (!threads.TryGetValue(threadId, out var thread))
                    thread = ThreadFromMessage(match.Newest, contactMap);
                candidates[threadId] = thread;
            }

            if (hasText && kind != SearchFilterKind.Starred)
            {
                foreach (var thread in threads.Values)
                {
                    if (candidates.ContainsKey(thread.ThreadId))
                        continue;
                    if (archivedIds.Contains(thread.ThreadId) && !query.IncludeArchivedAndSpam)
                        continue;
                    if (!SearchTextHelper.ContainsAny(thread.ContactName, variants) && !AddressMatches(thread.Address, variants))
                        continue;
                    if (kind == SearchFilterKind.Unread && thread.UnreadCount == 0)
                        continue;
                    if (kind == SearchFilterKind.Sim && !simSubIds.Contains(thread.SubId))
                        continue;

                    candidates[thread.ThreadId] = thread;
                }
            }
            else if (!messageLevel)
            {
                foreach (var thread in threads.Values)
                {
                    if (archivedIds.Contains(thread.ThreadId) && !query.IncludeArchivedAndSpam)
                        continue;
                    candidates[thread.ThreadId] = thread;
                }
            }

            var results = new List<SearchResultChat>(candidates.Count);
            foreach (var (threadId, thread) in candidates)
            {
                var isKnown = !string.IsNullOrWhiteSpace(thread.ContactName);
                if (kind == SearchFilterKind.Known && !isKnown)
                    continue;
                if (kind == SearchFilterKind.Unknown && isKnown)
                    continue;

                var hasMatch = matches.TryGetValue(threadId, out var match);
                var newest = match?.Newest;
                var timestamp = hasMatch ? DateTimeOffset.FromUnixTimeMilliseconds(newest!.Value.DateMs).LocalDateTime : thread.Timestamp;

                results.Add(new SearchResultChat(
                    threadId,
                    hasMatch ? newest!.Value.Id : 0,
                    thread.Address,
                    thread.ContactName,
                    hasMatch ? newest!.Value.Body : thread.Snippet,
                    timestamp,
                    hasMatch && newest!.Value.SubId > 0 ? newest.Value.SubId : thread.SubId,
                    hasMatch ? match!.Count : 1,
                    thread.UnreadCount == 0,
                    archivedIds.Contains(threadId),
                    false,
                    starredThreadIds.Contains(threadId),
                    isKnown));
            }

            return results
                .OrderByDescending(r => r.Timestamp)
                .Take(MaxSearchResults)
                .ToList();
        }, cancellationToken);

    private async Task<IReadOnlyList<long>> GetStarredMessageIdsAsync()
    {
        if (_metadataRepo == null)
            return [];

        try
        {
            return await _metadataRepo.GetAllStarredMessageIdsAsync().ConfigureAwait(false);
        }
        catch
        {
            return [];
        }
    }

    private static SmsThread ThreadFromMessage(MessageRow row, Dictionary<string, string> contactMap)
    {
        string? contactName = null;
        var key = PhoneNumberNormalizer.ToLookupKey(row.Address);
        if (contactMap.TryGetValue(key, out var found))
            contactName = found;
        else if (PhoneNumberNormalizer.IsAlphanumeric(row.Address))
            contactName = row.Address;

        return new SmsThread(
            row.ThreadId,
            row.Address,
            contactName,
            row.Body,
            DateTimeOffset.FromUnixTimeMilliseconds(row.DateMs).LocalDateTime,
            1,
            row.IsRead ? 0 : 1,
            false,
            row.SubId);
    }

    private static bool AddressMatches(string address, IReadOnlyList<string> variants)
    {
        if (string.IsNullOrEmpty(address))
            return false;

        if (SearchTextHelper.ContainsAny(address, variants))
            return true;

        var addressDigits = new string(address.Where(char.IsAsciiDigit).ToArray());
        foreach (var variant in variants)
        {
            var queryDigits = new string(variant.Where(char.IsAsciiDigit).ToArray());
            if (queryDigits.Length < 3)
                continue;

            // 0912... typed locally must match +98912... stored internationally.
            if (addressDigits.Contains(queryDigits, StringComparison.Ordinal) ||
                (queryDigits.StartsWith('0') && addressDigits.Contains(queryDigits[1..], StringComparison.Ordinal)))
                return true;
        }

        return false;
    }

    private static HashSet<long> QueryThreadIds(Context context, IReadOnlyList<long> messageIds, CancellationToken cancellationToken)
    {
        var threadIds = new HashSet<long>();
        foreach (var chunk in messageIds.Chunk(400))
        {
            try
            {
                ForEachMessage(
                    context,
                    $"{Telephony.Sms.InterfaceConsts.Id} IN ({string.Join(",", chunk)})",
                    null,
                    chunk.Length,
                    cancellationToken,
                    row =>
                    {
                        threadIds.Add(row.ThreadId);
                        return true;
                    });
            }
            catch (System.OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
        }
        return threadIds;
    }

    private static void ForEachMessage(
        Context context,
        string? selection,
        string[]? selectionArgs,
        int maxRows,
        CancellationToken cancellationToken,
        Func<MessageRow, bool> visit)
    {
        var smsUri = Telephony.Sms.ContentUri;
        if (smsUri == null)
            return;

        using var cursor = context.ContentResolver?.Query(smsUri, SmsProjection, selection, selectionArgs, "date DESC");
        if (cursor == null)
            return;

        var idCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Id);
        var threadCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.ThreadId);
        var addressCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Address);
        var bodyCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Body);
        var dateCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Date);
        var readCol = cursor.GetColumnIndex(Telephony.Sms.InterfaceConsts.Read);
        var subIdCol = cursor.GetColumnIndex("sub_id");

        var rows = 0;
        while (cursor.MoveToNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rows++ >= maxRows)
                break;

            var threadId = threadCol >= 0 ? cursor.GetLong(threadCol) : 0;
            if (threadId <= 0)
                continue;

            var row = new MessageRow(
                idCol >= 0 ? cursor.GetLong(idCol) : 0,
                threadId,
                addressCol >= 0 ? cursor.GetString(addressCol) ?? string.Empty : string.Empty,
                bodyCol >= 0 ? cursor.GetString(bodyCol) ?? string.Empty : string.Empty,
                dateCol >= 0 ? cursor.GetLong(dateCol) : 0,
                readCol < 0 || cursor.GetInt(readCol) != 0,
                subIdCol >= 0 && !cursor.IsNull(subIdCol) ? cursor.GetInt(subIdCol) : 0);

            if (!visit(row))
                break;
        }
    }

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
