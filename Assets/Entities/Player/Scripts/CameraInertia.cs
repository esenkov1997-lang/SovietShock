using UnityEngine;

namespace StarterAssets
{
	// Инерция камеры — тот же расчёт, что у оружия (MovementInertia): голова как груз на пружине. При приземлении и
	// приседании камера проседает и затухающе возвращается, при старте/остановке чуть кивает. Значения по умолчанию
	// намного тише, чем у оружия: камера двигает весь кадр, сильное раскачивание укачивает.
	//
	// Вешается на PlayerCapsule рядом с FirstPersonController. Пишет в свои поля FirstPersonController
	// (InertiaCameraPositionOffset/RotationOffset) — с покачиванием шага, анимацией камеры оружия и подъёма
	// не конфликтует. LateUpdate до FirstPersonController.LateUpdate, который применяет смещения к камере
	[DefaultExecutionOrder(-60)]
	[RequireComponent(typeof(FirstPersonController))]
	public class CameraInertia : MonoBehaviour
	{
		[SerializeField] private MovementInertiaSettings settings = new MovementInertiaSettings
		{
			PitchPerAcceleration = 0.04f,
			RollPerAcceleration = 0.03f,
			PositionPerAcceleration = 0f,
			VerticalPitchPerAcceleration = 0.05f,
			VerticalPositionPerAcceleration = 0.0015f,
			MaxAcceleration = 30f,
			InputSmoothing = 8f,
			Stiffness = 80f,
			Damping = 12f,
			RotationStiffness = 60f,
			RotationDamping = 10f,
			ArcCoupling = 0f,
		};
		[Tooltip("Общая сила эффекта: 0 — выключен, 1 — как в настройках")]
		[Range(0f, 2f)]
		[SerializeField] private float strength = 1f;

		private FirstPersonController _movement;
		private readonly MovementInertia _inertia = new MovementInertia();

		private void Awake()
		{
			_movement = GetComponent<FirstPersonController>();
		}

		private void OnDisable()
		{
			if (_movement == null) return;
			_movement.InertiaCameraPositionOffset = Vector3.zero;
			_movement.InertiaCameraRotationOffset = Quaternion.identity;
		}

		private void LateUpdate()
		{
			_inertia.Update(_movement, settings, strength, Time.deltaTime, out Vector3 position, out Quaternion rotation);
			_movement.InertiaCameraPositionOffset = position;
			_movement.InertiaCameraRotationOffset = rotation;
		}
	}
}
