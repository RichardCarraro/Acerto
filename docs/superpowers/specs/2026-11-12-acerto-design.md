# Acertô — Design da Etapa 1 (corretor automático)

Data: 2026-11-12 · Status: rascunho para aprovação

## 1. O que é e por quê

O Acertô é um programa para Windows que corrige o que você digita **em qualquer lugar do sistema**,
sozinho, na hora, do jeito que o teclado do celular faz. Ele fica rodando escondido na bandeja,
perto do relógio.

O Windows tem verificação ortográfica, mas ela só sublinha as palavras, e a correção automática dele só funciona em
poucos apps. O que existe de terceiros é pago (PhraseExpress), só corrige palavras de uma lista
fixa (AutoHotkey) ou não troca a palavra sozinho (LanguageTool). O usuário quer escrever em qualquer
programa, inclusive no app do Claude, no Chrome, no Teams e no Word, e ver `informacao` virar
`informação` sem fazer nada.

A ideia é virar um produto de verdade. O modelo de negócio inicial é o do WinRAR: o programa é **grátis
e completo**, com um "Apoie o projeto" via Pix. Mais pra frente pode ter uma versão Pro paga.

## 2. Objetivos da etapa 1

1. **Corrigir sozinho ao terminar a palavra**, ou seja, ao digitar espaço, Enter, Tab ou pontuação (`. , ; : ! ?`
   e fechamento de parênteses/aspas):
   - acento ou cedilha esquecidos: `nao` → `não`, `informacao` → `informação`, `voce` → `você`;
   - palavra escrita errada: `poblema` → `problema`, `excessao` → `exceção`;
   - erro de digitação: letra trocada, faltando, sobrando ou invertida: `qeu` → `que`, `porbelma` → `problema`.
2. **Backspace desfaz**: se o usuário aperta Backspace logo depois de uma correção, a palavra volta
   a ser exatamente o que ele digitou, com o caractere final mantido. Igual ao celular.
3. **Aprender palavras**: quando o usuário desfaz uma correção, a palavra original entra no dicionário
   pessoal e nunca mais é corrigida (ex.: `CRCPR`, nomes, gírias). O usuário pode ver e remover
   palavras aprendidas.
4. **Maiúscula automática** no começo da frase: depois de `. ! ?` + espaço, depois de Enter e no
   primeiro caractere digitado num campo vazio, quando der para saber que ele está vazio.
5. **Espaço duplo vira ponto**: `palavra␣␣` → `palavra.␣` (só depois de uma letra ou número).
6. **Atalhos de texto** editáveis pelo usuário: `vlw` → `valeu`, `att` → `Atenciosamente,`, `tbm` → `também`.
   O programa vem com alguns atalhos de exemplo.
7. **Funcionar em todo lugar onde se digita**: apps Win32, apps da Store, Chrome/Edge/Firefox,
   apps Electron (Claude, Teams, WhatsApp Desktop), Office. **Editores de código e terminais**
   (VS Code, Visual Studio, JetBrains, Windows Terminal, cmd, PowerShell) vêm **desligados por padrão**,
   na lista de apps excluídos, e o usuário pode ligar se quiser.
8. **Ícone na bandeja** com menu: Pausar/Retomar, Palavras aprendidas, Atalhos, Apps excluídos,
   Iniciar com o Windows, Apoie o projeto (Pix), Sair. Também um **atalho de teclado global para pausar**.

## 3. Fora da etapa 1 (não fazer agora)

- Barra de sugestões, completar palavra e prever a próxima palavra. Ficam para a **etapa 2**, mas a
  arquitetura tem que permitir (ver §6).
- Corrigir gramática ou concordância (`os menino` → `os meninos`).
- Trocar uma palavra que existe por outra **quando as duas são comuns** (`esta` → `está`, `pais` → `país`,
  `e` → `é`, `secretaria` → `secretária`). Isso depende de contexto e fica para quando houver previsão.
  Quando a forma sem acento é rara e a acentuada é muito comum, troca sim (ver §6.3, regra 3).
- Outros idiomas, versão Mac, versão Pro, sincronização, aviso periódico de "Apoie" (só o item no menu entra agora).
- Instalador, atualização automática e assinatura de código. Na etapa 1, o programa roda de uma pasta
  (publish self-contained). O usuário **quer um instalador de verdade** logo depois da etapa 1, então nada
  na etapa 1 pode depender de rodar de uma pasta específica (caminhos de dados sempre em `%APPDATA%`).

## 4. O que seria um fracasso (anti-objetivos)

Mesmo funcionando, o Acertô fracassou se:

- **Estragar texto certo.** Uma correção errada é muito pior que uma correção que não aconteceu. Na
  dúvida, **não troca**.
- **Atrasar ou perder teclas.** Qualquer atraso perceptível ao digitar, letras trocadas de ordem ou
  teclas perdidas. O programa nunca pode atrapalhar quem digita rápido.
- **Mandar qualquer coisa para fora do computador.** O programa não faz **nenhuma** conexão de rede.
- **Gravar o que o usuário digita.** Nada de log de texto digitado, nem em modo debug. Só as palavras
  aprendidas e os atalhos são salvos.
- **Mexer em senha.** Campos de senha nunca são lidos nem corrigidos.
- **Quebrar jogos, terminais ou atalhos de teclado** (Ctrl+C, Alt+Tab etc.).

## 5. Restrições

- **Windows 10 1607+ e Windows 11**, x64. O usuário tem Windows 11 com teclado **ABNT2**. A digitação de
  acentos usa **teclas mortas** (`´` + `a` = `á`), e o programa não pode quebrar isso: ler as teclas não pode
  "consumir" a tecla morta. Uma forma conhecida de evitar o problema é `ToUnicodeEx` com o flag `0x4`
  (não alterar o estado do teclado).
- **Licenças compatíveis com uso comercial.** Dicionário, lista de frequência e bibliotecas têm que permitir
  distribuição num produto vendido. Os dados vão como arquivos separados, com os créditos num
  `THIRD-PARTY-NOTICES`.
- **Offline e sem rede**, como dito acima.
- **Linguagem: C# / .NET 10 (LTS).** A máquina tem o runtime 10.0.12, mas **não tem o SDK**: é preciso instalar
  o .NET 10 SDK antes de compilar.
- **Antivírus corporativo (Morphisec)** pode bloquear um programa que captura o teclado. Não há o que fazer no
  código além de não fazer nada suspeito, como injetar DLL em outros processos. Assinatura de código fica para depois.
- Repositório: `D:\Dev\acerto`, git.

## 6. Como funciona (decisões já tomadas)

### 6.1 Duas partes separadas

- **`Acerto.Core`**: o "cérebro". Biblioteca .NET pura, **sem nenhuma dependência de Windows**. Recebe
  eventos de texto (caractere digitado, backspace, "contexto resetado") e devolve ações ("apague N
  caracteres e escreva X"). Contém dicionário, ranqueamento, dicionário pessoal, atalhos, maiúscula
  automática, espaço duplo e a lógica de desfazer. Tem testes automáticos.
- **`Acerto.Windows`**: o app de bandeja. Escuta o teclado, traduz teclas em caracteres, pergunta ao
  Core e executa as ações digitando no programa da vez. Também cuida da UI (bandeja, janelas de
  configuração) e da detecção de campo de senha e de apps excluídos.

O Core não sabe que existe Windows. É isso que permite, no futuro, versão Mac, barra de sugestões
(etapa 2) ou plugin sem reescrever o corretor.

### 6.2 Escutar e digitar

- Hook global de teclado de baixo nível (`SetWindowsHookEx(WH_KEYBOARD_LL)`), sem injetar DLL em
  outros processos.
- O programa mantém a palavra atual num buffer. O buffer **zera** em: clique do mouse, setas,
  Home/End/PageUp/PageDown, troca de janela/foco, Ctrl/Alt/Win + tecla, Esc. Depois de zerar, o
  programa não corrige até começar uma palavra nova. Corrigir em cima de um texto que ele não viu é proibido.
- A correção é feita com `SendInput` + `KEYEVENTF_UNICODE`: N backspaces + palavra corrigida + o
  caractere final. Os eventos que o próprio programa gera são marcados e ignorados pelo hook.
- O callback do hook tem que voltar **muito rápido**. O Windows derruba hooks lentos, e um hook lento
  trava a digitação do sistema todo.

### 6.3 Como escolher a correção

Ordem de verificação:

1. **Atalho de texto** do usuário → expande. Atalhos vêm antes dos dicionários, então um atalho
   funciona mesmo se o gatilho for uma palavra que existe.
2. **Dicionário pessoal**, número, sigla toda em maiúscula, e-mail, URL ou caminho de arquivo → não mexe.
3. **Lista de correções embutida** (arquivo de dados curado, que vem com o programa): pares fixos
   `errado → certo`, inclusive os que viram duas palavras (`oque` → `o que`, `concerteza` → `com certeza`,
   `apartir` → `a partir`). Começa com as correções de `D:\Dev\autocorrecao-ptbr\autocorrecao.ahk` e cresce
   com o tempo. O usuário não edita essa lista; para desligar uma entrada, basta desfazer (vai para o
   dicionário pessoal, que ganha da lista porque vem antes).
4. **Acento esquecido:** índice "palavra sem acento → palavras com acento" (`informacao` → `informação`).
   - Se a forma digitada **não existe**: troca se há só uma candidata ou uma claramente mais frequente.
   - Se a forma digitada **existe** mas é muito rara perto da acentuada (`agua` de "aguar" × `água`,
     `pagina` × `página`, `numero` × `número`, `secretario` × `secretário`): troca, igual o celular faz.
   - Se as duas são comuns (`esta`/`está`, `pais`/`país`, `secretaria`/`secretária`): não mexe.
   A razão de frequência que conta como "muito rara" é decisão de quem constrói, calibrada pelos testes do §7.
5. Se a palavra está no **dicionário pt-BR** → não mexe.
6. **Erro de escrita/digitação:** gera candidatas por distância de edição (troca, falta, sobra,
   inversão), com peso menor para letras vizinhas no teclado ABNT2, combinada com a frequência da
   palavra no português. Troca **só se a melhor candidata ganha com folga da segunda**. Senão, não mexe.
7. Mantém o padrão de maiúsculas: `Nao` → `Não`, `NAO` → `NÃO`.

**O que desfazer ensina:** desfazer uma correção de palavra (regras 3, 4 e 6) põe a palavra original no
dicionário pessoal. Desfazer um atalho, uma maiúscula automática ou um espaço-duplo-vira-ponto só
desfaz aquela vez e não ensina nada.

### 6.4 Dados

- **Dicionário:** pt-BR do projeto VERO (o mesmo do LibreOffice), em formato Hunspell, carregado com
  uma biblioteca .NET de Hunspell (ex.: `WeCantSpell.Hunspell`). Tem que reconhecer todas as flexões e
  conjugações.
- **Frequência de palavras:** uma lista de frequência do português com licença comercial-compatível,
  usada para ranquear candidatas.
- **Dados do usuário** em `%APPDATA%\Acerto\`: palavras aprendidas, atalhos, apps excluídos e
  configurações, em arquivos legíveis (JSON ou texto).

### 6.5 Onde não corrigir

- **Campo de senha:** detectado via UI Automation (`IsPassword` do elemento com foco). Se não der para
  saber, prefere não corrigir em campos marcados como senha pelo Win32 (`ES_PASSWORD`).
- **Apps excluídos:** lista por nome de processo, editável. Pausa automática nesses apps.
- **Apps rodando como administrador:** o Windows não deixa um programa comum digitar neles. Na etapa 1 o
  Acertô simplesmente não funciona nesses apps, e isso fica documentado.

### 6.6 Apoie

Item "Apoie o projeto" no menu, que abre uma janelinha com texto e espaço para o QR code e a chave Pix
(placeholder até o usuário fornecer a chave).

## 7. O que conta como sucesso

- O usuário usa o Acertô por uma semana no Claude, Chrome, Teams e Word e:
  - acentos esquecidos são corrigidos sem ele pensar nisso;
  - correções erradas são raras, e o Backspace resolve quando acontecem;
  - ele nunca sente atraso nem perde tecla.
- Teste automático do Core com dois conjuntos:
  - **deve corrigir:** pares `errado → certo`, incluindo as 170 correções já levantadas em
    `D:\Dev\autocorrecao-ptbr\autocorrecao.ahk`;
  - **não pode mexer:** palavras corretas, inclusive as ambíguas em que as duas formas são comuns
    (`esta`, `pais`, `e`, `tem`, `nos`, `secretaria`, `publico`, `pratica`, `inicio`), nomes próprios comuns,
    siglas, números, URLs e e-mails.

## 8. Deixado para quem vai construir

- Estrutura exata da solução (projetos, pastas), framework de UI da bandeja (WinForms/WPF) e de testes.
- Algoritmo exato de candidatas e o limiar de "ganhar com folga", desde que respeite "na dúvida, não troca".
- Fonte exata da lista de frequência, respeitando a licença.
- Atalho de teclado para pausar e nomes exatos de processo da lista padrão de apps excluídos
  (que precisa cobrir pelo menos os editores de código e terminais do §2.7).
- Formato dos arquivos de configuração.
- Ícone (sugestão: um "ô" ou um ✓ estilizado).
