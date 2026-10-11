using UnityEngine;

namespace Enemies
{
	// Вешается на модель врага — объект с Animator. Unity вызывает OnAnimatorMove только у компонентов
	// на объекте с Animator; пока такой метод есть, Unity сама НЕ двигает объект по root motion,
	// а отдаёт перемещение root-кости за кадр (deltaPosition/deltaRotation) сюда. Мы пересылаем его
	// в EnemyMovement на корне — там оно применяется к корню с учётом NavMesh.
	[RequireComponent(typeof(Animator))]
	public class EnemyRootMotion : MonoBehaviour
	{
		private Animator _animator;
		private EnemyMovement _movement;

		private void Awake()
		{
			_animator = GetComponent<Animator>();
			_movement = GetComponentInParent<EnemyMovement>();

			if (_movement == null)
				Debug.LogWarning($"{name}: EnemyRootMotion не нашёл EnemyMovement у родителей — root motion не применяется", this);
			else if (!_movement.UseRootMotion)
				Debug.LogWarning($"{name}: на модели есть EnemyRootMotion, но у EnemyMovement выключен Use Root Motion — " +
					"перемещение из анимации игнорируется", this);
			if (_animator.avatar == null)
				Debug.LogWarning($"{name}: у Animator нет Avatar — Unity не знает root-кость, root motion будет нулевым. " +
					"Создай Avatar в настройках Rig модели и укажи Root Node", this);
		}

		private void OnAnimatorMove()
		{
			if (_movement != null) _movement.ApplyRootMotion(_animator.deltaPosition, _animator.deltaRotation);
		}
	}
}
