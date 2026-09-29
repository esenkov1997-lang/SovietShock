using TMPro;
using UnityEngine;
using Weapons;

namespace HUD
{
	// Счётчик патронов: "в магазине / в запасе". Вешается на объект счётчика в HUD-канвасе.
	// Прячется целиком, когда в руках нет оружия или у оружия нет магазина (обычное холодное).
	// Состояние читается у WeaponController каждый кадр, но текст пересобирается только при изменении —
	// никакой аллокации строк, пока цифры стоят на месте.
	public class AmmoCounterUI : PlayerHUDBinding
	{
		[Header("References")]
		[Tooltip("Что прятать, когда показывать нечего (без оружия / без магазина). Обычно — сам объект счётчика или его дочерняя панель. Пусто — прячутся только тексты")]
		[SerializeField] private GameObject visualRoot;
		[Tooltip("Патроны в магазине")]
		[SerializeField] private TMP_Text magazineText;
		[Tooltip("Опционально — патроны в запасе (в инвентаре)")]
		[SerializeField] private TMP_Text reserveText;

		[Header("Format")]
		[Tooltip("Минимум цифр в магазине: 2 — \"07\" вместо \"7\"")]
		[SerializeField] private int magazineDigits = 2;
		[Tooltip("Текст запаса, когда патроны бесконечные (у оружия не задан AmmoId или сцена без инвентаря)")]
		[SerializeField] private string infiniteReserveText = "∞";
		[Tooltip("Текст в поле магазина во время перезарядки. Пусто — показывать число как обычно")]
		[SerializeField] private string reloadingText = "--";

		[Header("Colors")]
		[SerializeField] private Color normalColor = Color.white;
		[Tooltip("Цвет магазина, когда патронов осталось не больше Low Ammo Fraction от ёмкости")]
		[SerializeField] private Color lowAmmoColor = new Color(1f, 0.35f, 0.25f);
		[Range(0f, 1f)]
		[SerializeField] private float lowAmmoFraction = 0.25f;
		[Tooltip("Цвет запаса, когда в инвентаре пусто")]
		[SerializeField] private Color emptyReserveColor = new Color(1f, 1f, 1f, 0.35f);

		private int _shownMagazine = int.MinValue;
		private int _shownReserve = int.MinValue;
		private int _shownCapacity = int.MinValue;
		private bool _shownReloading;
		private bool _shownVisible = true;

		private void Update()
		{
			WeaponData data = Weapons != null && !Weapons.IsHolstered ? Weapons.CurrentWeaponData : null;
			AmmoData ammo = data != null ? data.Ammo : null;

			SetVisible(ammo != null);
			if (ammo == null) return;

			int magazine = Weapons.CurrentAmmo;
			int reserve = Weapons.ReserveAmmo;
			bool reloading = Weapons.IsReloading && !string.IsNullOrEmpty(reloadingText);

			if (magazine == _shownMagazine && reserve == _shownReserve && ammo.MagazineSize == _shownCapacity && reloading == _shownReloading) return;

			_shownMagazine = magazine;
			_shownReserve = reserve;
			_shownCapacity = ammo.MagazineSize;
			_shownReloading = reloading;

			if (magazineText != null)
			{
				magazineText.text = reloading ? reloadingText : magazine.ToString().PadLeft(magazineDigits, '0');
				bool low = !reloading && magazine <= Mathf.FloorToInt(ammo.MagazineSize * lowAmmoFraction);
				magazineText.color = low ? lowAmmoColor : normalColor;
			}

			if (reserveText != null)
			{
				reserveText.text = reserve < 0 ? infiniteReserveText : reserve.ToString();
				reserveText.color = reserve == 0 ? emptyReserveColor : normalColor;
			}
		}

		private void SetVisible(bool visible)
		{
			if (visible == _shownVisible) return;
			_shownVisible = visible;

			if (visualRoot != null && visualRoot != gameObject)
			{
				visualRoot.SetActive(visible);
				return;
			}

			// visualRoot не задан или это сам объект счётчика — его выключать нельзя, иначе перестанет
			// работать Update и счётчик уже не появится. Прячем только тексты
			if (magazineText != null) magazineText.enabled = visible;
			if (reserveText != null) reserveText.enabled = visible;
		}
	}
}
