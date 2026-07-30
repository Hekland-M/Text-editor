using System;
using System.Drawing;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace WritingApp
{
    internal sealed class MainForm : Form
    {
        private readonly WritingAppTextBox editor;
        private readonly ToolStripStatusLabel positionLabel;
        private readonly SoundPlayer topBoundarySound;
        private readonly SoundPlayer bottomBoundarySound;
        private readonly Action openNewWindow;
        private readonly Timer autosaveTimer;
        private ToolStripMenuItem overviewWordItem;
        private ToolStripMenuItem overviewCharacterItem;
        private string currentPath;
        private string recoveryPath;
        private bool documentChanged;
        private bool isRichText;
        private bool isDocx;
        private bool isPdf;
        private bool richFormattingUsed;
        private bool loadingDocument;

        public MainForm(
            string initialPath,
            RecoverySnapshot recovery,
            Action openNewWindow)
        {
            this.openNewWindow = openNewWindow;
            Text = "WritingApp";
            AccessibleName = "WritingApp";
            Width = 900;
            Height = 650;
            StartPosition = FormStartPosition.CenterScreen;
            topBoundarySound = CreateBoundarySound(880);
            bottomBoundarySound = CreateBoundarySound(440);
            autosaveTimer = new Timer();
            autosaveTimer.Interval = 5000;
            autosaveTimer.Tick += SaveRecoveryCopy;

            var menu = BuildMenu();
            MainMenuStrip = menu;
            Controls.Add(menu);

            var statusBar = new StatusStrip();
            positionLabel = new ToolStripStatusLabel("Linje 1, kolonne 1");
            positionLabel.Spring = true;
            positionLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusBar.Items.Add(positionLabel);
            Controls.Add(statusBar);

            editor = new WritingAppTextBox();
            editor.Multiline = true;
            editor.AcceptsTab = true;
            editor.ScrollBars = RichTextBoxScrollBars.Vertical;
            editor.WordWrap = true;
            editor.HideSelection = false;
            editor.DetectUrls = false;
            editor.AutoWordSelection = false;
            editor.Dock = DockStyle.Fill;
            editor.Font = new Font("Segoe UI", 11F);
            editor.ModifiedChanged += delegate
            {
                documentChanged = editor.Modified;
                UpdateTitle();
            };
            editor.TextChanged += delegate
            {
                if (loadingDocument)
                    return;
                autosaveTimer.Stop();
                autosaveTimer.Start();
            };
            editor.KeyDown += OnEditorKeyDown;
            editor.SelectionChanged += delegate { UpdatePosition(); };
            Controls.Add(editor);
            editor.BringToFront();

            FormClosing += OnFormClosing;
            Shown += delegate
            {
                if (recovery != null)
                    LoadRecovery(recovery);
                else if (!String.IsNullOrEmpty(initialPath))
                    LoadDocument(initialPath);
                editor.Focus();
            };
        }

        private void SaveRecoveryCopy(object sender, EventArgs e)
        {
            autosaveTimer.Stop();
            if (!documentChanged)
                return;

            try
            {
                if (String.IsNullOrEmpty(recoveryPath))
                    recoveryPath = RecoveryManager.CreateRecoveryPath();
                bool preserveFormatting = isRichText || isDocx;
                RecoveryManager.Save(
                    recoveryPath,
                    currentPath,
                    preserveFormatting,
                    preserveFormatting ? editor.Rtf : editor.Text);
            }
            catch
            {
                SystemSounds.Exclamation.Play();
            }
        }

        private void LoadRecovery(RecoverySnapshot recovery)
        {
            loadingDocument = true;
            try
            {
                recoveryPath = recovery.RecoveryPath;
                currentPath = String.IsNullOrEmpty(recovery.OriginalPath)
                    ? null
                    : recovery.OriginalPath;
                isDocx = !String.IsNullOrEmpty(currentPath) &&
                    String.Equals(
                        Path.GetExtension(currentPath),
                        ".docx",
                        StringComparison.OrdinalIgnoreCase);
                isRichText = recovery.IsRichText && !isDocx;
                richFormattingUsed = recovery.IsRichText;
                if (recovery.IsRichText)
                    editor.Rtf = recovery.Content;
                else
                    editor.Text = TextUtilities.NormalizeLineEndings(recovery.Content);
                editor.SelectionStart = 0;
                editor.Modified = true;
                documentChanged = true;
                UpdateTitle();
            }
            finally
            {
                loadingDocument = false;
            }
        }

        private void OnEditorKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && !e.Shift && !e.Alt && e.KeyCode == Keys.Y)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Modifiers != Keys.None || editor.SelectionLength != 0)
                return;

            int currentLine = editor.GetLineFromCharIndex(editor.SelectionStart);
            int lastLine = Math.Max(0, editor.VisualLineCount - 1);

            if (e.KeyCode == Keys.Up && currentLine == 0)
            {
                topBoundarySound.Play();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Down && currentLine == lastLine)
            {
                bottomBoundarySound.Play();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private static SoundPlayer CreateBoundarySound(int frequency)
        {
            const int sampleRate = 16000;
            const int durationMilliseconds = 70;
            const double volume = 0.16;
            int sampleCount = sampleRate * durationMilliseconds / 1000;
            int fadeSamples = sampleRate * 8 / 1000;

            var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + sampleCount * 2);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(sampleCount * 2);

                for (int i = 0; i < sampleCount; i++)
                {
                    double envelope = 1.0;
                    if (i < fadeSamples)
                        envelope = (double)i / fadeSamples;
                    else if (i >= sampleCount - fadeSamples)
                        envelope = (double)(sampleCount - i - 1) / fadeSamples;

                    double wave = Math.Sin(2.0 * Math.PI * frequency * i / sampleRate);
                    writer.Write((short)(wave * envelope * volume * short.MaxValue));
                }
            }

            stream.Position = 0;
            var player = new SoundPlayer(stream);
            player.Load();
            return player;
        }

        private MenuStrip BuildMenu()
        {
            var menu = new MenuStrip();

            var file = new ToolStripMenuItem("&Fil");
            file.DropDownItems.Add(MenuItem("&Nytt", Keys.Control | Keys.N, NewDocument));
            file.DropDownItems.Add(MenuItem("&Åpne…", Keys.Control | Keys.O, OpenDocument));
            file.DropDownItems.Add(MenuItem("&Lagre", Keys.Control | Keys.S, SaveDocument));
            file.DropDownItems.Add(MenuItem("Lagre &som…", Keys.Control | Keys.Shift | Keys.S, SaveDocumentAs));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(MenuItem("Eksporter som &PDF…", Keys.None, ExportAsPdf));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(MenuItem("A&vslutt", Keys.Alt | Keys.F4, delegate { Close(); }));

            var edit = new ToolStripMenuItem("&Rediger");
            edit.DropDownItems.Add(MenuItem("&Angre", Keys.Control | Keys.Z, delegate { if (editor.CanUndo) editor.Undo(); }));
            edit.DropDownItems.Add(MenuItem("&Gjør om", Keys.Control | Keys.Shift | Keys.Z, delegate { if (editor.CanRedo) editor.Redo(); }));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(MenuItem("Klipp &ut", Keys.Control | Keys.X, delegate { editor.Cut(); }));
            edit.DropDownItems.Add(MenuItem("&Kopier", Keys.Control | Keys.C, delegate { editor.Copy(); }));
            edit.DropDownItems.Add(MenuItem("&Lim inn", Keys.Control | Keys.V, delegate { editor.Paste(); }));
            edit.DropDownItems.Add(MenuItem("Marker &alt", Keys.Control | Keys.A, delegate { editor.SelectAll(); }));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(MenuItem("&Søk og erstatt…", Keys.None, ShowSearchAndReplace));

            var help = new ToolStripMenuItem("&Hjelp");
            help.DropDownItems.Add(MenuItem("&Om WritingApp", Keys.None, ShowAbout));

            var format = new ToolStripMenuItem("F&ormat");
            var paragraphStyle = new ToolStripMenuItem("&Avsnittsstil");
            paragraphStyle.DropDownItems.Add(MenuItem("&Vanlig avsnitt", Keys.None,
                delegate { ApplyParagraphStyle(11F, FontStyle.Regular, -1, 9); }));
            paragraphStyle.DropDownItems.Add(MenuItem("Overskrift &1", Keys.None,
                delegate { ApplyParagraphStyle(20F, FontStyle.Bold, -2, 0); }));
            paragraphStyle.DropDownItems.Add(MenuItem("Overskrift &2", Keys.None,
                delegate { ApplyParagraphStyle(16F, FontStyle.Bold, -3, 1); }));
            paragraphStyle.DropDownItems.Add(MenuItem("Overskrift &3", Keys.None,
                delegate { ApplyParagraphStyle(13F, FontStyle.Bold, -4, 2); }));
            paragraphStyle.DropDownItems.Add(MenuItem("Overskrift &4", Keys.None,
                delegate { ApplyParagraphStyle(12F, FontStyle.Bold, -5, 3); }));
            paragraphStyle.DropDownItems.Add(MenuItem("Overskrift &5", Keys.None,
                delegate { ApplyParagraphStyle(11.25F, FontStyle.Bold, -6, 4); }));
            paragraphStyle.DropDownItems.Add(MenuItem("Overskrift &6", Keys.None,
                delegate { ApplyParagraphStyle(10.5F, FontStyle.Bold, -7, 5); }));
            format.DropDownItems.Add(paragraphStyle);
            format.DropDownItems.Add(new ToolStripSeparator());
            format.DropDownItems.Add(MenuItem("&Fet", Keys.None,
                delegate { ToggleTextStyle(FontStyle.Bold); }));
            format.DropDownItems.Add(MenuItem("&Kursiv", Keys.None,
                delegate { ToggleTextStyle(FontStyle.Italic); }));
            format.DropDownItems.Add(MenuItem("&Understreking", Keys.None,
                delegate { ToggleTextStyle(FontStyle.Underline); }));
            format.DropDownItems.Add(new ToolStripSeparator());
            format.DropDownItems.Add(MenuItem("&Punktliste", Keys.None, ToggleBulletList));
            format.DropDownItems.Add(MenuItem("&Nummerert liste", Keys.None, ToggleNumberedList));
            format.DropDownItems.Add(new ToolStripSeparator());
            format.DropDownItems.Add(MenuItem("F&jern formatering", Keys.None, ClearFormatting));

            var overview = new ToolStripMenuItem("&Oversikt");
            overviewWordItem = new ToolStripMenuItem("Ord: 0");
            overviewCharacterItem = new ToolStripMenuItem("Tegn: 0");
            overviewWordItem.Click += delegate { };
            overviewCharacterItem.Click += delegate { };
            overview.DropDownItems.Add(overviewWordItem);
            overview.DropDownItems.Add(overviewCharacterItem);
            overview.DropDownOpening += delegate { UpdateDocumentOverview(); };

            menu.Items.Add(file);
            menu.Items.Add(edit);
            menu.Items.Add(format);
            menu.Items.Add(overview);
            menu.Items.Add(help);
            return menu;
        }

        private void ToggleTextStyle(FontStyle style)
        {
            Font currentFont = editor.SelectionFont ?? editor.Font;
            bool isApplied = (currentFont.Style & style) == style;
            FontStyle newStyle = isApplied
                ? currentFont.Style & ~style
                : currentFont.Style | style;
            editor.SelectionFont = new Font(
                currentFont.FontFamily,
                currentFont.Size,
                newStyle);
            MarkRichFormatting();
        }

        private void ApplyParagraphStyle(
            float size,
            FontStyle style,
            short nativeStyle,
            byte outlineLevel)
        {
            int originalStart = editor.SelectionStart;
            int originalLength = editor.SelectionLength;
            SelectTouchedParagraphs();
            editor.SetNativeParagraphStyle(nativeStyle, outlineLevel);
            editor.SelectionFont = new Font("Segoe UI", size, style);
            editor.SelectionStart = originalStart;
            editor.SelectionLength = originalLength;
            MarkRichFormatting();
        }

        private void ToggleBulletList(object sender, EventArgs e)
        {
            SelectTouchedParagraphs();
            editor.ToggleNativeBulletList();
            MarkRichFormatting();
        }

        private void ToggleNumberedList(object sender, EventArgs e)
        {
            SelectTouchedParagraphs();
            editor.ToggleNativeNumberedList();
            MarkRichFormatting();
        }

        private void ClearFormatting(object sender, EventArgs e)
        {
            int originalStart = editor.SelectionStart;
            int originalLength = editor.SelectionLength;
            SelectTouchedParagraphs();
            editor.SetNativeParagraphStyle(-1, 9);
            editor.ClearNativeList();
            editor.SelectionFont = new Font("Segoe UI", 11F, FontStyle.Regular);
            editor.SelectionIndent = 0;
            editor.SelectionHangingIndent = 0;
            editor.SelectionRightIndent = 0;
            editor.SelectionStart = originalStart;
            editor.SelectionLength = originalLength;
            MarkRichFormatting();
        }

        private void SelectTouchedParagraphs()
        {
            string text = editor.Text;
            int start = editor.SelectionStart;
            int end = start + editor.SelectionLength;

            while (start > 0 && text[start - 1] != '\n')
                start--;
            if (end > start && end <= text.Length && text[end - 1] == '\n')
                end--;
            while (end < text.Length && text[end] != '\n')
                end++;

            editor.Select(start, end - start);
        }

        private void MarkRichFormatting()
        {
            richFormattingUsed = true;
            editor.Modified = true;
            editor.Focus();
        }

        private void UpdateDocumentOverview()
        {
            string text = editor.Text;
            int wordCount = Regex.Matches(
                text,
                @"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*",
                RegexOptions.CultureInvariant).Count;

            int characterCount = 0;
            TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(text);
            while (elements.MoveNext())
            {
                string element = elements.GetTextElement();
                if (element != "\r" && element != "\n")
                    characterCount++;
            }

            overviewWordItem.Text = "Ord: " + wordCount;
            overviewCharacterItem.Text = "Tegn: " + characterCount;
        }

        private void ShowSearchAndReplace(object sender, EventArgs e)
        {
            using (var dialog = new SearchReplaceDialog(this))
                dialog.ShowDialog(this);
            editor.Focus();
        }

        internal List<SearchMatch> FindMatches(string searchText)
        {
            var matches = new List<SearchMatch>();
            if (String.IsNullOrEmpty(searchText))
                return matches;

            string documentText = editor.Text;
            int searchFrom = 0;
            while (searchFrom <= documentText.Length - searchText.Length)
            {
                int index = documentText.IndexOf(
                    searchText,
                    searchFrom,
                    StringComparison.CurrentCultureIgnoreCase);
                if (index < 0)
                    break;

                matches.Add(new SearchMatch(
                    index,
                    searchText.Length,
                    BuildMatchDescription(documentText, index, searchText.Length, matches.Count + 1)));
                searchFrom = index + Math.Max(1, searchText.Length);
            }

            return matches;
        }

        internal bool ReplaceMatch(SearchMatch match, string replacement)
        {
            if (match.Start < 0 || match.Start + match.Length > editor.TextLength)
                return false;

            editor.Select(match.Start, match.Length);
            editor.SelectedText = replacement ?? String.Empty;
            editor.SelectionStart = match.Start + (replacement ?? String.Empty).Length;
            editor.SelectionLength = 0;
            return true;
        }

        internal int ReplaceAllMatches(string searchText, string replacement)
        {
            List<SearchMatch> matches = FindMatches(searchText);
            for (int index = matches.Count - 1; index >= 0; index--)
                ReplaceMatch(matches[index], replacement);
            return matches.Count;
        }

        private static string BuildMatchDescription(
            string text,
            int matchStart,
            int matchLength,
            int matchNumber)
        {
            int sentenceStart = matchStart;
            while (sentenceStart > 0)
            {
                char previous = text[sentenceStart - 1];
                if (previous == '.' || previous == '!' || previous == '?' ||
                    previous == '\r' || previous == '\n')
                    break;
                sentenceStart--;
            }

            while (sentenceStart < text.Length && Char.IsWhiteSpace(text[sentenceStart]))
                sentenceStart++;

            int sentenceEnd = matchStart + matchLength;
            while (sentenceEnd < text.Length)
            {
                char current = text[sentenceEnd];
                sentenceEnd++;
                if (current == '.' || current == '!' || current == '?' ||
                    current == '\r' || current == '\n')
                    break;
            }

            string sentence = text.Substring(
                sentenceStart,
                Math.Max(0, sentenceEnd - sentenceStart));
            sentence = sentence.Replace("\r", " ").Replace("\n", " ").Trim();
            return "Treff " + matchNumber + ": " + sentence;
        }

        private ToolStripMenuItem MenuItem(string text, Keys shortcut, EventHandler action)
        {
            var item = new ToolStripMenuItem(text);
            if (shortcut != Keys.None)
                item.ShortcutKeys = shortcut;
            item.Click += action;
            return item;
        }

        private void NewDocument(object sender, EventArgs e)
        {
            openNewWindow();
        }

        private void OpenDocument(object sender, EventArgs e)
        {
            if (!CanDiscardChanges())
                return;

            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Åpne dokument";
                dialog.Filter =
                    "Støttede dokumenter (*.txt;*.rtf;*.docx;*.pdf)|*.txt;*.rtf;*.docx;*.pdf|" +
                    "Word-dokument (*.docx)|*.docx|" +
                    "PDF-dokument (*.pdf)|*.pdf|" +
                    "Rikt tekstdokument (*.rtf)|*.rtf|" +
                    "Tekstdokument (*.txt)|*.txt|" +
                    "Alle filer (*.*)|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                LoadDocument(dialog.FileName);
            }
        }

        private void LoadDocument(string path)
        {
            try
            {
                loadingDocument = true;
                autosaveTimer.Stop();
                RecoveryManager.Delete(recoveryPath);
                recoveryPath = null;
                isRichText = String.Equals(
                    Path.GetExtension(path),
                    ".rtf",
                    StringComparison.OrdinalIgnoreCase);
                isDocx = String.Equals(
                    Path.GetExtension(path),
                    ".docx",
                    StringComparison.OrdinalIgnoreCase);
                isPdf = String.Equals(
                    Path.GetExtension(path),
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase);
                richFormattingUsed = isRichText;
                if (isRichText)
                    editor.LoadFile(path, RichTextBoxStreamType.RichText);
                else if (isDocx)
                    DocxTextConverter.ReadInto(path, editor);
                else if (isPdf)
                    editor.Text = TextUtilities.NormalizeLineEndings(
                        PdfTextExtractor.Read(path));
                else
                    editor.Text = TextUtilities.NormalizeLineEndings(
                        File.ReadAllText(path, Encoding.UTF8));
                currentPath = path;
                editor.Modified = false;
                editor.SelectionStart = 0;
                UpdateTitle();
                editor.Focus();
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "Dokumentet kunne ikke åpnes.\r\n\r\n" + error.Message,
                    "WritingApp", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                loadingDocument = false;
            }
        }

        private void SaveDocument(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(currentPath) ||
                isPdf ||
                (!isRichText && !isDocx && richFormattingUsed))
                SaveDocumentAs(sender, e);
            else
                WriteDocument(currentPath);
        }

        private void SaveDocumentAs(object sender, EventArgs e)
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "Lagre dokument som";
                dialog.Filter =
                    "Word-dokument (*.docx)|*.docx|" +
                    "Rikt tekstdokument (*.rtf)|*.rtf|" +
                    "Tekstdokument (*.txt)|*.txt|" +
                    "Alle filer (*.*)|*.*";
                bool preferRichText = isRichText || richFormattingUsed;
                dialog.FilterIndex = isDocx ? 1 : (preferRichText ? 2 : 3);
                dialog.DefaultExt = isDocx ? "docx" : (preferRichText ? "rtf" : "txt");
                dialog.AddExtension = true;
                if (!String.IsNullOrEmpty(currentPath))
                    dialog.FileName = Path.GetFileName(currentPath);

                if (dialog.ShowDialog(this) == DialogResult.OK)
                    WriteDocument(dialog.FileName);
            }
        }

        private bool WriteDocument(string path)
        {
            try
            {
                bool saveAsRichText = String.Equals(
                    Path.GetExtension(path),
                    ".rtf",
                    StringComparison.OrdinalIgnoreCase);
                bool saveAsDocx = String.Equals(
                    Path.GetExtension(path),
                    ".docx",
                    StringComparison.OrdinalIgnoreCase);
                if (!saveAsRichText && !saveAsDocx && richFormattingUsed)
                {
                    DialogResult answer = MessageBox.Show(
                        this,
                        "TXT kan ikke lagre formatering. Hvis du fortsetter, blir fet skrift, " +
                        "overskrifter, lister og annen formatering borte i den lagrede filen.\r\n\r\n" +
                        "Vil du lagre som ren tekst likevel?",
                        "Formatering kan gå tapt",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (answer != DialogResult.Yes)
                        return false;
                }
                if (saveAsRichText)
                    editor.SaveFile(path, RichTextBoxStreamType.RichText);
                else if (saveAsDocx)
                    DocxTextConverter.Write(path, editor);
                else
                    File.WriteAllText(
                        path,
                        TextUtilities.NormalizeLineEndings(editor.Text),
                        new UTF8Encoding(false));
                currentPath = path;
                isRichText = saveAsRichText;
                isDocx = saveAsDocx;
                isPdf = false;
                if (saveAsRichText)
                    richFormattingUsed = true;
                editor.Modified = false;
                autosaveTimer.Stop();
                RecoveryManager.Delete(recoveryPath);
                recoveryPath = null;
                editor.Focus();
                return true;
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "Dokumentet kunne ikke lagres.\r\n\r\n" + error.Message,
                    "WritingApp", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void ExportAsPdf(object sender, EventArgs e)
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "Eksporter som PDF";
                dialog.Filter = "PDF-dokument (*.pdf)|*.pdf";
                dialog.DefaultExt = "pdf";
                dialog.AddExtension = true;
                dialog.OverwritePrompt = true;
                dialog.FileName = String.IsNullOrEmpty(currentPath)
                    ? "Dokument.pdf"
                    : Path.GetFileNameWithoutExtension(currentPath) + ".pdf";

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    editor.Focus();
                    return;
                }

                try
                {
                    PdfExporter.Export(dialog.FileName, editor.Text);
                    SystemSounds.Asterisk.Play();
                }
                catch (Exception error)
                {
                    MessageBox.Show(
                        this,
                        "PDF-filen kunne ikke eksporteres.\r\n\r\n" + error.Message,
                        "WritingApp",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                editor.Focus();
            }
        }

        private bool CanDiscardChanges()
        {
            if (!documentChanged)
                return true;

            var result = MessageBox.Show(this,
                "Vil du lagre endringene i dokumentet?",
                "WritingApp",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Cancel)
                return false;
            if (result == DialogResult.No)
                return true;

            SaveDocument(this, EventArgs.Empty);
            return !documentChanged;
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!CanDiscardChanges())
            {
                e.Cancel = true;
                return;
            }
            autosaveTimer.Stop();
            RecoveryManager.Delete(recoveryPath);
        }

        private void UpdateTitle()
        {
            Text = String.IsNullOrEmpty(currentPath)
                ? "WritingApp"
                : Path.GetFileNameWithoutExtension(currentPath);
        }

        private void UpdatePosition()
        {
            int index = editor.SelectionStart;
            int line = editor.GetLineFromCharIndex(index);
            int lineStart = editor.GetFirstCharIndexFromLine(line);
            int column = index - lineStart;
            positionLabel.Text = "Linje " + (line + 1) + ", kolonne " + (column + 1);
        }

        private void ShowAbout(object sender, EventArgs e)
        {
            MessageBox.Show(this,
                "WritingApp, tidlig testversjon\r\n\r\nEt enkelt skriveprogram uten konto, abonnement eller tull.",
                "Om WritingApp",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            editor.Focus();
        }
    }

    internal static class TextUtilities
    {
        public static string NormalizeLineEndings(string text)
        {
            if (String.IsNullOrEmpty(text))
                return text;

            return text
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\n", Environment.NewLine);
        }

    }

    internal sealed class WritingAppTextBox : RichTextBox
    {
        private const int WmPaste = 0x0302;
        private const int EmGetLineCount = 0x00BA;
        private const int WmUser = 0x0400;
        private const int EmGetParaFormat = WmUser + 61;
        private const int EmSetParaFormat = WmUser + 71;
        private const uint PfmStartIndent = 0x00000001;
        private const uint PfmOffset = 0x00000004;
        private const uint PfmNumbering = 0x00000020;
        private const uint PfmStyle = 0x00000400;
        private const uint PfmNumberingStyle = 0x00002000;
        private const uint PfmNumberingTab = 0x00004000;
        private const uint PfmNumberingStart = 0x00008000;
        private const uint PfmOutlineLevel = 0x02000000;
        private const ushort PfnBullet = 1;
        private const ushort PfnArabic = 2;
        private const ushort PfnsPeriod = 0x0200;
        private const ushort PfnsNewNumber = 0x8000;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct ParaFormat2
        {
            public uint cbSize;
            public uint dwMask;
            public ushort wNumbering;
            public ushort wEffects;
            public int dxStartIndent;
            public int dxRightIndent;
            public int dxOffset;
            public ushort wAlignment;
            public short cTabCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public int[] rgxTabs;
            public int dySpaceBefore;
            public int dySpaceAfter;
            public int dyLineSpacing;
            public short sStyle;
            public byte bLineSpacingRule;
            public byte bOutlineLevel;
            public ushort wShadingWeight;
            public ushort wShadingStyle;
            public ushort wNumberingStart;
            public ushort wNumberingStyle;
            public ushort wNumberingTab;
            public ushort wBorderSpace;
            public ushort wBorderWidth;
            public ushort wBorders;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr windowHandle,
            int message,
            IntPtr wordParameter,
            IntPtr longParameter);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(
            IntPtr windowHandle,
            int message,
            IntPtr wordParameter,
            ref ParaFormat2 format);

        public int VisualLineCount
        {
            get
            {
                return SendMessage(
                    Handle,
                    EmGetLineCount,
                    IntPtr.Zero,
                    IntPtr.Zero).ToInt32();
            }
        }

        public void SetNativeParagraphStyle(short style, byte outlineLevel)
        {
            ParaFormat2 format = CreateParaFormat();
            format.dwMask = PfmStyle | PfmOutlineLevel;
            format.sStyle = style;
            format.bOutlineLevel = outlineLevel;
            SendMessage(Handle, EmSetParaFormat, IntPtr.Zero, ref format);
        }

        public WritingAppParagraphFormat GetParagraphFormatAt(int characterIndex)
        {
            int safeIndex = Math.Max(0, Math.Min(characterIndex, TextLength));
            Select(safeIndex, 0);
            ParaFormat2 format = GetParaFormat(
                PfmStyle | PfmOutlineLevel | PfmNumbering);
            return new WritingAppParagraphFormat(
                format.sStyle,
                format.bOutlineLevel,
                format.wNumbering);
        }

        public void SetNativeListType(string listType)
        {
            if (String.Equals(listType, "bullet", StringComparison.Ordinal))
                SetNativeList(PfnBullet, 0);
            else if (String.Equals(listType, "number", StringComparison.Ordinal))
                SetNativeList(PfnArabic, (ushort)(PfnsPeriod | PfnsNewNumber));
            else
                ClearNativeList();
        }

        public void ToggleNativeBulletList()
        {
            ParaFormat2 current = GetParaFormat(PfmNumbering);
            if (current.wNumbering == PfnBullet)
                ClearNativeList();
            else
                SetNativeList(PfnBullet, 0);
        }

        public void ToggleNativeNumberedList()
        {
            ParaFormat2 current = GetParaFormat(PfmNumbering);
            if (current.wNumbering == PfnArabic)
                ClearNativeList();
            else
                SetNativeList(PfnArabic, (ushort)(PfnsPeriod | PfnsNewNumber));
        }

        public void ClearNativeList()
        {
            ParaFormat2 format = CreateParaFormat();
            format.dwMask = PfmNumbering | PfmStartIndent | PfmOffset;
            format.wNumbering = 0;
            format.dxStartIndent = 0;
            format.dxOffset = 0;
            SendMessage(Handle, EmSetParaFormat, IntPtr.Zero, ref format);
        }

        private void SetNativeList(ushort numbering, ushort numberingStyle)
        {
            ParaFormat2 format = CreateParaFormat();
            format.dwMask =
                PfmNumbering |
                PfmNumberingStyle |
                PfmNumberingStart |
                PfmNumberingTab |
                PfmStartIndent |
                PfmOffset;
            format.wNumbering = numbering;
            format.wNumberingStyle = numberingStyle;
            format.wNumberingStart = 1;
            format.wNumberingTab = 720;
            format.dxStartIndent = 720;
            format.dxOffset = -360;
            SendMessage(Handle, EmSetParaFormat, IntPtr.Zero, ref format);
        }

        private ParaFormat2 GetParaFormat(uint mask)
        {
            ParaFormat2 format = CreateParaFormat();
            format.dwMask = mask;
            SendMessage(Handle, EmGetParaFormat, IntPtr.Zero, ref format);
            return format;
        }

        private static ParaFormat2 CreateParaFormat()
        {
            var format = new ParaFormat2();
            format.cbSize = (uint)Marshal.SizeOf(typeof(ParaFormat2));
            format.rgxTabs = new int[32];
            return format;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmPaste && Clipboard.ContainsText())
            {
                try
                {
                    SelectedText = TextUtilities.NormalizeLineEndings(
                        Clipboard.GetText(TextDataFormat.UnicodeText));
                    return;
                }
                catch
                {
                    // If another program temporarily locks the clipboard,
                    // let the native Windows text control handle the paste.
                }
            }

            base.WndProc(ref message);
        }
    }

    internal sealed class WritingAppParagraphFormat
    {
        internal readonly short Style;
        internal readonly byte OutlineLevel;
        internal readonly ushort Numbering;

        internal WritingAppParagraphFormat(
            short style,
            byte outlineLevel,
            ushort numbering)
        {
            Style = style;
            OutlineLevel = outlineLevel;
            Numbering = numbering;
        }
    }
}
