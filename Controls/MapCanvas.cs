using Ensemble.Models;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Imaging;

namespace Ensemble.Controls
{
    public sealed class MapCanvas : Canvas
    {
        private const double MarginSize = 34;

        private ScenarioMap? _map;

        private TerrainHeightMap?
            _terrainHeightMap;

        private BitmapSource?
            _terrainHeightBitmap;

        private object? _selectedItem;

        // Object dragging state:
        private object? _draggedItem;

        private Vector3 _dragStartPosition;

        private bool _isDragging;

        private bool
            _isDraggingPathPoint;

        private ScenarioPath?
            _draggedPath;

        private int
            _draggedPathPointIndex =
                -1;

        private Vector3
            _pathPointDragStartPosition;

        // Placement state:
        private bool _isPlacementMode;

        private float _viewMinX;
        private float _viewMinZ;
        private float _viewMaxX;
        private float _viewMaxZ;

        // Panning state:
        private bool _isPanning;

        private Point _panStartMouse;

        private float _panStartMinX;
        private float _panStartMinZ;
        private float _panStartMaxX;
        private float _panStartMaxZ;

        private string _placementLabel =
            string.Empty;

        private Vector3 _placementPreviewPosition;

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

        public event EventHandler<ScenarioObjectAddedEventArgs>?
            ObjectAdded;

        public event EventHandler<ScenarioObjectDeletedEventArgs>?
            ObjectDeleted;

        public event EventHandler<ScenarioPlacementRequestedEventArgs>?
            PlacementRequested;

        public event EventHandler<ScenarioPathPointMovedEventArgs>?
            PathPointMoved;

        public bool IsObjectPlacementActive =>
            _isPlacementMode;

        private sealed class PathPointHandle
        {
            public PathPointHandle(
                ScenarioPath path,
                int index)
            {
                Path =
                    path;

                Index =
                    index;
            }

            public ScenarioPath Path
            {
                get;
            }

            public int Index
            {
                get;
            }
        }

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

            PreviewMouseLeftButtonDown +=
                MapCanvas_PreviewMouseLeftButtonDown;

            PreviewMouseRightButtonDown +=
                MapCanvas_PreviewMouseRightButtonDown;

            PreviewMouseWheel +=
                MapCanvas_PreviewMouseWheel;

            PreviewMouseDown +=
                MapCanvas_PreviewMouseDownForPan;

            PreviewMouseUp +=
                MapCanvas_PreviewMouseUpForPan;
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

            _terrainHeightMap =
                null;

            _terrainHeightBitmap =
                null;

            _selectedItem =
                null;

            FitMapView();

            RenderMap();

            SelectionChanged?.Invoke(
                this,
                new ScenarioSelectionChangedEventArgs(
                    null));
        }

        public void FitMapView()
        {
            if (_map == null)
                return;

            _viewMinX =
                _map.MinX;

            _viewMinZ =
                _map.MinZ;

            _viewMaxX =
                _map.MaxX;

            _viewMaxZ =
                _map.MaxZ;

            RenderMap();
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

            DrawTerrainHeightMap();

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

            DrawPlacementPreview();
            DrawMapTitle();
            DrawLegend();
        }

        public void SetTerrainHeightMap(
            TerrainHeightMap? terrain)
        {
            _terrainHeightMap =
                terrain;

            _terrainHeightBitmap =
                terrain == null
                    ? null
                    : BuildTerrainHeightBitmap(
                        terrain);

            RenderMap();
        }

        private static BitmapSource BuildTerrainHeightBitmap(
            TerrainHeightMap terrain)
        {
            int width =
                terrain.Width;

            int height =
                terrain.Height;

            int stride =
                checked(
                    width *
                    4);

            byte[] pixels =
                new byte[
                    checked(
                        stride *
                        height)];

            float heightRange =
                Math.Max(
                    0.0001f,
                    terrain.MaxHeight -
                    terrain.MinHeight);


            for (int z = 0;
                 z < height;
                 z++)
            {
                // World Z increases upward,
                // bitmap Y increases downward.
                int bitmapY =
                    height -
                    1 -
                    z;

                for (int x = 0;
                     x < width;
                     x++)
                {
                    float worldHeight =
                        terrain.Heights[
                            z *
                            width +
                            x];

                    float normalized =
                        (worldHeight -
                         terrain.MinHeight) /
                        heightRange;

                    normalized =
                        Math.Clamp(
                            normalized,
                            0,
                            1);

                    byte shade =
                        (byte)(
                            35 +
                            normalized *
                            175);

                    int p =
                        bitmapY *
                        stride +
                        x *
                        4;

                    // BGRA
                    pixels[p] =
                        shade;

                    pixels[p + 1] =
                        shade;

                    pixels[p + 2] =
                        shade;

                    pixels[p + 3] =
                        255;
                }
            }


            BitmapSource bitmap =
                BitmapSource.Create(
                    width,
                    height,
                    96,
                    96,
                    PixelFormats.Bgra32,
                    null,
                    pixels,
                    stride);

            bitmap.Freeze();

            return bitmap;
        }

        private void DrawTerrainHeightMap()
        {
            if (_terrainHeightMap == null ||
                _terrainHeightBitmap == null)
            {
                return;
            }

            float terrainMaxX =
                _terrainHeightMap.WorldWidth;

            float terrainMaxZ =
                _terrainHeightMap.WorldDepth;

            Point topLeft =
                WorldToScreen(
                    0,
                    terrainMaxZ);

            Point bottomRight =
                WorldToScreen(
                    terrainMaxX,
                    0);

            double left =
                Math.Min(
                    topLeft.X,
                    bottomRight.X);

            double top =
                Math.Min(
                    topLeft.Y,
                    bottomRight.Y);

            double width =
                Math.Abs(
                    bottomRight.X -
                    topLeft.X);

            double height =
                Math.Abs(
                    bottomRight.Y -
                    topLeft.Y);

            Image terrainImage =
                new Image
                {
                    Source =
                        _terrainHeightBitmap,

                    Width =
                        width,

                    Height =
                        height,

                    Stretch =
                        Stretch.Fill,

                    Opacity =
                        0.78,

                    IsHitTestVisible =
                        false
                };

            SetLeft(
                terrainImage,
                left);

            SetTop(
                terrainImage,
                top);

            Children.Add(
                terrainImage);
        }

        private void DrawPlacementPreview()
        {
            if (!_isPlacementMode ||
                _map == null)
            {
                return;
            }

            Point point =
                WorldToScreen(
                    _placementPreviewPosition.X,
                    _placementPreviewPosition.Z);

            const double size =
                20;

            Ellipse preview =
                new Ellipse
                {
                    Width =
                        size,

                    Height =
                        size,

                    Stroke =
                        Brushes.White,

                    StrokeThickness =
                        2,

                    StrokeDashArray =
                        new DoubleCollection
                        {
                    3,
                    2
                        },

                    Fill =
                        new SolidColorBrush(
                            Color.FromArgb(
                                35,
                                255,
                                255,
                                255)),

                    IsHitTestVisible =
                        false
                };

            SetLeft(
                preview,
                point.X -
                size / 2);

            SetTop(
                preview,
                point.Y -
                size / 2);

            Children.Add(
                preview);


            Line horizontal =
                new Line
                {
                    X1 =
                        point.X - 14,

                    X2 =
                        point.X + 14,

                    Y1 =
                        point.Y,

                    Y2 =
                        point.Y,

                    Stroke =
                        Brushes.White,

                    StrokeThickness =
                        1,

                    IsHitTestVisible =
                        false
                };

            Line vertical =
                new Line
                {
                    X1 =
                        point.X,

                    X2 =
                        point.X,

                    Y1 =
                        point.Y - 14,

                    Y2 =
                        point.Y + 14,

                    Stroke =
                        Brushes.White,

                    StrokeThickness =
                        1,

                    IsHitTestVisible =
                        false
                };

            Children.Add(
                horizontal);

            Children.Add(
                vertical);


            TextBlock label =
                new TextBlock
                {
                    Text =
                        $"{_placementLabel}\n" +
                        $"X {_placementPreviewPosition.X:0.##}  " +
                        $"Z {_placementPreviewPosition.Z:0.##}",

                    Foreground =
                        Brushes.White,

                    Background =
                        new SolidColorBrush(
                            Color.FromArgb(
                                210,
                                20,
                                20,
                                20)),

                    Padding =
                        new Thickness(
                            5,
                            3,
                            5,
                            3),

                    FontSize =
                        11,

                    IsHitTestVisible =
                        false
                };

            SetLeft(
                label,
                point.X + 18);

            SetTop(
                label,
                point.Y + 10);

            Children.Add(
                label);
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
                    _terrainHeightBitmap == null
                    ? new SolidColorBrush(
                        Color.FromRgb(30,30,30)) : Brushes.Transparent,

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

                bool selected =
                    ReferenceEquals(
                        path,
                        _selectedItem);

                Polyline line =
                    new Polyline
                    {
                        Stroke =
                            selected
                                ? Brushes.OrangeRed
                                : new SolidColorBrush(
                                    Color.FromRgb(
                                        255,
                                        120,
                                        70)),

                        StrokeThickness =
                            selected
                                ? 3
                                : 2,

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

                foreach (Vector3 point
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

                if (selected)
                {
                    DrawPathPointHandles(
                        path);
                }
            }
        }

        private void DrawPathPointHandles(
            ScenarioPath path)
        {
            for (int i = 0;
                 i < path.Points.Count;
                 i++)
            {
                Vector3 point =
                    path.Points[i];

                Point screen =
                    WorldToScreen(
                        point.X,
                        point.Z);

                const double size =
                    12;

                Ellipse handle =
                    new Ellipse
                    {
                        Width =
                            size,

                        Height =
                            size,

                        Fill =
                            Brushes.White,

                        Stroke =
                            Brushes.OrangeRed,

                        StrokeThickness =
                            2,

                        Tag =
                            new PathPointHandle(
                                path,
                                i),

                        ToolTip =
                            $"Point {i + 1}\n" +
                            $"X {point.X:0.####}\n" +
                            $"Y {point.Y:0.####}\n" +
                            $"Z {point.Z:0.####}"
                    };

                handle.MouseLeftButtonDown +=
                    PathPointHandle_MouseLeftButtonDown;

                SetLeft(
                    handle,
                    screen.X -
                    size / 2);

                SetTop(
                    handle,
                    screen.Y -
                    size / 2);

                Children.Add(
                    handle);
            }
        }

        private void PathPointHandle_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (sender
                is not FrameworkElement element ||
                element.Tag
                is not PathPointHandle handle)
            {
                return;
            }

            if (handle.Index < 0 ||
                handle.Index >=
                    handle.Path.Points.Count)
            {
                return;
            }

            _selectedItem =
                handle.Path;

            _draggedPath =
                handle.Path;

            _draggedPathPointIndex =
                handle.Index;

            _pathPointDragStartPosition =
                handle.Path.Points[
                    handle.Index];

            _isDraggingPathPoint =
                true;

            Cursor =
                Cursors.SizeAll;

            CaptureMouse();

            SelectionChanged?.Invoke(
                this,
                new ScenarioSelectionChangedEventArgs(
                    handle.Path));

            e.Handled =
                true;
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
            0.0001,
            _viewMaxX -
            _viewMinX),
                    MapHeight /
                    Math.Max(
            0.0001,
            _viewMaxZ -
            _viewMinZ));

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

        public void BeginObjectPlacement(
            string label)
        {
            if (_map == null)
                return;

            _isPlacementMode =
                true;

            _placementLabel =
                label;

            Cursor =
                Cursors.Cross;

            Point mouse =
                Mouse.GetPosition(
                    this);

            (float x, float z) =
                ScreenToWorld(
                    mouse);

            _placementPreviewPosition =
                new Vector3(
                    x,
                    0,
                    z);

            RenderMap();
        }

        public void CancelObjectPlacement()
        {
            if (!_isPlacementMode)
                return;

            _isPlacementMode =
                false;

            _placementLabel =
                string.Empty;

            Cursor =
                null;

            RenderMap();
        }

        // =========================================================
        // Mouse Clicks
        // =========================================================

        private void MapCanvas_PreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (!_isPlacementMode ||
                _map == null)
            {
                return;
            }

            Point mouse =
                e.GetPosition(
                    this);

            (float worldX, float worldZ) =
                ScreenToWorld(
                    mouse);

            _isPlacementMode =
                false;

            Cursor =
                null;

            string label =
                _placementLabel;

            _placementLabel =
                string.Empty;

            PlacementRequested?.Invoke(
                this,
                new ScenarioPlacementRequestedEventArgs(
                    worldX,
                    worldZ));

            e.Handled =
                true;
        }

        private void MapCanvas_PreviewMouseRightButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (!_isPlacementMode)
                return;

            CancelObjectPlacement();

            e.Handled =
                true;
        }

        private void MapCanvas_PreviewMouseWheel(
            object sender,
            MouseWheelEventArgs e)
        {
            if (_map == null)
                return;

            Point mouse =
                e.GetPosition(
                    this);

            // Ignore wheel events outside the actual map viewport.
            if (mouse.X < MarginSize ||
                mouse.X > MarginSize + MapWidth ||
                mouse.Y < MarginSize ||
                mouse.Y > MarginSize + MapHeight)
            {
                return;
            }

            double normalizedX =
                (mouse.X -
                 MarginSize) /
                MapWidth;

            double normalizedScreenY =
                (mouse.Y -
                 MarginSize) /
                MapHeight;

            double normalizedZ =
                1.0 -
                normalizedScreenY;

            float worldUnderMouseX =
                (float)(
                    _viewMinX +
                    normalizedX *
                    (_viewMaxX -
                     _viewMinX));

            float worldUnderMouseZ =
                (float)(
                    _viewMinZ +
                    normalizedZ *
                    (_viewMaxZ -
                     _viewMinZ));

            float currentWidth =
                _viewMaxX -
                _viewMinX;

            float currentDepth =
                _viewMaxZ -
                _viewMinZ;

            // Wheel up = zoom in.
            float factor =
                e.Delta > 0
                    ? 0.80f
                    : 1.25f;

            float mapWidth =
                _map.MaxX -
                _map.MinX;

            float mapDepth =
                _map.MaxZ -
                _map.MinZ;

            // Maximum zoom:
            // visible region can't become smaller than about
            // 1/32 of the full map.
            float minimumWidth =
                Math.Max(
                    4,
                    mapWidth /
                    32.0f);

            float minimumDepth =
                Math.Max(
                    4,
                    mapDepth /
                    32.0f);

            // Maximum zoom-out:
            // don't go farther than 2x the map dimensions.
            float maximumWidth =
                mapWidth *
                2.0f;

            float maximumDepth =
                mapDepth *
                2.0f;

            float newWidth =
                Math.Clamp(
                    currentWidth *
                    factor,
                    minimumWidth,
                    maximumWidth);

            float newDepth =
                Math.Clamp(
                    currentDepth *
                    factor,
                    minimumDepth,
                    maximumDepth);

            // Keep the world position underneath the cursor
            // stationary while zooming.
            _viewMinX =
                worldUnderMouseX -
                newWidth *
                (float)normalizedX;

            _viewMaxX =
                _viewMinX +
                newWidth;

            _viewMinZ =
                worldUnderMouseZ -
                newDepth *
                (float)normalizedZ;

            _viewMaxZ =
                _viewMinZ +
                newDepth;

            RenderMap();

            e.Handled =
                true;
        }

        private void MapCanvas_PreviewMouseDownForPan(
            object sender,
            MouseButtonEventArgs e)
        {
            if (e.ChangedButton !=
                MouseButton.Middle)
            {
                return;
            }

            if (_map == null)
                return;

            _isPanning =
                true;

            _panStartMouse =
                e.GetPosition(
                    this);

            _panStartMinX =
                _viewMinX;

            _panStartMinZ =
                _viewMinZ;

            _panStartMaxX =
                _viewMaxX;

            _panStartMaxZ =
                _viewMaxZ;

            Cursor =
                Cursors.Hand;

            CaptureMouse();

            e.Handled =
                true;
        }

        private void MapCanvas_PreviewMouseUpForPan(
            object sender,
            MouseButtonEventArgs e)
        {
            if (e.ChangedButton !=
                MouseButton.Middle)
            {
                return;
            }

            if (!_isPanning)
                return;

            FinishPan();

            e.Handled =
                true;
        }

        private void FinishPan()
        {
            if (!_isPanning)
                return;

            _isPanning =
                false;

            Cursor =
                _isPlacementMode
                    ? Cursors.Cross
                    : null;

            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
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
        // DELETED OBJECTS
        // =========================================================

        public void DeleteScenarioObjectFromEditor(
            ScenarioObject obj)
        {
            if (_map == null)
                return;

            if (!_map.Objects.Contains(
                    obj))
            {
                return;
            }

            bool wasNewObject =
                obj.IsNewObject;

            _map.Objects.Remove(
                obj);

            // Existing XMX object:
            // remember that the structural writer must remove it.
            //
            // New unsaved duplicate:
            // it never existed in the base XMX, so simply removing it
            // from ScenarioMap is enough.
            if (!wasNewObject)
            {
                _map.DeletedObjectIds.Add(
                    obj.Id);
            }

            _selectedItem =
                null;

            RenderMap();

            SelectionChanged?.Invoke(
                this,
                new ScenarioSelectionChangedEventArgs(
                    null));

            ObjectDeleted?.Invoke(
                this,
                new ScenarioObjectDeletedEventArgs(
                    obj,
                    wasNewObject));
        }

        public void ApplyHistoryRestoreObject(
            ScenarioObject obj,
            bool wasNewObject)
        {
            if (_map == null)
                return;

            if (!_map.Objects.Contains(
                    obj))
            {
                _map.Objects.Add(
                    obj);
            }

            if (!wasNewObject)
            {
                _map.DeletedObjectIds.Remove(
                    obj.Id);
            }

            _selectedItem =
                obj;

            RenderMap();

            SelectionChanged?.Invoke(
                this,
                new ScenarioSelectionChangedEventArgs(
                    obj));
        }

        public void ApplyHistoryDeleteObject(
            ScenarioObject obj,
            bool wasNewObject)
        {
            if (_map == null)
                return;

            _map.Objects.Remove(
                obj);

            if (!wasNewObject)
            {
                _map.DeletedObjectIds.Add(
                    obj.Id);
            }

            if (ReferenceEquals(
                    _selectedItem,
                    obj))
            {
                _selectedItem =
                    null;
            }

            RenderMap();

            SelectionChanged?.Invoke(
                this,
                new ScenarioSelectionChangedEventArgs(
                    null));
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

        private static bool CanDragItem(object item)
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
            base.OnMouseMove(e);

            // =====================================================
            // VIEW PANNING
            // =====================================================

            if (_isPanning &&
                _map != null)
            {
                if (e.MiddleButton !=
                    MouseButtonState.Pressed)
                {
                    FinishPan();

                    return;
                }

                Point mouse =
                    e.GetPosition(
                        this);

                double screenDeltaX =
                    mouse.X -
                    _panStartMouse.X;

                double screenDeltaY =
                    mouse.Y -
                    _panStartMouse.Y;

                float worldWidth =
                    _panStartMaxX -
                    _panStartMinX;

                float worldDepth =
                    _panStartMaxZ -
                    _panStartMinZ;

                float worldDeltaX =
                    (float)(
                        screenDeltaX /
                        MapWidth *
                        worldWidth);

                // Screen Y increases downward,
                // world Z increases upward.
                float worldDeltaZ =
                    (float)(
                        -screenDeltaY /
                        MapHeight *
                        worldDepth);

                _viewMinX =
                    _panStartMinX -
                    worldDeltaX;

                _viewMaxX =
                    _panStartMaxX -
                    worldDeltaX;

                _viewMinZ =
                    _panStartMinZ -
                    worldDeltaZ;

                _viewMaxZ =
                    _panStartMaxZ -
                    worldDeltaZ;

                RenderMap();

                e.Handled =
                    true;

                return;
            }


            // Placement

            if (_isPlacementMode && _map != null)
            {
                Point mouse =
                    e.GetPosition(
                        this);

                (float placementWorldX, float placementWorldZ) =
                    ScreenToWorld(mouse);

                _placementPreviewPosition =
                    new Vector3(
                        placementWorldX,
                        0,
                        placementWorldZ);

                RenderMap();

                e.Handled =
                    true;

                return;
            }

            // =====================================================
            // DESIGN PATH POINT DRAGGING
            // =====================================================

            if (_isDraggingPathPoint &&
                _draggedPath != null &&
                _map != null)
            {
                if (e.LeftButton !=
                    MouseButtonState.Pressed)
                {
                    FinishPathPointDrag();

                    return;
                }

                if (_draggedPathPointIndex <
                        0 ||
                    _draggedPathPointIndex >=
                        _draggedPath.Points.Count)
                {
                    FinishPathPointDrag();

                    return;
                }

                Point pathMousePosition =
                    e.GetPosition(
                        this);

                (float pathWorldX, float pathWorldZ) =
                    ScreenToWorld(
                        pathMousePosition);

                Vector3 oldPoint =
                    _draggedPath.Points[
                        _draggedPathPointIndex];

                Vector3 newPoint =
                    new Vector3(
                        pathWorldX,

                        // Preserve the original terrain height.
                        oldPoint.Y,

                        pathWorldZ);

                if (Vector3.DistanceSquared(
                        oldPoint,
                        newPoint) <
                    0.000001f)
                {
                    return;
                }

                _draggedPath.Points[
                    _draggedPathPointIndex] =
                        newPoint;

                RenderMap();

                SelectionChanged?.Invoke(
                    this,
                    new ScenarioSelectionChangedEventArgs(
                        _draggedPath));

                e.Handled =
                    true;

                return;
            }

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
            if (_isDraggingPathPoint)
            {
                FinishPathPointDrag();

                e.Handled =
                    true;

                return;
            }

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
            if (_isPanning)
            {
                _isPanning =
                    false;

                Cursor =
                    _isPlacementMode
                        ? Cursors.Cross
                        : null;
            }

            if (_isDraggingPathPoint)
            {
                FinishPathPointDrag(
                    releaseCapture: false);
            }

            if (_isDragging)
            {
                FinishDrag(
                    releaseCapture: false);
            }
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

        private void FinishPathPointDrag(
            bool releaseCapture = true)
        {
            if (!_isDraggingPathPoint)
                return;

            ScenarioPath? path =
                _draggedPath;

            int index =
                _draggedPathPointIndex;

            Vector3 oldPosition =
                _pathPointDragStartPosition;

            Vector3 newPosition =
                oldPosition;

            if (path != null &&
                index >= 0 &&
                index <
                    path.Points.Count)
            {
                newPosition =
                    path.Points[index];
            }

            _isDraggingPathPoint =
                false;

            _draggedPath =
                null;

            _draggedPathPointIndex =
                -1;

            Cursor =
                null;

            if (releaseCapture &&
                IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            if (path == null ||
                index < 0)
            {
                return;
            }

            if (Vector3.DistanceSquared(
                    oldPosition,
                    newPosition) <
                0.000001f)
            {
                return;
            }

            PathPointMoved?.Invoke(
                this,
                new ScenarioPathPointMovedEventArgs(
                    path,
                    index,
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
            string editorName,
            int player,
            int group,
            int visualVariationIndex)
        {
            string oldEditorName =
                obj.EditorName;

            int oldPlayer =
                obj.Player;

            int oldGroup =
                obj.Group;

            int oldVisualVariationIndex =
                obj.VisualVariationIndex;

            if (string.Equals(
                    oldEditorName,
                    editorName,
                    StringComparison.Ordinal) &&
                oldPlayer == player &&
                oldGroup == group &&
                oldVisualVariationIndex == visualVariationIndex)
            {
                return;
            }

            obj.EditorName =
                editorName;

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
                    oldEditorName,
                    oldPlayer,
                    oldGroup,
                    oldVisualVariationIndex,
                    editorName,
                    player,
                    group,
                    visualVariationIndex));
        }

        public void AddScenarioObjectFromEditor(
            ScenarioObject obj)
        {
            if (_map == null)
                return;

            _map.Objects.Add(
                obj);

            _selectedItem =
                obj;

            RenderMap();

            SelectionChanged?.Invoke(
                this,
                new ScenarioSelectionChangedEventArgs(
                    obj));

            ObjectAdded?.Invoke(
                this,
                new ScenarioObjectAddedEventArgs(
                    obj));
        }

        public void ApplyHistoryAddObject(
            ScenarioObject obj)
        {
            if (_map == null)
                return;

            if (!_map.Objects.Contains(
                    obj))
            {
                _map.Objects.Add(
                    obj);
            }

            _selectedItem =
                obj;

            RenderMap();

            SelectionChanged?.Invoke(
                this,
                new ScenarioSelectionChangedEventArgs(
                    obj));
        }

        public void ApplyHistoryRemoveObject(
            ScenarioObject obj)
        {
            if (_map == null)
                return;

            _map.Objects.Remove(
                obj);

            if (ReferenceEquals(
                    _selectedItem,
                    obj))
            {
                _selectedItem =
                    null;
            }

            RenderMap();

            SelectionChanged?.Invoke(
                this,
                new ScenarioSelectionChangedEventArgs(
                    _selectedItem));
        }

        public void ApplyHistoryObjectProperties(
            ScenarioObject obj,
            string editorName,
            int player,
            int group,
            int visualVariationIndex)
        {
            obj.EditorName =
                editorName;

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

        public void ApplyHistoryPathPoint(
            ScenarioPath path,
            int pointIndex,
            Vector3 position)
        {
            if (pointIndex < 0 ||
                pointIndex >=
                    path.Points.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pointIndex));
            }

            path.Points[
                pointIndex] =
                    position;

            _selectedItem =
                path;

            RenderMap();

            SelectionChanged?.Invoke(
                this,
                new ScenarioSelectionChangedEventArgs(
                    path));
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
                    0.0001,
                    _viewMaxX -
                    _viewMinX);

            double depth =
                Math.Max(
                    0.0001,
                    _viewMaxZ -
                    _viewMinZ);

            double normalizedX =
                (worldX -
                 _viewMinX) /
                width;

            double normalizedZ =
                (worldZ -
                 _viewMinZ) /
                depth;

            double x =
                MarginSize +
                normalizedX *
                MapWidth;

            double y =
                MarginSize +
                (1.0 -
                 normalizedZ) *
                MapHeight;

            return new Point(
                x,
                y);
        }

        private (float X, float Z) ScreenToWorld(Point screen)
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

            double normalizedZ =
                1.0 -
                normalizedScreenY;

            float worldX =
                (float)(
                    _viewMinX +
                    normalizedX *
                    (_viewMaxX -
                     _viewMinX));

            float worldZ =
                (float)(
                    _viewMinZ +
                    normalizedZ *
                    (_viewMaxZ -
                     _viewMinZ));

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

    public sealed class ScenarioObjectAddedEventArgs :
    EventArgs
    {
        public ScenarioObjectAddedEventArgs(
            ScenarioObject obj)
        {
            Object =
                obj;
        }

        public ScenarioObject Object
        {
            get;
        }
    }

    public sealed class ScenarioPlacementRequestedEventArgs :
    EventArgs
    {
        public ScenarioPlacementRequestedEventArgs(
            float worldX,
            float worldZ)
        {
            WorldX =
                worldX;

            WorldZ =
                worldZ;
        }

        public float WorldX
        {
            get;
        }

        public float WorldZ
        {
            get;
        }
    }

    public sealed class ScenarioObjectPropertiesChangedEventArgs :
    EventArgs
    {
        public ScenarioObjectPropertiesChangedEventArgs(
            ScenarioObject obj,
            string oldEditorName,
            int oldPlayer,
            int oldGroup,
            int oldVisualVariationIndex,
            string newEditorName,
            int newPlayer,
            int newGroup,
            int newVisualVariationIndex)
        {
            Object =
                obj;

            OldEditorName =
                oldEditorName;

            OldPlayer =
                oldPlayer;

            OldGroup =
                oldGroup;

            OldVisualVariationIndex =
                oldVisualVariationIndex;

            NewEditorName =
                newEditorName;

            NewPlayer =
                newPlayer;

            NewGroup =
                newGroup;

            NewVisualVariationIndex =
                newVisualVariationIndex;
        }

        public ScenarioObject Object { get; }

        public string OldEditorName { get; }

        public int OldPlayer { get; }

        public int OldGroup { get; }

        public int OldVisualVariationIndex { get; }

        public string NewEditorName { get; }

        public int NewPlayer { get; }

        public int NewGroup { get; }

        public int NewVisualVariationIndex { get; }
    }

    public sealed class ScenarioObjectDeletedEventArgs :
    EventArgs
    {
        public ScenarioObjectDeletedEventArgs(
            ScenarioObject obj,
            bool wasNewObject)
        {
            Object =
                obj;

            WasNewObject =
                wasNewObject;
        }

        public ScenarioObject Object
        {
            get;
        }

        public bool WasNewObject
        {
            get;
        }
    }

    public sealed class ScenarioPathPointMovedEventArgs :
    EventArgs
    {
        public ScenarioPathPointMovedEventArgs(
            ScenarioPath path,
            int pointIndex,
            Vector3 oldPosition,
            Vector3 newPosition)
        {
            Path =
                path;

            PointIndex =
                pointIndex;

            OldPosition =
                oldPosition;

            NewPosition =
                newPosition;
        }

        public ScenarioPath Path
        {
            get;
        }

        public int PointIndex
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

}