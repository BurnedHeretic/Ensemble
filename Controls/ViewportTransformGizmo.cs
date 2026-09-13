using Ensemble.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using NumericsVector3 = System.Numerics.Vector3;
using WpfPoint = System.Windows.Point;

namespace Ensemble.Controls
{
    /// <summary>
    /// Screen-space translation gizmo layered over MapViewport3D.
    ///
    /// The handles are projected from real world X/Y/Z axes, so they remain
    /// readable at any camera angle while all movement is written back to the
    /// selected Halo Wars object in world coordinates. The centre handle keeps
    /// the existing quick X/Z ground-plane movement workflow.
    /// </summary>
    internal sealed class ViewportTransformGizmo : IDisposable
    {
        private enum GizmoAxis
        {
            None,
            X,
            Y,
            Z,
            FreeXZ
        }

        private sealed class AxisVisual
        {
            public required GizmoAxis Axis { get; init; }
            public required Line Underlay { get; init; }
            public required Line HitLine { get; init; }
            public required Line Line { get; init; }
            public required Polygon Head { get; init; }
            public required TextBlock Label { get; init; }
        }

        private readonly MapViewport3D _owner;
        private readonly Grid _host;
        private readonly Canvas _overlay;
        private Viewport3D? _viewport;
        private readonly AxisVisual _xAxis;
        private readonly AxisVisual _yAxis;
        private readonly AxisVisual _zAxis;
        private readonly Border _centreHandle;
        private readonly TextBlock _coordinateReadout;

        private object? _selectedItem;
        private bool _disposed;
        private bool _dragging;
        private GizmoAxis _dragAxis;
        private WpfPoint _dragStartMouse;
        private NumericsVector3 _dragStartPosition;
        private Vector _dragScreenUnit;
        private double _dragWorldPerPixel;
        private long _lastLiveMoveMs;

        public event EventHandler<ScenarioItemMovedEventArgs>?
            LiveMoved;

        public event EventHandler<ScenarioItemMovedEventArgs>?
            MoveCommitted;

        public bool IsInteractionEnabled
        {
            get;
            set;
        } = true;

        public ViewportTransformGizmo(
            MapViewport3D owner)
        {
            _owner =
                owner
                ?? throw new ArgumentNullException(
                    nameof(owner));

            if (_owner.Content
                is not Grid host)
            {
                throw new InvalidOperationException(
                    "MapViewport3D does not expose the expected Grid host.");
            }

            _host = host;
            // MapViewport3D creates its Viewport3D before assigning the host Grid
            // as Content. Reading the host children directly works even before
            // WPF has built the visual tree; the old VisualTreeHelper-only lookup
            // could return null during startup and left the gizmo permanently hidden.
            _viewport =
                FindViewport();

            _overlay =
                new Canvas
                {
                    Background = null,
                    ClipToBounds = true,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    IsHitTestVisible = true
                };

            Panel.SetZIndex(
                _overlay,
                5000);

            _host.Children.Add(
                _overlay);

            _xAxis =
                CreateAxis(
                    GizmoAxis.X,
                    Color.FromRgb(
                        0xF2,
                        0x55,
                        0x55),
                    "X");

            _yAxis =
                CreateAxis(
                    GizmoAxis.Y,
                    Color.FromRgb(
                        0x68,
                        0xE8,
                        0x78),
                    "Y");

            _zAxis =
                CreateAxis(
                    GizmoAxis.Z,
                    Color.FromRgb(
                        0x45,
                        0xAE,
                        0xFF),
                    "Z");

            _centreHandle =
                new Border
                {
                    Width = 15,
                    Height = 15,
                    CornerRadius = new CornerRadius(2),
                    Background =
                        new SolidColorBrush(
                            Color.FromRgb(
                                0xE6,
                                0xA5,
                                0x3B)),
                    BorderBrush =
                        new SolidColorBrush(
                            Color.FromRgb(
                                0xFF,
                                0xDD,
                                0x84)),
                    BorderThickness = new Thickness(1.5),
                    Cursor = Cursors.SizeAll,
                    ToolTip = "Move freely on the X/Z ground plane"
                };

            _centreHandle.MouseLeftButtonDown +=
                (_, e) =>
                    BeginDrag(
                        GizmoAxis.FreeXZ,
                        e);

            _overlay.Children.Add(
                _centreHandle);

            _coordinateReadout =
                new TextBlock
                {
                    Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(
                                0xB9,
                                0xE5,
                                0xF2)),
                    Background =
                        new SolidColorBrush(
                            Color.FromArgb(
                                205,
                                0x05,
                                0x12,
                                0x1B)),
                    Padding = new Thickness(
                        5,
                        2,
                        5,
                        2),
                    FontFamily = new FontFamily(
                        "Consolas"),
                    FontSize = 10,
                    IsHitTestVisible = false
                };

            _overlay.Children.Add(
                _coordinateReadout);

            _overlay.MouseMove +=
                Overlay_MouseMove;

            _overlay.MouseLeftButtonUp +=
                Overlay_MouseLeftButtonUp;

            _overlay.LostMouseCapture +=
                Overlay_LostMouseCapture;

            _owner.SelectionChanged +=
                Owner_SelectionChanged;

            _owner.IsVisibleChanged +=
                Owner_IsVisibleChanged;

            CompositionTarget.Rendering +=
                CompositionTarget_Rendering;

            SetVisualsVisible(
                false);
        }

        public void SetSelectedItem(
            object? item)
        {
            if (ReferenceEquals(
                    _selectedItem,
                    item))
            {
                UpdateVisuals();
                return;
            }

            if (_dragging)
            {
                CommitDrag(
                    releaseCapture:
                        true);
            }

            _selectedItem =
                item;

            UpdateVisuals();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            CompositionTarget.Rendering -=
                CompositionTarget_Rendering;

            _owner.SelectionChanged -=
                Owner_SelectionChanged;

            _owner.IsVisibleChanged -=
                Owner_IsVisibleChanged;

            _overlay.MouseMove -=
                Overlay_MouseMove;

            _overlay.MouseLeftButtonUp -=
                Overlay_MouseLeftButtonUp;

            _overlay.LostMouseCapture -=
                Overlay_LostMouseCapture;

            if (_host.Children.Contains(
                    _overlay))
            {
                _host.Children.Remove(
                    _overlay);
            }
        }

        private AxisVisual CreateAxis(
            GizmoAxis axis,
            Color color,
            string label)
        {
            SolidColorBrush brush =
                new SolidColorBrush(
                    color);

            SolidColorBrush glow =
                new SolidColorBrush(
                    Color.FromArgb(
                        95,
                        color.R,
                        color.G,
                        color.B));

            Line hitLine =
                new Line
                {
                    Stroke = Brushes.Transparent,
                    StrokeThickness = 18,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Cursor = Cursors.SizeAll,
                    ToolTip = $"Move along {label} axis"
                };

            Line line =
                new Line
                {
                    Stroke = brush,
                    StrokeThickness = 3.5,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    IsHitTestVisible = false
                };

            Line underlay =
                new Line
                {
                    Stroke = glow,
                    StrokeThickness = 7,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    IsHitTestVisible = false
                };

            Polygon head =
                new Polygon
                {
                    Fill = brush,
                    Stroke = Brushes.White,
                    StrokeThickness = 0.5,
                    Cursor = Cursors.SizeAll,
                    ToolTip = $"Move along {label} axis"
                };

            TextBlock text =
                new TextBlock
                {
                    Text = label,
                    Foreground = brush,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    IsHitTestVisible = false
                };

            hitLine.MouseLeftButtonDown +=
                (_, e) =>
                    BeginDrag(
                        axis,
                        e);

            head.MouseLeftButtonDown +=
                (_, e) =>
                    BeginDrag(
                        axis,
                        e);

            _overlay.Children.Add(
                underlay);

            _overlay.Children.Add(
                hitLine);

            _overlay.Children.Add(
                line);

            _overlay.Children.Add(
                head);

            _overlay.Children.Add(
                text);

            // Store the underlay alongside the main line by binding its
            // coordinates to the main line. This keeps the Halo Wars-style
            // glow without another bookkeeping object.
            underlay.SetBinding(
                Line.X1Property,
                new System.Windows.Data.Binding(
                    nameof(Line.X1))
                {
                    Source = line
                });

            underlay.SetBinding(
                Line.Y1Property,
                new System.Windows.Data.Binding(
                    nameof(Line.Y1))
                {
                    Source = line
                });

            underlay.SetBinding(
                Line.X2Property,
                new System.Windows.Data.Binding(
                    nameof(Line.X2))
                {
                    Source = line
                });

            underlay.SetBinding(
                Line.Y2Property,
                new System.Windows.Data.Binding(
                    nameof(Line.Y2))
                {
                    Source = line
                });

            return new AxisVisual
            {
                Axis = axis,
                Underlay = underlay,
                HitLine = hitLine,
                Line = line,
                Head = head,
                Label = text
            };
        }

        private void Owner_SelectionChanged(
            object? sender,
            ScenarioSelectionChangedEventArgs e)
        {
            SetSelectedItem(
                e.SelectedItem);
        }

        private void Owner_IsVisibleChanged(
            object sender,
            DependencyPropertyChangedEventArgs e)
        {
            UpdateVisuals();
        }

        private void CompositionTarget_Rendering(
            object? sender,
            EventArgs e)
        {
            if (!_dragging)
            {
                UpdateVisuals();
            }
        }

        private void BeginDrag(
            GizmoAxis axis,
            MouseButtonEventArgs e)
        {
            if (!IsInteractionEnabled ||
                _selectedItem == null ||
                !TryGetPosition(
                    _selectedItem,
                    out NumericsVector3 position))
            {
                return;
            }

            e.Handled = true;

            _dragging = true;
            _dragAxis = axis;
            _dragStartMouse =
                e.GetPosition(
                    _overlay);
            _dragStartPosition =
                position;

            if (!PrepareAxisDragProjection(
                    axis,
                    position))
            {
                _dragging = false;
                _dragAxis = GizmoAxis.None;

                return;
            }

            _overlay.CaptureMouse();
            _owner.Focus();

            switch (axis)
            {
                case GizmoAxis.Y:
                    _overlay.Cursor =
                        Cursors.SizeNS;
                    break;

                case GizmoAxis.FreeXZ:
                    _overlay.Cursor =
                        Cursors.SizeAll;
                    break;

                default:
                    _overlay.Cursor =
                        Cursors.SizeAll;
                    break;
            }
        }

        private bool PrepareAxisDragProjection(
            GizmoAxis axis,
            NumericsVector3 position)
        {
            if (axis ==
                GizmoAxis.FreeXZ)
            {
                _dragScreenUnit =
                    new Vector(
                        1,
                        0);

                _dragWorldPerPixel =
                    GetWorldUnitsPerPixel(
                        position);

                return true;
            }

            double axisLength =
                GetAxisWorldLength(
                    position);

            NumericsVector3 visualOrigin =
                GetVisualOrigin(
                    position,
                    axisLength);

            NumericsVector3 axisVector =
                AxisVector(
                    axis);

            if (!TryProject(
                    visualOrigin,
                    out WpfPoint start) ||
                !TryProject(
                    visualOrigin +
                    axisVector *
                    (float)axisLength,
                    out WpfPoint end))
            {
                return false;
            }

            Vector screenAxis =
                end -
                start;

            double pixelLength =
                screenAxis.Length;

            if (pixelLength <
                8)
            {
                // An axis can point almost directly at the camera. Keep it
                // draggable with a predictable fallback rather than letting
                // one pixel turn into hundreds of world units.
                screenAxis =
                    axis switch
                    {
                        GizmoAxis.Y =>
                            new Vector(
                                0,
                                -1),

                        GizmoAxis.Z =>
                            new Vector(
                                0,
                                1),

                        _ =>
                            new Vector(
                                1,
                                0)
                    };

                pixelLength =
                    Math.Max(
                        42,
                        pixelLength);
            }

            screenAxis.Normalize();

            _dragScreenUnit =
                screenAxis;

            _dragWorldPerPixel =
                axisLength /
                pixelLength;

            return true;
        }

        private void Overlay_MouseMove(
            object sender,
            MouseEventArgs e)
        {
            if (!_dragging ||
                _selectedItem == null ||
                e.LeftButton !=
                    MouseButtonState.Pressed)
            {
                return;
            }

            WpfPoint current =
                e.GetPosition(
                    _overlay);

            Vector mouseDelta =
                current -
                _dragStartMouse;

            NumericsVector3 newPosition;

            if (_dragAxis ==
                GizmoAxis.FreeXZ)
            {
                newPosition =
                    MoveOnGroundPlane(
                        _dragStartPosition,
                        mouseDelta);
            }
            else
            {
                double pixels =
                    mouseDelta.X *
                        _dragScreenUnit.X +
                    mouseDelta.Y *
                        _dragScreenUnit.Y;

                double worldDelta =
                    pixels *
                    _dragWorldPerPixel;

                newPosition =
                    _dragStartPosition +
                    AxisVector(
                        _dragAxis) *
                    (float)worldDelta;
            }

            SetPosition(
                _selectedItem,
                newPosition);

            UpdateVisuals();

            long now =
                Environment.TickCount64;

            if (now -
                    _lastLiveMoveMs >=
                30)
            {
                _lastLiveMoveMs =
                    now;

                LiveMoved?.Invoke(
                    this,
                    new ScenarioItemMovedEventArgs(
                        _selectedItem,
                        _dragStartPosition,
                        newPosition));
            }

            e.Handled = true;
        }

        private void Overlay_MouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            if (!_dragging)
            {
                return;
            }

            e.Handled = true;

            CommitDrag(
                releaseCapture:
                    true);
        }

        private void Overlay_LostMouseCapture(
            object sender,
            MouseEventArgs e)
        {
            if (_dragging)
            {
                CommitDrag(
                    releaseCapture:
                        false);
            }
        }

        private void CommitDrag(
            bool releaseCapture)
        {
            if (!_dragging)
            {
                return;
            }

            object? item =
                _selectedItem;

            NumericsVector3 oldPosition =
                _dragStartPosition;

            NumericsVector3 newPosition =
                oldPosition;

            if (item !=
                null)
            {
                TryGetPosition(
                    item,
                    out newPosition);
            }

            _dragging = false;
            _dragAxis = GizmoAxis.None;
            _overlay.Cursor = Cursors.Arrow;

            if (releaseCapture &&
                _overlay.IsMouseCaptured)
            {
                _overlay.ReleaseMouseCapture();
            }

            if (item !=
                    null &&
                NumericsVector3.DistanceSquared(
                    oldPosition,
                    newPosition) >
                0.000001f)
            {
                MoveCommitted?.Invoke(
                    this,
                    new ScenarioItemMovedEventArgs(
                        item,
                        oldPosition,
                        newPosition));
            }

            UpdateVisuals();
        }

        private NumericsVector3 MoveOnGroundPlane(
            NumericsVector3 start,
            Vector mouseDelta)
        {
            Viewport3D? viewport =
                FindViewport();

            if (viewport?.Camera
                is not PerspectiveCamera camera)
            {
                return start;
            }

            Vector3D look =
                camera.LookDirection;

            if (look.LengthSquared <
                0.000001)
            {
                return start;
            }

            look.Normalize();

            Vector3D right =
                Vector3D.CrossProduct(
                    look,
                    new Vector3D(
                        0,
                        1,
                        0));

            right.Y =
                0;

            if (right.LengthSquared >
                0.000001)
            {
                right.Normalize();
            }

            Vector3D forward =
                new Vector3D(
                    look.X,
                    0,
                    look.Z);

            if (forward.LengthSquared >
                0.000001)
            {
                forward.Normalize();
            }

            double units =
                Math.Max(
                    0.0001,
                    _dragWorldPerPixel);

            Vector3D move =
                right *
                    (mouseDelta.X *
                     units)
                +
                forward *
                    (-mouseDelta.Y *
                     units);

            return new NumericsVector3(
                start.X +
                    (float)move.X,
                start.Y,
                start.Z +
                    (float)move.Z);
        }

        private void UpdateVisuals()
        {
            if (_disposed ||
                !_owner.IsVisible ||
                _selectedItem ==
                    null ||
                !TryGetPosition(
                    _selectedItem,
                    out NumericsVector3 position))
            {
                SetVisualsVisible(
                    false);

                return;
            }

            double axisLength =
                GetAxisWorldLength(
                    position);

            NumericsVector3 origin =
                GetVisualOrigin(
                    position,
                    axisLength);

            if (!TryProject(
                    origin,
                    out WpfPoint centre))
            {
                SetVisualsVisible(
                    false);

                return;
            }

            bool xVisible =
                UpdateAxisVisual(
                    _xAxis,
                    origin,
                    NumericsVector3.UnitX,
                    axisLength);

            bool yVisible =
                UpdateAxisVisual(
                    _yAxis,
                    origin,
                    NumericsVector3.UnitY,
                    axisLength);

            bool zVisible =
                UpdateAxisVisual(
                    _zAxis,
                    origin,
                    NumericsVector3.UnitZ,
                    axisLength);

            _centreHandle.Visibility =
                Visibility.Visible;

            Canvas.SetLeft(
                _centreHandle,
                centre.X -
                _centreHandle.Width *
                0.5);

            Canvas.SetTop(
                _centreHandle,
                centre.Y -
                _centreHandle.Height *
                0.5);

            _coordinateReadout.Text =
                $"X {position.X:0.##}   " +
                $"Y {position.Y:0.##}   " +
                $"Z {position.Z:0.##}";

            Canvas.SetLeft(
                _coordinateReadout,
                centre.X +
                12);

            Canvas.SetTop(
                _coordinateReadout,
                centre.Y +
                12);

            _coordinateReadout.Visibility =
                Visibility.Visible;

            _xAxis.Line.Visibility =
                xVisible
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            _xAxis.Underlay.Visibility =
                _xAxis.Line.Visibility;

            _xAxis.HitLine.Visibility =
                _xAxis.Line.Visibility;

            _xAxis.Head.Visibility =
                _xAxis.Line.Visibility;

            _xAxis.Label.Visibility =
                _xAxis.Line.Visibility;

            _yAxis.Line.Visibility =
                yVisible
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            _yAxis.Underlay.Visibility =
                _yAxis.Line.Visibility;

            _yAxis.HitLine.Visibility =
                _yAxis.Line.Visibility;

            _yAxis.Head.Visibility =
                _yAxis.Line.Visibility;

            _yAxis.Label.Visibility =
                _yAxis.Line.Visibility;

            _zAxis.Line.Visibility =
                zVisible
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            _zAxis.Underlay.Visibility =
                _zAxis.Line.Visibility;

            _zAxis.HitLine.Visibility =
                _zAxis.Line.Visibility;

            _zAxis.Head.Visibility =
                _zAxis.Line.Visibility;

            _zAxis.Label.Visibility =
                _zAxis.Line.Visibility;

            _overlay.IsHitTestVisible =
                IsInteractionEnabled;
        }

        private bool UpdateAxisVisual(
            AxisVisual visual,
            NumericsVector3 origin,
            NumericsVector3 axis,
            double worldLength)
        {
            if (!TryProject(
                    origin,
                    out WpfPoint start) ||
                !TryProject(
                    origin +
                    axis *
                    (float)worldLength,
                    out WpfPoint end))
            {
                return false;
            }

            visual.Line.X1 =
                start.X;
            visual.Line.Y1 =
                start.Y;
            visual.Line.X2 =
                end.X;
            visual.Line.Y2 =
                end.Y;

            visual.HitLine.X1 =
                start.X;
            visual.HitLine.Y1 =
                start.Y;
            visual.HitLine.X2 =
                end.X;
            visual.HitLine.Y2 =
                end.Y;

            Vector direction =
                end -
                start;

            if (direction.Length <
                2)
            {
                return false;
            }

            direction.Normalize();

            Vector perpendicular =
                new Vector(
                    -direction.Y,
                    direction.X);

            WpfPoint basePoint =
                end -
                direction *
                13;

            visual.Head.Points =
                new PointCollection
                {
                    end,
                    basePoint +
                        perpendicular *
                        6,
                    basePoint -
                        perpendicular *
                        6
                };

            Canvas.SetLeft(
                visual.Label,
                end.X +
                perpendicular.X *
                7 -
                4);

            Canvas.SetTop(
                visual.Label,
                end.Y +
                perpendicular.Y *
                7 -
                7);

            return true;
        }

        private void SetVisualsVisible(
            bool visible)
        {
            Visibility value =
                visible
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            foreach (AxisVisual axis
                     in new[]
                     {
                         _xAxis,
                         _yAxis,
                         _zAxis
                     })
            {
                axis.Underlay.Visibility =
                    value;
                axis.Line.Visibility =
                    value;
                axis.HitLine.Visibility =
                    value;
                axis.Head.Visibility =
                    value;
                axis.Label.Visibility =
                    value;
            }

            _centreHandle.Visibility =
                value;

            _coordinateReadout.Visibility =
                value;
        }

        private bool TryProject(
            NumericsVector3 world,
            out WpfPoint point)
        {
            point =
                default;

            Viewport3D? viewport =
                FindViewport();

            if (viewport?.Camera
                    is not PerspectiveCamera camera ||
                viewport.ActualWidth <=
                    1 ||
                viewport.ActualHeight <=
                    1)
            {
                return false;
            }

            Vector3D forward =
                camera.LookDirection;

            Vector3D up =
                camera.UpDirection;

            if (forward.LengthSquared <
                    0.000001 ||
                up.LengthSquared <
                    0.000001)
            {
                return false;
            }

            forward.Normalize();
            up.Normalize();

            Vector3D right =
                Vector3D.CrossProduct(
                    forward,
                    up);

            if (right.LengthSquared <
                0.000001)
            {
                return false;
            }

            right.Normalize();

            up =
                Vector3D.CrossProduct(
                    right,
                    forward);

            up.Normalize();

            Vector3D relative =
                new Vector3D(
                    world.X -
                        camera.Position.X,
                    world.Y -
                        camera.Position.Y,
                    world.Z -
                        camera.Position.Z);

            double cameraZ =
                Vector3D.DotProduct(
                    relative,
                    forward);

            if (cameraZ <=
                Math.Max(
                    0.001,
                    camera.NearPlaneDistance))
            {
                return false;
            }

            double cameraX =
                Vector3D.DotProduct(
                    relative,
                    right);

            double cameraY =
                Vector3D.DotProduct(
                    relative,
                    up);

            double tanHalfHorizontal =
                Math.Tan(
                    camera.FieldOfView *
                    Math.PI /
                    360.0);

            double aspect =
                viewport.ActualWidth /
                viewport.ActualHeight;

            double tanHalfVertical =
                tanHalfHorizontal /
                Math.Max(
                    0.0001,
                    aspect);

            double ndcX =
                cameraX /
                (cameraZ *
                 tanHalfHorizontal);

            double ndcY =
                cameraY /
                (cameraZ *
                 tanHalfVertical);

            point =
                new WpfPoint(
                    (ndcX +
                     1.0) *
                    0.5 *
                    viewport.ActualWidth,
                    (1.0 -
                     ndcY) *
                    0.5 *
                    viewport.ActualHeight);

            return double.IsFinite(
                       point.X) &&
                   double.IsFinite(
                       point.Y);
        }

        private double GetAxisWorldLength(
            NumericsVector3 position)
        {
            Viewport3D? viewport =
                FindViewport();

            if (viewport?.Camera
                is not PerspectiveCamera camera)
            {
                return 40;
            }

            double dx =
                position.X -
                camera.Position.X;

            double dy =
                position.Y -
                camera.Position.Y;

            double dz =
                position.Z -
                camera.Position.Z;

            double distance =
                Math.Sqrt(
                    dx *
                    dx +
                    dy *
                    dy +
                    dz *
                    dz);

            return Math.Clamp(
                distance *
                0.075,
                14,
                180);
        }

        private double GetWorldUnitsPerPixel(
            NumericsVector3 position)
        {
            Viewport3D? viewport =
                FindViewport();

            if (viewport?.Camera
                    is not PerspectiveCamera camera ||
                viewport.ActualWidth <=
                    1)
            {
                return 1;
            }

            double dx =
                position.X -
                camera.Position.X;

            double dy =
                position.Y -
                camera.Position.Y;

            double dz =
                position.Z -
                camera.Position.Z;

            double distance =
                Math.Max(
                    1,
                    Math.Sqrt(
                        dx *
                        dx +
                        dy *
                        dy +
                        dz *
                        dz));

            double horizontalSpan =
                2.0 *
                distance *
                Math.Tan(
                    camera.FieldOfView *
                    Math.PI /
                    360.0);

            return horizontalSpan /
                Math.Max(
                    1,
                    viewport.ActualWidth);
        }

        private static NumericsVector3 GetVisualOrigin(
            NumericsVector3 position,
            double axisLength)
        {
            // Raise the handles slightly so X/Z do not disappear into the
            // terrain or the base of a native mesh.
            return position +
                new NumericsVector3(
                    0,
                    (float)(axisLength *
                            0.08),
                    0);
        }

        private static NumericsVector3 AxisVector(
            GizmoAxis axis)
        {
            return axis switch
            {
                GizmoAxis.X =>
                    NumericsVector3.UnitX,

                GizmoAxis.Y =>
                    NumericsVector3.UnitY,

                GizmoAxis.Z =>
                    NumericsVector3.UnitZ,

                _ =>
                    NumericsVector3.Zero
            };
        }

        private static bool TryGetPosition(
            object item,
            out NumericsVector3 position)
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

        private static void SetPosition(
            object item,
            NumericsVector3 position)
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

        private Viewport3D? FindViewport()
        {
            if (_viewport !=
                null)
            {
                return _viewport;
            }

            // The Viewport3D is a direct child of MapViewport3D's host Grid.
            // This path does not depend on the WPF visual tree being measured.
            _viewport =
                _host.Children
                    .OfType<Viewport3D>()
                    .FirstOrDefault();

            // Keep a visual-tree fallback for future layout changes.
            _viewport ??=
                FindVisualDescendant<Viewport3D>(
                    _owner);

            return _viewport;
        }

        private static T? FindVisualDescendant<T>(
            DependencyObject root)
            where T : DependencyObject
        {
            if (root
                is T match)
            {
                return match;
            }

            int count =
                VisualTreeHelper.GetChildrenCount(
                    root);

            for (int i = 0;
                 i < count;
                 i++)
            {
                DependencyObject child =
                    VisualTreeHelper.GetChild(
                        root,
                        i);

                T? result =
                    FindVisualDescendant<T>(
                        child);

                if (result !=
                    null)
                {
                    return result;
                }
            }

            return null;
        }
    }
}
