using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Routes the controller's primary button (A) into <see cref="CardScanClient.Rescan"/>, and the
/// secondary button (B) into a transport cycle on <see cref="HttpCardScanSource"/>.
///
/// FrameCapture used to poll the button itself and capture straight away, which meant the button
/// bypassed the state machine: no rescan cooldown, no track reset, and no "the round found
/// nothing, keep the old result" behaviour. One button, one entry point - the hand HUD calls the
/// same method.
///
/// The B shortcut exists because the transport must stay switchable after the APK is built: an
/// on-site network that turns out to be isolated, or a USB cable that turns out to be charge-only,
/// is not something worth a rebuild.
/// </summary>
public class ScanInputRouter : MonoBehaviour
{
    [Tooltip("Leave empty to find the CardScanClient automatically.")]
    public CardScanClient client;

    [Tooltip("Read A on the right controller, falling back to the left one.")]
    public bool readPrimaryButton = true;

    [Tooltip("Read B to cycle Auto -> Usb -> Lan -> Auto. Switchable on site, no rebuild.")]
    public bool readSwitchButton = true;

    bool wasPressed;
    bool wasSwitched;

    HttpCardScanSource source;

    void Awake()
    {
        if (client == null)
            client = FindFirstObjectByType<CardScanClient>();

        if (client != null)
            source = client.GetComponent<HttpCardScanSource>();

        if (source == null)
            source = FindFirstObjectByType<HttpCardScanSource>();
    }

    void Update()
    {
        if (!readPrimaryButton && !readSwitchButton)
            return;

        if (!TryReadController(out var device))
            return;

        if (readPrimaryButton && client != null &&
            device.TryGetFeatureValue(CommonUsages.primaryButton, out bool pressed))
        {
            // Edge only: the OpenXR button read stays true for several frames per press.
            if (pressed && !wasPressed)
                client.Rescan();

            wasPressed = pressed;
        }

        if (readSwitchButton &&
            device.TryGetFeatureValue(CommonUsages.secondaryButton, out bool switched))
        {
            if (switched && !wasSwitched)
                CycleTransport();

            wasSwitched = switched;
        }
    }

    void CycleTransport()
    {
        if (source == null)
        {
            Debug.LogWarning("[Scan] B pressed, but no HttpCardScanSource is wired to switch.");
            return;
        }

        ServerTransport next;

        if (source.Transport == ServerTransport.Auto)
            next = ServerTransport.Usb;
        else if (source.Transport == ServerTransport.Usb)
            next = ServerTransport.Lan;
        else
            next = ServerTransport.Auto;

        source.SetTransport(next);

        // A switch should show up at once, not on the next manual press. Rescan swallows the
        // request while a round is running, which is exactly what should happen mid-scan.
        if (client != null)
            client.Rescan();
    }

    static bool TryReadController(out InputDevice device)
    {
        device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        if (!device.isValid)
            device = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

        return device.isValid;
    }
}
