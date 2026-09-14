using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Arvore.UIExporter.Editor
{
    /// <summary>
    /// O que este import vai mudar no prefab base, calculado <b>antes</b> de escrever nada.
    /// </summary>
    /// <remarks>
    /// Existe por uma razão: remoção é a única operação do import que destrói trabalho. Um
    /// node que desapareceu do design leva embora o GameObject e, com ele, qualquer coisa que
    /// o dev tivesse pendurado nesse objeto específico. Mostrar isso antes, e pedir
    /// confirmação, transforma uma perda silenciosa numa decisão consciente.
    /// </remarks>
    public sealed class ImportDiff
    {
        private ImportDiff(
            bool isNewScreen,
            List<string> added,
            List<string> removed,
            List<string> recreated,
            int kept)
        {
            IsNewScreen = isNewScreen;
            Added = added;
            Removed = removed;
            Recreated = recreated;
            Kept = kept;
        }

        public bool IsNewScreen { get; }

        /// <summary>Nomes dos nodes que aparecem pela primeira vez.</summary>
        public List<string> Added { get; }

        /// <summary>Nomes dos objetos que serão destruídos.</summary>
        public List<string> Removed { get; }

        /// <summary>
        /// Nomes dos objetos que serão destruídos e refeitos por terem trocado de prefab do
        /// kit — mesmo continuando no design.
        /// </summary>
        /// <remarks>
        /// Some do radar sem isto: o node continua no IR, então a comparação de ids não acusa
        /// nada, mas <see cref="PrefabBuilder"/> recria o objeto porque a <i>espécie</i> mudou.
        /// O <c>fileID</c> novo leva junto os overrides do Variant, e o dev só descobre depois.
        /// </remarks>
        public List<string> Recreated { get; }

        public int Kept { get; }

        /// <summary>Se true, vale pedir confirmação antes de gravar.</summary>
        public bool IsDestructive => Removed.Count > 0 || Recreated.Count > 0;

        /// <param name="resolver">
        /// Quando informado, o diff também prevê recriação por troca de prefab do kit. Sem ele
        /// a análise fica limitada à comparação de ids.
        /// </param>
        public static ImportDiff Compute(
            IRDocument document,
            string basePrefabPath,
            ComponentResolver resolver = null)
        {
            var incoming = new Dictionary<string, IRNode>(StringComparer.Ordinal);
            Collect(document.Root, incoming);

            var baseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath);

            if (baseAsset == null)
            {
                var allNames = new List<string>(incoming.Count);
                foreach (IRNode node in incoming.Values)
                {
                    allNames.Add(node.Name);
                }

                return new ImportDiff(
                    isNewScreen: true,
                    added: allNames,
                    removed: new List<string>(),
                    recreated: new List<string>(),
                    kept: 0);
            }

            var existing = new Dictionary<string, GameObject>(StringComparer.Ordinal);

            foreach (FigmaNodeRef reference in
                baseAsset.GetComponentsInChildren<FigmaNodeRef>(includeInactive: true))
            {
                if (!string.IsNullOrEmpty(reference.NodeId) &&
                    !existing.ContainsKey(reference.NodeId))
                {
                    existing.Add(reference.NodeId, reference.gameObject);
                }
            }

            var added = new List<string>();
            var removed = new List<string>();
            var recreated = new List<string>();
            int kept = 0;

            foreach (KeyValuePair<string, IRNode> entry in incoming)
            {
                if (!existing.TryGetValue(entry.Key, out GameObject current))
                {
                    added.Add(entry.Value.Name);
                    continue;
                }

                kept++;

                // A raiz nunca passa por Acquire: ela é o próprio prefab base sendo
                // reaproveitado, então não há recriação possível ali.
                bool isRoot = string.Equals(entry.Key, document.Root?.Id, StringComparison.Ordinal);

                if (!isRoot && resolver != null && WillBeRecreated(entry.Value, current, resolver))
                {
                    recreated.Add(entry.Value.Name);
                }
            }

            foreach (KeyValuePair<string, GameObject> entry in existing)
            {
                if (!incoming.ContainsKey(entry.Key))
                {
                    removed.Add(entry.Value.name);
                }
            }

            return new ImportDiff(isNewScreen: false, added, removed, recreated, kept);
        }

        public string Summary()
        {
            if (IsNewScreen)
            {
                return $"Tela nova: {Added.Count} objetos serão criados.";
            }

            string summary = $"{Added.Count} novos, {Kept} preservados, {Removed.Count} removidos.";

            if (Recreated.Count > 0)
            {
                summary += $" {Recreated.Count} serão recriados por troca de prefab do kit.";
            }

            return summary;
        }

        /// <summary>
        /// Pergunta ao <see cref="PrefabBuilder"/> a mesma decisão que ele tomaria, em vez de
        /// reimplementá-la aqui — duas cópias da regra divergiriam, e a divergência apareceria
        /// como um diff que prometeu preservar o que foi perdido.
        /// </summary>
        private static bool WillBeRecreated(IRNode node, GameObject current, ComponentResolver resolver)
        {
            GameObject kitPrefab = node.Kind == NodeKind.Instance && node.Component != null
                ? resolver.Resolve(node.Component.CanonicalName)
                : null;

            return !PrefabBuilder.IsShapeCompatible(current, kitPrefab);
        }

        private static void Collect(IRNode node, Dictionary<string, IRNode> into)
        {
            if (!string.IsNullOrEmpty(node.Id))
            {
                into[node.Id] = node;
            }

            if (node.Children == null)
            {
                return;
            }

            foreach (IRNode child in node.Children)
            {
                Collect(child, into);
            }
        }
    }
}
