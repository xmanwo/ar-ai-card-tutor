using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One quarter of the Wild colour wheel.
///
/// Why this exists: the four slices share the same bounding box, because they are four 90 degree
/// arcs of the same circle. The hand HUD's usual hit test asks "is the point inside this
/// RectTransform?", which would answer yes for all four and always pick the first. This component
/// answers with an angular test instead: convert the hit point to local space, take its angle, and
/// check whether it falls inside this slice's arc.
///
/// The sprite is generated at runtime. A Sprite built from a Texture2D cannot be written into a
/// prefab, so the prefab stores the slice with no sprite and Awake fills it in.
/// </summary>
[RequireComponent(typeof(Image))]
public class WildColorSector : MonoBehaviour
{
    [Tooltip("Colour name passed to ScanPanel.SelectWildColor when this slice is chosen.")]
    public string color = "Red";

    [Tooltip("Start of the arc in degrees, clockwise from +X (three o'clock).")]
    [Range(0f, 360f)] public float startAngle;

    [Tooltip("Arc width. A quarter wheel uses 90.")]
    public float spanDegrees = 90f;

    static Sprite discSprite;

    /// <summary>A white disc shared by every slice, generated once.</summary>
    public static Sprite Disc
    {
        get
        {
            if (discSprite == null)
                discSprite = MakeDisc(128);

            return discSprite;
        }
    }

    void Awake()
    {
        var image = GetComponent<Image>();

        if (image != null && image.sprite == null)
            image.sprite = Disc;
    }

    /// <summary>
    /// True when the ray lands inside this slice's arc.
    /// </summary>
    /// <param name="maxDistance">Current nearest hit. Anything further is ignored.</param>
    /// <param name="worldPoint">Hit point on the wheel plane, for the HUD cursor.</param>
    public bool TryHit(Vector3 origin, Vector3 direction, float maxDistance, out Vector3 worldPoint)
    {
        worldPoint = default;

        var rect = (RectTransform)transform;
        Vector3 normal = rect.forward;
        float denominator = Vector3.Dot(direction, normal);

        if (Mathf.Abs(denominator) < 1e-4f)
            return false;

        float distance = Vector3.Dot(rect.position - origin, normal) / denominator;

        if (distance <= 0f || distance > maxDistance)
            return false;

        Vector3 point = origin + direction * distance;
        Vector2 local = rect.InverseTransformPoint(point);

        // Outside the disc itself, in a corner of the square bounding box.
        float radius = rect.rect.width * 0.5f;

        if (local.sqrMagnitude > radius * radius)
            return false;

        float angle = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;

        if (angle < 0f)
            angle += 360f;

        float delta = Mathf.Repeat(angle - startAngle, 360f);

        if (delta > spanDegrees)
            return false;

        worldPoint = point;
        return true;
    }

    static Sprite MakeDisc(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var pixels = new Color32[size * size];
        float centre = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - centre;
                float dy = y - centre;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                byte alpha = (byte)(Mathf.Clamp01(centre - distance) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
