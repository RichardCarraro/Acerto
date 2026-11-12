// Uso: AcertoSmokePad.exe <arquivo-de-saida> [atraso-ms-por-tecla]
// Com o Acertô rodando, digita casos numa caixa de texto e grava "OK|FALHOU caso → obtido" no arquivo.
// Os casos começam com espaço: depois de trocar o foco o Acertô espera um espaço para confiar no contexto.
// Não usa nada que ensine palavras, para não mexer no dicionário pessoal real.

var output = args.Length > 0 ? args[0] : "smoke.txt";
int delay = args.Length > 1 ? int.Parse(args[1]) : 40;

(string Keys, string Expected, bool Password)[] cases =
[
    (" nao voce informacao poblema oque ", " não você informação problema o que ", false),
    (" vlw{BACKSPACE}", " vl", false),
    (" vlw {BACKSPACE}", " vlw ", false),
    (" nao x{BACKSPACE}{BACKSPACE}", " não", false), // desfazer só vale logo depois da correção
    (" fim.  nao ", " fim.  Não ", false),
    (" Ok. nao ", " Ok. Não ", false),
    (" Fim  ", " Fim. ", false),
    (" nao{ENTER}", " não\r\n", false),
    (" www.site.com joao@crc.org.br abc123 ", " www.site.com joao@crc.org.br abc123 ", false),
    (" esta pais secretaria casa ", " esta pais secretaria casa ", false),
    (" joao.silva@crc.org.br relatorio.pdf ", " joao.silva@crc.org.br relatorio.pdf ", false),
    (" nao. voce: ", " não. Você: ", false),
    (" curitiba apps obs office ficara duvida ", " curitiba apps obs office ficara duvida ", false),
    (" nao\t", " nao\t", false),
    (" nao ", " nao ", true),
];

ApplicationConfiguration.Initialize();
var form = new Form { Text = "Acertô — teste automático", Width = 700, Height = 300, TopMost = true, StartPosition = FormStartPosition.CenterScreen };
var text = new TextBox { Multiline = true, AcceptsTab = true, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 12) };
var pwd = new TextBox { UseSystemPasswordChar = true, Dock = DockStyle.Bottom };
form.Controls.Add(text);
form.Controls.Add(pwd);

form.Shown += async (_, _) =>
{
    var lines = new List<string>();
    await Task.Delay(1500);
    foreach (var (keys, expected, password) in cases)
    {
        var box = password ? pwd : text;
        var other = password ? text : pwd;
        box.Clear();
        form.Activate();
        other.Focus(); // trocar o foco reseta o Acertô, como na vida real
        await Task.Delay(200);
        box.Focus();
        await Task.Delay(800); // Acertô resolve "é senha?" em segundo plano
        foreach (var k in Tokens(keys))
        {
            SendKeys.SendWait(k);
            await Task.Delay(delay);
        }
        await Task.Delay(400);
        var got = box.Text;
        lines.Add($"{(got == expected ? "OK" : "FALHOU")} | {Show(keys)} → {Show(got)}{(got == expected ? "" : $"   (esperado {Show(expected)})")}");
    }
    File.WriteAllLines(output, lines);
    form.Close();
};
Application.Run(form);

static IEnumerable<string> Tokens(string keys)
{
    for (int i = 0; i < keys.Length; i++)
    {
        if (keys[i] == '{')
        {
            int end = keys.IndexOf('}', i);
            yield return keys[i..(end + 1)];
            i = end;
        }
        else yield return keys[i].ToString();
    }
}

static string Show(string s) => "\"" + s.Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
