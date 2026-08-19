using System;
using System.Globalization;

namespace Arvore.UIExporter.Editor
{
    public enum SchemaCompatibility
    {
        /// <summary>Mesma major, minor conhecida. Importa normalmente.</summary>
        Compatible,

        /// <summary>Mesma major, minor mais nova que a suportada. Importa com aviso.</summary>
        NewerMinor,

        /// <summary>Major diferente. Recusado.</summary>
        Incompatible,

        /// <summary>schemaVersion ausente ou fora do formato semver.</summary>
        Unreadable,
    }

    /// <summary>
    /// Decide se um pacote pode ser importado por esta versão do importador.
    /// </summary>
    /// <remarks>
    /// Recusar major diferente é deliberado: tentar adivinhar a intenção de um pacote de
    /// outra major produz um prefab silenciosamente errado, o que custa muito mais caro
    /// que um erro de import explícito.
    /// </remarks>
    public static class SchemaGate
    {
        public const int SupportedMajor = 1;
        public const int SupportedMinor = 0;

        public static string SupportedVersion =>
            $"{SupportedMajor}.{SupportedMinor}.x";

        public static SchemaCompatibility Check(string schemaVersion, out string message)
        {
            if (!TryParse(schemaVersion, out int major, out int minor, out _))
            {
                message =
                    $"schemaVersion '{schemaVersion}' não está no formato semver. " +
                    $"Este importador suporta {SupportedVersion}.";
                return SchemaCompatibility.Unreadable;
            }

            if (major != SupportedMajor)
            {
                message =
                    $"O pacote é da versão {schemaVersion} e este importador suporta " +
                    $"{SupportedVersion}. " +
                    (major > SupportedMajor
                        ? "Atualize o pacote com.arvore.uiexporter."
                        : "O pacote foi gerado por uma versão antiga do plugin do Figma; " +
                          "peça um novo export.");
                return SchemaCompatibility.Incompatible;
            }

            if (minor > SupportedMinor)
            {
                message =
                    $"O pacote é da versão {schemaVersion}, mais nova que a suportada " +
                    $"({SupportedVersion}). O import segue, mas recursos novos do contrato " +
                    "serão ignorados — vale atualizar o pacote da Unity.";
                return SchemaCompatibility.NewerMinor;
            }

            message = null;
            return SchemaCompatibility.Compatible;
        }

        private static bool TryParse(string version, out int major, out int minor, out int patch)
        {
            major = 0;
            minor = 0;
            patch = 0;

            if (string.IsNullOrWhiteSpace(version))
            {
                return false;
            }

            string[] parts = version.Trim().Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            return TryParsePart(parts[0], out major)
                && TryParsePart(parts[1], out minor)
                && TryParsePart(parts[2], out patch);
        }

        private static bool TryParsePart(string part, out int value)
        {
            return int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }
    }
}
