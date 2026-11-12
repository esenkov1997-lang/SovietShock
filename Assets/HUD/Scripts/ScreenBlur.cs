using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HUD
{
	// Volume-компонент размытия экрана. Рисует его ScreenBlurFeature (должна стоять в Forward Renderer),
	// включает и плавно выключает — ScreenBlurController (перезарядка, инвентарь) через свои Volume.
	// Можно добавить и в обычный Volume Profile (Add Override → SovietShock → Screen Blur) — например, для зоны.
	[Serializable, VolumeComponentMenu("SovietShock/Screen Blur")]
	[SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
	public sealed class ScreenBlur : VolumeComponent
	{
		public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);

		// сколько раз картинка уменьшается вдвое — главный регулятор "силы" размытия
		public ClampedIntParameter iterations = new ClampedIntParameter(4, 1, 6);

		// разброс выборок внутри шага: больше — мягче и шире, но при > 2 может появиться рябь
		public ClampedFloatParameter spread = new ClampedFloatParameter(1f, 0.5f, 3f);

		// выключено — размывается только мир, оружие и руки (WeaponCamera) остаются чёткими;
		// включено — размывается вся картинка вместе с оружием
		public BoolParameter includeWeapon = new BoolParameter(false);

		// ретро-пикселизация: размытая картинка берётся блоками по столько экранных пикселей (1 — выкл).
		// При плавном появлении Volume размер блока растёт постепенно — пиксели "проступают"
		public ClampedIntParameter pixelSize = new ClampedIntParameter(1, 1, 32);

		// ретро-палитра: столько уровней на канал (0 — выкл). Без интерполяции: иначе при появлении
		// Volume число цветов проходило бы через 1..2 и картинка мигала бы плакатом
		public NoInterpClampedIntParameter colorSteps = new NoInterpClampedIntParameter(0, 0, 64);

		public bool IsActive() => intensity.value > 0f;
	}
}
