using TMPro;

namespace Arvore.UIExporter.Editor
{
    /// <summary>
    /// Acesso defensivo às configurações do TextMeshPro.
    /// </summary>
    /// <remarks>
    /// Num projeto sem TMP Essential Resources importados, o próprio getter de
    /// <c>TMP_Settings.defaultFontAsset</c> lança <see cref="System.NullReferenceException"/>
    /// — checar o retorno contra null não protege, porque a exceção acontece antes de haver
    /// retorno. Como o pacote é instalado em jogos diferentes, incluindo projetos recém
    /// criados, o acesso precisa passar por aqui.
    /// </remarks>
    internal static class TmpSupport
    {
        internal static bool TryGetDefaultFont(out TMP_FontAsset font)
        {
            try
            {
                font = TMP_Settings.defaultFontAsset;
            }
            catch (System.Exception)
            {
                font = null;
            }

            return font != null;
        }

        /// <summary>
        /// Mensagem única para o report quando não há fonte nenhuma disponível.
        /// </summary>
        internal const string MissingResourcesMessage =
            "Este projeto não tem TMP Essential Resources importados, então nenhuma fonte " +
            "default existe e os textos não vão renderizar. Importe em " +
            "Window > TextMeshPro > Import TMP Essential Resources.";
    }
}
