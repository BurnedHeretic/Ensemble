using Ensemble.Models;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Ensemble.Controls
{
    public sealed class MapCanvas : Canvas
    {
        private const double MarginSize = 34;

        private ScenarioMap? _map;

        private object? _selectedItem;

        private object? _draggedItem;

        private Vector3 _dragStartPosition;

        private bool _isDragging;

        public event EventHandler<ScenarioSelectionChangedEventArgs>?
            SelectionChanged;

        public event EventHandler<ScenarioItemMovedEventArgs>?
            ItemMoved;

        public event EventHandler<ScenarioItemRotatedEventArgs>?
            ItemRotated;

        public event EventHandler<ScenarioSphereRadiusChangedEventArgs>?
            SphereRadiusChanged;

        public event EventHandler<ScenarioObjectPropertiesChangedEventArgs>?
            ObjectPropertiesChanged;

        public MapCanvas()
        {
            Background =
                new SolidColorBrush(
                    Color.FromRgb(
                        24,
                        24,
                        24));

            ClipToBounds =
                true;

            SizeChanged +=
                (_, _) =>
                    RenderMap();

            // Use preview mouse-up so we catch the release
            // even when it occurs over one of our child markers.
            PreviewMouseLeftButtonUp +=
                MapCanvas_PreviewMouseLeftButtonUp;

            // Safety fallback in case WPF releases capture
            // before the normal MouseUp reaches us.
            LostMouseCapture +=
                MapCanvas_LostMouseCapture;
        }

        public ScenarioMap? Scenario
            => _map;

        public void SetMap(
            ScenarioMap map)
        {
            _map =
                map
                ?? throw new ArgumentNullException(
                    nameof(map));

            _selectedItem =
                null;

            RenderMap();

            SelectionChanged?.Invoke(
                this,
                new ScenarioSelectionChangedEventArgs(
                    null));
        }

        protected override void OnMouseLeftButtonDown(
            MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            if (!e.Handled)
            {
                SelectItem(
                    null);
            }
        }

        private void RenderMap()
        {
            Children.Clear();

            if (_map == null)
                return;

            if (ActualWidth < 100 ||
                ActualHeight < 100)
            {
                return;
            }

            DrawGrid();

            DrawPaths();

            DrawSpheres();

            // Draw generic/resource markers first so important
            // gameplay objects remain visible on top.
            foreach (ScenarioObject obj
                     in _map.Objects
                         .OrderBy(
                             GetRenderPriority))
            {
                DrawObject(
                    obj);
            }

            foreach (ScenarioPlayerStart start
                     in _map.PlayerStarts)
            {
                DrawPlayerStart(
                    start);
            }

            DrawMapTitle();
            DrawLegend();
        }

        // =========================================================
        // GRID
        // =========================================================

        private void DrawGrid()
        {
            if (_map == null)
                return;

            Rectangle border =
                new Rectangle
                {
                    Width =
                        MapWidth,

                    Height =
                        MapHeight,

                    Stroke =
                        new SolidColorBrush(
                            Color.FromRgb(
                                90,
                                90,
                                90)),

                    StrokeThickness =
                        1,

                    Fill =
                        new SolidColorBrush(
                            Color.FromRgb(
                                30,
                                30,
                                30)),

                    IsHitTestVisible =
                        false
                };

            SetLeft(
                border,
                MarginSize);

            SetTop(
                border,
                MarginSize);

            Children.Add(
                border);

            const int divisions =
                8;

            for (int i = 1;
                 i < divisions;
                 i++)
            {
                double x =
                    MarginSize +
                    (MapWidth / divisions) *
                    i;

                double y =
                    MarginSize +
                    (MapHeight / divisions) *
                    i;

                Line vertical =
                    new Line
                    {
                        X1 = x,
                        X2 = x,
                        Y1 = MarginSize,
                        Y2 = MarginSize + MapHeight,
                        Stroke =
                            new SolidColorBrush(
                                Color.FromRgb(
                                    45,
                                    45,
                                    45)),
                        StrokeThickness =
                            1,
                        IsHitTestVisible =
                            false
                    };

                Line horizontal =
                    new Line
                    {
                        X1 = MarginSize,
                        X2 = MarginSize + MapWidth,
                        Y1 = y,
                        Y2 = y,
                        Stroke =
                            new SolidColorBrush(
                                Color.FromRgb(
                                    45,
                                    45,
                                    45)),
                        StrokeThickness =
                            1,
                        IsHitTestVisible =
                            false
                    };

                Children.Add(
                    vertical);

                Children.Add(
                    horizontal);
            }
        }

        // =========================================================
        // PATHS
        // =========================================================

        private void DrawPaths()
        {
            if (_map == null)
                return;

            foreach (ScenarioPath path
                     in _map.Paths)
            {
                if (path.Points.Count <
                    2)
                {
                    continue;
                }

                Polyline line =
                    new Polyline
                    {
                        Stroke =
                            new SolidColorBrush(
                                Color.FromRgb(
                                    255,
                                    120,
                                    70)),

                        StrokeThickness =
                            2,

                        StrokeDashArray =
                            new DoubleCollection
                            {
                                5,
                                3
                            },

                        Tag =
                            path,

                        ToolTip =
                            $"{path.Name}\n" +
                            $"{path.Type}\n" +
                            $"{path.Points.Count} points"
                    };

                foreach (var point
                         in path.Points)
                {
                    Point screen =
                        WorldToScreen(
                            point.X,
                            point.Z);

                    line.Points.Add(
                        screen);
                }

                line.MouseLeftButtonDown +=
                    SelectMarker_MouseLeftButtonDown;

                Children.Add(
                    line);
            }
        }

        // =========================================================
        // DESIGN SPHERES
        // =========================================================

        private void DrawSpheres()
        {
            if (_map == null)
                return;

            double worldScale =
                Math.Min(
                    MapWidth /
                    Math.Max(
                        1,
                        _map.MaxX -
                        _map.MinX),

                    MapHeight /
                    Math.Max(
                        1,
                        _map.MaxZ -
                        _map.MinZ));

            foreach (ScenarioSphere sphere
                     in _map.Spheres)
            {
                Point centre =
                    WorldToScreen(
                        sphere.Position.X,
                        sphere.Position.Z);

                double diameter =
                    Math.Max(
                        6,
                        sphere.Radius *
                        2 *
                        worldScale);

                bool selected =
                    ReferenceEquals(
                        sphere,
                        _selectedItem);

                Ellipse circle =
                    new Ellipse
                    {
                        Width =
                            diameter,

                        Height =
                            diameter,

                        Stroke =
                            selected
                                ? Brushes.White
                                : new SolidColorBrush(
                                    Color.FromRgb(
                                        255,
                                        80,
                                        80)),

                        StrokeThickness =
                            selected
                                ? 3
                                : 1.5,

                        Fill =
                            new SolidColorBrush(
                                Color.FromArgb(
                                    24,
                                    255,
                                    80,
                                    80)),

                        Tag =
                            sphere,

                        ToolTip =
                            $"{sphere.Name}\n" +
                            $"Type: {sphere.Type}\n" +
                            $"Radius: {sphere.Radius:0.##}"
                    };

                circle.MouseLeftButtonDown +=
                    SelectMarker_MouseLeftButtonDown;

                SetLeft(
                    circle,
                    centre.X -
                    diameter / 2);

                SetTop(
                    circle,
                    centre.Y -
                    diameter / 2);

                Children.Add(
                    circle);
            }
        }

        public void ChangeSphereRadiusFromEditor(
            ScenarioSphere sphere,
            float newRadius)
        {
            if (newRadius < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(newRadius),
                    "Sphere radius cannot be negative.");
            }

            float oldRadius =
                sphere.Radius;

            if (MathF.Abs(
                    oldRadius -
                    newRadius) <
                0.0001f)
            {
                return;
            }

            sphere.Radius =
                newRadius;

            _selectedItem =
                sphere;

            RenderMap();

            SelectionChanged?.Invoke(
                this,
                new ScenarioSelectionChangedEventArgs(
                    sphere));

            SphereRadiusChanged?.Invoke(
                this,
                new ScenarioSphereRadiusChangedEventArgs(
                    sphere,
                    oldRadius,
                    newRadius));
        }

        public void ApplyHistorySphereRadius(
            ScenarioSphere sphere,
            float radius)
        {
            sphere.Radius =
                radius;

            _selectedItem =
                sphere;

            RenderMap();

            SelectionChanged?.Invoke(
                this,
                new ScenarioSelectionChangedEventArgs(
                    sphere));
        }

        // =========================================================
        // SCENARIO OBJECTS
        // =========================================================

        private void DrawObject(
            ScenarioObject obj)
        {
            Point point =
                WorldToScreen(
                    obj.Position.X,
                    obj.Position.Z);

            Brush colour =
                GetObjectBrush(
                    obj);

            double size =
                GetObjectSize(
                    obj);

            bool selected =
                ReferenceEquals(
                    obj,
                    _selectedItem);

            if (selected)
            {
                DrawDirectionIndicator(
                    point,
                    obj.Forward);
            }

            Ellipse marker =
                new Ellipse
                {
                    Width =
                        size,

                    Height =
                        size,

                    Fill =
                        colour,

                    Stroke =
                        selected
                            ? Brushes.White
                            : Brushes.Black,

                    StrokeThickness =
                        selected
                            ? 3
                            : 1,

                    Tag =
                        obj,

                    ToolTip =
                        $"{obj.EditorName}\n" +
                        $"{obj.Type}\n" +
                        $"ID: {obj.Id}\n" +
                        $"X {obj.Position.X:0.##}, " +
                        $"Y {obj.Position.Y:0.##}, " +
                        $"Z {obj.Position.Z:0.##}"
                };

            marker.MouseLeftButtonDown +=
                SelectMarker_MouseLeftButtonDown;

            SetLeft(
                marker,
                point.X -
                size / 2);

            SetTop(
                marker,
                point.Y -
                size / 2);

            Children.Add(
                marker);
        }

        private static int GetRenderPriority(
            ScenarioObject obj)
        {
            return obj.Category switch
            {
                "Crate" => 0,
                "Rebel Marker" => 1,
                "Creep" => 2,
                "Object" => 3,
                "Sniper Platform" => 4,
                "Supply" => 5,
                "Reactor" => 6,
                "Teleporter" => 7,
                "Base" => 8,
                _ => 3
            };
        }

        private static double GetObjectSize(
            ScenarioObject obj)
        {
            return obj.Category switch
            {
                "Base" => 18,
                "Reactor" => 14,
                "Supply" => 14,
                "Teleporter" => 14,
                "Sniper Platform" => 12,
                "Creep" => 9,
                "Rebel Marker" => 8,
                "Crate" => 6,
                _ => 8
            };
        }

        private static Brush GetObjectBrush(
            ScenarioObject obj)
        {
            return obj.Category switch
            {
                "Base" =>
                    Brushes.Orange,

                "Reactor" =>
                    Brushes.Gold,

                "Supply" =>
                    Brushes.LimeGreen,

                "Teleporter" =>
                    Brushes.MediumPurple,

                "Sniper Platform" =>
                    Brushes.DeepSkyBlue,

                "Creep" =>
                    Brushes.IndianRed,

                "Rebel Marker" =>
                    Brushes.Crimson,

                "Crate" =>
                    Brushes.Gray,

                _ =>
                    Brushes.White
            };
        }

        // =========================================================
        // PLAYER STARTS
        // =========================================================

        private void DrawPlayerStart(
            ScenarioPlayerStart start)
        {
            Point point =
                WorldToScreen(
                    start.Position.X,
                    start.Position.Z);

            bool selected =
                ReferenceEquals(
                    start,
                    _selectedItem);

            if (selected)
            {
                DrawDirectionIndicator(
                    point,
                    start.Forward);
            }

            Border marker =
                new Border
                {
                    Width =
                        26,

                    Height =
                        26,

                    Background =
                        Brushes.DodgerBlue,

                    BorderBrush =
                        selected
                            ? Brushes.White
                            : Brushes.Black,

                    BorderThickness =
                        new Thickness(
                            selected
                                ? 3
                                : 1),

                    CornerRadius =
                        new CornerRadius(
                            13),

                    Tag =
                        start,

                    ToolTip =
                        $"Player Start {start.Number}\n" +
                        $"X {start.Position.X:0.##}, " +
                        $"Y {start.Position.Y:0.##}, " +
                        $"Z {start.Position.Z:0.##}",

                    Child =
                        new TextBlock
                        {
                            Text =
                                $"P{start.Number}",

                            Foreground =
                                Brushes.White,

                            FontWeight =
                                FontWeights.Bold,

                            FontSize =
                                11,

                            HorizontalAlignment =
                                HorizontalAlignment.Center,

                            VerticalAlignment =
                                VerticalAlignment.Center
                        }
                };

            marker.MouseLeftButtonDown +=
                SelectMarker_MouseLeftButtonDown;

            SetLeft(
                marker,
                point.X -
                marker.Width / 2);

            SetTop(
                marker,
                point.Y -
                marker.Height / 2);

            Children.Add(
                marker);
        }

        // =========================================================
        // TITLE / LEGEND
        // =========================================================

        private void DrawMapTitle()
        {
            if (_map == null)
                return;

            TextBlock text =
                new TextBlock
                {
                    Text =
                        $"{_map.Name}  " +
                        $"| {_map.Objects.Count} objects  " +
                        $"| {_map.PlayerStarts.Count} starts",

                    Foreground =
                        Brushes.White,

                    FontWeight =
                        FontWeights.Bold,

                    FontSize =
                        14,

                    IsHitTestVisible =
                        false
                };

            SetLeft(
                text,
                MarginSize);

            SetTop(
                text,
                8);

            Children.Add(
                text);
        }

        private void DrawLegend()
        {
            StackPanel panel =
                new StackPanel
                {
                    Orientation =
                        Orientation.Horizontal,

                    Background =
                        new SolidColorBrush(
                            Color.FromArgb(
                                210,
                                25,
                                25,
                                25))
                };

            AddLegendItem(
                panel,
                Brushes.DodgerBlue,
                "Player");

            AddLegendItem(
                panel,
                Brushes.Orange,
                "Base");

            AddLegendItem(
                panel,
                Brushes.Gold,
                "Reactor");

            AddLegendItem(
                panel,
                Brushes.LimeGreen,
                "Supply");

            AddLegendItem(
                panel,
                Brushes.MediumPurple,
                "Teleporter");

            AddLegendItem(
                panel,
                Brushes.DeepSkyBlue,
                "Sniper");

            AddLegendItem(
                panel,
                Brushes.IndianRed,
                "Creep");

            SetLeft(
                panel,
                MarginSize + 8);

            SetTop(
                panel,
                ActualHeight - 28);

            Children.Add(
                panel);
        }

        private static void AddLegendItem(
            Panel panel,
            Brush brush,
            string name)
        {
            StackPanel item =
                new StackPanel
                {
                    Orientation =
                        Orientation.Horizontal,

                    Margin =
                        new Thickness(
                            4,
                            2,
                            6,
                            2)
                };

            item.Children.Add(
                new Ellipse
                {
                    Width =
                        8,

                    Height =
                        8,

                    Fill =
                        brush,

                    Margin =
                        new Thickness(
                            0,
                            4,
                            4,
                            0)
                });

            item.Children.Add(
                new TextBlock
                {
                    Text =
                        name,

                    Foreground =
                        Brushes.White,

                    FontSize =
                        10
                });

            panel.Children.Add(
                item);
        }

        private void DrawDirectionIndicator(
    Point origin,
    Vector3 forward)
        {
            double magnitude =
                Math.Sqrt(
                    forward.X * forward.X +
                    forward.Z * forward.Z);

            if (magnitude <
                0.000001)
            {
                return;
            }

            const double length =
                32;

            double dx =
                (forward.X / magnitude) *
                length;

            // Screen Y is inverted relative to world Z.
            double dy =
                -(forward.Z / magnitude) *
                length;

            Line direction =
                new Line
                {
                    X1 =
                        origin.X,

                    Y1 =
                        origin.Y,

                    X2 =
                        origin.X + dx,

                    Y2 =
                        origin.Y + dy,

                    Stroke =
                        Brushes.White,

                    StrokeThickness =
                        2,

                    IsHitTestVisible =
                        false
                };

            Children.Add(
                direction);

            Ellipse tip =
                new Ellipse
                {
                    Width =
                        5,

                    Height =
                        5,

                    Fill =
                        Brushes.White,

                    IsHitTestVisible =
                        false
                };

            SetLeft(
                tip,
                origin.X + dx - 2.5);

            SetTop(
                tip,
                origin.Y + dy - 2.5);

            Children.Add(
                tip);
        }

        // =========================================================
        // SELECTION
        // =========================================================

        private void SelectMarker_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element)
                return;

            if (element.Tag == null)
                return;

            object item =
                element.Tag;

            SelectItem(
                item);

            if (CanDragItem(
                    item))
            {
                BeginDrag(
                    item);
            }

            e.Handled =
                true;
        }

        private void SelectItem(
            object? item)
        {
            if (ReferenceEquals(
                    item,
                    _selectedItem))
            {
                return;
            }

            _selectedItem =
                item;

            RenderMap();

            SelectionChanged?.Invoke(
                this,
                new ScenarioSelectionChangedEventArgs(
                    item));
        }

        private static bool CanDragItem(
    object item)
        {
            return
                item is ScenarioObject ||
                item is ScenarioPlayerStart ||
                item is ScenarioSphere;
        }

        private void BeginDrag(
            object item)
        {
            if (!CanDragItem(
                    item))
            {
                return;
            }

            _draggedItem =
                item;

            _dragStartPosition =
                GetItemPosition(
                    item);

            _isDragging =
                true;

            Cursor =
                Cursors.SizeAll;

            CaptureMouse();
        }

        protected override void OnMouseMove(
            MouseEventArgs e)
        {
            base.OnMouseMove(
                e);

            if (!_isDragging ||
                _draggedItem == null ||
                _map == null)
            {
                return;
            }

            if (e.LeftButton !=
                MouseButtonState.Pressed)
            {
                FinishDrag();

                return;
            }

            Point mousePosition =
                e.GetPosition(
                    this);

            (float worldX, float worldZ) =
                ScreenToWorld(
                    mousePosition);

            Vector3 oldPosition =
                GetItemPosition(
                    _draggedItem);

            Vector3 newPosition =
                new Vector3(
                    worldX,

                    // IMPORTANT:
                    // Keep the original terrain height.
                    oldPosition.Y,

                    worldZ);

            if (Math.Abs(
                    newPosition.X -
                    oldPosition.X) <
                0.0001f &&
                Math.Abs(
                    newPosition.Z -
                    oldPosition.Z) <
                0.0001f)
            {
                return;
            }

            SetItemPosition(
                _draggedItem,
                newPosition);

            // Re-render so the marker follows the cursor.
            RenderMap();

            // Refresh the properties panel live.
            SelectionChanged?.Invoke(
                this,
                new ScenarioSelectionChangedEventArgs(
                    _draggedItem));

            e.Handled =
                true;
        }

        private void MapCanvas_PreviewMouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            if (!_isDragging)
                return;

            FinishDrag();

            e.Handled =
                true;
        }

        private void MapCanvas_LostMouseCapture(
            object sender,
            MouseEventArgs e)
        {
            if (!_isDragging)
                return;

            FinishDrag(
                releaseCapture: false);
        }

        private void FinishDrag(bool releaseCapture = true)
        {
            if (!_isDragging)
                return;

            object? item =
                _draggedItem;

            Vector3 oldPosition =
                _dragStartPosition;

            Vector3 newPosition =
                item != null
                    ? GetItemPosition(item)
                    : oldPosition;

            // IMPORTANT:
            // Clear drag state BEFORE releasing mouse capture.
            // ReleaseMouseCapture can itself raise LostMouseCapture.
            _isDragging =
                false;

            _draggedItem =
                null;

            Cursor =
                null;

            if (releaseCapture &&
                IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            if (item == null)
                return;

            bool actuallyMoved =
                Vector3.DistanceSquared(
                    oldPosition,
                    newPosition) >
                0.0001f;

            if (!actuallyMoved)
                return;

            ItemMoved?.Invoke(
                this,
                new ScenarioItemMovedEventArgs(
                    item,
                    oldPosition,
                    newPosition));
        }

        public void MoveItemFromEditor(
            object item,
            Vector3 newPosition)
        {
            if (!CanDragItem(item))
            {
                throw new InvalidOperationException(
                    $"Object type {item.GetType().Name} cannot be moved.");
            }

            Vector3 oldPosition =
                GetItemPosition(item);

            if (Vector3.DistanceSquared(
                    oldPosition,
                    newPosition) <
                0.000001f)
            {
                return;
            }

            SetItemPosition(
                item,
                newPosition);

            _selectedItem =
                item;

            RenderMap();

            // Refresh the right-side properties.
            SelectionChanged?.Invoke(
                this,
                new ScenarioSelectionChangedEventArgs(
                    item));

            // IMPORTANT:
            // Raise the same event a completed mouse drag raises.
            // This means MainWindow automatically adds this
            // coordinate change to Undo/Redo history.
            ItemMoved?.Invoke(
                this,
                new ScenarioItemMovedEventArgs(
                    item,
                    oldPosition,
                    newPosition));
        }

        public void ChangeObjectPropertiesFromEditor(
            ScenarioObject obj,
            int player,
            int group,
            int visualVariationIndex)
        {
            int oldPlayer =
                obj.Player;

            int oldGroup =
                obj.Group;

            int oldVisualVariationIndex =
                obj.VisualVariationIndex;

            if (oldPlayer == player &&
                oldGroup == group &&
                oldVisualVariationIndex == visualVariationIndex)
            {
                return;
            }

            obj.Player =
                player;

            obj.Group =
                group;

            obj.VisualVariationIndex =
                visualVariationIndex;

            _selectedItem =
                obj;

            RenderMap();

            SelectionChanged?.Invoke(
                this,
                new ScenarioSelectionChangedEventArgs(
                    obj));

            ObjectPropertiesChanged?.Invoke(
                this,
                new ScenarioObjectPropertiesChangedEventArgs(
                    obj,
                    oldPlayer,
                    oldGroup,
                    oldVisualVariationIndex,
                    player,
                    group,
                    visualVariationIndex));
        }

        public void ApplyHistoryObjectProperties(
            ScenarioObject obj,
            int player,
            int group,
            int visualVariationIndex)
        {
            obj.Player =
                player;

            obj.Group =
                group;

            obj.VisualVariationIndex =
                visualVariationIndex;

            _selectedItem =
                obj;

            RenderMap();

            SelectionChanged?.Invoke(
                this,
                new ScenarioSelectionChangedEventArgs(
                    obj));
        }

        public void ApplyHistoryPosition(
            object item,
            Vector3 position)
        {
            if (!CanDragItem(item))
            {
                throw new InvalidOperationException(
                    $"Object type {item.GetType().Name} cannot be moved.");
            }

            SetItemPosition(
                item,
                position);

            _selectedItem =
                item;

            RenderMap();

            SelectionChanged?.Invoke(
                this,
                new ScenarioSelectionChangedEventArgs(
                    item));
        }

        private static Vector3 GetItemPosition(
            object item)
        {
            return item switch
            {
                ScenarioObject obj =>
                    obj.Position,

                ScenarioPlayerStart start =>
                    start.Position,

                ScenarioSphere sphere =>
                    sphere.Position,

                _ =>
                    throw new InvalidOperationException(
                        $"Object type {item.GetType().Name} " +
                        "does not have an editable map position.")
            };
        }

        private static void SetItemPosition(
            object item,
            Vector3 position)
        {
            switch (item)
            {
                case ScenarioObject obj:
                    obj.Position =
                        position;
                    break;

                case ScenarioPlayerStart start:
                    start.Position =
                        position;
                    break;

                case ScenarioSphere sphere:
                    sphere.Position =
                        position;
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Object type {item.GetType().Name} " +
                        "does not have an editable map position.");
            }
        }

        private static bool CanRotateItem(
    object item)
        {
            return
                item is ScenarioObject ||
                item is ScenarioPlayerStart;
        }

        public static float GetYawDegrees(
            Vector3 forward)
        {
            if (MathF.Abs(forward.X) < 0.000001f &&
                MathF.Abs(forward.Z) < 0.000001f)
            {
                return 0;
            }

            float yaw =
                MathF.Atan2(
                    forward.X,
                    forward.Z)
                * (180.0f / MathF.PI);

            if (yaw < 0)
            {
                yaw += 360.0f;
            }

            return yaw;
        }

        public void RotateItemFromEditor(
            object item,
            float yawDegrees)
        {
            if (!CanRotateItem(item))
            {
                throw new InvalidOperationException(
                    $"Object type {item.GetType().Name} cannot be rotated.");
            }

            Vector3 oldForward =
                GetItemForward(item);

            Vector3 oldRight =
                GetItemRight(item);

            float normalizedYaw =
                yawDegrees % 360.0f;

            if (normalizedYaw < 0)
            {
                normalizedYaw += 360.0f;
            }

            float radians =
                normalizedYaw *
                (MathF.PI / 180.0f);

            Vector3 newForward =
                new Vector3(
                    MathF.Sin(radians),
                    0,
                    MathF.Cos(radians));

            Vector3 newRight =
                new Vector3(
                    newForward.Z,
                    0,
                    -newForward.X);

            if (Vector3.DistanceSquared(
                    oldForward,
                    newForward) <
                0.000001f)
            {
                return;
            }

            SetItemOrientation(
                item,
                newForward,
                newRight);

            _selectedItem =
                item;

            RenderMap();

            SelectionChanged?.Invoke(
                this,
                new ScenarioSelectionChangedEventArgs(
                    item));

            ItemRotated?.Invoke(
                this,
                new ScenarioItemRotatedEventArgs(
                    item,
                    oldForward,
                    oldRight,
                    newForward,
                    newRight));
        }

        public void ApplyHistoryOrientation(
            object item,
            Vector3 forward,
            Vector3 right)
        {
            if (!CanRotateItem(item))
            {
                throw new InvalidOperationException(
                    $"Object type {item.GetType().Name} cannot be rotated.");
            }

            SetItemOrientation(
                item,
                forward,
                right);

            _selectedItem =
                item;

            RenderMap();

            SelectionChanged?.Invoke(
                this,
                new ScenarioSelectionChangedEventArgs(
                    item));
        }

        private static Vector3 GetItemForward(
            object item)
        {
            return item switch
            {
                ScenarioObject obj =>
                    obj.Forward,

                ScenarioPlayerStart start =>
                    start.Forward,

                _ =>
                    Vector3.Zero
            };
        }

        private static Vector3 GetItemRight(
            object item)
        {
            return item switch
            {
                ScenarioObject obj =>
                    obj.Right,

                _ =>
                    Vector3.Zero
            };
        }

        private static void SetItemOrientation(
            object item,
            Vector3 forward,
            Vector3 right)
        {
            switch (item)
            {
                case ScenarioObject obj:

                    obj.Forward =
                        forward;

                    obj.Right =
                        right;

                    break;


                case ScenarioPlayerStart start:

                    start.Forward =
                        forward;

                    break;


                default:

                    throw new InvalidOperationException(
                        $"Object type {item.GetType().Name} cannot be rotated.");
            }
        }

        // =========================================================
        // COORDINATES
        // =========================================================

        private Point WorldToScreen(
            float worldX,
            float worldZ)
        {
            if (_map == null)
                return new Point();

            double width =
                Math.Max(
                    1,
                    _map.MaxX -
                    _map.MinX);

            double depth =
                Math.Max(
                    1,
                    _map.MaxZ -
                    _map.MinZ);

            double normalizedX =
                (worldX -
                 _map.MinX) /
                width;

            double normalizedZ =
                (worldZ -
                 _map.MinZ) /
                depth;

            double x =
                MarginSize +
                normalizedX *
                MapWidth;

            // Higher Z appears toward the top.
            double y =
                MarginSize +
                (1.0 -
                 normalizedZ) *
                MapHeight;

            return new Point(
                x,
                y);
        }

        private (float X, float Z) ScreenToWorld(
    Point screen)
        {
            if (_map == null)
            {
                return (
                    0,
                    0);
            }

            double normalizedX =
                (screen.X -
                 MarginSize) /
                MapWidth;

            double normalizedScreenY =
                (screen.Y -
                 MarginSize) /
                MapHeight;

            normalizedX =
                Math.Clamp(
                    normalizedX,
                    0.0,
                    1.0);

            normalizedScreenY =
                Math.Clamp(
                    normalizedScreenY,
                    0.0,
                    1.0);

            // WorldToScreen flips Z vertically,
            // so reverse it here.
            double normalizedZ =
                1.0 -
                normalizedScreenY;

            float worldX =
                (float)(
                    _map.MinX +
                    normalizedX *
                    (_map.MaxX -
                     _map.MinX));

            float worldZ =
                (float)(
                    _map.MinZ +
                    normalizedZ *
                    (_map.MaxZ -
                     _map.MinZ));

            return (
                worldX,
                worldZ);
        }

        private double MapWidth
            => Math.Max(
                1,
                ActualWidth -
                MarginSize * 2);

        private double MapHeight
            => Math.Max(
                1,
                ActualHeight -
                MarginSize * 2);
    }

    public sealed class ScenarioSelectionChangedEventArgs :
        EventArgs
    {
        public ScenarioSelectionChangedEventArgs(
            object? selectedItem)
        {
            SelectedItem =
                selectedItem;
        }

        public object? SelectedItem
        {
            get;
        }
    }

    public sealed class ScenarioItemMovedEventArgs :
    EventArgs
    {
        public ScenarioItemMovedEventArgs(
            object item,
            Vector3 oldPosition,
            Vector3 newPosition)
        {
            Item =
                item;

            OldPosition =
                oldPosition;

            NewPosition =
                newPosition;
        }

        public object Item
        {
            get;
        }

        public Vector3 OldPosition
        {
            get;
        }

        public Vector3 NewPosition
        {
            get;
        }
    }

    public sealed class ScenarioItemRotatedEventArgs :
    EventArgs
    {
        public ScenarioItemRotatedEventArgs(
            object item,
            Vector3 oldForward,
            Vector3 oldRight,
            Vector3 newForward,
            Vector3 newRight)
        {
            Item =
                item;

            OldForward =
                oldForward;

            OldRight =
                oldRight;

            NewForward =
                newForward;

            NewRight =
                newRight;
        }

        public object Item
        {
            get;
        }

        public Vector3 OldForward
        {
            get;
        }

        public Vector3 OldRight
        {
            get;
        }

        public Vector3 NewForward
        {
            get;
        }

        public Vector3 NewRight
        {
            get;
        }
    }

    public sealed class ScenarioSphereRadiusChangedEventArgs :
    EventArgs
    {
        public ScenarioSphereRadiusChangedEventArgs(
            ScenarioSphere sphere,
            float oldRadius,
            float newRadius)
        {
            Sphere =
                sphere;

            OldRadius =
                oldRadius;

            NewRadius =
                newRadius;
        }

        public ScenarioSphere Sphere
        {
            get;
        }

        public float OldRadius
        {
            get;
        }

        public float NewRadius
        {
            get;
        }
    }

    public sealed class ScenarioObjectPropertiesChangedEventArgs :
    EventArgs
    {
        public ScenarioObjectPropertiesChangedEventArgs(
            ScenarioObject obj,
            int oldPlayer,
            int oldGroup,
            int oldVisualVariationIndex,
            int newPlayer,
            int newGroup,
            int newVisualVariationIndex)
        {
            Object =
                obj;

            OldPlayer =
                oldPlayer;

            OldGroup =
                oldGroup;

            OldVisualVariationIndex =
                oldVisualVariationIndex;

            NewPlayer =
                newPlayer;

            NewGroup =
                newGroup;

            NewVisualVariationIndex =
                newVisualVariationIndex;
        }

        public ScenarioObject Object { get; }

        public int OldPlayer { get; }

        public int OldGroup { get; }

        public int OldVisualVariationIndex { get; }

        public int NewPlayer { get; }

        public int NewGroup { get; }

        public int NewVisualVariationIndex { get; }
    }

}