using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Routes the controller's primary button (A) into <see cref="CardScanClient.Rescan"/>.
///
/// FrameCapture used to poll that button itself and capture straight away, which meant the button
/// bypassed the state machine: no rescan cooldown, no track reset, and no "the round found
/// nothing, keep the old result" behaviour. One button, one entry point - the hand HUD calls the
/// same method.
/// </summary>
public class ScanInputRouter : MonoBehaviour
{
    [Tooltip("Leave empty to find the CardScanClient automatically.")]
    public CardScanClient client;

    [Tooltip("Read A on the right controller, falling back to the left one.")]
    public bool readPrimaryButton = true;

    bool wasPressed;

    void Awake()
    {
        if (client == null)
            client = FindFirstObjectByType<CardScanClient>();
    }

    void Update()
    {
        if (!readPrimaryButton || client == null)
            return;

        if (!TryReadPrimaryButton(out bool pressed))
            return;

        // Edge only: the OpenXR button read stays true for several frames per press.
        if (pressed && !wasPressed)
            client.Rescan();

        wasPressed = pressed;
    }

    static bool TryReadPrimaryButton(out bool pressed)
    {
        pressed = false;

        var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        if (!device.isValid)
            device = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

        if (!device.isValid)
            return false;

        return device.TryGetFeatureValue(CommonUsages.primaryButton, out pressed);
    }
}
