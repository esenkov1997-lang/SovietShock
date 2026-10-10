using System;
using UnityEngine;

namespace Enemies
{
	// Вешается на тот же объект, где Animator (модель врага, например DobermanRig).
	// Animation Event из клипа может вызвать метод только у компонентов на объекте с Animator, а логика
	// врага живёт на корне — этот компонент принимает события и пересылает их тем, кто подписан
	// (EnemyAttack и т.п.). Тот же принцип, что WeaponAnimationEvents у оружия.
	public class EnemyAnimationEvents : MonoBehaviour
	{
		public event Action AttackHitReceived;
		public event Action AttackEndReceived;

		// Animation Event "AttackHit" — на кадр клипа атаки, где зубы/лапа касаются цели. Тут наносится урон
		public void AttackHit()
		{
			AttackHitReceived?.Invoke();
		}

		// Animation Event "AttackEnd" — ближе к концу клипа атаки, когда враг снова готов двигаться
		public void AttackEnd()
		{
			AttackEndReceived?.Invoke();
		}
	}
}
