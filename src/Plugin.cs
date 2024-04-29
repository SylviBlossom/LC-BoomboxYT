using BepInEx;
using BepInEx.Logging;
using CessilCellsCeaChells.CeaChore;
using GameNetcodeStuff;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

[assembly: RequiresMethod(typeof(BoomboxItem), "Awake", typeof(void))]

namespace BoomboxYT;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
[BepInDependency("com.rune580.LethalCompanyInputUtils", BepInDependency.DependencyFlags.HardDependency)]
public class Plugin : BaseUnityPlugin
{
	public static Plugin Instance { get; private set; }

	public static new Config Config { get; private set; }
	public static new ManualLogSource Logger { get; private set; }
	public static YoutubeDL YTDL { get; private set; }

	public static string RootPath { get; private set; }

	private void Awake()
	{
		Instance = this;
		Logger = base.Logger;
		Config = new(base.Config);

		new Thread(() => SetupFiles()).Start();

		RootPath = Path.Combine(Paths.BepInExRootPath, "boomboxyt");

		YTDL = new YoutubeDL();
		YTDL.FFmpegPath = Path.Combine(RootPath, "bin", Utils.FfmpegBinaryName);
		YTDL.YoutubeDLPath = Path.Combine(RootPath, "bin", Utils.YtDlpBinaryName);
		YTDL.OutputFolder = Path.Combine(RootPath, "music");

		NetcodePatcher();

		var harmony = new Harmony("moe.sylvi.BoomboxYT");
		harmony.PatchAll(typeof(Patches));

		Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
	}

	private static void NetcodePatcher()
	{
		var types = Assembly.GetExecutingAssembly().GetTypes();
		foreach (var type in types)
		{
			var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
			foreach (var method in methods)
			{
				var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
				if (attributes.Length > 0)
				{
					method.Invoke(null, null);
				}
			}
		}
	}

	private static void SetupFiles()
	{
		var binPath = Path.Combine(RootPath, "bin");
		var musicPath = Path.Combine(RootPath, "music");

		if (Directory.Exists(musicPath))
		{
			Logger.LogInfo("Clearing previously downloaded music files");

			Directory.Delete(musicPath, true);
		}

		Directory.CreateDirectory(binPath);
		Directory.CreateDirectory(musicPath);

		Utils.DownloadBinaries(true, binPath).Wait();

		Logger.LogInfo("YoutubeDL binaries downloaded / up-to-date");
	}

	public static void DownloadBoomboxMusic(string url, int playerId, BoomboxYTComponent boomboxComponent)
	{
		boomboxComponent.StartDownloading(playerId);

		if (BoomboxYTComponent.LoadedTracks.ContainsKey(url))
		{
			boomboxComponent.FinishDownloading(url, BoomboxYTComponent.LoadedTracks[url]);
		}
		else
		{
			new Thread(() => __DownloadBoomboxMusic(url, boomboxComponent)).Start();
		}
	}

	private static void __DownloadBoomboxMusic(string url, BoomboxYTComponent boomboxComponent)
	{
		var task = DownloadClipFromYT(url);

		task.Wait();

		var result = task.Result;

		boomboxComponent.threadCallback = () =>
		{
			boomboxComponent.FinishDownloading(url, result);
		};
	}

	private static async Task<AudioClip> DownloadClipFromYT(string url)
	{
		Logger.LogInfo($"Downloading audio from url: {url}");

		var result = await YTDL.RunAudioDownload(url, format: AudioConversionFormat.Vorbis);

		Logger.LogInfo($"Audio downloaded (Success: {result.Success})");

		if (!result.Success)
		{
			return null;
		}

		var fullPath = Path.GetFullPath(result.Data);

		Logger.LogInfo($"Creating audio clip");

		var clip = await DownloadClip(fullPath, AudioType.OGGVORBIS);

		if (clip != null)
		{
			Logger.LogInfo("Successfully imported audio");
		}
		else
		{
			Logger.LogInfo("Audio import failed");
		}

		return clip;
	}

	// https://discussions.unity.com/t/load-audioclip-from-folder-on-computer-into-game-in-runtime/209967/2
	private static async Task<AudioClip> DownloadClip(string path, AudioType audioType = AudioType.UNKNOWN)
	{
		AudioClip clip = null;

		using (UnityWebRequest uwr = UnityWebRequestMultimedia.GetAudioClip(path, audioType))
		{
			uwr.SendWebRequest();

			// wrap tasks in try/catch, otherwise it'll fail silently
			try
			{
				while (!uwr.isDone) await Task.Delay(5);

				if (uwr.result != UnityWebRequest.Result.Success)
				{
					Logger.LogError(uwr.error);
				}
				else
				{
					clip = DownloadHandlerAudioClip.GetContent(uwr);
				}
			}
			catch (Exception err)
			{
				Logger.LogError(err);
			}
		}

		return clip;
	}
}