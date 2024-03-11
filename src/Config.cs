using BepInEx.Configuration;

namespace BoomboxYT;

public class Config
{
	public static ConfigEntry<bool> SeparateClearBind;

	public Config(ConfigFile cfg)
	{
		SeparateClearBind = cfg.Bind("General", "SeparateClearBind", false, "Use E to clear custom music, instead of toggle with Q");
	}
}
