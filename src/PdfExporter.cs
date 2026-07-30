using System;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;

namespace WritingApp
{
    internal static class PdfExporter
    {
        private const string PdfPrinterName = "Microsoft Print to PDF";

        public static void Export(string path, string text)
        {
            if (String.IsNullOrEmpty(path))
                throw new ArgumentException("PDF-filen mangler et filnavn.", "path");

            bool printerFound = false;
            foreach (string printerName in PrinterSettings.InstalledPrinters)
            {
                if (String.Equals(printerName, PdfPrinterName, StringComparison.OrdinalIgnoreCase))
                {
                    printerFound = true;
                    break;
                }
            }

            if (!printerFound)
                throw new InvalidOperationException(
                    "Windows-funksjonen Microsoft Print to PDF er ikke tilgjengelig.");

            string fullPath = Path.GetFullPath(path);
            int characterIndex = 0;
            string documentText = PreserveBlankLines(text ?? String.Empty);

            using (var document = new PrintDocument())
            using (var font = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point))
            {
                document.DocumentName = Path.GetFileNameWithoutExtension(fullPath);
                document.PrintController = new StandardPrintController();
                document.PrinterSettings.PrinterName = PdfPrinterName;
                document.PrinterSettings.PrintToFile = true;
                document.PrinterSettings.PrintFileName = fullPath;
                document.DefaultPageSettings.Margins = new Margins(72, 72, 72, 72);

                document.PrintPage += delegate(object sender, PrintPageEventArgs e)
                {
                    string remainingText = characterIndex < documentText.Length
                        ? documentText.Substring(characterIndex)
                        : String.Empty;

                    int charactersFitted;
                    int linesFilled;
                    using (var format = new StringFormat(StringFormatFlags.LineLimit))
                    {
                        format.Trimming = StringTrimming.None;
                        e.Graphics.MeasureString(
                            remainingText,
                            font,
                            e.MarginBounds.Size,
                            format,
                            out charactersFitted,
                            out linesFilled);

                        if (remainingText.Length > 0 && charactersFitted <= 0)
                            throw new InvalidOperationException(
                                "Teksten fikk ikke plass på PDF-siden.");

                        if (charactersFitted > 0)
                        {
                            e.Graphics.DrawString(
                                remainingText.Substring(0, charactersFitted),
                                font,
                                Brushes.Black,
                                e.MarginBounds,
                                format);
                            characterIndex += charactersFitted;
                        }
                    }

                    e.HasMorePages = characterIndex < documentText.Length;
                };

                document.Print();
            }

            if (!File.Exists(fullPath) || new FileInfo(fullPath).Length == 0)
                throw new IOException("Windows opprettet ikke PDF-filen.");
        }

        private static string PreserveBlankLines(string text)
        {
            string normalized = text
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");
            string[] lines = normalized.Split(new[] { '\n' });

            for (int index = 0; index < lines.Length; index++)
            {
                if (lines[index].Length == 0)
                    lines[index] = "\u00A0";
            }

            return String.Join(Environment.NewLine, lines);
        }
    }
}
