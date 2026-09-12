using Ensemble.Models;
using Ensemble.Services;
using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using NumericsVector3 = System.Numerics.Vector3;

namespace Ensemble.Controls
{
    public sealed class MapViewport3D :
        UserControl
    {
        private readonly Viewport3D _viewport;
        private readonly PerspectiveCamera _camera;
        private readonly Model3DGroup _root;
        private readonly Model3DGroup _objects;
        private readonly Dictionary<GeometryModel3D, object> _modelToItem = new();
        private readonly Dictionary<object, List<GeometryModel3D>> _itemToModels = new();
        private readonly Dictionary<GeometryModel3D, Material> _baseMaterials = new();

        private ScenarioMap? _map;
        private EraArchiveInfo? _archive;
        private TerrainHeightMap? _terrain;
        private IReadOnlyList<ScenarioArtObject> _artObjects = Array.Empty<ScenarioArtObject>();
        private ImageSource? _terrainTexture;

        private object? _selectedItem;
        private bool _showObjects = true;

        private Point3D _target;
        private double _yaw = -0.72;
        private double _pitch = 0.78;
        private double _distance = 1200.0;
        private double _markerSize = 10.0;

        private bool _orbiting;
        private bool _panning;
        private Point _mouseStart;
        private double _yawStart;
        private double _pitchStart;
        private Point3D _targetStart;

        private bool _draggingItem;
        private object? _dragItem;
        private Point _dragStartMouse;
        private NumericsVector3 _dragStartPosition;

        public event EventHandler<ScenarioSelectionChangedEventArgs>? SelectionChanged;
        public event EventHandler<ScenarioItemMovedEventArgs>? ItemMoved;

        public bool ShowObjects
        {
            get => _showObjects;
            set
            {
                if (_showObjects == value)
                    return;

                _showObjects = value;
                RebuildObjects();
            }
        }

        public MapViewport3D()
        {
            Background =
                new SolidColorBrush(
                    Color.FromRgb(0x04, 0x0B, 0x12));

            Focusable = true;
            ClipToBounds = true;

            Grid layout = new Grid();

            _viewport =
                new Viewport3D
                {
                    ClipToBounds = true
                };

            _camera =
                new PerspectiveCamera
                {
                    FieldOfView = 52,
                    UpDirection = new Vector3D(0, 1, 0),
                    NearPlaneDistance = 0.5,
                    FarPlaneDistance = 100000
                };

            _viewport.Camera = _camera;

            _root = new Model3DGroup();
            _objects = new Model3DGroup();

            _root.Children.Add(
                new AmbientLight(
                    Color.FromRgb(0x86, 0x90, 0x96)));

            _root.Children.Add(
                new DirectionalLight(
                    Colors.White,
                    new Vector3D(-0.35, -1.0, -0.45)));

            _root.Children.Add(
                new DirectionalLight(
                    Color.FromRgb(0x45, 0xA0, 0xC8),
                    new Vector3D(0.55, -0.45, 0.35)));

            _root.Children.Add(_objects);

            _viewport.Children.Add(
                new ModelVisual3D
                {
                    Content = _root
                });

            layout.Children.Add(_viewport);

            Border accent =
                new Border
                {
                    Height = 2,
                    VerticalAlignment = VerticalAlignment.Top,
                    Background =
                        new SolidColorBrush(
                            Color.FromRgb(0x43, 0xC8, 0xF5)),
                    IsHitTestVisible = false
                };

            layout.Children.Add(accent);

            Border help =
                new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(12),
                    Padding = new Thickness(8, 5, 8, 5),
                    Background =
                        new SolidColorBrush(
                            Color.FromArgb(190, 0x06, 0x11, 0x1B)),
                    BorderBrush =
                        new SolidColorBrush(
                            Color.FromRgb(0x31, 0x5E, 0x73)),
                    BorderThickness = new Thickness(1),
                    IsHitTestVisible = false
                };

            help.Child =
                new TextBlock
                {
                    Text =
                        "3D VIEW  •  LMB SELECT/MOVE  •  RMB ORBIT  •  MMB PAN  •  WHEEL ZOOM",
                    Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(0x9B, 0xEA, 0xFF)),
                    FontSize = 10
                };

            layout.Children.Add(help);

            Content = layout;

            MouseLeftButtonDown += OnLeftDown;
            MouseLeftButtonUp += OnLeftUp;
            MouseRightButtonDown += OnRightDown;
            MouseRightButtonUp += OnRightUp;
            MouseDown += OnAnyMouseDown;
            MouseUp += OnAnyMouseUp;
            MouseMove += OnMouseMove;
            MouseWheel += OnMouseWheel;
            MouseDoubleClick += OnDoubleClick;
        }

        public void SetScene(
            ScenarioMap? map,
            TerrainHeightMap? terrain,
            IReadOnlyList<ScenarioArtObject>? artObjects,
            ImageSource? terrainTexture = null,
            EraArchiveInfo? archive = null)
        {
            bool mapChanged = !ReferenceEquals(_map, map);

            _map = map;
            _archive = archive;
            _terrain = terrain;
            _artObjects = artObjects ?? Array.Empty<ScenarioArtObject>();
            _terrainTexture = terrainTexture;

            RebuildTerrain();
            RebuildObjects();

            if (mapChanged)
                FitMap();
        }

        public void RefreshObjects(
            IReadOnlyList<ScenarioArtObject>? artObjects)
        {
            _artObjects = artObjects ?? Array.Empty<ScenarioArtObject>();
            RebuildObjects();
        }

        public void SetTerrainTexture(
            ImageSource? terrainTexture)
        {
            _terrainTexture = terrainTexture;
            RebuildTerrain();
        }

        public void SelectItem(
            object? item)
        {
            _selectedItem = item;
            UpdateSelectionMaterials();
        }

        public void FitMap()
        {
            if (_map == null)
                return;

            double cx = (_map.MinX + _map.MaxX) * 0.5;
            double cz = (_map.MinZ + _map.MaxZ) * 0.5;
            double cy = _terrain != null
                    ? (_terrain.MinHeight + _terrain.MaxHeight) * 0.5
                    : 0.0;

            _target = new Point3D(cx, cy, cz);

            double width = Math.Max(1, _map.MaxX - _map.MinX);
            double depth = Math.Max(1, _map.MaxZ - _map.MinZ);

            _distance = Math.Max(width, depth) * 1.05;
            _yaw = -0.72;
            _pitch = 0.78;
            UpdateCamera();
        }

        private void RebuildTerrain()
        {
            for (int i = _root.Children.Count - 1; i >= 0; i--)
            {
                Model3D child = _root.Children[i];

                if (child is Light || ReferenceEquals(child, _objects))
                    continue;

                _root.Children.RemoveAt(i);
            }

            GeometryModel3D model =
                _terrain != null &&
                _terrain.Width >= 2 &&
                _terrain.Height >= 2 &&
                _terrain.Heights.Length >= _terrain.Width * _terrain.Height
                    ? BuildTerrainModel(_terrain)
                    : BuildFlatGround();

            int objectIndex = _root.Children.IndexOf(_objects);
            _root.Children.Insert(Math.Max(2, objectIndex), model);
        }

        private GeometryModel3D BuildTerrainModel(
            TerrainHeightMap terrain)
        {
            const int maxSamples = 129;

            List<int> xs = BuildIndices(terrain.Width, maxSamples);
            List<int> zs = BuildIndices(terrain.Height, maxSamples);

            MeshGeometry3D mesh = new MeshGeometry3D();

            foreach (int z in zs)
            {
                foreach (int x in xs)
                {
                    float y = terrain.Heights[z * terrain.Width + x];

                    mesh.Positions.Add(
                        new Point3D(
                            terrain.WorldMin.X + x * terrain.TileScale,
                            y,
                            terrain.WorldMin.Z + z * terrain.TileScale));

                    mesh.Normals.Add(
                        EstimateNormal(terrain, x, z));

                    double u = terrain.Width > 1 ? (double)x / (terrain.Width - 1) : 0;
                    double v = terrain.Height > 1 ? 1.0 - ((double)z / (terrain.Height - 1)) : 0;
                    mesh.TextureCoordinates.Add(new System.Windows.Point(u, v));
                }
            }

            int cols = xs.Count;

            for (int z = 0; z < zs.Count - 1; z++)
            {
                for (int x = 0; x < xs.Count - 1; x++)
                {
                    int a = z * cols + x;
                    int b = a + 1;
                    int c = a + cols;
                    int d = c + 1;

                    mesh.TriangleIndices.Add(a);
                    mesh.TriangleIndices.Add(c);
                    mesh.TriangleIndices.Add(b);

                    mesh.TriangleIndices.Add(b);
                    mesh.TriangleIndices.Add(c);
                    mesh.TriangleIndices.Add(d);
                }
            }

            if (mesh.CanFreeze)
                mesh.Freeze();

            MaterialGroup material = CreateTerrainMaterial();

            return new GeometryModel3D(mesh, material)
            {
                BackMaterial = material
            };
        }

        private MaterialGroup CreateTerrainMaterial()
        {
            MaterialGroup material = new MaterialGroup();

            if (_terrainTexture != null)
            {
                ImageBrush brush =
                    new ImageBrush(_terrainTexture)
                    {
                        Stretch = Stretch.Fill,
                        TileMode = TileMode.None,
                        ViewportUnits = BrushMappingMode.RelativeToBoundingBox
                    };

                if (brush.CanFreeze)
                    brush.Freeze();

                material.Children.Add(new DiffuseMaterial(brush));
                material.Children.Add(
                    new EmissiveMaterial(
                        new SolidColorBrush(
                            Color.FromArgb(35, 255, 255, 255))));
                material.Children.Add(
                    new SpecularMaterial(
                        new SolidColorBrush(
                            Color.FromArgb(80, 255, 255, 255)),
                        12));
            }
            else
            {
                material.Children.Add(
                    new DiffuseMaterial(
                        new SolidColorBrush(
                            Color.FromRgb(0x4A, 0x54, 0x3C))));
                material.Children.Add(
                    new SpecularMaterial(
                        new SolidColorBrush(
                            Color.FromRgb(0x2D, 0x48, 0x55)),
                        10));
            }

            return material;
        }

        private GeometryModel3D BuildFlatGround()
        {
            if (_map == null)
                return new GeometryModel3D();

            MeshGeometry3D mesh =
                new MeshGeometry3D
                {
                    Positions =
                        new Point3DCollection
                        {
                            new Point3D(_map.MinX, 0, _map.MinZ),
                            new Point3D(_map.MaxX, 0, _map.MinZ),
                            new Point3D(_map.MinX, 0, _map.MaxZ),
                            new Point3D(_map.MaxX, 0, _map.MaxZ)
                        },
                    TriangleIndices =
                        new Int32Collection { 0, 2, 1, 1, 2, 3 },
                    Normals =
                        new Vector3DCollection
                        {
                            new Vector3D(0, 1, 0),
                            new Vector3D(0, 1, 0),
                            new Vector3D(0, 1, 0),
                            new Vector3D(0, 1, 0)
                        },
                    TextureCoordinates =
                        new System.Windows.Media.PointCollection
                        {
                            new System.Windows.Point(0,1),
                            new System.Windows.Point(1,1),
                            new System.Windows.Point(0,0),
                            new System.Windows.Point(1,0)
                        }
                };

            MaterialGroup material = CreateTerrainMaterial();
            return new GeometryModel3D(mesh, material) { BackMaterial = material };
        }

        private static List<int> BuildIndices(int count, int maxSamples)
        {
            List<int> values = new();

            if (count <= maxSamples)
            {
                for (int i = 0; i < count; i++)
                    values.Add(i);
                return values;
            }

            double step = (count - 1) / (double)(maxSamples - 1);
            int previous = -1;

            for (int i = 0; i < maxSamples; i++)
            {
                int value = Math.Clamp((int)Math.Round(i * step), 0, count - 1);
                if (value != previous)
                {
                    values.Add(value);
                    previous = value;
                }
            }

            if (values[^1] != count - 1)
                values.Add(count - 1);

            return values;
        }

        private static Vector3D EstimateNormal(
            TerrainHeightMap terrain,
            int x,
            int z)
        {
            int l = Math.Max(0, x - 1);
            int r = Math.Min(terrain.Width - 1, x + 1);
            int d = Math.Max(0, z - 1);
            int u = Math.Min(terrain.Height - 1, z + 1);

            float hl = terrain.Heights[z * terrain.Width + l];
            float hr = terrain.Heights[z * terrain.Width + r];
            float hd = terrain.Heights[d * terrain.Width + x];
            float hu = terrain.Heights[u * terrain.Width + x];

            double dx = Math.Max(0.001, (r - l) * terrain.TileScale);
            double dz = Math.Max(0.001, (u - d) * terrain.TileScale);

            Vector3D normal = new Vector3D(-(hr - hl) / dx, 1, -(hu - hd) / dz);
            normal.Normalize();
            return normal;
        }

        private void RebuildObjects()
        {
            _objects.Children.Clear();
            _modelToItem.Clear();
            _itemToModels.Clear();
            _baseMaterials.Clear();

            if (!_showObjects || _map == null)
                return;

            double width = Math.Max(1, _map.MaxX - _map.MinX);
            double depth = Math.Max(1, _map.MaxZ - _map.MinZ);
            _markerSize = Math.Clamp(Math.Max(width, depth) * 0.012, 5, 22);

            foreach (ScenarioObject obj in _map.Objects)
            {
                AddRenderedObject(
                    obj,
                    obj.Position,
                    obj.Forward,
                    GetScenarioColor(obj),
                    ObjectCatalogLayer.Scenario,
                    string.IsNullOrWhiteSpace(obj.EditorName) ? obj.Type : obj.EditorName,
                    obj.Type);
            }

            foreach (ScenarioArtObject obj in _artObjects)
            {
                AddRenderedObject(
                    obj,
                    obj.Position,
                    obj.Forward,
                    Color.FromRgb(0x91, 0xC8, 0xD7),
                    ObjectCatalogLayer.ArtObject,
                    obj.DisplayName,
                    obj.Type);
            }

            // Player starts are not normal SCN objects. Halo Wars uses these
            // <Positions> entries as runtime spawn anchors, which is why the
            // old 2D viewport showed P1/P2 but the 3D object pass missed them.
            foreach (ScenarioPlayerStart start
                     in _map.PlayerStarts)
            {
                AddPlayerStart(
                    start);
            }

            UpdateSelectionMaterials();
        }

        private void AddPlayerStart(
            ScenarioPlayerStart start)
        {
            // A skirmish start does not store a faction in the scenario.
            // The game chooses the leader/civilisation at runtime and creates
            // that leader's StartingUnits at this position.  Therefore the
            // editor renders the real neutral base socket as the factual map
            // footprint, then overlays the P1/P2/etc editor label.

            Color accent =
                GetPlayerStartColor(
                    start);

            bool renderedNativePad =
                false;

            if (_archive !=
                null)
            {
                UgxMeshService.UgxMeshAsset? basePad =
                    UgxMeshService.TryLoadForObject(
                        _archive,
                        "game_base_socket_01",
                        "game_base_socket_01");

                if (basePad !=
                        null
                    &&
                    basePad.Parts.Count >
                        0)
                {
                    AddNativeMesh(
                        start,
                        basePad,
                        start.Position,
                        start.Forward,
                        accent);

                    renderedNativePad =
                        true;
                }
            }

            if (!renderedNativePad)
            {
                AddEditorGizmo(
                    start,
                    start.Position,
                    start.Forward,
                    accent);
            }

            AddPlayerStartLabel(
                start,
                accent);
        }

        private static Color GetPlayerStartColor(
            ScenarioPlayerStart start)
        {
            // Keep the first few starts visually distinct without implying
            // UNSC/Covenant faction assignment.
            return start.Player switch
            {
                1 =>
                    Color.FromRgb(
                        0x42,
                        0xB8,
                        0xFF),

                2 =>
                    Color.FromRgb(
                        0xFF,
                        0x73,
                        0x73),

                3 =>
                    Color.FromRgb(
                        0x62,
                        0xE2,
                        0x86),

                4 =>
                    Color.FromRgb(
                        0xD0,
                        0x86,
                        0xFF),

                _ =>
                    Color.FromRgb(
                        0xFF,
                        0xD4,
                        0x55)
            };
        }

        private void AddPlayerStartLabel(
            ScenarioPlayerStart start,
            Color accent)
        {
            string label =
                "P" +
                Math.Max(
                    1,
                    start.Player)
                    .ToString(
                        CultureInfo.InvariantCulture);

            Material material =
                CreatePlayerStartLabelMaterial(
                    label,
                    accent);

            double width =
                Math.Clamp(
                    _markerSize *
                    3.4,
                    18,
                    55);

            double height =
                Math.Clamp(
                    _markerSize *
                    1.55,
                    9,
                    28);

            GeometryModel3D model =
                new GeometryModel3D(
                    BuildCrossPlane(
                        width,
                        height),
                    material)
                {
                    BackMaterial =
                        material,

                    Transform =
                        CreateObjectTransform(
                            start.Position,
                            start.Forward,
                            Math.Clamp(
                                _markerSize *
                                3.2,
                                20,
                                70))
                };

            _objects.Children.Add(
                model);

            _modelToItem[
                model] =
                    start;

            _baseMaterials[
                model] =
                    material;

            if (!_itemToModels.TryGetValue(
                    start,
                    out List<GeometryModel3D>? models))
            {
                models =
                    new List<GeometryModel3D>();

                _itemToModels[
                    start] =
                        models;
            }

            models.Add(
                model);
        }

        private static Material CreatePlayerStartLabelMaterial(
            string label,
            Color accent)
        {
            DrawingGroup drawing =
                new DrawingGroup();

            drawing.Children.Add(
                new GeometryDrawing(
                    new SolidColorBrush(
                        Color.FromArgb(
                            225,
                            0x05,
                            0x11,
                            0x1B)),
                    new Pen(
                        new SolidColorBrush(
                            accent),
                        0.055),
                    new RectangleGeometry(
                        new Rect(
                            0.02,
                            0.04,
                            0.96,
                            0.92),
                        0.08,
                        0.08)));

            FormattedText formatted =
                new FormattedText(
                    label,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(
                        new FontFamily(
                            "Bahnschrift SemiCondensed"),
                        FontStyles.Normal,
                        FontWeights.Bold,
                        FontStretches.Normal),
                    0.58,
                    Brushes.White,
                    1.0);

            Geometry textGeometry =
                formatted.BuildGeometry(
                    new System.Windows.Point(
                        0.5 -
                        formatted.Width /
                        2.0,
                        0.5 -
                        formatted.Height /
                        2.0));

            drawing.Children.Add(
                new GeometryDrawing(
                    Brushes.White,
                    null,
                    textGeometry));

            DrawingBrush brush =
                new DrawingBrush(
                    drawing)
                {
                    Stretch =
                        Stretch.Fill
                };

            MaterialGroup material =
                new MaterialGroup();

            material.Children.Add(
                new DiffuseMaterial(
                    brush));

            material.Children.Add(
                new EmissiveMaterial(
                    new SolidColorBrush(
                        Color.FromArgb(
                            50,
                            accent.R,
                            accent.G,
                            accent.B))));

            return material;
        }

        private void AddRenderedObject(
            object item,
            NumericsVector3 position,
            NumericsVector3 forward,
            Color accent,
            ObjectCatalogLayer layer,
            string? name,
            string? type)
        {
            string safeName =
                name ??
                string.Empty;

            string safeType =
                type ??
                string.Empty;

            UgxMeshService.UgxMeshAsset? nativeAsset =
                _archive != null
                    ? UgxMeshService.TryLoadForObject(
                        _archive,
                        safeType,
                        safeName)
                    : null;

            if (nativeAsset != null &&
                nativeAsset.Parts.Count > 0)
            {
                AddNativeMesh(
                    item,
                    nativeAsset,
                    position,
                    forward,
                    accent);

                return;
            }

            if (ShouldRenderAsEditorGizmo(
                    safeType))
            {
                AddEditorGizmo(
                    item,
                    position,
                    forward,
                    GetEditorGizmoColor(
                        safeType));

                return;
            }

            AddPreviewImpostor(
                item,
                position,
                forward,
                accent,
                layer,
                safeName,
                safeType);
        }

        private void AddNativeMesh(
            object item,
            UgxMeshService.UgxMeshAsset asset,
            NumericsVector3 position,
            NumericsVector3 forward,
            Color accent)
        {
            List<GeometryModel3D> models = new();

            foreach (UgxMeshService.UgxMeshPart part
                     in asset.Parts)
            {
                Material material =
                    CreateNativeMaterial(
                        part.DiffuseTexture,
                        accent);

                GeometryModel3D model =
                    new GeometryModel3D(
                        part.Geometry,
                        material)
                    {
                        BackMaterial = material,
                        Transform =
                            CreateObjectTransform(
                                position,
                                forward,
                                0)
                    };

                _objects.Children.Add(model);
                _modelToItem[model] = item;
                _baseMaterials[model] = material;
                models.Add(model);
            }

            if (models.Count > 0)
            {
                _itemToModels[item] = models;
            }
        }

        private static bool ShouldRenderAsEditorGizmo(
            string type)
        {
            if (string.IsNullOrWhiteSpace(
                    type))
            {
                return false;
            }

            string value =
                type.ToLowerInvariant();

            return value.StartsWith(
                       "sys_creep",
                       StringComparison.Ordinal)
                   ||
                   value.StartsWith(
                       "sys_marker_",
                       StringComparison.Ordinal)
                   ||
                   value.StartsWith(
                       "sys_rebelmarker_",
                       StringComparison.Ordinal)
                   ||
                   value.StartsWith(
                       "sys_unitstart",
                       StringComparison.Ordinal)
                   ||
                   value.Contains(
                       "difficulty",
                       StringComparison.Ordinal)
                   ||
                   value.Contains(
                       "reward",
                       StringComparison.Ordinal);
        }

        private static Color GetEditorGizmoColor(
            string type)
        {
            string value =
                type.ToLowerInvariant();

            if (value.Contains(
                    "difficulty",
                    StringComparison.Ordinal))
            {
                return Color.FromRgb(
                    0xCB,
                    0x7C,
                    0xFF);
            }

            if (value.Contains(
                    "reward",
                    StringComparison.Ordinal))
            {
                return Color.FromRgb(
                    0xFF,
                    0xD3,
                    0x44);
            }

            if (value.Contains(
                    "creepworld",
                    StringComparison.Ordinal))
            {
                return Color.FromRgb(
                    0x55,
                    0xE0,
                    0xC1);
            }

            if (value.Contains(
                    "crate",
                    StringComparison.Ordinal))
            {
                return Color.FromRgb(
                    0xC9,
                    0x9B,
                    0x63);
            }

            if (value.Contains(
                    "sniper",
                    StringComparison.Ordinal))
            {
                return Color.FromRgb(
                    0x54,
                    0xC9,
                    0xFF);
            }

            return Color.FromRgb(
                0xF0,
                0x87,
                0x87);
        }

        private void AddEditorGizmo(
            object item,
            NumericsVector3 position,
            NumericsVector3 forward,
            Color color)
        {
            double radius =
                Math.Clamp(
                    _markerSize *
                    0.55,
                    2.5,
                    9.0);

            MaterialGroup material =
                new MaterialGroup();

            material.Children.Add(
                new DiffuseMaterial(
                    new SolidColorBrush(
                        Color.FromArgb(
                            210,
                            color.R,
                            color.G,
                            color.B))));

            material.Children.Add(
                new EmissiveMaterial(
                    new SolidColorBrush(
                        Color.FromArgb(
                            90,
                            color.R,
                            color.G,
                            color.B))));

            GeometryModel3D model =
                new GeometryModel3D(
                    BuildOctahedron(
                        radius),
                    material)
                {
                    BackMaterial =
                        material,

                    Transform =
                        CreateObjectTransform(
                            position,
                            forward,
                            radius)
                };

            _objects.Children.Add(
                model);

            _modelToItem[
                model] =
                    item;

            _baseMaterials[
                model] =
                    material;

            _itemToModels[
                item] =
                    new List<GeometryModel3D>
                    {
                        model
                    };
        }

        private static MeshGeometry3D BuildOctahedron(
            double radius)
        {
            Point3D top =
                new Point3D(
                    0,
                    radius,
                    0);

            Point3D bottom =
                new Point3D(
                    0,
                    -radius,
                    0);

            Point3D east =
                new Point3D(
                    radius,
                    0,
                    0);

            Point3D west =
                new Point3D(
                    -radius,
                    0,
                    0);

            Point3D north =
                new Point3D(
                    0,
                    0,
                    radius);

            Point3D south =
                new Point3D(
                    0,
                    0,
                    -radius);

            Point3D[] points =
            {
                top,
                east,
                north,
                west,
                south,
                bottom
            };

            int[] triangles =
            {
                0, 1, 2,
                0, 2, 3,
                0, 3, 4,
                0, 4, 1,
                5, 2, 1,
                5, 3, 2,
                5, 4, 3,
                5, 1, 4
            };

            MeshGeometry3D mesh =
                new MeshGeometry3D
                {
                    Positions =
                        new Point3DCollection(
                            points),

                    TriangleIndices =
                        new Int32Collection(
                            triangles)
                };

            if (mesh.CanFreeze)
            {
                mesh.Freeze();
            }

            return mesh;
        }

        private void AddPreviewImpostor(
            object item,
            NumericsVector3 position,
            NumericsVector3 forward,
            Color accent,
            ObjectCatalogLayer layer,
            string name,
            string type)
        {
            ImageSource preview =
                ObjectPreviewService.Create(
                    layer,
                    name ?? string.Empty,
                    type ?? string.Empty);

            double objectWidth = _markerSize * 4.2;
            double objectHeight = _markerSize * 2.5;

            Material material =
                CreatePreviewMaterial(
                    preview,
                    accent);

            GeometryModel3D model =
                new GeometryModel3D(
                    BuildCrossPlane(objectWidth, objectHeight),
                    material)
                {
                    BackMaterial = material,
                    Transform =
                        CreateObjectTransform(
                            position,
                            forward,
                            objectHeight * 0.5)
                };

            _objects.Children.Add(model);
            _modelToItem[model] = item;
            _baseMaterials[model] = material;
            _itemToModels[item] = new List<GeometryModel3D> { model };
        }

        private static Transform3D CreateObjectTransform(
            NumericsVector3 position,
            NumericsVector3 forward,
            double yOffset)
        {
            Transform3DGroup transform =
                new Transform3DGroup();

            if (forward.LengthSquared() >
                0.000001f)
            {
                double yaw =
                    Math.Atan2(
                        forward.X,
                        forward.Z) *
                    180.0 /
                    Math.PI;

                transform.Children.Add(
                    new RotateTransform3D(
                        new AxisAngleRotation3D(
                            new Vector3D(0, 1, 0),
                            yaw)));
            }

            transform.Children.Add(
                new TranslateTransform3D(
                    position.X,
                    position.Y + yOffset,
                    position.Z));

            return transform;
        }

        private static MeshGeometry3D BuildCrossPlane(
            double width,
            double height)
        {
            double hw = width * 0.5;
            double h = height;

            MeshGeometry3D mesh = new MeshGeometry3D();

            void AddQuad(Point3D p0, Point3D p1, Point3D p2, Point3D p3)
            {
                int start = mesh.Positions.Count;
                mesh.Positions.Add(p0);
                mesh.Positions.Add(p1);
                mesh.Positions.Add(p2);
                mesh.Positions.Add(p3);

                mesh.TextureCoordinates.Add(new System.Windows.Point(0, 1));
                mesh.TextureCoordinates.Add(new System.Windows.Point(1, 1));
                mesh.TextureCoordinates.Add(new System.Windows.Point(1, 0));
                mesh.TextureCoordinates.Add(new System.Windows.Point(0, 0));

                Vector3D normal = Vector3D.CrossProduct(p1 - p0, p2 - p0);
                if (normal.LengthSquared > 0.0001)
                    normal.Normalize();
                else
                    normal = new Vector3D(0, 0, 1);

                for (int i = 0; i < 4; i++)
                    mesh.Normals.Add(normal);

                mesh.TriangleIndices.Add(start + 0);
                mesh.TriangleIndices.Add(start + 1);
                mesh.TriangleIndices.Add(start + 2);
                mesh.TriangleIndices.Add(start + 0);
                mesh.TriangleIndices.Add(start + 2);
                mesh.TriangleIndices.Add(start + 3);
            }

            AddQuad(
                new Point3D(-hw, 0, 0),
                new Point3D(hw, 0, 0),
                new Point3D(hw, h, 0),
                new Point3D(-hw, h, 0));

            AddQuad(
                new Point3D(0, 0, -hw),
                new Point3D(0, 0, hw),
                new Point3D(0, h, hw),
                new Point3D(0, h, -hw));

            if (mesh.CanFreeze)
                mesh.Freeze();

            return mesh;
        }

        private static Material CreateNativeMaterial(
            ImageSource? diffuseTexture,
            Color accent)
        {
            MaterialGroup material =
                new MaterialGroup();

            if (diffuseTexture != null)
            {
                ImageBrush brush =
                    new ImageBrush(diffuseTexture)
                    {
                        Stretch = Stretch.Fill,
                        TileMode = TileMode.None,
                        ViewportUnits = BrushMappingMode.RelativeToBoundingBox
                    };

                if (brush.CanFreeze)
                    brush.Freeze();

                material.Children.Add(
                    new DiffuseMaterial(brush));
            }
            else
            {
                Color neutral =
                    Color.FromRgb(
                        (byte)((accent.R + 150) / 2),
                        (byte)((accent.G + 150) / 2),
                        (byte)((accent.B + 150) / 2));

                material.Children.Add(
                    new DiffuseMaterial(
                        new SolidColorBrush(neutral)));
            }

            material.Children.Add(
                new SpecularMaterial(
                    new SolidColorBrush(
                        Color.FromArgb(
                            70,
                            220,
                            235,
                            245)),
                    16));

            return material;
        }

        private static Material CreatePreviewMaterial(
            ImageSource preview,
            Color accent)
        {
            DrawingGroup drawing = new DrawingGroup();

            drawing.Children.Add(
                new GeometryDrawing(
                    new SolidColorBrush(Color.FromRgb(0x08, 0x12, 0x1A)),
                    null,
                    new RectangleGeometry(new Rect(0, 0, 1, 1))));

            drawing.Children.Add(
                new ImageDrawing(
                    preview,
                    new Rect(0.06, 0.06, 0.88, 0.88)));

            drawing.Children.Add(
                new GeometryDrawing(
                    null,
                    new Pen(
                        new SolidColorBrush(accent),
                        0.04),
                    new RectangleGeometry(new Rect(0.02, 0.02, 0.96, 0.96))));

            DrawingBrush brush = new DrawingBrush(drawing)
            {
                Stretch = Stretch.Fill
            };

            MaterialGroup material = new MaterialGroup();
            material.Children.Add(new DiffuseMaterial(brush));
            material.Children.Add(
                new EmissiveMaterial(
                    new SolidColorBrush(
                        Color.FromArgb(24, accent.R, accent.G, accent.B))));
            return material;
        }

        private static Material ApplySelectionHighlight(
            Material baseMaterial,
            bool selected)
        {
            if (!selected)
                return baseMaterial;

            MaterialGroup selectedMaterial =
                new MaterialGroup();

            selectedMaterial.Children.Add(baseMaterial);
            selectedMaterial.Children.Add(
                new EmissiveMaterial(
                    new SolidColorBrush(
                        Color.FromArgb(
                            70,
                            255,
                            224,
                            64))));

            return selectedMaterial;
        }

        private static Color GetScenarioColor(ScenarioObject obj)
        {
            return obj.Category switch
            {
                "Base" => Color.FromRgb(0xFF, 0xA5, 0x2C),
                "Reactor" => Color.FromRgb(0xFF, 0xE2, 0x30),
                "Supply" => Color.FromRgb(0x4A, 0xD2, 0x6D),
                "Teleporter" => Color.FromRgb(0xB3, 0x74, 0xE8),
                "Sniper Platform" => Color.FromRgb(0x49, 0xC8, 0xFF),
                _ => Color.FromRgb(0xE0, 0x72, 0x72)
            };
        }

        private void UpdateSelectionMaterials()
        {
            foreach (var pair in _itemToModels)
            {
                bool selected =
                    ReferenceEquals(
                        pair.Key,
                        _selectedItem);

                foreach (GeometryModel3D model in pair.Value)
                {
                    if (!_baseMaterials.TryGetValue(
                            model,
                            out Material? baseMaterial))
                    {
                        continue;
                    }

                    Material material =
                        ApplySelectionHighlight(
                            baseMaterial,
                            selected);

                    model.Material = material;
                    model.BackMaterial = material;
                }
            }
        }

        private static NumericsVector3 GetForward(
            object item)
        {
            return item switch
            {
                ScenarioObject obj => obj.Forward,
                ScenarioArtObject art => art.Forward,
                ScenarioPlayerStart start => start.Forward,
                _ => NumericsVector3.UnitZ
            };
        }

        private void UpdateCamera()
        {
            double cp = Math.Cos(_pitch);

            Vector3D offset = new Vector3D(
                _distance * cp * Math.Sin(_yaw),
                _distance * Math.Sin(_pitch),
                _distance * cp * Math.Cos(_yaw));

            Point3D position = _target + offset;
            _camera.Position = position;
            _camera.LookDirection = _target - position;
            _camera.UpDirection = new Vector3D(0, 1, 0);
        }

        private object? HitItem(System.Windows.Point point)
        {
            HitTestResult? hit = VisualTreeHelper.HitTest(_viewport, point);

            if (hit is RayMeshGeometry3DHitTestResult ray &&
                ray.ModelHit is GeometryModel3D model &&
                _modelToItem.TryGetValue(model, out object? item))
            {
                return item;
            }

            return null;
        }

        private void OnLeftDown(object sender, MouseButtonEventArgs e)
        {
            Focus();

            object? item = HitItem(e.GetPosition(_viewport));
            _selectedItem = item;
            UpdateSelectionMaterials();

            SelectionChanged?.Invoke(this, new ScenarioSelectionChangedEventArgs(item));

            if (item != null && TryGetPosition(item, out NumericsVector3 position))
            {
                _draggingItem = true;
                _dragItem = item;
                _dragStartPosition = position;
                _dragStartMouse = e.GetPosition(this);
                CaptureMouse();
                Cursor = Cursors.SizeAll;
            }

            e.Handled = true;
        }

        private void OnLeftUp(object sender, MouseButtonEventArgs e)
        {
            if (!_draggingItem)
                return;

            object? item = _dragItem;
            NumericsVector3 oldPosition = _dragStartPosition;

            _draggingItem = false;
            _dragItem = null;
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;

            if (item != null &&
                TryGetPosition(item, out NumericsVector3 newPosition) &&
                NumericsVector3.DistanceSquared(oldPosition, newPosition) > 0.000001f)
            {
                ItemMoved?.Invoke(this, new ScenarioItemMovedEventArgs(item, oldPosition, newPosition));
            }

            e.Handled = true;
        }

        private void OnRightDown(object sender, MouseButtonEventArgs e)
        {
            _orbiting = true;
            _mouseStart = e.GetPosition(this);
            _yawStart = _yaw;
            _pitchStart = _pitch;
            CaptureMouse();
            Cursor = Cursors.ScrollAll;
            e.Handled = true;
        }

        private void OnRightUp(object sender, MouseButtonEventArgs e)
        {
            if (!_orbiting)
                return;

            _orbiting = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
            e.Handled = true;
        }

        private void OnAnyMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
                OnMiddleDown(sender, e);
        }

        private void OnAnyMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
                OnMiddleUp(sender, e);
        }

        private void OnMiddleDown(object sender, MouseButtonEventArgs e)
        {
            _panning = true;
            _mouseStart = e.GetPosition(this);
            _targetStart = _target;
            CaptureMouse();
            Cursor = Cursors.Hand;
            e.Handled = true;
        }

        private void OnMiddleUp(object sender, MouseButtonEventArgs e)
        {
            if (!_panning)
                return;

            _panning = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
            e.Handled = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            System.Windows.Point current = e.GetPosition(this);

            if (_orbiting)
            {
                double dx = current.X - _mouseStart.X;
                double dy = current.Y - _mouseStart.Y;
                _yaw = _yawStart - dx * 0.008;
                _pitch = Math.Clamp(_pitchStart + dy * 0.006, 0.10, 1.48);
                UpdateCamera();
                return;
            }

            if (_panning)
            {
                GetViewAxes(out Vector3D right, out Vector3D forward);
                double units = UnitsPerPixel();
                double dx = current.X - _mouseStart.X;
                double dy = current.Y - _mouseStart.Y;
                _target = _targetStart + right * (-dx * units) + forward * (dy * units);
                UpdateCamera();
                return;
            }

            if (_draggingItem && _dragItem != null && e.LeftButton == MouseButtonState.Pressed)
            {
                GetViewAxes(out Vector3D right, out Vector3D forward);
                double units = UnitsPerPixel();
                double dx = current.X - _dragStartMouse.X;
                double dy = current.Y - _dragStartMouse.Y;
                Vector3D move = right * (dx * units) + forward * (-dy * units);

                NumericsVector3 newPosition = new NumericsVector3(
                    _dragStartPosition.X + (float)move.X,
                    _dragStartPosition.Y,
                    _dragStartPosition.Z + (float)move.Z);

                SetPosition(_dragItem, newPosition);

                if (_itemToModels.TryGetValue(_dragItem, out List<GeometryModel3D>? models))
                {
                    foreach (GeometryModel3D model in models)
                    {
                        bool nativeModel =
                            model.Geometry
                                is MeshGeometry3D meshGeometry &&
                            meshGeometry.Positions.Count >
                                8;

                        model.Transform =
                            CreateObjectTransform(
                                newPosition,
                                GetForward(_dragItem),
                                nativeModel
                                    ? 0
                                    : _markerSize * 1.25);
                    }
                }
            }
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            _distance = Math.Clamp(_distance * (e.Delta > 0 ? 0.86 : 1.16), 15, 50000);
            UpdateCamera();
            e.Handled = true;
        }

        private void OnDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Right)
            {
                FitMap();
                e.Handled = true;
            }
        }

        private void GetViewAxes(out Vector3D right, out Vector3D forward)
        {
            Vector3D look = _camera.LookDirection;
            if (look.LengthSquared < 0.000001)
                look = new Vector3D(0, -1, -1);

            look.Normalize();
            right = Vector3D.CrossProduct(look, new Vector3D(0, 1, 0));
            right.Y = 0;
            if (right.LengthSquared > 0.000001)
                right.Normalize();

            forward = new Vector3D(look.X, 0, look.Z);
            if (forward.LengthSquared > 0.000001)
                forward.Normalize();
        }

        private double UnitsPerPixel()
        {
            double height = Math.Max(1, ActualHeight);
            double span = 2 * _distance * Math.Tan(_camera.FieldOfView * Math.PI / 360.0);
            return span / height;
        }

        private static bool TryGetPosition(object item, out NumericsVector3 position)
        {
            switch (item)
            {
                case ScenarioObject obj:
                    position = obj.Position;
                    return true;

                case ScenarioArtObject art:
                    position = art.Position;
                    return true;

                case ScenarioPlayerStart start:
                    position = start.Position;
                    return true;

                default:
                    position = default;
                    return false;
            }
        }

        private static void SetPosition(object item, NumericsVector3 position)
        {
            switch (item)
            {
                case ScenarioObject obj:
                    obj.Position = position;
                    break;

                case ScenarioArtObject art:
                    art.Position = position;
                    break;

                case ScenarioPlayerStart start:
                    start.Position = position;
                    break;
            }
        }
    }
}
