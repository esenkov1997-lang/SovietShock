using UnityEngine;

namespace Weapons
{
	public enum FireMode
	{
		Single, // один выстрел за нажатие, следующий — только после отпускания и нового нажатия
		Auto    // очередь, пока зажата кнопка, с темпом FireRate
	}

	// Огнестрел: патроны, отдача, прицеливание (ПКМ), декоративная пуля
	[CreateAssetMenu(fileName = "NewFirearm", menuName = "Weapons/Firearm")]
	public class FirearmData : WeaponData
	{
		[Header("Fire Mode")]
		public FireMode Mode = FireMode.Single;

		[Header("Ammo")]
		public AmmoData Magazine = new AmmoData();

		[Header("Recoil")]
		public RecoilData Recoil = new RecoilData();

		[Header("Aim Down Sights (ПКМ)")]
		public AimData Aim = new AimData();

		[Header("Bullet Visualization")]
		public BulletVisualData Bullet = new BulletVisualData();

		public override AmmoData Ammo => Magazine;
	}
}
