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
    internal enum ViewportGizmoMode
    {
        Move,
        Rotate,
        Scale
    }

    internal sealed class ScenarioItemScaledEventArgs : EventArgs
    {
        public ScenarioItemScaledEventArgs(
            object item,
            float oldScale,
            float newScale)
        {
            Item = item;
            OldScale = oldScale;
            NewScale = newScale;
        }

        public object Item { get; }
        public float OldScale { get; }
        public float NewScale { get; }
    }

    /// <summary>
    /// Screen-space transform gizmo layered over MapViewport3D.
    ///
    /// W = move, E = rotate around world Y, R = uniform scale. Scale is
    /// intentionally delegated back to MainWindow because Halo Wars SC2
    /// ArtObjects do not expose a standalone scale field; Ensemble bakes a
    /// custom-mesh scale into the queued UGX instead.
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

        private enum DragKind
        {
            None,
            Move,
            Rotate,
            Scale
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

        private readonly StackPanel _modeBar;
        private readonly Button _moveButton;
        private readonly Button _rotateButton;
        private readonly Button _scaleButton;

        private readonly Ellipse _rotationRing;
        private readonly Ellipse _rotationHitRing;
        private readonly Line _scaleGuide;
        private readonly Line _scaleGuideGlow;
        private readonly Border _scaleHandle;

        private object? _selectedItem;
        private bool _disposed;
        private bool _dragging;
        private DragKind _dragKind;
        private GizmoAxis _dragAxis;
        private ViewportGizmoMode _mode = ViewportGizmoMode.Move;

        private WpfPoint _dragStartMouse;
        private NumericsVector3 _dragStartPosition;
        private NumericsVector3 _dragStartForward;
        private NumericsVector3 _dragStartRight;
        private float _dragStartScale = 1.0f;
        private double _dragStartYaw;
        private double _dragStartPointerAngle;
        private WpfPoint _dragScreenCentre;
        private Vector _dragScreenUnit;
        private double _dragWorldPerPixel;
        private long _lastLiveTransformMs;

        public event EventHandler<ScenarioItemMovedEventArgs>? LiveMoved;
        public event EventHandler<ScenarioItemMovedEventArgs>? MoveCommitted;
        public event EventHandler<ScenarioItemRotatedEventArgs>? LiveRotated;
        public event EventHandler<ScenarioItemRotatedEventArgs>? RotationCommitted;
        public event EventHandler<ScenarioItemScaledEventArgs>? LiveScaled;
        public event EventHandler<ScenarioItemScaledEventArgs>? ScaleCommitted;

        public Func<object, float?>? ScaleReader { get; set; }
        public Action<object, float>? ScaleWriter { get; set; }

        public bool IsInteractionEnabled
        {
            get;
            set;
        } = true;

        public ViewportGizmoMode Mode => _mode;

        public ViewportTransformGizmo(
            MapViewport3D owner)
        {
            _owner =
                owner
                ?? throw new ArgumentNullException(
                    nameof(owner));

            if (_owner.Content is not Grid host)
            {
                throw new InvalidOperationException(
                    "MapViewport3D does not expose the expected Grid host.");
            }

            _host = host;
            _viewport = FindViewport();

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
                    Color.FromRgb(0xF2, 0x55, 0x55),
                    "X");

            _yAxis =
                CreateAxis(
                    GizmoAxis.Y,
                    Color.FromRgb(0x68, 0xE8, 0x78),
                    "Y");

            _zAxis =
                CreateAxis(
                    GizmoAxis.Z,
                    Color.FromRgb(0x45, 0xAE, 0xFF),
                    "Z");

            _centreHandle =
                new Border
                {
                    Width = 15,
                    Height = 15,
                    CornerRadius = new CornerRadius(2),
                    Background =
                        new SolidColorBrush(
                            Color.FromRgb(0xE6, 0xA5, 0x3B)),
                    BorderBrush =
                        new SolidColorBrush(
                            Color.FromRgb(0xFF, 0xDD, 0x84)),
                    BorderThickness = new Thickness(1.5),
                    Cursor = Cursors.SizeAll,
                    ToolTip = "Move freely on the X/Z ground plane"
                };

            _centreHandle.MouseLeftButtonDown +=
                (_, e) =>
                    BeginMoveDrag(
                        GizmoAxis.FreeXZ,
                        e);

            _overlay.Children.Add(
                _centreHandle);

            _coordinateReadout =
                new TextBlock
                {
                    Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(0xB9, 0xE5, 0xF2)),
                    Background =
                        new SolidColorBrush(
                            Color.FromArgb(205, 0x05, 0x12, 0x1B)),
                    Padding = new Thickness(5, 2, 5, 2),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10,
                    IsHitTestVisible = false
                };

            _overlay.Children.Add(
                _coordinateReadout);

            _rotationHitRing =
                new Ellipse
                {
                    Width = 112,
                    Height = 112,
                    Stroke = Brushes.Transparent,
                    StrokeThickness = 18,
                    Fill = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    ToolTip = "Drag to rotate around world Y"
                };

            _rotationRing =
                new Ellipse
                {
                    Width = 96,
                    Height = 96,
                    Stroke =
                        new SolidColorBrush(
                            Color.FromRgb(0xF3, 0xB8, 0x42)),
                    StrokeThickness = 3.5,
                    Fill = Brushes.Transparent,
                    IsHitTestVisible = false
                };

            _rotationHitRing.MouseLeftButtonDown +=
                (_, e) => BeginRotateDrag(e);

            _overlay.Children.Add(_rotationHitRing);
            _overlay.Children.Add(_rotationRing);

            _scaleGuideGlow =
                new Line
                {
                    Stroke =
                        new SolidColorBrush(
                            Color.FromArgb(90, 0xC8, 0x7A, 0xFF)),
                    StrokeThickness = 8,
                    IsHitTestVisible = false
                };

            _scaleGuide =
                new Line
                {
                    Stroke =
                        new SolidColorBrush(
                            Color.FromRgb(0xD6, 0x9A, 0xFF)),
                    StrokeThickness = 3,
                    IsHitTestVisible = false
                };

            _scaleHandle =
                new Border
                {
                    Width = 19,
                    Height = 19,
                    CornerRadius = new CornerRadius(2),
                    Background =
                        new SolidColorBrush(
                            Color.FromRgb(0x9E, 0x52, 0xCE)),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(1.2),
                    Cursor = Cursors.SizeNWSE,
                    ToolTip = "Drag to uniformly scale this imported custom mesh"
                };

            _scaleHandle.MouseLeftButtonDown +=
                (_, e) => BeginScaleDrag(e);

            _overlay.Children.Add(_scaleGuideGlow);
            _overlay.Children.Add(_scaleGuide);
            _overlay.Children.Add(_scaleHandle);

            _modeBar =
                new StackPanel
                {
                    Orientation = Orientation.Horizontal
                };

            _moveButton = CreateModeButton("MOVE [W]");
            _rotateButton = CreateModeButton("ROTATE [E]");
            _scaleButton = CreateModeButton("SCALE [R]");

            _moveButton.Click +=
                (_, _) => SetMode(ViewportGizmoMode.Move);

            _rotateButton.Click +=
                (_, _) => SetMode(ViewportGizmoMode.Rotate);

            _scaleButton.Click +=
                (_, _) => SetMode(ViewportGizmoMode.Scale);

            _modeBar.Children.Add(_moveButton);
            _modeBar.Children.Add(_rotateButton);
            _modeBar.Children.Add(_scaleButton);

            _overlay.Children.Add(_modeBar);
            Canvas.SetLeft(_modeBar, 12);
            Canvas.SetTop(_modeBar, 42);

            _overlay.MouseMove += Overlay_MouseMove;
            _overlay.MouseLeftButtonUp += Overlay_MouseLeftButtonUp;
            _overlay.LostMouseCapture += Overlay_LostMouseCapture;

            _owner.SelectionChanged += Owner_SelectionChanged;
            _owner.IsVisibleChanged += Owner_IsVisibleChanged;
            _owner.PreviewKeyDown += Owner_PreviewKeyDown;

            CompositionTarget.Rendering += CompositionTarget_Rendering;

            SetVisualsVisible(false);
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
                    releaseCapture: true);
            }

            _selectedItem = item;

            if (_mode == ViewportGizmoMode.Scale &&
                !CanScaleSelectedItem())
            {
                _mode = ViewportGizmoMode.Move;
            }

            UpdateVisuals();
        }

        public void SetMode(
            ViewportGizmoMode mode)
        {
            if (_dragging)
            {
                CommitDrag(
                    releaseCapture: true);
            }

            if (mode == ViewportGizmoMode.Rotate &&
                (_selectedItem == null ||
                 !TryGetOrientation(
                     _selectedItem,
                     out _,
                     out _)))
            {
                mode = ViewportGizmoMode.Move;
            }

            if (mode == ViewportGizmoMode.Scale &&
                !CanScaleSelectedItem())
            {
                mode = ViewportGizmoMode.Move;
            }

            _mode = mode;
            UpdateVisuals();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            _owner.SelectionChanged -= Owner_SelectionChanged;
            _owner.IsVisibleChanged -= Owner_IsVisibleChanged;
            _owner.PreviewKeyDown -= Owner_PreviewKeyDown;

            _overlay.MouseMove -= Overlay_MouseMove;
            _overlay.MouseLeftButtonUp -= Overlay_MouseLeftButtonUp;
            _overlay.LostMouseCapture -= Overlay_LostMouseCapture;

            if (_host.Children.Contains(_overlay))
                _host.Children.Remove(_overlay);
        }

        private static Button CreateModeButton(
            string text)
        {
            return new Button
            {
                Content = text,
                MinWidth = 82,
                Height = 27,
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0, 0, 4, 0),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                ToolTip = text
            };
        }

        private AxisVisual CreateAxis(
            GizmoAxis axis,
            Color color,
            string label)
        {
            SolidColorBrush brush =
                new SolidColorBrush(color);

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
                    BeginMoveDrag(
                        axis,
                        e);

            head.MouseLeftButtonDown +=
                (_, e) =>
                    BeginMoveDrag(
                        axis,
                        e);

            _overlay.Children.Add(underlay);
            _overlay.Children.Add(hitLine);
            _overlay.Children.Add(line);
            _overlay.Children.Add(head);
            _overlay.Children.Add(text);

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
            SetSelectedItem(e.SelectedItem);
        }

        private void Owner_IsVisibleChanged(
            object sender,
            DependencyPropertyChangedEventArgs e)
        {
            UpdateVisuals();
        }

        private void Owner_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Handled ||
                Keyboard.Modifiers != ModifierKeys.None ||
                Keyboard.FocusedElement is TextBox)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.W:
                    SetMode(ViewportGizmoMode.Move);
                    e.Handled = true;
                    break;

                case Key.E:
                    SetMode(ViewportGizmoMode.Rotate);
                    e.Handled = true;
                    break;

                case Key.R:
                    if (CanScaleSelectedItem())
                    {
                        SetMode(ViewportGizmoMode.Scale);
                        e.Handled = true;
                    }
                    break;
            }
        }

        private void CompositionTarget_Rendering(
            object? sender,
            EventArgs e)
        {
            if (!_dragging)
                UpdateVisuals();
        }

        private void BeginMoveDrag(
            GizmoAxis axis,
            MouseButtonEventArgs e)
        {
            if (_mode != ViewportGizmoMode.Move ||
                !IsInteractionEnabled ||
                _selectedItem == null ||
                !TryGetPosition(
                    _selectedItem,
                    out NumericsVector3 position))
            {
                return;
            }

            e.Handled = true;

            _dragging = true;
            _dragKind = DragKind.Move;
            _dragAxis = axis;
            _dragStartMouse = e.GetPosition(_overlay);
            _dragStartPosition = position;

            if (!PrepareAxisDragProjection(
                    axis,
                    position))
            {
                _dragging = false;
                _dragKind = DragKind.None;
                _dragAxis = GizmoAxis.None;
                return;
            }

            _overlay.CaptureMouse();
            _owner.Focus();
            _overlay.Cursor =
                axis == GizmoAxis.Y
                    ? Cursors.SizeNS
                    : Cursors.SizeAll;
        }

        private void BeginRotateDrag(
            MouseButtonEventArgs e)
        {
            if (_mode != ViewportGizmoMode.Rotate ||
                !IsInteractionEnabled ||
                _selectedItem == null ||
                !TryGetPosition(
                    _selectedItem,
                    out NumericsVector3 position) ||
                !TryGetOrientation(
                    _selectedItem,
                    out NumericsVector3 forward,
                    out NumericsVector3 right))
            {
                return;
            }

            double axisLength =
                GetAxisWorldLength(position);

            if (!TryProject(
                    GetVisualOrigin(position, axisLength),
                    out WpfPoint centre))
            {
                return;
            }

            e.Handled = true;

            _dragging = true;
            _dragKind = DragKind.Rotate;
            _dragAxis = GizmoAxis.None;
            _dragStartMouse = e.GetPosition(_overlay);
            _dragStartForward = forward;
            _dragStartRight = right;
            _dragStartYaw =
                Math.Atan2(
                    forward.X,
                    forward.Z);
            _dragScreenCentre = centre;
            _dragStartPointerAngle =
                PointerAngle(
                    _dragStartMouse,
                    centre);

            _overlay.CaptureMouse();
            _owner.Focus();
            _overlay.Cursor = Cursors.Hand;
        }

        private void BeginScaleDrag(
            MouseButtonEventArgs e)
        {
            if (_mode != ViewportGizmoMode.Scale ||
                !IsInteractionEnabled ||
                _selectedItem == null ||
                ScaleReader?.Invoke(_selectedItem)
                    is not float scale)
            {
                return;
            }

            e.Handled = true;

            _dragging = true;
            _dragKind = DragKind.Scale;
            _dragAxis = GizmoAxis.None;
            _dragStartMouse = e.GetPosition(_overlay);
            _dragStartScale = scale;

            _overlay.CaptureMouse();
            _owner.Focus();
            _overlay.Cursor = Cursors.SizeNWSE;
        }

        private bool PrepareAxisDragProjection(
            GizmoAxis axis,
            NumericsVector3 position)
        {
            if (axis == GizmoAxis.FreeXZ)
            {
                _dragScreenUnit = new Vector(1, 0);
                _dragWorldPerPixel = GetWorldUnitsPerPixel(position);
                return true;
            }

            double axisLength =
                GetAxisWorldLength(position);

            NumericsVector3 visualOrigin =
                GetVisualOrigin(
                    position,
                    axisLength);

            NumericsVector3 axisVector =
                AxisVector(axis);

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
                end - start;

            double pixelLength =
                screenAxis.Length;

            if (pixelLength < 8)
            {
                screenAxis =
                    axis switch
                    {
                        GizmoAxis.Y => new Vector(0, -1),
                        GizmoAxis.Z => new Vector(0, 1),
                        _ => new Vector(1, 0)
                    };

                pixelLength = Math.Max(42, pixelLength);
            }

            screenAxis.Normalize();

            _dragScreenUnit = screenAxis;
            _dragWorldPerPixel = axisLength / pixelLength;
            return true;
        }

        private void Overlay_MouseMove(
            object sender,
            MouseEventArgs e)
        {
            if (!_dragging ||
                _selectedItem == null ||
                e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            WpfPoint current =
                e.GetPosition(_overlay);

            switch (_dragKind)
            {
                case DragKind.Move:
                    UpdateMoveDrag(current);
                    break;

                case DragKind.Rotate:
                    UpdateRotateDrag(current);
                    break;

                case DragKind.Scale:
                    UpdateScaleDrag(current);
                    break;
            }

            e.Handled = true;
        }

        private void UpdateMoveDrag(
            WpfPoint current)
        {
            if (_selectedItem == null)
                return;

            Vector mouseDelta =
                current - _dragStartMouse;

            NumericsVector3 newPosition;

            if (_dragAxis == GizmoAxis.FreeXZ)
            {
                newPosition =
                    MoveOnGroundPlane(
                        _dragStartPosition,
                        mouseDelta);
            }
            else
            {
                double pixels =
                    mouseDelta.X * _dragScreenUnit.X +
                    mouseDelta.Y * _dragScreenUnit.Y;

                double worldDelta =
                    pixels * _dragWorldPerPixel;

                newPosition =
                    _dragStartPosition +
                    AxisVector(_dragAxis) *
                    (float)worldDelta;
            }

            SetPosition(
                _selectedItem,
                newPosition);

            UpdateVisuals();

            if (ShouldSendLiveEvent())
            {
                LiveMoved?.Invoke(
                    this,
                    new ScenarioItemMovedEventArgs(
                        _selectedItem,
                        _dragStartPosition,
                        newPosition));
            }
        }

        private void UpdateRotateDrag(
            WpfPoint current)
        {
            if (_selectedItem == null)
                return;

            double pointerAngle =
                PointerAngle(
                    current,
                    _dragScreenCentre);

            double delta =
                NormaliseRadians(
                    pointerAngle -
                    _dragStartPointerAngle);

            // Screen Y grows downward. Subtracting the screen-space angle gives
            // an intuitive clockwise/counter-clockwise world-Y rotation.
            double yaw =
                _dragStartYaw -
                delta;

            NumericsVector3 newForward =
                new NumericsVector3(
                    (float)Math.Sin(yaw),
                    0,
                    (float)Math.Cos(yaw));

            NumericsVector3 newRight =
                new NumericsVector3(
                    (float)Math.Cos(yaw),
                    0,
                    (float)-Math.Sin(yaw));

            SetOrientation(
                _selectedItem,
                newForward,
                newRight);

            UpdateVisuals();

            if (ShouldSendLiveEvent())
            {
                LiveRotated?.Invoke(
                    this,
                    new ScenarioItemRotatedEventArgs(
                        _selectedItem,
                        _dragStartForward,
                        _dragStartRight,
                        newForward,
                        newRight));
            }
        }

        private void UpdateScaleDrag(
            WpfPoint current)
        {
            if (_selectedItem == null ||
                ScaleWriter == null)
            {
                return;
            }

            Vector delta =
                current - _dragStartMouse;

            double signedPixels =
                delta.X -
                delta.Y * 0.65;

            float factor =
                (float)Math.Pow(
                    2.0,
                    signedPixels /
                    160.0);

            float newScale =
                Math.Clamp(
                    _dragStartScale * factor,
                    0.05f,
                    20.0f);

            ScaleWriter(
                _selectedItem,
                newScale);

            UpdateVisuals();

            if (ShouldSendLiveEvent())
            {
                LiveScaled?.Invoke(
                    this,
                    new ScenarioItemScaledEventArgs(
                        _selectedItem,
                        _dragStartScale,
                        newScale));
            }
        }

        private bool ShouldSendLiveEvent()
        {
            long now =
                Environment.TickCount64;

            if (now - _lastLiveTransformMs < 30)
                return false;

            _lastLiveTransformMs = now;
            return true;
        }

        private void Overlay_MouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            if (!_dragging)
                return;

            e.Handled = true;
            CommitDrag(releaseCapture: true);
        }

        private void Overlay_LostMouseCapture(
            object sender,
            MouseEventArgs e)
        {
            if (_dragging)
                CommitDrag(releaseCapture: false);
        }

        private void CommitDrag(
            bool releaseCapture)
        {
            if (!_dragging)
                return;

            object? item = _selectedItem;
            DragKind kind = _dragKind;

            _dragging = false;
            _dragKind = DragKind.None;
            _dragAxis = GizmoAxis.None;
            _overlay.Cursor = Cursors.Arrow;

            if (releaseCapture &&
                _overlay.IsMouseCaptured)
            {
                _overlay.ReleaseMouseCapture();
            }

            if (item != null)
            {
                switch (kind)
                {
                    case DragKind.Move:
                        if (TryGetPosition(
                                item,
                                out NumericsVector3 newPosition) &&
                            NumericsVector3.DistanceSquared(
                                _dragStartPosition,
                                newPosition) >
                            0.000001f)
                        {
                            MoveCommitted?.Invoke(
                                this,
                                new ScenarioItemMovedEventArgs(
                                    item,
                                    _dragStartPosition,
                                    newPosition));
                        }
                        break;

                    case DragKind.Rotate:
                        if (TryGetOrientation(
                                item,
                                out NumericsVector3 newForward,
                                out NumericsVector3 newRight) &&
                            (NumericsVector3.DistanceSquared(
                                 _dragStartForward,
                                 newForward) >
                             0.000001f ||
                             NumericsVector3.DistanceSquared(
                                 _dragStartRight,
                                 newRight) >
                             0.000001f))
                        {
                            RotationCommitted?.Invoke(
                                this,
                                new ScenarioItemRotatedEventArgs(
                                    item,
                                    _dragStartForward,
                                    _dragStartRight,
                                    newForward,
                                    newRight));
                        }
                        break;

                    case DragKind.Scale:
                        if (ScaleReader?.Invoke(item)
                                is float newScale &&
                            Math.Abs(
                                newScale -
                                _dragStartScale) >
                            0.00001f)
                        {
                            ScaleCommitted?.Invoke(
                                this,
                                new ScenarioItemScaledEventArgs(
                                    item,
                                    _dragStartScale,
                                    newScale));
                        }
                        break;
                }
            }

            UpdateVisuals();
        }

        private NumericsVector3 MoveOnGroundPlane(
            NumericsVector3 start,
            Vector mouseDelta)
        {
            Viewport3D? viewport =
                FindViewport();

            if (viewport?.Camera is not PerspectiveCamera camera)
                return start;

            Vector3D look = camera.LookDirection;

            if (look.LengthSquared < 0.000001)
                return start;

            look.Normalize();

            Vector3D right =
                Vector3D.CrossProduct(
                    look,
                    new Vector3D(0, 1, 0));

            right.Y = 0;

            if (right.LengthSquared > 0.000001)
                right.Normalize();

            Vector3D forward =
                new Vector3D(
                    look.X,
                    0,
                    look.Z);

            if (forward.LengthSquared > 0.000001)
                forward.Normalize();

            double units =
                Math.Max(
                    0.0001,
                    _dragWorldPerPixel);

            Vector3D move =
                right *
                    (mouseDelta.X * units) +
                forward *
                    (-mouseDelta.Y * units);

            return new NumericsVector3(
                start.X + (float)move.X,
                start.Y,
                start.Z + (float)move.Z);
        }

        private void UpdateVisuals()
        {
            if (_disposed ||
                !_owner.IsVisible ||
                _selectedItem == null ||
                !TryGetPosition(
                    _selectedItem,
                    out NumericsVector3 position))
            {
                SetVisualsVisible(false);
                return;
            }

            double axisLength =
                GetAxisWorldLength(position);

            NumericsVector3 origin =
                GetVisualOrigin(
                    position,
                    axisLength);

            if (!TryProject(
                    origin,
                    out WpfPoint centre))
            {
                SetVisualsVisible(false);
                return;
            }

            bool canRotate =
                TryGetOrientation(
                    _selectedItem,
                    out NumericsVector3 forward,
                    out _);

            bool canScale =
                CanScaleSelectedItem();

            if (_mode == ViewportGizmoMode.Rotate && !canRotate)
                _mode = ViewportGizmoMode.Move;

            if (_mode == ViewportGizmoMode.Scale && !canScale)
                _mode = ViewportGizmoMode.Move;

            UpdateModeButtons(
                canRotate,
                canScale);

            _modeBar.Visibility = Visibility.Visible;

            bool moveMode =
                _mode == ViewportGizmoMode.Move;

            bool rotateMode =
                _mode == ViewportGizmoMode.Rotate;

            bool scaleMode =
                _mode == ViewportGizmoMode.Scale;

            SetMoveVisualsVisible(moveMode);

            if (moveMode)
            {
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

                SetAxisVisibility(_xAxis, xVisible);
                SetAxisVisibility(_yAxis, yVisible);
                SetAxisVisibility(_zAxis, zVisible);
            }

            _centreHandle.Visibility =
                moveMode || scaleMode
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            Canvas.SetLeft(
                _centreHandle,
                centre.X -
                _centreHandle.Width * 0.5);

            Canvas.SetTop(
                _centreHandle,
                centre.Y -
                _centreHandle.Height * 0.5);

            _rotationRing.Visibility =
                rotateMode
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            _rotationHitRing.Visibility =
                _rotationRing.Visibility;

            if (rotateMode)
            {
                Canvas.SetLeft(
                    _rotationRing,
                    centre.X -
                    _rotationRing.Width * 0.5);

                Canvas.SetTop(
                    _rotationRing,
                    centre.Y -
                    _rotationRing.Height * 0.5);

                Canvas.SetLeft(
                    _rotationHitRing,
                    centre.X -
                    _rotationHitRing.Width * 0.5);

                Canvas.SetTop(
                    _rotationHitRing,
                    centre.Y -
                    _rotationHitRing.Height * 0.5);
            }

            _scaleGuide.Visibility =
                scaleMode
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            _scaleGuideGlow.Visibility =
                _scaleGuide.Visibility;

            _scaleHandle.Visibility =
                _scaleGuide.Visibility;

            if (scaleMode)
            {
                const double offset = 64;

                _scaleGuide.X1 = centre.X;
                _scaleGuide.Y1 = centre.Y;
                _scaleGuide.X2 = centre.X + offset;
                _scaleGuide.Y2 = centre.Y - offset;

                _scaleGuideGlow.X1 = _scaleGuide.X1;
                _scaleGuideGlow.Y1 = _scaleGuide.Y1;
                _scaleGuideGlow.X2 = _scaleGuide.X2;
                _scaleGuideGlow.Y2 = _scaleGuide.Y2;

                Canvas.SetLeft(
                    _scaleHandle,
                    centre.X + offset -
                    _scaleHandle.Width * 0.5);

                Canvas.SetTop(
                    _scaleHandle,
                    centre.Y - offset -
                    _scaleHandle.Height * 0.5);
            }

            string modeText =
                _mode switch
                {
                    ViewportGizmoMode.Rotate =>
                        $"ROTATE Y {GetYawDegrees(forward):0.##}°",

                    ViewportGizmoMode.Scale when
                        ScaleReader?.Invoke(_selectedItem)
                            is float scale =>
                        $"SCALE {scale:0.###}x",

                    _ =>
                        "MOVE"
                };

            _coordinateReadout.Text =
                $"{modeText}   |   " +
                $"X {position.X:0.##}   " +
                $"Y {position.Y:0.##}   " +
                $"Z {position.Z:0.##}";

            Canvas.SetLeft(
                _coordinateReadout,
                centre.X + 12);

            Canvas.SetTop(
                _coordinateReadout,
                centre.Y + 12);

            _coordinateReadout.Visibility = Visibility.Visible;
            _overlay.IsHitTestVisible = IsInteractionEnabled;
        }

        private void UpdateModeButtons(
            bool canRotate,
            bool canScale)
        {
            _moveButton.IsEnabled = IsInteractionEnabled;
            _rotateButton.IsEnabled = IsInteractionEnabled && canRotate;
            _scaleButton.IsEnabled = IsInteractionEnabled && canScale;

            ApplyModeButtonState(
                _moveButton,
                _mode == ViewportGizmoMode.Move);

            ApplyModeButtonState(
                _rotateButton,
                _mode == ViewportGizmoMode.Rotate);

            ApplyModeButtonState(
                _scaleButton,
                _mode == ViewportGizmoMode.Scale);
        }

        private static void ApplyModeButtonState(
            Button button,
            bool active)
        {
            button.Opacity = active ? 1.0 : 0.72;
            button.BorderThickness =
                active
                    ? new Thickness(2)
                    : new Thickness(1);
        }

        private void SetMoveVisualsVisible(
            bool visible)
        {
            SetAxisVisibility(_xAxis, visible);
            SetAxisVisibility(_yAxis, visible);
            SetAxisVisibility(_zAxis, visible);
        }

        private static void SetAxisVisibility(
            AxisVisual axis,
            bool visible)
        {
            Visibility value =
                visible
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            axis.Underlay.Visibility = value;
            axis.Line.Visibility = value;
            axis.HitLine.Visibility = value;
            axis.Head.Visibility = value;
            axis.Label.Visibility = value;
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

            visual.Line.X1 = start.X;
            visual.Line.Y1 = start.Y;
            visual.Line.X2 = end.X;
            visual.Line.Y2 = end.Y;

            visual.Underlay.X1 = start.X;
            visual.Underlay.Y1 = start.Y;
            visual.Underlay.X2 = end.X;
            visual.Underlay.Y2 = end.Y;

            visual.HitLine.X1 = start.X;
            visual.HitLine.Y1 = start.Y;
            visual.HitLine.X2 = end.X;
            visual.HitLine.Y2 = end.Y;

            Vector direction =
                end - start;

            if (direction.Length < 2)
                return false;

            direction.Normalize();

            Vector perpendicular =
                new Vector(
                    -direction.Y,
                    direction.X);

            WpfPoint basePoint =
                end -
                direction * 13;

            visual.Head.Points =
                new PointCollection
                {
                    end,
                    basePoint +
                        perpendicular * 6,
                    basePoint -
                        perpendicular * 6
                };

            Canvas.SetLeft(
                visual.Label,
                end.X +
                perpendicular.X * 7 -
                4);

            Canvas.SetTop(
                visual.Label,
                end.Y +
                perpendicular.Y * 7 -
                7);

            return true;
        }

        private void SetVisualsVisible(
            bool visible)
        {
            SetMoveVisualsVisible(visible);

            Visibility value =
                visible
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            _centreHandle.Visibility = value;
            _coordinateReadout.Visibility = value;
            _modeBar.Visibility = value;
            _rotationRing.Visibility = Visibility.Collapsed;
            _rotationHitRing.Visibility = Visibility.Collapsed;
            _scaleGuide.Visibility = Visibility.Collapsed;
            _scaleGuideGlow.Visibility = Visibility.Collapsed;
            _scaleHandle.Visibility = Visibility.Collapsed;
        }

        private bool CanScaleSelectedItem()
        {
            return _selectedItem != null &&
                ScaleReader?.Invoke(_selectedItem)
                    is float scale &&
                float.IsFinite(scale) &&
                scale > 0;
        }

        private static double PointerAngle(
            WpfPoint point,
            WpfPoint centre)
        {
            return Math.Atan2(
                -(point.Y - centre.Y),
                point.X - centre.X);
        }

        private static double NormaliseRadians(
            double angle)
        {
            while (angle > Math.PI)
                angle -= Math.PI * 2.0;

            while (angle < -Math.PI)
                angle += Math.PI * 2.0;

            return angle;
        }

        private static double GetYawDegrees(
            NumericsVector3 forward)
        {
            if (forward.LengthSquared() < 0.000001f)
                return 0;

            return Math.Atan2(
                    forward.X,
                    forward.Z) *
                180.0 /
                Math.PI;
        }

        private bool TryProject(
            NumericsVector3 world,
            out WpfPoint point)
        {
            point = default;

            Viewport3D? viewport = FindViewport();

            if (viewport?.Camera is not PerspectiveCamera camera ||
                viewport.ActualWidth <= 1 ||
                viewport.ActualHeight <= 1)
            {
                return false;
            }

            Vector3D forward = camera.LookDirection;
            Vector3D up = camera.UpDirection;

            if (forward.LengthSquared < 0.000001 ||
                up.LengthSquared < 0.000001)
            {
                return false;
            }

            forward.Normalize();
            up.Normalize();

            Vector3D right =
                Vector3D.CrossProduct(
                    forward,
                    up);

            if (right.LengthSquared < 0.000001)
                return false;

            right.Normalize();

            up =
                Vector3D.CrossProduct(
                    right,
                    forward);

            up.Normalize();

            Vector3D relative =
                new Vector3D(
                    world.X - camera.Position.X,
                    world.Y - camera.Position.Y,
                    world.Z - camera.Position.Z);

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
                Math.Max(0.0001, aspect);

            double ndcX =
                cameraX /
                (cameraZ * tanHalfHorizontal);

            double ndcY =
                cameraY /
                (cameraZ * tanHalfVertical);

            point =
                new WpfPoint(
                    (ndcX + 1.0) *
                    0.5 *
                    viewport.ActualWidth,
                    (1.0 - ndcY) *
                    0.5 *
                    viewport.ActualHeight);

            return double.IsFinite(point.X) &&
                double.IsFinite(point.Y);
        }

        private double GetAxisWorldLength(
            NumericsVector3 position)
        {
            Viewport3D? viewport = FindViewport();

            if (viewport?.Camera is not PerspectiveCamera camera)
                return 40;

            double dx = position.X - camera.Position.X;
            double dy = position.Y - camera.Position.Y;
            double dz = position.Z - camera.Position.Z;

            double distance =
                Math.Sqrt(
                    dx * dx +
                    dy * dy +
                    dz * dz);

            return Math.Clamp(
                distance * 0.075,
                14,
                180);
        }

        private double GetWorldUnitsPerPixel(
            NumericsVector3 position)
        {
            Viewport3D? viewport = FindViewport();

            if (viewport?.Camera is not PerspectiveCamera camera ||
                viewport.ActualWidth <= 1)
            {
                return 1;
            }

            double dx = position.X - camera.Position.X;
            double dy = position.Y - camera.Position.Y;
            double dz = position.Z - camera.Position.Z;

            double distance =
                Math.Max(
                    1,
                    Math.Sqrt(
                        dx * dx +
                        dy * dy +
                        dz * dz));

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
            return position +
                new NumericsVector3(
                    0,
                    (float)(axisLength * 0.08),
                    0);
        }

        private static NumericsVector3 AxisVector(
            GizmoAxis axis)
        {
            return axis switch
            {
                GizmoAxis.X => NumericsVector3.UnitX,
                GizmoAxis.Y => NumericsVector3.UnitY,
                GizmoAxis.Z => NumericsVector3.UnitZ,
                _ => NumericsVector3.Zero
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

        private static bool TryGetOrientation(
            object item,
            out NumericsVector3 forward,
            out NumericsVector3 right)
        {
            switch (item)
            {
                case ScenarioObject obj:
                    forward = obj.Forward;
                    right = obj.Right;
                    return true;

                case ScenarioArtObject art:
                    forward = art.Forward;
                    right = art.Right;
                    return true;

                default:
                    forward = default;
                    right = default;
                    return false;
            }
        }

        private static void SetOrientation(
            object item,
            NumericsVector3 forward,
            NumericsVector3 right)
        {
            switch (item)
            {
                case ScenarioObject obj:
                    obj.Forward = forward;
                    obj.Right = right;
                    break;

                case ScenarioArtObject art:
                    art.Forward = forward;
                    art.Right = right;
                    break;
            }
        }

        private Viewport3D? FindViewport()
        {
            if (_viewport != null)
                return _viewport;

            _viewport =
                _host.Children
                    .OfType<Viewport3D>()
                    .FirstOrDefault();

            _viewport ??=
                FindVisualDescendant<Viewport3D>(
                    _owner);

            return _viewport;
        }

        private static T? FindVisualDescendant<T>(
            DependencyObject root)
            where T : DependencyObject
        {
            if (root is T match)
                return match;

            int count =
                VisualTreeHelper.GetChildrenCount(root);

            for (int i = 0; i < count; i++)
            {
                DependencyObject child =
                    VisualTreeHelper.GetChild(
                        root,
                        i);

                T? result =
                    FindVisualDescendant<T>(
                        child);

                if (result != null)
                    return result;
            }

            return null;
        }
    }
}
