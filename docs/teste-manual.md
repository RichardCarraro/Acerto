# Acertô — Checklist de aceitação da etapa 1

Build: `dotnet publish src/Acerto.Windows -c Release -r win-x64 --self-contained -o publish` → `publish\Acerto.exe`.

## Automático (AcertoSmokePad) — 2026-11-12

`tests/Acerto.SmokePad` digita numa caixa de texto com o Acertô rodando (Release, pasta movida para fora do repositório):

| Caso | Resultado |
|---|---|
| ` nao voce informacao poblema oque ` → ` não você informação problema o que ` | ✅ |
| Backspace logo depois de atalho desfaz (` vlw ` → ` valeu ` → ` vlw `) | ✅ |
| Backspace depois de outra tecla não desfaz | ✅ |
| Maiúscula depois de `. ` e de `.  ` | ✅ |
| Espaço duplo vira `. ` | ✅ |
| Enter corrige (` nao⏎` → ` não⏎`) | ✅ |
| URL, e-mail e `abc123` intactos | ✅ |
| `esta pais secretaria casa` intactos | ✅ |
| Campo de senha não é corrigido | ✅ |
| Rajada sem intervalo entre teclas (0 ms), nada fora de ordem | ✅ (44/44 em 4 rodadas) |
| Segunda instância fecha sozinha | ✅ |

Observação: uma rodada isolada com 20 ms apagou o começo do primeiro caso e não se repetiu em 44 casos seguidos. Provavelmente foi interferência de uso da área de trabalho durante o teste. Fique de olho no M3.

## Riscos abertos depois da revisão final

- Rajada sem intervalo (0 ms entre teclas): em 1 de 4 rodadas, ` nao. voce: ` perdeu o ponto, embora o motor tenha mandado a correção certa. Em velocidade de digitação real (30 ms) passou sempre. Confira no M3, com foco em frases terminadas em ponto.
- Campos com autocompletar que seleciona texto (Excel, barra de endereço do navegador, Executar): a correção pode sair errada ("nnão"). Se acontecer, adicione o app em "Apps excluídos".
- Tab não corrige mais a palavra anterior, porque em Word, Outlook e Gmail ele aceita a sugestão do próprio app.

## Manual — falta fazer (Bloco de Notas, Chrome, app do Claude, Teams, Word)

- [ ] M1 `nao voce informacao poblema oque ` → `não você informação problema o que ` em cada app
- [ ] M2 teclas mortas: `´a ~a ^e ¨u ´␣` saem iguais com e sem o Acertô; AltGr+Q = `/`
- [ ] M3 digitar `informacao nao voce tambem ` o mais rápido possível 5×: nenhuma letra fora de ordem ou perdida
- [ ] M4 digitar `informa`, clicar em outro lugar, digitar `cao ` → nada corrigido
- [ ] M5 campo de senha (login do Chrome/site): `nao ` continua `nao `
- [ ] M6 Bloco de Notas como administrador: digitar normalmente, nenhuma tecla perdida
- [ ] M7 VS Code e Windows Terminal: nada corrigido (excluídos por padrão)
- [ ] M8 Backspace logo depois de uma correção desfaz; depois disso a palavra não é mais corrigida (e aparece em "Palavras aprendidas…")
- [ ] M9 Enter no chat do Claude com `nao` → envia "não", e o Backspace seguinte não reenvia nada
- [ ] M10 Ctrl+Shift+F12 pausa e retoma; o ícone fica cinza/azul
- [ ] M11 Menu: Palavras aprendidas, Atalhos (criar `fds=fim de semana` e testar), Apps excluídos, Iniciar com o Windows (reiniciar o PC), Apoie

## Mudanças depois da etapa 1 (2026-11-12, tarde) — conferir no uso

- [ ] Interface nova: clique no ícone ô abre o painel translúcido; "Configurações" abre a janela no estilo do Windows 11 (fundo translúcido só aparece na tela de verdade; as imagens de conferência usam cor sólida).
- [ ] Animação: ao corrigir, a palavra ganha um realce que some em ~0,5 s e um sublinhado que some em ~1,5 s. Conferir se cai em cima da palavra certa no Claude, Chrome, Word e Bloco de Notas. Dá para desligar em Início → "Destacar a palavra corrigida".
- [ ] Primeira palavra: clicar num campo vazio e digitar `testi ` → `Teste `. Em apps que não informam o texto antes do cursor continua valendo "espera o primeiro espaço".
- [ ] Backspace depois de uma correção só apaga (o item M8 antigo, "Backspace desfaz", deixou de valer).
- [ ] Apagar a palavra corrigida e digitar a mesma de novo: fica como digitada e entra em "Minhas palavras".
- [ ] `palvra`, `geito`, `anida`, `intendi` são corrigidas (lista embutida com ~300 entradas).

## Corretor de digitação e animação (2026-11-12, fim da tarde) — conferir no uso

Medição com `tests/Acerto.Eval` (103 mil erros gerados nas 3000 palavras mais usadas): acerto 89,1% dos erros
corrigíveis, 1,7% de correção errada. Por tipo: letra dobrada 97%, inversão 93%, tecla a mais 91%, tecla vizinha 85%,
letra faltando 55% (o resto é ambíguo sem contexto: `cas` é casa ou caso?).

- [ ] Erros comuns: `palvras mutio vcoê tabém obigado probelma trablho dificl coputador hje erado` são corrigidos.
- [ ] Nomes, siglas e estrangeirismos em minúsculo continuam intactos (`curitiba apps office cpf pdf`).
- [ ] Animação: aparece em cima da palavra corrigida mesmo quando você continua digitando; some ao clicar,
      trocar de janela ou apertar Enter; não pisca em posição antiga.

Limite conhecido: erros muito frequentes que coincidem com palavras de outros idiomas (`save`, `estaba`, `ahora`)
não são corrigidos, porque o corpus não distingue erro de palavra estrangeira.

## Modo "Corrigir mais" (2026-11-12, padrão ligado)

Pedido do usuário: melhor tentar e errar às vezes do que não corrigir, como o teclado do celular.
Medição (`AcertoEval data 3000 0 perfil=agressivo`): acerto 93,6% dos erros corrigíveis (cuidadoso: 89,1%),
correção errada 4,7% (cuidadoso: 1,7%), quase nunca desiste. Preço medido: 227 das 60 mil palavras mais frequentes
do corpus passam a ser trocadas, várias em inglês ou espanhol (`code`→`pode`, `plan`→`plano`, `send`→`sendo`,
`ahora`→`agora`). `data/protegidas.txt` (10.596 nomes e estrangeirismos que o corretor do Windows aceita) fica de fora.

- [ ] Se trocar uma palavra que você queria: apague e digite de novo; ele mantém e aprende (Minhas palavras).
- [ ] Início → "Corrigir mais" desligado volta ao modo que só corrige quando tem certeza.

## Filtro afrouxado e primeira palavra (2026-11-12, 15h)

Decisão do usuário: afrouxar o filtro de erros "atestados" do modo Corrigir mais (30/50 em vez de 300/3000).
Medição: acerto 94,2%, correção errada 4,9%; 1636 das 60 mil palavras frequentes do corpus passam a ser trocadas,
várias em inglês digitadas em minúsculo (`put`→`puta`, `one`→`onde`, `list`→`lista`, `save`→`sabe`).
Saída sem esse preço: lista de palavras em inglês/espanhol em `data/protegidas.txt`.

- [ ] Primeira palavra no app do Claude, Chrome e Teams: clicar na caixa vazia e digitar `testi ` → `Teste `.
      (A consulta do texto antes do cursor atravessava a caixa e lia o resto da página; agora é limitada ao campo.)
- [ ] `corrie`, `escrevu`, `corrigndo` são corrigidas (pode não ser para a palavra que você queria).
