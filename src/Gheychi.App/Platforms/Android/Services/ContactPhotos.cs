using Android;
using Android.Content.PM;
using Android.Provider;
using AndroidX.Core.Content;
using Avalonia.Media.Imaging;
using Gheychi.Core.Services;

namespace Gheychi.App.Platforms.Android.Services;

/// <summary>
/// The photos of the phone's contacts, found by phone number like their names are. The numbers and photo addresses of all
/// contacts are read in one query and kept for a minute; the pictures are decoded small, once, and kept.
/// </summary>
internal static class ContactPhotos
{
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(1);
    private const int SmallWidth = 160;
    private const int LargeWidth = 400;

    private readonly record struct PhotoUris(string? Thumbnail, string? Full);

    private sealed record Entry(Task<Bitmap?> Photo, long At);

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Cache = new();
    private static Dictionary<string, PhotoUris>? _uris;
    private static long _urisAt;

    /// <summary>The contact's picture, or null when there is none (or no permission). <paramref name="large"/> is for a big avatar.</summary>
    public static Task<Bitmap?> GetAsync(string address, bool large)
    {
        if (PhoneNumberNormalizer.IsAlphanumeric(address) || PhoneNumberNormalizer.ToLookupKey(address) is not { Length: > 0 } key)
            return Task.FromResult<Bitmap?>(null);

        var cacheKey = (large ? "L" : "S") + key;
        lock (Gate)
        {
            var now = Environment.TickCount64;
            // A missing picture is looked for again after a minute: the contact may have been given one.
            if (Cache.TryGetValue(cacheKey, out var known)
                && (!known.Photo.IsCompleted || known.Photo.Result is not null || now - known.At < Fresh.TotalMilliseconds))
                return known.Photo;

            var photo = Task.Run(() => Load(key, large));
            Cache[cacheKey] = new Entry(photo, now);
            return photo;
        }
    }

    private static Bitmap? Load(string key, bool large)
    {
        try
        {
            if (!ReadUris().TryGetValue(key, out var uris))
                return null;

            var context = Platform.AppContext;
            foreach (var uri in large ? new[] { uris.Full, uris.Thumbnail } : new[] { uris.Thumbnail, uris.Full })
            {
                if (string.IsNullOrEmpty(uri))
                    continue;

                try
                {
                    using var stream = context.ContentResolver?.OpenInputStream(global::Android.Net.Uri.Parse(uri)!);
                    if (stream is not null)
                        return Bitmap.DecodeToWidth(stream, large ? LargeWidth : SmallWidth);
                }
                catch (Exception)
                {
                    // This address has no picture; the next one may.
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Contact photo failed: {ex}");
        }

        return null;
    }

    private static Dictionary<string, PhotoUris> ReadUris()
    {
        lock (Gate)
        {
            if (_uris is not null && Environment.TickCount64 - _urisAt < Fresh.TotalMilliseconds)
                return _uris;
        }

        var map = new Dictionary<string, PhotoUris>(StringComparer.Ordinal);
        var context = Platform.AppContext;
        if (ContextCompat.CheckSelfPermission(context, Manifest.Permission.ReadContacts) == Permission.Granted
            && ContactsContract.CommonDataKinds.Phone.ContentUri is { } contentUri)
        {
            using var cursor = context.ContentResolver?.Query(
                contentUri,
                [ContactsContract.CommonDataKinds.Phone.Number, "photo_thumb_uri", "photo_uri"],
                null,
                null,
                null);

            if (cursor is not null)
            {
                var numberColumn = cursor.GetColumnIndex(ContactsContract.CommonDataKinds.Phone.Number);
                var thumbColumn = cursor.GetColumnIndex("photo_thumb_uri");
                var fullColumn = cursor.GetColumnIndex("photo_uri");
                while (cursor.MoveToNext())
                {
                    var number = numberColumn >= 0 ? cursor.GetString(numberColumn) : null;
                    var thumb = thumbColumn >= 0 ? cursor.GetString(thumbColumn) : null;
                    var full = fullColumn >= 0 ? cursor.GetString(fullColumn) : null;
                    if (string.IsNullOrEmpty(number) || (thumb is null && full is null))
                        continue;

                    map.TryAdd(PhoneNumberNormalizer.ToLookupKey(number), new PhotoUris(thumb, full));
                }
            }
        }

        lock (Gate)
        {
            _uris = map;
            _urisAt = Environment.TickCount64;
        }

        return map;
    }
}
