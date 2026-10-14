using Interactables;
using UnityEngine;
using Weapons;

namespace HUD
{
	// Прячет HUD (прицел, патроны, здоровье, индикаторы обнаружения), когда игрок смотрит не "своими глазами":
	//   - через камеру наблюдения (SecurityCamera.Active) — всегда;
	//   - необязательно: всё время работы с терминалом и другими объектами с фокусировкой (PlayerCameraFocus).
	//
	// Вешается на Canvas HUD (тот же объект, где HUDSignalRenderer). Прячет выключением самого Canvas — скрипты HUD
	// продолжают работать (здоровье, индикаторы обновляются), просто ничего не рисуется, и после возврата HUD
	// сразу актуален. Объекты, которые прячет DeathScreen (hideOnShow), это не затрагивает.
	[RequireComponent(typeof(Canvas))]
	public class HUDVisibility : PlayerHUDBinding
	{
		[Tooltip("Прятать HUD всё время, пока игрок работает с терминалом (и другими объектами с фокусировкой камеры). " +
			"Выключено — только пока игрок смотрит через камеру наблюдения")]
		[SerializeField] private bool hideDuringFocus;

		private Canvas _canvas;
		private PlayerCameraFocus _focus;
		private bool _focused;

		private void Awake()
		{
			_canvas = GetComponent<Canvas>();
		}

		protected override void OnEnable()
		{
			base.OnEnable();
			SecurityCamera.ActiveChanged += HandleSecurityCameraChanged;
			Refresh();
		}

		protected override void OnDisable()
		{
			SecurityCamera.ActiveChanged -= HandleSecurityCameraChanged;
			base.OnDisable();
			// компонент выключили — HUD не должен остаться спрятанным навсегда
			if (_canvas != null) _canvas.enabled = true;
		}

		protected override void OnBind(WeaponController weapons)
		{
			_focus = Movement != null ? Movement.GetComponent<PlayerCameraFocus>() : null;
			if (_focus == null) return;

			_focus.FocusStarted += HandleFocusStarted;
			_focus.FocusEnded += HandleFocusEnded;
			_focused = _focus.IsBusy;
			Refresh();
		}

		protected override void OnUnbind(WeaponController weapons)
		{
			if (_focus != null)
			{
				_focus.FocusStarted -= HandleFocusStarted;
				_focus.FocusEnded -= HandleFocusEnded;
			}
			_focus = null;
			_focused = false;
			Refresh();
		}

		private void HandleFocusStarted()
		{
			_focused = true;
			Refresh();
		}

		private void HandleFocusEnded()
		{
			_focused = false;
			Refresh();
		}

		private void HandleSecurityCameraChanged(SecurityCamera active) => Refresh();

		private void Refresh()
		{
			if (_canvas == null) return;

			bool hidden = SecurityCamera.Active != null || (hideDuringFocus && _focused);
			_canvas.enabled = !hidden;
		}
	}
}
