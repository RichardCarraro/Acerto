# Créditos e licenças de terceiros

O Acertô distribui os arquivos abaixo sem modificação, como arquivos separados na pasta `data\`.

## Dicionário pt-BR VERO (`data/pt_BR.dic`, `data/pt_BR.aff`)
Projeto VERO — Verificador Ortográfico do LibreOffice. Copyright (C) 2006-2013 Raimundo Santos Moura e colaboradores.
Licença: LGPLv3 / MPL. Detalhes completos em `data/README_pt_BR.txt`.
https://pt-br.libreoffice.org/projetos/projeto-vero-verificador-ortografico/

## Lista de frequência de palavras (`data/frequencia.txt`)
FrequencyWords, de Hermit Dave (https://github.com/hermitdave/FrequencyWords), arquivo `content/2018/pt_br/pt_br_full.txt`,
derivado do corpus OpenSubtitles 2018 (http://opus.nlpl.eu/OpenSubtitles2018.php).
Licença do conteúdo: Creative Commons Attribution-ShareAlike 4.0 (CC BY-SA 4.0), https://creativecommons.org/licenses/by-sa/4.0/
Licença do código do projeto: MIT (`data/LICENSE_FrequencyWords_MIT.txt`).

## WeCantSpell.Hunspell
https://github.com/aarondandy/WeCantSpell.Hunspell — Licença MPL 1.1 / GPL 2.0 / LGPL 2.1 (herdada do Hunspell). Usado sem modificação.

## Pares de palavras para correção por contexto (`data/bigramas.txt`)
Tabela derivada (contagens de palavras vizinhas) do corpus `por-br_newscrawl_2011_1M` da Leipzig Corpora Collection,
Universidade de Leipzig (https://wortschatz.uni-leipzig.de). Os corpus para download são CC BY; os termos de uso do
projeto restringem o uso comercial a autorização por escrito.
**Pendência antes de vender o Acertô:** obter a autorização ou gerar a tabela de outro corpus com
`AcertoEval data bigramas <frases.txt> data/bigramas.txt` (o gerador aceita qualquer arquivo com uma frase por linha).

## Palavras protegidas (`data/protegidas.txt`)
Lista de palavras do corpus de frequência acima que o corretor ortográfico pt-BR do Windows aceita.
