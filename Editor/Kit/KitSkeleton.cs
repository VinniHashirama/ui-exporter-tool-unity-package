using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Arvore.UIExporter.Editor
{
    /// <summary>
    /// Dá comportamento ao prefab do kit a partir do papel declarado no pacote.
    /// </summary>
    /// <remarks>
    /// A divisão de trabalho é o ponto todo desta classe: o Figma entrega a <b>pele</b>
    /// (sprite, cor, tamanho, layout, tipografia) e a Unity entrega o <b>comportamento</b>
    /// (clique, estados, navegação por gamepad). A forma no Figma não distingue um botão de
    /// um rótulo — um retângulo com texto dentro pode ser os dois —, então o papel é dito
    /// explicitamente no contrato em vez de adivinhado aqui.
    /// <para>
    /// Roda depois da reconciliação, porque precisa apontar para o <see cref="Graphic"/> que
    /// ela acabou de produzir. E converge em vez de somar: um componente que deixou de ser
    /// botão perde o <see cref="Button"/>, senão carregaria resíduo do import anterior.
    /// </para>
    /// </remarks>
    internal static class KitSkeleton
    {
        public static void Apply(GameObject root, KitRole role, ImportReport report)
        {
            switch (role)
            {
                case KitRole.Button:
                    ApplySelectable<Button>(root, report);
                    break;

                case KitRole.Toggle:
                    ApplyToggle(root, report);
                    break;

                default:
                    // Container, Display, Icon e Image não têm comportamento próprio: são
                    // superfície e conteúdo. Remover garante que trocar o papel de um
                    // componente já importado não deixe o comportamento antigo para trás.
                    Strip(root);
                    break;
            }
        }

        private static void ApplySelectable<T>(GameObject root, ImportReport report)
            where T : Selectable
        {
            Graphic target = ResolveTargetGraphic(root, report);

            if (target == null)
            {
                // Sem Graphic não há área clicável nem alvo de tint. Adicionar o Button assim
                // mesmo produziria um botão que existe e não funciona — pior que não ter.
                Strip(root);
                return;
            }

            var selectable = ComponentUtil.Ensure<T>(root);

            UIKitFactory.SetupStates(selectable, target, target.color);
        }

        private static void ApplyToggle(GameObject root, ImportReport report)
        {
            Graphic target = ResolveTargetGraphic(root, report);

            if (target == null)
            {
                Strip(root);
                return;
            }

            ComponentUtil.Remove<Button>(root);

            var toggle = ComponentUtil.Ensure<Toggle>(root);
            UIKitFactory.SetupStates(toggle, target, target.color);

            // `graphic` é a marca de "ligado". Sem slot `checkmark` o Toggle funciona, mas não
            // mostra o estado — vale relatar em vez de deixar o designer descobrir em runtime.
            if (root.TryGetComponent(out UIKitComponent kit) &&
                kit.TryGetSlot("checkmark", out Transform checkmark) &&
                checkmark.TryGetComponent(out Graphic mark))
            {
                toggle.graphic = mark;
            }
            else
            {
                report.Info(
                    "kit/no-checkmark",
                    $"'{root.name}' é um toggle sem slot 'checkmark', então nada muda " +
                    "visualmente quando ele liga. Marque a layer da marca com '$checkmark'.",
                    root.name);
            }
        }

        /// <summary>
        /// Acha o <see cref="Graphic"/> que serve de área clicável e de alvo do tint.
        /// </summary>
        /// <remarks>
        /// A raiz vem primeiro, mas não é o caso mais comum: no Figma o arranjo natural é a
        /// raiz ser o frame de Auto Layout e a arte viver numa layer filha (`bg`). Por isso a
        /// busca desce um nível e pega o maior fundo — exigir o fill na raiz reprovaria a
        /// maioria dos botões desenhados por gente de verdade.
        /// <para>
        /// Quem chama liga o <c>raycastTarget</c> do que for devolvido; a reconciliação o
        /// deixa desligado por padrão, porque fundo decorativo não deve interceptar clique.
        /// </para>
        /// </remarks>
        private static Graphic ResolveTargetGraphic(GameObject root, ImportReport report)
        {
            if (root.TryGetComponent(out Image rootImage))
            {
                return rootImage;
            }

            // Texto puro também serve de alvo: um botão só de texto é legítimo.
            if (root.TryGetComponent(out TMP_Text rootText))
            {
                return rootText;
            }

            Graphic background = FindLargestChildImage(root);

            if (background != null)
            {
                return background;
            }

            report.Warn(
                "kit/no-graphic",
                $"'{root.name}' foi exportado como interativo mas não tem nenhum fundo, então " +
                "não há área clicável nem onde aplicar o tint dos estados. Dê um fill à raiz do " +
                "componente no Figma, ou marque a layer de fundo com '#img'.",
                root.name);

            return null;
        }

        /// <summary>
        /// Maior <see cref="Image"/> entre os filhos diretos — o fundo, na prática.
        /// </summary>
        /// <remarks>
        /// Por área, e não pelo primeiro da hierarquia: a ordem das layers no Figma é decisão
        /// de desenho do designer, e escolher por ela faria o alvo do tint mudar quando alguém
        /// reordenasse as camadas.
        /// </remarks>
        private static Graphic FindLargestChildImage(GameObject root)
        {
            Graphic best = null;
            float bestArea = 0f;

            foreach (Transform child in root.transform)
            {
                if (!child.TryGetComponent(out Image image)) continue;
                if (!(child is RectTransform rect)) continue;

                float area = Mathf.Abs(rect.rect.width * rect.rect.height);

                if (area > bestArea)
                {
                    bestArea = area;
                    best = image;
                }
            }

            return best;
        }

        private static void Strip(GameObject root)
        {
            ComponentUtil.Remove<Button>(root);
            ComponentUtil.Remove<Toggle>(root);
        }
    }
}
