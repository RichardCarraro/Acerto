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
4. Gere publish e instalador (comandos no README) e faça commit na `main` com o placar antes → depois na mensagem.
5. Acrescente uma linha em "Histórico" abaixo.
6. Próximo ciclo. Pare quando três ciclos seguidos não acharem melhoria que passe nas regras; então liste o que
   ficou de fora e por quê.

---

## Histórico

| Data | Versão | Mudança | Antes → depois |
|---|---|---|---|
| 2026-11-16 | 1.3.5 | `data/nomes.txt`: nomes e lugares brasileiros (chave minúscula sem acento → forma certa), consultado antes das outras regras — `parana` virava **parada** e `maringa` virava **marinha** pela regra de palavra válida rara; `joao`/`belem`/`iguacu` só ganhavam acento. Só nomes que não são palavra comum (`marcos`, `santos`, `natal`, `foz` ficam de fora). `#` comenta nos arquivos de dados | frases reais 92,3% → 93,3% (313); perfil cuidadoso intacto |
| 2026-11-16 | 1.3.4 | Nome próprio em minúscula ganha maiúscula quando o dicionário só o tem assim (`curitiba` → Curitiba, `londres`), com 4 letras ou mais (`big`, `man`, `down` também são "nomes" no VERO); `CorrectionKind.Case`; modo `caixa` no Eval; locuções com `lá` (`de la`, `por la`, `até la`); lote 4 de 40 frases | frases reais 93,8% (273) → 92,3% (313; o lote 4 trouxe `joão`/`maria`/`brasil`, que o VERO aceita em minúscula, e verbos no futuro); `caixa`: 1184 das 60 mil frequentes ganhariam maiúscula, quase todas nomes (john, europa, alemanha) |
| 2026-11-16 | 1.3.3 | `data/locucoes.txt`: locuções de duas palavras feitas à mão, consultadas pela anterior (`isso ai` → aí, `a viajem` → viagem, `não mecha` → mexa) e pela seguinte (`as vezes` → às, `a toa` → à); chave de três palavras quando a de duas é ambígua (`fica a vontade` → à; `a vontade` sozinha é substantivo 495 × 261 no corpus) e como exceção (`todas as vezes` fica). Contagens decidiram o que entrou: `as claras` ficou de fora (claras de ovo) | frases reais 92,3% → 93,8% (273, 0 estragos); acento pela seguinte volta 33,6% → 34,1% (`as`→`às` 34 → 80), estragos 195 → 200; contexto A e B iguais, C 502 → 509 |
| 2026-11-16 | 1.3.2 | Classe fonética: `x`↔`ch` e `c`↔`ss` como dígrafos (um erro, não dois), `TypoKind.Phonetic` só para pares de escrita (`s/z`, `s/c`, `g/j`, `x/s`, `x/z`, `m/n`; `k/c` e vogais ficam de fora, são nomes), razão própria para atestadas (10) e custo 0,4; categoria "fonético" no Eval sintético; `paralização` e família na lista (estava em `protegidas.txt` porque o corretor do Windows aceita); lote 3 de 50 frases | frases reais 93,7% (223) → 92,3% (273; o lote 3 trouxe crase e homófonos que não dá para pegar); fonético gerado 84,9% → 87,3% certo, errado 5,2% → 2,7%; trocas no corpus 1640 → 1661; sintético total 94,2% = |
| 2026-11-16 | 1.3.1 | Limiar do acento pela seguinte 10 → 7; Tab desfaz a correção pela seguinte (as duas palavras, sem aprender "e"); lista embutida cede ao contexto forte (`bom dai` → dia: par ≥ 200 e par da lista = 0; começo de frase não conta, senão `pao` virava `Não`); `exite` → `hesite`; animação com largura mínima para palavra de uma letra e barra surgindo em 90 ms; UiShots fotografa a animação quadro a quadro; 48 frases novas | frases reais 90,9% → 93,7% (223 frases, 0 estragos); acento pela seguinte B 30,2% → 33,6%, C 0,038% → 0,054%; contexto A 93,0% → 93,1%, B e C iguais |
| 2026-11-16 | 1.3.0 | Acento pela palavra seguinte (`isso e muito` → `é`, limiar 10); termos de TI protegidos (`deploy`, `commit`, `teams`); acentuada exata ganha da candidata de digitação menos usada (`almocaram` → `almoçaram`); erros fonéticos na lista; animação com entrada suave | frases reais 79,4% → 90,9% (0 estragos); acento pela seguinte: +30% dos que escapavam, +0,038% estragos; contexto B 81,0% → 81,7%; sintéticos 94,2% = |

### Tentado e reprovado / pistas

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
- **Verbo no futuro sem acento** (`chegara amanhã`, `fara o possível`): a forma sem acento é o mais-que-perfeito,
  válida e rara; `fara`/`tera` já vão pela regra do contexto, `chegara` não tem par. Candidata a regra: forma
  válida muito mais rara que a acentuada (razão de frequência) quando a anterior é sujeito.
- **Locuções**: `data/locucoes.txt` cresce com uso; antes de pôr uma de duas palavras, conte os dois lados no
  `bigramas.txt` (`=a` → `vontade` contra `=à` → `vontade`) e use três palavras ou exceções quando empatar.
- **Fonético gerado, ainda errado**: `pessa` → pessoa (peça), `pesso` → pessoa (peço): letra faltando e dígrafo
  custam parecido e `pessoa` é mais frequente. `eçe`/`axa`/`xão` não corrigem (3 letras: regra de palavra curta).
