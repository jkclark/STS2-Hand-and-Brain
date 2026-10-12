using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace HandAndBrain;

/// <summary>
/// Whether hand-and-brain is on, and the order in which players pick for each other.
/// Order[i] picks the card rewards of Order[i + 1]; the last player picks for the first.
/// </summary>
public sealed class HandAndBrainSettings
{
	public bool Enabled { get; set; }

	public List<ulong> Order { get; set; } = new List<ulong>();

	[JsonIgnore]
	public bool IsInEffect => Enabled && Order.Count >= 2;

	public HandAndBrainSettings Clone()
	{
		return new HandAndBrainSettings
		{
			Enabled = Enabled,
			Order = Order.ToList()
		};
	}

	/// <summary>The player whose card rewards <paramref name="chooserId"/> picks, or null.</summary>
	public ulong? TargetOf(ulong chooserId)
	{
		int index = Order.IndexOf(chooserId);
		if (index < 0 || Order.Count < 2)
		{
			return null;
		}
		return Order[(index + 1) % Order.Count];
	}

	/// <summary>The player who picks <paramref name="recipientId"/>'s card rewards, or null.</summary>
	public ulong? ChooserOf(ulong recipientId)
	{
		int index = Order.IndexOf(recipientId);
		if (index < 0 || Order.Count < 2)
		{
			return null;
		}
		return Order[(index - 1 + Order.Count) % Order.Count];
	}

	/// <summary>
	/// Drops players who are no longer present and appends new players at the end,
	/// keeping the relative order of everyone else.
	/// </summary>
	public HandAndBrainSettings NormalizedFor(IEnumerable<ulong> playerIds)
	{
		List<ulong> present = playerIds.ToList();
		List<ulong> order = Order.Where(present.Contains).Distinct().ToList();
		order.AddRange(present.Where((ulong id) => !order.Contains(id)));
		return new HandAndBrainSettings
		{
			Enabled = Enabled,
			Order = order
		};
	}

	public bool SameAs(HandAndBrainSettings? other)
	{
		return other != null && other.Enabled == Enabled && other.Order.SequenceEqual(Order);
	}

	public override string ToString()
	{
		return $"Enabled={Enabled} Order=[{string.Join(",", Order)}]";
	}
}

/// <summary>Global mod state: the lobby being configured and the run in progress.</summary>
public static class HandAndBrainState
{
	/// <summary>Settings for the lobby currently open (edited by the host, mirrored on clients).</summary>
	public static HandAndBrainSettings Lobby { get; set; } = new HandAndBrainSettings();

	/// <summary>True once the host has told us the settings for a loaded run.</summary>
	public static bool LobbyReceivedFromHost { get; set; }

	/// <summary>Settings for the run in progress, or null when no hand-and-brain run is active.</summary>
	public static HandAndBrainSettings? Active { get; set; }

	public static bool IsActive => Active?.IsInEffect ?? false;

	public static void ResetLobby()
	{
		Lobby = new HandAndBrainSettings();
		LobbyReceivedFromHost = false;
	}
}

/// <summary>
/// Remembers each run's settings in user://hand_and_brain so a saved multiplayer run
/// resumes with the same pairing. Keyed by seed and the set of players.
/// </summary>
public static class SettingsStore
{
	private static string Dir => Path.Combine(OS.GetUserDataDir(), "hand_and_brain");

	private static string KeyFor(string? seed, IEnumerable<ulong> playerIds)
	{
		string safeSeed = new string((seed ?? "noseed").Where(char.IsLetterOrDigit).ToArray());
		return safeSeed + "_" + string.Join("-", playerIds.OrderBy((ulong id) => id));
	}

	public static void Save(string? seed, IEnumerable<ulong> playerIds, HandAndBrainSettings settings)
	{
		try
		{
			Directory.CreateDirectory(Dir);
			string path = Path.Combine(Dir, KeyFor(seed, playerIds) + ".json");
			File.WriteAllText(path, JsonSerializer.Serialize(settings));
			Log.Info($"[HandAndBrain] Saved run settings to {path}: {settings}");
		}
		catch (Exception ex)
		{
			Log.Error($"[HandAndBrain] Failed to save run settings: {ex}");
		}
	}

	public static HandAndBrainSettings? Load(string? seed, IEnumerable<ulong> playerIds)
	{
		try
		{
			string path = Path.Combine(Dir, KeyFor(seed, playerIds) + ".json");
			if (!File.Exists(path))
			{
				return null;
			}
			HandAndBrainSettings? settings = JsonSerializer.Deserialize<HandAndBrainSettings>(File.ReadAllText(path));
			Log.Info($"[HandAndBrain] Loaded run settings from {path}: {settings}");
			return settings;
		}
		catch (Exception ex)
		{
			Log.Error($"[HandAndBrain] Failed to load run settings: {ex}");
			return null;
		}
	}
}
