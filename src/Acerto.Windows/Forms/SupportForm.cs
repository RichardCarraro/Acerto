namespace Acerto.Windows.Forms;

/// <summary>"Apoie o projeto": por enquanto só texto; QR Code e chave Pix entram quando existirem.</summary>
internal sealed class SupportForm : Form
{
    public SupportForm()
    {
        Text = "Apoie o Acertô ♥";
        Width = 420;
        Height = 380;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        Font = new Font("Segoe UI", 10);

        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(20), WrapContents = false };
        layout.Controls.Add(new Label
        {
            Text = "O Acertô é grátis e sempre vai ser.\nSe ele te ajuda, considere apoiar com um Pix.",
            AutoSize = true, MaximumSize = new Size(360, 0),
        });
        layout.Controls.Add(new Label
        {
            Text = "QR Code em breve",
            Width = 180, Height = 180, TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle, ForeColor = SystemColors.GrayText, Margin = new Padding(90, 16, 0, 16),
        });
        layout.Controls.Add(new Label { Text = "Chave Pix: em breve", AutoSize = true });
        Controls.Add(layout);
    }
}
