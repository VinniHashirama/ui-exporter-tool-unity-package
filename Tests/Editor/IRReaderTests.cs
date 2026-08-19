using Arvore.UIExporter.Editor;
using NUnit.Framework;

namespace Arvore.UIExporter.Tests
{
    public sealed class SchemaGateTests
    {
        [Test]
        public void SameMajorAndMinor_IsCompatible()
        {
            SchemaCompatibility result = SchemaGate.Check("1.0.0", out string message);

            Assert.AreEqual(SchemaCompatibility.Compatible, result);
            Assert.IsNull(message);
        }

        [Test]
        public void PatchBump_IsCompatible()
        {
            Assert.AreEqual(
                SchemaCompatibility.Compatible,
                SchemaGate.Check("1.0.7", out _));
        }

        [Test]
        public void NewerMinor_ImportsWithWarning()
        {
            SchemaCompatibility result = SchemaGate.Check("1.9.0", out string message);

            Assert.AreEqual(SchemaCompatibility.NewerMinor, result);
            StringAssert.Contains("mais nova", message);
        }

        [Test]
        public void DifferentMajor_IsRefused()
        {
            SchemaCompatibility result = SchemaGate.Check("2.0.0", out string message);

            Assert.AreEqual(SchemaCompatibility.Incompatible, result);
            StringAssert.Contains("Atualize o pacote", message);
        }

        [Test]
        public void OlderMajor_TellsDesignerToReexport()
        {
            SchemaCompatibility result = SchemaGate.Check("0.9.0", out string message);

            Assert.AreEqual(SchemaCompatibility.Incompatible, result);
            StringAssert.Contains("novo export", message);
        }

        [Test]
        public void MalformedVersion_IsUnreadable()
        {
            Assert.AreEqual(SchemaCompatibility.Unreadable, SchemaGate.Check("1.0", out _));
            Assert.AreEqual(SchemaCompatibility.Unreadable, SchemaGate.Check("abc", out _));
            Assert.AreEqual(SchemaCompatibility.Unreadable, SchemaGate.Check("", out _));
            Assert.AreEqual(SchemaCompatibility.Unreadable, SchemaGate.Check(null, out _));
            Assert.AreEqual(SchemaCompatibility.Unreadable, SchemaGate.Check("1.0.-1", out _));
        }
    }

    public sealed class IRReaderTests
    {
        private const string MinimalDocument = @"{
          ""schemaVersion"": ""1.0.0"",
          ""source"": {
            ""fileKey"": ""K"", ""fileName"": ""F"",
            ""exportedAt"": ""2026-01-01T00:00:00.000Z"", ""pluginVersion"": ""0.1.0""
          },
          ""canvas"": { ""width"": 1080, ""height"": 1920, ""orientation"": ""portrait"" },
          ""assets"": [],
          ""lint"": [],
          ""root"": {
            ""id"": ""0:1"", ""name"": ""HomeMenu"", ""kind"": ""frame"",
            ""rect"": { ""x"": 0, ""y"": 0, ""width"": 1080, ""height"": 1920 }
          }
        }";

        [Test]
        public void ReadsMinimalDocument()
        {
            IRDocument document = IRReader.Read(MinimalDocument);

            Assert.AreEqual("1.0.0", document.SchemaVersion);
            Assert.AreEqual("HomeMenu", document.Root.Name);
            Assert.AreEqual(NodeKind.Frame, document.Root.Kind);
            Assert.AreEqual(1080f, document.Canvas.Width);
            Assert.AreEqual(ScreenOrientation.Portrait, document.Canvas.Orientation);
        }

        [Test]
        public void AppliesSchemaDefaultsForAbsentFields()
        {
            IRDocument document = IRReader.Read(MinimalDocument);

            // Ausente no JSON significa o default do schema, não zero.
            Assert.AreEqual(1f, document.Root.Opacity, "opacity default");
            Assert.IsTrue(document.Root.Visible, "visible default");
            Assert.AreEqual(0f, document.Root.Rotation, "rotation default");
            Assert.IsFalse(document.Root.Clip, "clip default");
        }

        [Test]
        public void TranslatesUpperSnakeEnums()
        {
            const string json = @"{
              ""schemaVersion"": ""1.0.0"",
              ""source"": { ""fileKey"": ""K"", ""fileName"": ""F"", ""exportedAt"": ""x"", ""pluginVersion"": ""0.1.0"" },
              ""canvas"": { ""width"": 100, ""height"": 100 },
              ""root"": {
                ""id"": ""0:1"", ""name"": ""R"", ""kind"": ""frame"",
                ""rect"": { ""x"": 0, ""y"": 0, ""width"": 100, ""height"": 100 },
                ""constraints"": { ""horizontal"": ""STRETCH"", ""vertical"": ""SCALE"" },
                ""layout"": {
                  ""mode"": ""HORIZONTAL"", ""padding"": [1,2,3,4], ""spacing"": 8,
                  ""primaryAlign"": ""SPACE_BETWEEN"", ""counterAlign"": ""BASELINE"",
                  ""sizing"": { ""horizontal"": ""HUG"", ""vertical"": ""FILL"" }
                },
                ""children"": [{
                  ""id"": ""0:2"", ""name"": ""T"", ""kind"": ""text"",
                  ""rect"": { ""x"": 0, ""y"": 0, ""width"": 10, ""height"": 10 },
                  ""text"": {
                    ""characters"": ""oi"", ""font"": { ""family"": ""Nunito"", ""style"": ""Bold"" },
                    ""size"": 24, ""alignHorizontal"": ""CENTER"", ""alignVertical"": ""BOTTOM"",
                    ""autoResize"": ""WIDTH_AND_HEIGHT"", ""color"": ""#ffffff"",
                    ""truncation"": ""ELLIPSIS"", ""case"": ""UPPER""
                  }
                }]
              }
            }";

            IRDocument document = IRReader.Read(json);
            IRNode root = document.Root;

            Assert.AreEqual(ConstraintMode.Stretch, root.Constraints.Horizontal);
            Assert.AreEqual(ConstraintMode.Scale, root.Constraints.Vertical);
            Assert.AreEqual(LayoutMode.Horizontal, root.Layout.Mode);
            Assert.AreEqual(PrimaryAlign.SpaceBetween, root.Layout.PrimaryAlign);
            Assert.AreEqual(CounterAlign.Baseline, root.Layout.CounterAlign);
            Assert.AreEqual(SizingMode.Hug, root.Layout.Sizing.Horizontal);
            Assert.AreEqual(SizingMode.Fill, root.Layout.Sizing.Vertical);

            IRNode text = root.Children[0];
            Assert.AreEqual(NodeKind.Text, text.Kind);
            Assert.AreEqual(TextAlignH.Center, text.Text.AlignHorizontal);
            Assert.AreEqual(TextAlignV.Bottom, text.Text.AlignVertical);
            Assert.AreEqual(TextAutoResize.WidthAndHeight, text.Text.AutoResize);
            Assert.AreEqual(TextTruncation.Ellipsis, text.Text.Truncation);
            Assert.AreEqual(TextCasing.Upper, text.Text.Case);
        }

        [Test]
        public void ReadsPaddingAndCornerRadiusInOrder()
        {
            const string json = @"{
              ""schemaVersion"": ""1.0.0"",
              ""source"": { ""fileKey"": ""K"", ""fileName"": ""F"", ""exportedAt"": ""x"", ""pluginVersion"": ""0.1.0"" },
              ""canvas"": { ""width"": 100, ""height"": 100 },
              ""root"": {
                ""id"": ""0:1"", ""name"": ""R"", ""kind"": ""frame"",
                ""rect"": { ""x"": 0, ""y"": 0, ""width"": 100, ""height"": 100 },
                ""cornerRadius"": [8, 12, 16, 20],
                ""layout"": {
                  ""mode"": ""VERTICAL"", ""padding"": [1, 2, 3, 4], ""spacing"": 0,
                  ""primaryAlign"": ""MIN"", ""counterAlign"": ""MIN"",
                  ""sizing"": { ""horizontal"": ""FIXED"", ""vertical"": ""FIXED"" }
                }
              }
            }";

            IRNode root = IRReader.Read(json).Root;

            // padding: [top, right, bottom, left]
            Assert.AreEqual(new[] { 1f, 2f, 3f, 4f }, root.Layout.Padding);
            // cornerRadius: [topLeft, topRight, bottomRight, bottomLeft]
            Assert.AreEqual(new[] { 8f, 12f, 16f, 20f }, root.CornerRadius);
        }

        [Test]
        public void ReadsComponentPropertiesAsLooseValues()
        {
            const string json = @"{
              ""schemaVersion"": ""1.0.0"",
              ""source"": { ""fileKey"": ""K"", ""fileName"": ""F"", ""exportedAt"": ""x"", ""pluginVersion"": ""0.1.0"" },
              ""canvas"": { ""width"": 100, ""height"": 100 },
              ""root"": {
                ""id"": ""0:1"", ""name"": ""R"", ""kind"": ""instance"",
                ""rect"": { ""x"": 0, ""y"": 0, ""width"": 100, ""height"": 100 },
                ""component"": {
                  ""canonicalName"": ""Button/Primary"", ""setKey"": ""abc"",
                  ""properties"": { ""label"": ""Jogar"", ""Size"": ""L"", ""enabled"": true, ""count"": 3 }
                }
              }
            }";

            IRComponentRef component = IRReader.Read(json).Root.Component;

            Assert.AreEqual("Button/Primary", component.CanonicalName);
            Assert.AreEqual("abc", component.SetKey);
            Assert.AreEqual("Jogar", component.Properties["label"].ToString());
            Assert.AreEqual("True", component.Properties["enabled"].ToString());
        }

        [Test]
        public void IgnoresUnknownFieldsForForwardCompatibility()
        {
            // Um pacote de MINOR mais nova traz campos que esta versão não conhece;
            // ignorar é o comportamento certo, e o SchemaGate já avisou.
            const string json = @"{
              ""schemaVersion"": ""1.5.0"",
              ""source"": { ""fileKey"": ""K"", ""fileName"": ""F"", ""exportedAt"": ""x"", ""pluginVersion"": ""0.1.0"" },
              ""canvas"": { ""width"": 100, ""height"": 100 },
              ""futureField"": { ""nested"": true },
              ""root"": {
                ""id"": ""0:1"", ""name"": ""R"", ""kind"": ""frame"",
                ""rect"": { ""x"": 0, ""y"": 0, ""width"": 100, ""height"": 100 },
                ""somethingNew"": 42
              }
            }";

            Assert.DoesNotThrow(() => IRReader.Read(json));
        }

        [Test]
        public void RejectsNodeWithoutId()
        {
            const string json = @"{
              ""schemaVersion"": ""1.0.0"",
              ""source"": { ""fileKey"": ""K"", ""fileName"": ""F"", ""exportedAt"": ""x"", ""pluginVersion"": ""0.1.0"" },
              ""canvas"": { ""width"": 100, ""height"": 100 },
              ""root"": { ""name"": ""R"", ""kind"": ""frame"", ""rect"": { ""x"":0, ""y"":0, ""width"":1, ""height"":1 } }
            }";

            var error = Assert.Throws<UIExportException>(() => IRReader.Read(json));
            StringAssert.Contains("sem id", error.Message);
        }

        [Test]
        public void RejectsTextNodeWithoutTextBlock()
        {
            const string json = @"{
              ""schemaVersion"": ""1.0.0"",
              ""source"": { ""fileKey"": ""K"", ""fileName"": ""F"", ""exportedAt"": ""x"", ""pluginVersion"": ""0.1.0"" },
              ""canvas"": { ""width"": 100, ""height"": 100 },
              ""root"": { ""id"": ""0:1"", ""name"": ""R"", ""kind"": ""text"", ""rect"": { ""x"":0, ""y"":0, ""width"":1, ""height"":1 } }
            }";

            Assert.Throws<UIExportException>(() => IRReader.Read(json));
        }

        [Test]
        public void RejectsInvalidJson()
        {
            Assert.Throws<UIExportException>(() => IRReader.Read("{ isto nao e json"));
            Assert.Throws<UIExportException>(() => IRReader.Read(""));
            Assert.Throws<UIExportException>(() => IRReader.Read(null));
        }

        [Test]
        public void RejectsUnknownEnumValue()
        {
            const string json = @"{
              ""schemaVersion"": ""1.0.0"",
              ""source"": { ""fileKey"": ""K"", ""fileName"": ""F"", ""exportedAt"": ""x"", ""pluginVersion"": ""0.1.0"" },
              ""canvas"": { ""width"": 100, ""height"": 100 },
              ""root"": { ""id"": ""0:1"", ""name"": ""R"", ""kind"": ""hologram"", ""rect"": { ""x"":0, ""y"":0, ""width"":1, ""height"":1 } }
            }";

            var error = Assert.Throws<UIExportException>(() => IRReader.Read(json));
            StringAssert.Contains("hologram", error.Message);
        }

        [Test]
        public void KeepsExportedAtAsRawString()
        {
            // Sem DateParseHandling.None o Newtonsoft converteria para DateTime e o valor
            // exibido mudaria conforme o fuso da máquina de quem importa.
            IRDocument document = IRReader.Read(MinimalDocument);
            Assert.AreEqual("2026-01-01T00:00:00.000Z", document.Source.ExportedAt);
        }
    }
}
