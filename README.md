# com.arvore.uiexporter

Importa pacotes `.uiexport` gerados no
[plugin do Figma](https://github.com/VinniHashirama/ui-exporter-tool-figma-plugin) e monta
interfaces UGUI, **preservando o trabalho do dev entre re-exports do designer**.

Requer **Unity 6000.3+**. Dependências: `com.unity.ugui` (que já traz o TextMeshPro) e
`com.unity.nuget.newtonsoft-json`.

> Ferramenta interna da Arvore. Documentação completa e roadmap em
> [ui-exporter-tool-docs-and-samples](https://github.com/VinniHashirama/ui-exporter-tool-docs-and-samples).

---

## 1. Instalar

**Window → Package Manager → `+` → Add package from git URL**, e cole:

```
https://github.com/VinniHashirama/ui-exporter-tool-unity-package.git#v0.2.0
```

Ou, direto no `Packages/manifest.json` do jogo:

```jsonc
{
  "dependencies": {
    "com.arvore.uiexporter": "https://github.com/VinniHashirama/ui-exporter-tool-unity-package.git#v0.2.0"
  }
}
```

**Sempre instale com a tag** (`#v0.2.0`). Sem ela o Package Manager fixa o commit que estava no
`main` no momento da instalação, e a partir daí atualizar dá trabalho — veja a seção seguinte.

Se o projeto for novo, importe também **Window → TextMeshPro → Import TMP Essential Resources**.
Sem isso não existe fonte default e nenhum texto renderiza.

## 2. Atualizar

O Package Manager **não mostra botão de update para pacotes vindos de Git** — não é bug, é como
ele funciona. A atualização é explícita:

1. Veja o que mudou no [CHANGELOG.md](CHANGELOG.md).
2. Troque a tag em `Packages/manifest.json`: `#v0.1.0` → `#v0.2.0`.
3. Volte para a Unity. O Package Manager resolve a nova versão ao recuperar o foco.

Ser explícito é proposital: são vários jogos com cronogramas diferentes, e cada um decide quando
subir de versão — em vez de qualquer commit novo no `main` entrar sozinho no meio de uma sprint.

Se você instalou sem tag e quer voltar a fixar uma, troque a URL no `manifest.json` e apague a
entrada de `com.arvore.uiexporter` do `Packages/packages-lock.json`.

## 3. Primeiro uso

1. **Window → Arvore → UI Exporter → Gerar kit placeholder.** Cria os 15 prefabs canônicos em
   `Assets/UI/Generated/Kit`. Sem kit, toda instância de componente vira caixa vazia.
2. **Assets → Create → Arvore → UI Exporter → Import Settings.** Aponte `Rounded Sprite` para o
   sprite gerado em `Kit/Sprites` e — importante em projeto grande — restrinja
   `Kit Search Folders` à pasta do kit, senão o importador varre todos os prefabs do projeto a
   cada import.
3. **Assets → Create → Arvore → UI Exporter → Font Map.** Mapeie cada família e estilo que o
   time usa no Figma para o `TMP_FontAsset` correspondente. Sem isso todo texto sai na fonte
   default, e o relatório avisa em cada import.

Quer testar antes de ter um arquivo do designer? O pacote traz um exemplo em
`Samples~/HomeMenu.uiexport`, que você seleciona direto no seletor de arquivo da janela.

## 4. Importar uma tela

1. **Window → Arvore → UI Exporter → `Escolher...`** e selecione o `.uiexport`.
2. **Confira o diff.** A janela mostra o que vai ser criado, preservado e **removido**. Nada é
   escrito antes de você confirmar.
3. **Importar.** Se houver remoções, aparece uma confirmação extra — remoção é a única operação
   que destrói trabalho.
4. **Leia o relatório.** Componente não mapeado, fonte faltando e aproximação de layout são casos
   em que o import teve sucesso mas o resultado não é o que o designer desenhou.

### Quem é dono de quê

```
Assets/UI/Generated/<Tela>/<Tela>_Base.prefab   ← DA FERRAMENTA. Sobrescrito a cada import.
Assets/UI/Generated/<Tela>/Sprites/             ← DA FERRAMENTA.
Assets/UI/Screens/<Tela>.prefab                 ← DO DEV. Prefab Variant. Nunca tocado.
```

Trabalhe **sempre** no Variant. Scripts, animações, áudio e wiring vão lá.

O motivo é técnico, não estilístico: um Prefab Variant rastreia seus overrides pelo `fileID`
local de cada objeto no prefab base. O importador nunca recria os GameObjects do base — ele
reencontra cada node pelo `FigmaNodeRef` e reusa o objeto existente, o que preserva os `fileID`
e, com eles, tudo que você pendurou no Variant. Editar o `_Base` na mão é trabalho que será
perdido no próximo import.

Consequência para o designer: **renomear uma layer é seguro; deletar e recriar não é.** Uma layer
recriada tem id novo, então para a ferramenta é outro objeto — o antigo é removido, e o que
estava pendurado nele vai junto. O diff sempre mostra isso antes.

## 5. Atualizar o kit inteiro de uma vez

O plugin do Figma também exporta **todo componente da página num arquivo só**, `.uikitset`, em
vez de um `.uikit` por componente — útil depois de uma leva de ajustes visuais no kit inteiro.

1. **Window → Arvore → UI Exporter → `Escolher...`** e selecione o `.uikitset` (a mesma janela
   de sempre; a extensão é o que roteia para o import em lote, igual já acontece entre
   `.uiexport` e `.uikit`).
2. A janela lista cada componente do lote com o status dele: pronto, bloqueado, ou precisando de
   adoção (prefab existente que a ferramenta não gerou).
3. **`Importar todos os prontos`** importa de uma vez todo componente sem pendência. Um
   componente bloqueado ou pendente de adoção **não trava os outros** — ele fica de fora do lote
   e aparece no relatório, para você resolver sozinho pelo fluxo de `.uikit` de sempre.

Cada componente do lote passa pela mesma validação e o mesmo diff do import avulso — o lote só
evita reabrir a janela e reescolher arquivo catorze vezes.

## 6. Acessar a tela por código

Toda layer marcada com `@Nome` no Figma entra no `UIViewRefs` da raiz:

```csharp
var view = screen.GetComponent<UIViewRefs>();

view.Get<Button>("PlayButton").onClick.AddListener(StartGame);
view.Get<TMP_Text>("CoinLabel").text = coins.ToString();

// Get<T> lança com mensagem clara se o bind não existe; TryGet para o caminho tolerante.
if (view.TryGet<Slider>("Volume", out var slider)) { /* ... */ }
```

`view.DesignResolution` traz a resolução em que a tela foi desenhada — use para configurar o
`CanvasScaler`. O prefab gerado **não** traz Canvas próprio: em jogo as telas normalmente vivem
sob um Canvas compartilhado, e um Canvas por tela criaria batches separados e conflito de
ordenação.

## Limitações conhecidas

| Item | Estado |
|---|---|
| `SPACE_BETWEEN` | Aproximado com `childForceExpand`; reportado. O espaço vai para dentro dos itens, não entre eles |
| `WRAP` | Vira `GridLayoutGroup` com células iguais ao primeiro item; reportado |
| Estados de variante (`State=Disabled`) | Não aplicados — os estados vêm do prefab do kit; reportado |
| Entressenha e entreletra | Fórmulas derivadas do `faceInfo` da fonte; precisam de calibração visual |
| 9-slice automático | Não detectado; o campo existe no contrato mas só por anotação explícita |
| Gradientes, sombras, blur | Achatados em PNG pelo designer via `#img` |
| `SpriteAtlas` | Não gerado |
| View tipada por codegen | Não; o binding é `UIViewRefs.Get<T>(key)` |

## Desenvolvimento

```bash
bash tools~/unity-test.sh                 # compila e roda os 84 testes EditMode
bash tools~/unity-test.sh --compile-only  # só compila
```

O script cria um projeto Unity descartável **fora do repositório** na primeira execução
(`../ui-exporter-test-project`, ou o que estiver em `UNITY_TEST_PROJECT`) e reusa nas seguintes.
Nada de projeto Unity versionado: o repositório entrega o pacote, e um projeto commitado só
inflaria o clone e desatualizaria.

Não há CI: runner de Unity exige licença ativada, o que é custo e complexidade desproporcionais
aqui. **Rode os testes localmente antes de criar uma tag.**

As pastas `Samples~` e `tools~` terminam com til de propósito: a Unity não importa pasta com esse
sufixo, então elas não geram `.meta` nem entram no projeto de quem instala o pacote.

O teste que decide se a ferramenta cumpre a promessa é
`ScreenImportTests.Reimport_PreservesDevWorkInTheVariant`: o dev adiciona um componente e um
override no Variant, o designer renomeia e move uma layer, re-importa, e o trabalho do dev
continua lá.
