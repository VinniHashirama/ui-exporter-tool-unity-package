using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Arvore.UIExporter.Editor
{
    /// <summary>
    /// Monta e atualiza o prefab base de uma tela.
    /// </summary>
    /// <remarks>
    /// <b>A regra que define este arquivo: nunca recriar, sempre reconciliar.</b>
    /// <para>
    /// Um Prefab Variant rastreia seus overrides pelo <c>fileID</c> local de cada objeto no
    /// prefab base. Destruir e recriar os GameObjects do base mudaria todos os
    /// <c>fileID</c>, e os overrides do Variant — os scripts, as referências e os ajustes do
    /// dev — virariam órfãos em silêncio, sem um único erro no console. Por isso cada node
    /// do IR é reencontrado pelo <see cref="FigmaNodeRef"/> e o GameObject existente é
    /// <i>reusado</i>.
    /// </para>
    /// <para>
    /// Reusar tem um custo: não basta adicionar o que o design pede, é preciso remover o que
    /// ele não pede mais. Um node que era frame e virou texto precisa perder o Image e ganhar
    /// o TMP; um container que perdeu o Auto Layout precisa perder o LayoutGroup. Cada
    /// aplicador aqui converge o objeto para o estado descrito, em vez de só somar.
    /// </para>
    /// </remarks>
    public sealed class PrefabBuilder
    {
        private const string DefaultStateValue = "Default";

        private static readonly string[] LabelPropertyNames = { "label", "text", "title", "caption" };

        private readonly UIImportSettings settings;
        private readonly ComponentResolver resolver;
        private readonly SpriteImporter.Result sprites;
        private readonly ImportReport report;

        private readonly Dictionary<string, GameObject> existingByNodeId =
            new Dictionary<string, GameObject>(StringComparer.Ordinal);

        private readonly HashSet<string> visitedNodeIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<UIViewRef> binds = new List<UIViewRef>();
        private readonly HashSet<string> bindKeys = new HashSet<string>(StringComparer.Ordinal);

        public PrefabBuilder(
            UIImportSettings settings,
            ComponentResolver resolver,
            SpriteImporter.Result sprites,
            ImportReport report)
        {
            this.settings = settings;
            this.resolver = resolver;
            this.sprites = sprites;
            this.report = report;
        }

        /// <summary>Cria ou atualiza o prefab base e devolve o caminho gravado.</summary>
        public string BuildOrUpdate(IRDocument document, string basePrefabPath)
        {
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath) != null;
            GameObject root = exists
                ? PrefabUtility.LoadPrefabContents(basePrefabPath)
                : new GameObject(document.Root.Name, typeof(RectTransform));

            try
            {
                if (exists)
                {
                    IndexExisting(root);
                }

                var canvasSize = new Vector2(document.Canvas.Width, document.Canvas.Height);

                Reconcile(
                    document.Root,
                    parent: null,
                    existingRoot: root,
                    parentSize: canvasSize,
                    parentHasLayout: false,
                    siblingIndex: 0);

                RemoveOrphans();

                UIViewRefs refs = ComponentUtil.Ensure<UIViewRefs>(root);
                refs.SetRefs(binds, canvasSize);

                PrefabUtility.SaveAsPrefabAsset(root, basePrefabPath);
                return basePrefabPath;
            }
            finally
            {
                if (exists)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        /// <summary>
        /// Cria o Prefab Variant do dev, se ainda não existir.
        /// </summary>
        /// <remarks>
        /// Criado uma vez e nunca mais tocado: é território do dev. É aqui que scripts,
        /// animações e wiring devem morar, e é o que sobrevive a todo re-export do designer.
        /// </remarks>
        public static bool EnsureVariant(string basePrefabPath, string variantPath, ImportReport report)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(variantPath) != null)
            {
                return false;
            }

            var baseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath);
            if (baseAsset == null)
            {
                return false;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(baseAsset);

            try
            {
                PrefabUtility.SaveAsPrefabAsset(instance, variantPath, out bool success);

                if (success)
                {
                    report.Info(
                        "variant/created",
                        $"Criei o Prefab Variant em '{variantPath}'. Trabalhe nele — scripts e " +
                        "ajustes aqui sobrevivem aos próximos imports. Não edite o _Base.");
                    return true;
                }

                report.Warn("variant/failed", $"Não consegui criar o Variant em '{variantPath}'.");
                return false;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private void IndexExisting(GameObject root)
        {
            foreach (FigmaNodeRef reference in root.GetComponentsInChildren<FigmaNodeRef>(includeInactive: true))
            {
                string id = reference.NodeId;
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                // Id repetido não deveria acontecer; se acontecer, o primeiro vence e o
                // segundo será tratado como órfão — melhor que escolher em silêncio.
                if (!existingByNodeId.ContainsKey(id))
                {
                    existingByNodeId.Add(id, reference.gameObject);
                }
            }
        }

        private void Reconcile(
            IRNode node,
            Transform parent,
            GameObject existingRoot,
            Vector2 parentSize,
            bool parentHasLayout,
            int siblingIndex)
        {
            bool isRoot = parent == null;
            GameObject kitPrefab = ResolveKitPrefab(node);

            GameObject target = isRoot
                ? existingRoot
                : Acquire(node, parent, kitPrefab);

            visitedNodeIds.Add(node.Id);

            target.name = SafeName(node.Name);

            FigmaNodeRef reference = ComponentUtil.Ensure<FigmaNodeRef>(target);
            reference.Assign(node.Id, node.Name);

            var rect = (RectTransform)target.transform;

            if (!isRoot)
            {
                rect.SetParent(parent, worldPositionStays: false);
                rect.SetSiblingIndex(siblingIndex);
            }

            ApplyTransform(rect, node, parentSize, isRoot, parentHasLayout);
            ApplyOpacity(target, node);
            target.SetActive(node.Visible);

            LayoutApplier.ApplyChild(target, node);

            ApplyKind(target, node, kitPrefab);

            CollectBind(target, node);

            bool hasLayout = node.Layout != null;
            var selfSize = new Vector2(node.Rect.Width, node.Rect.Height);

            // Instância do kit é caixa fechada: o interior dela pertence ao prefab, e o
            // export nem manda os filhos.
            if (node.Kind == NodeKind.Instance && kitPrefab != null)
            {
                return;
            }

            if (node.Children == null)
            {
                return;
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                Reconcile(
                    node.Children[i],
                    rect,
                    existingRoot: null,
                    parentSize: selfSize,
                    parentHasLayout: hasLayout,
                    siblingIndex: i);
            }
        }

        /// <summary>Reusa o GameObject do import anterior, ou cria um novo.</summary>
        private GameObject Acquire(IRNode node, Transform parent, GameObject kitPrefab)
        {
            if (existingByNodeId.TryGetValue(node.Id, out GameObject existing) && existing != null)
            {
                if (IsShapeCompatible(existing, kitPrefab))
                {
                    return existing;
                }

                // Trocou de prefab (ou deixou de ser instância): não há como convergir por
                // mutação, o objeto tem que ser refeito — e isso custa os overrides que o
                // dev tinha pendurado nele, então precisa aparecer no report.
                report.Warn(
                    "reconcile/recreated",
                    $"'{node.Name}' mudou de tipo de componente e foi recriado. Ajustes do " +
                    "dev que estavam nesse objeto específico foram perdidos.",
                    node.Name);

                existingByNodeId.Remove(node.Id);
                UnityEngine.Object.DestroyImmediate(existing);
            }

            if (kitPrefab != null)
            {
                var created = (GameObject)PrefabUtility.InstantiatePrefab(kitPrefab, parent);
                return created;
            }

            var plain = new GameObject(SafeName(node.Name), typeof(RectTransform));
            plain.transform.SetParent(parent, worldPositionStays: false);
            return plain;
        }

        /// <summary>
        /// Um objeto só pode ser reusado se continuar sendo a mesma <i>espécie</i> de coisa:
        /// instância do mesmo prefab do kit, ou objeto comum como antes.
        /// </summary>
        private static bool IsShapeCompatible(GameObject candidate, GameObject kitPrefab)
        {
            bool isInstanceRoot = PrefabUtility.IsAnyPrefabInstanceRoot(candidate);

            if (kitPrefab == null)
            {
                return !isInstanceRoot;
            }

            if (!isInstanceRoot)
            {
                return false;
            }

            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(candidate);
            if (source == null)
            {
                return false;
            }

            string sourcePath = AssetDatabase.GetAssetPath(source);
            string expectedPath = AssetDatabase.GetAssetPath(kitPrefab);

            return !string.IsNullOrEmpty(sourcePath)
                && string.Equals(sourcePath, expectedPath, StringComparison.Ordinal);
        }

        private GameObject ResolveKitPrefab(IRNode node)
        {
            if (node.Kind != NodeKind.Instance || node.Component == null)
            {
                return null;
            }

            string canonical = node.Component.CanonicalName;
            GameObject prefab = resolver.Resolve(canonical);

            if (prefab != null)
            {
                return prefab;
            }

            string suggestion = resolver.SuggestSimilar(canonical);

            report.Warn(
                "kit/unresolved",
                $"Nenhum prefab do kit para '{canonical}'. O node virou uma caixa vazia, " +
                "sem comportamento." +
                (suggestion != null
                    ? $" O projeto tem '{suggestion}' — confira se é diferença de caixa ou de nome."
                    : " Crie o prefab com UIKitComponent e esse nome canônico."),
                node.Name);

            return null;
        }

        private void ApplyTransform(
            RectTransform rect,
            IRNode node,
            Vector2 parentSize,
            bool isRoot,
            bool parentHasLayout)
        {
            rect.localScale = Vector3.one;
            rect.localRotation = Mathf.Abs(node.Rotation) > 0.01f
                ? Quaternion.Euler(0f, 0f, node.Rotation)
                : Quaternion.identity;

            if (isRoot)
            {
                // A raiz preenche o pai onde for instanciada: é o CanvasScaler que resolve
                // a escala, e a resolução de design fica registrada no UIViewRefs.
                RectSolution.FullStretch.ApplyTo(rect);
                rect.pivot = new Vector2(0.5f, 0.5f);
                return;
            }

            if (parentHasLayout)
            {
                // Quem posiciona é o LayoutGroup. Escrever posição aqui criaria disputa
                // entre os dois; o tamanho serve só como valor inicial sensato para o
                // primeiro cálculo de layout.
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(node.Rect.Width, node.Rect.Height);
                return;
            }

            rect.pivot = new Vector2(0.5f, 0.5f);
            RectSolver.Solve(node.Rect, parentSize, node.Constraints).ApplyTo(rect);
        }

        private static void ApplyOpacity(GameObject target, IRNode node)
        {
            if (node.Opacity >= 0.999f)
            {
                ComponentUtil.Remove<CanvasGroup>(target);
                return;
            }

            ComponentUtil.Ensure<CanvasGroup>(target).alpha = node.Opacity;
        }

        private void ApplyKind(GameObject target, IRNode node, GameObject kitPrefab)
        {
            switch (node.Kind)
            {
                case NodeKind.Text:
                    LayoutApplier.Apply(target, node, report);
                    ApplyClip(target, node);
                    TextApplier.Apply(target, node, settings, report);
                    break;

                case NodeKind.Image:
                    ComponentUtil.Remove<TextMeshProUGUI>(target);
                    LayoutApplier.Apply(target, node, report);
                    ApplyClip(target, node);
                    ApplyImageFill(target, node);
                    break;

                case NodeKind.Instance:
                    if (kitPrefab != null)
                    {
                        ApplyComponentSlots(target, node);
                    }
                    else
                    {
                        // Sem prefab resolvido o node degrada para container vazio; já
                        // reportado em ResolveKitPrefab.
                        ComponentUtil.Remove<TextMeshProUGUI>(target);
                        LayoutApplier.Apply(target, node, report);
                    }

                    break;

                default:
                    ComponentUtil.Remove<TextMeshProUGUI>(target);
                    LayoutApplier.Apply(target, node, report);
                    ApplyClip(target, node);
                    ApplySurfaceFill(target, node);
                    break;
            }
        }

        private static void ApplyClip(GameObject target, IRNode node)
        {
            if (node.Clip)
            {
                ComponentUtil.Ensure<RectMask2D>(target);
            }
            else
            {
                ComponentUtil.Remove<RectMask2D>(target);
            }
        }

        /// <summary>Fundo de frame/group: cor chapada, com sprite só quando há canto arredondado.</summary>
        private void ApplySurfaceFill(GameObject target, IRNode node)
        {
            IRFill fill = node.Fill;

            if (fill == null)
            {
                ComponentUtil.Remove<Image>(target);
                return;
            }

            if (fill.Type == FillType.Image)
            {
                ApplyImageFill(target, node);
                return;
            }

            Image image = ComponentUtil.Ensure<Image>(target);
            image.color = ComponentUtil.ParseColor(fill.Color, Color.white);
            image.raycastTarget = false;

            bool rounded = HasCornerRadius(node);

            if (rounded && settings.RoundedSprite != null)
            {
                image.sprite = settings.RoundedSprite;
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = ResolveCornerMultiplier(node, settings.RoundedSprite);
            }
            else
            {
                image.sprite = null;
                image.type = Image.Type.Simple;

                if (rounded)
                {
                    report.Info(
                        "fill/corner-radius",
                        "Canto arredondado precisa de um sprite 9-slice: configure " +
                        "'Rounded Sprite' nas Import Settings, ou o fundo sai quadrado.",
                        node.Name);
                }
            }
        }

        private void ApplyImageFill(GameObject target, IRNode node)
        {
            Image image = ComponentUtil.Ensure<Image>(target);
            image.color = Color.white;
            image.raycastTarget = false;

            Sprite sprite = sprites.Resolve(node.Fill?.AssetId);

            if (sprite == null)
            {
                image.sprite = null;
                report.Warn(
                    "fill/sprite-missing",
                    $"O sprite '{node.Fill?.AssetId}' não foi importado; o node ficou sem imagem.",
                    node.Name);
                return;
            }

            image.sprite = sprite;

            switch (node.Fill.ScaleMode)
            {
                case ImageScaleMode.Tile:
                    image.type = Image.Type.Tiled;
                    image.preserveAspect = false;
                    break;

                case ImageScaleMode.Fit:
                    image.type = Image.Type.Simple;
                    image.preserveAspect = true;
                    break;

                case ImageScaleMode.Fill:
                    // "Fill" recorta o excedente, o que em UGUI exigiria uma máscara. Sem
                    // ela a aproximação é esticar, e o report diz que houve aproximação.
                    image.type = Image.Type.Simple;
                    image.preserveAspect = false;
                    report.Info(
                        "fill/crop",
                        "Modo 'fill' recorta a imagem no design; aqui ela foi esticada. " +
                        "Marque a layer com '#img' no Figma para sair exata.",
                        node.Name);
                    break;

                default:
                    image.type = Image.Type.Simple;
                    image.preserveAspect = false;
                    break;
            }
        }

        private static bool HasCornerRadius(IRNode node)
        {
            if (node.CornerRadius == null)
            {
                return false;
            }

            foreach (float value in node.CornerRadius)
            {
                if (value > 0.01f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Ajusta a escala do 9-slice para o raio bater com o do design.
        /// </summary>
        /// <remarks>
        /// O sprite arredondado tem um raio próprio; <c>pixelsPerUnitMultiplier</c> é o
        /// único jeito de reaproveitar o mesmo sprite para raios diferentes sem gerar uma
        /// textura por raio.
        /// </remarks>
        private static float ResolveCornerMultiplier(IRNode node, Sprite sprite)
        {
            float designRadius = 0f;
            foreach (float value in node.CornerRadius)
            {
                designRadius = Mathf.Max(designRadius, value);
            }

            if (designRadius <= 0.01f)
            {
                return 1f;
            }

            // O raio do sprite é aproximado pela menor borda declarada nele.
            Vector4 border = sprite.border;
            float spriteRadius = Mathf.Max(
                1f,
                Mathf.Min(
                    Mathf.Min(border.x, border.y),
                    Mathf.Min(border.z, border.w)));

            return Mathf.Clamp(spriteRadius / designRadius, 0.01f, 100f);
        }

        private void ApplyComponentSlots(GameObject target, IRNode node)
        {
            if (!target.TryGetComponent(out UIKitComponent kit))
            {
                report.Warn(
                    "kit/no-slots",
                    $"O prefab de '{node.Component.CanonicalName}' perdeu o UIKitComponent, " +
                    "então nenhum slot pode ser preenchido.",
                    node.Name);
                return;
            }

            Dictionary<string, object> properties = node.Component.Properties;
            if (properties == null)
            {
                return;
            }

            if (TryGetLabel(properties, out string label) &&
                kit.TryGetSlot("label", out Transform labelSlot) &&
                labelSlot.TryGetComponent(out TMP_Text labelText))
            {
                labelText.text = label;
            }

            WarnAboutUnappliedState(node, properties);
        }

        /// <summary>
        /// Variante diferente da default é uma diferença visual real que o MVP não aplica:
        /// os estados vêm do prefab do kit. Vale reportar, não vale falhar.
        /// </summary>
        private void WarnAboutUnappliedState(IRNode node, Dictionary<string, object> properties)
        {
            foreach (KeyValuePair<string, object> entry in properties)
            {
                if (!string.Equals(entry.Key, "State", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string value = entry.Value?.ToString();
                if (string.IsNullOrEmpty(value) ||
                    string.Equals(value, DefaultStateValue, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "Normal", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                report.Warn(
                    "component/state-ignored",
                    $"A variante State='{value}' não é aplicada: no MVP os estados vêm do " +
                    "prefab do kit. O componente foi montado no estado default.",
                    node.Name);
                return;
            }
        }

        private static bool TryGetLabel(Dictionary<string, object> properties, out string label)
        {
            foreach (string candidate in LabelPropertyNames)
            {
                foreach (KeyValuePair<string, object> entry in properties)
                {
                    if (string.Equals(entry.Key, candidate, StringComparison.OrdinalIgnoreCase) &&
                        entry.Value != null)
                    {
                        label = entry.Value.ToString();
                        return true;
                    }
                }
            }

            label = null;
            return false;
        }

        private void CollectBind(GameObject target, IRNode node)
        {
            if (string.IsNullOrEmpty(node.Bind))
            {
                return;
            }

            if (!bindKeys.Add(node.Bind))
            {
                report.Warn(
                    "bind/duplicate",
                    $"O bind '@{node.Bind}' aparece mais de uma vez; só o primeiro entra no " +
                    "UIViewRefs.",
                    node.Name);
                return;
            }

            binds.Add(new UIViewRef { key = node.Bind, target = target });
        }

        private void RemoveOrphans()
        {
            foreach (KeyValuePair<string, GameObject> entry in existingByNodeId)
            {
                if (visitedNodeIds.Contains(entry.Key) || entry.Value == null)
                {
                    continue;
                }

                report.Warn(
                    "reconcile/removed",
                    $"'{entry.Value.name}' não existe mais no design e foi removido do prefab " +
                    "base. Se o dev tinha algo pendurado nesse objeto, foi junto.",
                    entry.Value.name);

                UnityEngine.Object.DestroyImmediate(entry.Value);
            }
        }

        /// <summary>
        /// Nome de layer é conteúdo de designer: chega aqui já sanitizado pelo plugin, mas
        /// o importador não confia nisso — o pacote pode ter sido gerado por outra coisa.
        /// </summary>
        private static string SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Node";
            }

            var builder = new System.Text.StringBuilder(name.Length);

            foreach (char c in name)
            {
                builder.Append(char.IsControl(c) ? ' ' : c);
            }

            string cleaned = builder.ToString().Trim();
            return cleaned.Length > 0 ? cleaned : "Node";
        }
    }
}
