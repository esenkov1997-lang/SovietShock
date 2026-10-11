using System.Collections.Generic;
using UnityEngine;

namespace Enemies
{
	// Маркер укрытия, расставленный дизайнером: "здесь хорошо прятаться" — за ящиком, колонной, в нише.
	// Ставится прямо на пол. CoverFinder добавляет такие точки к автоматически найденным и даёт им бонус
	// к оценке, но всё равно проверяет, скрыта ли точка от игрока прямо сейчас: укрытие за ящиком
	// не работает, если игрок обошёл ящик. Без маркеров CoverFinder тоже работает — они лишь подсказка.
	public class CoverPoint : MonoBehaviour
	{
		private static readonly List<CoverPoint> _all = new List<CoverPoint>();

		// все активные маркеры сцены — CoverFinder перебирает их без поиска по сцене
		public static IReadOnlyList<CoverPoint> All => _all;

		private void OnEnable()
		{
			_all.Add(this);
		}

		private void OnDisable()
		{
			_all.Remove(this);
		}

		private void OnDrawGizmos()
		{
			Gizmos.color = new Color(0.2f, 0.9f, 0.4f);
			Vector3 p = transform.position;
			Gizmos.DrawWireCube(p + Vector3.up * 0.25f, new Vector3(0.5f, 0.5f, 0.5f));
			Gizmos.DrawLine(p, p + Vector3.up * 0.8f);
		}
	}
}
