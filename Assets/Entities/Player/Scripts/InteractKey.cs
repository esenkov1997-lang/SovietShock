using UnityEngine.InputSystem;

namespace Player
{
	// Единая точка доступа к действию Interact из Input Actions игрока — для тех, кто живёт вне игрока
	// (терминал) и для подписи клавиши в подсказке. Клавиша берётся из реального биндинга, поэтому после
	// переназначения в StarterAssets.inputactions (или ребинда в настройках) подсказка и терминал
	// подхватят новую клавишу сами.
	//
	// Регистрирует действие PlayerInteractor в Awake — у него уже есть доступ к PlayerInput игрока.
	public static class InteractKey
	{
		public const string ActionName = "Interact";
		private const string FallbackName = "E";

		private static PlayerInput _playerInput;
		private static InputAction _action;

		public static void Register(PlayerInput playerInput)
		{
			_playerInput = playerInput;
			_action = playerInput != null ? playerInput.actions.FindAction(ActionName) : null;
		}

		// Нажато именно в этом кадре. Читается напрямую из действия, а не из StarterAssetsInputs.interact:
		// тот флаг "съедает" PlayerInteractor, а во время фокуса на терминале он вообще выключен
		public static bool WasPressedThisFrame => _action != null && _action.WasPressedThisFrame();

		// Подпись клавиши для текущей схемы управления: "F" на клавиатуре, кнопка геймпада на геймпаде
		public static string DisplayName => GetDisplayName(_action, FallbackName);

		// То же для любого другого действия игрока по имени из Input Actions ("Jump" → "Space") —
		// для подсказок вроде "[Space] Взобраться". fallback — если действия нет или PlayerInput не зарегистрирован
		public static string GetDisplayName(string actionName, string fallback)
		{
			InputAction action = _playerInput != null ? _playerInput.actions.FindAction(actionName) : null;
			return GetDisplayName(action, fallback);
		}

		private static string GetDisplayName(InputAction action, string fallback)
		{
			if (action == null) return fallback;

			string scheme = _playerInput != null ? _playerInput.currentControlScheme : null;
			string name = string.IsNullOrEmpty(scheme)
				? action.GetBindingDisplayString()
				: action.GetBindingDisplayString(InputBinding.MaskByGroup(scheme));

			return string.IsNullOrEmpty(name) ? fallback : name;
		}
	}
}
