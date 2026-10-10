using UnityEngine;

namespace Enemies
{
	// Маршрут патруля — объект в сцене, дочерние объекты которого и есть точки маршрута,
	// в порядке иерархии (чтобы поменять порядок — перетащи их в Hierarchy). Маршрут — отдельный
	// объект, а не часть префаба врага: точки принадлежат уровню, и один префаб собаки можно
	// поставить на разные маршруты. Для точки с особыми настройками добавь ей PatrolPoint.
	public class PatrolRoute : MonoBehaviour
	{
		public enum RouteMode
		{
			Loop,      // 1 → 2 → 3 → 1 → ...
			PingPong   // 1 → 2 → 3 → 2 → 1 → ...
		}

		[Tooltip("Loop — по кругу, PingPong — туда и обратно")]
		[SerializeField] private RouteMode mode = RouteMode.Loop;
		[Tooltip("Сколько секунд стоять на каждой точке: случайно между X и Y. PatrolPoint на точке может переопределить")]
		[SerializeField] private Vector2 waitTimeRange = new Vector2(1f, 3f);

		public int Count => transform.childCount;

		public Transform GetPoint(int index) => transform.GetChild(index);

		public PatrolPoint GetSettings(int index) => GetPoint(index).GetComponent<PatrolPoint>();

		public float GetWaitTime(int index)
		{
			PatrolPoint settings = GetSettings(index);
			if (settings != null && settings.OverrideWaitTime) return settings.WaitTime;
			return Random.Range(waitTimeRange.x, waitTimeRange.y);
		}

		// ближайшая точка — с неё враг продолжает патруль после погони/поиска
		public int GetNearestIndex(Vector3 position)
		{
			int nearest = 0;
			float nearestDistance = float.MaxValue;
			for (int i = 0; i < Count; i++)
			{
				float distance = (GetPoint(i).position - position).sqrMagnitude;
				if (distance < nearestDistance)
				{
					nearestDistance = distance;
					nearest = i;
				}
			}
			return nearest;
		}

		// direction: +1 вперёд по списку, -1 назад (меняется только в PingPong)
		public int GetNextIndex(int index, ref int direction)
		{
			if (Count <= 1) return 0;

			if (mode == RouteMode.Loop)
				return (index + 1) % Count;

			int next = index + direction;
			if (next < 0 || next >= Count)
			{
				direction = -direction;
				next = index + direction;
			}
			return next;
		}

		private void OnValidate()
		{
			waitTimeRange.x = Mathf.Max(0f, waitTimeRange.x);
			waitTimeRange.y = Mathf.Max(waitTimeRange.x, waitTimeRange.y);
		}

#if UNITY_EDITOR
		// маршрут виден всегда: оранжевые точки с номерами, линии между ними, стрелка — направление взгляда на точке
		private void OnDrawGizmos()
		{
			int count = Count;
			if (count == 0) return;

			Gizmos.color = new Color(1f, 0.55f, 0f);
			for (int i = 0; i < count; i++)
			{
				Transform point = GetPoint(i);
				Gizmos.DrawSphere(point.position + Vector3.up * 0.1f, 0.15f);
				UnityEditor.Handles.Label(point.position + Vector3.up * 0.5f, (i + 1).ToString());

				PatrolPoint settings = point.GetComponent<PatrolPoint>();
				if (settings != null && settings.FaceDirection)
					Gizmos.DrawRay(point.position + Vector3.up * 0.1f, point.forward * 0.8f);

				bool isLast = i == count - 1;
				if (!isLast || mode == RouteMode.Loop)
				{
					Transform next = GetPoint((i + 1) % count);
					Gizmos.DrawLine(point.position + Vector3.up * 0.1f, next.position + Vector3.up * 0.1f);
				}
			}
		}
#endif
	}
}
