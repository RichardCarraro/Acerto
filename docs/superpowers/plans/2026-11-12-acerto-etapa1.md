# Acertô Etapa 1 — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** App de bandeja para Windows que corrige sozinho, em qualquer programa, o português digitado (acento, escrita, digitação), com desfazer por Backspace, aprendizado, maiúscula automática, espaço duplo → ponto e atalhos de texto.

**Architecture:** `Acerto.Core` é uma biblioteca .NET pura que recebe caracteres e devolve ações ("apague N, escreva X"). Ela contém dicionários, ranqueamento e regras, e não tem nenhuma dependência de Windows. `Acerto.Windows` é um app WinForms de bandeja: hook global de teclado/mouse, tradução de tecla para caractere, `SendInput`, detecção de senha/app excluído e UI. O hook roda na thread da UI e chama o Core de forma síncrona.

**Tech Stack:** C# / .NET 10 (`net10.0` e `net10.0-windows`), WinForms + `System.Windows.Automation` (UseWPF só para UIA), xUnit, WeCantSpell.Hunspell, dicionário pt-BR VERO, lista de frequência FrequencyWords pt_br.

**Spec:** `docs/superpowers/specs/2026-11-12-acerto-design.md`

## Global Constraints

- Windows 10 1607+ / Windows 11, x64. Teclado de referência: ABNT2, com teclas mortas `´ ` ^ ~ ¨`.
- `Acerto.Core` não referencia nada de Windows (TargetFramework `net10.0`, sem `UseWindowsForms`/`UseWPF`).
- **Nenhuma conexão de rede** em nenhum projeto: não usar `HttpClient`, sockets, telemetria ou update check.
- **Nunca gravar nem logar texto digitado**, nem em Debug. Só palavras aprendidas, atalhos e config são persistidos.
- Dados do usuário sempre em `%APPDATA%\Acerto\` (`palavras.txt`, `atalhos.txt`, `config.json`). Nenhum caminho pode depender da pasta do exe, exceto os dados somente-leitura que vêm com o programa (`<pasta do exe>\data\`).
- Licenças: só dados e bibliotecas que permitam uso comercial, como arquivos separados e com créditos em `THIRD-PARTY-NOTICES.md`. Se uma licença não permitir, **pare e pergunte ao usuário**.
- Na dúvida, não troca. Uma correção errada é pior que nenhuma.
- O callback do hook tem que voltar rápido: `Corrector.Correct` deve levar **≤ 15 ms no p99** para palavras de até 20 letras.
- Baixar arquivos (SDK, dicionário, lista de frequência, pacotes NuGet novos) exige confirmação do usuário no chat antes.

## Review Focus

1. **Digitação rápida durante a correção.** Teclas digitadas enquanto o `SendInput` corrige não podem sair fora de ordem nem se perder. Coberto pela injeção síncrona dentro do callback (Task 6) e pelo teste manual M3 (Task 8).
2. **Teclas mortas e AltGr** (`´a`, `~a`, `^e`, `¨u`, `´` + espaço, AltGr+Q = `/`). O buffer recebe o caractere certo e o app de destino continua compondo acentos normalmente. Coberto por `DeadKeysTests` (Task 1) e M2 (Task 8).
3. **Foco mudou no meio da palavra** (clique, Alt+Tab, setas). Nada é corrigido em texto que o programa não viu. Coberto por `Engine_ResetMidWord_NoCorrection` (Task 5) e M4.
4. **Nomes, siglas, URLs, e-mails, caminhos e caixa mista** (`Carraro`, `CRCPR`, `iPhone`, `www.site.com`, `joao@crc.org.br`, `C:\pasta`). Ficam intactos. Coberto por `nao-mexer.txt` (Task 3) e `Engine_ImpureToken_NoCorrection` (Task 5).
5. **App rodando como administrador.** Se o Acertô engolir uma tecla e o `SendInput` for bloqueado pelo UIPI, a tecla se perde. O app em foreground elevado tem que ser tratado como excluído. Coberto por `ForegroundInfo.IsElevated` (Task 6) e M6.

---

## Estrutura de arquivos

```
acerto/
  Acerto.slnx
  THIRD-PARTY-NOTICES.md
  data/                         (somente-leitura, copiado para <exe>\data\)
    pt_BR.dic, pt_BR.aff        (VERO)
    frequencia.txt              ("palavra contagem" por linha)
    correcoes.txt               ("errado<TAB>certo" por linha)
    atalhos-padrao.txt          ("gatilho=texto" por linha)
  src/Acerto.Core/
    TextUtil.cs                 acentos, caixa
    DeadKeys.cs                 composição de tecla morta
    Lexicon.cs                  Hunspell
    FrequencyTable.cs
    FixedCorrections.cs
    PersonalDictionary.cs       palavras.txt
    ShortcutTable.cs            atalhos.txt
    AccentIndex.cs
    TypoRanker.cs
    Corrector.cs                regras §6.3
    TextEngine.cs               buffer, desfazer, maiúscula, espaço duplo
    CoreData.cs                 carrega tudo de uma pasta
  src/Acerto.Windows/
    Program.cs                  instância única, Application.Run
    TrayApp.cs                  NotifyIcon, menu, ícone desenhado
    Native.cs                   P/Invoke
    InputHooks.cs               WH_KEYBOARD_LL, WH_MOUSE_LL, WinEvent
    KeyTranslator.cs            VK → char (ToUnicodeEx flag 0x4)
    InputSender.cs              SendInput com marcador
    ForegroundInfo.cs           processo, elevação, senha (UIA)
    AppSettings.cs              config.json
    Forms/ListEditorForm.cs     palavras aprendidas / apps excluídos
    Forms/ShortcutsForm.cs
    Forms/SupportForm.cs        Apoie (Pix placeholder)
  tests/Acerto.Core.Tests/
    Data/deve-corrigir.txt      ("errado<TAB>certo")
    Data/nao-mexer.txt          (uma palavra/token por linha)
    *Tests.cs
```

---

### Task 0: Pré-requisitos

- [ ] **Step 1:** Rode `dotnet --list-sdks`. Esperado: uma linha `10.x`. Hoje a máquina só tem o runtime 10.0.12 e nenhum SDK. Se faltar, peça ao usuário para autorizar `winget install Microsoft.DotNet.SDK.10` e rode de novo.

---

### Task 1: Solução + utilitários de texto

**Files:**
- Create: `Acerto.slnx`, `src/Acerto.Core/Acerto.Core.csproj`, `src/Acerto.Core/TextUtil.cs`, `src/Acerto.Core/DeadKeys.cs`, `tests/Acerto.Core.Tests/Acerto.Core.Tests.csproj`, `.gitignore` (padrão `dotnet new gitignore`)
- Test: `tests/Acerto.Core.Tests/TextUtilTests.cs`, `tests/Acerto.Core.Tests/DeadKeysTests.cs`

**Interfaces:**
- Produces:
  - `static string TextUtil.StripAccents(string s)`: minúsculo continua minúsculo; remove diacríticos via NFD e `ç→c`.
  - `static string TextUtil.ApplyCase(string typed, string replacement)`
  - `static bool TextUtil.IsAllCaps(string s)`: ≥ 2 letras, todas maiúsculas.
  - `static bool TextUtil.IsMixedCase(string s)`: tem maiúscula depois da 1ª letra e não é all-caps.
  - `static string DeadKeys.Compose(char dead, char baseChar)`

- [ ] **Step 1: Testes que falham**

```csharp
[Theory]
[InlineData("informação", "informacao")] [InlineData("Ção", "Cao")] [InlineData("pé", "pe")]
public void StripAccents(string i, string o) => Assert.Equal(o, TextUtil.StripAccents(i));

[Theory]
[InlineData("nao", "não", "não")] [InlineData("Nao", "não", "Não")] [InlineData("NAO", "não", "NÃO")]
[InlineData("Oque", "o que", "O que")]
public void ApplyCase(string typed, string rep, string expected) => Assert.Equal(expected, TextUtil.ApplyCase(typed, rep));

[Theory]
[InlineData('´', 'a', "á")] [InlineData('~', 'a', "ã")] [InlineData('^', 'e', "ê")] [InlineData('`', 'a', "à")]
[InlineData('¨', 'u', "ü")] [InlineData('´', 'C', "Ć")] [InlineData('´', ' ', "´")] [InlineData('~', 'x', "~x")]
public void Compose(char d, char b, string o) => Assert.Equal(o, DeadKeys.Compose(d, b));
```

- [ ] **Step 2:** `dotnet test` → FAIL (tipos não existem).
- [ ] **Step 3: Implementar.** `DeadKeys.Compose`: mapeie `´→U+0301`, `` ` ``→U+0300, `^→U+0302`, `~→U+0303`, `¨→U+0308`. Se `baseChar == ' '`, retorne o próprio caractere morto. Senão, `(baseChar + combining).Normalize(FormC)`. Se não virar um único caractere, retorne `$"{dead}{baseChar}"`, igual o Windows faz. `ApplyCase` usa `CultureInfo("pt-BR")`.
- [ ] **Step 4:** `dotnet test` → PASS.
- [ ] **Step 5:** Commit `feat(core): solução e utilitários de texto`.

---

### Task 2: Dados e carregadores

**Files:**
- Create: `data/*` (ver estrutura), `THIRD-PARTY-NOTICES.md`, `src/Acerto.Core/{Lexicon,FrequencyTable,FixedCorrections,PersonalDictionary,ShortcutTable,CoreData}.cs`
- Modify: `src/Acerto.Core/Acerto.Core.csproj` (adicionar `WeCantSpell.Hunspell`; `data\**` como `Content` com `CopyToOutputDirectory=PreserveNewest` e link `data\%(RecursiveDir)%(Filename)%(Extension)`)
- Test: `tests/Acerto.Core.Tests/DataTests.cs`

**Fontes de dados (confirmar download com o usuário):**
- `pt_BR.dic`/`.aff`: repositório LibreOffice `dictionaries/pt_BR` (VERO, LGPLv3/MPL). Copie o texto da licença para os notices.
- `frequencia.txt`: `hermitdave/FrequencyWords`, `content/2018/pt_br/pt_br_full.txt` (CC BY-SA 4.0). Distribuir sem modificar, como arquivo separado, com atribuição. Confira a licença no repositório antes de usar.
- `WeCantSpell.Hunspell`: confira a licença no NuGet. Precisa permitir uso comercial como biblioteca.
- `correcoes.txt`: converta `D:\Dev\autocorrecao-ptbr\autocorrecao.ahk`, onde cada linha `::a::b` vira `a<TAB>b`. Pule comentários e diretivas.
- `atalhos-padrao.txt`: `vlw=valeu`, `att=Atenciosamente,`, `tbm=também`, `obg=obrigado`, `blz=beleza`.

**Interfaces:**
- Produces:
  - `sealed class Lexicon(string affPath, string dicPath)` com `bool IsValid(string word)`. Aceita a forma exata, e também a forma com a 1ª letra maiúscula, para nomes próprios.
  - `sealed class FrequencyTable(string path)` com `long Count(string lowerWord)` (0 se ausente) e `IEnumerable<(string Word, long Count)> Entries`.
  - `sealed class FixedCorrections(string path)` com `bool TryGet(string lowerWord, out string replacement)` e `int Count`.
  - `sealed class PersonalDictionary(string path)` com `bool Contains(string lowerWord)`, `void Add(string word)` (guarda minúsculo e salva na hora), `void Remove(string word)`, `IReadOnlyCollection<string> Words`. Cria o arquivo se não existir.
  - `sealed class ShortcutTable(string userPath, string defaultsPath)` com `bool TryGet(string lowerTrigger, out string expansion)`, `IReadOnlyDictionary<string,string> All` e `void Save(IDictionary<string,string>)`. Se `userPath` não existe, copia de `defaultsPath`. O split é no primeiro `=`.
  - `sealed class CoreData` com `static CoreData Load(string dataDir, string userDir)` e as propriedades `Lexicon`, `Frequency`, `Fixed`, `Personal`, `Shortcuts`.

- [ ] **Step 1: Testes que falham** (`DataTests` carrega de `Path.Combine(AppContext.BaseDirectory, "data")` e usa um diretório temporário como `userDir`)

```csharp
Assert.True(lex.IsValid("informação")); Assert.False(lex.IsValid("informacao"));
Assert.True(lex.IsValid("correríamos")); Assert.True(lex.IsValid("Curitiba"));
Assert.True(freq.Count("não") > freq.Count("nao"));
Assert.True(fixedList.Count >= 160); Assert.True(fixedList.TryGet("oque", out var r) && r == "o que");
personal.Add("CRCPR"); Assert.True(new PersonalDictionary(path).Contains("crcpr"));      // persistiu
Assert.True(shortcuts.TryGet("vlw", out var e) && e == "valeu");
```

- [ ] **Step 2:** `dotnet test` → FAIL.
- [ ] **Step 3:** Baixe os dados, gere `correcoes.txt`, escreva os notices e implemente as classes.
- [ ] **Step 4:** `dotnet test` → PASS.
- [ ] **Step 5:** Commit `feat(core): dicionário, frequência, correções fixas, dicionário pessoal, atalhos`.

---

### Task 3: Corretor (regras do §6.3 sem erro de digitação)

**Files:**
- Create: `src/Acerto.Core/AccentIndex.cs`, `src/Acerto.Core/Corrector.cs`
- Create: `tests/Acerto.Core.Tests/Data/deve-corrigir.txt`, `tests/Acerto.Core.Tests/Data/nao-mexer.txt` (copiados para o output)
- Test: `tests/Acerto.Core.Tests/CorrectorTests.cs`

**Interfaces:**
- Consumes: Task 1 e Task 2.
- Produces:
  - `sealed class AccentIndex(FrequencyTable freq, Lexicon lex)` com `IReadOnlyList<(string Word, long Count)> Lookup(string strippedLower)`, ordenado por contagem desc. Só entram palavras com acento/ç, válidas no `Lexicon` e com `Count ≥ 3`.
  - `enum CorrectionKind { Shortcut, Fixed, Accent, Typo }`
  - `sealed record Correction(string Replacement, CorrectionKind Kind)`
  - `sealed class Corrector(CoreData data)` com `Correction? Correct(string word, bool atSentenceStart)`. `word` é só letras e hífen, sem pontuação.
  - Constantes públicas: `Corrector.AccentDominance = 3.0`, `Corrector.RareFormRatio = 50.0` (valores iniciais, calibrados pelos testes).

**Ordem (fixa; `w = word.ToLowerInvariant()`):**
1. `Shortcuts.TryGet(w)` → `Shortcut`. A expansão sai como foi definida; só ganha a 1ª letra maiúscula se `word` começou com maiúscula.
2. `Personal.Contains(w)` → `null`.
3. `Fixed.TryGet(w)` → `Fixed`, com `ApplyCase`.
4. `IsMixedCase(word)` → `null`. Palavra com hífen → `null` (hífen só passa pelas regras 1–3).
5. Acento: `c = AccentIndex.Lookup(StripAccents(w))`, sem o próprio `w`. Se `c` está vazio, segue. `best` domina se `c.Count == 1` ou `best.Count ≥ AccentDominance × c[1].Count`.
   - `!Lexicon.IsValid(word)` e `best` domina → `Accent`.
   - `IsValid` e `best` domina e `best.Count ≥ RareFormRatio × max(Frequency.Count(w), 1)` → `Accent`.
6. `Lexicon.IsValid(word)` → `null`.
7. `IsAllCaps(word)` ou `w.Length < 3` → `null`. Começa com maiúscula e `!atSentenceStart` (provável nome) → `null`.
8. Regra de digitação: fica na Task 4. Nesta task, retorna `null`.

- [ ] **Step 1: Testes que falham**

```csharp
[Theory, MemberData(nameof(DeveCorrigir))]      // linhas de deve-corrigir.txt (Kind != Typo)
public void Corrige(string errado, string certo) => Assert.Equal(certo, corrector.Correct(errado, false)?.Replacement);

[Theory, MemberData(nameof(NaoMexer))]
public void NaoMexe(string palavra) => Assert.Null(corrector.Correct(palavra, false));

[Fact] public void Maiusculas() { Assert.Equal("Não", C("Nao")); Assert.Equal("NÃO", C("NAO")); Assert.Equal("O que", C("Oque")); }
[Fact] public void AprendidaGanhaDaListaFixa() { data.Personal.Add("oque"); Assert.Null(corrector.Correct("oque", false)); }
[Fact] public void AtalhoGanhaDoDicionario() { data.Shortcuts.Save(new Dictionary<string,string> { ["casa"] = "Casa Branca" }); Assert.Equal("Casa Branca", C("casa")); }
```

`deve-corrigir.txt`: todas as linhas de `correcoes.txt`, mais `informacao→informação`, `voce→você`, `agua→água`, `pagina→página`, `numero→número`, `secretario→secretário`, `atencao→atenção`, `configuracoes→configurações`.
`nao-mexer.txt`: `esta pais e tem nos secretaria publico pratica inicio sabia para por da de mas pode Carraro Richard CRCPR OAB iPhone guarda-chuva casa texto correríamos`.

- [ ] **Step 2:** `dotnet test --filter CorrectorTests` → FAIL.
- [ ] **Step 3:** Implemente `AccentIndex` e `Corrector`.
- [ ] **Step 4:** Rode e **calibre** `AccentDominance`/`RareFormRatio` até tudo passar. Se um caso de "não mexer" conflitar com um de "corrigir", o "não mexer" ganha: tire o caso de "corrigir" pela regra 5 e, se ele for importante, coloque em `correcoes.txt`. Anote no commit o que mudou.
- [ ] **Step 5:** Commit `feat(core): corretor por lista fixa e acento`.

---

### Task 4: Regra de erro de digitação

**Files:**
- Create: `src/Acerto.Core/TypoRanker.cs`
- Modify: `src/Acerto.Core/Corrector.cs` (regra 8 chama o ranker)
- Modify: `tests/Acerto.Core.Tests/Data/deve-corrigir.txt` (adicionar casos de digitação)
- Test: `tests/Acerto.Core.Tests/TypoRankerTests.cs`

**Interfaces:**
- Consumes: `Lexicon`, `FrequencyTable`, `TextUtil`.
- Produces: `sealed class TypoRanker(Lexicon lex, FrequencyTable freq)` com `string? Best(string lowerWord)`. O `Corrector` aplica `ApplyCase` e retorna `Kind = Typo`.

**Algoritmo (não determinado pelos testes):**
- Compare as formas sem acento. Mantenha um mapa `stripped → palavras reais` (válidas no `Lexicon`, `Count ≥ 5`), montado uma vez a partir de `FrequencyTable`.
- Gere variações de Damerau-Levenshtein de distância 1 sobre `a–z` (troca, falta, sobra, inversão de vizinhas). Se não houver candidata e `Length ≥ 6`, tente distância 2.
- Custo de uma edição: troca entre teclas vizinhas no ABNT2 = 0.6, inversão = 0.7, demais = 1.0. Linhas ABNT2: `qwertyuiop`, `asdfghjklç`, `zxcvbnm`. Vizinha é a tecla adjacente na mesma linha ou a de cima/baixo com índice igual ou ±1.
- `score = ln(count) − 4 × custo`. Retorne a melhor só se `count(best) ≥ 50` e `score(best) − score(segunda) ≥ TypoMargin` (inicial 1.5). Senão, `null`.

- [ ] **Step 1: Testes que falham**

```csharp
[Theory]
[InlineData("qeu", "que")] [InlineData("poblema", "problema")] [InlineData("porbelma", "problema")]
[InlineData("trabalo", "trabalho")] [InlineData("obrigaod", "obrigado")]
public void Corrige(string i, string o) => Assert.Equal(o, ranker.Best(i));

[Theory] [InlineData("xkzq")] [InlineData("www")] [InlineData("cpf")]
public void Desiste(string i) => Assert.Null(ranker.Best(i));

[Fact] public void Rapido() { /* aquece; 1000 chamadas com palavras de 6–20 letras; p99 ≤ 15 ms */ }
```

- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implemente e ligue no `Corrector`.
- [ ] **Step 4:** `dotnet test` → PASS, incluindo toda a `CorrectorTests` (o `nao-mexer.txt` continua intacto). Calibre `TypoMargin` se precisar, seguindo a mesma regra da Task 3.
- [ ] **Step 5:** Commit `feat(core): correção de erro de digitação`.

---

### Task 5: Motor de texto

**Files:**
- Create: `src/Acerto.Core/TextEngine.cs`
- Test: `tests/Acerto.Core.Tests/TextEngineTests.cs`

**Interfaces:**
- Consumes: `Corrector`, `PersonalDictionary`.
- Produces:
  - `readonly record struct EngineAction(bool Swallow, int Backspaces, string Insert)` com `static EngineAction None`. Quando `Swallow` é true, o host **não** deixa a tecla passar e envia `Backspaces` × Backspace + `Insert`. No `Insert`, `'\n'` = tecla Enter e `'\t'` = tecla Tab.
  - `sealed class TextEngine(Corrector corrector, PersonalDictionary personal)` com `EngineAction OnChar(char c)`, `EngineAction OnBackspace()`, `void Reset()`.

**Regras:**
- **Token** = tudo desde o último espaço/Enter/Tab. A **palavra** é a parte final de letras e hífen. O token fica **impuro** se tiver dígito, `@ / \ _ # : . ,` ou qualquer não-letra no meio. Prefixos `( " “ [ '` são permitidos. Token impuro não é corrigido.
- **Finais de palavra:** `' ' '\n' '\t' . , ; : ! ? ) ] } " ” »`. No final de um token puro com palavra não vazia, chame `Correct(palavra, atSentenceStart)`. Se `capNext` e a palavra começa com minúscula, a 1ª letra do resultado (ou da própria palavra, se não houver correção) vira maiúscula. Se algo mudou → `Swallow=true, Backspaces=palavra.Length, Insert=resultado + c`.
- **Depois de Reset (e ao nascer):** `capNext=false`, buffer vazio e o token é "desconhecido": nada é corrigido até o próximo espaço/Enter/Tab. **Exceção:** se o primeiro caractere depois do Reset já é final de palavra, a próxima palavra está liberada.
- **capNext** liga depois de `. ! ?` seguido de espaço, e depois de `'\n'`. Desliga ao completar uma palavra.
- **Espaço duplo:** `' '` quando o caractere anterior foi `' '` e o de antes era letra/dígito, e o anterior não veio de um espaço duplo → `Swallow, Backspaces=1, Insert=". "`. Liga `capNext`.
- **Desfazer:** depois de qualquer ação com `Swallow`, guarde `undo = (Backspaces: Insert.Length, Insert: original + c, Learn: palavra se Kind ∈ {Fixed, Accent, Typo})`. O próximo `OnBackspace` com `undo` pendente → `Swallow, Backspaces=undo.Backspaces, Insert=undo.Insert`, e `personal.Add(Learn)` se houver. Qualquer outro evento descarta o `undo`. **Não guarde undo quando `c` é `'\n'` ou `'\t'`**: o Enter pode ter enviado a mensagem.
- `OnBackspace` sem undo tira o último caractere do buffer. Se o buffer já está vazio, vira `Reset()`.

- [ ] **Step 1: Testes que falham.** Cada teste usa engine novo e um `StringBuilder doc` vazio. O helper `Type(string)` alimenta `OnChar` (`'\b'` = `OnBackspace`), simula o app (sem `Swallow`, o caractere entra em `doc` e o Backspace apaga o último caractere; com `Swallow`, aplica `Backspaces` + `Insert`) e retorna `doc.ToString()`. Os textos começam com espaço porque o motor nasce em estado "desconhecido", igual depois de um `Reset`.

```csharp
[Fact] public void Corrige_No_Espaco() => Assert.Equal(" não ", Type(" nao "));
[Fact] public void Corrige_Na_Pontuacao() => Assert.Equal(" você?", Type(" voce?"));
[Fact] public void Backspace_Desfaz_E_Aprende() { Assert.Equal(" oque ", Type(" oque \b")); Assert.True(personal.Contains("oque")); }
[Fact] public void Desfazer_Atalho_Nao_Aprende() { Type(" vlw \b"); Assert.False(personal.Contains("vlw")); }
[Fact] public void Enter_Nao_Gera_Undo() => Assert.Equal(" não", Type(" nao\n\b"));   // o Backspace só apaga o '\n'
[Fact] public void Maiuscula_Apos_Ponto() => Assert.Equal(" Ok. Não ", Type(" Ok. nao "));
[Fact] public void Espaco_Duplo() => Assert.Equal(" Fim. ", Type(" Fim  "));
[Fact] public void Engine_ResetMidWord_NoCorrection() { Type(" informa"); engine.Reset(); Assert.Equal(" informacao ", Type("cao ")); }
[Theory] [InlineData(" www.site.com ")] [InlineData(" joao@crc.org.br ")] [InlineData(" C:\\pasta\\nao ")] [InlineData(" abc123 ")]
public void Engine_ImpureToken_NoCorrection(string s) => Assert.Equal(s, Type(s));
[Fact] public void Sem_Maiuscula_No_Inicio_Desconhecido() => Assert.Equal(" casa ", Type(" casa "));
[Fact] public void Inicio_Desconhecido_Nao_Corrige() => Assert.Equal("nao ", Type("nao "));
```

- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** Implemente `TextEngine`.
- [ ] **Step 4:** `dotnet test` (tudo) → PASS.
- [ ] **Step 5:** Commit `feat(core): motor de texto com desfazer, maiúscula e espaço duplo`.

---

### Task 6: Captura e injeção no Windows

**Files:**
- Create: `src/Acerto.Windows/Acerto.Windows.csproj` (`net10.0-windows`, `OutputType=WinExe`, `UseWindowsForms=true`, `UseWPF=true` só por causa do UIA, referência a `Acerto.Core`), `Program.cs`, `Native.cs`, `InputHooks.cs`, `KeyTranslator.cs`, `InputSender.cs`, `ForegroundInfo.cs`, `AppSettings.cs`
- Modify: `Acerto.slnx`

**Interfaces:**
- Consumes: `CoreData.Load`, `TextEngine`, `EngineAction`, `DeadKeys.Compose`.
- Produces (usados na Task 7):
  - `sealed class AppSettings` com `bool Paused`, `List<string> ExcludedApps`, `bool StartWithWindows`, `static AppSettings Load()`, `void Save()`. Arquivo `%APPDATA%\Acerto\config.json`. Padrão de `ExcludedApps`: `Code.exe devenv.exe Cursor.exe idea64.exe rider64.exe pycharm64.exe phpstorm64.exe webstorm64.exe studio64.exe sublime_text.exe notepad++.exe WindowsTerminal.exe wt.exe cmd.exe powershell.exe pwsh.exe conhost.exe mintty.exe`.
  - `sealed class InputHooks : IDisposable` com `InputHooks(TextEngine engine, AppSettings settings, ForegroundInfo fg)`, `void Install()` e `event Action? PauseToggled`.
  - `sealed class ForegroundInfo` com `string ProcessName`, `bool IsElevated`, `volatile bool FocusIsPassword`, `bool FocusResolved` e `void Refresh()` (chamado pelo WinEvent).

**Decisões:**
- Hooks na thread da UI (`Application.Run`). O engine só é usado nessa thread.
- `InputSender.Send(int backspaces, string text)`: `SendInput` em lote único. Backspace com `VK_BACK`, `\n` com `VK_RETURN`, `\t` com `VK_TAB` e o resto com `KEYEVENTF_UNICODE`. Todos os eventos levam `dwExtraInfo = 0x41435254` ("ACRT"). Chame **dentro** do callback, antes de retornar `1`.
- No hook de teclado: ignore key-up. Ignore eventos com `LLKHF_INJECTED` **e** marcador ACRT. Antes de tudo, se `Paused`, app excluído, `IsElevated`, `FocusIsPassword` ou `!FocusResolved` → `engine.Reset()` e deixe passar.
- Ctrl ou Win pressionado sem AltGr, ou com Ctrl+Backspace → `Reset`. Setas, Home/End/PgUp/PgDn, Delete, Insert, Esc e Shift+Tab → `Reset`. F1–F24, Shift, Ctrl, Alt, CapsLock e Win sozinhos → nada.
- `Back` → `OnBackspace`. `Return` → `OnChar('\n')`. `Tab` → `OnChar('\t')`.
- Os demais passam por `KeyTranslator`: `ToUnicodeEx(vk, scan, state, buf, 8, 0x4, hkl)`, com `hkl` = layout da thread da janela em foreground. Monte `state[256]` com `GetAsyncKeyState`/`GetKeyState`: Shift, Ctrl, Alt, CapsLock (bit de toggle). Retorno `-1` = tecla morta: guarde o caractere e deixe passar. Na próxima tecla com caractere pendente, alimente o engine com cada caractere de `DeadKeys.Compose`. `VK_PACKET` não-ACRT → o caractere vem em `scanCode`. Retorno `0` → `Reset`.
- Hook de mouse: botão pressionado → `Reset`. WinEvent `EVENT_SYSTEM_FOREGROUND` e `EVENT_OBJECT_FOCUS` (out-of-context) → `Reset` + `ForegroundInfo.Refresh()`.
- `Refresh`: nome do processo via `GetWindowThreadProcessId` → `Process`. `IsElevated` via `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `TokenElevation`; se falhar, conta como elevado. A senha é checada fora da thread da UI (`Task.Run`): `AutomationElement.FocusedElement.Current.IsPassword` e, como reserva, o estilo Win32 `ES_PASSWORD` do controle com foco. Ao terminar, define `FocusIsPassword` e `FocusResolved`. Exceção → senha = true.
- Pausa global: `RegisterHotKey` **Ctrl+Shift+F12** → alterna `Paused`, salva e dispara `PauseToggled`.
- Instância única: `Mutex("Local\\Acerto.SingleInstance")`.

- [ ] **Step 1:** `dotnet build` → sucesso. Rode o app (sem UI ainda; o `Program` só instala os hooks e roda `Application.Run`).
- [ ] **Step 2:** Teste manual rápido no Bloco de Notas: `nao ` → `não `, `oque \b` → `oque `, `´a` → `á` no buffer (`informaç´ao ` → `informação ` sem correção), Ctrl+Shift+F12 pausa.
- [ ] **Step 3:** Commit `feat(windows): captura de teclado e injeção`.

---

### Task 7: App de bandeja e janelas

**Files:**
- Create: `src/Acerto.Windows/TrayApp.cs`, `Forms/ListEditorForm.cs`, `Forms/ShortcutsForm.cs`, `Forms/SupportForm.cs`
- Modify: `src/Acerto.Windows/Program.cs`

**Interfaces:**
- Consumes: `AppSettings`, `InputHooks.PauseToggled`, `CoreData.Personal`, `CoreData.Shortcuts`.

**Decisões:**
- `NotifyIcon` com ícone desenhado em runtime: "ô" branco em círculo azul `#1E63D6`; pausado = círculo cinza `#8A8A8A`. Tooltip: "Acertô — ativo" / "Acertô — pausado (Ctrl+Shift+F12)".
- Itens do menu, nesta ordem: **Pausar/Retomar**, separador, **Palavras aprendidas…**, **Atalhos…**, **Apps excluídos…**, separador, **Iniciar com o Windows** (checkbox: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, valor `Acerto` = `"<caminho do exe>"`), **Apoie o projeto ♥**, separador, **Sair**.
- `ListEditorForm(string title, IEnumerable<string> items)`: lista + Remover (palavras); lista + caixa + Adicionar/Remover (apps). Salva ao fechar.
- `ShortcutsForm`: `DataGridView` com 2 colunas (Gatilho, Texto). Salva via `ShortcutTable.Save`. Gatilho vazio ou repetido → linha destacada e não salva.
- `SupportForm`: "O Acertô é grátis e sempre vai ser. Se ele te ajuda, considere apoiar com um Pix." + área do QR (placeholder "QR Code em breve") + "Chave Pix: em breve".
- Primeira execução: balão "Acertô está ativo. Corrigindo em todos os programas. Ctrl+Shift+F12 pausa."

- [ ] **Step 1:** `dotnet build` → sucesso. Abra cada item do menu e confira que as mudanças persistem depois de reiniciar o app (palavra removida some, atalho novo funciona, app excluído para de ser corrigido, autostart aparece no registro).
- [ ] **Step 2:** Commit `feat(windows): bandeja, configurações e apoie`.

---

### Task 8: Publicação e aceitação

**Files:**
- Create: `docs/teste-manual.md` (checklist abaixo, com resultado preenchido)

- [ ] **Step 1:** `dotnet publish src/Acerto.Windows -c Release -r win-x64 --self-contained -o publish` → `publish\Acerto.Windows.exe` + `publish\data\`. Mova a pasta `publish` para outro lugar e confira que roda (só usa `%APPDATA%`).
- [ ] **Step 2: Checklist manual** em Bloco de Notas, Chrome, app do Claude, Teams e Word:
  - M1 `nao voce informacao poblema oque ` → `não você informação problema o que `
  - M2 teclas mortas: `´a ~a ^e ¨u ´␣` saem iguais com e sem o Acertô; AltGr+Q = `/`
  - M3 digitar `informacao nao voce tambem ` o mais rápido possível 5×: nenhuma letra fora de ordem ou perdida
  - M4 digitar `informa`, clicar em outro lugar, digitar `cao ` → nada corrigido
  - M5 campo de senha (login do Windows/Chrome): `nao ` continua `nao `
  - M6 Bloco de Notas como administrador: digitar normalmente, nenhuma tecla perdida
  - M7 VS Code e Windows Terminal: nada corrigido (excluídos por padrão)
  - M8 Backspace logo depois de uma correção desfaz; depois disso a palavra não é mais corrigida
  - M9 Enter no chat do Claude com `nao` → envia "não" e o Backspace seguinte não reenvia nada
  - M10 Ctrl+Shift+F12 pausa e retoma; o ícone muda
- [ ] **Step 3:** Commit `docs: checklist de aceitação da etapa 1`.
