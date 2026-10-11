using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Enemies
{
	// Поиск укрытия от угрозы (игрока) — небольшая "система запросов" в духе EQS из Unreal:
	//   1. Генерация кандидатов: точки на кольцах вокруг врага + маркеры CoverPoint поблизости.
	//   2. Фильтры, от дешёвых к дорогим: точка на NavMesh → расстояние до угрозы в допустимых пределах →
	//      угроза её НЕ видит (лучи от глаз игрока к телу врага в этой точке) → путь существует, не слишком
	//      длинный и не проходит мимо угрозы.
	//   3. Оценка с весами из инспектора: короче путь, дальше от угрозы, бонус маркерам. Побеждает лучшая.
	//
	// Дорогой расчёт пути делается только для нескольких лучших по предварительной оценке (Max Path Checks).
	// Запрос выполняется не каждый кадр, а лишь когда мозг просит (начало отступления, укрытие раскрыто).
	public class CoverFinder : MonoBehaviour
	{
		private enum CandidateResult
		{
			Rejected,   // не на NavMesh / не по расстоянию / недостижимо / путь мимо угрозы
			Visible,    // угроза видит точку
			Hidden,     // подходящее укрытие
			Chosen      // выбранное
		}

		private struct Candidate
		{
			public Vector3 Position;
			public bool IsMarker;
			public float Score;
			public CandidateResult Result;
		}

		[Header("Где искать")]
		[Tooltip("Радиусы колец (м) вокруг врага, на которых генерируются точки-кандидаты")]
		[SerializeField] private float[] ringRadii = { 4f, 7f, 10f };
		[Tooltip("Сколько точек на каждом кольце. Больше — точнее, но дороже (на каждую — 2 луча)")]
		[Range(4, 24)]
		[SerializeField] private int pointsPerRing = 10;
		[Tooltip("Маркеры CoverPoint в этом радиусе (м) от врага тоже становятся кандидатами")]
		[SerializeField] private float coverPointSearchRadius = 15f;

		[Header("Фильтры")]
		[Tooltip("Укрытие должно быть не ближе этого (м) к игроку")]
		[SerializeField] private float minDistanceFromThreat = 5f;
		[Tooltip("И не дальше этого (м) — чтобы враг не убегал на другой конец уровня и мог вернуться в бой")]
		[SerializeField] private float maxDistanceFromThreat = 20f;
		[Tooltip("Максимальная длина пути (м) до укрытия")]
		[SerializeField] private float maxPathLength = 18f;
		[Tooltip("Путь до укрытия не должен подходить к игроку ближе этого (м) — иначе враг \"прячется\" через его ноги")]
		[SerializeField] private float pathSafetyRadius = 3f;
		[Tooltip("Расчёт пути — самая дорогая проверка, делается только для стольких лучших кандидатов")]
		[SerializeField] private int maxPathChecks = 6;

		[Header("Скрытность точки")]
		[Tooltip("Высоты (м над полом) точек тела врага, которые должны быть скрыты от глаз игрока. " +
			"Для собаки — примерно спина и голова. Все точки должны быть закрыты")]
		[SerializeField] private float[] bodyCheckHeights = { 0.35f, 0.7f };
		[Tooltip("Слои, которые закрывают обзор игроку (стены, ящики). Стекло и сетки лучше исключить")]
		[SerializeField] private LayerMask coverLayers = ~(1 << 2);

		[Header("Оценка")]
		[Tooltip("Вес короткого пути: чем выше, тем охотнее враг выбирает ближайшее укрытие")]
		[SerializeField] private float pathLengthWeight = 1f;
		[Tooltip("Вес расстояния от игрока: чем выше, тем охотнее враг уходит подальше")]
		[SerializeField] private float threatDistanceWeight = 0.7f;
		[Tooltip("Бонус маркерам CoverPoint — чтобы враг предпочитал места, которые ты разметил")]
		[SerializeField] private float coverPointBonus = 0.5f;

		[Header("Отладка")]
		[Tooltip("Показать кандидатов последнего запроса при выделенном враге: серый — отброшен, красный — виден игроку, " +
			"жёлтый — подходящее укрытие, зелёный — выбранное")]
		[SerializeField] private bool drawDebug = true;

		private const int RaycastBufferSize = 16;

		private readonly RaycastHit[] _hits = new RaycastHit[RaycastBufferSize];
		private readonly List<Candidate> _candidates = new List<Candidate>();
		private NavMeshAgent _agent;
		private NavMeshPath _path;

		private void Awake()
		{
			_agent = GetComponent<NavMeshAgent>();
			_path = new NavMeshPath();
		}

		// threat — корень игрока (его коллайдеры не считаются укрытием), threatEye — откуда он смотрит
		public bool TryFindCover(Transform threat, Vector3 threatEye, out Vector3 cover)
		{
			cover = transform.position;
			_candidates.Clear();
			int areaMask = _agent != null ? _agent.areaMask : NavMesh.AllAreas;

			GenerateCandidates(areaMask);

			// дешёвые фильтры и предварительная оценка (по прямой вместо пути)
			for (int i = 0; i < _candidates.Count; i++)
			{
				Candidate candidate = _candidates[i];
				float threatDistance = HorizontalDistance(candidate.Position, threat.position);

				if (threatDistance < minDistanceFromThreat || threatDistance > maxDistanceFromThreat)
					candidate.Result = CandidateResult.Rejected;
				else if (!IsHiddenFrom(candidate.Position, threat, threatEye))
					candidate.Result = CandidateResult.Visible;
				else
				{
					candidate.Result = CandidateResult.Hidden;
					candidate.Score = Score(Vector3.Distance(transform.position, candidate.Position), threatDistance, candidate.IsMarker);
				}

				_candidates[i] = candidate;
			}

			// дорогая проверка пути — только для лучших по предварительной оценке
			float currentThreatDistance = HorizontalDistance(transform.position, threat.position);
			// враг обычно убегает прямо от игрока, стоя вплотную, — путь не может сразу начинаться дальше Safety Radius
			float safetyRadius = Mathf.Min(pathSafetyRadius, currentThreatDistance * 0.9f);

			int bestIndex = -1;
			float bestScore = float.MinValue;
			for (int checks = 0; checks < maxPathChecks; checks++)
			{
				int index = FindBestUncheckedHidden();
				if (index < 0) break;

				Candidate candidate = _candidates[index];
				if (!TryGetSafePathLength(candidate.Position, threat.position, safetyRadius, areaMask, out float pathLength))
				{
					candidate.Result = CandidateResult.Rejected;
				}
				else
				{
					candidate.Score = Score(pathLength, HorizontalDistance(candidate.Position, threat.position), candidate.IsMarker);
					if (candidate.Score > bestScore)
					{
						bestScore = candidate.Score;
						bestIndex = index;
					}
					candidate.Result = CandidateResult.Chosen; // временно: "проверен и годен", ниже поправим
				}
				_candidates[index] = candidate;
			}

			// все проверенные и годные помечены Chosen — оставляем так только победителя
			for (int i = 0; i < _candidates.Count; i++)
			{
				if (_candidates[i].Result != CandidateResult.Chosen || i == bestIndex) continue;
				Candidate candidate = _candidates[i];
				candidate.Result = CandidateResult.Hidden;
				_candidates[i] = candidate;
			}

			if (bestIndex < 0) return false;
			cover = _candidates[bestIndex].Position;
			return true;
		}

		// Скрыта ли точка на полу от глаз угрозы: все контрольные точки тела над ней закрыты препятствиями.
		// Используется и для проверки "не раскрыто ли текущее укрытие".
		public bool IsHiddenFrom(Vector3 floorPoint, Transform threat, Vector3 threatEye)
		{
			foreach (float height in bodyCheckHeights)
			{
				if (IsLineClear(threatEye, floorPoint + Vector3.up * height, threat)) return false;
			}
			return true;
		}

		private void GenerateCandidates(int areaMask)
		{
			Vector3 origin = transform.position;
			// случайный поворот колец — повторный запрос с того же места даёт немного другие точки
			float angleOffset = Random.Range(0f, 360f);

			foreach (float radius in ringRadii)
			{
				for (int i = 0; i < pointsPerRing; i++)
				{
					float angle = (angleOffset + i * 360f / pointsPerRing) * Mathf.Deg2Rad;
					Vector3 raw = origin + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
					if (NavMesh.SamplePosition(raw, out NavMeshHit hit, 1.5f, areaMask))
						_candidates.Add(new Candidate { Position = hit.position });
				}
			}

			float markerRadiusSqr = coverPointSearchRadius * coverPointSearchRadius;
			foreach (CoverPoint marker in CoverPoint.All)
			{
				if ((marker.transform.position - origin).sqrMagnitude > markerRadiusSqr) continue;
				if (NavMesh.SamplePosition(marker.transform.position, out NavMeshHit hit, 1f, areaMask))
					_candidates.Add(new Candidate { Position = hit.position, IsMarker = true });
			}
		}

		private int FindBestUncheckedHidden()
		{
			int best = -1;
			for (int i = 0; i < _candidates.Count; i++)
			{
				if (_candidates[i].Result != CandidateResult.Hidden) continue;
				if (best < 0 || _candidates[i].Score > _candidates[best].Score) best = i;
			}
			return best;
		}

		// Путь существует целиком, не длиннее Max Path Length и ни на одном отрезке не подходит к угрозе ближе safetyRadius
		private bool TryGetSafePathLength(Vector3 destination, Vector3 threatPosition, float safetyRadius, int areaMask, out float length)
		{
			length = 0f;
			if (!NavMesh.CalculatePath(transform.position, destination, areaMask, _path)) return false;
			if (_path.status != NavMeshPathStatus.PathComplete) return false;

			Vector3[] corners = _path.corners;
			for (int i = 1; i < corners.Length; i++)
			{
				length += Vector3.Distance(corners[i - 1], corners[i]);
				if (length > maxPathLength) return false;
				if (HorizontalDistanceToSegment(threatPosition, corners[i - 1], corners[i]) < safetyRadius) return false;
			}
			return true;
		}

		private float Score(float pathLength, float threatDistance, bool isMarker)
		{
			float shortPath = 1f - Mathf.Clamp01(pathLength / maxPathLength);
			float farFromThreat = Mathf.InverseLerp(minDistanceFromThreat, maxDistanceFromThreat, threatDistance);
			return shortPath * pathLengthWeight + farFromThreat * threatDistanceWeight + (isMarker ? coverPointBonus : 0f);
		}

		// Свободен ли отрезок от препятствий. Коллайдеры самого врага и угрозы препятствием не считаются.
		private bool IsLineClear(Vector3 from, Vector3 to, Transform ignoreRoot)
		{
			Vector3 direction = to - from;
			float distance = direction.magnitude;
			if (distance < 0.001f) return true;

			int count = Physics.RaycastNonAlloc(from, direction / distance, _hits, distance, coverLayers, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < count; i++)
			{
				Transform hit = _hits[i].transform;
				if (hit.IsChildOf(transform)) continue;
				if (ignoreRoot != null && hit.IsChildOf(ignoreRoot)) continue;
				return false;
			}
			return true;
		}

		private static float HorizontalDistance(Vector3 a, Vector3 b)
		{
			a.y = 0f;
			b.y = 0f;
			return Vector3.Distance(a, b);
		}

		private static float HorizontalDistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
		{
			point.y = a.y = b.y = 0f;
			Vector3 ab = b - a;
			float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude) : 0f;
			return Vector3.Distance(point, a + ab * t);
		}

		private void OnDrawGizmosSelected()
		{
			if (!drawDebug) return;

			foreach (Candidate candidate in _candidates)
			{
				switch (candidate.Result)
				{
					case CandidateResult.Rejected: Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.6f); break;
					case CandidateResult.Visible: Gizmos.color = Color.red; break;
					case CandidateResult.Hidden: Gizmos.color = Color.yellow; break;
					case CandidateResult.Chosen: Gizmos.color = Color.green; break;
				}

				float size = candidate.Result == CandidateResult.Chosen ? 0.35f : candidate.IsMarker ? 0.22f : 0.12f;
				Gizmos.DrawSphere(candidate.Position + Vector3.up * 0.1f, size);
			}
		}
	}
}
