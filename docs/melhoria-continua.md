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
| Testes | `dotnet test tests/Acerto.Core.Tests` (≈ 4 min) | todos passam |

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
   não rouba foco, respeita "Efeitos de animação" do Windows, tema claro/escuro. Mudança visual: tire foto com
   `tests/Acerto.UiShots` e olhe a imagem antes de commitar.

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
| 2026-11-16 | 1.3.0 | Acento pela palavra seguinte (`isso e muito` → `é`, limiar 10); termos de TI protegidos (`deploy`, `commit`, `teams`); acentuada exata ganha da candidata de digitação menos usada (`almocaram` → `almoçaram`); erros fonéticos na lista; animação com entrada suave | frases reais 79,4% → 90,9% (0 estragos); acento pela seguinte: +30% dos que escapavam, +0,038% estragos; contexto B 81,0% → 81,7%; sintéticos 94,2% = |

### Tentado e reprovado / pistas

- **Palavra válida rara trocada pela vizinha que a anterior pede** (`tudo cetro` → `certo`, par ≥ 50, par digitado = 0):
  +75 estragos em 416 mil (`de menus` → `menos`). Antes de tentar de novo, crie no Eval uma medida de "erro que
  vira outra palavra válida" para ver o ganho.
- **Peso do contexto 0,9 nas sugestões**: +0,3 ponto de teclas economizadas (28,7% → 29,0%), mas mexe também nas
  correções. Só vale com uma opção separada para o `Suggester`.
- **Faltam no `frases-reais.txt`**: `e` → `é` com razão entre 5 e 9 (`ele e meu irmão`, `o cara e muito chato`),
  crase (`as três`), `bom dai` (a lista embutida manda `dai` → `daí` sem olhar o contexto), `eu concerto o carro`.
