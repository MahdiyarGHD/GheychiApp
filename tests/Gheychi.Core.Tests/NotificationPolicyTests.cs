using Xunit;
using Gheychi.Core.Notifications;

namespace Gheychi.Core.Tests;

public class NotificationPolicyTests
{
    private static readonly IncomingMessage Message = new(7, "+15550100", "hi", 1_000, 1);

    private sealed class FixedRule(NotificationDecision decision) : INotificationRule
    {
        public int Calls { get; private set; }

        public NotificationDecision Evaluate(IncomingMessage message)
        {
            Calls++;
            return decision;
        }
    }

    [Fact]
    public void No_rules_shows()
    {
        Assert.Equal(NotificationDecision.Show, new NotificationPolicy([]).Decide(Message));
    }

    [Fact]
    public void Most_restrictive_decision_wins()
    {
        var policy = new NotificationPolicy(
        [
            new FixedRule(NotificationDecision.Show),
            new FixedRule(NotificationDecision.Silent),
            new FixedRule(NotificationDecision.Show)
        ]);

        Assert.Equal(NotificationDecision.Silent, policy.Decide(Message));
    }

    [Fact]
    public void Suppress_wins_over_silent_and_stops_evaluating()
    {
        var after = new FixedRule(NotificationDecision.Show);
        var policy = new NotificationPolicy(
        [
            new FixedRule(NotificationDecision.Silent),
            new FixedRule(NotificationDecision.Suppress),
            after
        ]);

        Assert.Equal(NotificationDecision.Suppress, policy.Decide(Message));
        Assert.Equal(0, after.Calls);
    }

    [Fact]
    public void Active_chat_rule_suppresses_only_the_open_visible_thread()
    {
        var state = new ActiveChatState();
        var rule = new ActiveChatRule(state);

        state.SetOpenThread(7);
        state.SetAppVisible(true);
        Assert.Equal(NotificationDecision.Suppress, rule.Evaluate(Message));
        Assert.Equal(NotificationDecision.Show, rule.Evaluate(Message with { ThreadId = 8 }));
    }

    [Fact]
    public void Active_chat_rule_shows_when_app_is_in_background_or_chat_closed()
    {
        var state = new ActiveChatState();
        var rule = new ActiveChatRule(state);

        state.SetOpenThread(7);
        state.SetAppVisible(false);
        Assert.Equal(NotificationDecision.Show, rule.Evaluate(Message));

        state.SetAppVisible(true);
        state.ClearOpenThread();
        Assert.Equal(NotificationDecision.Show, rule.Evaluate(Message));
    }

    [Fact]
    public void Unknown_thread_is_never_on_screen()
    {
        var state = new ActiveChatState();
        state.SetAppVisible(true);
        state.SetOpenThread(0);

        Assert.False(state.IsThreadOnScreen(0));
    }
}
