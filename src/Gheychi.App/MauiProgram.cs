using Gheychi.App.Platforms.Android.Services;
using Gheychi.App.ViewModels;
using Gheychi.Core.Notifications;
using Gheychi.Core.Services;
using Microsoft.Extensions.Logging;

namespace Gheychi.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        Localization.CultureService.ApplySystemCulture();
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

        builder.Services.AddSingleton<IDateFormattingService, DateFormattingService>();
        builder.Services.AddSingleton<IMessageMetadataRepository>(_ =>
            new Gheychi.Infrastructure.Data.MessageMetadataRepository(Path.Combine(FileSystem.AppDataDirectory, "gheychi.db")));
        builder.Services.AddSingleton<ISmsService, AndroidSmsService>();

        // A new reason to hold back a notification (archived, snoozed, spam) is one more INotificationRule here.
        builder.Services.AddSingleton<ActiveChatState>();
        builder.Services.AddSingleton<IActiveChatState>(sp => sp.GetRequiredService<ActiveChatState>());
        builder.Services.AddSingleton<INotificationRule, ActiveChatRule>();
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
