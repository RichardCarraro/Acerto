# Acertô

**Corretor automático para Windows, em português, que funciona em qualquer programa — igual ao teclado do celular.**

Você digita `nao`, aperta espaço e ele já virou `não`. Sem sublinhado vermelho, sem clicar em nada. Funciona no navegador, no Word, no Teams, no WhatsApp Desktop, no app do Claude, no Bloco de Notas, em qualquer lugar onde se digita.

## O que ele faz

- **Corrige sozinho ao terminar a palavra** (espaço, Enter ou pontuação):
  - acento e cedilha esquecidos — `informacao` → `informação`, `voce` → `você`, e também em formas que existem sem acento mas quase ninguém usa: `chegara` → `chegará`, `veiculo` → `veículo`
  - erros de digitação — `mutio` → `muito`, `paara` → `para`, `probelma` → `problema`, `hje` → `hoje`
  - erros comuns de escrita — `concerteza` → `com certeza`, `excessao` → `exceção`, `idéia` → `ideia`
  - trocas de som — `enxente` → `enchente`, `xinelo` → `chinelo`, `paralização` → `paralisação` (`x`/`ch`, `ss`/`ç`, `s`/`z`, `g`/`j` contam como um erro só)
- **Usa o contexto** para escolher a correção certa, como o celular: `minha cas` → `casa`, `esse cas` → `caso`, `ele esta` → `ele está`. Olha também a palavra seguinte: em `isso e muito`, quando o `muito` termina, o `e` vira `é` (Tab desfaz). E o contexto ganha até da lista de erros comuns: `bom dai` → `bom dia`, não `daí`. Locuções que a estatística não decide vêm de uma lista feita à mão (`data/locucoes.txt`): `isso ai` → `isso aí`, `fica a vontade` → `à vontade`, `as vezes` → `às vezes` (mas `todas as vezes` fica), `a viajem` → `a viagem`.
- **Sugestões ao digitar**: uma barrinha perto do cursor mostra até três palavras para completar a atual (mesmo com erro: `obrgi` → `obrigado`) ou adiantar a próxima. **Tab** aceita a primeira. Logo depois de uma correção, a barrinha mostra o que você digitou e outras opções: **Tab** desfaz a correção.
- **Aprende o seu jeito**: as palavras e sequências que você mais usa passam na frente nas sugestões e nas correções.
- **Maiúscula automática** no começo da frase e em nomes que o dicionário só conhece com maiúscula: `fui para curitiba` → `Curitiba`, `londres` → `Londres` (a partir de 4 letras). Nomes e lugares brasileiros comuns vêm de uma lista (`data/nomes.txt`): `joao` → `João`, `parana` → `Paraná`, `maringa` → `Maringá`.
- **Dois espaços viram ponto final.**
- **Atalhos de texto** personalizáveis: `vlw` → `valeu`, `att` → `Atenciosamente,`.
- **Aprende com você**: apagou a correção e digitou a palavra de novo? Ele mantém e não corrige mais.
- **Animação** rápida sobre a palavra corrigida, para você perceber o que mudou.
- **Não mexe** em campos de senha, em programas rodando como administrador, em jogos em tela cheia, nem em editores de código e terminais (configurável).

## Privacidade

Tudo roda no seu computador. O Acertô **não se conecta à internet** e **não grava o seu texto**. Em `%APPDATA%\Acerto` ficam só o número de palavras corrigidas, as palavras que você ensinou, os seus atalhos e, com "Aprender com o que eu escrevo" ligado, a contagem das palavras do dicionário que você usa e de quais vêm uma depois da outra (`historico.txt`). Senhas, números e e-mails nunca entram nessa contagem; dá para desligar ou apagar tudo nas configurações.

## Instalação

Baixe e rode o `Acerto-Setup-<versão>.exe`. Não precisa de administrador. O ícone **ô** aparece perto do relógio: clique nele para pausar, abrir as configurações ou sair. **Ctrl+Shift+F12** pausa e retoma de qualquer lugar.

> O instalador não tem assinatura digital, então o Windows pode mostrar "O Windows protegeu o computador". Clique em **Mais informações → Executar assim mesmo**.

## Configurações

| Opção | O que faz |
|---|---|
| Corrigir mais | Tenta corrigir sempre, como o celular, mesmo com dúvida. Desligado, só corrige quando tem certeza. |
| Sugestões ao digitar | Mostra a barrinha de sugestões; Tab aceita a primeira (logo depois de uma correção, Tab desfaz). |
| Aprender com o que eu escrevo | Conta as palavras que você usa para sugerir e corrigir do seu jeito. Botão "Apagar o que aprendi". |
| Maiúscula automática | Começa as frases com maiúscula. |
| Dois espaços viram ponto | Espaço duplo termina a frase. |
| Destacar a palavra corrigida | Liga ou desliga a animação. |
| Iniciar com o Windows | Abre sozinho ao ligar o computador. |

Também dá para editar **atalhos de texto**, **minhas palavras** (que nunca são corrigidas) e **apps excluídos**.

## Como funciona

O corretor (`Acerto.Core`) é uma biblioteca .NET independente do Windows:

1. **Dicionário** pt-BR completo (VERO/Hunspell, o do LibreOffice) para saber se a palavra existe.
2. **Lista de frequência** de palavras para saber quais são mais usadas.
3. **Modelo de erro de digitação** com pesos: letras invertidas, letra dobrada, letra faltando e tecla vizinha no teclado ABNT2 custam menos que erros improváveis.
4. **Pares de palavras vizinhas** (bigramas) para usar a palavra anterior como contexto e para sugerir a próxima.
5. **Lista curada** de quase mil erros comuns, minerados do corpus e revisados à mão.

A parte do Windows (`Acerto.Windows`) escuta o teclado com um hook global, troca a palavra enviando as teclas de volta ao programa e usa UI Automation para saber onde está o cursor (para a animação, as sugestões e para corrigir já a primeira palavra de um campo). A interface é WPF com o tema do Windows 11.

### Números

Medido com `tests/Acerto.Eval` (erros gerados nas palavras mais usadas e frases reais fora dos dados de treino):

- acerta **~94%** dos erros de digitação no modo "Corrigir mais"
- com contexto, em frases reais: **93,0%** de acerto contra 89,7% sem contexto; acento esquecido volta em **83,0%** dos casos
- palavras certas estragadas em texto real: **0,12%**
- frases do dia a dia com erros reais (`tests/Acerto.Eval/frases-reais.txt`, 419 frases): **92,6%** saem certas, nenhuma frase certa é mexida
- erros de escrita por som gerados (`x`/`ch`, `ss`/`ç`, `s`/`z`, `g`/`j`): **87%** corrigidos
- sugestões: o Tab economiza **~29%** das letras das palavras de 4 letras ou mais

## Desenvolvimento

Requer o **.NET 10 SDK** e, para o instalador, o **Inno Setup 7**.

```bash
# testes (≈ 4 min)
dotnet test tests/Acerto.Core.Tests

# programa
dotnet publish src/Acerto.Windows -c Release -r win-x64 --self-contained -o publish

# instalador (sai em dist/)
"C:\Program Files\Inno Setup 7\ISCC.exe" installer\acerto.iss
```

A versão fica em `src/Acerto.Windows/Acerto.Windows.csproj` (`<Version>`); o instalador lê dela.

Estrutura:

```
src/Acerto.Core        corretor, contexto, sugestões, motor de texto (sem dependência de Windows)
src/Acerto.Windows     app de bandeja: hooks, injeção de teclas, interface
tests/Acerto.Core.Tests  testes (incluem as 60 mil palavras mais usadas que não podem ser alteradas)
tests/Acerto.Eval      medição de acerto (frases, contexto, direita, sugestoes), mineração de erros comuns, bigramas
tests/Acerto.SmokePad  teste ponta a ponta que digita numa janela
tests/Acerto.Bancada   digita frases variadas com erros sorteados (caixa, Chrome ou Edge) e compara com o motor
data/                  dicionário, frequências, correções, locuções, nomes, bigramas
installer/             roteiro do instalador (Inno Setup)
```

Com o Acertô aberto, `AcertoBancada.exe frases=20 alvo=chrome saida=bancada.txt` (também `alvo=caixa|edge`, `semente=N`, `rapido=1`) digita como uma pessoa e diz se o app ficou diferente do motor (parada, tecla perdida) e quanto corrigiu certo. Não mexa no mouse e no teclado enquanto ele roda.

Para depurar sem registrar texto, defina `ACERTO_DIAG=<arquivo>` antes de abrir: ele grava só estados (ativo, foco, motivo de cada reset).

Para melhorar o corretor em ciclos medidos (sozinho, com o Claude Code), veja [docs/melhoria-continua.md](docs/melhoria-continua.md).

## Limitações conhecidas

- A troca de acento pela palavra seguinte não acontece quando a seguinte termina com `.` ou `:` (eles esperam para ver se é e-mail ou site).

- No modo "Corrigir mais", palavras em inglês digitadas em minúsculo às vezes são trocadas (`list` → `lista`). Apague e digite de novo que ele aprende, ou desligue o modo.
- Não funciona em programas abertos como administrador (limitação do Windows), a não ser que o Acertô também seja aberto como administrador.
- A barra de sugestões e a animação só aparecem em programas que informam a posição do cursor. Em apps como o Evernote, que informam só o cursor, a animação é posicionada por estimativa e pode ficar um pouco deslocada.
- Na pesquisa do Windows a correção funciona, mas a barra de sugestões e a animação ficam atrás dela: o Windows só deixa desenhar por cima da pesquisa programas assinados e instalados em Arquivos de Programas.
- Na barra de endereço do Chrome, Edge e Firefox a correção usa o perfil cuidadoso (para não trocar nomes de site) e apaga antes o trecho que o navegador completa sozinho. Outros campos com autocompletar que seleciona texto (Excel) podem confundir a correção; adicione-os em "Apps excluídos" se acontecer.

## Créditos

Veja [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Resumo:

- Dicionário **VERO** (LibreOffice) — LGPLv3 / MPL
- **FrequencyWords** (Hermit Dave, OpenSubtitles) — CC BY-SA 4.0
- **WeCantSpell.Hunspell** — MPL 1.1
- Bigramas derivados da **Leipzig Corpora Collection** — CC BY; **uso comercial depende de autorização** do projeto. Antes de vender, obtenha a autorização ou gere `data/bigramas.txt` de outro corpus (`AcertoEval data bigramas <frases.txt> data/bigramas.txt`).

## Apoie

O Acertô é grátis. Se ele te ajuda, considere apoiar o projeto pela página **Apoie** dentro do app.
