using Gheychi.App.Platforms.Android.Services;
using Gheychi.App.ViewModels;
using Gheychi.Core.Notifications;
using Gheychi.Core.Scheduling;
using Gheychi.Core.Services;
using Gheychi.Core.Spam;
using Gheychi.Core.Updates;
using Gheychi.Infrastructure.Data;
using Gheychi.Infrastructure.Spam;
using Gheychi.Infrastructure.Updates;

namespace Gheychi.App;

internal static class AppServices
{
    public static IServiceProvider Build()
    {
        var services = new AppServiceProvider();
        var databasePath = Path.Combine(FileSystem.AppDataDirectory, "gheychi.db");

        services.Singleton<IDateFormattingService>(() => new DateFormattingService());
        services.Singleton<IMessageMetadataRepository>(() => new MessageMetadataRepository(databasePath));
        services.Singleton<ISmsService>(() => new AndroidSmsService(services.Get<IMessageMetadataRepository>()));

        services.Singleton<ISpamSettings>(() => new Services.PreferencesSpamSettings());
        services.Singleton<ITrustedSenders>(() => new Services.PreferencesTrustedSenders());
        services.Singleton<IBlockedSenders>(() => new Services.PreferencesBlockedSenders());
        services.Singleton<ISpamMessageRepository>(() => new SpamMessageRepository(databasePath));
        services.Singleton<ITrashRepository>(() => new TrashRepository(databasePath));
        services.Singleton<ISpamStatsRepository>(() => new SpamStatRepository(databasePath));
        services.Singleton(() => new SpamModelStore(
            Path.Combine(FileSystem.AppDataDirectory, "spam-model"),
            name => FileSystem.OpenAppPackageFileAsync($"SpamModel/{name}")));
        services.Singleton(() => new MlNetSpamClassifier(services.Get<SpamModelStore>()));
        services.Singleton<ISpamClassifier>(services.Get<MlNetSpamClassifier>);
        services.Singleton<ISpamModelUpdater>(services.Get<MlNetSpamClassifier>);
        services.Singleton<ISpamModelSource>(() => new GitHubSpamModelSource(
            new HttpClient { Timeout = TimeSpan.FromMinutes(2) },
            FileSystem.CacheDirectory));
        services.Singleton<IAppReleaseSource>(() => new GitHubAppReleaseSource(
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
            GitHubAppReleaseSource.ChooseAbi(global::Android.OS.Build.SupportedAbis ?? [])));
        services.Singleton<IAppUpdateState>(() => new Services.PreferencesAppUpdateState());
        services.Singleton(() => new AppUpdates(
            services.Get<IAppReleaseSource>(),
            services.Get<IAppUpdateState>(),
            AppInfo.Current.VersionString));
        services.Singleton<ISpamReporter>(() => new GoogleFormSpamReporter(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }));
        services.Singleton<ISpamModelUpdateState>(() => new Services.PreferencesSpamModelUpdateState());
        services.Singleton(() => new SpamModelUpdates(
            services.Get<ISpamModelSource>(),
            services.Get<ISpamModelUpdater>(),
            services.Get<ISpamModelUpdateState>()));
        services.Singleton(() => new SpamDetector(
            services.Get<ISpamClassifier>(),
            services.Get<ISpamSettings>(),
            services.Get<ITrustedSenders>()));
        services.Singleton(() => new SpamViewModel(
            services.Get<ISpamMessageRepository>(),
            services.Get<ISpamStatsRepository>(),
            services.Get<ISmsService>(),
            services.Get<ISpamSettings>(),
            services.Get<ITrustedSenders>(),
            services.Get<IDateFormattingService>()));

        services.Singleton(() => new ActiveChatState());
        services.Singleton<IActiveChatState>(services.Get<ActiveChatState>);
        services.Singleton<IThreadSettings>(() => new Services.PreferencesThreadSettings());
        services.Singleton<IDraftStore>(() => new Services.PreferencesDraftStore());
        services.Singleton<IPinStore>(() => new Services.PreferencesPinStore());
        services.Singleton<IScheduledSendStore>(() => new Services.PreferencesScheduledSendStore());
        services.Singleton(() => new Services.PreferencesFolderStore());
        services.Singleton<IScheduledSendScheduler>(() => new ScheduledSendAlarm());
        // A new reason to hold back a notification (archived, snoozed) is one more INotificationRule here.
        // Spam never gets this far: it is quarantined before the message is stored.
        services.Singleton(() => new NotificationPolicy(
        [
            new ActiveChatRule(services.Get<IActiveChatState>()),
            new SnoozeRule(services.Get<IThreadSettings>())
        ]));
        services.Singleton(() => new MessagesViewModel(services.Get<ISmsService>(), services.Get<IDateFormattingService>()));
        services.Transient(() => new ChatViewModel(
            smsService: services.Get<ISmsService>(),
            dateFormatter: services.Get<IDateFormattingService>(),
            metadataRepo: services.Get<IMessageMetadataRepository>()));
        services.Transient(() => new SearchViewModel(services.Get<ISmsService>(), services.Get<IDateFormattingService>()));

        return services;
    }
}

/// <summary>
/// The services, wired by hand. A DI container finds the constructors by reflection the first time each service is
/// asked for, and that was paid for while the app started.
/// </summary>
internal sealed class AppServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, Func<object>> _factories = [];

    public object? GetService(Type serviceType) =>
        _factories.TryGetValue(serviceType, out var factory) ? factory() : null;

    public T Get<T>() where T : class => (T)(GetService(typeof(T))
        ?? throw new InvalidOperationException($"No service for type '{typeof(T)}' has been registered."));

    /// <summary>Made on first use; a factory that throws is tried again on the next use.</summary>
    public void Singleton<T>(Func<T> create) where T : class
    {
        T? instance = null;
        var gate = new object();
        _factories[typeof(T)] = () =>
        {
            if (Volatile.Read(ref instance) is { } made)
                return made;

            lock (gate)
            {
                made = instance;
                if (made is null)
                {
                    made = create();
                    Volatile.Write(ref instance, made);
                }

                return made;
            }
        };
    }

    public void Transient<T>(Func<T> create) where T : class => _factories[typeof(T)] = create;
}
