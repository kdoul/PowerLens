using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PowerLens.Core;

namespace PowerLens.App;

internal sealed class RowItem : INotifyPropertyChanged
{
    private string _value;

    public RowItem(string key, string value)
    {
        Key = key;
        _value = value;
    }

    public string Key { get; }

    public string Value
    {
        get => _value;
        set
        {
            if (value == _value) return;
            _value = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed class RowGroup(string name)
{
    public string Name { get; } = name;
    public ObservableCollection<RowItem> Rows { get; } = [];
}

/// <summary>
/// Grouped rows for the tab lists. <see cref="Update"/> patches the existing items in place,
/// so a refresh every poll changes text but never recreates the visuals (and never resets scrolling).
/// </summary>
internal sealed class RowsModel
{
    public ObservableCollection<RowGroup> Groups { get; } = [];

    public void Update(IEnumerable<ReportRow> rows)
    {
        var incoming = rows.GroupBy(r => r.Group).ToList();

        for (int g = 0; g < incoming.Count; g++)
        {
            string name = incoming[g].Key;
            if (g >= Groups.Count || Groups[g].Name != name)
            {
                int existing = IndexOf(name, g + 1);
                if (existing >= 0) Groups.Move(existing, g);
                else Groups.Insert(g, new RowGroup(name));
            }
            Patch(Groups[g].Rows, incoming[g].ToList());
        }

        while (Groups.Count > incoming.Count)
            Groups.RemoveAt(Groups.Count - 1);
    }

    private static void Patch(ObservableCollection<RowItem> target, List<ReportRow> rows)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (i < target.Count && target[i].Key == rows[i].Key) target[i].Value = rows[i].Value;
            else if (i < target.Count) target[i] = new RowItem(rows[i].Key, rows[i].Value);
            else target.Add(new RowItem(rows[i].Key, rows[i].Value));
        }
        while (target.Count > rows.Count)
            target.RemoveAt(target.Count - 1);
    }

    private int IndexOf(string name, int start)
    {
        for (int i = start; i < Groups.Count; i++)
            if (Groups[i].Name == name) return i;
        return -1;
    }
}
