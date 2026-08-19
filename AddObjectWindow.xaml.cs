using Ensemble.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Ensemble
{
    public partial class AddObjectWindow : Window
    {
        private readonly List<AddObjectTemplateEntry>
            _templates = new();

        public ScenarioObject?
            SelectedTemplate
        {
            get;
            private set;
        }

        public AddObjectWindow(
            IEnumerable<ScenarioObject> objects)
        {
            InitializeComponent();

            // -----------------------------------------------------
            // V1 palette:
            //
            // One representative template for every exact object
            // Type currently present in the scenario.
            //
            // Prefer an original object over a newly duplicated
            // object because the original XMX subtree is the safest
            // structural template.
            // -----------------------------------------------------

            IEnumerable<AddObjectTemplateEntry> entries =
                objects
                    .Where(
                        x =>
                            !string.IsNullOrWhiteSpace(
                                x.Type))
                    .GroupBy(
                        x =>
                            x.Type,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(
                        group =>
                        {
                            ScenarioObject source =
                                group
                                    .OrderBy(
                                        x =>
                                            x.IsNewObject
                                                ? 1
                                                : 0)
                                    .ThenBy(
                                        x =>
                                            x.Id)
                                    .First();

                            return
                                new AddObjectTemplateEntry(
                                    source,
                                    group.Count());
                        })
                    .OrderBy(
                        x =>
                            x.Category)
                    .ThenBy(
                        x =>
                            x.Type,
                        StringComparer.OrdinalIgnoreCase);

            _templates.AddRange(
                entries);

            RefreshList();

            Loaded +=
                (_, _) =>
                {
                    SearchTextBox.Focus();

                    if (TemplateListBox.Items.Count >
                        0)
                    {
                        TemplateListBox.SelectedIndex =
                            0;
                    }
                };
        }

        private void SearchTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            RefreshList();
        }

        private void RefreshList()
        {
            string query =
                SearchTextBox.Text?
                    .Trim()
                ?? string.Empty;

            IEnumerable<AddObjectTemplateEntry> result =
                _templates;

            if (!string.IsNullOrWhiteSpace(
                    query))
            {
                result =
                    result.Where(
                        x =>
                            x.Type.Contains(
                                query,
                                StringComparison.OrdinalIgnoreCase)
                            ||
                            x.Category.Contains(
                                query,
                                StringComparison.OrdinalIgnoreCase)
                            ||
                            x.EditorName.Contains(
                                query,
                                StringComparison.OrdinalIgnoreCase)
                            ||
                            x.TemplateSourceId
                                .ToString()
                                .Contains(
                                    query,
                                    StringComparison.OrdinalIgnoreCase));
            }

            TemplateListBox.ItemsSource =
                result.ToList();

            if (TemplateListBox.Items.Count >
                0)
            {
                TemplateListBox.SelectedIndex =
                    0;
            }
            else
            {
                AddButton.IsEnabled =
                    false;

                ClearDetails();
            }
        }

        private void TemplateListBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (TemplateListBox.SelectedItem
                is not AddObjectTemplateEntry entry)
            {
                AddButton.IsEnabled =
                    false;

                ClearDetails();

                return;
            }

            AddButton.IsEnabled =
                true;

            CategoryText.Text =
                entry.Category;

            TemplateIdText.Text =
                entry.TemplateSourceId
                    .ToString();

            EditorNameText.Text =
                entry.EditorName;

            PlayerText.Text =
                entry.Source.Player
                    .ToString();

            FlagsText.Text =
                entry.Source.Flags.Count == 0
                    ? "None"
                    : string.Join(
                        ", ",
                        entry.Source.Flags);
        }

        private void TemplateListBox_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
        {
            AcceptSelection();
        }

        private void AddButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            AcceptSelection();
        }

        private void AcceptSelection()
        {
            if (TemplateListBox.SelectedItem
                is not AddObjectTemplateEntry entry)
            {
                return;
            }

            SelectedTemplate =
                entry.Source;

            DialogResult =
                true;
        }

        private void ClearDetails()
        {
            CategoryText.Text =
                "-";

            TemplateIdText.Text =
                "-";

            EditorNameText.Text =
                "-";

            PlayerText.Text =
                "-";

            FlagsText.Text =
                "-";
        }
    }

    internal sealed class AddObjectTemplateEntry
    {
        public AddObjectTemplateEntry(
            ScenarioObject source,
            int instanceCount)
        {
            Source =
                source;

            InstanceCount =
                instanceCount;
        }

        public ScenarioObject Source
        {
            get;
        }

        public int InstanceCount
        {
            get;
        }

        public string Type =>
            Source.Type;

        public string Category =>
            Source.Category;

        public string EditorName =>
            Source.EditorName;

        public int TemplateSourceId =>
            Source.IsNewObject
                ? Source.SourceObjectId
                : Source.Id;

        public string Description =>
            $"{Category}  •  " +
            $"{InstanceCount} instance" +
            (InstanceCount == 1
                ? string.Empty
                : "s") +
            $"  •  template ID {TemplateSourceId}";
    }
}