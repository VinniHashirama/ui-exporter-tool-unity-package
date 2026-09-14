using UnityEditor;
using UnityEngine;

namespace Arvore.UIExporter.Editor
{
    /// <summary>
    /// Condição de dimensão que o importador exige de cada sprite.
    /// </summary>
    /// <remarks>
    /// A distinção importa e costuma ser confundida: o que a <b>compressão em blocos</b>
    /// (ASTC, DXT, ETC) exige é dimensão múltipla de 4 — sem isso a textura fica em RGBA32 e
    /// ocupa várias vezes mais memória. Potência de 2 é requisito de formatos e plataformas
    /// antigos, e para UI em UGUI quase nunca faz diferença. Por isso o padrão é o múltiplo
    /// de 4, e POT existe como opção para quem tem uma exigência concreta.
    /// </remarks>
    public enum SpriteSizePolicy
    {
        /// <summary>Aceita qualquer dimensão; nem relata.</summary>
        None,

        /// <summary>Exige múltiplo de 4, para a compressão em blocos funcionar.</summary>
        MultipleOfFour,

        /// <summary>Exige potência de 2. Só para quem tem um formato que pede isso.</summary>
        PowerOfTwo,
    }

    /// <summary>
    /// Configuração do importador, uma por projeto.
    /// </summary>
    /// <remarks>
    /// A ferramenta é distribuída como pacote e usada em jogos diferentes, então nada de
    /// caminho ou referência de asset pode estar embutido no código.
    /// </remarks>
    [CreateAssetMenu(
        menuName = "Arvore/UI Exporter/Import Settings",
        fileName = "UIImportSettings")]
    public sealed class UIImportSettings : ScriptableObject
    {
        [Header("Destinos")]
        [SerializeField]
        [Tooltip("Onde os prefabs BASE são gerados. Território da ferramenta: sobrescrito a cada import.")]
        private string generatedRoot = "Assets/UI/Generated";

        [SerializeField]
        [Tooltip("Onde os Prefab Variants do dev ficam. A ferramenta cria uma vez e nunca mais toca.")]
        private string screensRoot = "Assets/UI/Screens";

        [Header("Mapeamento")]
        [SerializeField]
        private FontMap fontMap;

        [SerializeField]
        [Tooltip("Só para exceções: o mapeamento normal é descoberto varrendo o projeto.")]
        private UIMappingTable mappingTable;

        [SerializeField]
        [Tooltip("Pastas onde procurar prefabs do kit. Restringir acelera muito o import em projeto grande.")]
        private string[] kitSearchFolders = { "Assets" };

        [Header("Sprites")]
        [SerializeField]
        [Tooltip("Sprite branco com bordas 9-slice, usado em fundos de cor chapada com canto arredondado.")]
        private Sprite roundedSprite;

        [SerializeField]
        [Range(256, 8192)]
        private int maxTextureSize = 2048;

        [SerializeField]
        [Tooltip(
            "Dimensão mínima exigida do sprite. Múltiplo de 4 é o que a compressão em blocos " +
            "(ASTC/DXT/ETC) precisa; sem isso a textura fica em RGBA32 e ocupa várias vezes " +
            "mais memória. Potência de 2 quase nunca é necessária para UI em UGUI.")]
        private SpriteSizePolicy spriteSizePolicy = SpriteSizePolicy.MultipleOfFour;

        public string GeneratedRoot => Normalize(generatedRoot, "Assets/UI/Generated");

        public string ScreensRoot => Normalize(screensRoot, "Assets/UI/Screens");

        public FontMap FontMap => fontMap;

        public UIMappingTable MappingTable => mappingTable;

        public Sprite RoundedSprite => roundedSprite;

        public int MaxTextureSize => maxTextureSize;

        public SpriteSizePolicy SpriteSizePolicy => spriteSizePolicy;

        public string[] KitSearchFolders =>
            kitSearchFolders is { Length: > 0 } ? kitSearchFolders : new[] { "Assets" };

        /// <summary>
        /// Acha o asset de configuração do projeto, ou devolve um default em memória.
        /// </summary>
        /// <remarks>
        /// Devolver um default em vez de exigir configuração é deliberado: a ferramenta tem
        /// que funcionar no primeiro import, sem cerimônia. O que falta aparece no report.
        /// </remarks>
        public static UIImportSettings LoadOrDefault()
        {
            string[] found = AssetDatabase.FindAssets($"t:{nameof(UIImportSettings)}");

            foreach (string guid in found)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var settings = AssetDatabase.LoadAssetAtPath<UIImportSettings>(path);
                if (settings != null)
                {
                    return settings;
                }
            }

            return CreateInstance<UIImportSettings>();
        }

        private static string Normalize(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            string trimmed = value.Trim().Replace('\\', '/').TrimEnd('/');
            return trimmed.StartsWith("Assets", System.StringComparison.Ordinal) ? trimmed : fallback;
        }
    }
}
