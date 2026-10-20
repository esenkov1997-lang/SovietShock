using StarterAssets;
using UnityEngine;

namespace Weapons
{
	// Вешается на кость/локатор камеры внутри рига оружия (та же иерархия и те же клипы, что и у рук/оружия —
	// отдельный Animator Controller под камеру не нужен). Каждый кадр читает, насколько эта кость отклонилась
	// от своей исходной позы, и кладёт это отклонение в FirstPersonController.ExtraPositionOffset/ExtraRotationOffset,
	// который уже сам аддитивно применяет их к реальной камере — вместо того чтобы двигать камеру напрямую.
	//
	// DefaultExecutionOrder гарантирует, что мы обновим смещение до того, как FirstPersonController.LateUpdate()
	// его прочитает — иначе порядок двух LateUpdate() на разных объектах ничем не гарантирован.
	[DefaultExecutionOrder(-100)]
	public class WeaponCameraAnchor : MonoBehaviour
	{
		// Точка отсчёта — VisualRoot, а не сам WeaponHolder: именно VisualRoot несёт взгляд игрока,
		// HandPosition/AimPosition, sway и bob (см. WeaponController/ProceduralWeaponAnimation).
		// Считая позу кости относительно VisualRoot, мы вычитаем всё это разом, и в смещение попадает
		// только то, что реально анимирует сама кость внутри клипа (например, отдача при выстреле)
		private Transform _visualRoot;
		private FirstPersonController _movement;

		private Vector3 _restLocalPosition;
		private Quaternion _restLocalRotation;
		private bool _restCaptured;

		private void Start()
		{
			WeaponController weaponController = GetComponentInParent<WeaponController>();
			_visualRoot = weaponController != null ? weaponController.VisualRoot : transform.root;
			_movement = GetComponentInParent<FirstPersonController>();

			// "покой" НЕ снимаем здесь: Start() выполняется до того, как Animator вообще
			// хоть раз проиграл позу этого кадра (Update -> Animator -> LateUpdate), так что
			// кость ещё в позе импорта (бинд-поза), а не в реальной Idle-позе клипа. Если бы мы
			// сняли "покой" тут, разница с бинд-позой навсегда осела бы в ExtraPositionOffset —
			// это и выглядит как "камера уехала назад", см. первый LateUpdate ниже
		}

		private void LateUpdate()
		{
			if (_movement == null || _visualRoot == null) return;

			// первый LateUpdate этого объекта уже идёт после того, как Animator отработал хотя бы
			// один раз в этом кадре — самый ранний момент, когда текущая поза кости реальна
			if (!_restCaptured)
			{
				_restLocalPosition = _visualRoot.InverseTransformPoint(transform.position);
				_restLocalRotation = Quaternion.Inverse(_visualRoot.rotation) * transform.rotation;
				_restCaptured = true;
				return;
			}

			Vector3 currentLocalPosition = _visualRoot.InverseTransformPoint(transform.position);
			Quaternion currentLocalRotation = Quaternion.Inverse(_visualRoot.rotation) * transform.rotation;

			// += (not =): CameraHeadBob also writes ExtraPositionOffset, in Update() earlier this same
			// frame (Unity always finishes every Update() before any LateUpdate() runs), so this layers
			// the bone's recoil delta on top of that frame's fresh bob value instead of stomping it.
			// No cross-frame accumulation either, since CameraHeadBob resets the field every Update().
			_movement.ExtraPositionOffset += currentLocalPosition - _restLocalPosition;
			_movement.ExtraRotationOffset = Quaternion.Inverse(_restLocalRotation) * currentLocalRotation;
		}

		private void OnDisable()
		{
			// не оставлять камеру смещённой, если оружие сменили/объект выключили посреди анимации
			if (_movement == null) return;
			_movement.ExtraPositionOffset = Vector3.zero;
			_movement.ExtraRotationOffset = Quaternion.identity;
		}
	}
}
