using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class ListSynchronizerTests
{
    private sealed record Row(int Id, string Text);

    private static void Sync(IList<Row> target, params Row[] source) =>
        ListSynchronizer.Sync(target, source, r => r.Id, (a, b) => a.Text == b.Text);

    [Fact]
    public void Identical_MakesNoChanges()
    {
        var list = new ObservableCollection<Row> { new(1, "a"), new(2, "b") };
        var events = 0;
        list.CollectionChanged += (_, _) => events++;

        Sync(list, new Row(1, "a"), new Row(2, "b"));

        Assert.Equal(0, events);
    }

    [Fact]
    public void ChangedContent_ReplacesOnlyThatRow()
    {
        var list = new ObservableCollection<Row> { new(1, "a"), new(2, "b"), new(3, "c") };
        var actions = new List<NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, e) => actions.Add(e.Action);

        Sync(list, new Row(1, "a"), new Row(2, "B"), new Row(3, "c"));

        Assert.Equal([NotifyCollectionChangedAction.Replace], actions);
        Assert.Equal("B", list[1].Text);
    }

    [Fact]
    public void NewMessageMovesThreadToTop_WithoutReset()
    {
        var list = new ObservableCollection<Row> { new(1, "a"), new(2, "b"), new(3, "c") };
        var actions = new List<NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, e) => actions.Add(e.Action);

        Sync(list, new Row(3, "c2"), new Row(1, "a"), new Row(2, "b"));

        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
        Assert.Equal([3, 1, 2], list.Select(r => r.Id));
        Assert.Equal("c2", list[0].Text);
    }

    [Fact]
    public void RemovedAndInsertedRows_AreApplied()
    {
        var list = new ObservableCollection<Row> { new(1, "a"), new(2, "b"), new(3, "c") };

        Sync(list, new Row(4, "d"), new Row(1, "a"), new Row(3, "c"));

        Assert.Equal([4, 1, 3], list.Select(r => r.Id));
    }

    [Fact]
    public void CarryOver_CopiesStateToTheReplacement()
    {
        var list = new List<Row> { new(1, "a") };
        var carried = new List<(Row Old, Row New)>();

        ListSynchronizer.Sync(list, [new Row(1, "z")], r => r.Id, (a, b) => a.Text == b.Text, (o, n) => carried.Add((o, n)));

        Assert.Single(carried);
        Assert.Equal("a", carried[0].Old.Text);
    }

    [Fact]
    public void EmptyTarget_FillsFromSource()
    {
        var list = new List<Row>();
        Sync(list, new Row(1, "a"), new Row(2, "b"));
        Assert.Equal([1, 2], list.Select(r => r.Id));
    }
}
