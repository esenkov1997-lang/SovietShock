using System;
using Inventory;
using UnityEngine;
using UnityEngine.Rendering;

namespace HUD
{
	// Включает размытие экрана (ScreenBlur) по игровым состояниям и плавно его гасит:
	//   - перезарядка — размыт мир, оружие чёткое;
	//   - открыт инвентарь — размыто всё (по умолчанию вместе с оружием).
	//
	// Вешается в UIInterface рядом с InventoryUI. Для каждого состояния создаёт свой глобальный Volume
	// с ScreenBlur и двигает только его Weight — поэтому всё настраивается здесь, без отдельных Volume Profile,
	// а в Play Mode изменения видны сразу. Если состояния совпали (перезарядка при открытом инвентаре),
	// побеждает инвентарь (у него выше приоритет), сила смешивается плавно.
	//
	// Само размытие рисует ScreenBlurFeature — она должна стоять в Forward Renderer каждого уровня качества.
	public class ScreenBlurController : PlayerHUDBinding
	{
		[Serializable]
		private class BlurState
		{
			[Range(0f, 1f)] public float intensity = 1f;
			[Tooltip("Сколько раз картинка уменьшается вдвое — главный регулятор силы размытия")]
			[Range(1, 6)] public int iterations = 4;
			[Tooltip("Разброс выборок: больше — мягче и шире; выше 2 может появиться рябь")]
			[Range(0.5f, 3f)] public float spread = 1f;
			[Tooltip("Размывать и оружие с руками. Выключено — оружие остаётся чётким")]
			public bool includeWeapon;
			[Tooltip("Ретро-пикселизация. Выключено — Pixel Size не действует")]
			public bool pixelate = true;
			[Tooltip("Размер пикселя в экранных пикселях. 1 — без пикселизации")]
			[Range(1, 32)] public int pixelSize = 1;
			[Tooltip("Ретро-палитра. Выключено — Color Steps не действует")]
			public bool limitColors = true;
			[Tooltip("Цветов на канал. 0 — без ограничения, 4..16 — заметно, 32+ — едва")]
			[Range(0, 64)] public int colorSteps;
			[Tooltip("Время появления размытия, сек")]
			[Min(0f)] public float fadeIn = 0.15f;
			[Tooltip("Время исчезновения размытия, сек")]
			[Min(0f)] public float fadeOut = 0.25f;

			[NonSerialized] public Volume volume;
			[NonSerialized] public ScreenBlur blur;
		}

		[SerializeField] private InventoryUI inventory;

		[Header("Перезарядка")]
		[SerializeField] private bool blurOnReload = true;
		[SerializeField] private BlurState reload = new BlurState { intensity = 0.8f, iterations = 3 };

		[Header("Инвентарь")]
		[SerializeField] private bool blurOnInventory = true;
		[SerializeField] private BlurState inventoryOpen = new BlurState { iterations = 5, includeWeapon = true, fadeIn = 0.2f, fadeOut = 0.2f };

		[Header("Volume")]
		[Tooltip("Слой создаваемых Volume — должен входить в Volume Mask камер игрока (у MainCamera это Default = 0)")]
		[SerializeField] private int volumeLayer;
		[Tooltip("Приоритет Volume перезарядки; у инвентаря на 1 выше")]
		[SerializeField] private float volumePriority = 100f;

		private GameObject _volumeRoot;

		private void Awake()
		{
			_volumeRoot = new GameObject("ScreenBlurVolumes") { layer = volumeLayer };
			_volumeRoot.transform.SetParent(transform, false);

			CreateVolume(reload, "Reload", volumePriority);
			CreateVolume(inventoryOpen, "Inventory", volumePriority + 1f);
		}

		protected override void OnDisable()
		{
			base.OnDisable();
			// выключили посреди эффекта — экран не должен остаться размытым
			ResetWeight(reload);
			ResetWeight(inventoryOpen);
		}

		private void OnDestroy()
		{
			DestroyProfile(reload);
			DestroyProfile(inventoryOpen);
			if (_volumeRoot != null) Destroy(_volumeRoot);
		}

		private void Update()
		{
			Tick(reload, blurOnReload && Weapons != null && Weapons.IsReloading);
			Tick(inventoryOpen, blurOnInventory && inventory != null && inventory.IsOpen);
		}

		private void CreateVolume(BlurState state, string name, float priority)
		{
			VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
			profile.name = "ScreenBlur " + name;
			state.blur = profile.Add<ScreenBlur>(true);

			state.volume = _volumeRoot.AddComponent<Volume>();
			state.volume.isGlobal = true;
			state.volume.priority = priority;
			state.volume.sharedProfile = profile;
			state.volume.weight = 0f;
			Apply(state);
		}

		private static void Tick(BlurState state, bool active)
		{
			if (state.volume == null) return;

			float duration = active ? state.fadeIn : state.fadeOut;
			float step = duration > 0f ? Time.unscaledDeltaTime / duration : 1f;
			state.volume.weight = Mathf.MoveTowards(state.volume.weight, active ? 1f : 0f, step);
			Apply(state);
		}

		// каждый кадр — чтобы правки в инспекторе во время игры сразу были видны
		private static void Apply(BlurState state)
		{
			state.blur.intensity.value = state.intensity;
			state.blur.iterations.value = state.iterations;
			state.blur.spread.value = state.spread;
			state.blur.includeWeapon.value = state.includeWeapon;
			state.blur.pixelSize.value = state.pixelate ? state.pixelSize : 1;
			state.blur.colorSteps.value = state.limitColors ? state.colorSteps : 0;
		}

		private static void ResetWeight(BlurState state)
		{
			if (state.volume != null) state.volume.weight = 0f;
		}

		private static void DestroyProfile(BlurState state)
		{
			if (state.volume != null && state.volume.sharedProfile != null) Destroy(state.volume.sharedProfile);
		}
	}
}
