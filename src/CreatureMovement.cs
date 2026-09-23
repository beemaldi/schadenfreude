using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace Schadenfreude;

/// <summary>
/// How fast a creature moves with purpose, and which animation it uses: taken from its own
/// seek-player task, otherwise from wandering (a bit brisker). Cached per entity type.
/// </summary>
public static class CreatureMovement
{
    public class Style
    {
        public float MoveSpeed = 0.03f;
        public string Animation;
        public float AnimationSpeed = 1f;
    }

    static readonly Dictionary<string, Style> cache = new();

    public static void Reset() => cache.Clear();

    public static Style Of(EntityProperties type)
    {
        string key = type.Code.ToString();
        if (cache.TryGetValue(key, out Style cached)) return cached;

        JsonObject seek = null, wander = null;
        foreach (JsonObject behavior in type.Server?.BehaviorsAsJsonObj ?? [])
        {
            if (behavior["code"].AsString() != "taskai") continue;
            foreach (JsonObject task in behavior["aitasks"].AsArray() ?? [])
            {
                if (!task["enabled"].AsBool(true)) continue;
                string code = task["code"].AsString()?.ToLowerInvariant();
                if (code is "seekentity" or "seekentity-r" && seek == null && TargetsPlayer(task["entityCodes"])) seek = task;
                if (code is "wander" or "wander-r" && wander == null) wander = task;
            }
        }

        var style = new Style();
        if (seek != null)
        {
            style.MoveSpeed = seek["movespeed"].AsFloat(style.MoveSpeed);
            style.Animation = seek["animation"].AsString()?.ToLowerInvariant();
            style.AnimationSpeed = seek["animationSpeed"].AsFloat(1f);
        }
        else if (wander != null)
        {
            style.MoveSpeed = wander["movespeed"].AsFloat(0.015f) * 2;
            style.Animation = wander["animation"].AsString()?.ToLowerInvariant();
            style.AnimationSpeed = wander["animationSpeed"].AsFloat(1f) * 1.5f;
        }

        cache[key] = style;
        return style;
    }

    public static bool TargetsPlayer(JsonObject entityCodes)
    {
        if (!entityCodes.Exists) return false;
        foreach (string code in entityCodes.AsArray<string>() ?? [])
        {
            if (code != null && WildcardUtil.Match(code, "player")) return true;
        }
        return false;
    }
}
