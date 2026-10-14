using System.Text;
using PowerLens.Core;

namespace PowerLens.App.Controls;

/// <summary>Grouped two-column list that can be refreshed every poll without losing scroll position.</summary>
internal sealed class KeyValueList : ListView
{
    private readonly ColumnHeader _keyColumn = new() { Text = "Property" };
    private readonly ColumnHeader _valueColumn = new() { Text = "Value" };

    public KeyValueList()
    {
        View = View.Details;
        FullRowSelect = true;
        HeaderStyle = ColumnHeaderStyle.Nonclickable;
        ShowItemToolTips = true;
        BorderStyle = BorderStyle.None;
        DoubleBuffered = true;
        Columns.AddRange([_keyColumn, _valueColumn]);
    }

    public void SetRows(IEnumerable<ReportRow> rows)
    {
        int top = TopItem?.Index ?? 0;
        var selected = SelectedIndices.Count > 0 ? SelectedIndices[0] : -1;

        BeginUpdate();
        try
        {
            Items.Clear();
            Groups.Clear();
            var groups = new Dictionary<string, ListViewGroup>();
            foreach (var row in rows)
            {
                if (!groups.TryGetValue(row.Group, out var group))
                {
                    group = new ListViewGroup(row.Group, row.Group);
                    groups[row.Group] = group;
                    Groups.Add(group);
                }
                Items.Add(new ListViewItem([row.Key, row.Value], group) { ToolTipText = row.Value });
            }

            if (selected >= 0 && selected < Items.Count) Items[selected].Selected = true;
            if (top > 0 && top < Items.Count)
            {
                try { TopItem = Items[top]; } catch (NotSupportedException) { /* Not supported with groups on some versions. */ }
            }
        }
        finally
        {
            EndUpdate();
        }
        ResizeColumns();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ResizeColumns();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Control && e.KeyCode == Keys.C && SelectedItems.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (ListViewItem item in SelectedItems)
                sb.AppendLine($"{item.SubItems[0].Text}\t{item.SubItems[1].Text}");
            Clipboard.SetText(sb.ToString());
        }
        else if (e.Control && e.KeyCode == Keys.A)
        {
            foreach (ListViewItem item in Items) item.Selected = true;
        }
    }

    private void ResizeColumns()
    {
        int width = ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
        if (width <= 0) return;
        _keyColumn.Width = Math.Max(160, width * 36 / 100);
        _valueColumn.Width = Math.Max(160, width - _keyColumn.Width);
    }
}
