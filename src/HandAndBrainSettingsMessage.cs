using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace HandAndBrain;

/// <summary>Host → clients: the hand-and-brain settings for the current lobby.</summary>
public struct HandAndBrainSettingsMessage : INetMessage, IPacketSerializable
{
	public bool enabled;

	public List<ulong>? order;

	public bool ShouldBroadcast => true;

	public NetTransferMode Mode => NetTransferMode.Reliable;

	public LogLevel LogLevel => LogLevel.Debug;

	public bool ShouldBuffer => false;

	public static HandAndBrainSettingsMessage From(HandAndBrainSettings settings)
	{
		return new HandAndBrainSettingsMessage
		{
			enabled = settings.Enabled,
			order = new List<ulong>(settings.Order)
		};
	}

	public HandAndBrainSettings ToSettings()
	{
		return new HandAndBrainSettings
		{
			Enabled = enabled,
			Order = new List<ulong>(order ?? new List<ulong>())
		};
	}

	public void Serialize(PacketWriter writer)
	{
		writer.WriteBool(enabled);
		List<ulong> list = order ?? new List<ulong>();
		writer.WriteInt(list.Count, 8);
		foreach (ulong id in list)
		{
			writer.WriteULong(id);
		}
	}

	public void Deserialize(PacketReader reader)
	{
		enabled = reader.ReadBool();
		int count = reader.ReadInt(8);
		order = new List<ulong>(count);
		for (int i = 0; i < count; i++)
		{
			order.Add(reader.ReadULong());
		}
	}
}

/// <summary>Client → host: "send me the current settings" (sent when a client's lobby is created).</summary>
public struct HandAndBrainSettingsRequestMessage : INetMessage, IPacketSerializable
{
	public bool ShouldBroadcast => false;

	public NetTransferMode Mode => NetTransferMode.Reliable;

	public LogLevel LogLevel => LogLevel.Debug;

	public bool ShouldBuffer => false;

	public void Serialize(PacketWriter writer)
	{
	}

	public void Deserialize(PacketReader reader)
	{
	}
}
