using UnityEngine;

namespace Player
{
	// Живёт на отдельной камере оружия (WeaponCamera — ребёнок MainCamera в сцене, отдельный
	// Culling Mask + крошечный Near Clip для anti-clip рендера, см. инструкцию по WeaponAimZoom).
	// WeaponAimZoom находит её через Instance вместо прямой ссылки в инспекторе — WeaponHolder
	// внутри Player.prefab не может сериализовать ссылку на объект сцены (та же причина, что и
	// у PickupPromptUI: Unity не умеет сохранить в prefab-ассет ссылку на конкретный экземпляр сцены).
	[RequireComponent(typeof(Camera))]
	public class WeaponCameraMarker : MonoBehaviour
	{
		public static Camera Instance { get; private set; }

		private void Awake()
		{
			Instance = GetComponent<Camera>();
		}

		private void OnDestroy()
		{
			if (Instance == GetComponent<Camera>()) Instance = null;
		}
	}
}
