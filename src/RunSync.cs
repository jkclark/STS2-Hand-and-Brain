using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace HandAndBrain;

/// <summary>Chooser → everyone: whether the chooser hides the cards they are offered from their recipient.</summary>
public struct HandAndBrainHideCardsMessage : INetMessage, IPacketSerializable
{
	public bool hidden;

	public bool ShouldBroadcast => true;

	public NetTransferMode Mode => NetTransferMode.Reliable;

	public LogLevel LogLevel => LogLevel.Debug;

	public bool ShouldBuffer => false;

	public void Serialize(PacketWriter writer)
	{
		writer.WriteBool(hidden);
	}

	public void Deserialize(PacketReader reader)
	{
		hidden = reader.ReadBool();
	}
}

/// <summary>
/// Network state for the run in progress. Everything else hand-and-brain needs during a run is
/// simulated identically on every peer; only the chooser's "hide cards" toggle has to be sent.
/// </summary>
public static class RunSync
{
	private static readonly ConditionalWeakTable<INetGameService, MessageHandlerDelegate<HandAndBrainHideCardsMessage>> _handlers = new ConditionalWeakTable<INetGameService, MessageHandlerDelegate<HandAndBrainHideCardsMessage>>();

	private static readonly Dictionary<ulong, bool> _hiddenByChooser = new Dictionary<ulong, bool>();

	/// <summary>The local player's choice: hide the cards they are offered from the player they pick for.</summary>
	public static bool HideFromRecipient { get; private set; }

	public static void Register(INetGameService netService)
	{
		_hiddenByChooser.Clear();
		HideFromRecipient = false;
		if (netService.Type == NetGameType.Singleplayer || _handlers.TryGetValue(netService, out _))
		{
			return;
		}
		MessageHandlerDelegate<HandAndBrainHideCardsMessage> handler = delegate(HandAndBrainHideCardsMessage message, ulong senderId)
		{
			_hiddenByChooser[senderId] = message.hidden;
		};
		_handlers.Add(netService, handler);
		netService.RegisterMessageHandler(handler);
	}

	public static void Unregister(INetGameService? netService)
	{
		_hiddenByChooser.Clear();
		if (netService != null && _handlers.TryGetValue(netService, out MessageHandlerDelegate<HandAndBrainHideCardsMessage>? handler))
		{
			netService.UnregisterMessageHandler(handler);
			_handlers.Remove(netService);
		}
	}

	public static bool IsHiddenBy(ulong chooserId)
	{
		return _hiddenByChooser.TryGetValue(chooserId, out bool hidden) && hidden;
	}

	public static void SetHideFromRecipient(bool hidden)
	{
		HideFromRecipient = hidden;
		BroadcastHideFromRecipient();
	}

	/// <summary>Sent on every change and again before the local player opens a card reward, so late joiners catch up.</summary>
	public static void BroadcastHideFromRecipient()
	{
		INetGameService? netService = RunManager.Instance.NetService;
		if (netService == null || netService.Type == NetGameType.Singleplayer)
		{
			return;
		}
		netService.SendMessage(new HandAndBrainHideCardsMessage
		{
			hidden = HideFromRecipient
		});
	}
}
