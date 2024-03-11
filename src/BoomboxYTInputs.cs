using LethalCompanyInputUtils.Api;
using UnityEngine.InputSystem;

namespace BoomboxYT;

public class BoomboxYTInputs : LcInputActions
{
	public static BoomboxYTInputs Instance = new();

	[InputAction("<Keyboard>/minus", Name = "Boombox Vol-", ActionId = "BoomboxVolumeDown")]
	public InputAction VolumeDown { get; set; }

	[InputAction("<Keyboard>/equals", Name = "Boombox Vol+", ActionId = "BoomboxVolumeUp")]
	public InputAction VolumeUp { get; set; }
}
