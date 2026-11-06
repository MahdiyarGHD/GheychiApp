using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Gheychi.App.Theming;
using Gheychi.Core.Spam;

namespace Gheychi.App.ViewModels;

/// <summary>The round check on a review row: filled when on, a dash when only some of a sender's messages are.</summary>
public abstract class ScanCheckRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected abstract bool? CheckState { get; }

    public IBrush CheckFill => CheckState == false ? Palette.Transparent : Palette.Accent(AccentRole.Solid);
    public IBrush CheckStroke => CheckState == false ? Palette.Brush("#8A8F98") : Palette.Accent(AccentRole.Solid);
    public string CheckGlyph => CheckState switch { true => "✓", null => "–", _ => string.Empty };

    public void RaiseCheckChanged()
    {
        foreach (var name in (string[])[nameof(CheckFill), nameof(CheckStroke), nameof(CheckGlyph)])
            Raise(name);
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One message in the scan review (a candidate) or in a scan's batch (an <see cref="SpamItem"/>).</summary>
public sealed class ScanMessageRow : ScanCheckRow
{
    private bool _selected;
    private bool _kept;

    public SpamScanCandidate? Candidate { get; init; }
    public SpamItem? Item { get; init; }
    public required string Address { get; init; }
    public required string Sender { get; init; }
    public required string Body { get; init; }
    public required float Score { get; init; }
    public required DateTime Time { get; init; }
    public required string DateText { get; init; }

    /// <summary>Review rows have a check; batch rows only open their actions.</summary>
    public bool ShowCheck => Candidate is not null;

    public string Percent => SpamItem.FormatPercent(SpamConfidence.Percent(Score));
    public IBrush ConfidenceColor => SpamConfidence.Level(Score) == SpamConfidenceLevel.VeryLikely ? SpamItem.VeryLikelyColor : SpamItem.LikelyColor;

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;
            _selected = value;
            RaiseCheckChanged();
        }
    }

    /// <summary>The user said it is not spam: it stays in the list, dimmed, and is never moved.</summary>
    public bool Kept
    {
        get => _kept;
        set
        {
            if (_kept == value)
                return;
            _kept = value;
            Raise(nameof(RowOpacity));
        }
    }

    public double RowOpacity => _kept ? 0.45 : 1;

    protected override bool? CheckState => _selected;
}

/// <summary>A sender's messages in the review or a batch, folded under one row.</summary>
public sealed class ScanGroupRow : ScanCheckRow
{
    public required string Key { get; init; }
    public required string Sender { get; init; }

    /// <summary>Messages the scan judged spam, newest first.</summary>
    public required IReadOnlyList<ScanMessageRow> Hits { get; init; }

    /// <summary>The sender's other suspicious messages, listed unselected.</summary>
    public required IReadOnlyList<ScanMessageRow> Others { get; init; }

    public required string CountText { get; init; }
    public bool ShowCheck { get; init; }

    public bool Expanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value)
                return;
            _expanded = value;
            Raise(nameof(Chevron));
        }
    }

    private bool _expanded;

    // Bound as a whole: a binding on a transform's own Angle has no data context to read from.
    public Transform Chevron => new RotateTransform(Expanded ? 90 : 0);

    public string Snippet => Hits.Count > 0 ? Hits[0].Body : Others.Count > 0 ? Others[0].Body : string.Empty;

    public IEnumerable<ScanMessageRow> All => Hits.Concat(Others);

    protected override bool? CheckState
    {
        get
        {
            var all = All.Where(m => !m.Kept).ToList();
            var on = all.Count(m => m.Selected);
            return on == 0 ? false : on == all.Count ? true : null;
        }
    }
}

/// <summary>A line of grey text between rows: a section title or "N more from this sender".</summary>
public sealed record ScanNoteRow(string Text, bool Indented);

public sealed class ScanTemplateSelector : IDataTemplate, IRecyclingDataTemplate
{
    private static readonly object GroupKind = new();
    private static readonly object MessageKind = new();
    private static readonly object NoteKind = new();

    public IDataTemplate? GroupTemplate { get; set; }
    public IDataTemplate? MessageTemplate { get; set; }
    public IDataTemplate? NoteTemplate { get; set; }

    public bool Match(object? data) => data is ScanGroupRow or ScanMessageRow or ScanNoteRow;

    public Control? Build(object? data) => Build(data, null);

    public Control? Build(object? data, Control? existing)
    {
        var (kind, template) = data switch
        {
            ScanGroupRow => (GroupKind, GroupTemplate),
            ScanMessageRow => (MessageKind, MessageTemplate),
            _ => (NoteKind, NoteTemplate)
        };
        if (existing is not null && ReferenceEquals(existing.Tag, kind))
            return existing;

        var control = (template ?? throw new InvalidOperationException("Missing row template")).Build(data);
        if (control is not null)
            control.Tag = kind;
        return control;
    }
}
