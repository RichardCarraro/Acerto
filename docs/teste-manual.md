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
