using System.Windows;

namespace Acerto.Windows.UI;

/// <summary>Janela de instalação/atualização: duas opções, um botão, a barra de progresso e "Pronto".</summary>
internal partial class SetupWindow : Window
{
    private bool _done;

    public SetupWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Dwm.ApplyBackdrop(this, Dwm.Backdrop.Mica);
        VersionText.Text = "Versão " + Setup.Version;
        if (Setup.IsInstalled)
        {
            // atualização: mantém as escolhas de antes
            Title = "Atualizar o Acertô";
            InstallButton.Content = "Atualizar";
            Intro.Text = $"Vai atualizar o Acertô para a versão {Setup.Version}. Suas palavras, atalhos e configurações continuam.";
            StartupSwitch.IsChecked = Setup.StartsWithWindows;
            DesktopSwitch.IsChecked = Setup.HasDesktopShortcut;
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_done)
        {
            Close();
            return;
        }

        bool startup = StartupSwitch.IsChecked == true, desktop = DesktopSwitch.IsChecked == true;
        OptionsPanel.Visibility = Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        InstallButton.IsEnabled = CancelButton.IsEnabled = false;
        var progress = new Progress<double>(v => Progress.Value = v);
        try
        {
            await Task.Run(() => Setup.Install(startup, desktop, progress));
            Setup.Launch();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            OptionsPanel.Visibility = Visibility.Visible;
            ErrorText.Text = "Não deu para instalar: " + ex.Message + " Feche o Acertô e tente de novo.";
            ErrorText.Visibility = Visibility.Visible;
            InstallButton.IsEnabled = CancelButton.IsEnabled = true;
            return;
        }

        _done = true;
        ProgressPanel.Visibility = Visibility.Collapsed;
        DonePanel.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Collapsed;
        InstallButton.Content = "Fechar";
        InstallButton.IsEnabled = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
