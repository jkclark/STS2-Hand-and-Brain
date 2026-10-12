using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;

namespace HandAndBrain.Patches;

/// <summary>
/// Testing aid: launched with <c>--hab-enemies-1hp</c>, every enemy spawns with 1 HP so fights end
/// in one hit. Combat is simulated on every peer, so every game in the lobby must be launched with
/// the flag, or the peers will disagree about enemy HP and desync.
/// </summary>
[HarmonyPatch(typeof(CombatState), nameof(CombatState.CreateCreature))]
public static class OneHpEnemiesPatch
{
	public const string Flag = "hab-enemies-1hp";

	private static readonly bool _enabled = CommandLineHelper.HasArg(Flag);

	private static bool _logged;

	public static void Postfix(Creature __result, CombatSide side)
	{
		if (!_enabled || side != CombatSide.Enemy)
		{
			return;
		}
		if (!_logged)
		{
			_logged = true;
			Log.Warn($"[HandAndBrain] --{Flag} is set: enemies spawn with 1 HP");
		}
		__result.SetMaxHpInternal(1m);
		__result.SetCurrentHpInternal(1m);
	}
}
