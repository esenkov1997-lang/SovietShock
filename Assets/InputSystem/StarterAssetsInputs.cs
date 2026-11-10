using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
	public class StarterAssetsInputs : MonoBehaviour
	{
		[Header("Character Input Values")]
		public Vector2 move;
		public Vector2 look;
		public bool jump;
		public bool sprint;
		public bool crouch;

		[Header("Weapon Input Values")]
		public bool fire;
		// прокрутка колеса мыши: >0 — следующее оружие, <0 — предыдущее (см. WeaponInput.TickWeaponScroll)
		public float weaponScroll;
		// выбор слота колеса оружия клавишами (действия Weapon1..Weapon4): номер слота с 0, -1 — не нажато.
		// Одноразовое срабатывание — читает и сбрасывает Weapons.WeaponInput
		public int weaponSlot = -1;
		public bool interact;
		public bool reload;
		public bool aim;
		// использовать аптечку (H) — одноразовое срабатывание, читает и сбрасывает Player.PlayerHealing
		public bool heal;

		[Header("Movement Settings")]
		public bool analogMovement;

		[Header("Mouse Cursor Settings")]
		public bool cursorLocked = true;
		public bool cursorInputForLook = true;

		// Выключается, пока открыт UI поверх игры (см. Inventory.InventoryUI) — боевые кнопки перестают
		// записываться в fire/aim прямо в обработчиках ввода, поэтому не важно, в каком порядке
		// выполняются скрипты: WeaponController в этот кадр гарантированно увидит "кнопка не нажата".
		// Уже зажатые кнопки сбрасываются в момент выключения, чтобы удержание не "протекло" в UI-режим.
		private bool _gameplayInputEnabled = true;
		public bool GameplayInputEnabled
		{
			get => _gameplayInputEnabled;
			set
			{
				_gameplayInputEnabled = value;
				if (value) return;

				fire = false;
				aim = false;
			}
		}

#if ENABLE_INPUT_SYSTEM
		public void OnMove(InputValue value)
		{
			MoveInput(value.Get<Vector2>());
		}

		public void OnLook(InputValue value)
		{
			if(cursorInputForLook)
			{
				LookInput(value.Get<Vector2>());
			}
		}

		public void OnJump(InputValue value)
		{
			JumpInput(value.isPressed);
		}

		public void OnSprint(InputValue value)
		{
			SprintInput(value.isPressed);
		}

		public void OnCrouch(InputValue value)
		{
			CrouchInput(value.isPressed);
		}

		public void OnFire(InputValue value)
		{
			fire = _gameplayInputEnabled && value.isPressed;
		}

		// action называется ScrollWeapon (Vector2) — стандартный биндинг "Scroll [Mouse]" даёт весь
		// вектор колеса, нас интересует только вертикальная прокрутка (y)
		public void OnScrollWeapon(InputValue value)
		{
			weaponScroll = value.Get<Vector2>().y;
		}

		// по действию на каждый слот (а не одно действие с номером в значении) — так в настройках управления
		// у каждой клавиши будет своя понятная строка "Оружие 1", "Оружие 2"...
		public void OnWeapon1(InputValue value) => SelectWeaponSlot(value, 0);
		public void OnWeapon2(InputValue value) => SelectWeaponSlot(value, 1);
		public void OnWeapon3(InputValue value) => SelectWeaponSlot(value, 2);
		public void OnWeapon4(InputValue value) => SelectWeaponSlot(value, 3);

		private void SelectWeaponSlot(InputValue value, int slot)
		{
			if (value.isPressed) weaponSlot = slot;
		}

		public void OnInteract(InputValue value)
		{
			interact = value.isPressed;
		}

		public void OnReload(InputValue value)
		{
			reload = value.isPressed;
		}

		public void OnAim(InputValue value)
		{
			aim = _gameplayInputEnabled && value.isPressed;
		}

		public void OnHeal(InputValue value)
		{
			heal = _gameplayInputEnabled && value.isPressed;
		}
#endif


		public void MoveInput(Vector2 newMoveDirection)
		{
			move = newMoveDirection;
		} 

		public void LookInput(Vector2 newLookDirection)
		{
			look = newLookDirection;
		}

		public void JumpInput(bool newJumpState)
		{
			jump = newJumpState;
		}

		public void SprintInput(bool newSprintState)
		{
			sprint = newSprintState;
		}

		public void CrouchInput(bool newCrouchState)
		{
			crouch = newCrouchState;
		}

		private void OnApplicationFocus(bool hasFocus)
		{
			SetCursorState(cursorLocked);
		}

		private void SetCursorState(bool newState)
		{
			Cursor.lockState = newState ? CursorLockMode.Locked : CursorLockMode.None;
		}
	}
	
}