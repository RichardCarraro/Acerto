# Ciclo de melhoria contínua do Acertô

Prompt para uma sessão do Claude Code melhorar o Acertô sozinha, em ciclos medidos. Use assim:

```
/loop Siga docs/melhoria-continua.md e faça um ciclo.
```

ou cole a seção "Prompt" numa sessão nova. Cada ciclo termina com um commit; nada vai para frente sem número melhor.

---

## Prompt

Você está melhorando o **Acertô**, corretor automático de português para Windows (leia o `README.md`). O objetivo é
que ele corrija certo o máximo de vezes, estrague o mínimo de palavras certas e que sugestões e animação pareçam as
de um bom teclado de celular. Trabalhe em ciclos curtos, sempre medindo antes e depois.

### 1. Prepare (uma vez por sessão)

- `dotnet build tests/Acerto.Eval -c Release` → `E=tests/Acerto.Eval/bin/Release/net10.0/AcertoEval.exe`.
- Corpus fora da amostra para medir estragos: baixe para a pasta temporária um corpus pt-BR da Leipzig
  (`https://downloads.wortschatz-leipzig.de/corpora/por-br_newscrawl_2011_100K.tar.gz`) e use o
  `*-sentences.txt`. Ele é de notícias: parte do que aparece como "estragada" é erro do próprio jornal
  (`não da para` → `dá` está certo). Leia as amostras antes de culpar o motor.

### 2. Placar (rode tudo e anote no começo do ciclo)

| Medida | Comando | Quer |
|---|---|---|
| Frases reais | `$E data frases tests/Acerto.Eval/frases-reais.txt perfil=agressivo` | ↑ certas, **0** "ESTRAGOU" |
| Erros sintéticos | `$E data 3000 15 perfil=agressivo` | ↑ "certo" dos corrigíveis, ↓ ERRADO |
| Contexto | `$E data contexto <corpus> 30000 perfil=agressivo` | ↑ A e B, C estragadas não sobe |
| Acento pela seguinte | `$E data direita <corpus> 30000 perfil=agressivo` | ↑ B, C não sobe |
| Sugestões | `$E data sugestoes <corpus> 2000 perfil=agressivo` | ↑ teclas economizadas, ↑ próxima palavra |
| Desempenho | `$E data desempenho tests/Acerto.Eval/frases-reais.txt <corpus> 20000 perfil=agressivo` (3 vezes, mediana; com a máquina parada) | ↓ carga e p99; **assinatura das respostas igual** (otimização não muda resposta) |
| Testes | `dotnet test tests/Acerto.Core.Tests -nologo --logger "console;verbosity=normal" 2>&1 \| grep -E "\[FAIL\]\|Com falha\|Total de testes\|Aprovados\|Expected\|Actual" \| tail -30` (≈ 6 min) | resumo com `Com falha: 0` (ou só `Aprovados: N`). Saída sem o resumo **não** é aprovação: o filtro errou, rode de novo |

`contexto` e `direita` levam ~5 min: rode em segundo plano e em paralelo, com saída em arquivo.

### 3. Escolha UMA melhoria

Em ordem de valor:

1. **Falhas em `frases-reais.txt`.** Agrupe por causa (acento, contexto, palavra válida rara, inglês, regra de
   maiúscula) e ataque o maior grupo.
2. **Gere casos novos** antes de mexer: escreva 20–40 frases como pessoas escrevem de verdade em chat, e-mail de
   trabalho e redes (sem acento, letra trocada, `concerteza`, `seje`, gíria, termos de TI) e acrescente a
   `frases-reais.txt`. Sem TAB = a frase não pode mudar (gíria, inglês, nome, e-mail, número).
3. **Amostras das medidas**: as listas "ERRADO", "estragadas" e "só o contexto acertou" do Eval mostram padrões.
   `$E data explicar <palavra> ant=<anterior> perfil=agressivo` mostra as candidatas e notas.
4. **Sugestões**: a barra erra a próxima palavra ou demora a completar? Ajuste no `Suggester`, medindo com `sugestoes`.
5. **Animação e barra** (`src/Acerto.Windows/UI/HighlightOverlay.cs`, `SuggestionBar.cs`): rápida (< 700 ms),
   não rouba foco, respeita "Efeitos de animação" do Windows, tema claro/escuro. Mudança visual: rode
   `dotnet build tests/Acerto.UiShots -c Release` e `AcertoUiShots.exe <pasta> light` (e `dark`): sai
   `correcao-<ms>.png` com a animação de verdade quadro a quadro (entrada, auge, saída), `barra-sugestoes.png`
   e as telas de configuração. **Olhe as imagens** (Read) antes de commitar; compare com os quadros anteriores.

Onde mexer, do mais barato ao mais caro:
- **Dados**: `data/correcoes.txt` (erro → certo, sem contexto; pode incluir formas válidas raras como
  `estrupo`), `data/protegidas.txt` (nunca passam pela regra de digitação; as do fim são à mão), atalhos.
- **Limiares** em `TypoOptions` (`src/Acerto.Core/TypoRanker.cs`). Teste vários valores em paralelo passando
  `Opcao=valor` na linha do Eval, e escolha olhando acertos ganhos ÷ estragos ganhos.
- **Regra nova** no `Corrector`/`TextEngine`, sempre atrás de uma opção (0 = desligada) medida antes de ligar.

### 4. Regras que não se quebram

- **Estragar palavra certa é pior que não corrigir.** Uma mudança que aumenta "estragadas" (C) precisa trazer
  bem mais acertos — anote a conta (ex.: "+412 acertos, +55 estragos") no comentário da opção.
- Perfil cuidadoso (`TypoOptions.Careful`) continua com zero trocas indevidas: os testes de corpus exigem.
- Nunca corrigir: senha, e-mail, endereço, número, código, palavra com maiúscula no meio da frase (nome), sigla,
  "Minhas palavras". Tab não corrige. Apagou a correção e digitou de novo = fica e aprende.
- Não mexer no gabarito para fazer o teste passar, a não ser que o gabarito esteja errado (diga por quê).
- Toda regra nova ganha teste em `tests/Acerto.Core.Tests` (caso que corrige + caso parecido que não pode mudar).
- Privacidade: nada de rede no app, nada de gravar texto do usuário.
- `tests/Acerto.Bancada` digita de verdade no teclado e mouse da pessoa: só rode se ela pediu e não está usando o PC.

### 5. Feche o ciclo

1. Rode o placar inteiro de novo. Se piorou algo que importa, desfaça ou ajuste.
2. `dotnet test tests/Acerto.Core.Tests` passando.
3. Atualize os números do `README.md` se mudaram, suba a versão em `src/Acerto.Windows/Acerto.Windows.csproj`.
4. Gere publish e instalador (`powershell -ExecutionPolicy Bypass -File installer/gerar.ps1`) e faça commit na `main` com o placar antes → depois na mensagem.
5. Acrescente uma linha em "Histórico" abaixo.
6. Próximo ciclo. Pare quando três ciclos seguidos não acharem melhoria que passe nas regras; então liste o que
   ficou de fora e por quê.

---

## Rodada de 2026-11-18: linha de base, placar final e os 12 ciclos

Medido com `$E` (placar acima), corpus de notícias fora da amostra, perfil padrão (agressivo). Desempenho: mediana de
3 rodadas de `AcertoEval data desempenho`, máquina parada. Visual: `AcertoUiShots` nos temas claro e escuro, imagens
olhadas uma a uma.

| Frente | Medida | Linha de base (1.4.2) | Final (1.7.1) |
|---|---|---|---|
| Correção | frases reais certas | 92,6% de 419, 0 estragos | 94,3% de 559, 0 estragos |
| Correção | sintéticos (certo / ponderado) | 88,3% / 86,3% | 88,3% / 86,2% |
| Correção | contexto A com / sem; B; C | 93,0% / 89,7%; 83,0%; 573 | 93,0% / 89,6%; 83,1%; 576 |
| Correção | direita B; C | 32,7%; 189 | 34,3%; 194 |
| Correção | sugestões: próxima (1ª / 3); Tab economiza | 11,7% / 20,9%; 28,7% | iguais |
| Desempenho | CoreData.Load | 1268 ms (1513, 1265, 1268) | 898 ms (898, 992, 876) |
| Desempenho | criar o Corrector | 2638 ms (2679, 2638, 2580) | 361 ms (361, 386, 339) |
| Desempenho | Correct por palavra: média / p99 | 47,5 µs / 122 µs | 55,6 µs / 121 µs (entrada mudou: frases-reais ganhou 140 frases) |
| Desempenho | Suggest por tecla: média / p99 | 644 µs / 3,9 ms | 608 µs / 3,7 ms |
| Desempenho | memória (gerenciada / conjunto de trabalho) | 177 / 268 MB | 189 / 269 MB |
| Visual | caixas de texto vazias | sem dica nem nome acessível | dica cinza e nome acessível |
| Visual | foco de teclado (Tab) em interruptores e menus | sem moldura | moldura do Windows 11 |
| Visual | "Apoie o projeto" | "QR Code em breve" | QR Code, chave e copia e cola (pedido do autor) |

| Ciclo | Frente | Resultado | Versão | Testes do Core antes de publicar |
|---|---|---|---|---|
| 1 | Correção | aprovado: cidades de duas palavras | 1.4.3 | 1297, 0 falhas |
| 2 | Visual | aprovado: dica nas caixas vazias | 1.5.0 | 1297, 0 falhas (Core igual ao da 1.4.3) |
| 3 | Desempenho | aprovado: Hunspell em paralelo, StripAccents sem cópia | 1.5.1 | 1297, 0 falhas |
| 4 | Desempenho | aprovado: arquivos carregam juntos | 1.5.2 | 1297, 0 falhas |
| 5 | Correção | sem melhoria: lote 9, 39 de 40 já certas | — | — |
| 6 | Desempenho | sem melhoria: Hunspell em blocos | — | — |
| 7 | Visual | aprovado: moldura de foco | 1.6.0 | 1297, 0 falhas (Core igual ao da 1.5.2) |
| 8 | Correção | aprovado: par mínimo 3 no acento pela seguinte | 1.6.1 | 1298, 0 falhas |
| 9 | Desempenho | aprovado: data/validas.txt | 1.6.2 | 1299, 0 falhas |
| 10 | Visual | sem melhoria: revisão das telas e da animação | — | — |
| 11 | Correção | aprovado: ênfase fica como digitada | 1.6.3 | 1309, 0 falhas |
| 12 | Correção | aprovado: termos de contabilidade | 1.7.1 | 1325, 0 falhas |

Parada: 12 ciclos (o limite). O Pix (1.7.0) foi pedido do autor no meio da rodada e não conta como ciclo. Todas as versões da tabela têm release no GitHub com o `Acerto-Setup-<versão>.exe` (conferido pela API).

## Histórico

| Data | Versão | Mudança | Antes → depois |
|---|---|---|---|
| 2026-11-23 | 1.7.4 | Rodada de 2026-11-19, ciclo 3 (travamento na teclada): consultas de posição por UI Automation (barra de sugestões e animação) numa tarefa por vez (`AppController.OneAtATime`); a que fica velha esperando na fila nem começa. App ocupado com a teclada demora a responder UIA e as consultas se empilhavam no pool | consultas de UIA em andamento ao mesmo tempo: uma por tecla com pausa ≥ 30 ms → no máximo 1; Core igual ao da 1.7.2 (1326 testes, 0 falhas); efeito na máquina real a conferir pelo autor |
| 2026-11-23 | 1.7.3 | Rodada de 2026-11-19, ciclo 2 (travamento na teclada): o hook põe no máximo um `PublishSuggestions` na fila da UI, calculado para o estado mais recente (antes era um por tecla, todos calculados mesmo já velhos). Correção 3 do goal (limite de tamanho no `Correct`) dispensada: o espaço já fica abaixo de 1 ms com 100 letras | teclada de N teclas rápidas: N cálculos de sugestão na fila → 1; Core igual ao da 1.7.2 (1326 testes, 0 falhas); efeito na máquina real a conferir pelo autor |
| 2026-11-19 | 1.7.2 | Rodada de 2026-11-19, ciclo 1 (travamento na teclada): `NearPrefixes` só calcula o custo (O(n²)) das variações que levam a alguma palavra (busca binária antes) e o `Suggester` não tenta variações além da maior palavra indexada. Bloco "teclada" no modo `desempenho` do Eval e teste `TextEngineTecladaTests` (pior tecla e espaço ≤ 5 ms com 100 letras) | teclada pior tecla: 30 letras 66 → 0,18 ms, 50 letras 355 → 0,13 ms, 100 letras 3070 → 0,12 ms (soma 79 s → 1,4 ms); Suggest normal média 565 → 205 µs, p99 3,4 → 1,9 ms; assinatura das respostas igual (30A2FA5C024CE840) e de todas as sugestões de todos os prefixos igual (2C3319A5C28BE791); testes 1326, 0 falhas |
| 2026-11-18 | 1.7.1 | Contabilidade e impostos na lista à mão (a frequência, de legendas, quase não tem esses termos): `aliquota(s)`, `contabeis`, `tributavel/tributaveis`, `dedutivel`, `previdenciario(s)/previdenciarias`, `orcamentaria(s)/orcamentarios` (virava `orçamentária` no singular) e as válidas raras `tributaria` (0 × 92), `beneficio` (264 × 3605), `liquido` (259 × 4447), `alugueis` (47 × 356). Ficaram de fora `cambio` (espanhol e verbo) e `liquida` (verbo comum, 77 × 729). Lote 11 (contabilidade e e-mail formal) | frases reais 94,1% (529) → 94,3% (559), 0 estragos; contexto B 83,0% → 83,1% (sem contexto 75,9% → 76,1%), C 573 → 576; direita C 196 → 194; sintéticos e sugestões iguais |
| 2026-11-18 | 1.7.0 | Pedido do autor: "Apoie o projeto" com o Pix de verdade no lugar de "em breve". QR Code gerado do código lido no QR oficial da InfinitePay (CRC16 confere; 4 px por módulo, nítido em 100–200% de escala, sempre em fundo branco), chave aleatória por extenso e botões "Copiar chave" e "Copiar Pix copia e cola" | QR lido de volta das capturas dos dois temas = código original |
| 2026-11-18 | 1.6.3 | Ênfase fica como digitada: vogal 3+ vezes no fim (`naoooo` virava **namoro**, `muitooo` virava `muito`). Tecla presa (`issso`, `compreeendo`, `álcoool`) segue corrigida. Tentado também "qualquer letra 4+ vezes" e "vogal tripla em qualquer lugar": a primeira não muda nada, a segunda barrava `compreeendo`. Lote 10 (rede social: ênfase, risada, emoji, hashtag, menção, link). Zera a contagem de ciclos sem melhoria | frases reais 93,8% (499) → 94,1% (529), 0 estragos; sintéticos 88,3% = (ponderado 86,3% → 86,2%: casos artificiais como `topo` → `tooo`); contexto A sem contexto 89,7% → 89,6%, com contexto 93,0% =; B, C, direita e sugestões iguais |
| 2026-11-18 | — | Ciclo sem melhoria (visual): revisadas as telas nos dois temas (configurações, bandeja, barra, instalação) e os quadros da animação. O realce some aos 620 ms; o sublinhado fica até 1250 ms de propósito (mostra o que mudou depois que o realce some), então não foi encurtado. "QR Code em breve"/"Chave Pix: em breve" no apoio dependem da chave Pix, que só o autor tem. A sobra embaixo do instalador nas capturas é do UiShots (altura com a barra de título), não da janela. **1º ciclo seguido sem melhoria** | — |
| 2026-11-18 | 1.6.2 | Desempenho ao abrir: `data/validas.txt` (139.955 palavras da frequência que o Hunspell aceita, 1,4 MB, gerado por `AcertoEval data validas`) troca as ~370 mil consultas ao Hunspell da montagem dos índices por uma busca num conjunto; lido enquanto o Hunspell carrega. `DataTests.Validas_BateComDicionarioEFrequencia` falha se o arquivo ficar desatualizado. Testes de tempo (`TypoRankerTests`) numa coleção sem paralelismo: a montagem do corretor usa todos os núcleos e falseava o p99 | criar o corretor ~1430 → 359 ms (mediana de 3); CoreData.Load igual (~920 ms); pronto para corrigir ~2,3 s → ~1,3 s; memória gerenciada 181 → 189 MB; assinatura das respostas igual (92D60D7C6C833BD9), frases e sintéticos iguais |
| 2026-11-18 | 1.6.1 | Acento pela seguinte: par (acentuada, seguinte) mínimo 5 → 3 no perfil padrão (`NextMinPairCount`), pega `econômica esta complicada` (par `está complicada` = 3). Com 4: +58 acentos, +5 estragos. Estrago visto: `hilário e irônico` → é. Obs.: `TypoRankerTests.Rapido` (p99 ≤ 15 ms) falha se rodar junto com o placar; com a máquina livre passa. Uma rodada de `direita` × 3 em paralelo ficou parada sem CPU uma vez; não se repetiu em 96 cargas de estresse nem na reprodução | frases reais 93,6% → 93,8% (499), 0 estragos; direita B 32,7% → 34,3% (+125), C 189 → 196 (+7, 18:1); contexto, sintéticos e sugestões iguais |
| 2026-11-18 | 1.6.0 | Visual/acessibilidade: interruptores, menu lateral e menu da bandeja (`Switch`, `NavItem`, `MenuRow`) tinham `FocusVisualStyle` nulo e nenhum gatilho de foco: quem usa Tab não via onde estava. Agora usam a moldura do Windows 11 (`DefaultControlFocusVisualStyle`). UiShots desenha o estilo de foco por cima (`main-home-foco.png`) sem dar foco de verdade, e falha se o controle não tiver moldura. Zera a contagem de ciclos sem melhoria | antes: sem moldura de foco nos três; depois: moldura nos dois temas |
| 2026-11-18 | — | Ciclo sem melhoria (desempenho): consultas ao Hunspell em blocos de 4096 (`Partitioner.Create`) em vez de uma tarefa por palavra. Respostas iguais, sem ganho: criar o corretor 1390–1463 ms antes, 1516–1818 ms depois. O limite parece ser o próprio Hunspell, não a divisão do trabalho. **2º ciclo seguido sem melhoria** | assinatura de referência com o lote 9: 92D60D7C6C833BD9 |
| 2026-11-18 | — | Ciclo sem melhoria: lote 9 (40 frases de escola, saúde, compras, esporte, família e trabalho) teve 39 certas; a falha (`pais e na quinta`) é o `e` → `é` sem par no corpus (`é na` = fraco), já em "Tentado e reprovado". Lote guardado como regressão. **1º ciclo seguido sem melhoria** | frases reais 93,2% (459) → 93,6% (499), 0 estragos |
| 2026-11-18 | 1.5.2 | Desempenho ao abrir: `CoreData.Load` lê o dicionário Hunspell, a frequência e os bigramas ao mesmo tempo (são independentes); o erro de arquivo faltando sai como antes | CoreData.Load 1268 → 875 ms (mediana de 3); criar o corretor igual (~1500 ms); assinatura das respostas igual (22B72E41DDBD6C5A) |
| 2026-11-18 | 1.5.1 | Desempenho ao abrir: as ~370 mil consultas ao Hunspell do `TypoRanker` e do `AccentIndex` rodam em paralelo (`Lexicon.AreValid`, inserção na mesma ordem); `StripAccents` devolve a própria palavra quando ela é só ASCII (a maioria; roda também a cada tecla nas sugestões) | criar o corretor 2638 → 1501 ms (mediana de 3); CoreData.Load igual (~1270 ms); Suggest média 644 → 595 µs, p99 3,9 → 3,4 ms; Correct p99 122 → 112 µs; memória 268 → 279 MB; assinatura das respostas igual (22B72E41DDBD6C5A), frases e sintéticos iguais |
| 2026-11-18 | 1.5.0 | Visual: dica cinza dentro das caixas vazias ("Nova palavra, como um nome ou uma sigla", "Nome do programa, como Code.exe", "vlw" → "valeu" nos atalhos), como o PlaceholderText do Windows 11, e nome para leitor de tela nas quatro caixas. UiShots fotografa a caixa preenchida (`main-words-com-texto.png`) para provar que a dica some | antes: caixas vazias sem dica nem nome acessível; depois: dica nos dois temas, some com texto |
| 2026-11-18 | 1.4.3 | Cidades e estados de duas palavras em `locucoes.txt` (`sao paulo` → São Paulo, `belo horizonte`, `porto alegre`, `santa catarina`, `mato grosso`, `em salvador`); a palavra já corrigida também espera a seguinte, só para as locuções da lista (`sao` → `são` → `São`). `contexto` e `direita` não contam troca só de maiúscula como estrago (o corpus é minúsculo). Lote 8 (40 frases da vida real). Modo `desempenho` no Eval | frases reais 92,6% (419) → 93,2% (459), 0 estragos; contexto, direita, sintéticos e sugestões iguais (C 573 e 189); desempenho: carga 1268 ms + corretor 2638 ms, Correct p99 122 µs, Suggest p99 3,9 ms, 268 MB |
| 2026-11-17 | 1.3.9 | `e` sozinho antes de dois-pontos vira `é` (`a senha e: abc123`; notícias: 16 `é:` × 0 `e:`), com palavra antes (rótulo `E:` no começo fica) e fora da barra de endereço. Zera a contagem de ciclos sem melhoria | frases reais 92,4% → 92,6% de 419, 0 estragos |
| 2026-11-17 | — | Ciclo sem melhoria: lote 7 (30 frases longas com erros de digitação) teve 28 certas; as 2 falhas (`dela e semana`, `econômica esta complicada`) não têm par no corpus (`é semana` = 0, `está complicada` = 3 < 5). Lote guardado como regressão. **1º ciclo seguido sem melhoria** | frases reais 92,4% de 419 |
| 2026-11-16 | 1.3.8 | Lote 6 (internetês de chat: `pq`, `qnd`, `msm`, `vdd`, `sdds`, `tmj`, `kkk`): 36 de 39 já certas; `dboa` virava `boa` → protegidas. `não da` × `não dá` é 139 × 1015 (7:1, e "não da forma…" existe): fica fora das locuções | frases reais 92,3% de 389, 0 estragos |
| 2026-11-16 | 1.3.7 | Piso da regra de acento 360 → 1200 (`sera` → será, `ficara`, `dara`); reticências não dão mais maiúscula (`certo... não pensei`); lote 5 (mensagens de trabalho com pontuação, caminhos, e-mails, listas) | frases reais 93,6% (313) → 92,3% (350; o lote 5 trouxe `e` → `é` com seguinte que não é palavra); contexto B 82,5% → 83,0%, C 556 → 572 |
| 2026-11-16 | 1.3.6 | Forma válida rara ganha acento sem contexto quando a acentuada é ≥ 30× mais usada (`chegara` → chegará, `veiculo` → veículo, `duvidas` → dúvidas), só até 360 de uso no corpus (abaixo das 30 mil mais usadas: `esta`, `sabia`, `pais`, `critica` ficam) e com 4 letras ou mais (`pê`, `fá`, `dã` ficam); `RareAccentRatio`/`RareAccentMaxCount`, só no agressivo. Guarda das 30 mil válidas passa a aceitar acento abaixo do piso da regra. Eval `contexto` ignora maiúscula de nome em C (o corpus é minúsculo) | frases reais 93,3% → 93,6%; contexto B 81,7% → 82,5%, C 509 → 556 (+47, mais-que-perfeito real: `entendera`) |
| 2026-11-16 | 1.3.5 | `data/nomes.txt`: nomes e lugares brasileiros (chave minúscula sem acento → forma certa), consultado antes das outras regras — `parana` virava **parada** e `maringa` virava **marinha** pela regra de palavra válida rara; `joao`/`belem`/`iguacu` só ganhavam acento. Só nomes que não são palavra comum (`marcos`, `santos`, `natal`, `foz` ficam de fora). `#` comenta nos arquivos de dados | frases reais 92,3% → 93,3% (313); perfil cuidadoso intacto |
| 2026-11-16 | 1.3.4 | Nome próprio em minúscula ganha maiúscula quando o dicionário só o tem assim (`curitiba` → Curitiba, `londres`), com 4 letras ou mais (`big`, `man`, `down` também são "nomes" no VERO); `CorrectionKind.Case`; modo `caixa` no Eval; locuções com `lá` (`de la`, `por la`, `até la`); lote 4 de 40 frases | frases reais 93,8% (273) → 92,3% (313; o lote 4 trouxe `joão`/`maria`/`brasil`, que o VERO aceita em minúscula, e verbos no futuro); `caixa`: 1184 das 60 mil frequentes ganhariam maiúscula, quase todas nomes (john, europa, alemanha) |
| 2026-11-16 | 1.3.3 | `data/locucoes.txt`: locuções de duas palavras feitas à mão, consultadas pela anterior (`isso ai` → aí, `a viajem` → viagem, `não mecha` → mexa) e pela seguinte (`as vezes` → às, `a toa` → à); chave de três palavras quando a de duas é ambígua (`fica a vontade` → à; `a vontade` sozinha é substantivo 495 × 261 no corpus) e como exceção (`todas as vezes` fica). Contagens decidiram o que entrou: `as claras` ficou de fora (claras de ovo) | frases reais 92,3% → 93,8% (273, 0 estragos); acento pela seguinte volta 33,6% → 34,1% (`as`→`às` 34 → 80), estragos 195 → 200; contexto A e B iguais, C 502 → 509 |
| 2026-11-16 | 1.3.2 | Classe fonética: `x`↔`ch` e `c`↔`ss` como dígrafos (um erro, não dois), `TypoKind.Phonetic` só para pares de escrita (`s/z`, `s/c`, `g/j`, `x/s`, `x/z`, `m/n`; `k/c` e vogais ficam de fora, são nomes), razão própria para atestadas (10) e custo 0,4; categoria "fonético" no Eval sintético; `paralização` e família na lista (estava em `protegidas.txt` porque o corretor do Windows aceita); lote 3 de 50 frases | frases reais 93,7% (223) → 92,3% (273; o lote 3 trouxe crase e homófonos que não dá para pegar); fonético gerado 84,9% → 87,3% certo, errado 5,2% → 2,7%; trocas no corpus 1640 → 1661; sintético total 94,2% = |
| 2026-11-16 | 1.3.1 | Limiar do acento pela seguinte 10 → 7; Tab desfaz a correção pela seguinte (as duas palavras, sem aprender "e"); lista embutida cede ao contexto forte (`bom dai` → dia: par ≥ 200 e par da lista = 0; começo de frase não conta, senão `pao` virava `Não`); `exite` → `hesite`; animação com largura mínima para palavra de uma letra e barra surgindo em 90 ms; UiShots fotografa a animação quadro a quadro; 48 frases novas | frases reais 90,9% → 93,7% (223 frases, 0 estragos); acento pela seguinte B 30,2% → 33,6%, C 0,038% → 0,054%; contexto A 93,0% → 93,1%, B e C iguais |
| 2026-11-16 | 1.3.0 | Acento pela palavra seguinte (`isso e muito` → `é`, limiar 10); termos de TI protegidos (`deploy`, `commit`, `teams`); acentuada exata ganha da candidata de digitação menos usada (`almocaram` → `almoçaram`); erros fonéticos na lista; animação com entrada suave | frases reais 79,4% → 90,9% (0 estragos); acento pela seguinte: +30% dos que escapavam, +0,038% estragos; contexto B 81,0% → 81,7%; sintéticos 94,2% = |

### Tentado e reprovado / pistas

- **Gerar `data/validas.txt` de novo** sempre que `frequencia.txt` ou `pt_BR.dic`/`.aff` mudarem: `AcertoEval data validas > <pasta temporária>/validas.txt` e copiar (redirecionar direto para `data/` cria o arquivo vazio antes de o Eval ler).
- **Hunspell em blocos maiores** (`Partitioner.Create(0, n, 4096)`): sem ganho sobre `Parallel.For` (o paralelo já dá ~2× com 16 núcleos). Para abrir mais rápido de verdade, só evitando as 370 mil consultas ao abrir (por exemplo, gravar a validade junto da frequência na hora de gerar os dados).
- **Palavra válida rara trocada pela vizinha que a anterior pede** (`tudo cetro` → `certo`, par ≥ 50, par digitado = 0):
  +75 estragos em 416 mil (`de menus` → `menos`). Antes de tentar de novo, crie no Eval uma medida de "erro que
  vira outra palavra válida" para ver o ganho.
- **Peso do contexto 0,9 nas sugestões**: +0,3 ponto de teclas economizadas (28,7% → 29,0%), mas mexe também nas
  correções. Só vale com uma opção separada para o `Suggester`.
- **Acento pela seguinte abaixo de 7**: com 5 já troca `maior e melhor` → `é` (3 vezes em 30 mil frases). As que
  ainda escapam (`ele e meu irmão`, razão ≈ 6; `o rapaz e educado`, par `é educado` = 4 < mínimo 5) precisariam de
  outra evidência, não de limiar menor.
- **Crase `as três` → `às três`**: no corpus `as três` (327) é mais comum que `às três` (21); sem como decidir.
- **Homófonos válidos** (`eu concerto o carro` → conserto): não há par `eu concerto`/`eu conserto` no corpus;
  precisaria de uma lista de pares com o contexto que decide, feita à mão.
- **Palavra válida rara no lugar da comum** (`tudo cetro`, `combinado entoa`): ver a regra reprovada acima.
- **`meu email e joao.silva@gmail.com`**: a seguinte não é palavra; o acento pela seguinte não tem como ver. Fica.
- **`isso ai` → `aí`**: no corpus `isso ai` sem acento tem 49 contra 146 com acento (razão 3; a regra pede 10).
  Baixar o limiar para 3 estragaria o resto. Precisaria de uma lista de pares fixos (`isso aí`, `e aí`, `aí sim`).
- **Crase fora de locução** (`vou a praia`, `ela foi a feira`, `cheguei as duas`): `a praia`/`as duas` com artigo são
  tão ou mais comuns; precisaria de análise do verbo (ir/chegar + lugar), fora do alcance de pares.
- **`data/nomes.txt`** cresce com uso. Antes de pôr um nome, confira se não é palavra comum (`marcos`, `santos`,
  `natal`): `AcertoEval data explicar <nome>` mostra `válida=` e a contagem no corpus.
- **`e` → `é` quando a seguinte não é palavra** (`o valor e R$`, `a pasta e C:\`; `a senha e:` já resolvido) ou a anterior
  está entre aspas (`o campo "nome" e obrigatório`): a regra pela seguinte precisa de palavra dos dois lados.
  Candidata: com a anterior conhecida e a seguinte "não palavra", decidir só por P(é | anterior) com limiar alto.
- **Locuções**: `data/locucoes.txt` cresce com uso; antes de pôr uma de duas palavras, conte os dois lados no
  `bigramas.txt` (`=a` → `vontade` contra `=à` → `vontade`) e use três palavras ou exceções quando empatar.
- **Fonético gerado, ainda errado**: `pessa` → pessoa (peça), `pesso` → pessoa (peço): letra faltando e dígrafo
  custam parecido e `pessoa` é mais frequente. `eçe`/`axa`/`xão` não corrigem (3 letras: regra de palavra curta).
