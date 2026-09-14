# Changelog

Formato: [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/). Versionamento semântico.

Instale sempre com tag (`#v0.2.0`) e atualize trocando a tag no `Packages/manifest.json` — o
Package Manager não mostra botão de update para pacotes vindos de Git.

A compatibilidade com o plugin é dada pelo **major do `schemaVersion`** do contrato, não por
esta versão. Este pacote suporta `schemaVersion` **1.1.x** e recusa major diferente com
mensagem clara, em vez de gerar um prefab silenciosamente errado.

## [0.2.0] — 2026-09-14

Import de componente do kit autorado no Figma, e a correção de quatro caminhos que destruíam
trabalho do dev sem aviso. 72 testes EditMode.

**Para atualizar:** troque `#v0.1.0` por `#v0.2.0` no `Packages/manifest.json`. Nenhum passo de
migração é necessário — telas já importadas continuam funcionando, e o pacote passa a aceitar
também `schemaVersion` 1.1.x. Se o seu projeto tinha dois prefabs reivindicando o mesmo nome
canônico, o import agora **bloqueia** em vez de escolher um: resolva com um override na
`UIMappingTable` antes de subir de versão.

### Adicionado

- **`KitImporter`**: lê um pacote `.uikit` e gera ou atualiza
  `Assets/UI/Generated/Kit/<Nome>.prefab`, com a arte, o tamanho, o layout e a tipografia do
  Figma, mais `UIKitComponent` e os slots ligados por `nodeId`.
- **`KitSkeleton`**: monta o comportamento por papel (`Button`, `Toggle` com os 5 estados por
  `ColorTint`). A pele vem do Figma, o comportamento vem daqui.
- `Image.Type.Sliced` automático quando o sprite tem borda de 9-slice. Sem isso o campo
  `nineSlice` do contrato não tinha efeito nenhum — a borda era configurada e ignorada.
- Relatório de dimensão de textura (`SpriteSizePolicy`, `MultipleOfFour` por padrão).
- A janela aceita `.uiexport` e `.uikit`, roteando pela extensão.
- Adoção explícita: um prefab que a ferramenta não gerou nunca é sobrescrito sem confirmação.

### Corrigido

Quatro bugs no caminho que destrói trabalho do dev, nenhum deles com teste antes:

- `ComponentResolver` varria os prefabs em ordem não especificada, então qual prefab um nome
  canônico resolvia podia variar entre máquinas — e trocar de prefab **recria** as instâncias,
  levando junto os overrides do Variant.
- Ambiguidade de `canonicalName` era aviso e o import seguia com uma escolha arbitrária. Agora
  é erro que bloqueia: não existe valor seguro a devolver ali, porque devolver `null` faz o
  builder destruir todas as instâncias daquele componente.
- O diff era calculado **antes** do resolver rodar, então uma troca de prefab não aparecia na
  única confirmação do sistema. O resolver foi movido para o `Prepare`, e `ImportDiff` passou a
  prever recriação (`Diff.Recreated`).
- `raycastTarget` e a remoção de `Image` eram aplicados sem olhar se havia um `Selectable`
  dependendo deles.

### Limitações conhecidas

- **Dimensão de textura é relatada, não corrigida.** Corrigir exigiria preencher e recortar o
  sprite, e o recorte depende de um pacote que este não quer impor a todo jogo. Preencher sem
  recortar espremeria o desenho — 1% num fundo de 200px, 11% num ícone de 18px.
- **Aparência do prefab do kit é revertida no re-import.** Intencional: sem isso, um botão que
  perdeu a transparência no Figma continuaria transparente para sempre. Componente que a
  ferramenta não gerencia (scripts, `AudioSource`) sobrevive. Para um visual que o Figma não
  dita, aponte um prefab próprio pela `UIMappingTable`.
- **Componentes estruturais** (`Slider`, `ScrollView`, `InputField`, `ProgressBar`, `Tabs`) não
  podem ser autorados no Figma: neles a geometria é o comportamento.

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
