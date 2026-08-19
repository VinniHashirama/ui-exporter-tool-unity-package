using System;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;

namespace Arvore.UIExporter.Editor
{
    /// <summary>
    /// Faz a ponte entre o <c>UPPER_SNAKE_CASE</c> do JSON e enums PascalCase em C#.
    /// </summary>
    /// <remarks>
    /// O contrato usa valores como <c>SPACE_BETWEEN</c> e <c>WIDTH_AND_HEIGHT</c>. Sem
    /// este conversor, a alternativa seria anotar cada valor de cada enum com
    /// <c>[EnumMember]</c> — dezenas de atributos que ninguém mantém em sincronia — ou
    /// nomear os membros em UPPER_SNAKE, deixando o modelo estranho de usar no resto do
    /// código. Um conversor resolve os dois problemas de uma vez.
    /// <para>
    /// A comparação ignora underscores e caixa, então <c>SPACE_BETWEEN</c> casa com
    /// <c>SpaceBetween</c>.
    /// </para>
    /// </remarks>
    internal sealed class WireEnumConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            Type type = Nullable.GetUnderlyingType(objectType) ?? objectType;
            return type.IsEnum;
        }

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            Type enumType = Nullable.GetUnderlyingType(objectType) ?? objectType;

            if (reader.TokenType == JsonToken.Null)
            {
                if (Nullable.GetUnderlyingType(objectType) != null)
                {
                    return null;
                }

                throw new JsonSerializationException(
                    $"null não é um valor válido para {enumType.Name}.");
            }

            string raw = reader.Value?.ToString();
            if (string.IsNullOrEmpty(raw))
            {
                throw new JsonSerializationException($"valor vazio para {enumType.Name}.");
            }

            string normalized = Normalize(raw);

            foreach (string name in Enum.GetNames(enumType))
            {
                if (string.Equals(Normalize(name), normalized, StringComparison.Ordinal))
                {
                    return Enum.Parse(enumType, name);
                }
            }

            throw new JsonSerializationException(
                $"'{raw}' não é um valor conhecido de {enumType.Name}. " +
                $"Valores aceitos: {string.Join(", ", Enum.GetNames(enumType))}.");
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteValue(ToWire(value.ToString()));
        }

        private static string Normalize(string value)
        {
            return value.Replace("_", string.Empty).ToUpperInvariant();
        }

        /// <summary><c>SpaceBetween</c> -&gt; <c>SPACE_BETWEEN</c>.</summary>
        private static string ToWire(string pascalCase)
        {
            var builder = new StringBuilder(pascalCase.Length + 4);

            for (int i = 0; i < pascalCase.Length; i++)
            {
                char current = pascalCase[i];

                if (i > 0 && char.IsUpper(current) && !char.IsUpper(pascalCase[i - 1]))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToUpper(current, CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
