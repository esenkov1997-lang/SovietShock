using StarterAssets;
using UnityEngine;

namespace Weapons
{
	// Мост между вводом (StarterAssetsInputs, Input System) и WeaponController.
	// WeaponController намеренно ничего не знает про Input System — вся привязка клавиш живёт здесь.
	[RequireComponent(typeof(WeaponController))]
	public class WeaponInput : MonoBehaviour
	{
		[Tooltip("Если не задано — берётся StarterAssetsInputs с родительских объектов (обычно с игрока)")]
		[SerializeField] private StarterAssetsInputs input;
		[Tooltip("Прицел переключается по нажатию ПКМ, а не держится, пока она зажата")]
		[SerializeField] private bool aimIsToggle = true;

		private WeaponController _weapons;
		private bool _aimToggled;
		private bool _aimButtonLatch;

		private void Awake()
		{
			_weapons = GetComponent<WeaponController>();
			if (input == null) input = GetComponentInParent<StarterAssetsInputs>();
		}

		private void Update()
		{
			// оружие убрано (фокус на терминале/рычаге) — глотаем все нажатия, чтобы ни одно не
			// "дотекло" до момента, когда оружие снова в руках, и сбрасываем toggle-прицел
			if (_weapons.IsHolstered)
			{
				_aimToggled = false;
				_aimButtonLatch = input.aim;
				input.dropWeapon = false;
				input.reload = false;
				input.weaponScroll = 0f;
				return;
			}

			// fire передаём как есть (держится, пока зажата) — WeaponController сам решает,
			// стрелять ли каждый кадр (одиночно/по кулдауну)
			_weapons.TickFire(input.fire);
			_weapons.SetAiming(ResolveAimHeld());

			// ремонт/прожиг всегда на удержании кнопки — независимо от aimIsToggle, который влияет
			// только на прицел обычного оружия
			_weapons.TickHeal(input.aim);

			TickWeaponScroll();

			// а эти флаги — одноразовые срабатывания: считали и сразу сбросили,
			// чтобы не выбрасывать оружие на каждом кадре, пока клавиша зажата
			if (input.dropWeapon)
			{
				_weapons.DropCurrentWeapon();
				input.dropWeapon = false;
			}

			if (input.reload)
			{
				_weapons.Reload();
				input.reload = false;
			}
		}

		// прокрутка колеса мыши — единая команда переключения: вверх (>0) — следующее оружие,
		// вниз (<0) — предыдущее. Считываем и сразу сбрасываем, иначе то же значение сработает
		// повторно в следующем кадре, пока Input System не пришлёт новое событие прокрутки
		private void TickWeaponScroll()
		{
			if (input.weaponScroll > 0f) _weapons.Next();
			else if (input.weaponScroll < 0f) _weapons.Previous();

			input.weaponScroll = 0f;
		}

		// hold — просто зеркалит сырое состояние кнопки; toggle — переключается по фронту нажатия
		private bool ResolveAimHeld()
		{
			if (!aimIsToggle) return input.aim;

			bool aimPressed = input.aim && !_aimButtonLatch;
			_aimButtonLatch = input.aim;
			if (aimPressed) _aimToggled = !_aimToggled;

			return _aimToggled;
		}
	}
}
