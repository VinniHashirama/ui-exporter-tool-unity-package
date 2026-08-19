using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Arvore.UIExporter.Editor
{
    /// <summary>Criação de pastas e conversão de caminho de asset para caminho de disco.</summary>
    public static class AssetFolders
    {
        /// <summary>
        /// Garante que a pasta exista, criando os níveis que faltam.
        /// </summary>
        /// <remarks>
        /// <c>AssetDatabase.CreateFolder</c> só cria um nível por vez e falha se o pai não
        /// existir, então o caminho é percorrido segmento a segmento.
        /// </remarks>
        public static void Ensure(string assetFolder)
        {
            if (string.IsNullOrWhiteSpace(assetFolder))
            {
                throw new UIExportException("caminho de pasta vazio.");
            }

            string normalized = assetFolder.Replace('\\', '/').TrimEnd('/');

            if (!normalized.StartsWith("Assets", StringComparison.Ordinal))
            {
                throw new UIExportException(
                    $"'{assetFolder}' está fora de Assets/. O importador nunca escreve fora " +
                    "da pasta de assets do projeto.");
            }

            if (AssetDatabase.IsValidFolder(normalized))
            {
                return;
            }

            string[] segments = normalized.Split('/');
            string current = segments[0];

            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];

                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[i]);
                }

                current = next;
            }
        }

        /// <summary>Converte <c>Assets/x/y.png</c> no caminho absoluto em disco.</summary>
        public static string ToAbsolute(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
