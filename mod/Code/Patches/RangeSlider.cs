using System;
using Godot;

namespace AutoAnthonyRelics.Patches;

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
    private Label _lowLabel = null!;
    private Label _highLabel = null!;

    private int _min;
    private int _max = 100;
    private int _low;
    private int _high = 100;
    private bool _draggingLow;
    private bool _draggingHigh;

    /// <summary>Emitted after either handle moves, with the ordered value pair.</summary>
    public event Action<int, int>? RangeChanged;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(320f, 40f);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = false;

        _track = new ColorRect
        {
            Color = new Color(0.22f, 0.22f, 0.26f, 1f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_track);

        _filled = new ColorRect
        {
            Color = new Color(0.36f, 0.62f, 0.86f, 1f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_filled);

        _lowHandle = MakeHandle(new Color(0.92f, 0.92f, 0.96f, 1f));
        _highHandle = MakeHandle(new Color(0.92f, 0.92f, 0.96f, 1f));

        _lowLabel = MakeValueLabel(HorizontalAlignment.Left);
        _highLabel = MakeValueLabel(HorizontalAlignment.Right);

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

    private Label MakeValueLabel(HorizontalAlignment alignment)
    {
        var label = new Label
        {
            HorizontalAlignment = alignment,
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(64f, 0f),
        };
        label.AddThemeFontSizeOverride("font_size", 14);
        AddChild(label);
        return label;
    }

    /// <summary>Configure the inclusive band. Values are clamped into it.</summary>
    public void Configure(int min, int max, int low, int high)
    {
        _min = min;
        _max = Math.Max(min + 1, max);
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
        _low = Math.Clamp(low, _min, _max);
        _high = Math.Clamp(high, _min, _max);
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
        TrackLeft + TrackWidth * ((float)(value - _min) / (_max - _min));

    private int XToValue(float x)
    {
        float ratio = (x - TrackLeft) / TrackWidth;
        return Math.Clamp(_min + (int)MathF.Round(ratio * (_max - _min)), _min, _max);
    }

    private void Layout()
    {
        if (_track is null)
        {
            return; // called before _Ready built the children
        }
        float y = Size.Y * 0.5f;
        float lowX = ValueToX(_low);
        float highX = ValueToX(_high);

        _track.Position = new Vector2(TrackLeft, y - TrackHeight * 0.5f);
        _track.Size = new Vector2(TrackWidth, TrackHeight);

        _filled.Position = new Vector2(lowX, y - TrackHeight * 0.5f);
        _filled.Size = new Vector2(Math.Max(0f, highX - lowX), TrackHeight);

        _lowHandle.Position = new Vector2(lowX - HandleWidth * 0.5f, y - HandleHeight * 0.5f);
        _highHandle.Position = new Vector2(highX - HandleWidth * 0.5f, y - HandleHeight * 0.5f);

        _lowLabel.Text = _low.ToString();
        _highLabel.Text = _high.ToString();
        _lowLabel.Position = new Vector2(0f, y + HandleHeight * 0.5f - 2f);
        _highLabel.Position = new Vector2(Math.Max(0f, Size.X - 64f), y + HandleHeight * 0.5f - 2f);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            Layout();
        }
    }

    public override void _GuiInput(InputEvent inputEvent)
    {
        switch (inputEvent)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                if (button.Pressed)
                {
                    float x = button.Position.X;
                    // Grab whichever handle is nearer to the click; ties go to
                    // the low handle so the pair can always be separated.
                    bool low = MathF.Abs(x - ValueToX(_low)) <= MathF.Abs(x - ValueToX(_high));
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
