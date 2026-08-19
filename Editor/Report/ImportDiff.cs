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
        private ImportDiff(bool isNewScreen, List<string> added, List<string> removed, int kept)
        {
            IsNewScreen = isNewScreen;
            Added = added;
            Removed = removed;
            Kept = kept;
        }

        public bool IsNewScreen { get; }

        /// <summary>Nomes dos nodes que aparecem pela primeira vez.</summary>
        public List<string> Added { get; }

        /// <summary>Nomes dos objetos que serão destruídos.</summary>
        public List<string> Removed { get; }

        public int Kept { get; }

        /// <summary>Se true, vale pedir confirmação antes de gravar.</summary>
        public bool IsDestructive => Removed.Count > 0;

        public static ImportDiff Compute(IRDocument document, string basePrefabPath)
        {
            var incoming = new Dictionary<string, string>(StringComparer.Ordinal);
            Collect(document.Root, incoming);

            var baseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath);

            if (baseAsset == null)
            {
                return new ImportDiff(
                    isNewScreen: true,
                    added: new List<string>(incoming.Values),
                    removed: new List<string>(),
                    kept: 0);
            }

            var existing = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (FigmaNodeRef reference in
                baseAsset.GetComponentsInChildren<FigmaNodeRef>(includeInactive: true))
            {
                if (!string.IsNullOrEmpty(reference.NodeId))
                {
                    existing[reference.NodeId] = reference.gameObject.name;
                }
            }

            var added = new List<string>();
            var removed = new List<string>();
            int kept = 0;

            foreach (KeyValuePair<string, string> entry in incoming)
            {
                if (existing.ContainsKey(entry.Key))
                {
                    kept++;
                }
                else
                {
                    added.Add(entry.Value);
                }
            }

            foreach (KeyValuePair<string, string> entry in existing)
            {
                if (!incoming.ContainsKey(entry.Key))
                {
                    removed.Add(entry.Value);
                }
            }

            return new ImportDiff(isNewScreen: false, added, removed, kept);
        }

        public string Summary()
        {
            if (IsNewScreen)
            {
                return $"Tela nova: {Added.Count} objetos serão criados.";
            }

            return $"{Added.Count} novos, {Kept} preservados, {Removed.Count} removidos.";
        }

        private static void Collect(IRNode node, Dictionary<string, string> into)
        {
            if (!string.IsNullOrEmpty(node.Id))
            {
                into[node.Id] = node.Name;
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
