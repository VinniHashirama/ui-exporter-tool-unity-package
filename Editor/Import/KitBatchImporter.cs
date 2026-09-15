using System;
using System.Collections.Generic;

namespace Arvore.UIExporter.Editor
{
    /// <summary>
    /// Orquestra o import de um <c>.uikitset</c> inteiro: um <see cref="KitImporter.Plan"/> por
    /// componente do lote.
    /// </summary>
    /// <remarks>
    /// O ponto do lote é justamente não deixar um componente ruim travar os outros catorze
    /// bons — por isso <see cref="Prepare"/> nunca lança por causa de um componente individual
    /// (só por um problema no zip inteiro, via <see cref="UIKitSetPackage"/>), e
    /// <see cref="Execute"/> segue para o próximo plano mesmo quando um falha. Cada componente
    /// usa exatamente o mesmo <see cref="KitImporter"/> de um <c>.uikit</c> avulso — o lote só
    /// decide quais planos rodar e agrega os reports.
    /// </remarks>
    public static class KitBatchImporter
    {
        public sealed class BatchPlan
        {
            public IReadOnlyList<KitImporter.Plan> Components { get; internal set; } = Array.Empty<KitImporter.Plan>();

            public ImportReport Report { get; internal set; }

            public int ReadyCount { get; internal set; }

            public int NeedsAdoptionCount { get; internal set; }

            public int BlockedCount { get; internal set; }
        }

        /// <summary>Lê e valida o <c>.uikitset</c>. Não escreve nada no projeto.</summary>
        public static BatchPlan Prepare(string packagePath, UIImportSettings settings = null)
        {
            var report = new ImportReport();
            var batchPlan = new BatchPlan { Report = report };

            UIKitSetPackage kitSet;

            try
            {
                kitSet = UIKitSetPackage.Read(packagePath);
            }
            catch (UIExportException error)
            {
                report.Error("kitset/invalid", error.Message);
                return batchPlan;
            }

            UIImportSettings resolvedSettings = settings ?? UIImportSettings.LoadOrDefault();

            // Informativo, nunca bloqueia: um componente que já falhou ao exportar no Figma
            // não é um problema deste import, é um aviso sobre o que não veio.
            foreach (UIKitSetPackage.SkippedComponent entry in kitSet.Skipped)
            {
                report.Warn(
                    "kitset/skipped",
                    string.IsNullOrEmpty(entry.Reason)
                        ? $"'{entry.Name}' não foi exportado."
                        : $"'{entry.Name}' não foi exportado: {entry.Reason}");
            }

            var plans = new List<KitImporter.Plan>(kitSet.Components.Count);
            int ready = 0;
            int needsAdoption = 0;
            int blocked = 0;

            foreach (UIKitSetPackage.Component component in kitSet.Components)
            {
                string virtualPath = $"{System.IO.Path.GetFileName(packagePath)}::{component.EntryPath}";

                KitImporter.Plan plan = KitImporter.Prepare(component.Package, virtualPath, resolvedSettings);
                plans.Add(plan);

                if (plan.CanImport && !plan.NeedsAdoption)
                {
                    ready++;
                }
                else if (plan.NeedsAdoption)
                {
                    needsAdoption++;
                }
                else
                {
                    blocked++;
                }
            }

            batchPlan.Components = plans;
            batchPlan.ReadyCount = ready;
            batchPlan.NeedsAdoptionCount = needsAdoption;
            batchPlan.BlockedCount = blocked;

            return batchPlan;
        }

        /// <summary>
        /// Comete o import de cada componente pronto do lote.
        /// </summary>
        /// <param name="adoptAll">
        /// Quando false (o padrão), componentes que precisam de adoção são pulados em vez de
        /// travar o lote — o dev resolve esses individualmente pela aba de import de
        /// componente, onde a decisão de adotar é explícita por componente. Quando true, todos
        /// os componentes pendentes de adoção são adotados sem distinção.
        /// </param>
        public static ImportReport Execute(BatchPlan batchPlan, bool adoptAll = false)
        {
            var aggregate = new ImportReport();

            // Os avisos de skipped[] (e um eventual erro de leitura do .uikitset inteiro) já
            // estão em batchPlan.Report desde o Prepare; sem repeti-los aqui, quem só olha o
            // relatório final do import não veria os componentes que nem chegaram a entrar no
            // lote.
            foreach (ImportEntry entry in batchPlan.Report.Entries)
            {
                aggregate.Add(entry.Severity, entry.Rule, entry.Message, entry.NodeName);
            }

            foreach (KitImporter.Plan plan in batchPlan.Components)
            {
                string label = Label(plan);

                if (!plan.CanImport)
                {
                    MergeReport(aggregate, plan.Report, label);
                    continue;
                }

                if (plan.NeedsAdoption && !adoptAll)
                {
                    aggregate.Warn(
                        "kitset/adoption-skipped",
                        $"'{label}' precisa de adoção e foi pulado no lote. Importe-o " +
                        "individualmente pela aba de componente para confirmar.");
                    continue;
                }

                if (plan.NeedsAdoption)
                {
                    plan.AdoptExisting = true;
                }

                try
                {
                    ImportReport componentReport = KitImporter.Execute(plan);
                    MergeReport(aggregate, componentReport, label);
                }
                catch (Exception error)
                {
                    // KitImporter.Execute já contém UIExportException internamente; isto é
                    // reforço contra o que ele não previu, para um componente ruim não derrubar
                    // os outros catorze do lote.
                    aggregate.Error(
                        "kitset/component-failed",
                        $"'{label}' falhou de forma inesperada: {error.Message}");
                }
            }

            return aggregate;
        }

        /// <summary>
        /// Identifica um componente do lote no report. Antes do IR ser lido (pacote inválido,
        /// schema incompatível) o nome canônico ainda não existe, então cai para o caminho
        /// virtual da entrada no zip.
        /// </summary>
        private static string Label(KitImporter.Plan plan)
        {
            return string.IsNullOrEmpty(plan.CanonicalName) ? plan.PackagePath : plan.CanonicalName;
        }

        /// <summary>
        /// Junta o report de um componente no agregado, prefixando a mensagem com o componente
        /// de origem.
        /// </summary>
        /// <remarks>
        /// <see cref="ImportReport.Add"/> deduplica por severidade+regra+mensagem, sem olhar
        /// o nome do node — dois componentes diferentes que caiam na mesma regra com o mesmo
        /// texto (ex.: dois componentes sem canonicalName, que geram a mensagem idêntica de
        /// "kit/missing-header") colapsariam num só e o segundo desapareceria do relatório do
        /// lote. Prefixar o componente na própria mensagem garante que cada um fique com sua
        /// linha.
        /// </remarks>
        private static void MergeReport(ImportReport aggregate, ImportReport source, string componentLabel)
        {
            foreach (ImportEntry entry in source.Entries)
            {
                aggregate.Add(entry.Severity, entry.Rule, $"[{componentLabel}] {entry.Message}", entry.NodeName);
            }
        }
    }
}
