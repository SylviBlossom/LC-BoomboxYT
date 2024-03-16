using GameNetcodeStuff;
using HarmonyLib;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BoomboxYT;

internal static class Patches
{
	[HarmonyPatch(typeof(StartOfRound), "Start")]
	[HarmonyPrefix]
	private static void StartOfRound_Start(StartOfRound __instance)
	{
		var boomboxes = __instance.allItemsList.itemsList.Where(item => item.spawnPrefab != null && item.spawnPrefab.GetComponent<BoomboxItem>());

		foreach (var item in boomboxes)
		{
			var itemIndex = __instance.allItemsList.itemsList.IndexOf(item);
			Plugin.Logger.LogInfo($"Adding BoomboxYT component to \"{item.name}\" ID:{itemIndex}");

			if (item.spawnPrefab.GetComponent<BoomboxYTComponent>())
			{
				continue;
			}

			item.spawnPrefab.AddComponent<BoomboxYTComponent>();
			item.syncInteractLRFunction = false;
		}
	}

	[HarmonyPatch(typeof(PlayerControllerB), "OnEnable")]
	[HarmonyPostfix]
	private static void PlayerControllerB_OnEnable(PlayerControllerB __instance)
	{
		BoomboxYTInputs.Instance.VolumeDown.performed += __instance.VolumeDown_performed;
		BoomboxYTInputs.Instance.VolumeUp.performed += __instance.VolumeUp_performed;
	}

	[HarmonyPatch(typeof(PlayerControllerB), "OnDisable")]
	[HarmonyPostfix]
	private static void PlayerControllerB_OnDisable(PlayerControllerB __instance)
	{
		BoomboxYTInputs.Instance.VolumeDown.performed -= __instance.VolumeDown_performed;
		BoomboxYTInputs.Instance.VolumeUp.performed -= __instance.VolumeUp_performed;
	}

	private static void ChangeVolumeCallback(PlayerControllerB player, InputAction.CallbackContext ctx, int dir)
	{
		if (player.currentlyHeldObjectServer == null || player.currentlyHeldObjectServer is not BoomboxItem boombox)
		{
			return;
		}
		if ((!player.IsOwner || !player.isPlayerControlled || (player.IsServer && !player.isHostPlayerObject)) && !player.isTestingPlayer)
		{
			return;
		}
		if (!ctx.performed)
		{
			return;
		}
		if (player.isGrabbingObjectAnimation || player.isTypingChat || player.inTerminalMenu || player.inSpecialInteractAnimation || player.throwingObject)
		{
			return;
		}

		BoomboxYTComponent.GetInstance(boombox).ChangeVolume(dir);
	}

	private static void VolumeDown_performed(this PlayerControllerB player, InputAction.CallbackContext ctx)
	{
		ChangeVolumeCallback(player, ctx, -1);
	}

	private static void VolumeUp_performed(this PlayerControllerB player, InputAction.CallbackContext ctx)
	{
		ChangeVolumeCallback(player, ctx, 1);
	}

	[HarmonyPatch(typeof(BoomboxItem), "StartMusic")]
	[HarmonyILManipulator]
	private static void StartMusic_IL(ILContext ctx, MethodBase orig)
	{
		var cursor = new ILCursor(ctx);

		if (!cursor.TryGotoNext(MoveType.AfterLabel, instr => instr.MatchCallvirt<AudioSource>("set_clip")))
		{
			Plugin.Logger.LogError("IL Patch failed at BoomboxItem.StartMusic");
			return;
		}

		cursor.Emit(OpCodes.Ldarg_0);
		cursor.EmitDelegate((AudioClip clip, BoomboxItem boombox) => BoomboxYTComponent.GetInstance(boombox).GetAudioClip() ?? clip);
	}

	[HarmonyPatch(typeof(BoomboxItem), "StartMusic")]
	[HarmonyPrefix]
	private static void StartMusic_Prefix(BoomboxItem __instance, bool startMusic)
	{
		if (startMusic)
		{
			var component = BoomboxYTComponent.GetInstance(__instance);

			__instance.boomboxAudio.volume = BoomboxYTComponent.BaseVolume * component.GetVolume();
		}
	}

	[HarmonyPatch(typeof(GrabbableObject), "EquipItem")]
	[HarmonyPostfix]
	private static void EquipItem(GrabbableObject __instance)
	{
		if (__instance is BoomboxItem && __instance.playerHeldBy != null)
		{
			__instance.playerHeldBy.equippedUsableItemQE = true;
		}
	}

	[HarmonyPatch(typeof(GrabbableObject), "SetControlTipsForItem")]
	[HarmonyPrefix]
	private static bool SetControlTipsForItem(GrabbableObject __instance)
	{
		if (__instance is BoomboxItem boombox)
		{
			BoomboxYTComponent.GetInstance(boombox).SetControlTipsForItem();

			return false;
		}

		return true;
	}

	[HarmonyPatch(typeof(GrabbableObject), "ItemInteractLeftRight")]
	[HarmonyPostfix]
	private static void ItemInteractLeftRight(GrabbableObject __instance, bool right)
	{
		if (__instance is BoomboxItem boombox)
		{
			boombox.isBeingUsed = boombox.isPlayingMusic;

			var component = BoomboxYTComponent.GetInstance(boombox);

			if (!Config.SeparateClearBind.Value)
			{
				if (right)
				{
					return;
				}

				if (component.currentTrack == null)
				{
					component.ImportMusic();
				}
				else
				{
					component.ClearMusic();
				}
			}
			else
			{
				if (!right)
				{
					component.ImportMusic();
				}
				else
				{
					component.ClearMusic();
				}
			}
		}
	}
}
