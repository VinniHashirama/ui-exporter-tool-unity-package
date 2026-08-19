using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arvore.UIExporter.Editor
{
    [Serializable]
    public struct ComponentOverride
    {
        [Tooltip("Nome canônico vindo do design, ex. Button/Primary.")]
        public string canonicalName;

        [Tooltip("Prefab que este jogo usa para esse nome.")]
        public GameObject prefab;
    }

    /// <summary>
    /// Exceções ao mapeamento automático de componentes.
    /// </summary>
    /// <remarks>
    /// O caso comum <b>não passa por aqui</b>: o importador varre o projeto procurando
    /// prefabs com <see cref="UIKitComponent"/> e monta a tabela sozinho, então colocar o
    /// prefab no projeto com o nome canônico certo já o registra.
    /// <para>
    /// Esta tabela existe para os casos em que isso não basta: mais de um prefab
    /// reivindicando o mesmo nome, ou um jogo que resolve um componente canônico de outra
    /// forma. Manter a configuração manual pequena é intencional — tabela grande é tabela
    /// que dessincroniza.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(
        menuName = "Arvore/UI Exporter/Mapping Table",
        fileName = "UIMappingTable")]
    public sealed class UIMappingTable : ScriptableObject
    {
        [SerializeField]
        private List<ComponentOverride> overrides = new List<ComponentOverride>();

        public IReadOnlyList<ComponentOverride> Overrides => overrides;

        public bool TryResolve(string canonicalName, out GameObject prefab)
        {
            prefab = null;
            if (string.IsNullOrEmpty(canonicalName))
            {
                return false;
            }

            foreach (ComponentOverride entry in overrides)
            {
                if (entry.prefab != null &&
                    string.Equals(entry.canonicalName, canonicalName, StringComparison.Ordinal))
                {
                    prefab = entry.prefab;
                    return true;
                }
            }

            return false;
        }
    }
}
