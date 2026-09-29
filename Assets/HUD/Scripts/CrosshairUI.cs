using System.Collections.Generic;
using UnityEngine;
using Weapons;

namespace HUD
{
	// Поведение перекрестия: динамический разброс (движение, прыжок, присед, стрельба), стиль под тип
	// оружия и скрытие. Рисует CrosshairGraphic — вешается на тот же объект.
	//
	// Скрыть прицел из любого места игры (катсцена, диалог, особое оружие и т.п.):
	//     CrosshairUI.SetHidden(this, true);   // ... и потом SetHidden(this, false)
	// Каждый источник снимает только свой запрет: прицел виден, только когда запретов не осталось, поэтому
	// две системы не "перещёлкивают" его друг у друга. Прицел, убранное оружие и открытый курсор (инвентарь,
	// терминал) учитываются автоматически — для них вызывать ничего не нужно.
	[RequireComponent(typeof(CrosshairGraphic))]
	public class CrosshairUI : PlayerHUDBinding
	{
		public enum Style { Hidden, DotOnly, Full }

		[Header("Style per weapon")]
		[Tooltip("Без оружия в руках")]
		[SerializeField] private Style noWeapon = Style.DotOnly;
		[Tooltip("Холодное оружие / ремонтный инструмент")]
		[SerializeField] private Style melee = Style.DotOnly;
		[Tooltip("Огнестрел")]
		[SerializeField] private Style firearm = Style.Full;

		[Header("Spread (px, добавляется к Gap в CrosshairGraphic)")]
		[Tooltip("Разброс при беге на полной скорости (Sprint Speed); при ходьбе — пропорционально скорости")]
		[SerializeField] private float movementSpread = 12f;
		[Tooltip("Разброс в воздухе (прыжок/падение)")]
		[SerializeField] private float airborneSpread = 10f;
		[Tooltip("Множитель разброса в приседе")]
		[SerializeField] private float crouchMultiplier = 0.6f;
		[Tooltip("Разброс за один выстрел огнестрела")]
		[SerializeField] private float shotSpread = 6f;
		[Tooltip("Максимальный накопленный разброс от стрельбы")]
		[SerializeField] private float maxShotSpread = 24f;
		[Tooltip("Скорость, с которой разброс от стрельбы сходит на нет, px/сек")]
		[SerializeField] private float shotRecovery = 40f;
		[Tooltip("Плавность раскрытия/сведения от движения: больше — резче")]
		[SerializeField] private float smoothing = 12f;

		[Header("Visibility")]
		[Tooltip("Прятать при прицеливании (ПКМ) — прицел заменяет мушка оружия")]
		[SerializeField] private bool hideWhenAiming = true;
		[Tooltip("Прятать, пока курсор мыши разблокирован (открыт инвентарь, терминал, меню)")]
		[SerializeField] private bool hideWhenCursorUnlocked = true;
		[Tooltip("Скорость появления/исчезновения. 0 — мгновенно")]
		[SerializeField] private float fadeSpeed = 12f;

		private static readonly HashSet<Object> HideRequests = new HashSet<Object>();

		private CrosshairGraphic _graphic;
		private float _movementSpread;
		private float _shotSpread;
		private float _alpha = 1f;

		// спрятать/показать прицел от имени requester (обычно this вызывающего компонента)
		public static void SetHidden(Object requester, bool hidden)
		{
			if (requester == null) return;
			if (hidden) HideRequests.Add(requester);
			else HideRequests.Remove(requester);
		}

		private void Awake()
		{
			_graphic = GetComponent<CrosshairGraphic>();
		}

		protected override void OnBind(WeaponController weapons) => weapons.Fired += HandleFired;
		protected override void OnUnbind(WeaponController weapons) => weapons.Fired -= HandleFired;

		private void HandleFired(WeaponData data)
		{
			if (data is FirearmData) _shotSpread = Mathf.Min(_shotSpread + shotSpread, maxShotSpread);
		}

		private void Update()
		{
			Style style = ResolveStyle();
			_graphic.SetStyle(style == Style.Full, style == Style.DotOnly);

			UpdateSpread();

			float targetAlpha = style != Style.Hidden && !IsHiddenByState() ? 1f : 0f;
			_alpha = fadeSpeed > 0f ? Mathf.MoveTowards(_alpha, targetAlpha, fadeSpeed * Time.deltaTime) : targetAlpha;
			_graphic.canvasRenderer.SetAlpha(_alpha);
		}

		private Style ResolveStyle()
		{
			WeaponData data = Weapons != null ? Weapons.CurrentWeaponData : null;
			if (data is FirearmData) return firearm;
			if (data is MeleeWeaponData) return melee;
			return noWeapon;
		}

		private bool IsHiddenByState()
		{
			// запросы от уничтоженных объектов (забыли снять перед Destroy) не должны держать прицел скрытым вечно
			HideRequests.RemoveWhere(requester => requester == null);
			if (HideRequests.Count > 0) return true;

			if (hideWhenCursorUnlocked && Cursor.lockState != CursorLockMode.Locked) return true;
			if (Weapons == null) return false;
			if (Weapons.IsHolstered) return true;
			return hideWhenAiming && Weapons.IsAiming;
		}

		private void UpdateSpread()
		{
			float target = 0f;
			if (Movement != null)
			{
				float speed01 = Movement.SprintSpeed > 0f ? Mathf.Clamp01(Movement.CurrentSpeed / Movement.SprintSpeed) : 0f;
				target = speed01 * movementSpread;
				if (!Movement.Grounded) target += airborneSpread;
				if (Movement.IsCrouching) target *= crouchMultiplier;
			}

			_movementSpread = Mathf.Lerp(_movementSpread, target, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
			_shotSpread = Mathf.MoveTowards(_shotSpread, 0f, shotRecovery * Time.deltaTime);

			_graphic.Spread = _movementSpread + _shotSpread;
		}
	}
}
