using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

[Tool]
public partial class StartXR : Node
{
    // Signals
    [Signal]
    public delegate void XRStartedEventHandler();

    [Signal]
    public delegate void XREndedEventHandler();

    // Exported Properties
    [Export]
    public bool AutoInitialize { get; set; } = true;

    [Export]
    public float RenderTargetSizeMultiplier { get; set; } = 1.0f;

    private bool _enablePassthrough;

    [Export]
    public bool EnablePassthrough
    {
        get => _enablePassthrough;
        set => SetEnablePassthrough(value);
    }

    [Export]
    public int PhysicsRateMultiplier { get; set; } = 1;

    [Export]
    public float TargetRefreshRate { get; set; } = 0;

    // Properties
    public XRInterface xrInterface;
    public bool xrActive = false;
    public float currentRefreshRate = 0;

    public override void _Ready()
    {
        if (!Engine.IsEditorHint() && AutoInitialize)
        {
            Initialize();
        }
    }

    public bool Initialize()
    {
        xrInterface = XRServer.FindInterface("OpenXR");
        if (xrInterface is not null)
        {
            return OpenXRSetup();
        }

        xrInterface = XRServer.FindInterface("WebXR");
        if (xrInterface is not null)
        {
            return WebXRSetup();
        }

        xrInterface = null;
        GD.Print("No XR interface detected");
        return false;
    }

    public override string[] _GetConfigurationWarnings()
    {
        var warnings = new List<string>();

        if (PhysicsRateMultiplier < 1)
        {
            warnings.Add("Physics rate multiplier should be at least 1x the HMD rate");
        }

        return warnings.ToArray();
    }
    

    private void SetEnablePassthrough(bool newValue)
    {
        _enablePassthrough = newValue;

        if (xrInterface != null)
        {
            if (_enablePassthrough)
            {
                _enablePassthrough = xrInterface.StartPassthrough();
            }
            else
            {
                xrInterface.StopPassthrough();
            }
        }
    }









    public bool OpenXRSetup()
    {
        GD.Print("OpenXR: Configuring interface");

        if (xrInterface.Get("render_target_size_multiplier").VariantType is not Variant.Type.Nil)
        {
            xrInterface.Set("render_target_size_multiplier", RenderTargetSizeMultiplier);
        }

        if (!xrInterface.IsInitialized())
        {
            GD.Print("OpenXR: Initializing interface");
            if (!xrInterface.Initialize())
            {
                GD.PushError("OpenXR: Failed to initialize");
                return false;
            }
        }

        xrInterface.Connect("session_begun", Callable.From(OnOpenXRSessionBegun));
        xrInterface.Connect("session_visible", Callable.From(OnOpenXRVisibleState));
        xrInterface.Connect("session_focussed", Callable.From(OnOpenXRFocusedState));

        if (EnablePassthrough && xrInterface.IsPassthroughSupported())
        {
            EnablePassthrough = xrInterface.StartPassthrough();
        }

        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        GetViewport().UseXR = true;

        return true;
    }

    private void OnOpenXRSessionBegun()
    {
        GD.Print("OpenXR: Session begun");

        currentRefreshRate = xrInterface.Get("get_display_refresh_rate").As<float>();
        GD.Print(currentRefreshRate > 0
            ? $"OpenXR: Refresh rate reported as {currentRefreshRate}"
            : "OpenXR: No refresh rate given by XR runtime");

        var desiredRate = TargetRefreshRate > 0 ? TargetRefreshRate : currentRefreshRate;
        var availableRates = xrInterface.Get("get_available_display_refresh_rates").AsFloat32Array();

        if (availableRates.Count() == 0)
        {
            GD.Print("OpenXR: Target does not support refresh rate extension");
        }
        else if (availableRates.Count() == 1)
        {
            GD.Print("OpenXR: Target supports only one refresh rate");
        }
        else if (desiredRate > 0)
        {
            GD.Print("OpenXR: Available refresh rates are ", string.Join(", ", availableRates));
            var rate = availableRates.FindClosest(desiredRate);
            if (rate > 0)
            {
                GD.Print($"OpenXR: Setting refresh rate to {rate}");
                xrInterface.Set("set_display_refresh_rate", rate);
                currentRefreshRate = rate;
            }
        }

        var activeRate = currentRefreshRate > 0 ? currentRefreshRate : 144.0f;
        var physicsRate = Mathf.RoundToInt(activeRate * PhysicsRateMultiplier);
        GD.Print($"Setting physics rate to {physicsRate}");
        Engine.PhysicsTicksPerSecond = physicsRate;
    }

    private void OnOpenXRVisibleState()
    {
        if (xrActive)
        {
            GD.Print("OpenXR: XR ended (visible_state)");
            xrActive = false;
            EmitSignal(SignalName.XRStarted);
        }
    }

    private void OnOpenXRFocusedState()
    {
        if (!xrActive)
        {
            GD.Print("OpenXR: XR started (focused_state)");
            xrActive = true;
            EmitSignal(SignalName.XREnded);
        }
    }









    public bool WebXRSetup()
    {
        GD.Print("WebXR: Configuring interface");

        xrInterface.Connect("session_supported", new Callable(this, "OnWebXRSessionSupported"));
        xrInterface.Connect("session_started", Callable.From(OnWebXRSessionStarted));
        xrInterface.Connect("session_ended", Callable.From(OnWebXRSessionEnded));
        xrInterface.Connect("session_failed", new Callable(this, "OnWebXRSessionFailed"));

        Engine.PhysicsTicksPerSecond = 144;

        if (GetViewport().UseXR)
        {
            return true;
        }

        // Raises the OnSessionSupported event
        xrInterface.Call("is_session_supported", "immersive-vr");
        return true;
    }

    public void OnWebXRSessionSupported(string sessionMode, bool supported)
    {
        if (sessionMode == "immersive-vr")
        {
            if (supported)
            {
                GetNode<Control>("EnterWebXR").Visible = true;
            }
            else
            {
                OS.Alert("Your web browser doesn't support VR. Sorry!");
            }
        }
    }

    private void OnWebXRSessionStarted()
    {
        GD.Print("WebXR: Session started");

        GetNode<Control>("EnterWebXR").Visible = false;
        GetViewport().UseXR = true;

        xrActive = true;
        EmitSignal(SignalName.XRStarted);
    }

    private void OnWebXRSessionEnded()
    {
        GD.Print("WebXR: Session ended");

        GetNode<Control>("EnterWebXR").Visible = true;
        GetViewport().UseXR = false;

        xrActive = false;
        EmitSignal(SignalName.XREnded);
    }

    private void OnWebXRSessionFailed(string message)
    {
        OS.Alert("Unable to enter VR: " + message);
        GetNode<Control>("EnterWebXR").Visible = true;
    }

    private void OnWebXRButtonPressed()
    {
        // Configure the WebXR interface
        xrInterface.Set("session_mode", "immersive-vr");
        xrInterface.Set("requested_reference_space_types", "bounded-floor, local-floor, local");
        xrInterface.Set("required_features", "local-floor");
        xrInterface.Set("optional_features", "bounded-floor");

        // Initialize the interface. This should trigger either _on_webxr_session_started
        // or _on_webxr_session_failed
        if (!xrInterface.Initialize())
            OS.Alert("Failed to initialize WebXR");
    }
	
	
}



static public class ListUtils
{
    static public float FindClosest(this IList<float> values, float target)
    {
        if (values.Count == 0)
        {
            return 0.0f;
        }

        float best = values[0];
        foreach (var value in values)
        {
            if (Math.Abs(target - value) < Math.Abs(target - best))
            {
                best = value;
            }
        }

        return best;
    }
}