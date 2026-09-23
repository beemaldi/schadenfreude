using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace Schadenfreude;

/// <summary>
/// Code patterns from the config file (e.g. "game:vegetable-*"), parsed once and cached.
/// Unusable entries are reported once and ignored afterwards, so that a typo in the config file
/// does not disturb the server.
/// </summary>
public static class CodePatterns
{
    // The key is the array instance from the config - cleared whenever the config is reloaded
    static readonly Dictionary<string[], AssetLocation[]> cache = new();

    public static void Clear()
    {
        lock (cache) cache.Clear();
    }

    public static AssetLocation[] Compile(string[] patterns, ILogger logger = null)
    {
        if (patterns == null) return [];

        lock (cache)
        {
            if (cache.TryGetValue(patterns, out AssetLocation[] compiled)) return compiled;

            var list = new List<AssetLocation>(patterns.Length);
            foreach (string pattern in patterns)
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;
                try
                {
                    list.Add(new AssetLocation(pattern.Trim()));
                }
                catch (System.Exception e)
                {
                    logger?.Warning("[schadenfreude] Unusable pattern in the config file: '{0}' ({1})", pattern, e.Message);
                }
            }

            compiled = list.ToArray();
            cache[patterns] = compiled;
            return compiled;
        }
    }

    public static bool Matches(string[] patterns, AssetLocation code, ILogger logger = null)
    {
        if (code == null) return false;
        foreach (AssetLocation pattern in Compile(patterns, logger))
        {
            if (WildcardUtil.Match(pattern, code)) return true;
        }
        return false;
    }
}
