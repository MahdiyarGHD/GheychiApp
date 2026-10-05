namespace Gheychi.Core.Notifications;

public sealed class NotificationPolicy
{
    private readonly INotificationRule[] _rules;

    public NotificationPolicy(IEnumerable<INotificationRule> rules)
    {
        _rules = rules.ToArray();
    }

    public NotificationDecision Decide(IncomingMessage message)
    {
        var result = NotificationDecision.Show;
        foreach (var rule in _rules)
        {
            var decision = rule.Evaluate(message);
            if (decision == NotificationDecision.Suppress)
                return decision;
            if (decision > result)
                result = decision;
        }

        return result;
    }
}
