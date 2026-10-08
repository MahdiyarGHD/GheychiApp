using System.Globalization;
using Android.Content;

namespace Gheychi.App;

/// <summary>
/// Key-value settings in the same SharedPreferences file the MAUI build used, so an update keeps what the user chose.
/// </summary>
public sealed class Preferences
{
    public static Preferences Default { get; } = new();

    private readonly Lazy<ISharedPreferences> _store = new(() =>
        Platform.AppContext.GetSharedPreferences($"{Platform.AppContext.PackageName}.microsoft.maui.essentials.preferences", FileCreationMode.Private)!);

    private ISharedPreferences Store => _store.Value;

    private Preferences()
    {
    }

    public bool ContainsKey(string key) => Store.Contains(key);

    public void Remove(string key) => Store.Edit()!.Remove(key)!.Apply();

    public T Get<T>(string key, T defaultValue)
    {
        if (!Store.Contains(key))
            return defaultValue;

        // By the declared type, not the default's: a null default ("string?") has no runtime type to switch on.
        object? value;
        if (typeof(T) == typeof(string))
            value = Store.GetString(key, defaultValue as string);
        else if (typeof(T) == typeof(int))
            value = Store.GetInt(key, (int)(object)defaultValue!);
        else if (typeof(T) == typeof(bool))
            value = Store.GetBoolean(key, (bool)(object)defaultValue!);
        else if (typeof(T) == typeof(long))
            value = Store.GetLong(key, (long)(object)defaultValue!);
        else if (typeof(T) == typeof(float))
            value = Store.GetFloat(key, (float)(object)defaultValue!);
        else if (typeof(T) == typeof(double))
            value = Java.Lang.Double.LongBitsToDouble(Store.GetLong(key, Java.Lang.Double.DoubleToLongBits((double)(object)defaultValue!)));
        else if (typeof(T) == typeof(DateTime))
            value = ReadDateTime(key, (DateTime)(object)defaultValue!);
        else
            throw new NotSupportedException($"Preferences cannot hold a {typeof(T)}.");

        return (T)value!;
    }

    public void Set<T>(string key, T value)
    {
        var editor = Store.Edit()!;
        switch (value)
        {
            case null:
                editor.Remove(key);
                break;
            case string s:
                editor.PutString(key, s);
                break;
            case int i:
                editor.PutInt(key, i);
                break;
            case bool b:
                editor.PutBoolean(key, b);
                break;
            case long l:
                editor.PutLong(key, l);
                break;
            case float f:
                editor.PutFloat(key, f);
                break;
            case double d:
                editor.PutLong(key, Java.Lang.Double.DoubleToLongBits(d));
                break;
            case DateTime t:
                editor.PutString(key, t.ToBinary().ToString(CultureInfo.InvariantCulture));
                break;
            default:
                throw new NotSupportedException($"Preferences cannot hold a {typeof(T)}.");
        }

        editor.Apply();
    }

    // MAUI stored a DateTime as its binary value in a string; accept a long as well.
    private DateTime ReadDateTime(string key, DateTime fallback)
    {
        try
        {
            return long.TryParse(Store.GetString(key, null), NumberStyles.Integer, CultureInfo.InvariantCulture, out var binary)
                ? DateTime.FromBinary(binary)
                : fallback;
        }
        catch (Java.Lang.ClassCastException)
        {
            return DateTime.FromBinary(Store.GetLong(key, fallback.ToBinary()));
        }
    }
}
