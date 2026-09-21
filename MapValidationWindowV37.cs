using Ensemble.Services;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ensemble
{
    internal enum MapValidationSeverityV37
    {
        Pass,
        Warning,
        Error
    }

    internal sealed class MapValidationItemV37
    {
        public required MapValidationSeverityV37 Severity
        {
            get;
            init;
        }

        public required string Title
        {
            get;
            init;
        }

        public required string Detail
        {
            get;
            init;
        }
    }

    internal sealed class MapValidationWindowV37 :
        Window
    {
        private readonly IReadOnlyList<MapValidationItemV37>
            _items;

        public MapValidationWindowV37(
            string mapName,
            IReadOnlyList<MapValidationItemV37> items)
        {
            _items =
                items
                ??
                throw new ArgumentNullException(
                    nameof(items));

            Title =
                "Validate Current Map";

            Width =
                760;

            Height =
                650;

            MinWidth =
                650;

            MinHeight =
                500;

            WindowStartupLocation =
                WindowStartupLocation.CenterOwner;

            ShowInTaskbar =
                false;

            HaloWarsThemeService.Apply(
                this);

            Grid root =
                new Grid
                {
                    Margin =
                        new Thickness(
                            18)
                };

            root.RowDefinitions.Add(
                new RowDefinition
                {
                    Height =
                        GridLength.Auto
                });

            root.RowDefinitions.Add(
                new RowDefinition
                {
                    Height =
                        GridLength.Auto
                });

            root.RowDefinitions.Add(
                new RowDefinition
                {
                    Height =
                        new GridLength(
                            1,
                            GridUnitType.Star)
                });

            root.RowDefinitions.Add(
                new RowDefinition
                {
                    Height =
                        GridLength.Auto
                });

            TextBlock heading =
                new TextBlock
                {
                    Text =
                        "MAP VALIDATION",

                    FontSize =
                        19,

                    FontWeight =
                        FontWeights.Bold,

                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            6)
                };

            root.Children.Add(
                heading);

            int errors =
                items.Count(
                    item =>
                        item.Severity ==
                        MapValidationSeverityV37.Error);

            int warnings =
                items.Count(
                    item =>
                        item.Severity ==
                        MapValidationSeverityV37.Warning);

            int passes =
                items.Count(
                    item =>
                        item.Severity ==
                        MapValidationSeverityV37.Pass);

            TextBlock summary =
                new TextBlock
                {
                    Text =
                        $"{mapName}   |   {passes} passed   |   {warnings} warning(s)   |   {errors} error(s)",

                    TextWrapping =
                        TextWrapping.Wrap,

                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            12)
                };

            Grid.SetRow(
                summary,
                1);

            root.Children.Add(
                summary);

            ListBox list =
                new ListBox
                {
                    BorderThickness =
                        new Thickness(
                            0),

                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            12)
                };

            foreach (MapValidationItemV37 item
                     in items)
            {
                Border card =
                    new Border
                    {
                        BorderThickness =
                            new Thickness(
                                1),

                        Padding =
                            new Thickness(
                                10),

                        Margin =
                            new Thickness(
                                0,
                                0,
                                0,
                                7),

                        Child =
                            BuildCard(
                                item)
                    };

                list.Items.Add(
                    card);
            }

            Grid.SetRow(
                list,
                2);

            root.Children.Add(
                list);

            StackPanel buttons =
                new StackPanel
                {
                    Orientation =
                        Orientation.Horizontal,

                    HorizontalAlignment =
                        HorizontalAlignment.Right
                };

            Button copy =
                new Button
                {
                    Content =
                        "COPY REPORT",

                    MinWidth =
                        115,

                    MinHeight =
                        32,

                    Margin =
                        new Thickness(
                            0,
                            0,
                            8,
                            0)
                };

            copy.Click +=
                (
                    _,
                    _) =>
                {
                    Clipboard.SetText(
                        BuildPlainTextReport(
                            mapName,
                            items));
                };

            Button close =
                new Button
                {
                    Content =
                        "CLOSE",

                    MinWidth =
                        92,

                    MinHeight =
                        32,

                    IsDefault =
                        true,

                    IsCancel =
                        true
                };

            buttons.Children.Add(
                copy);

            buttons.Children.Add(
                close);

            Grid.SetRow(
                buttons,
                3);

            root.Children.Add(
                buttons);

            Content =
                root;
        }

        private static StackPanel BuildCard(
            MapValidationItemV37 item)
        {
            StackPanel panel =
                new StackPanel();

            string prefix =
                item.Severity switch
                {
                    MapValidationSeverityV37.Error =>
                        "ERROR",

                    MapValidationSeverityV37.Warning =>
                        "WARNING",

                    _ =>
                        "PASS"
                };

            TextBlock title =
                new TextBlock
                {
                    Text =
                        $"{prefix}  |  {item.Title}",

                    FontWeight =
                        FontWeights.Bold
                };

            TextBlock detail =
                new TextBlock
                {
                    Text =
                        item.Detail,

                    TextWrapping =
                        TextWrapping.Wrap,

                    Opacity =
                        0.82,

                    Margin =
                        new Thickness(
                            0,
                            4,
                            0,
                            0)
                };

            panel.Children.Add(
                title);

            panel.Children.Add(
                detail);

            return panel;
        }

        private static string BuildPlainTextReport(
            string mapName,
            IReadOnlyList<MapValidationItemV37> items)
        {
            StringBuilder text =
                new StringBuilder();

            text.AppendLine(
                "ENSEMBLE MAP VALIDATION");

            text.AppendLine(
                mapName);

            text.AppendLine();

            foreach (MapValidationItemV37 item
                     in items)
            {
                text.Append(
                    '[');

                text.Append(
                    item.Severity.ToString()
                        .ToUpperInvariant());

                text.Append(
                    "] ");

                text.AppendLine(
                    item.Title);

                text.AppendLine(
                    item.Detail);

                text.AppendLine();
            }

            return text.ToString();
        }
    }
}
