using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Arvore.UIExporter.Editor
{
    /// <summary>
    /// Resolve nome canônico do design para o prefab do kit daquele jogo.
    /// </summary>
    /// <remarks>
    /// A descoberta é por <b>varredura do projeto</b>, não por configuração: qualquer prefab
    /// com <see cref="UIKitComponent"/> se registra sozinho. Isso importa porque a
    /// alternativa — uma tabela mantida à mão — dessincroniza na primeira semana, e um
    /// mapeamento errado é indistinguível de um componente faltando.
    /// <para>
    /// A <see cref="UIMappingTable"/> entra por cima, só para exceções: dois prefabs
    /// reivindicando o mesmo nome, ou um jogo que resolve um componente de outra forma.
    /// </para>
    /// </remarks>
    public sealed class ComponentResolver
    {
        private readonly Dictionary<string, GameObject> byCanonicalName;

        private ComponentResolver(Dictionary<string, GameObject> byCanonicalName)
        {
            this.byCanonicalName = byCanonicalName;
        }

        public IReadOnlyCollection<string> KnownNames => byCanonicalName.Keys;

        public int Count => byCanonicalName.Count;

        public static ComponentResolver Build(UIImportSettings settings, ImportReport report)
        {
            var map = new Dictionary<string, GameObject>(StringComparer.Ordinal);

            string[] guids = AssetDatabase.FindAssets("t:Prefab", settings.KitSearchFolders);

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                if (!prefab.TryGetComponent(out UIKitComponent kit))
                {
                    continue;
                }

                string canonical = kit.CanonicalName;
                if (string.IsNullOrWhiteSpace(canonical))
                {
                    report.Warn(
                        "kit/unnamed",
                        $"O prefab '{path}' tem UIKitComponent sem nome canônico, então " +
                        "nenhuma tela consegue apontar para ele.");
                    continue;
                }

                if (map.TryGetValue(canonical, out GameObject existing))
                {
                    // Ambíguo: escolher em silêncio significaria que o resultado do import
                    // depende da ordem da varredura, o que é pior que reclamar.
                    report.Warn(
                        "kit/ambiguous",
                        $"'{canonical}' é reivindicado por mais de um prefab " +
                        $"('{AssetDatabase.GetAssetPath(existing)}' e '{path}'). " +
                        "Resolva com um override na Mapping Table.");
                    continue;
                }

                map.Add(canonical, prefab);
            }

            if (settings.MappingTable != null)
            {
                foreach (ComponentOverride entry in settings.MappingTable.Overrides)
                {
                    if (entry.prefab == null || string.IsNullOrWhiteSpace(entry.canonicalName))
                    {
                        continue;
                    }

                    map[entry.canonicalName] = entry.prefab;
                }
            }

            if (map.Count == 0)
            {
                report.Warn(
                    "kit/empty",
                    "Nenhum prefab de kit encontrado. Toda instância de componente vai virar " +
                    "caixa genérica sem comportamento. Gere o kit em " +
                    "Window > Arvore > UI Exporter, ou ajuste as pastas de busca nas Import Settings.");
            }

            return new ComponentResolver(map);
        }

        public GameObject Resolve(string canonicalName)
        {
            if (string.IsNullOrEmpty(canonicalName))
            {
                return null;
            }

            return byCanonicalName.TryGetValue(canonicalName, out GameObject prefab) ? prefab : null;
        }

        /// <summary>
        /// Sugere nomes parecidos para a mensagem de erro. Um typo de caixa entre design e
        /// prefab é o motivo mais comum de "não achei", e apontar o vizinho resolve na hora.
        /// </summary>
        public string SuggestSimilar(string canonicalName)
        {
            if (string.IsNullOrEmpty(canonicalName))
            {
                return null;
            }

            foreach (string known in byCanonicalName.Keys)
            {
                if (string.Equals(known, canonicalName, StringComparison.OrdinalIgnoreCase))
                {
                    return known;
                }
            }

            string family = canonicalName.Split('/')[0];
            foreach (string known in byCanonicalName.Keys)
            {
                if (known.StartsWith(family + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return known;
                }
            }

            return null;
        }
    }
}
