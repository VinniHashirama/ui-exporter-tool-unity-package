using System.Collections.Generic;
using System.Text;

namespace Arvore.UIExporter.Editor
{
    public enum ImportSeverity
    {
        Error,
        Warning,
        Info,
    }

    public readonly struct ImportEntry
    {
        public ImportEntry(ImportSeverity severity, string rule, string message, string nodeName)
        {
            Severity = severity;
            Rule = rule;
            Message = message;
            NodeName = nodeName;
        }

        public ImportSeverity Severity { get; }

        public string Rule { get; }

        public string Message { get; }

        public string NodeName { get; }
    }

    /// <summary>
    /// Acumula tudo que o dev precisa saber sobre um import.
    /// </summary>
    /// <remarks>
    /// O report é o que separa "a ferramenta funcionou" de "a ferramenta é confiável".
    /// Componente não mapeado, fonte faltando, aproximação de layout e node removido são
    /// todos casos em que o import <i>tem</i> sucesso mas o resultado não é o que o
    /// designer desenhou — sem relatar, isso vira bug misterioso semanas depois.
    /// <para>
    /// Os diagnósticos do lado do designer também entram aqui, via
    /// <see cref="IngestLint"/>: quem importa precisa ver que a tela já saiu do Figma com
    /// avisos, senão vai investigar na Unity um problema cuja causa está no arquivo.
    /// </para>
    /// </remarks>
    public sealed class ImportReport
    {
        private readonly List<ImportEntry> entries = new List<ImportEntry>();
        private readonly HashSet<string> seen = new HashSet<string>();

        public IReadOnlyList<ImportEntry> Entries => entries;

        public bool HasErrors { get; private set; }

        public int WarningCount { get; private set; }

        public void Add(ImportSeverity severity, string rule, string message, string nodeName = null)
        {
            // Uma tela grande com a mesma fonte faltando em 40 textos deve render uma
            // linha, não quarenta — senão o report deixa de ser lido.
            string key = severity + "|" + rule + "|" + message;
            if (!seen.Add(key))
            {
                return;
            }

            entries.Add(new ImportEntry(severity, rule, message, nodeName));

            if (severity == ImportSeverity.Error)
            {
                HasErrors = true;
            }
            else if (severity == ImportSeverity.Warning)
            {
                WarningCount++;
            }
        }

        public void Error(string rule, string message, string nodeName = null)
        {
            Add(ImportSeverity.Error, rule, message, nodeName);
        }

        public void Warn(string rule, string message, string nodeName = null)
        {
            Add(ImportSeverity.Warning, rule, message, nodeName);
        }

        public void Info(string rule, string message, string nodeName = null)
        {
            Add(ImportSeverity.Info, rule, message, nodeName);
        }

        /// <summary>Traz os diagnósticos que o plugin do Figma já havia registrado.</summary>
        public void IngestLint(IEnumerable<IRDiagnostic> lint)
        {
            if (lint == null)
            {
                return;
            }

            foreach (IRDiagnostic diagnostic in lint)
            {
                ImportSeverity severity;
                switch (diagnostic.Severity)
                {
                    case DiagnosticSeverity.Error:
                        severity = ImportSeverity.Error;
                        break;
                    case DiagnosticSeverity.Warning:
                        severity = ImportSeverity.Warning;
                        break;
                    default:
                        severity = ImportSeverity.Info;
                        break;
                }

                Add(severity, "figma/" + diagnostic.Rule, diagnostic.Message, diagnostic.NodeName);
            }
        }

        public string ToText()
        {
            if (entries.Count == 0)
            {
                return "Import sem observações.";
            }

            var builder = new StringBuilder();
            AppendGroup(builder, ImportSeverity.Error, "ERROS");
            AppendGroup(builder, ImportSeverity.Warning, "AVISOS");
            AppendGroup(builder, ImportSeverity.Info, "INFO");
            return builder.ToString().TrimEnd();
        }

        private void AppendGroup(StringBuilder builder, ImportSeverity severity, string title)
        {
            bool wroteTitle = false;

            foreach (ImportEntry entry in entries)
            {
                if (entry.Severity != severity)
                {
                    continue;
                }

                if (!wroteTitle)
                {
                    builder.Append(title).Append('\n');
                    wroteTitle = true;
                }

                builder.Append("  [").Append(entry.Rule).Append("] ");

                if (!string.IsNullOrEmpty(entry.NodeName))
                {
                    builder.Append(entry.NodeName).Append(": ");
                }

                builder.Append(entry.Message).Append('\n');
            }

            if (wroteTitle)
            {
                builder.Append('\n');
            }
        }
    }
}
