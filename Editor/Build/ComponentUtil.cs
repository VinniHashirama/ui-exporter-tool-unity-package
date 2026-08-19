using UnityEngine;

namespace Arvore.UIExporter.Editor
{
    /// <summary>
    /// Helpers de componente para a reconciliação.
    /// </summary>
    /// <remarks>
    /// Como o importador <b>reusa</b> GameObjects entre imports em vez de recriá-los,
    /// aplicar um node não é só adicionar componentes: é convergir o objeto para o estado
    /// que o design descreve, o que inclui remover o que não pertence mais. Sem isso um
    /// node que mudou de tipo carrega resíduo do estado anterior.
    /// </remarks>
    public static class ComponentUtil
    {
        public static T Ensure<T>(GameObject target) where T : Component
        {
            return target.TryGetComponent(out T existing) ? existing : target.AddComponent<T>();
        }

        public static void Remove<T>(GameObject target) where T : Component
        {
            if (target.TryGetComponent(out T existing))
            {
                Object.DestroyImmediate(existing, allowDestroyingAssets: false);
            }
        }

        /// <summary>Converte <c>#RRGGBB</c> ou <c>#RRGGBBAA</c> em cor.</summary>
        public static Color ParseColor(string hex, Color fallback)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color parsed))
            {
                return parsed;
            }

            return fallback;
        }
    }
}
