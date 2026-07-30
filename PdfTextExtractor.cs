using System;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace WritingApp
{
    internal static class PdfTextExtractor
    {
        public static string Read(string path)
        {
            var text = new StringBuilder();

            using (PdfDocument document = PdfDocument.Open(path))
            {
                foreach (Page page in document.GetPages())
                {
                    if (text.Length > 0)
                        text.AppendLine().AppendLine();

                    text.Append(ContentOrderTextExtractor.GetText(page));
                }
            }

            if (text.Length == 0)
                throw new InvalidOperationException(
                    "PDF-filen inneholder ingen tekst som kan leses. " +
                    "Den kan være skannet som bilder.");

            return text.ToString();
        }
    }
}
