using System.Text;
using UnityEditor;
using UnityEngine;

namespace Arvore.UIExporter.Editor
{
    /// <summary>
    /// Orquestra o import de um componente do kit, nas mesmas duas fases do import de tela.
    /// </summary>
    /// <remarks>
    /// <see cref="Prepare"/> lê e valida sem escrever nada; <see cref="Execute"/> comete. A
    /// separação importa ainda mais aqui do que numa tela: o prefab do kit é instanciado por
    /// <b>todas</b> as telas do jogo, então um import que recrie objetos muda fileIDs que
    /// telas não envolvidas neste import dependem. O que se perde ali não aparece em lugar
    /// nenhum até alguém abrir a tela semanas depois.
    /// </remarks>
    public static class KitImporter
    {
        public sealed class Plan
        {
            public IRDocument Document { get; internal set; }

            public UIExportPackage Package { get; internal set; }

            public string PackagePath { get; internal set; }

            public string CanonicalName { get; internal set; }

            public string PrefabPath { get; internal set; }

            public string SpritesFolder { get; internal set; }

            public ImportDiff Diff { get; internal set; }

            public ImportReport Report { get; internal set; }

            public UIImportSettings Settings { get; internal set; }

            /// <summary>
            /// Já existe um prefab neste nome canônico que não foi criado por esta ferramenta.
            /// </summary>
            /// <remarks>
            /// Pode ser o kit placeholder gerado pelo menu, ou um prefab feito à mão pelo dev.
            /// Sobrescrever sem perguntar apagaria trabalho; por isso o import só segue com
            /// <see cref="AdoptExisting"/> ligado, o que é uma decisão explícita na janela.
            /// </remarks>
            public bool NeedsAdoption { get; internal set; }

            /// <summary>Autoriza reconstruir o corpo de um prefab existente não gerenciado.</summary>
            public bool AdoptExisting { get; set; }

            /// <summary>Telas que instanciam este prefab e podem ser afetadas.</summary>
            public string[] DependentScreens { get; internal set; } = new string[0];

            public bool CanImport { get; internal set; }
        }

        /// <summary>Lê e valida o pacote. Não escreve nada no projeto.</summary>
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

            try
            {
                package = UIExportPackage.Read(packagePath);
            }
            catch (UIExportException error)
            {
                report.Error("package/invalid", error.Message);
                return plan;
            }

            return PrepareFromPackage(package, plan);
        }

        /// <summary>
        /// Lê e valida um pacote já em memória. Não escreve nada no projeto.
        /// </summary>
        /// <remarks>
        /// Existe para o <see cref="KitBatchImporter"/>: um <c>.uikitset</c> não tem um
        /// arquivo <c>.uikit</c> por componente no disco, só o zip inteiro já lido — sem este
        /// overload, importar em lote exigiria escrever cada componente num arquivo temporário
        /// só para <see cref="UIExportPackage.Read"/> conseguir ler de novo.
        /// </remarks>
        /// <param name="virtualPackagePath">
        /// Usado só para exibição e para reanalisar (<see cref="Plan.PackagePath"/>) — nada no
        /// import consome isto como caminho de arquivo de verdade. Um valor como
        /// <c>"MeuKit.uikitset::components/Button_Primary/kit.json"</c> serve para o dev
        /// identificar de qual entrada do lote um erro veio.
        /// </param>
        public static Plan Prepare(
            UIExportPackage package,
            string virtualPackagePath,
            UIImportSettings settings = null)
        {
            var report = new ImportReport();
            var plan = new Plan
            {
                PackagePath = virtualPackagePath,
                Report = report,
                Settings = settings ?? UIImportSettings.LoadOrDefault(),
                CanImport = false,
            };

            if (package == null)
            {
                report.Error("package/invalid", "nenhum pacote informado.");
                return plan;
            }

            return PrepareFromPackage(package, plan);
        }

        private static Plan PrepareFromPackage(UIExportPackage package, Plan plan)
        {
            ImportReport report = plan.Report;
            IRDocument document;

            try
            {
                document = IRReader.Read(package.Json);
            }
            catch (UIExportException error)
            {
                report.Error("package/invalid", error.Message);
                return plan;
            }

            if (!package.IsKit)
            {
                report.Error(
                    "package/not-a-kit",
                    "Este é um pacote de tela. Use a aba de import de tela para ele.");
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

            if (document.Kit == null || string.IsNullOrWhiteSpace(document.Kit.CanonicalName))
            {
                report.Error(
                    "kit/missing-header",
                    "O pacote tem kit.json mas não declara o nome canônico do componente.");
                return plan;
            }

            report.IngestLint(document.Lint);

            string canonical = document.Kit.CanonicalName;
            string fileName = UIKitGenerator.FileNameFor(canonical);

            plan.Package = package;
            plan.Document = document;
            plan.CanonicalName = canonical;
            plan.PrefabPath = $"{plan.Settings.GeneratedRoot}/Kit/{fileName}.prefab";
            plan.SpritesFolder = $"{plan.Settings.GeneratedRoot}/Kit/Sprites";

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(plan.PrefabPath);

            if (existing != null && !IsManagedByTool(existing))
            {
                plan.NeedsAdoption = true;
                report.Warn(
                    "kit/needs-adoption",
                    $"Já existe um prefab em '{plan.PrefabPath}' que não foi gerado a partir do " +
                    "Figma — provavelmente o kit placeholder, ou um prefab seu. Adotar reconstrói " +
                    "o corpo dele a partir do componente do Figma, e ajustes internos que você " +
                    "tenha feito ali se perdem. A referência do prefab e o rect da raiz " +
                    "sobrevivem, então as telas continuam apontando para ele.");
            }

            if (existing != null && !VariantMatches(existing, document, report))
            {
                return plan;
            }

            plan.DependentScreens = FindDependentScreens(plan.Settings, plan.PrefabPath);
            plan.Diff = ImportDiff.Compute(document, plan.PrefabPath);
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

            if (plan.NeedsAdoption && !plan.AdoptExisting)
            {
                report.Error(
                    "kit/adoption-required",
                    $"'{plan.CanonicalName}' já existe e não é gerenciado pela ferramenta. " +
                    "Confirme a adoção para o import seguir.");
                return report;
            }

            try
            {
                AssetFolders.Ensure($"{plan.Settings.GeneratedRoot}/Kit");

                SpriteImporter.Result sprites = SpriteImporter.Import(
                    plan.Document,
                    plan.Package,
                    plan.SpritesFolder,
                    plan.Settings,
                    report);

                // O resolver existe para instâncias aninhadas dentro do componente; um
                // componente que não tem nenhuma simplesmente não o consulta.
                ComponentResolver resolver = ComponentResolver.Build(plan.Settings, report);

                var builder = new PrefabBuilder(plan.Settings, resolver, sprites, report);
                builder.BuildKitOrUpdate(plan.Document, plan.PrefabPath);

                AssetDatabase.SaveAssets();

                report.Info("import/done", BuildSummary(plan, sprites));
            }
            catch (UIExportException error)
            {
                report.Error("import/failed", error.Message);
            }

            return report;
        }

        /// <summary>
        /// O prefab foi gerado a partir do Figma?
        /// </summary>
        /// <remarks>
        /// O marcador é o <see cref="FigmaNodeRef"/> na raiz, que só a reconciliação escreve.
        /// Um prefab do kit placeholder ou feito à mão não tem nenhum, e seus filhos também
        /// não — reconciliar direto sobre ele acrescentaria o corpo do Figma ao lado do que já
        /// está lá, duplicando o conteúdo em vez de substituí-lo.
        /// </remarks>
        private static bool IsManagedByTool(GameObject prefab)
        {
            return prefab.TryGetComponent(out FigmaNodeRef reference)
                && !string.IsNullOrEmpty(reference.NodeId);
        }

        /// <summary>
        /// Recusa um pacote exportado de outra variante do mesmo componente.
        /// </summary>
        /// <remarks>
        /// Cada variante do Figma tem ids de node próprios. Trocar a variante de origem
        /// trocaria <b>todos</b> os ids de uma vez, e a reconciliação, sem reencontrar nada,
        /// recriaria o prefab inteiro — mudando os fileIDs que todas as telas do jogo
        /// referenciam. Como o id da raiz é o id da variante, comparar com ele basta e
        /// dispensa guardar o dado em outro lugar.
        /// </remarks>
        private static bool VariantMatches(GameObject existing, IRDocument document, ImportReport report)
        {
            if (!existing.TryGetComponent(out FigmaNodeRef reference)) return true;
            if (string.IsNullOrEmpty(reference.NodeId)) return true;
            if (string.Equals(reference.NodeId, document.Root.Id, System.StringComparison.Ordinal))
            {
                return true;
            }

            report.Error(
                "kit/variant-changed",
                $"Este pacote saiu de uma variante diferente da que gerou o prefab atual " +
                $"('{reference.NodeId}' antes, '{document.Root.Id}' agora). Importar assim " +
                "recriaria o prefab inteiro e todas as telas que o instanciam perderiam os " +
                "ajustes feitas nele. Exporte a partir da mesma variante de antes, ou apague o " +
                "prefab de propósito para começar do zero.");

            return false;
        }

        /// <summary>
        /// Telas cujo prefab base instancia este componente.
        /// </summary>
        /// <remarks>
        /// São elas que pagam a conta de um import de kit que recrie objetos, e não estão
        /// sendo importadas agora — ninguém olharia o report delas. Contá-las antes é o que
        /// permite dizer ao dev qual é o tamanho do risco.
        /// </remarks>
        private static string[] FindDependentScreens(UIImportSettings settings, string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return new string[0];

            var found = new System.Collections.Generic.List<string>();
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { settings.GeneratedRoot });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.Equals(path, prefabPath, System.StringComparison.Ordinal)) continue;

                var candidate = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (candidate == null) continue;

                foreach (Transform child in candidate.GetComponentsInChildren<Transform>(true))
                {
                    GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                    if (source == null) continue;

                    if (string.Equals(
                            AssetDatabase.GetAssetPath(source),
                            prefabPath,
                            System.StringComparison.Ordinal))
                    {
                        found.Add(path);
                        break;
                    }
                }
            }

            found.Sort(System.StringComparer.Ordinal);
            return found.ToArray();
        }

        private static string BuildSummary(Plan plan, SpriteImporter.Result sprites)
        {
            var text = new StringBuilder();

            text.Append($"'{plan.CanonicalName}' importado como {plan.Document.Kit.Role}. ");
            text.Append($"{plan.Document.Kit.Slots.Count} slot(s), ");
            text.Append($"{sprites.Written} sprite(s) gravado(s), {sprites.Reused} reusado(s).");

            if (plan.DependentScreens.Length > 0)
            {
                text.Append($" {plan.DependentScreens.Length} tela(s) instanciam este componente.");
            }

            return text.ToString();
        }
    }
}
