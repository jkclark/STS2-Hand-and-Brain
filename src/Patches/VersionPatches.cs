using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace HandAndBrain.Patches;

/// <summary>
/// The game refuses to connect peers whose gameplay mods differ, comparing "id-version" strings.
/// Every build of this mod would report the same manifest version, so two different builds could
/// play together and desync. Appending a fingerprint of the compiled assembly makes the game's own
/// handshake reject mismatched builds ("HandAndBrain-0.3.0+1a2b3c4d").
/// </summary>
[HarmonyPatch(typeof(ModManager), nameof(ModManager.GetGameplayRelevantModNameList))]
public static class GameplayModListPatch
{
	/// <summary>Changes whenever the compiled code changes (builds are deterministic).</summary>
	public static readonly string BuildId = typeof(ModEntry).Assembly.ManifestModule.ModuleVersionId.ToString("N").Substring(0, 8);

	public static void Postfix(List<string>? __result)
	{
		if (__result == null)
		{
			return;
		}
		string prefix = ModEntry.Id + "-";
		for (int i = 0; i < __result.Count; i++)
		{
			if (__result[i].StartsWith(prefix) && !__result[i].Contains('+'))
			{
				__result[i] += "+" + BuildId;
			}
		}
	}
}
