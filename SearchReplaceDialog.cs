using System;
using System.Collections.Generic;
using System.Drawing;
using System.Media;
using System.Windows.Forms;

namespace WritingApp
{
    internal sealed class SearchMatch
    {
        public readonly int Start;
        public readonly int Length;
        public readonly string Description;

        public SearchMatch(int start, int length, string description)
        {
            Start = start;
            Length = length;
            Description = description;
        }

        public override string ToString()
        {
            return Description;
        }
    }

    internal sealed class SearchReplaceDialog : Form
    {
        private readonly MainForm documentWindow;
        private readonly TextBox searchBox;
        private readonly TextBox replacementBox;
        private readonly Button replaceAllButton;
        private readonly Button replaceButton;
        private readonly AnnouncingLabel statusLabel;

        public SearchReplaceDialog(MainForm documentWindow)
        {
            this.documentWindow = documentWindow;
            Text = "Søk og erstatt";
            Width = 520;
            Height = 260;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(14);
            layout.ColumnCount = 1;
            layout.RowCount = 7;
            layout.AutoSize = true;

            var searchLabel = new Label();
            searchLabel.Text = "Søk etter:";
            searchLabel.AutoSize = true;
            searchLabel.TabStop = false;
            layout.Controls.Add(searchLabel);

            searchBox = new TextBox();
            searchBox.AccessibleName = "Søk etter";
            searchBox.Dock = DockStyle.Top;
            searchBox.TabIndex = 0;
            layout.Controls.Add(searchBox);

            var replacementLabel = new Label();
            replacementLabel.Text = "Erstatt med:";
            replacementLabel.AutoSize = true;
            replacementLabel.TabStop = false;
            layout.Controls.Add(replacementLabel);

            replacementBox = new TextBox();
            replacementBox.AccessibleName = "Erstatt med";
            replacementBox.Dock = DockStyle.Top;
            replacementBox.TabIndex = 1;
            layout.Controls.Add(replacementBox);

            replaceAllButton = new Button();
            replaceAllButton.Text = "Erstatt alle";
            replaceAllButton.AutoSize = true;
            replaceAllButton.TabIndex = 2;
            replaceAllButton.Click += ReplaceAll;
            layout.Controls.Add(replaceAllButton);

            replaceButton = new Button();
            replaceButton.Text = "Erstatt";
            replaceButton.AutoSize = true;
            replaceButton.TabIndex = 3;
            replaceButton.Click += ShowMatchesForReplacement;
            layout.Controls.Add(replaceButton);

            statusLabel = new AnnouncingLabel();
            statusLabel.AutoSize = true;
            statusLabel.AccessibleRole = AccessibleRole.Alert;
            statusLabel.TabStop = false;
            layout.Controls.Add(statusLabel);

            Controls.Add(layout);
            Shown += delegate { searchBox.Focus(); };
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }

        private bool HasSearchText()
        {
            if (!String.IsNullOrEmpty(searchBox.Text))
                return true;

            SystemSounds.Exclamation.Play();
            statusLabel.Text = "Skriv inn teksten du vil søke etter.";
            statusLabel.Announce();
            searchBox.Focus();
            return false;
        }

        private void ReplaceAll(object sender, EventArgs e)
        {
            if (!HasSearchText())
                return;

            int count = documentWindow.ReplaceAllMatches(
                searchBox.Text,
                replacementBox.Text);
            if (count == 0)
            {
                SystemSounds.Exclamation.Play();
                statusLabel.Text = "Ingen treff.";
            }
            else
            {
                SystemSounds.Asterisk.Play();
                statusLabel.Text = count + (count == 1
                    ? " treff ble erstattet."
                    : " treff ble erstattet.");
            }
            statusLabel.Announce();
            replaceAllButton.Focus();
        }

        private void ShowMatchesForReplacement(object sender, EventArgs e)
        {
            if (!HasSearchText())
                return;

            List<SearchMatch> matches = documentWindow.FindMatches(searchBox.Text);
            if (matches.Count == 0)
            {
                SystemSounds.Exclamation.Play();
                statusLabel.Text = "Ingen treff.";
                statusLabel.Announce();
                replaceButton.Focus();
                return;
            }

            using (var results = new SearchResultsDialog(
                documentWindow,
                searchBox.Text,
                replacementBox.Text))
            {
                results.ShowDialog(this);
            }
            replaceButton.Focus();
        }
    }

    internal sealed class SearchResultsDialog : Form
    {
        private readonly MainForm documentWindow;
        private readonly string searchText;
        private readonly string replacementText;
        private readonly ListBox resultsList;
        private readonly AnnouncingLabel instructionLabel;

        public SearchResultsDialog(
            MainForm documentWindow,
            string searchText,
            string replacementText)
        {
            this.documentWindow = documentWindow;
            this.searchText = searchText;
            this.replacementText = replacementText;

            Text = "Treff for " + searchText;
            Width = 680;
            Height = 420;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            instructionLabel = new AnnouncingLabel();
            instructionLabel.Text = "Velg et treff og trykk Enter for å erstatte det. Escape går tilbake.";
            instructionLabel.Dock = DockStyle.Top;
            instructionLabel.AutoSize = true;
            instructionLabel.Padding = new Padding(8);

            resultsList = new ListBox();
            resultsList.AccessibleName = "Treff";
            resultsList.Dock = DockStyle.Fill;
            resultsList.IntegralHeight = false;
            resultsList.KeyDown += OnResultsKeyDown;
            resultsList.DoubleClick += delegate { ReplaceSelectedMatch(); };

            Controls.Add(resultsList);
            Controls.Add(instructionLabel);
            RefreshMatches(0);
            Shown += delegate { resultsList.Focus(); };
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }

        private void OnResultsKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                ReplaceSelectedMatch();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void ReplaceSelectedMatch()
        {
            var match = resultsList.SelectedItem as SearchMatch;
            if (match == null)
                return;

            int selectedIndex = resultsList.SelectedIndex;
            if (documentWindow.ReplaceMatch(match, replacementText))
                SystemSounds.Asterisk.Play();
            RefreshMatches(selectedIndex);
        }

        private void RefreshMatches(int preferredIndex)
        {
            List<SearchMatch> matches = documentWindow.FindMatches(searchText);
            resultsList.BeginUpdate();
            resultsList.Items.Clear();
            foreach (SearchMatch match in matches)
                resultsList.Items.Add(match);
            resultsList.EndUpdate();

            if (resultsList.Items.Count == 0)
            {
                SystemSounds.Asterisk.Play();
                instructionLabel.Text = "Alle treff er erstattet. Trykk Escape for å gå tilbake.";
                instructionLabel.Announce();
                resultsList.Enabled = false;
                return;
            }

            resultsList.SelectedIndex = Math.Min(
                Math.Max(0, preferredIndex),
                resultsList.Items.Count - 1);
        }
    }

    internal sealed class AnnouncingLabel : Label
    {
        public void Announce()
        {
            AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        }
    }
}
