using Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Interactables
{
	// Управление терминалом — действия карты Interface из StarterAssets.inputactions. Клавиши задаются только там
	// (сейчас: стрелки/WASD, Enter, Escape/Backspace, колесо, PageUp/PageDown, Home/End и аналоги на геймпаде),
	// в коде ни одной клавиши нет.
	//
	// Действия берутся из PlayerInput того игрока, который открыл терминал, — того же экземпляра, что управляет
	// игроком, поэтому переназначение клавиш в настройках (когда появится) подхватится и здесь.
	// Карта включается при первом использовании и дальше не выключается: она общая для всех терминалов,
	// а читает её только тот, с которым сейчас работают.
	public sealed class TerminalControls
	{
		public const string MapName = "Interface";

		private readonly InputAction _up;
		private readonly InputAction _down;
		private readonly InputAction _left;
		private readonly InputAction _right;
		private readonly InputAction _submit;
		private readonly InputAction _back;
		private readonly InputAction _scroll;
		private readonly InputAction _pageUp;
		private readonly InputAction _pageDown;
		private readonly InputAction _scrollTop;
		private readonly InputAction _scrollBottom;

		private TerminalControls(InputActionMap map, Object context)
		{
			_up = Find(map, "Up", context);
			_down = Find(map, "Down", context);
			_left = Find(map, "Left", context);
			_right = Find(map, "Right", context);
			_submit = Find(map, "Submit", context);
			_back = Find(map, "Back", context);
			_scroll = Find(map, "Scroll", context);
			_pageUp = Find(map, "PageUp", context);
			_pageDown = Find(map, "PageDown", context);
			_scrollTop = Find(map, "ScrollTop", context);
			_scrollBottom = Find(map, "ScrollBottom", context);

			map.Enable();
		}

		// null — у игрока нет PlayerInput или в его Input Actions нет карты Interface (причина пишется в консоль)
		public static TerminalControls FromPlayer(Component player, Object context)
		{
			PlayerInput playerInput = player != null ? player.GetComponentInChildren<PlayerInput>() : null;
			if (playerInput == null || playerInput.actions == null)
			{
				Debug.LogError($"{nameof(TerminalControls)}: у игрока не найден PlayerInput с Input Actions — " +
					"терминалом нельзя управлять", context);
				return null;
			}

			InputActionMap map = playerInput.actions.FindActionMap(MapName);
			if (map == null)
			{
				Debug.LogError($"{nameof(TerminalControls)}: в {playerInput.actions.name} нет карты действий " +
					$"\"{MapName}\" — терминалом нельзя управлять", context);
				return null;
			}

			return new TerminalControls(map, context);
		}

		private static InputAction Find(InputActionMap map, string actionName, Object context)
		{
			InputAction action = map.FindAction(actionName);
			if (action == null)
			{
				Debug.LogError($"{nameof(TerminalControls)}: в карте {MapName} нет действия \"{actionName}\"", context);
			}
			return action;
		}

		private static bool Pressed(InputAction action) => action != null && action.WasPressedThisFrame();
		private static bool Held(InputAction action) => action != null && action.IsPressed();

		// навигация: нажато в этом кадре
		public bool UpPressed => Pressed(_up);
		public bool DownPressed => Pressed(_down);
		public bool LeftPressed => Pressed(_left);
		public bool RightPressed => Pressed(_right);
		// "зажато" — для плавных действий (прокрутка), в отличие от "нажато в этом кадре" выше
		public bool UpHeld => Held(_up);
		public bool DownHeld => Held(_down);

		// Interface/Submit (Enter). Для поля пароля — только он: клавиша взаимодействия там может оказаться буквой пароля
		public bool SubmitPressed => Pressed(_submit);
		// "Выбрать" в меню и списках: Submit или клавиша взаимодействия (та же, что открывает терминал)
		public bool SelectPressed => SubmitPressed || InteractKey.WasPressedThisFrame;

		// "Назад". textFieldFocused — сейчас печатают в поле ввода: тогда Backspace стирает символ и не считается
		// "назад" (а Escape и кнопка геймпада — считаются)
		public bool BackPressed(bool textFieldFocused = false)
		{
			if (!Pressed(_back)) return false;
			return !(textFieldFocused && _back.activeControl is KeyControl key && key.keyCode == Key.Backspace);
		}

		// прокрутка: колесо (щелчков за кадр, > 0 — вверх), страница, в начало/конец
		public float ScrollDelta => _scroll != null ? _scroll.ReadValue<float>() : 0f;
		public bool PageUpPressed => Pressed(_pageUp);
		public bool PageDownPressed => Pressed(_pageDown);
		public bool ScrollTopPressed => Pressed(_scrollTop);
		public bool ScrollBottomPressed => Pressed(_scrollBottom);
	}
}
