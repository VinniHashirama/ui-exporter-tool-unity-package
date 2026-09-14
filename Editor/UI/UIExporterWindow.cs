using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Arvore.UIExporter.Editor
{
    /// <summary>
    /// Janela do importador: escolher o pacote, ver o que vai mudar, confirmar, ler o report.
    /// </summary>
    /// <remarks>
    /// O fluxo é deliberadamente de dois passos. Ler o pacote e calcular o diff não escreve
    /// nada; só o botão de importar comete. Remoção é a única operação que destrói trabalho do
    /// dev, então ela precisa ser vista antes de acontecer — não descoberta depois.
    /// </remarks>
    public sealed class UIExporterWindow : EditorWindow
    {
        private const string PackageExtension = "uiexport";
        private const string KitExtension = "uikit";
        private const string LastFolderKey = "Arvore.UIExporter.LastFolder";

        private string packagePath;
        private ScreenImporter.Plan plan;
        private KitImporter.Plan kitPlan;
        private ImportReport lastReport;
        private Vector2 scroll;
        private bool showDiffDetails;

        [MenuItem("Window/Arvore/UI Exporter", priority = 10)]
        public static void Open()
        {
            UIExporterWindow window = GetWindow<UIExporterWindow>();
            window.titleContent = new GUIContent("UI Exporter");
            window.minSize = new Vector2(420f, 380f);
            window.Show();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawPackagePicker();
            EditorGUILayout.Space(8f);

            if (plan != null)
            {
                DrawPlan();
                EditorGUILayout.Space(8f);
            }

            if (kitPlan != null)
            {
                DrawKitPlan();
                EditorGUILayout.Space(8f);
            }

            if (lastReport != null)
            {
                DrawReport();
            }

            EditorGUILayout.Space(12f);
            DrawFooter();

            EditorGUILayout.EndScrollView();
        }

        private void DrawPackagePicker()
        {
            EditorGUILayout.LabelField("Pacote", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField(
                    string.IsNullOrEmpty(packagePath) ? "nenhum selecionado" : Path.GetFileName(packagePath));
                EditorGUI.EndDisabledGroup();

                if (GUILayout.Button("Escolher...", GUILayout.Width(90f)))
                {
                    PickPackage();
                }
            }

            EditorGUILayout.HelpBox(
                $".{PackageExtension} é uma tela; .{KitExtension} é um componente do kit. " +
                "Nada é escrito no projeto até você confirmar.",
                MessageType.None);
        }

        private void PickPackage()
        {
            string startFolder = EditorPrefs.GetString(LastFolderKey, Application.dataPath);

            string chosen = EditorUtility.OpenFilePanelWithFilters(
                "Escolher pacote do Figma",
                Directory.Exists(startFolder) ? startFolder : Application.dataPath,
                new[]
                {
                    "Pacotes do UI Exporter", $"{PackageExtension},{KitExtension}",
                    "Tela", PackageExtension,
                    "Componente do kit", KitExtension,
                });

            if (string.IsNullOrEmpty(chosen))
            {
                return;
            }

            EditorPrefs.SetString(LastFolderKey, Path.GetDirectoryName(chosen));

            packagePath = chosen;
            lastReport = null;
            showDiffDetails = false;
            plan = null;
            kitPlan = null;

            // A extensão roteia, não o conteúdo: um pacote nunca deve ser importado como a
            // coisa errada por causa de um campo ausente ou inesperado.
            bool isKit = Path.GetExtension(chosen)
                .TrimStart('.')
                .Equals(KitExtension, System.StringComparison.OrdinalIgnoreCase);

            if (isKit)
            {
                kitPlan = KitImporter.Prepare(packagePath);
                if (!kitPlan.CanImport) lastReport = kitPlan.Report;
                return;
            }

            plan = ScreenImporter.Prepare(packagePath);
            if (!plan.CanImport) lastReport = plan.Report;
        }

        private void DrawKitPlan()
        {
            EditorGUILayout.LabelField("O que vai acontecer", EditorStyles.boldLabel);

            if (!kitPlan.CanImport)
            {
                EditorGUILayout.HelpBox(
                    "O pacote não passou na validação. Veja os erros abaixo.",
                    MessageType.Error);
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Componente", kitPlan.CanonicalName);
                EditorGUILayout.LabelField("Papel", kitPlan.Document.Kit.Role.ToString());
                EditorGUILayout.LabelField(
                    "Tamanho de design",
                    $"{kitPlan.Document.Canvas.Width} x {kitPlan.Document.Canvas.Height}");
                EditorGUILayout.LabelField("Prefab", kitPlan.PrefabPath);
                EditorGUILayout.LabelField("Slots", string.Join(", ", SlotNames(kitPlan)));
                EditorGUILayout.LabelField("Mudanças", kitPlan.Diff.Summary());
            }

            if (kitPlan.DependentScreens.Length > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{kitPlan.DependentScreens.Length} tela(s) instanciam este componente e " +
                    "vão refletir a mudança. Elas não estão sendo importadas agora, então " +
                    "confira o resultado nelas depois.",
                    MessageType.Info);
            }

            if (kitPlan.NeedsAdoption)
            {
                EditorGUILayout.HelpBox(
                    "Já existe um prefab neste nome que não foi gerado a partir do Figma — " +
                    "provavelmente o kit placeholder. Adotar reconstrói o corpo dele a partir " +
                    "do componente do Figma; ajustes internos que você tenha feito ali se " +
                    "perdem. A referência do prefab sobrevive, então as telas continuam " +
                    "apontando para ele.",
                    MessageType.Warning);

                kitPlan.AdoptExisting = EditorGUILayout.ToggleLeft(
                    "Entendi, adotar o prefab existente",
                    kitPlan.AdoptExisting);
            }

            EditorGUILayout.Space(4f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reanalisar", GUILayout.Width(100f)))
                {
                    bool adopt = kitPlan.AdoptExisting;
                    kitPlan = KitImporter.Prepare(packagePath);
                    kitPlan.AdoptExisting = adopt;
                    lastReport = kitPlan.CanImport ? null : kitPlan.Report;
                }

                EditorGUI.BeginDisabledGroup(kitPlan.NeedsAdoption && !kitPlan.AdoptExisting);

                if (GUILayout.Button("Importar componente"))
                {
                    RunKitImport();
                }

                EditorGUI.EndDisabledGroup();
            }
        }

        private static string[] SlotNames(KitImporter.Plan source)
        {
            var names = new List<string>();
            foreach (IRKitSlot slot in source.Document.Kit.Slots)
            {
                if (!string.IsNullOrEmpty(slot?.Name)) names.Add(slot.Name);
            }

            return names.Count > 0 ? names.ToArray() : new[] { "nenhum" };
        }

        private void RunKitImport()
        {
            try
            {
                EditorUtility.DisplayProgressBar(
                    "UI Exporter",
                    $"Importando {kitPlan.CanonicalName}...",
                    0.5f);

                lastReport = KitImporter.Execute(kitPlan);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kitPlan.PrefabPath);
            if (prefab != null) EditorGUIUtility.PingObject(prefab);

            kitPlan = KitImporter.Prepare(packagePath);
        }

        private void DrawPlan()
        {
            EditorGUILayout.LabelField("O que vai acontecer", EditorStyles.boldLabel);

            if (!plan.CanImport)
            {
                EditorGUILayout.HelpBox(
                    "O pacote não passou na validação. Veja os erros abaixo.",
                    MessageType.Error);
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Tela", plan.ScreenName);
                EditorGUILayout.LabelField(
                    "Resolução de design",
                    $"{plan.Document.Canvas.Width} x {plan.Document.Canvas.Height}");
                EditorGUILayout.LabelField("Prefab base", plan.BasePrefabPath);
                EditorGUILayout.LabelField("Variant do dev", plan.VariantPath);
                EditorGUILayout.LabelField("Mudanças", plan.Diff.Summary());
            }

            if (plan.Diff.IsDestructive)
            {
                var warning = new StringBuilder();

                if (plan.Diff.Removed.Count > 0)
                {
                    warning.Append($"{plan.Diff.Removed.Count} objeto(s) serão REMOVIDOS do ");
                    warning.Append("prefab base. ");
                }

                if (plan.Diff.Recreated.Count > 0)
                {
                    warning.Append($"{plan.Diff.Recreated.Count} objeto(s) trocaram de prefab do ");
                    warning.Append("kit e serão RECRIADOS. ");
                }

                warning.Append("Se o dev tinha scripts ou referências pendurados nesses objetos, ");
                warning.Append("vão embora com eles.");

                EditorGUILayout.HelpBox(warning.ToString(), MessageType.Warning);

                showDiffDetails = EditorGUILayout.Foldout(showDiffDetails, "Ver o que será afetado");

                if (showDiffDetails)
                {
                    EditorGUI.indentLevel++;

                    foreach (string name in plan.Diff.Removed)
                    {
                        EditorGUILayout.LabelField("• " + name + "  (removido)");
                    }

                    foreach (string name in plan.Diff.Recreated)
                    {
                        EditorGUILayout.LabelField("• " + name + "  (recriado)");
                    }

                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.Space(4f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reanalisar", GUILayout.Width(100f)))
                {
                    plan = ScreenImporter.Prepare(packagePath);
                    lastReport = plan.CanImport ? null : plan.Report;
                }

                GUI.backgroundColor = plan.Diff.IsDestructive ? new Color(1f, 0.85f, 0.6f) : Color.white;

                if (GUILayout.Button(plan.Diff.IsDestructive ? "Importar (com perdas)" : "Importar"))
                {
                    RunImport();
                }

                GUI.backgroundColor = Color.white;
            }
        }

        private void RunImport()
        {
            if (plan.Diff.IsDestructive)
            {
                var detail = new StringBuilder();

                if (plan.Diff.Removed.Count > 0)
                {
                    detail.AppendLine(
                        $"{plan.Diff.Removed.Count} objeto(s) serão removidos do prefab base de " +
                        $"'{plan.ScreenName}'.");
                }

                if (plan.Diff.Recreated.Count > 0)
                {
                    detail.AppendLine(
                        $"{plan.Diff.Recreated.Count} objeto(s) trocaram de prefab do kit e serão " +
                        "destruídos e refeitos.");
                }

                bool confirmed = EditorUtility.DisplayDialog(
                    "Confirmar import",
                    detail +
                    "\nTrabalho pendurado nesses objetos específicos será perdido. Continuar?",
                    "Importar",
                    "Cancelar");

                if (!confirmed)
                {
                    return;
                }
            }

            try
            {
                EditorUtility.DisplayProgressBar("UI Exporter", $"Importando {plan.ScreenName}...", 0.5f);
                lastReport = ScreenImporter.Execute(plan);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // O diff antigo já não descreve o estado do projeto; recalcular evita a janela
            // continuar prometendo remoções que já aconteceram.
            plan = ScreenImporter.Prepare(packagePath);

            var variant = AssetDatabase.LoadAssetAtPath<GameObject>(plan.VariantPath);
            if (variant != null)
            {
                Selection.activeObject = variant;
                EditorGUIUtility.PingObject(variant);
            }
        }

        private void DrawReport()
        {
            EditorGUILayout.LabelField("Relatório", EditorStyles.boldLabel);

            IReadOnlyList<ImportEntry> entries = lastReport.Entries;

            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox("Import sem observações.", MessageType.Info);
                return;
            }

            foreach (ImportEntry entry in entries)
            {
                MessageType type;
                switch (entry.Severity)
                {
                    case ImportSeverity.Error:
                        type = MessageType.Error;
                        break;
                    case ImportSeverity.Warning:
                        type = MessageType.Warning;
                        break;
                    default:
                        type = MessageType.Info;
                        break;
                }

                string prefix = string.IsNullOrEmpty(entry.NodeName)
                    ? string.Empty
                    : entry.NodeName + " — ";

                EditorGUILayout.HelpBox($"{prefix}{entry.Message}\n[{entry.Rule}]", type);
            }

            if (GUILayout.Button("Copiar relatório"))
            {
                EditorGUIUtility.systemCopyBuffer = lastReport.ToText();
            }
        }

        private void DrawFooter()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Import Settings"))
                {
                    UIImportSettings settings = UIImportSettings.LoadOrDefault();

                    if (AssetDatabase.Contains(settings))
                    {
                        Selection.activeObject = settings;
                        EditorGUIUtility.PingObject(settings);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog(
                            "Import Settings",
                            "Este projeto ainda não tem um asset de Import Settings, então o " +
                            "importador está usando os defaults.\n\nCrie um em " +
                            "Assets > Create > Arvore > UI Exporter > Import Settings para " +
                            "configurar caminhos, Font Map e o sprite de canto arredondado.",
                            "Ok");
                    }
                }

                if (GUILayout.Button("Gerar kit placeholder"))
                {
                    EditorApplication.ExecuteMenuItem("Window/Arvore/UI Exporter/Gerar kit placeholder");
                }
            }
        }
    }
}
