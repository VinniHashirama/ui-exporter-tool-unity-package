using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Arvore.UIExporter.Editor
{
    /// <summary>Erro de leitura do pacote, com mensagem destinada ao dev.</summary>
    public sealed class UIExportException : Exception
    {
        public UIExportException(string message) : base(message)
        {
        }

        public UIExportException(string message, Exception inner) : base(message, inner)
        {
        }
    }

    /// <summary>Desserializa o <c>ui.json</c> do pacote no modelo do IR.</summary>
    public static class IRReader
    {
        private static readonly JsonSerializerSettings Settings = BuildSettings();

        public static IRDocument Read(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new UIExportException("ui.json está vazio.");
            }

            IRDocument document;
            try
            {
                document = JsonConvert.DeserializeObject<IRDocument>(json, Settings);
            }
            catch (JsonException error)
            {
                throw new UIExportException($"ui.json não é um JSON válido: {error.Message}", error);
            }

            if (document == null)
            {
                throw new UIExportException("ui.json não produziu nenhum documento.");
            }

            Validate(document);
            return document;
        }

        private static JsonSerializerSettings BuildSettings()
        {
            return new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy(),
                },
                Converters = { new WireEnumConverter() },

                // Compatibilidade para frente: um pacote de MINOR mais nova pode trazer
                // campos que esta versão não conhece. Ignorar é o comportamento certo —
                // o SchemaGate já avisou que o pacote é mais novo.
                MissingMemberHandling = MissingMemberHandling.Ignore,

                // O pacote é dado NÃO CONFIÁVEL: nunca deixar o JSON escolher qual tipo
                // instanciar. Ambos são o default do Newtonsoft, declarados explicitamente
                // porque desativá-los por acidente abriria execução de código arbitrário.
                TypeNameHandling = TypeNameHandling.None,
                MetadataPropertyHandling = MetadataPropertyHandling.Ignore,

                // ExportedAt fica string; sem isso o Newtonsoft converteria para DateTime
                // e o valor exibido no report mudaria conforme o fuso da máquina.
                DateParseHandling = DateParseHandling.None,
            };
        }

        /// <summary>
        /// Só o que o resto do importador assume como garantido. A validação completa
        /// contra o schema acontece no CI do plugin, do lado que gera o pacote.
        /// </summary>
        private static void Validate(IRDocument document)
        {
            if (string.IsNullOrEmpty(document.SchemaVersion))
            {
                throw new UIExportException("ui.json não declara schemaVersion.");
            }

            if (document.Root == null)
            {
                throw new UIExportException("ui.json não tem node raiz.");
            }

            if (document.Canvas == null || document.Canvas.Width <= 0f || document.Canvas.Height <= 0f)
            {
                throw new UIExportException("ui.json não tem um canvas com tamanho válido.");
            }

            document.Assets ??= new System.Collections.Generic.List<IRAsset>();
            document.Lint ??= new System.Collections.Generic.List<IRDiagnostic>();

            ValidateNode(document.Root, isRoot: true);
        }

        private static void ValidateNode(IRNode node, bool isRoot)
        {
            if (string.IsNullOrEmpty(node.Id))
            {
                throw new UIExportException(
                    $"node '{node.Name}' está sem id — sem ele a reconciliação não " +
                    "consegue reencontrar o objeto e o trabalho do dev seria perdido.");
            }

            if (string.IsNullOrEmpty(node.Name))
            {
                node.Name = isRoot ? "Screen" : "Node";
            }

            if (node.Rect == null)
            {
                throw new UIExportException($"node '{node.Name}' está sem rect.");
            }

            if (node.Kind == NodeKind.Text && node.Text == null)
            {
                throw new UIExportException($"node de texto '{node.Name}' está sem o bloco text.");
            }

            if (node.Kind == NodeKind.Instance && node.Component == null)
            {
                throw new UIExportException(
                    $"instância '{node.Name}' está sem o bloco component.");
            }

            if (node.CornerRadius != null && node.CornerRadius.Length != 4)
            {
                throw new UIExportException(
                    $"cornerRadius de '{node.Name}' precisa ter 4 valores.");
            }

            if (node.Layout?.Padding != null && node.Layout.Padding.Length != 4)
            {
                throw new UIExportException($"padding de '{node.Name}' precisa ter 4 valores.");
            }

            if (node.Children == null)
            {
                return;
            }

            foreach (IRNode child in node.Children)
            {
                ValidateNode(child, isRoot: false);
            }
        }
    }
}
