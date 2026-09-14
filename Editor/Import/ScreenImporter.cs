using System.Text;
using UnityEditor;

namespace Arvore.UIExporter.Editor
{
    /// <summary>
    /// Orquestra o import de uma tela, em duas fases.
    /// </summary>
    /// <remarks>
    /// <see cref="Prepare"/> lê, valida e calcula o diff <b>sem escrever nada</b>;
    /// <see cref="Execute"/> comete as mudanças. A separação é o que permite mostrar ao dev o
    /// que vai acontecer — em especial o que vai ser removido — e pedir confirmação antes de
    /// tocar no projeto.
    /// </remarks>
    public static class ScreenImporter
    {
        public sealed class Plan
        {
            public IRDocument Document { get; internal set; }

            public UIExportPackage Package { get; internal set; }

            public string PackagePath { get; internal set; }

            public string ScreenName { get; internal set; }

            public string BasePrefabPath { get; internal set; }

            public string VariantPath { get; internal set; }

            public string SpritesFolder { get; internal set; }

            public ImportDiff Diff { get; internal set; }

            /// <summary>
            /// Montado em <see cref="Prepare"/> e reusado em <see cref="Execute"/>: o diff
            /// depende dele, e reconstruí-lo poderia dar um resultado diferente do que foi
            /// mostrado ao dev.
            /// </summary>
            public ComponentResolver Resolver { get; internal set; }

            public ImportReport Report { get; internal set; }

            public UIImportSettings Settings { get; internal set; }

            /// <summary>Se false, <see cref="Execute"/> não deve ser chamado.</summary>
            public bool CanImport { get; internal set; }
        }

        /// <summary>Lê e valida o pacote. Não escreve nada no projeto.</summary>
        /// <param name="packagePath">Caminho do arquivo <c>.uiexport</c>.</param>
        /// <param name="settings">
        /// Configuração a usar. Null resolve a do projeto — o caminho normal. O parâmetro
        /// existe para os testes poderem apontar para pastas isoladas em vez de escrever no
        /// mesmo lugar que um import de verdade.
        /// </param>
        public static Plan Prepare(string packagePath, UIImportSettings settings = null)
        {
            var report = new ImportReport();
            var plan = new Plan
            {
                PackagePath = packagePath,
                Report = report,
                Settings = settings ?? UIImportSettings.LoadOrDefault(),
                CanImport = false,
            };

            UIExportPackage package;
            IRDocument document;

            try
            {
                package = UIExportPackage.Read(packagePath);
                document = IRReader.Read(package.Json);
            }
            catch (UIExportException error)
            {
                report.Error("package/invalid", error.Message);
                return plan;
            }

            SchemaCompatibility compatibility = SchemaGate.Check(document.SchemaVersion, out string message);

            switch (compatibility)
            {
                case SchemaCompatibility.Incompatible:
                case SchemaCompatibility.Unreadable:
                    report.Error("schema/incompatible", message);
                    return plan;

                case SchemaCompatibility.NewerMinor:
                    report.Warn("schema/newer", message);
                    break;
            }

            string screenName = ResolveScreenName(document);
            if (screenName == null)
            {
                report.Error(
                    "screen/name",
                    $"Não consegui derivar um nome de tela válido de '{document.Root.Name}'.");
                return plan;
            }

            // Diagnósticos do lado do designer entram no report: quem importa precisa saber
            // que a tela já saiu do Figma com avisos, senão investiga na Unity um problema
            // cuja causa está no arquivo.
            report.IngestLint(document.Lint);

            string generatedFolder = $"{plan.Settings.GeneratedRoot}/{screenName}";

            plan.Package = package;
            plan.Document = document;
            plan.ScreenName = screenName;
            plan.BasePrefabPath = $"{generatedFolder}/{screenName}_Base.prefab";
            plan.SpritesFolder = $"{generatedFolder}/Sprites";
            plan.VariantPath = $"{plan.Settings.ScreensRoot}/{screenName}.prefab";

            // O resolver roda aqui, e não no Execute, porque de qual prefab cada nome canônico
            // resolve depende se instâncias existentes serão reusadas ou destruídas. Calcular o
            // diff antes de saber disso deixaria a única confirmação do sistema cega justamente
            // para a mudança mais cara.
            plan.Resolver = ComponentResolver.Build(plan.Settings, report);
            plan.Diff = ImportDiff.Compute(document, plan.BasePrefabPath, plan.Resolver);

            if (plan.Resolver.AmbiguousNames.Count > 0)
            {
                return plan;
            }

            plan.CanImport = true;

            return plan;
        }

        /// <summary>Comete o import. Só chamar quando <see cref="Plan.CanImport"/> é true.</summary>
        public static ImportReport Execute(Plan plan)
        {
            ImportReport report = plan.Report;

            if (!plan.CanImport)
            {
                report.Error("import/aborted", "O pacote não passou na validação; nada foi escrito.");
                return report;
            }

            try
            {
                AssetFolders.Ensure($"{plan.Settings.GeneratedRoot}/{plan.ScreenName}");
                AssetFolders.Ensure(plan.Settings.ScreensRoot);

                SpriteImporter.Result spriteResult = SpriteImporter.Import(
                    plan.Document,
                    plan.Package,
                    plan.SpritesFolder,
                    plan.Settings,
                    report);

                ComponentResolver resolver = plan.Resolver;

                var builder = new PrefabBuilder(plan.Settings, resolver, spriteResult, report);
                builder.BuildOrUpdate(plan.Document, plan.BasePrefabPath);

                PrefabBuilder.EnsureVariant(plan.BasePrefabPath, plan.VariantPath, report);

                AssetDatabase.SaveAssets();

                report.Info(
                    "import/done",
                    BuildSummary(plan, spriteResult, resolver));
            }
            catch (UIExportException error)
            {
                report.Error("import/failed", error.Message);
            }

            return report;
        }

        private static string BuildSummary(
            Plan plan,
            SpriteImporter.Result sprites,
            ComponentResolver resolver)
        {
            var summary = new StringBuilder();

            summary.Append(plan.ScreenName).Append(": ");
            summary.Append(plan.Diff.Summary());
            summary.Append(' ');
            summary.Append($"{sprites.Written} sprite(s) gravado(s), {sprites.Reused} sem mudança. ");
            summary.Append($"{resolver.Count} componente(s) de kit disponíveis.");

            return summary.ToString();
        }

        /// <summary>
        /// Deriva o nome da tela do node raiz.
        /// </summary>
        /// <remarks>
        /// Vira nome de pasta e de arquivo, então passa por whitelist estrita. O nome vem de
        /// fora do projeto e não pode carregar nada que atravesse caminho.
        /// </remarks>
        private static string ResolveScreenName(IRDocument document)
        {
            string raw = document.Root?.Name;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var builder = new StringBuilder(raw.Length);

            foreach (char c in raw.Trim())
            {
                bool allowed = (c >= 'a' && c <= 'z')
                    || (c >= 'A' && c <= 'Z')
                    || (c >= '0' && c <= '9')
                    || c == '_'
                    || c == '-';

                if (allowed)
                {
                    builder.Append(c);
                }
            }

            string name = builder.ToString();

            if (name.Length == 0 || name.Length > 64)
            {
                return null;
            }

            return name;
        }
    }
}
