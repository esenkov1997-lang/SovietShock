using Player;
using Unity.Cinemachine;
using UnityEngine;

namespace Weapons
{
	// Меняет FOV Cinemachine-камеры во время прицеливания (ПКМ) — лерпит между базовым FOV (снятым
	// один раз при старте) и FirearmData.Aim.FOV текущего оружия. Использует тот же WeaponController.AimBlend,
	// которым уже управляется HandPosition/AimPosition (см. WeaponController.UpdateAimPose) — зум синхронен
	// с движением оружия без отдельного сглаживания. Для Melee AimBlend всегда 0, зум не включается.
	[RequireComponent(typeof(WeaponController))]
	public class WeaponAimZoom : MonoBehaviour
	{
		[Tooltip("Cinemachine-камера игрока. Если не задано — берётся первая найденная в сцене")]
		[SerializeField] private CinemachineCamera virtualCamera;
		[Tooltip("FOV рук и оружия (камеры оружия), независимый от FOV мира — ставь равным FOV камеры в Blender, " +
			"тогда руки всегда выглядят как при анимации, а FOV мира можно менять (хоть в настройках игрока). " +
			"В прицеле уменьшается в той же пропорции, что и FOV мира. 0 — как раньше: камера оружия повторяет FOV мира")]
		[SerializeField] private float viewModelFOV;

		private WeaponController _weaponController;
		private float _baseFOV;

		private void Awake()
		{
			_weaponController = GetComponent<WeaponController>();
		}

		private void Start()
		{
			if (virtualCamera == null) virtualCamera = FindFirstObjectByType<CinemachineCamera>();
			if (virtualCamera != null) _baseFOV = virtualCamera.Lens.FieldOfView;
		}

		private void LateUpdate()
		{
			if (virtualCamera == null) return;

			WeaponData data = _weaponController.CurrentWeaponData;
			float targetFOV = data is FirearmData firearm ? firearm.Aim.FOV : _baseFOV;
			float fov = Mathf.Lerp(_baseFOV, targetFOV, _weaponController.AimBlend);

			virtualCamera.Lens.FieldOfView = fov;

			// WeaponHolder живёт внутри Player.prefab и не может хранить прямую ссылку на WeaponCamera —
			// та живёт в сцене (ребёнок MainCamera). Находим через маркер-синглтон вместо инспектора,
			// см. WeaponCameraMarker — тот же приём, что и у PickupPromptUI
			if (WeaponCameraMarker.Instance != null)
			{
				float zoom = _baseFOV > 0f ? fov / _baseFOV : 1f;
				WeaponCameraMarker.Instance.fieldOfView = viewModelFOV > 0f ? viewModelFOV * zoom : fov;
			}
		}
	}
}
