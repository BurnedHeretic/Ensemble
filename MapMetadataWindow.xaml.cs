using Ensemble.Models;
using System.Windows;

namespace Ensemble
{
    public partial class MapMetadataWindow :
        Window
    {
        public MapMetadata? Metadata
        {
            get;
            private set;
        }


        public MapMetadataWindow(
            string eraFileName,
            MapMetadata metadata)
        {
            InitializeComponent();


            InternalEraText.Text =
                eraFileName;


            DisplayNameTextBox.Text =
                metadata.DisplayName;


            DescriptionTextBox.Text =
                metadata.Description;


            Loaded +=
                (_, _) =>
                {
                    DisplayNameTextBox.Focus();

                    DisplayNameTextBox.SelectAll();
                };
        }


        private void SaveButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string displayName =
                DisplayNameTextBox.Text
                    .Trim();


            if (string.IsNullOrWhiteSpace(
                    displayName))
            {
                MessageBox.Show(
                    this,
                    "Enter a map display name.",
                    "Map Metadata",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            Metadata =
                new MapMetadata
                {
                    DisplayName =
                        displayName,

                    Description =
                        DescriptionTextBox.Text
                            .Trim()
                };


            DialogResult =
                true;
        }
    }
}