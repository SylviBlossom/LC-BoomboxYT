using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System;
using System.Linq;

namespace BoomboxYT;

public class BoomboxYTComponent : NetworkBehaviour
{
	public const float BaseVolume = 0.754f;
	public const float DefaultVolume = 0.6f;

	public static Dictionary<string, AudioClip> LoadedTracks = new();
	public static Dictionary<string, float> TrackVolumes = new();

	public string currentTrack;
	public int downloadingMusic;
	public int playerStartedDownload;
	public bool anyFailed;

	private BoomboxItem boombox;
	public Action threadCallback;

	protected void Awake()
	{
		boombox = gameObject.GetComponent<BoomboxItem>();

		currentTrack = null;
	}

	protected void Update()
	{
		if (threadCallback != null)
		{
			threadCallback();

			threadCallback = null;
		}
	}

	public AudioClip GetAudioClip(string track = null)
	{
		if (track == null && currentTrack == null)
		{
			return null;
		}

		if (LoadedTracks.TryGetValue(track ?? currentTrack, out var clip))
		{
			return clip;
		}

		return null;
	}

	public float GetVolume(string track = null)
	{
		if (track == null && currentTrack == null)
		{
			return 1f;
		}

		if (TrackVolumes.TryGetValue(track ?? currentTrack, out var volume))
		{
			return volume;
		}

		return DefaultVolume;
	}

	public void SetControlTipsForItem()
	{
		var toolTips = boombox.itemProperties.toolTips.ToList();

		toolTips.Add("Import clipboard / clear [Q/E]");

		if (currentTrack != null)
		{
			var volume = GetVolume();

			toolTips.Add($"Volume {(int)Math.Round(volume * 100f)}% : [+/-]");
		}
		else
		{
			toolTips.Add("");
		}

		HUDManager.Instance.ChangeControlTipMultiple(toolTips.ToArray(), true, boombox.itemProperties);
	}

	public void ImportMusic()
	{
		if (downloadingMusic > 0)
		{
			HUDManager.Instance.AddChatMessage("Players are downloading music, wait a moment", "System");
			return;
		}

		var url = GUIUtility.systemCopyBuffer;
		var player = GameNetworkManager.Instance.localPlayerController;

		HUDManager.Instance.AddChatMessage($"Downloading music from {url}", "System");

		downloadingMusic++;

		ImportMusicServerRpc(url, (int)player.playerClientId);
	}

	public void ClearMusic()
	{
		if (currentTrack == null)
		{
			return;
		}

		if (downloadingMusic > 0)
		{
			HUDManager.Instance.AddChatMessage("Players are downloading music, wait a moment", "System");
			return;
		}

		ChangeMusicServerRpc("");
	}

	public void ChangeVolume(int dir)
	{
		if (currentTrack == null)
		{
			return;
		}

		var volume = Math.Clamp(GetVolume() + (dir * 0.1f), 0f, 1f);

		if (boombox.IsOwner)
		{
			__ChangeVolume(volume);
		}
		else
		{
			TrackVolumes[currentTrack] = volume;
		}

		ChangeVolumeServerRpc(volume);
	}

	public void StartDownloading(int playerId)
	{
		downloadingMusic = StartOfRound.Instance.connectedPlayersAmount;
		playerStartedDownload = playerId;
		anyFailed = false;
	}

	public void FinishDownloading(string url, AudioClip clip)
	{
		if (clip != null && !LoadedTracks.ContainsKey(url))
		{
			LoadedTracks.Add(url, clip);
		}

		FinishDownloadingServerRpc(url, clip == null);
	}

	[ServerRpc(RequireOwnership = false)]
	private void ImportMusicServerRpc(string url, int playerId)
	{
		ImportMusicClientRpc(url, playerId);
	}

	[ServerRpc(RequireOwnership = false)]
	private void ChangeMusicServerRpc(string target)
	{
		ChangeMusicClientRpc(target);
	}

	[ServerRpc(RequireOwnership = false)]
	private void ChangeVolumeServerRpc(float volume)
	{
		ChangeVolumeClientRpc(volume);
	}

	[ServerRpc(RequireOwnership = false)]
	private void FinishDownloadingServerRpc(string url, bool failed)
	{
		FinishDownloadingClientRpc(url, failed);
	}

	[ClientRpc]
	private void ImportMusicClientRpc(string url, int playerId)
	{
		Plugin.DownloadBoomboxMusic(url, playerId, this);
	}

	[ClientRpc]
	private void ChangeMusicClientRpc(string target)
	{
		if (target == "")
		{
			currentTrack = null;
			boombox.StartMusic(false);

			if (boombox.IsOwner && boombox.playerHeldBy != null)
			{
				SetControlTipsForItem();
			}

			return;
		}

		currentTrack = target;

		var clip = GetAudioClip(target);

		if (clip == null)
		{
			Plugin.Logger.LogError("Could not find imported music track");
			return;
		}

		if (boombox.isPlayingMusic)
		{
			boombox.boomboxAudio.Stop();
			boombox.boomboxAudio.clip = clip;
			boombox.boomboxAudio.volume = BaseVolume * GetVolume(target);
			boombox.boomboxAudio.pitch = 1f;
			boombox.boomboxAudio.Play();
		}

		if (boombox.IsOwner && boombox.playerHeldBy != null)
		{
			SetControlTipsForItem();
		}
	}

	[ClientRpc]
	private void ChangeVolumeClientRpc(float volume)
	{
		if (boombox.IsOwner)
		{
			return;
		}

		__ChangeVolume(volume);
	}

	private void __ChangeVolume(float volume)
	{
		if (currentTrack == null)
		{
			// Shouldnt happen!
			return;
		}

		TrackVolumes[currentTrack] = volume;

		if (boombox.isPlayingMusic)
		{
			boombox.boomboxAudio.volume = BaseVolume * volume;
		}

		if (boombox.IsOwner && boombox.playerHeldBy != null)
		{
			SetControlTipsForItem();
		}
	}

	[ClientRpc]
	private void FinishDownloadingClientRpc(string url, bool failed)
	{
		downloadingMusic--;
		anyFailed = anyFailed || failed;

		if (downloadingMusic > 0)
		{
			return;
		}

		if (!anyFailed)
		{
			if ((int)GameNetworkManager.Instance.localPlayerController.playerClientId == playerStartedDownload)
			{
				HUDManager.Instance.AddChatMessage("Music import finished", "System");
			}

			ChangeMusicServerRpc(url);
		}
		else
		{
			if ((int)GameNetworkManager.Instance.localPlayerController.playerClientId == playerStartedDownload)
			{
				HUDManager.Instance.AddChatMessage("Error: Music import failed", "System");
			}
		}
	}

	public static BoomboxYTComponent GetInstance(BoomboxItem boombox)
	{
		return boombox.gameObject.GetComponent<BoomboxYTComponent>();
	}
}
