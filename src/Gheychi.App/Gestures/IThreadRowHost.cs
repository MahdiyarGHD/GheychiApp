using Gheychi.App.ViewModels;

namespace Gheychi.App.Gestures;

/// <summary>A screen showing thread rows; <see cref="Behaviors.ThreadHoldBehavior"/> reports taps and long-presses to the nearest one.</summary>
public interface IThreadRowHost
{
    void OnThreadRowTappedDirect(ThreadItem thread);

    void OnThreadRowHeldDirect(ThreadItem thread);
}
