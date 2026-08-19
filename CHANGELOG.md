# Changelog

Formato: [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/). Versionamento semântico.

Instale sempre com tag (`#v0.1.0`) e atualize trocando a tag no `Packages/manifest.json` — o
Package Manager não mostra botão de update para pacotes vindos de Git.

A compatibilidade com o plugin é dada pelo **major do `schemaVersion`** do contrato, não por
esta versão. Este pacote suporta `schemaVersion` **1.0.x** e recusa major diferente com
mensagem clara, em vez de gerar um prefab silenciosamente errado.

## [0.1.0] — 2026-08-19

Primeira versão publicada. 55 testes EditMode passando no Unity 6000.3.

### Adicionado

- Import de `.uiexport` com leitura defensiva do zip: whitelist de entradas, proteção contra
  zip-slip e bomba de descompressão, e validação de assinatura PNG.
- Reconciliação por `FigmaNodeRef`: o prefab base é atualizado reusando os GameObjects, o que
  preserva os `fileID` e, com eles, o trabalho do dev no Prefab Variant.
- Diff antes de gravar, com confirmação quando o import remove objetos.
- `RectSolver`: constraints para âncoras, com a inversão do eixo Y num único lugar.
- `LayoutApplier`: Auto Layout para LayoutGroup, `ContentSizeFitter` e `LayoutElement`.
- `TextApplier`: mapeamento para TextMeshPro, com Font Map por projeto.
- `ComponentResolver`: descobre os prefabs do kit varrendo o projeto; `UIMappingTable` só para
  exceções.
- Gerador do kit placeholder: 15 prefabs canônicos criados por código.
- Relatório de import que também carrega os diagnósticos do lado do designer.
- `Samples~/HomeMenu.uiexport` para testar sem depender de um export real.

### Limitações conhecidas

Ver a tabela em [README.md](README.md#limitações-conhecidas).
