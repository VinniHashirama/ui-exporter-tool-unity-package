using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arvore.UIExporter
{
    /// <summary>Uma referência nomeada, vinda de uma layer marcada com <c>@Nome</c>.</summary>
    [Serializable]
    public struct UIViewRef
    {
        public string key;
        public GameObject target;
    }

    /// <summary>
    /// Ponte entre a tela importada e o código do jogo.
    /// </summary>
    /// <remarks>
    /// Toda layer marcada com <c>@Nome</c> no Figma entra aqui, então o dev acessa
    /// <c>view.Get&lt;Button&gt;("PlayButton")</c> em vez de caçar o objeto na hierarquia
    /// por caminho — caminho quebra quando o designer reorganiza a árvore, a chave não.
    /// <para>
    /// No MVP o binding é por string de propósito: gerar uma classe de view tipada
    /// obrigaria o import a esperar a recompilação do assembly, o que transforma um import
    /// de 2 segundos num ciclo de 30. Codegen tipado está previsto para v0.2.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class UIViewRefs : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Preenchido pelo importador a partir das layers marcadas com @.")]
        private List<UIViewRef> refs = new List<UIViewRef>();

        [SerializeField]
        [Tooltip("Resolução em que a tela foi desenhada. Use para configurar o CanvasScaler.")]
        private Vector2 designResolution;

        public IReadOnlyList<UIViewRef> Refs => refs;

        /// <summary>
        /// Tamanho do frame no arquivo de origem, em px de design.
        /// </summary>
        /// <remarks>
        /// Este componente vive na raiz da tela, então é onde a informação de resolução de
        /// referência pertence. O prefab gerado não traz Canvas próprio de propósito — em
        /// jogo as telas normalmente vivem sob um Canvas compartilhado — e é este valor que
        /// diz em que <c>CanvasScaler.referenceResolution</c> a tela foi desenhada.
        /// </remarks>
        public Vector2 DesignResolution => designResolution;

        /// <summary>Objeto associado à chave, ou null se não existir.</summary>
        public GameObject Find(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            foreach (UIViewRef entry in refs)
            {
                if (string.Equals(entry.key, key, StringComparison.Ordinal))
                {
                    return entry.target;
                }
            }

            return null;
        }

        public bool TryGet<T>(string key, out T component) where T : Component
        {
            GameObject found = Find(key);
            if (found == null)
            {
                component = null;
                return false;
            }

            return found.TryGetComponent(out component);
        }

        /// <summary>
        /// Igual a <see cref="TryGet{T}"/>, mas lança quando não encontra.
        /// </summary>
        /// <remarks>
        /// Falha explícita e imediata é melhor que devolver null: uma chave errada
        /// devolvida como null só aparece como NullReferenceException longe da causa, num
        /// ponto que não diz nada sobre qual bind quebrou.
        /// </remarks>
        public T Get<T>(string key) where T : Component
        {
            GameObject found = Find(key);
            if (found == null)
            {
                throw new KeyNotFoundException(
                    $"UIViewRefs em '{name}' não tem o bind '{key}'. " +
                    "Marque a layer correspondente com '@" + key + "' no Figma e re-exporte.");
            }

            if (!found.TryGetComponent(out T component))
            {
                throw new KeyNotFoundException(
                    $"O bind '{key}' em '{name}' aponta para '{found.name}', que não tem " +
                    $"{typeof(T).Name}.");
            }

            return component;
        }

        /// <summary>Chamado pelo importador.</summary>
        public void SetRefs(IEnumerable<UIViewRef> newRefs, Vector2 resolution)
        {
            refs = new List<UIViewRef>(newRefs);
            designResolution = resolution;
        }
    }
}
