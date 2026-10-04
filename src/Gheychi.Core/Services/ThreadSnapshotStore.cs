using System.Text.Json;
using Gheychi.Core.Models;

namespace Gheychi.Core.Services;

/// <summary>
/// Persists the last inbox thread list so the next launch can paint the inbox immediately
/// and refresh it from the SMS provider in the background.
/// </summary>
public static class ThreadSnapshotStore
{
    private const int MaxThreads = 300;

    public static IReadOnlyList<SmsThread> TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
                return [];

            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<List<SmsThread>>(stream) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(string path, IReadOnlyList<SmsThread> threads)
    {
        try
        {
            var temp = path + ".tmp";
            using (var stream = File.Create(temp))
            {
                JsonSerializer.Serialize(stream, threads.Take(MaxThreads).ToList());
            }
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
        }
    }
}
