using Ensemble.Models;
using Ensemble.Services;
using System.Globalization;
using System.Windows;

namespace Ensemble
{
    public partial class ArtObjectPropertiesWindow : Window
    {
        internal string EditorNameValue { get; private set; } = string.Empty;
        internal int GroupValue { get; private set; }
        internal int VisualVariationValue { get; private set; }

        internal ArtObjectPropertiesWindow(ScenarioArtObject artObject)
        {
            InitializeComponent();
            TypeText.Text = artObject.Type;
            IdText.Text = artObject.Id.ToString(CultureInfo.InvariantCulture);
            EditorNameText.Text = artObject.EditorName;
            GroupText.Text = artObject.Group.ToString(CultureInfo.InvariantCulture);
            VariationText.Text = artObject.VisualVariationIndex.ToString(CultureInfo.InvariantCulture);
            FlagsText.Text = artObject.Flags.Count == 0 ? "None" : string.Join(Environment.NewLine, artObject.Flags);
            Loaded += (_, _) => HaloWarsThemeService.Apply(this);
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(GroupText.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int group))
            {
                MessageBox.Show(this, "Group must be a whole number.", "Invalid Group", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(VariationText.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int variation))
            {
                MessageBox.Show(this, "Visual Variation must be a whole number.", "Invalid Variation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            EditorNameValue = EditorNameText.Text?.Trim() ?? string.Empty;
            GroupValue = group;
            VisualVariationValue = variation;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
