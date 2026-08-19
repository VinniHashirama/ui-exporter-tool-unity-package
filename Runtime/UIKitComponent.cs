using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arvore.UIExporter
{
    /// <summary>Ponto de injeção declarado por um prefab do kit.</summary>
    [Serializable]
    public struct UIKitSlot
    {
        [Tooltip("Nome do slot conforme a spec do kit: label, iconLeft, title, body, close...")]
        public string name;

        [Tooltip("Onde o importador deve escrever o conteúdo deste slot.")]
        public Transform target;
    }

    /// <summary>
    /// Marca um prefab como a implementação de um componente canônico do kit.
    /// </summary>
    /// <remarks>
    /// O importador descobre o mapeamento varrendo o projeto por prefabs com este
    /// componente — colocar o prefab no projeto com o <see cref="CanonicalName"/> certo
    /// já o registra, sem configuração manual.
    /// <para>
    /// Os slots são declarados pelo prefab, nunca adivinhados pelo builder. Por isso a
    /// hierarquia interna do prefab é livre: dá para reestruturar um botão sem quebrar
    /// nenhuma tela já importada, desde que os slots continuem apontando para os lugares
    /// certos.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class UIKitComponent : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Nome canônico, ex. Button/Primary. Case-sensitive, igual ao do contrato.")]
        private string canonicalName;

        [SerializeField]
        [Tooltip("Slots que este componente expõe ao importador.")]
        private List<UIKitSlot> slots = new List<UIKitSlot>();

        public string CanonicalName => canonicalName;

        public IReadOnlyList<UIKitSlot> Slots => slots;

        /// <summary>
        /// Busca o destino de um slot. Comparação case-insensitive, e slot apontando para
        /// nada é tratado como ausente — um prefab meio configurado não deve travar o
        /// import, só deixar o slot vazio e aparecer no report.
        /// </summary>
        public bool TryGetSlot(string slotName, out Transform target)
        {
            target = null;
            if (string.IsNullOrEmpty(slotName))
            {
                return false;
            }

            foreach (UIKitSlot slot in slots)
            {
                if (slot.target != null &&
                    string.Equals(slot.name, slotName, StringComparison.OrdinalIgnoreCase))
                {
                    target = slot.target;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Usado pelo gerador do kit placeholder.</summary>
        public void Configure(string canonical, IEnumerable<UIKitSlot> newSlots)
        {
            canonicalName = canonical;
            slots = new List<UIKitSlot>(newSlots);
        }
    }
}
