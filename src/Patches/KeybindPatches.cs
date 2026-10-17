using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace HandAndBrain.Patches;

/// <summary>
/// Puts the "view the deck you pick for" hotkey into the game's own keybinding system, as the
/// last row of the input settings menu (default R, rebindable for mouse and keyboard and for
/// keyboard-only mode). The game turns key presses into input actions itself, so the action has
/// no key events of its own.
///
/// The binding is kept in user://hand_and_brain/keybinds.json rather than in the game's settings
/// file, so uninstalling the mod leaves no unknown action behind in the game's key mapping.
/// </summary>
public static class RecipientDeckKeybind
{
	public const Key DefaultKey = Key.R;

	private sealed class Stored
	{
		public string MouseAndKeyboard { get; set; } = DefaultKey.ToString();

		public string KeyboardOnly { get; set; } = DefaultKey.ToString();
	}

	private static Stored? _stored;

	private static string FilePath => Path.Combine(OS.GetUserDataDir(), "hand_and_brain", "keybinds.json");

	private static Stored Bindings => _stored ??= Load();

	public static Key MouseAndKeyboardKey => Parse(Bindings.MouseAndKeyboard);

	public static Key KeyboardOnlyKey => Parse(Bindings.KeyboardOnly);

	public static void Register()
	{
		StringName action = RecipientDeckHotkey.Action;
		if (!InputMap.HasAction(action))
		{
			InputMap.AddAction(action);
		}
		// Declared as IReadOnlyList, but they are Lists. The settings menu lists these in order.
		foreach (IReadOnlyList<StringName> list in new[] { NInputManager.remappableMKbInputs, NInputManager.remappableKbOnlyInputs })
		{
			if (list is List<StringName> mutable && !mutable.Contains(action))
			{
				mutable.Add(action);
			}
		}
		NInputSettingsEntry.commandToLocTitle[action] = ModStrings.KeybindTitle;
		// Normally the input manager reads its defaults (and so our binding) after mods load.
		NInputManager? manager = NInputManager.Instance;
		if (manager != null)
		{
			SaveKeybindsPatch.AddIfMissing(manager);
		}
	}

	public static void Remember(Key? mouseAndKeyboard, Key? keyboardOnly)
	{
		Stored stored = Bindings;
		string mkb = mouseAndKeyboard?.ToString() ?? stored.MouseAndKeyboard;
		string kbOnly = keyboardOnly?.ToString() ?? stored.KeyboardOnly;
		if (mkb == stored.MouseAndKeyboard && kbOnly == stored.KeyboardOnly)
		{
			return;
		}
		stored.MouseAndKeyboard = mkb;
		stored.KeyboardOnly = kbOnly;
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
			File.WriteAllText(FilePath, JsonSerializer.Serialize(stored));
		}
		catch (Exception ex)
		{
			Log.Error($"[HandAndBrain] Failed to save keybinds: {ex}");
		}
	}

	private static Stored Load()
	{
		try
		{
			if (File.Exists(FilePath))
			{
				return JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath)) ?? new Stored();
			}
		}
		catch (Exception ex)
		{
			Log.Error($"[HandAndBrain] Failed to load keybinds: {ex}");
		}
		return new Stored();
	}

	private static Key Parse(string value)
	{
		return Enum.TryParse(value, out Key key) ? key : DefaultKey;
	}
}

/// <summary>
/// The game builds its key maps from these defaults and then applies the saved bindings, which
/// never include ours, so this is where our current binding enters the map.
/// </summary>
[HarmonyPatch(typeof(NInputManager), "DefaultHotkeyInputMap", MethodType.Getter)]
public static class DefaultHotkeyInputMapPatch
{
	public static void Postfix(Dictionary<StringName, Key> __result)
	{
		__result[RecipientDeckHotkey.Action] = RecipientDeckKeybind.MouseAndKeyboardKey;
	}
}

[HarmonyPatch(typeof(NInputManager), "DefaultKbOnlyInputMap", MethodType.Getter)]
public static class DefaultKbOnlyInputMapPatch
{
	public static void Postfix(Dictionary<StringName, Key> __result)
	{
		__result[RecipientDeckHotkey.Action] = RecipientDeckKeybind.KeyboardOnlyKey;
	}
}

/// <summary>"Reset to default" puts R back.</summary>
[HarmonyPatch(typeof(NInputManager), nameof(NInputManager.ResetToDefaults))]
public static class ResetKeybindsPatch
{
	public static void Prefix()
	{
		RecipientDeckKeybind.Remember(RecipientDeckKeybind.DefaultKey, RecipientDeckKeybind.DefaultKey);
	}
}

/// <summary>
/// Takes our binding out of the map while the game writes its settings file, and keeps it in our
/// own file instead.
/// </summary>
[HarmonyPatch]
public static class SaveKeybindsPatch
{
	private static readonly AccessTools.FieldRef<NInputManager, Dictionary<StringName, Key>> _mKbInputMap = AccessTools.FieldRefAccess<NInputManager, Dictionary<StringName, Key>>("_mKbInputMap");

	private static readonly AccessTools.FieldRef<NInputManager, Dictionary<StringName, Key>> _fKbInputMap = AccessTools.FieldRefAccess<NInputManager, Dictionary<StringName, Key>>("_fKbInputMap");

	[HarmonyPatch(typeof(NInputManager), "SaveMKbInputMapping")]
	[HarmonyPrefix]
	public static void BeforeSaveMKb(NInputManager __instance, out Key? __state)
	{
		__state = Take(_mKbInputMap(__instance));
		if (__state.HasValue)
		{
			RecipientDeckKeybind.Remember(__state.Value, null);
		}
	}

	[HarmonyPatch(typeof(NInputManager), "SaveMKbInputMapping")]
	[HarmonyFinalizer]
	public static void AfterSaveMKb(NInputManager __instance, Key? __state)
	{
		if (__state.HasValue)
		{
			_mKbInputMap(__instance)[RecipientDeckHotkey.Action] = __state.Value;
		}
	}

	[HarmonyPatch(typeof(NInputManager), "SaveFKbInputMapping")]
	[HarmonyPrefix]
	public static void BeforeSaveFKb(NInputManager __instance, out Key? __state)
	{
		__state = Take(_fKbInputMap(__instance));
		if (__state.HasValue)
		{
			RecipientDeckKeybind.Remember(null, __state.Value);
		}
	}

	[HarmonyPatch(typeof(NInputManager), "SaveFKbInputMapping")]
	[HarmonyFinalizer]
	public static void AfterSaveFKb(NInputManager __instance, Key? __state)
	{
		if (__state.HasValue)
		{
			_fKbInputMap(__instance)[RecipientDeckHotkey.Action] = __state.Value;
		}
	}

	public static void AddIfMissing(NInputManager manager)
	{
		Dictionary<StringName, Key>? mkb = _mKbInputMap(manager);
		if (mkb != null && !mkb.ContainsKey(RecipientDeckHotkey.Action))
		{
			mkb[RecipientDeckHotkey.Action] = RecipientDeckKeybind.MouseAndKeyboardKey;
		}
		Dictionary<StringName, Key>? kbOnly = _fKbInputMap(manager);
		if (kbOnly != null && !kbOnly.ContainsKey(RecipientDeckHotkey.Action))
		{
			kbOnly[RecipientDeckHotkey.Action] = RecipientDeckKeybind.KeyboardOnlyKey;
		}
	}

	private static Key? Take(Dictionary<StringName, Key> map)
	{
		if (!map.TryGetValue(RecipientDeckHotkey.Action, out Key key))
		{
			return null;
		}
		map.Remove(RecipientDeckHotkey.Action);
		return key;
	}
}

/// <summary>The settings row reads its title from the settings table, which our strings join on demand.</summary>
[HarmonyPatch(typeof(NInputSettingsEntry), nameof(NInputSettingsEntry._Ready))]
public static class InputSettingsEntryReadyPatch
{
	public static void Prefix()
	{
		ModStrings.EnsureMerged();
	}
}
