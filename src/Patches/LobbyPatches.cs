using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace HandAndBrain.Patches;

/// <summary>
/// Keeps <see cref="HandAndBrainState.Lobby"/> in sync between the host and clients.
/// The host owns the settings; clients ask for them when they join and mirror every update.
/// </summary>
public static class LobbySync
{
	private sealed class Handlers
	{
		public required MessageHandlerDelegate<HandAndBrainSettingsMessage> settings;

		public required MessageHandlerDelegate<HandAndBrainSettingsRequestMessage> request;
	}

	private static readonly ConditionalWeakTable<INetGameService, Handlers> _handlers = new ConditionalWeakTable<INetGameService, Handlers>();

	public static void Register(INetGameService netService)
	{
		if (netService.Type == NetGameType.Singleplayer || _handlers.TryGetValue(netService, out _))
		{
			return;
		}
		Handlers handlers = new Handlers
		{
			settings = delegate(HandAndBrainSettingsMessage message, ulong senderId)
			{
				if (netService.Type != NetGameType.Client)
				{
					return;
				}
				HandAndBrainState.Lobby = message.ToSettings();
				HandAndBrainState.LobbyReceivedFromHost = true;
				Log.Info($"[HandAndBrain] Received lobby settings from host: {HandAndBrainState.Lobby}");
			},
			request = delegate(HandAndBrainSettingsRequestMessage _, ulong senderId)
			{
				if (netService.Type == NetGameType.Host)
				{
					netService.SendMessage(HandAndBrainSettingsMessage.From(HandAndBrainState.Lobby), senderId);
				}
			}
		};
		_handlers.Add(netService, handlers);
		netService.RegisterMessageHandler(handlers.settings);
		netService.RegisterMessageHandler(handlers.request);
		if (netService.Type == NetGameType.Client)
		{
			netService.SendMessage(default(HandAndBrainSettingsRequestMessage));
		}
	}

	public static void Unregister(INetGameService netService)
	{
		if (_handlers.TryGetValue(netService, out Handlers? handlers))
		{
			netService.UnregisterMessageHandler(handlers.settings);
			netService.UnregisterMessageHandler(handlers.request);
			_handlers.Remove(netService);
		}
	}

	/// <summary>Host only: change the settings and tell every client.</summary>
	public static void HostSet(INetGameService netService, HandAndBrainSettings settings)
	{
		if (netService.Type != NetGameType.Host)
		{
			return;
		}
		HandAndBrainState.Lobby = settings;
		Broadcast(netService);
	}

	public static void Broadcast(INetGameService netService)
	{
		if (netService.Type == NetGameType.Host)
		{
			netService.SendMessage(HandAndBrainSettingsMessage.From(HandAndBrainState.Lobby));
		}
	}
}

[HarmonyPatch(typeof(StartRunLobby), MethodType.Constructor, new[] { typeof(GameMode), typeof(INetGameService), typeof(IStartRunLobbyListener), typeof(int) })]
public static class StartRunLobbyCtorPatch
{
	public static void Postfix(StartRunLobby __instance)
	{
		HandAndBrainState.ResetLobby();
		LobbySync.Register(__instance.NetService);
	}
}

[HarmonyPatch(typeof(StartRunLobby), nameof(StartRunLobby.CleanUp))]
public static class StartRunLobbyCleanUpPatch
{
	public static void Postfix(StartRunLobby __instance)
	{
		LobbySync.Unregister(__instance.NetService);
	}
}

/// <summary>Right before the host starts the run, settle the order and send it one last time.</summary>
[HarmonyPatch(typeof(StartRunLobby), "BeginRunForAllPlayers")]
public static class StartRunLobbyBeginPatch
{
	public static void Prefix(StartRunLobby __instance)
	{
		if (__instance.NetService.Type != NetGameType.Host)
		{
			return;
		}
		HandAndBrainState.Lobby = HandAndBrainState.Lobby.NormalizedFor(__instance.Players.Select((StartRunLobbyPlayer p) => p.id));
		LobbySync.Broadcast(__instance.NetService);
	}
}

[HarmonyPatch(typeof(LoadRunLobby), MethodType.Constructor, new[] { typeof(INetGameService), typeof(ILoadRunLobbyListener), typeof(SerializableRun) })]
public static class LoadRunLobbyCtorPatch
{
	public static void Postfix(LoadRunLobby __instance)
	{
		HandAndBrainState.ResetLobby();
		if (__instance.NetService.Type == NetGameType.Host)
		{
			HandAndBrainSettings? saved = SettingsStore.Load(__instance.Run.SerializableRng.Seed, __instance.Run.Players.Select((SerializablePlayer p) => p.NetId));
			if (saved != null)
			{
				HandAndBrainState.Lobby = saved;
			}
		}
		LobbySync.Register(__instance.NetService);
	}
}

[HarmonyPatch(typeof(LoadRunLobby), nameof(LoadRunLobby.CleanUp))]
public static class LoadRunLobbyCleanUpPatch
{
	public static void Postfix(LoadRunLobby __instance)
	{
		LobbySync.Unregister(__instance.NetService);
	}
}

[HarmonyPatch(typeof(LoadRunLobby), "TryBeginRunForAllPlayers")]
public static class LoadRunLobbyBeginPatch
{
	public static void Prefix(LoadRunLobby __instance)
	{
		LobbySync.Broadcast(__instance.NetService);
	}
}

/// <summary>Turns the lobby settings into the settings of the run that is starting.</summary>
public static class RunActivation
{
	public static void Activate(RunState state, HandAndBrainSettings? settings)
	{
		List<ulong> playerIds = state.Players.Select((Player p) => p.NetId).ToList();
		HandAndBrainSettings? active = settings?.NormalizedFor(playerIds);
		HandAndBrainState.Active = (active != null && active.IsInEffect) ? active : null;
		Log.Info($"[HandAndBrain] Run started. Hand and brain: {(HandAndBrainState.Active?.ToString() ?? "off")}");
	}
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewMultiplayer))]
public static class SetUpNewMultiplayerPatch
{
	public static void Prefix(RunState state)
	{
		RunActivation.Activate(state, HandAndBrainState.Lobby);
		// Everyone keeps a copy so whoever hosts the reload can restore the pairing.
		SettingsStore.Save(state.Rng.StringSeed, state.Players.Select((Player p) => p.NetId), HandAndBrainState.Active ?? new HandAndBrainSettings());
	}

	public static void Postfix()
	{
		RunSync.Register(RunManager.Instance.NetService);
	}
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpSavedMultiplayer))]
public static class SetUpSavedMultiplayerPatch
{
	public static void Prefix(RunState state, LoadRunLobby lobby)
	{
		HandAndBrainSettings? settings = (lobby.NetService.Type == NetGameType.Host || HandAndBrainState.LobbyReceivedFromHost)
			? HandAndBrainState.Lobby
			: SettingsStore.Load(state.Rng.StringSeed, state.Players.Select((Player p) => p.NetId));
		RunActivation.Activate(state, settings);
	}

	public static void Postfix()
	{
		RunSync.Register(RunManager.Instance.NetService);
	}
}

[HarmonyPatch]
public static class SingleplayerSetUpPatch
{
	public static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.Method(typeof(RunManager), nameof(RunManager.SetUpNewSingleplayer));
		yield return AccessTools.Method(typeof(RunManager), nameof(RunManager.SetUpSavedSingleplayer));
		yield return AccessTools.Method(typeof(RunManager), nameof(RunManager.SetUpReplay));
		yield return AccessTools.Method(typeof(RunManager), nameof(RunManager.SetUpTest));
	}

	public static void Prefix()
	{
		HandAndBrainState.Active = null;
	}
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
public static class RunCleanUpPatch
{
	public static void Prefix()
	{
		RunSync.Unregister(RunManager.Instance.NetService);
	}

	public static void Postfix()
	{
		HandAndBrainState.Active = null;
	}
}
