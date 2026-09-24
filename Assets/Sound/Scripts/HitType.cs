namespace Sound
{
	// Чем попали по поверхности — от этого зависит, какой набор клипов SurfaceSoundSet играет:
	// пуля по дереву и удар ключом по дереву звучат по-разному
	public enum HitType
	{
		Bullet,
		Melee
	}
}
