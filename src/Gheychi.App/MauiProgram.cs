using Gheychi.App.Platforms.Android.Services;
using Gheychi.App.ViewModels;
using Gheychi.Core.Notifications;
using Gheychi.Core.Services;
using Gheychi.Core.Spam;
using Gheychi.Infrastructure.Data;
using Gheychi.Infrastructure.Spam;
using Microsoft.Extensions.Logging;

namespace Gheychi.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        Localization.CultureService.ApplyCulture();
        SQLitePCL.Batteries_V2.Init();

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("PlusJakartaSans-Regular.ttf", "PlusJakartaSans");
                fonts.AddFont("PlusJakartaSans-SemiBold.ttf", "PlusJakartaSansSemiBold");
                fonts.AddFont("Vazirmatn-Regular.ttf", "Vazirmatn");
                fonts.AddFont("Vazirmatn-SemiBold.ttf", "VazirmatnSemiBold");
            });

        var databasePath = Path.Combine(FileSystem.AppDataDirectory, "gheychi.db");
        builder.Services.AddSingleton<IDateFormattingService, DateFormattingService>();
        builder.Services.AddSingleton<IMessageMetadataRepository>(_ =>
            new Gheychi.Infrastructure.Data.MessageMetadataRepository(databasePath));
        builder.Services.AddSingleton<ISmsService, AndroidSmsService>();

        builder.Services.AddSingleton<ISpamSettings, Services.PreferencesSpamSettings>();
        builder.Services.AddSingleton<ITrustedSenders, Services.PreferencesTrustedSenders>();
        builder.Services.AddSingleton<ISpamMessageRepository>(_ => new SpamMessageRepository(databasePath));
        builder.Services.AddSingleton<ISpamStatsRepository>(_ => new SpamStatRepository(databasePath));
        builder.Services.AddSingleton(_ => new SpamModelStore(
            Path.Combine(FileSystem.AppDataDirectory, "spam-model"),
            name => FileSystem.OpenAppPackageFileAsync($"SpamModel/{name}")));
        builder.Services.AddSingleton<MlNetSpamClassifier>();
        builder.Services.AddSingleton<ISpamClassifier>(sp => sp.GetRequiredService<MlNetSpamClassifier>());
        builder.Services.AddSingleton<ISpamModelUpdater>(sp => sp.GetRequiredService<MlNetSpamClassifier>());
        builder.Services.AddSingleton<ISpamModelSource>(_ => new GitHubSpamModelSource(
            new HttpClient { Timeout = TimeSpan.FromMinutes(2) },
            FileSystem.CacheDirectory));
        builder.Services.AddSingleton<ISpamModelUpdateState, Services.PreferencesSpamModelUpdateState>();
        builder.Services.AddSingleton(sp => new SpamModelUpdates(
            sp.GetRequiredService<ISpamModelSource>(),
            sp.GetRequiredService<ISpamModelUpdater>(),
            sp.GetRequiredService<ISpamModelUpdateState>()));
        builder.Services.AddSingleton<SpamDetector>();
        builder.Services.AddSingleton<SpamViewModel>();

        // A new reason to hold back a notification (archived, snoozed) is one more INotificationRule here.
        // Spam never gets this far: it is quarantined before the message is stored.
        builder.Services.AddSingleton<ActiveChatState>();
        builder.Services.AddSingleton<IActiveChatState>(sp => sp.GetRequiredService<ActiveChatState>());
        builder.Services.AddSingleton<IThreadSettings, Services.PreferencesThreadSettings>();
        builder.Services.AddSingleton<INotificationRule, ActiveChatRule>();
        builder.Services.AddSingleton<INotificationRule, SnoozeRule>();
        builder.Services.AddSingleton<NotificationPolicy>();
        builder.Services.AddSingleton<MessagesViewModel>();
        builder.Services.AddTransient<ChatViewModel>();
        builder.Services.AddTransient<SearchViewModel>();

#if ANDROID
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoUnderline", static (handler, _) =>
        {
            if (handler.PlatformView.Background is not null)
                handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
        });

        Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("NoUnderline", static (handler, editor) =>
        {
            if (handler.PlatformView.Background is not null)
                handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
            handler.PlatformView.SetPadding(0, 0, 0, 0);
            handler.PlatformView.SetIncludeFontPadding(false);
            if (string.IsNullOrEmpty(editor.Text) || !editor.Text.Contains('\n'))
            {
                handler.PlatformView.Gravity = Android.Views.GravityFlags.CenterVertical | Android.Views.GravityFlags.Start;
            }
            else
            {
                handler.PlatformView.Gravity = Android.Views.GravityFlags.Top | Android.Views.GravityFlags.Start;
            }
        });
#endif

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
