using System;

namespace BadLuck;

/// <summary>
/// Safety wrapper around the hooks into game code. A bug in this mod must never break doors,
/// crafting or knapping for everyone - not for other mods either.
/// Meant for rare events only; in per-tick postfixes building the lambda would be a waste.
/// </summary>
public static class Guard
{
    public static void Run(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            BadLuckModSystem.Logger?.Error("badluck: {0} failed, ignoring: {1}", what, e);
        }
    }

    /// <summary>For cancelling prefixes: on an error the game code carries on as usual</summary>
    public static bool Run(string what, Func<bool> action, bool onError = true)
    {
        try
        {
            return action();
        }
        catch (Exception e)
        {
            BadLuckModSystem.Logger?.Error("badluck: {0} failed, letting the game continue: {1}", what, e);
            return onError;
        }
    }
}
