using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Arvore.UIExporter.Editor
{
    [Serializable]
    public struct FontMapping
    {
        [Tooltip("Família como o Figma reporta, ex. Nunito.")]
        public string family;

        [Tooltip("Estilo como o Figma reporta, ex. Bold. Vazio casa com qualquer estilo da família.")]
        public string style;

        public TMP_FontAsset font;
    }

    /// <summary>
    /// Traduz família + estilo do arquivo de design para um <see cref="TMP_FontAsset"/>.
    /// </summary>
    /// <remarks>
    /// Um por jogo: a estrutura da tela é compartilhada entre projetos, a tipografia não.
    /// <para>
    /// Fonte não encontrada nunca falha o import — cai no fallback e entra no report. Uma
    /// tela montada com a fonte errada é corrigível em segundos; um import que aborta no
    /// meio deixa o prefab num estado indefinido.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(
        menuName = "Arvore/UI Exporter/Font Map",
        fileName = "UIFontMap")]
    public sealed class FontMap : ScriptableObject
    {
        [SerializeField]
        private List<FontMapping> mappings = new List<FontMapping>();

        [SerializeField]
        [Tooltip("Usada quando nenhum mapeamento casa. Sem ela o importador usa a fonte default do TMP.")]
        private TMP_FontAsset fallback;

        public TMP_FontAsset Fallback => fallback;

        /// <summary>
        /// Resolve na ordem: família + estilo exatos, depois só família, depois fallback.
        /// </summary>
        /// <param name="matchQuality">
        /// O quão bom foi o casamento, para o report distinguir "achei" de "achei mais ou
        /// menos" de "não achei".
        /// </param>
        public TMP_FontAsset Resolve(string family, string style, out FontMatch matchQuality)
        {
            if (!string.IsNullOrEmpty(family))
            {
                foreach (FontMapping mapping in mappings)
                {
                    if (mapping.font != null &&
                        Same(mapping.family, family) &&
                        Same(mapping.style, style))
                    {
                        matchQuality = FontMatch.Exact;
                        return mapping.font;
                    }
                }

                foreach (FontMapping mapping in mappings)
                {
                    if (mapping.font != null && Same(mapping.family, family))
                    {
                        matchQuality = FontMatch.FamilyOnly;
                        return mapping.font;
                    }
                }
            }

            matchQuality = FontMatch.Fallback;
            return fallback;
        }

        private static bool Same(string a, string b)
        {
            return string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    public enum FontMatch
    {
        Exact,
        FamilyOnly,
        Fallback,
    }
}
