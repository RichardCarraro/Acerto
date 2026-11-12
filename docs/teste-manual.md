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
