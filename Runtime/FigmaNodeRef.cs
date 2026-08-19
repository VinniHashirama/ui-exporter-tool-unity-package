using UnityEngine;

namespace Arvore.UIExporter
{
    /// <summary>
    /// Liga este GameObject ao node de origem no pacote <c>.uiexport</c>.
    /// </summary>
    /// <remarks>
    /// É a chave de reconciliação do importador, e a razão pela qual o trabalho do dev
    /// sobrevive a um re-export do designer.
    /// <para>
    /// Um Prefab Variant rastreia seus overrides pelo <c>fileID</c> local de cada objeto
    /// no prefab base. Se o import destruísse e recriasse os GameObjects do base, todos
    /// os <c>fileID</c> mudariam e os overrides do Variant virariam órfãos — o dev
    /// perderia scripts e referências, em silêncio e sem erro no console. Com este
    /// componente, o importador reencontra o GameObject correspondente a cada node e
    /// reusa o mesmo objeto, preservando o <c>fileID</c>.
    /// </para>
    /// <para>
    /// Consequência prática para quem desenha: renomear uma layer é seguro; deletar e
    /// recriar não é, porque a layer recriada tem id novo e para a ferramenta é outro
    /// objeto.
    /// </para>
    /// Componente gerenciado pelo importador. Não adicione nem edite na mão.
    /// </remarks>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class FigmaNodeRef : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Id do node na origem. Somente o importador escreve aqui.")]
        private string nodeId;

        [SerializeField]
        [Tooltip("Nome que a layer tinha na origem, para diagnóstico.")]
        private string sourceName;

        public string NodeId => nodeId;

        public string SourceName => sourceName;

        /// <summary>Chamado pelo importador ao criar ou reconciliar o objeto.</summary>
        public void Assign(string id, string originalName)
        {
            nodeId = id;
            sourceName = originalName;
        }
    }
}
