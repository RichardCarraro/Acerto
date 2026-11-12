namespace Acerto.Windows.Forms;

/// <summary>Lista editável simples (palavras aprendidas, apps excluídos). Salva ao fechar.</summary>
internal sealed class ListEditorForm : Form
{
    public ListEditorForm(string title, string hint, IEnumerable<string> items, bool allowAdd, Func<string, string>? normalize,
        Action<IReadOnlyList<string>> onSave)
    {
        Text = title;
        Width = 420;
        Height = 480;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        MinimizeBox = MaximizeBox = false;

        var label = new Label { Text = hint, Dock = DockStyle.Top, Height = 48, Padding = new Padding(8, 8, 8, 0) };
        var list = new ListBox { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, IntegralHeight = false };
        list.Items.AddRange(items.Order(StringComparer.CurrentCultureIgnoreCase).Cast<object>().ToArray());

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(6), FlowDirection = FlowDirection.LeftToRight };
        var remove = new Button { Text = "Remover", AutoSize = true };
        remove.Click += (_, _) =>
        {
            foreach (var item in list.SelectedItems.Cast<object>().ToList()) list.Items.Remove(item);
        };

        if (allowAdd)
        {
            var input = new TextBox { Width = 200 };
            var add = new Button { Text = "Adicionar", AutoSize = true };
            void Add()
            {
                var value = input.Text.Trim();
                if (value.Length == 0) return;
                value = normalize?.Invoke(value) ?? value;
                if (!list.Items.Cast<string>().Contains(value, StringComparer.OrdinalIgnoreCase)) list.Items.Add(value);
                input.Clear();
            }
            add.Click += (_, _) => Add();
            input.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Add(); e.SuppressKeyPress = true; } };
            bottom.Controls.AddRange([input, add]);
        }
        bottom.Controls.Add(remove);

        Controls.Add(list);
        Controls.Add(label);
        Controls.Add(bottom);

        FormClosing += (_, _) => onSave(list.Items.Cast<string>().ToList());
    }
}
