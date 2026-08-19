using System.Collections.Generic;
using System.IO;
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
        private const string LastFolderKey = "Arvore.UIExporter.LastFolder";

        private string packagePath;
        private ScreenImporter.Plan plan;
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
                $"Selecione o arquivo .{PackageExtension} que o designer exportou do Figma. " +
                "Nada é escrito no projeto até você confirmar.",
                MessageType.None);
        }

        private void PickPackage()
        {
            string startFolder = EditorPrefs.GetString(LastFolderKey, Application.dataPath);

            string chosen = EditorUtility.OpenFilePanel(
                "Escolher pacote .uiexport",
                Directory.Exists(startFolder) ? startFolder : Application.dataPath,
                PackageExtension);

            if (string.IsNullOrEmpty(chosen))
            {
                return;
            }

            EditorPrefs.SetString(LastFolderKey, Path.GetDirectoryName(chosen));

            packagePath = chosen;
            lastReport = null;
            showDiffDetails = false;

            plan = ScreenImporter.Prepare(packagePath);

            if (!plan.CanImport)
            {
                lastReport = plan.Report;
            }
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
                EditorGUILayout.HelpBox(
                    $"{plan.Diff.Removed.Count} objeto(s) serão REMOVIDOS do prefab base. " +
                    "Se o dev tinha scripts ou referências pendurados nesses objetos, vão " +
                    "embora com eles.",
                    MessageType.Warning);

                showDiffDetails = EditorGUILayout.Foldout(showDiffDetails, "Ver o que será removido");

                if (showDiffDetails)
                {
                    EditorGUI.indentLevel++;
                    foreach (string name in plan.Diff.Removed)
                    {
                        EditorGUILayout.LabelField("• " + name);
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

                if (GUILayout.Button(plan.Diff.IsDestructive ? "Importar (com remoções)" : "Importar"))
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
                bool confirmed = EditorUtility.DisplayDialog(
                    "Confirmar import",
                    $"{plan.Diff.Removed.Count} objeto(s) serão removidos do prefab base de " +
                    $"'{plan.ScreenName}'.\n\nTrabalho pendurado nesses objetos específicos " +
                    "será perdido. Continuar?",
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
