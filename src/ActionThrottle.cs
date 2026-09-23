using System.Collections.Generic;
using Vintagestory.API.Common;

namespace BadLuck;

/// <summary>
/// "Roll only once per action". One action often raises several events: felling a tree wears the axe
/// down once per trunk block, and a broken block reports tool wear on top of that.
/// Every mechanic keeps its own throttle.
/// </summary>
public class ActionThrottle
{
    const long SameActionMs = 100;

    readonly Dictionary<string, long> lastMs = new();

    /// <summary>true = this is a new action, rolling is allowed</summary>
    public bool IsNewAction(IPlayer player, long now)
    {
        if (player?.PlayerUID == null) return false;
        if (lastMs.TryGetValue(player.PlayerUID, out long last) && now - last < SameActionMs) return false;

        lastMs[player.PlayerUID] = now;
        return true;
    }

    public void Forget(IPlayer player) => lastMs.Remove(player.PlayerUID);

    public void Clear() => lastMs.Clear();
}
