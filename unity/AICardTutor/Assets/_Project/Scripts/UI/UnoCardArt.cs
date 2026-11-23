using System.Collections.Generic;
using UnityEngine;

public static class UnoCardArt
{
    static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

    public static Sprite Get(string color, string value)
    {
        string key = UnoCardKey.For(color, value);
        if (key == null) return null;
        if (!Cache.TryGetValue(key, out Sprite sprite))
        {
            sprite = Resources.Load<Sprite>("UnoCards/" + key);
            // Also works before the editor import rule has processed a newly copied texture.
            if (sprite == null)
            {
                var texture = Resources.Load<Texture2D>("UnoCards/" + key);
                if (texture != null)
                    sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            }
            Cache[key] = sprite;
        }
        return sprite;
    }
}
