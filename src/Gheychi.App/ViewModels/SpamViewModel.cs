using System.Globalization;
using Gheychi.App.Platforms.Android.Receivers;
using Gheychi.Core.Services;
using Gheychi.Core.Spam;

namespace Gheychi.App.ViewModels;

public sealed class SpamViewModel
{
    private readonly ISpamMessageRepository _repository;
    private readonly IDateFormattingService _dateFormatter;
    private bool _loading;
    private bool _loaded;
    private bool _reloadPending;

    public SpamViewModel(ISpamMessageRepository repository, IDateFormattingService dateFormatter)
    {
        _repository = repository;
        _dateFormatter = dateFormatter;
        SmsDeliverReceiver.SpamReceived += OnSpamReceived;
    }

    public FastObservableCollection<SpamItem> Items { get; } = new();

    /// <summary>Call from the UI thread.</summary>
    public async Task LoadAsync()
    {
        if (_loading)
        {
            _reloadPending = true;
            return;
        }

        _loading = true;
        try
        {
            do
            {
                _reloadPending = false;
                var items = await Task.Run(async () =>
                {
                    var messages = await _repository.GetAllAsync();
                    var now = DateTime.Now;
                    var culture = CultureInfo.CurrentUICulture;
                    return messages.Select(m => ToItem(m, now, culture)).ToList();
                });
                Items.Reset(items);
                _loaded = true;
            }
            while (_reloadPending);
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnSpamReceived(SpamMessage message)
    {
        if (_loading)
        {
            _reloadPending = true;
            return;
        }

        if (!_loaded || Items.Any(i => i.Id == message.Id))
            return;

        Items.Insert(0, ToItem(message, DateTime.Now, CultureInfo.CurrentUICulture));
    }

    private SpamItem ToItem(SpamMessage message, DateTime now, CultureInfo culture) =>
        new()
        {
            Id = message.Id,
            Sender = PhoneNumberNormalizer.FormatDisplay(message.Address),
            Time = _dateFormatter.FormatThreadTime(message.Timestamp, now, culture),
            Preview = message.Body
        };
}
