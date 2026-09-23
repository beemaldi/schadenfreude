using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace BadLuck;

/// <summary>
/// For a while, creatures notice the player from further away (the "animalSeekingRange" game stat).
/// Two mechanics use this: the fart and the coughing fit.
/// Each brings its own key so they cannot cancel each other out.
/// </summary>
public class SeekRangeBoost
{
    const string SeekStat = "animalSeekingRange";

    class Boost
    {
        public long UntilMs;
        public float Bonus;
    }

    readonly string statKey;
    readonly Dictionary<string, Boost> active = new();

    public SeekRangeBoost(string statKey)
    {
        this.statKey = statKey;
    }

    public void Clear() => active.Clear();

    /// <summary>Set a bonus; if one is already running, the longer and the larger one wins</summary>
    public void Apply(EntityPlayer eplr, float bonus, double seconds)
    {
        if (eplr == null || bonus <= 0 || seconds <= 0) return;

        long until = eplr.World.ElapsedMilliseconds + (long)(seconds * 1000);
        if (active.TryGetValue(eplr.PlayerUID, out Boost boost))
        {
            boost.UntilMs = System.Math.Max(boost.UntilMs, until);
            boost.Bonus = System.Math.Max(boost.Bonus, bonus);
        }
        else
        {
            active[eplr.PlayerUID] = boost = new Boost { UntilMs = until, Bonus = bonus };
        }
        eplr.Stats.Set(SeekStat, statKey, boost.Bonus, false);
    }

    /// <summary>Take expired bonuses off again</summary>
    public void RemoveExpired(ICoreServerAPI sapi)
    {
        long now = sapi.World.ElapsedMilliseconds;
        foreach (string uid in active.Keys.ToArray())
        {
            if (now < active[uid].UntilMs) continue;
            sapi.World.PlayerByUid(uid)?.Entity?.Stats.Remove(SeekStat, statKey);
            active.Remove(uid);
        }
    }

    public void Remove(IServerPlayer player)
    {
        if (active.Remove(player.PlayerUID)) player.Entity?.Stats.Remove(SeekStat, statKey);
    }
}
