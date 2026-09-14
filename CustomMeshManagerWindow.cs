using Ensemble.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ensemble
{
    /// <summary>
    /// Lists Ensemble-managed custom models already embedded in the current
    /// map ERA. Removal is queued and applied through the normal map save path
    /// so the SC2 placement and borrowed UGX slot stay synchronized.
    /// </summary>
    internal sealed class CustomMeshManagerWindow : Window
    {
        private readonly string _eraPath;
        private readonly ListBox _meshList;
        private readonly TextBlock _statusText;
        private readonly Button _deleteButton;

        private readonly List<CustomMeshPendingAssetService.EmbeddedCustomMeshRecord>
            _queuedRecords =
                new();

        public int QueuedDeletionCount =>
            _queuedRecords.Count;

        public IReadOnlyList<CustomMeshPendingAssetService.EmbeddedCustomMeshRecord> QueuedRecords =>
            _queuedRecords;

        public CustomMeshManagerWindow(
            string eraPath)
        {
            _eraPath = eraPath;

            Title = "ENSEMBLE // EMBEDDED CUSTOM MESHES";
            Width = 780;
            Height = 540;
            MinWidth = 680;
            MinHeight = 440;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            Grid root = new();
            root.SetResourceReference(Panel.BackgroundProperty, "HwBackgroundBrush");
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Border accent = new();
            accent.SetResourceReference(BackgroundProperty, "HwAccentBrush");
            Grid.SetRow(accent, 0);
            root.Children.Add(accent);

            Border header = new()
            {
                Padding = new Thickness(16, 13, 16, 13),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            header.SetResourceReference(BackgroundProperty, "HwPanelRaisedBrush");
            header.SetResourceReference(BorderBrushProperty, "HwLineBrush");
            Grid.SetRow(header, 1);

            StackPanel headerStack = new();
            TextBlock title = new()
            {
                Text = "EMBEDDED CUSTOM MESHES // CURRENT ERA",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold
            };
            title.SetResourceReference(ForegroundProperty, "HwAccentBrightBrush");

            TextBlock subtitle = new()
            {
                Text = "Select one or more Ensemble-managed custom models to remove. Deletion is queued until you save the map.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 0)
            };
            subtitle.SetResourceReference(ForegroundProperty, "HwMutedTextBrush");

            headerStack.Children.Add(title);
            headerStack.Children.Add(subtitle);
            header.Child = headerStack;
            root.Children.Add(header);

            Border body = new()
            {
                Margin = new Thickness(14),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0)
            };
            body.SetResourceReference(BackgroundProperty, "HwPanelBrush");
            body.SetResourceReference(BorderBrushProperty, "HwLineBrush");
            Grid.SetRow(body, 2);

            _meshList = new ListBox
            {
                SelectionMode = SelectionMode.Extended,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent
            };
            _meshList.SelectionChanged += (_, _) =>
                _deleteButton.IsEnabled = _meshList.SelectedItems.Count > 0;

            body.Child = _meshList;
            root.Children.Add(body);

            Border footer = new()
            {
                Padding = new Thickness(14, 11, 14, 11),
                BorderThickness = new Thickness(0, 1, 0, 0)
            };
            footer.SetResourceReference(BackgroundProperty, "HwPanelRaisedBrush");
            footer.SetResourceReference(BorderBrushProperty, "HwLineBrush");
            Grid.SetRow(footer, 3);

            DockPanel footerDock = new();

            _statusText = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 480
            };
            _statusText.SetResourceReference(ForegroundProperty, "HwMutedTextBrush");
            StackPanel buttons = new()
            {
                Orientation = Orientation.Horizontal
            };
            DockPanel.SetDock(buttons, Dock.Right);

            Button close = new()
            {
                Content = "CLOSE",
                Width = 100,
                Margin = new Thickness(0, 0, 8, 0)
            };
            close.Click += (_, _) => Close();

            _deleteButton = new Button
            {
                Content = "DELETE SELECTED",
                Width = 155,
                IsEnabled = false
            };
            _deleteButton.Click += DeleteSelected_Click;

            buttons.Children.Add(close);
            buttons.Children.Add(_deleteButton);
            footerDock.Children.Add(buttons);
            footerDock.Children.Add(_statusText);
            footer.Child = footerDock;
            root.Children.Add(footer);

            Content = root;

            Loaded += (_, _) =>
            {
                HaloWarsThemeService.Apply(this);
                LoadRecords();
            };
        }

        private void LoadRecords()
        {
            _meshList.Items.Clear();

            if (string.IsNullOrWhiteSpace(_eraPath) ||
                !File.Exists(_eraPath))
            {
                _statusText.Text = "The current ERA file is unavailable.";
                _deleteButton.IsEnabled = false;
                return;
            }

            IReadOnlyList<CustomMeshPendingAssetService.EmbeddedCustomMeshRecord> records;

            try
            {
                records = CustomMeshPendingAssetService.LoadEmbedded(_eraPath);
            }
            catch (Exception ex)
            {
                _statusText.Text = "Unable to read the custom-mesh registry: " + ex.Message;
                _deleteButton.IsEnabled = false;
                return;
            }

            if (records.Count == 0)
            {
                _statusText.Text =
                    "No Ensemble-managed custom meshes are embedded in this ERA yet. " +
                    "Meshes imported with v31 or newer will appear here after the ERA is saved.";
                _deleteButton.IsEnabled = false;
                return;
            }

            foreach (CustomMeshPendingAssetService.EmbeddedCustomMeshRecord record in records)
            {
                ListBoxItem item = new()
                {
                    Tag = record,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0)
                };

                Border itemBorder = new()
                {
                    Padding = new Thickness(12, 10, 12, 10),
                    BorderThickness = new Thickness(0, 0, 0, 1)
                };
                itemBorder.SetResourceReference(Border.BorderBrushProperty, "HwLineBrush");

                Grid grid = new();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                StackPanel text = new();

                TextBlock name = new()
                {
                    Text = string.IsNullOrWhiteSpace(record.DisplayName)
                        ? "Custom Mesh"
                        : record.DisplayName,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 14
                };
                name.SetResourceReference(TextBlock.ForegroundProperty, "HwTextBrush");

                TextBlock path = new()
                {
                    Text = record.UgxArchivePath,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10,
                    Margin = new Thickness(0, 4, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                };
                path.SetResourceReference(TextBlock.ForegroundProperty, "HwMutedTextBrush");

                TextBlock template = new()
                {
                    Text = "Stock template: " +
                           (string.IsNullOrWhiteSpace(record.TemplateEraHint)
                               ? "unknown"
                               : record.TemplateEraHint),
                    FontSize = 10,
                    Margin = new Thickness(0, 3, 0, 0)
                };
                template.SetResourceReference(TextBlock.ForegroundProperty, "HwAccentBrightBrush");

                text.Children.Add(name);
                text.Children.Add(path);
                text.Children.Add(template);
                Grid.SetColumn(text, 0);
                grid.Children.Add(text);

                StackPanel idPanel = new()
                {
                    Margin = new Thickness(16, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                TextBlock idLabel = new()
                {
                    Text = "SC2 OBJECT ID",
                    FontSize = 9,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                idLabel.SetResourceReference(TextBlock.ForegroundProperty, "HwMutedTextBrush");

                TextBlock id = new()
                {
                    Text = record.ArtObjectId.ToString(),
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                id.SetResourceReference(TextBlock.ForegroundProperty, "HwAccentBrightBrush");

                idPanel.Children.Add(idLabel);
                idPanel.Children.Add(id);
                Grid.SetColumn(idPanel, 1);
                grid.Children.Add(idPanel);

                itemBorder.Child = grid;
                item.Content = itemBorder;
                _meshList.Items.Add(item);
            }

            _statusText.Text =
                $"{records.Count:N0} embedded custom mesh(es). " +
                "Delete removes the SC2 placement and restores the borrowed UGX slot to its original Halo Wars model on the next save.";
        }

        private void DeleteSelected_Click(
            object sender,
            RoutedEventArgs e)
        {
            List<CustomMeshPendingAssetService.EmbeddedCustomMeshRecord> selected =
                _meshList.SelectedItems
                    .OfType<ListBoxItem>()
                    .Select(item => item.Tag)
                    .OfType<CustomMeshPendingAssetService.EmbeddedCustomMeshRecord>()
                    .ToList();

            if (selected.Count == 0)
                return;

            string names =
                string.Join(
                    "\n",
                    selected.Take(8).Select(record => "• " + record.DisplayName));

            if (selected.Count > 8)
                names += $"\n• ...and {selected.Count - 8} more";

            MessageBoxResult choice =
                MessageBox.Show(
                    this,
                    "Queue the selected custom mesh(es) for deletion?\n\n" +
                    names +
                    "\n\nThe change is applied when you save the map ERA. " +
                    "Ensemble will remove the SC2 placement and restore the borrowed stock UGX geometry.",
                    "Delete Embedded Custom Mesh",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (choice != MessageBoxResult.Yes)
                return;

            foreach (CustomMeshPendingAssetService.EmbeddedCustomMeshRecord record in selected)
            {
                if (!_queuedRecords.Any(
                        existing =>
                            existing.ArtObjectId == record.ArtObjectId &&
                            CustomMeshPendingAssetService.NormalizeArchivePath(existing.UgxArchivePath)
                                .Equals(
                                    CustomMeshPendingAssetService.NormalizeArchivePath(record.UgxArchivePath),
                                    StringComparison.OrdinalIgnoreCase)))
                {
                    _queuedRecords.Add(record);
                }
            }

            foreach (ListBoxItem item in _meshList.SelectedItems
                         .OfType<ListBoxItem>()
                         .ToList())
            {
                _meshList.Items.Remove(item);
            }

            _deleteButton.IsEnabled = false;
            _statusText.Text =
                $"{QueuedDeletionCount:N0} deletion(s) queued. Close this window and save the map (Ctrl+S) to apply them.";
        }
    }
}
