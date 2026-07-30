using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace WritingApp
{
    internal static class DocxTextConverter
    {
        private const string MainDocumentContentType =
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
        private const string StylesContentType =
            "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml";
        private const string NumberingContentType =
            "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml";
        private const string OfficeDocumentRelationship =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
        private const string StylesRelationship =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";
        private const string NumberingRelationship =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering";

        private static readonly XNamespace Word =
            "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        internal static void ReadInto(string path, WritingAppTextBox editor)
        {
            List<DocxParagraph> paragraphs;
            using (Package package = Package.Open(path, FileMode.Open, FileAccess.Read))
            {
                Uri documentUri = FindMainDocumentUri(package);
                PackagePart documentPart = package.GetPart(documentUri);
                Dictionary<string, int> headingStyles = ReadHeadingStyles(package);
                Dictionary<string, string> numbering = ReadNumberingFormats(package);

                XDocument document;
                using (Stream stream = documentPart.GetStream(FileMode.Open, FileAccess.Read))
                    document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);

                XElement body = document.Root == null
                    ? null
                    : document.Root.Element(Word + "body");
                paragraphs = new List<DocxParagraph>();
                if (body != null)
                {
                    foreach (XElement paragraph in body.Descendants(Word + "p"))
                        paragraphs.Add(ReadParagraph(paragraph, headingStyles, numbering));
                }
            }

            editor.Text = String.Join(
                Environment.NewLine,
                paragraphs.Select(paragraph => paragraph.Text).ToArray());

            int position = 0;
            for (int index = 0; index < paragraphs.Count; index++)
            {
                DocxParagraph paragraph = paragraphs[index];
                position = FindParagraphStart(editor.Text, paragraph.Text, position);
                editor.Select(position, paragraph.Text.Length);

                if (paragraph.HeadingLevel > 0)
                {
                    int level = Math.Min(6, paragraph.HeadingLevel);
                    editor.SetNativeParagraphStyle(
                        (short)(-1 - level),
                        (byte)(level - 1));
                    editor.SelectionFont = new System.Drawing.Font(
                        "Segoe UI",
                        HeadingFontSize(level),
                        System.Drawing.FontStyle.Bold);
                }
                else
                {
                    editor.SetNativeParagraphStyle(-1, 9);
                    editor.SelectionFont = new System.Drawing.Font(
                        "Segoe UI",
                        11F,
                        System.Drawing.FontStyle.Regular);
                }

                editor.SetNativeListType(paragraph.ListType);
                position += paragraph.Text.Length;
                if (index < paragraphs.Count - 1)
                    position += 1;
            }

            editor.Select(0, 0);
        }

        internal static void Write(string path, WritingAppTextBox editor)
        {
            List<EditorParagraph> paragraphs = ReadEditorParagraphs(editor);

            using (Package package = Package.Open(path, FileMode.Create, FileAccess.ReadWrite))
            {
                var documentUri = new Uri("/word/document.xml", UriKind.Relative);
                PackagePart documentPart = package.CreatePart(
                    documentUri,
                    MainDocumentContentType,
                    CompressionOption.Normal);
                package.CreateRelationship(
                    new Uri("word/document.xml", UriKind.Relative),
                    TargetMode.Internal,
                    OfficeDocumentRelationship);

                CreateStylesPart(package, documentPart);
                CreateNumberingPart(package, documentPart);

                var body = new XElement(Word + "body");
                foreach (EditorParagraph paragraph in paragraphs)
                    body.Add(CreateParagraphElement(paragraph));

                body.Add(
                    new XElement(
                        Word + "sectPr",
                        new XElement(
                            Word + "pgSz",
                            new XAttribute(Word + "w", "11906"),
                            new XAttribute(Word + "h", "16838")),
                        new XElement(
                            Word + "pgMar",
                            new XAttribute(Word + "top", "1440"),
                            new XAttribute(Word + "right", "1440"),
                            new XAttribute(Word + "bottom", "1440"),
                            new XAttribute(Word + "left", "1440"))));

                var document = new XDocument(
                    new XDeclaration("1.0", "UTF-8", "yes"),
                    new XElement(
                        Word + "document",
                        new XAttribute(
                            XNamespace.Xmlns + "w",
                            Word.NamespaceName),
                        body));
                SaveXml(documentPart, document);
            }
        }

        private static List<EditorParagraph> ReadEditorParagraphs(WritingAppTextBox editor)
        {
            string normalized = TextUtilities.NormalizeLineEndings(editor.Text ?? String.Empty);
            string[] lines = normalized.Split(
                new[] { Environment.NewLine },
                StringSplitOptions.None);
            var paragraphs = new List<EditorParagraph>();
            int selectionStart = editor.SelectionStart;
            int selectionLength = editor.SelectionLength;
            int position = 0;

            try
            {
                foreach (string line in lines)
                {
                    position = FindParagraphStart(editor.Text, line, position);
                    WritingAppParagraphFormat format = editor.GetParagraphFormatAt(position);
                    int headingLevel = DetectHeadingLevel(editor);
                    string listType = format.Numbering == 1
                        ? "bullet"
                        : (format.Numbering == 2 ? "number" : null);
                    paragraphs.Add(new EditorParagraph(line, headingLevel, listType));
                    position += line.Length + 1;
                }
            }
            finally
            {
                editor.Select(selectionStart, selectionLength);
            }

            return paragraphs;
        }

        private static XElement CreateParagraphElement(EditorParagraph paragraph)
        {
            var properties = new XElement(Word + "pPr");
            if (paragraph.HeadingLevel > 0)
            {
                properties.Add(
                    new XElement(
                        Word + "pStyle",
                        new XAttribute(
                            Word + "val",
                            "Heading" + paragraph.HeadingLevel)));
                properties.Add(
                    new XElement(
                        Word + "outlineLvl",
                        new XAttribute(
                            Word + "val",
                            paragraph.HeadingLevel - 1)));
            }

            if (!String.IsNullOrEmpty(paragraph.ListType))
            {
                properties.Add(
                    new XElement(
                        Word + "numPr",
                        new XElement(
                            Word + "ilvl",
                            new XAttribute(Word + "val", "0")),
                        new XElement(
                            Word + "numId",
                            new XAttribute(
                                Word + "val",
                                paragraph.ListType == "bullet" ? "1" : "2"))));
            }

            var result = new XElement(Word + "p");
            if (properties.HasElements)
                result.Add(properties);
            result.Add(CreateRuns(paragraph.Text));
            return result;
        }

        private static IEnumerable<XElement> CreateRuns(string text)
        {
            string[] parts = (text ?? String.Empty).Split('\t');
            for (int index = 0; index < parts.Length; index++)
            {
                var textElement = new XElement(Word + "t", parts[index]);
                if (parts[index].StartsWith(" ", StringComparison.Ordinal) ||
                    parts[index].EndsWith(" ", StringComparison.Ordinal) ||
                    parts[index].Contains("  "))
                {
                    textElement.SetAttributeValue(XNamespace.Xml + "space", "preserve");
                }

                yield return new XElement(Word + "r", textElement);
                if (index < parts.Length - 1)
                    yield return new XElement(Word + "r", new XElement(Word + "tab"));
            }
        }

        private static void CreateStylesPart(Package package, PackagePart documentPart)
        {
            var stylesUri = new Uri("/word/styles.xml", UriKind.Relative);
            PackagePart stylesPart = package.CreatePart(
                stylesUri,
                StylesContentType,
                CompressionOption.Normal);
            documentPart.CreateRelationship(
                new Uri("styles.xml", UriKind.Relative),
                TargetMode.Internal,
                StylesRelationship);

            var styles = new XElement(Word + "styles");
            styles.Add(CreateStyle("Normal", "Normal", 11F, false, 9, true));
            for (int level = 1; level <= 6; level++)
            {
                styles.Add(
                    CreateStyle(
                        "Heading" + level,
                        "heading " + level,
                        HeadingFontSize(level),
                        true,
                        level - 1,
                        false));
            }

            SaveXml(
                stylesPart,
                new XDocument(
                    new XDeclaration("1.0", "UTF-8", "yes"),
                    new XElement(
                        Word + "styles",
                        new XAttribute(XNamespace.Xmlns + "w", Word.NamespaceName),
                        styles.Elements())));
        }

        private static XElement CreateStyle(
            string styleId,
            string name,
            float size,
            bool bold,
            int outlineLevel,
            bool isDefault)
        {
            var style = new XElement(
                Word + "style",
                new XAttribute(Word + "type", "paragraph"),
                new XAttribute(Word + "styleId", styleId));
            if (isDefault)
                style.Add(new XAttribute(Word + "default", "1"));
            style.Add(new XElement(Word + "name", new XAttribute(Word + "val", name)));
            if (!isDefault)
            {
                style.Add(new XElement(Word + "basedOn", new XAttribute(Word + "val", "Normal")));
                style.Add(new XElement(Word + "next", new XAttribute(Word + "val", "Normal")));
                style.Add(new XElement(Word + "qFormat"));
            }

            var paragraphProperties = new XElement(Word + "pPr");
            if (outlineLevel < 9)
            {
                paragraphProperties.Add(
                    new XElement(
                        Word + "outlineLvl",
                        new XAttribute(Word + "val", outlineLevel)));
            }
            style.Add(paragraphProperties);

            var runProperties = new XElement(
                Word + "rPr",
                new XElement(
                    Word + "rFonts",
                    new XAttribute(Word + "ascii", "Segoe UI"),
                    new XAttribute(Word + "hAnsi", "Segoe UI")),
                new XElement(
                    Word + "sz",
                    new XAttribute(
                        Word + "val",
                        ((int)Math.Round(size * 2)).ToString())),
                new XElement(
                    Word + "szCs",
                    new XAttribute(
                        Word + "val",
                        ((int)Math.Round(size * 2)).ToString())));
            if (bold)
                runProperties.Add(new XElement(Word + "b"));
            style.Add(runProperties);
            return style;
        }

        private static void CreateNumberingPart(Package package, PackagePart documentPart)
        {
            var numberingUri = new Uri("/word/numbering.xml", UriKind.Relative);
            PackagePart numberingPart = package.CreatePart(
                numberingUri,
                NumberingContentType,
                CompressionOption.Normal);
            documentPart.CreateRelationship(
                new Uri("numbering.xml", UriKind.Relative),
                TargetMode.Internal,
                NumberingRelationship);

            var numbering = new XElement(
                Word + "numbering",
                CreateAbstractNumbering(1, "bullet", "•"),
                CreateAbstractNumbering(2, "decimal", "%1."),
                new XElement(
                    Word + "num",
                    new XAttribute(Word + "numId", "1"),
                    new XElement(
                        Word + "abstractNumId",
                        new XAttribute(Word + "val", "1"))),
                new XElement(
                    Word + "num",
                    new XAttribute(Word + "numId", "2"),
                    new XElement(
                        Word + "abstractNumId",
                        new XAttribute(Word + "val", "2"))));
            SaveXml(
                numberingPart,
                new XDocument(
                    new XDeclaration("1.0", "UTF-8", "yes"),
                    new XElement(
                        Word + "numbering",
                        new XAttribute(XNamespace.Xmlns + "w", Word.NamespaceName),
                        numbering.Elements())));
        }

        private static XElement CreateAbstractNumbering(
            int id,
            string format,
            string levelText)
        {
            return new XElement(
                Word + "abstractNum",
                new XAttribute(Word + "abstractNumId", id),
                new XElement(
                    Word + "multiLevelType",
                    new XAttribute(Word + "val", "singleLevel")),
                new XElement(
                    Word + "lvl",
                    new XAttribute(Word + "ilvl", "0"),
                    new XElement(
                        Word + "start",
                        new XAttribute(Word + "val", "1")),
                    new XElement(
                        Word + "numFmt",
                        new XAttribute(Word + "val", format)),
                    new XElement(
                        Word + "lvlText",
                        new XAttribute(Word + "val", levelText)),
                    new XElement(
                        Word + "suff",
                        new XAttribute(Word + "val", "tab")),
                    new XElement(
                        Word + "pPr",
                        new XElement(
                            Word + "tabs",
                            new XElement(
                                Word + "tab",
                                new XAttribute(Word + "val", "num"),
                                new XAttribute(Word + "pos", "720"))),
                        new XElement(
                            Word + "ind",
                            new XAttribute(Word + "left", "720"),
                            new XAttribute(Word + "hanging", "360")))));
        }

        private static Dictionary<string, int> ReadHeadingStyles(Package package)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Uri stylesUri = new Uri("/word/styles.xml", UriKind.Relative);
            if (!package.PartExists(stylesUri))
                return result;

            XDocument styles;
            using (Stream stream = package.GetPart(stylesUri).GetStream(FileMode.Open, FileAccess.Read))
                styles = XDocument.Load(stream);

            foreach (XElement style in styles.Descendants(Word + "style"))
            {
                string styleId = (string)style.Attribute(Word + "styleId");
                if (String.IsNullOrEmpty(styleId))
                    continue;

                int level = 0;
                XElement outline = style.Descendants(Word + "outlineLvl").FirstOrDefault();
                int outlineValue;
                if (outline != null &&
                    Int32.TryParse((string)outline.Attribute(Word + "val"), out outlineValue) &&
                    outlineValue >= 0 &&
                    outlineValue < 9)
                {
                    level = outlineValue + 1;
                }
                else
                {
                    Match match = Regex.Match(styleId, @"^Heading([1-9])$", RegexOptions.IgnoreCase);
                    if (match.Success)
                        level = Int32.Parse(match.Groups[1].Value);
                }

                if (level > 0)
                    result[styleId] = level;
            }
            return result;
        }

        private static Dictionary<string, string> ReadNumberingFormats(Package package)
        {
            var result = new Dictionary<string, string>();
            Uri numberingUri = new Uri("/word/numbering.xml", UriKind.Relative);
            if (!package.PartExists(numberingUri))
                return result;

            XDocument numbering;
            using (Stream stream = package.GetPart(numberingUri).GetStream(FileMode.Open, FileAccess.Read))
                numbering = XDocument.Load(stream);

            var abstractFormats = new Dictionary<string, string>();
            foreach (XElement abstractNumbering in numbering.Descendants(Word + "abstractNum"))
            {
                string abstractId = (string)abstractNumbering.Attribute(Word + "abstractNumId");
                XElement firstLevel = abstractNumbering.Elements(Word + "lvl").FirstOrDefault();
                XElement format = firstLevel == null
                    ? null
                    : firstLevel.Element(Word + "numFmt");
                if (!String.IsNullOrEmpty(abstractId) && format != null)
                    abstractFormats[abstractId] = (string)format.Attribute(Word + "val");
            }

            foreach (XElement number in numbering.Descendants(Word + "num"))
            {
                string numberId = (string)number.Attribute(Word + "numId");
                XElement abstractReference = number.Element(Word + "abstractNumId");
                string abstractId = abstractReference == null
                    ? null
                    : (string)abstractReference.Attribute(Word + "val");
                string format;
                if (!String.IsNullOrEmpty(numberId) &&
                    !String.IsNullOrEmpty(abstractId) &&
                    abstractFormats.TryGetValue(abstractId, out format))
                {
                    result[numberId] = String.Equals(
                        format,
                        "bullet",
                        StringComparison.OrdinalIgnoreCase)
                        ? "bullet"
                        : "number";
                }
            }
            return result;
        }

        private static DocxParagraph ReadParagraph(
            XElement paragraph,
            Dictionary<string, int> headingStyles,
            Dictionary<string, string> numbering)
        {
            XElement properties = paragraph.Element(Word + "pPr");
            int headingLevel = 0;
            string listType = null;

            if (properties != null)
            {
                XElement styleElement = properties.Element(Word + "pStyle");
                string styleId = styleElement == null
                    ? null
                    : (string)styleElement.Attribute(Word + "val");
                if (!String.IsNullOrEmpty(styleId))
                {
                    headingStyles.TryGetValue(styleId, out headingLevel);
                    if (styleId.StartsWith(
                        "ListBullet",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        listType = "bullet";
                    }
                    else if (styleId.StartsWith(
                        "ListNumber",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        listType = "number";
                    }
                }

                XElement directOutline = properties.Element(Word + "outlineLvl");
                int outlineValue;
                if (directOutline != null &&
                    Int32.TryParse((string)directOutline.Attribute(Word + "val"), out outlineValue) &&
                    outlineValue >= 0 &&
                    outlineValue < 9)
                {
                    headingLevel = outlineValue + 1;
                }

                XElement numberProperties = properties.Element(Word + "numPr");
                XElement numberIdElement = numberProperties == null
                    ? null
                    : numberProperties.Element(Word + "numId");
                string numberId = numberIdElement == null
                    ? null
                    : (string)numberIdElement.Attribute(Word + "val");
                if (!String.IsNullOrEmpty(numberId))
                    numbering.TryGetValue(numberId, out listType);
            }

            return new DocxParagraph(
                ReadParagraphText(paragraph),
                headingLevel,
                listType);
        }

        private static string ReadParagraphText(XElement paragraph)
        {
            var result = new StringBuilder();
            foreach (XElement element in paragraph.Descendants())
            {
                if (element.Name == Word + "t")
                    result.Append(element.Value);
                else if (element.Name == Word + "tab")
                    result.Append('\t');
                else if (element.Name == Word + "br" || element.Name == Word + "cr")
                    result.Append(Environment.NewLine);
                else if (element.Name == Word + "noBreakHyphen")
                    result.Append('\u2011');
            }
            return result.ToString();
        }

        private static Uri FindMainDocumentUri(Package package)
        {
            foreach (PackageRelationship relationship in
                package.GetRelationshipsByType(OfficeDocumentRelationship))
            {
                return PackUriHelper.ResolvePartUri(
                    new Uri("/", UriKind.Relative),
                    relationship.TargetUri);
            }

            var conventionalUri = new Uri("/word/document.xml", UriKind.Relative);
            if (package.PartExists(conventionalUri))
                return conventionalUri;
            throw new InvalidDataException("DOCX-filen inneholder ikke et hoveddokument.");
        }

        private static void SaveXml(PackagePart part, XDocument document)
        {
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = false,
                CloseOutput = false
            };
            using (Stream stream = part.GetStream(FileMode.Create, FileAccess.Write))
            using (XmlWriter writer = XmlWriter.Create(stream, settings))
                document.Save(writer);
        }

        private static float HeadingFontSize(int level)
        {
            switch (level)
            {
                case 1: return 20F;
                case 2: return 16F;
                case 3: return 13F;
                case 4: return 12F;
                case 5: return 11.5F;
                default: return 11F;
            }
        }

        private static int DetectHeadingLevel(WritingAppTextBox editor)
        {
            System.Drawing.Font font = editor.SelectionFont;
            if (font == null ||
                (font.Style & System.Drawing.FontStyle.Bold) == 0)
            {
                return 0;
            }

            float size = font.Size;
            if (size >= 18F) return 1;
            if (size >= 14.5F) return 2;
            if (size >= 12.5F) return 3;
            if (size >= 11.5F) return 4;
            if (size >= 11.1F) return 5;
            return 6;
        }

        private static int FindParagraphStart(
            string editorText,
            string paragraphText,
            int searchStart)
        {
            int safeStart = Math.Max(0, Math.Min(searchStart, editorText.Length));
            if (String.IsNullOrEmpty(paragraphText))
                return safeStart;

            int found = editorText.IndexOf(
                paragraphText,
                safeStart,
                StringComparison.Ordinal);
            return found >= 0 ? found : safeStart;
        }

        private sealed class DocxParagraph
        {
            internal readonly string Text;
            internal readonly int HeadingLevel;
            internal readonly string ListType;

            internal DocxParagraph(string text, int headingLevel, string listType)
            {
                Text = text ?? String.Empty;
                HeadingLevel = headingLevel;
                ListType = listType;
            }
        }

        private sealed class EditorParagraph
        {
            internal readonly string Text;
            internal readonly int HeadingLevel;
            internal readonly string ListType;

            internal EditorParagraph(string text, int headingLevel, string listType)
            {
                Text = text ?? String.Empty;
                HeadingLevel = headingLevel;
                ListType = listType;
            }
        }
    }
}
