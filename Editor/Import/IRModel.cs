using System.Collections.Generic;

namespace Arvore.UIExporter.Editor
{
    // Espelho de https://github.com/VinniHashirama/ui-exporter-tool-figma-plugin/blob/main/schema/uiir.schema.json. Em caso de divergência, o schema manda.
    //
    // Os nomes das propriedades são PascalCase e a ponte com o camelCase do JSON é feita
    // pelo CamelCaseNamingStrategy configurado em IRReader — nenhum [JsonProperty] à mão.
    // Os enums são traduzidos pelo WireEnumConverter.

    public enum NodeKind
    {
        Frame,
        Group,
        Text,
        Image,
        Instance,
    }

    public enum ConstraintMode
    {
        Min,
        Center,
        Max,
        Stretch,
        Scale,
    }

    public enum SizingMode
    {
        Fixed,
        Hug,
        Fill,
    }

    public enum LayoutMode
    {
        Horizontal,
        Vertical,
        Wrap,
    }

    public enum PrimaryAlign
    {
        Min,
        Center,
        Max,
        SpaceBetween,
    }

    public enum CounterAlign
    {
        Min,
        Center,
        Max,
        Baseline,
    }

    public enum FillType
    {
        Solid,
        Image,
    }

    public enum ImageScaleMode
    {
        Fill,
        Fit,
        Stretch,
        Tile,
    }

    public enum StrokeAlign
    {
        Inside,
        Outside,
        Center,
    }

    public enum LineHeightUnit
    {
        Pixels,
        Percent,
        Auto,
    }

    public enum SpacingUnit
    {
        Pixels,
        Percent,
    }

    public enum TextAlignH
    {
        Left,
        Center,
        Right,
        Justified,
    }

    public enum TextAlignV
    {
        Top,
        Center,
        Bottom,
    }

    public enum TextAutoResize
    {
        None,
        Height,
        WidthAndHeight,
    }

    public enum TextTruncation
    {
        Disabled,
        Ellipsis,
    }

    public enum TextCasing
    {
        Original,
        Upper,
        Lower,
        Title,
    }

    public enum TextDecorationMode
    {
        None,
        Underline,
        Strikethrough,
    }

    public enum DiagnosticSeverity
    {
        Error,
        Warning,
        Info,
    }

    public enum ScreenOrientation
    {
        Portrait,
        Landscape,
    }

    public sealed class IRDocument
    {
        public string SchemaVersion { get; set; }

        public IRSource Source { get; set; }

        public IRCanvas Canvas { get; set; }

        /// <summary>
        /// Presente apenas no pacote de componente. Ausente = pacote de tela.
        /// </summary>
        public IRKit Kit { get; set; }

        public IRTokens Tokens { get; set; }

        public List<IRAsset> Assets { get; set; } = new List<IRAsset>();

        public List<IRDiagnostic> Lint { get; set; } = new List<IRDiagnostic>();

        public IRNode Root { get; set; }
    }

    /// <summary>Papel do componente: qual esqueleto de comportamento montar na Unity.</summary>
    public enum KitRole
    {
        Button,
        Toggle,
        Container,
        Display,
        Icon,
        Image,
    }

    /// <summary>Cabeçalho do pacote de componente.</summary>
    public sealed class IRKit
    {
        public string CanonicalName { get; set; }

        public KitRole Role { get; set; }

        /// <summary>
        /// Variante do Figma de onde o pacote saiu.
        /// </summary>
        /// <remarks>
        /// Cada variante tem ids de node próprios. Exportar de uma variante diferente depois
        /// trocaria todos os ids de uma vez, e a reconciliação recriaria o prefab inteiro —
        /// levando junto tudo que o dev tivesse pendurado nele. Por isso o importador guarda
        /// este valor e recusa quando ele muda.
        /// </remarks>
        public string SourceVariantId { get; set; }

        public List<string> IgnoredVariants { get; set; } = new List<string>();

        public List<IRKitSlot> Slots { get; set; } = new List<IRKitSlot>();
    }

    public sealed class IRKitSlot
    {
        public string Name { get; set; }

        public string NodeId { get; set; }
    }

    public sealed class IRSource
    {
        public string FileKey { get; set; }

        public string FileName { get; set; }

        public string PageName { get; set; }

        /// <summary>
        /// Mantido como string de propósito: converter para DateTime na leitura só
        /// introduz surpresa de fuso, e o valor é usado apenas para exibição no report.
        /// </summary>
        public string ExportedAt { get; set; }

        public string PluginVersion { get; set; }
    }

    public sealed class IRCanvas
    {
        public float Width { get; set; }

        public float Height { get; set; }

        public ScreenOrientation? Orientation { get; set; }
    }

    public sealed class IRTokens
    {
        public Dictionary<string, string> Colors { get; set; }

        public Dictionary<string, IRTypographyToken> Typography { get; set; }

        public Dictionary<string, float> Spacing { get; set; }
    }

    public sealed class IRTypographyToken
    {
        public string Family { get; set; }

        public string Style { get; set; }

        public float Size { get; set; }

        public IRLineHeight LineHeight { get; set; }

        public float LetterSpacing { get; set; }
    }

    public sealed class IRAsset
    {
        public string Id { get; set; }

        public string File { get; set; }

        public int Scale { get; set; } = 1;

        public int Width { get; set; }

        public int Height { get; set; }

        /// <summary>[top, right, bottom, left], ou null para sprite simples.</summary>
        public float[] NineSlice { get; set; }
    }

    public sealed class IRNode
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public NodeKind Kind { get; set; }

        public IRComponentRef Component { get; set; }

        public IRRect Rect { get; set; }

        public float Rotation { get; set; }

        public float Opacity { get; set; } = 1f;

        public bool Visible { get; set; } = true;

        public bool Clip { get; set; }

        public IRConstraints Constraints { get; set; }

        public IRLayout Layout { get; set; }

        public IRLayoutChild LayoutChild { get; set; }

        public IRFill Fill { get; set; }

        public IRStroke Stroke { get; set; }

        /// <summary>[topLeft, topRight, bottomRight, bottomLeft].</summary>
        public float[] CornerRadius { get; set; }

        public IRText Text { get; set; }

        public string Bind { get; set; }

        public List<IRNode> Children { get; set; }
    }

    public sealed class IRComponentRef
    {
        public string SetKey { get; set; }

        public string CanonicalName { get; set; }

        public Dictionary<string, object> Properties { get; set; }
    }

    public sealed class IRRect
    {
        public float X { get; set; }

        public float Y { get; set; }

        public float Width { get; set; }

        public float Height { get; set; }
    }

    public sealed class IRConstraints
    {
        public ConstraintMode Horizontal { get; set; }

        public ConstraintMode Vertical { get; set; }
    }

    public sealed class IRSizing
    {
        public SizingMode Horizontal { get; set; }

        public SizingMode Vertical { get; set; }
    }

    public sealed class IRLayout
    {
        public LayoutMode Mode { get; set; }

        /// <summary>[top, right, bottom, left].</summary>
        public float[] Padding { get; set; }

        public float Spacing { get; set; }

        public PrimaryAlign PrimaryAlign { get; set; }

        public CounterAlign CounterAlign { get; set; }

        public IRSizing Sizing { get; set; }

        public bool ReverseZIndex { get; set; }
    }

    public sealed class IRLayoutChild
    {
        public IRSizing Sizing { get; set; }

        public float Grow { get; set; }
    }

    public sealed class IRFill
    {
        public FillType Type { get; set; }

        public string Color { get; set; }

        public string AssetId { get; set; }

        public ImageScaleMode ScaleMode { get; set; } = ImageScaleMode.Fit;

        public string Token { get; set; }
    }

    public sealed class IRStroke
    {
        public string Color { get; set; }

        public float Weight { get; set; }

        public StrokeAlign Align { get; set; } = StrokeAlign.Inside;

        public string Token { get; set; }
    }

    public sealed class IRLineHeight
    {
        public LineHeightUnit Unit { get; set; }

        public float Value { get; set; }
    }

    public sealed class IRLetterSpacing
    {
        public SpacingUnit Unit { get; set; }

        public float Value { get; set; }
    }

    public sealed class IRText
    {
        public string Characters { get; set; }

        public IRFont Font { get; set; }

        public float Size { get; set; }

        public IRLineHeight LineHeight { get; set; }

        public IRLetterSpacing LetterSpacing { get; set; }

        public TextAlignH AlignHorizontal { get; set; }

        public TextAlignV AlignVertical { get; set; }

        public TextAutoResize AutoResize { get; set; } = TextAutoResize.None;

        public string Color { get; set; }

        public int? MaxLines { get; set; }

        public TextTruncation Truncation { get; set; } = TextTruncation.Disabled;

        public TextCasing Case { get; set; } = TextCasing.Original;

        public TextDecorationMode Decoration { get; set; } = TextDecorationMode.None;

        public string Token { get; set; }

        public string LocKey { get; set; }
    }

    public sealed class IRFont
    {
        public string Family { get; set; }

        public string Style { get; set; }
    }

    public sealed class IRDiagnostic
    {
        public DiagnosticSeverity Severity { get; set; }

        public string Rule { get; set; }

        public string Message { get; set; }

        public string NodeId { get; set; }

        public string NodeName { get; set; }
    }
}
