namespace Gheychi.App.Controls;

/// <summary>Native settings for the RecyclerView behind a CollectionView.</summary>
internal static class ListTuning
{
    /// <summary>
    /// For a list that fills a star row, so its size never depends on its rows. Without it every inserted,
    /// removed or moved row (a message arriving, an inbox refresh, a history page) asks the whole window to
    /// lay out again; with it only the list does.
    /// </summary>
    public static void UseFixedSize(CollectionView list)
    {
        list.HandlerChanged += (_, _) => ApplyFixedSize(list);
        ApplyFixedSize(list);
    }

    private static void ApplyFixedSize(CollectionView list)
    {
#if ANDROID
        if (list.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView recycler)
            recycler.HasFixedSize = true;
#endif
    }
}
