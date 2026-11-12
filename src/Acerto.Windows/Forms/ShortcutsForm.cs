using Acerto.Core;

namespace Acerto.Windows.Forms;

/// <summary>Editor de atalhos (gatilho → texto). Linhas com gatilho vazio ou repetido não são salvas.</summary>
internal sealed class ShortcutsForm : Form
{
    private readonly DataGridView _grid;

    public ShortcutsForm(ShortcutTable shortcuts)
    {
        Text = "Acertô — Atalhos";
        Width = 560;
        Height = 480;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        MinimizeBox = MaximizeBox = false;

        var hint = new Label
        {
            Text = "Digite o gatilho e, ao apertar espaço, ele vira o texto. Ex.: vlw → valeu",
            Dock = DockStyle.Top, Height = 36, Padding = new Padding(8, 8, 8, 0),
        };
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = true,
            RowHeadersVisible = false,
        };
        _grid.Columns.Add("trigger", "Gatilho");
        _grid.Columns.Add("text", "Texto");
        _grid.Columns[0].FillWeight = 30;
        foreach (var (k, v) in shortcuts.All.OrderBy(kv => kv.Key)) _grid.Rows.Add(k, v);

        Controls.Add(_grid);
        Controls.Add(hint);

        FormClosing += (_, e) =>
        {
            _grid.EndEdit();
            var valid = new Dictionary<string, string>(StringComparer.Ordinal);
            var invalid = new List<DataGridViewRow>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow) continue;
                var trigger = (row.Cells[0].Value as string ?? "").Trim().ToLowerInvariant();
                var text = row.Cells[1].Value as string ?? "";
                row.DefaultCellStyle.BackColor = SystemColors.Window;
                if (trigger.Length == 0 || text.Length == 0 || trigger.Contains(' ') || !valid.TryAdd(trigger, text))
                    invalid.Add(row);
            }
            if (invalid.Count > 0)
            {
                foreach (var row in invalid) row.DefaultCellStyle.BackColor = Color.MistyRose;
                var answer = MessageBox.Show(this,
                    "As linhas em vermelho têm gatilho vazio, repetido, com espaço ou sem texto e não serão salvas.\n\nFechar mesmo assim?",
                    "Acertô", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer == DialogResult.No) { e.Cancel = true; return; }
            }
            shortcuts.Save(valid);
        };
    }
}
