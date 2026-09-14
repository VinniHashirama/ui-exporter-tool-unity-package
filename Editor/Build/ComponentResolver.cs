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
        private readonly List<string> ambiguousNames;

        private ComponentResolver(
            Dictionary<string, GameObject> byCanonicalName,
            List<string> ambiguousNames)
        {
            this.byCanonicalName = byCanonicalName;
            this.ambiguousNames = ambiguousNames;
        }

        public IReadOnlyCollection<string> KnownNames => byCanonicalName.Keys;

        public int Count => byCanonicalName.Count;

        /// <summary>
        /// Nomes canônicos reivindicados por mais de um prefab e não desempatados pela
        /// Mapping Table. Import com qualquer um destes precisa ser recusado.
        /// </summary>
        /// <remarks>
        /// Não existe valor seguro a devolver para um nome ambíguo. Escolher o primeiro da
        /// varredura faz o resultado depender da ordem do <c>AssetDatabase</c>; devolver
        /// <c>null</c> é pior ainda, porque <see cref="PrefabBuilder"/> trata "sem prefab"
        /// como mudança de espécie e <b>destrói</b> toda instância existente daquele
        /// componente, junto com os overrides que o dev tinha nelas. A única ação segura é
        /// não rodar o import.
        /// </remarks>
        public IReadOnlyList<string> AmbiguousNames => ambiguousNames;

        public static ComponentResolver Build(UIImportSettings settings, ImportReport report)
        {
            var map = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            var claimants = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            string[] guids = AssetDatabase.FindAssets("t:Prefab", settings.KitSearchFolders);

            // A ordem de FindAssets não é especificada, então sem ordenar aqui a resolução de
            // um nome disputado mudaria entre máquinas — e, com ela, qual prefab as telas
            // instanciam.
            var paths = new List<string>(guids.Length);
            foreach (string guid in guids)
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            paths.Sort(StringComparer.Ordinal);

            foreach (string path in paths)
            {
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

                if (!claimants.TryGetValue(canonical, out List<string> claimed))
                {
                    claimed = new List<string>(1);
                    claimants.Add(canonical, claimed);
                }

                claimed.Add(path);

                if (!map.ContainsKey(canonical))
                {
                    map.Add(canonical, prefab);
                }
            }

            var overridden = new HashSet<string>(StringComparer.Ordinal);

            if (settings.MappingTable != null)
            {
                foreach (ComponentOverride entry in settings.MappingTable.Overrides)
                {
                    if (entry.prefab == null || string.IsNullOrWhiteSpace(entry.canonicalName))
                    {
                        continue;
                    }

                    map[entry.canonicalName] = entry.prefab;
                    overridden.Add(entry.canonicalName);
                }
            }

            // O override da Mapping Table existe justamente para desempatar, então só é
            // ambíguo o que sobrou sem decisão explícita.
            var ambiguous = new List<string>();

            foreach (KeyValuePair<string, List<string>> entry in claimants)
            {
                if (entry.Value.Count > 1 && !overridden.Contains(entry.Key))
                {
                    ambiguous.Add(entry.Key);
                }
            }

            ambiguous.Sort(StringComparer.Ordinal);

            foreach (string canonical in ambiguous)
            {
                report.Error(
                    "kit/ambiguous",
                    $"'{canonical}' é reivindicado por mais de um prefab " +
                    $"({string.Join(", ", claimants[canonical])}). " +
                    "Aponte o certo com um override na Mapping Table, ou remova o UIKitComponent " +
                    "do outro. O import não roda assim: qual prefab venceria dependeria da ordem " +
                    "da varredura, e trocar de prefab recria as instâncias, perdendo os ajustes " +
                    "do dev nelas.");
            }

            if (map.Count == 0)
            {
                report.Warn(
                    "kit/empty",
                    "Nenhum prefab de kit encontrado. Toda instância de componente vai virar " +
                    "caixa genérica sem comportamento. Gere o kit em " +
                    "Window > Arvore > UI Exporter, ou ajuste as pastas de busca nas Import Settings.");
            }

            return new ComponentResolver(map, ambiguous);
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
