using UnityEngine;
using Weapons;

namespace Inventory
{
	// Лечащий расходник (аптечка, шприц): лежит в инвентаре стопкой, по кнопке лечения (Player.PlayerHealing) берётся
	// в руки вместо оружия на время своей анимации. Модель в руках устроена как у оружия (риг рук FPHands, предмет
	// на кости w_base, WeaponAnimationEvents на объекте с Animator) — поэтому её показывает тот же WeaponController
	// (IViewModelSource). События в клипе: ItemApplied (игла вошла — с этого кадра идёт лечение, предмет списывается),
	// ItemUseFinished (лечение набрано), WeaponHidden на последнем кадре (предмет убран — оружие возвращается)
	[CreateAssetMenu(fileName = "NewHealingItemData", menuName = "Inventory/Healing Item Data")]
	public class HealingItemData : ItemData, IViewModelSource
	{
		[Header("Healing")]
		[Tooltip("Сколько здоровья даёт одна штука — набирается равномерно между событиями ItemApplied и ItemUseFinished " +
			"в клипе. Прерванное лечение оставляет то, что успело набраться")]
		[Min(1)] public int healAmount = 50;

		[Header("In Hands")]
		[Tooltip("Модель в руках: риг рук с предметом, Animator с контроллером предмета и WeaponAnimationEvents — как In Hand Prefab оружия")]
		public GameObject inHandPrefab;
		[Tooltip("Trigger в Animator Controller предмета, запускающий клип применения. Пусто — клип и есть состояние по умолчанию")]
		public string useTrigger = "Use";
		[Tooltip("Как у оружия (WeaponData): кость камеры рига совпадает с камерой игрока — в игре видно ровно то, что через камеру Blender")]
		public bool alignArmsToCameraBone = true;
		[Tooltip("Смещение рук относительно \"как в Blender\" (обычно ноль) — как Hand Position у оружия")]
		public Vector3 handPosition;
		[Tooltip("Поворот рук относительно \"как в Blender\", градусы")]
		public Vector3 handRotation;
		[Tooltip("Sway/Bobbing — можно тот же профиль, что у лёгкого оружия. Пусто — руки без процедурной раскачки")]
		public WeaponAnimationProfileSO animationProfile;

		GameObject IViewModelSource.InHandPrefab => inHandPrefab;
		bool IViewModelSource.AlignArmsToCameraBone => alignArmsToCameraBone;
		Vector3 IViewModelSource.HandPosition => handPosition;
		Vector3 IViewModelSource.HandRotation => handRotation;
		WeaponAnimationProfileSO IViewModelSource.AnimationProfile => animationProfile;

		// лечащий предмет всегда расходник — незачем давать выставить это вручную и промахнуться
		private void OnValidate()
		{
			type = ItemType.Consumable;
		}
	}
}
