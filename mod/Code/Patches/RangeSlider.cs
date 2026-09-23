using System;
using Godot;

namespace QuriousCraftingRelics.Patches;

/// <summary>
/// A single-track, double-ended range slider: one line, two draggable handles.
/// This is what the budget editor needs (user order 2026-09-11: "一条线段两端
/// 滑块"), replacing the two independent <c>NSlider</c> widgets that could not
/// express a range at all.
///
/// Why not the engine's NSlider: NSlider._Ready does
/// <c>GetNode&lt;Control&gt;("%Handle")</c> and then dereferences <c>_handle</c>
/// unconditionally from _Process and UpdateHandlePosition. A bare
/// <c>new NSlider { .. }</c> has no Handle child, so _Ready throws and every
/// frame after it null-references - the two-slider editor built that way could
/// not have rendered. It is also single-ended by construction
/// (SetValueBasedOnMousePosition maps one mouse X onto one value), so it cannot
/// be made into a range control without rewriting it anyway.
///
/// This control is self-contained: a background track plus two handles, with
/// drag handling in _GuiInput. Handles cannot cross - dragging one past the
/// other clamps it to the other's value, and the value pair is always emitted
/// ordered (Low &lt;= High).
/// </summary>
internal sealed partial class RangeSlider : Control
{
    private const float HandleWidth = 16f;
    private const float HandleHeight = 26f;
    private const float TrackHeight = 6f;

    private ColorRect _track = null!;
    private ColorRect _filled = null!;
    private ColorRect _lowHandle = null!;
    private ColorRect _highHandle = null!;
    private SpinBox _lowInput = null!;
    private SpinBox _highInput = null!;

    private int _min;
    private int _max = 100;
    private int _low;
    private int _high = 100;
    private bool _draggingLow;
    private bool _draggingHigh;
    private bool _keyboardHigh;
    private bool _configured;

    /// <summary>Emitted after either handle moves, with the ordered value pair.</summary>
    public event Action<int, int>? RangeChanged;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(180f, 82f);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = false;
        FocusMode = FocusModeEnum.All;

        _track = new ColorRect
        {
            Color = new Color(0.22f, 0.22f, 0.26f, 1f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_track);

        _filled = new ColorRect
        {
            Color = QuriousSettingsStyle.Gold,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_filled);

        _lowHandle = MakeHandle(new Color(0.92f, 0.92f, 0.96f, 1f));
        _highHandle = MakeHandle(new Color(0.92f, 0.92f, 0.96f, 1f));

        _lowInput = MakeNumber();
        _highInput = MakeNumber();
        _lowInput.TooltipText = QuriousSettingsStyle.Loc("UI_MIN");
        _highInput.TooltipText = QuriousSettingsStyle.Loc("UI_MAX");
        _lowInput.ValueChanged += value => EditNumber(true, (int)value);
        _highInput.ValueChanged += value => EditNumber(false, (int)value);

        Layout();
    }

    private ColorRect MakeHandle(Color color)
    {
        var handle = new ColorRect
        {
            Color = color,
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(HandleWidth, HandleHeight),
        };
        AddChild(handle);
        return handle;
    }

    private SpinBox MakeNumber()
    {
        var input = new SpinBox { MinValue = int.MinValue, MaxValue = int.MaxValue,
            Step = 1, CustomMinimumSize = new Vector2(80, 36) };
        AddChild(input);
        return input;
    }

    private bool _editable = true;
    public bool Editable
    {
        get => _editable;
        set
        {
            _editable = value;
            if (_lowInput is not null) _lowInput.Editable = value;
            if (_highInput is not null) _highInput.Editable = value;
            Modulate = value ? Colors.White : new Color(0.6f, 0.6f, 0.6f);
        }
    }

    private void EditNumber(bool low, int value)
    {
        if (!Editable) return;
        int nextLow = low ? Math.Min(value, _high) : _low;
        int nextHigh = low ? _high : Math.Max(value, _low);
        bool changed = nextLow != _low || nextHigh != _high;
        SetValues(nextLow, nextHigh);
        if (changed) RangeChanged?.Invoke(_low, _high);
    }
    /// <summary>Set a drag domain that expands to retain all configured values.</summary>
    public void Configure(int min, int max, int low, int high)
    {
        if (!_configured)
        {
            _min = min;
            _max = Math.Max(min, max);
            _configured = true;
        }
        // Do not rescale the track on each ConfigChanged emitted while dragging.
        SetValues(low, high);
    }

    public int Low => _low;
    public int High => _high;

    /// <summary>Set both handles without emitting RangeChanged.</summary>
    public void SetValuesSilently(int low, int high) => SetValues(low, high);

    private void SetValues(int low, int high)
    {
        if (low > high)
        {
            (low, high) = (high, low);
        }
        _low = low;
        _high = high;
        _min = Math.Min(_min, low);
        _max = Math.Max(_max, high);
        Layout();
    }

    /// <summary>
    /// Push the values out. Called by the panel after a programmatic set, so
    /// the config write path stays in one place.
    /// </summary>
    public void Emit() => RangeChanged?.Invoke(_low, _high);

    private float TrackLeft => HandleWidth * 0.5f;
    private float TrackWidth => Math.Max(1f, Size.X - HandleWidth);

    private float ValueToX(int value) =>
        TrackLeft + TrackWidth * (float)(((double)value - _min) / Math.Max(1d, (double)_max - _min));

    private int XToValue(float x)
    {
        float ratio = (x - TrackLeft) / TrackWidth;
        return (int)Math.Clamp(Math.Round(_min + (double)ratio * ((double)_max - _min)), _min, _max);
    }

    private void Layout()
    {
        if (_track is null || _lowInput is null || _highInput is null)
        {
            return; // called before _Ready built the children
        }
        float y = 18f;
        float lowX = ValueToX(_low);
        float highX = ValueToX(_high);

        _track.Position = new Vector2(TrackLeft, y - TrackHeight * 0.5f);
        _track.Size = new Vector2(TrackWidth, TrackHeight);

        _filled.Position = new Vector2(lowX, y - TrackHeight * 0.5f);
        _filled.Size = new Vector2(Math.Max(0f, highX - lowX), TrackHeight);

        _lowHandle.Position = new Vector2(lowX - HandleWidth * 0.5f, y - HandleHeight * 0.5f);
        _highHandle.Position = new Vector2(highX - HandleWidth * 0.5f, y - HandleHeight * 0.5f);

        _lowInput.SetValueNoSignal(_low);
        _highInput.SetValueNoSignal(_high);
        float width = Math.Max(80, (Size.X - 12) * 0.5f);
        _lowInput.Position = new Vector2(0, 40);
        _highInput.Position = new Vector2(width + 12, 40);
        _lowInput.Size = _highInput.Size = new Vector2(width, 36);
        Editable = _editable;
    }

    public override void _Draw()
    {
        if (HasFocus()) DrawRect(new Rect2(Vector2.Zero, new Vector2(Size.X, 36)), QuriousSettingsStyle.Gold, false, 2);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationFocusEnter || what == NotificationFocusExit) QueueRedraw();
        if (what == NotificationResized)
        {
            Layout();
        }
    }

    public override void _GuiInput(InputEvent inputEvent)
    {
        if (!Editable) return;
        switch (inputEvent)
        {
            case InputEventKey { Pressed: true } key:
                if (key.Keycode == Key.Up || key.Keycode == Key.Down)
                {
                    _keyboardHigh = key.Keycode == Key.Up;
                    AcceptEvent();
                }
                else if (key.Keycode == Key.Left || key.Keycode == Key.Right)
                {
                    long current = _keyboardHigh ? _high : _low;
                    int next = (int)Math.Clamp(current + (key.Keycode == Key.Right ? 1 : -1), int.MinValue, int.MaxValue);
                    EditNumber(!_keyboardHigh, next);
                    AcceptEvent();
                }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed)
                {
                    GrabFocus();
                    float x = button.Position.X;
                    // Grab the nearest handle; coincident handles separate by click side.
                    bool low = _low == _high ? x < ValueToX(_low)
                        : MathF.Abs(x - ValueToX(_low)) <= MathF.Abs(x - ValueToX(_high));
                    _keyboardHigh = !low;
                    _draggingLow = low;
                    _draggingHigh = !low;
                    ApplyDrag(x);
                    AcceptEvent();
                }
                else
                {
                    _draggingLow = false;
                    _draggingHigh = false;
                    AcceptEvent();
                }
                break;
            case InputEventMouseMotion motion when _draggingLow || _draggingHigh:
                ApplyDrag(motion.Position.X);
                AcceptEvent();
                break;
        }
    }

    private void ApplyDrag(float x)
    {
        int value = XToValue(x);
        bool changed;
        if (_draggingLow)
        {
            int next = Math.Min(value, _high);
            changed = next != _low;
            _low = next;
        }
        else if (_draggingHigh)
        {
            int next = Math.Max(value, _low);
            changed = next != _high;
            _high = next;
        }
        else
        {
            return;
        }
        Layout();
        if (changed)
        {
            RangeChanged?.Invoke(_low, _high);
        }
    }
}
