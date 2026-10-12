using HandAndBrain.Patches;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace HandAndBrain;

[ModInitializer(nameof(Init))]
public static class ModEntry
{
	public const string Id = "HandAndBrain";

	public static void Init()
	{
		new Harmony("jclark.HandAndBrain").PatchAll(typeof(ModEntry).Assembly);
		RewardScreenDecorations.RegisterInputActions();
		Log.Info("[HandAndBrain] Loaded");
	}
}
