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
		public static string DisplayName
		{
			get
			{
				if (_action == null) return FallbackName;

				string scheme = _playerInput != null ? _playerInput.currentControlScheme : null;
				string name = string.IsNullOrEmpty(scheme)
					? _action.GetBindingDisplayString()
					: _action.GetBindingDisplayString(InputBinding.MaskByGroup(scheme));

				return string.IsNullOrEmpty(name) ? FallbackName : name;
			}
		}
	}
}
