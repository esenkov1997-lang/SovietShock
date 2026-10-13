using System.Collections.Generic;
using Enemies;
using UnityEngine;

namespace HUD
{
	// Индикаторы обнаружения над врагами — метки интерфейса шлема. Вешается на объект внутри Canvas HUD
	// (растянутый на весь экран), сам создаёт по квадрату на каждого врага и каждый кадр ставит его над головой
	// врага в экранных координатах. Квадрат заполняется по осведомлённости врага (EnemyPerception.Awareness):
	// жёлтый — что-то подозревает, полный красный — заметил. Спокойный враг метки не имеет.
	// Заметивший враг показывает красную метку Hide After Alerted секунд и прячет её, пока видит игрока; потерял — метка снова видна.
	//
	// Метка рисуется в HUD, а не объектом в мире: всегда одного размера, не загораживается стенами, не зависит
	// от освещения и эффектов камеры, а при помехах сбоит вместе с остальным интерфейсом шлема.
	[RequireComponent(typeof(RectTransform))]
	public class DetectionIndicatorUI : MonoBehaviour
	{
		[Header("Вид")]
		[Tooltip("Размер квадрата, px")]
		[SerializeField] private float size = 18f;
		[Tooltip("Цвет заливки, пока враг подозревает")]
		[SerializeField] private Color suspiciousColor = new Color(1f, 0.85f, 0.2f, 1f);
		[Tooltip("Цвет, когда враг заметил игрока (квадрат полный)")]
		[SerializeField] private Color alertedColor = new Color(1f, 0.25f, 0.15f, 1f);
		[Tooltip("Цвет пустой части квадрата")]
		[SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.4f);
		[Tooltip("Цвет рамки")]
		[SerializeField] private Color borderColor = new Color(1f, 1f, 1f, 0.8f);
		[Tooltip("Толщина рамки, px")]
		[SerializeField] private float borderThickness = 2f;

		[Header("Положение")]
		[Tooltip("На сколько метров выше глаз врага висит метка")]
		[SerializeField] private float heightAboveEyes = 0.5f;
		[Tooltip("Дальше этого расстояния (м) от камеры метки не показываются. 0 — без ограничения")]
		[SerializeField] private float maxDistance = 30f;
		[Tooltip("Ниже этой осведомлённости метка не показывается — чтобы не мигала на случайных долях секунды")]
		[Range(0f, 0.2f)]
		[SerializeField] private float showThreshold = 0.02f;

		[Header("После обнаружения")]
		[Tooltip("Сколько секунд полная красная метка видна после того, как враг заметил игрока, — потом прячется, " +
			"пока враг его видит или \"ведёт\". Потерял — метка возвращается и показывает спадающую осведомлённость. " +
			"0 — не прятать")]
		[SerializeField] private float hideAfterAlerted = 1.5f;

		private readonly Dictionary<EnemyPerception, DetectionMarkerGraphic> _markers = new Dictionary<EnemyPerception, DetectionMarkerGraphic>();
		// с какого момента враг в полном обнаружении — для Hide After Alerted
		private readonly Dictionary<EnemyPerception, float> _alertedSince = new Dictionary<EnemyPerception, float>();
		private readonly List<EnemyPerception> _stale = new List<EnemyPerception>();
		private RectTransform _rect;
		private Canvas _canvas;

		private void Awake()
		{
			_rect = (RectTransform)transform;
			_canvas = GetComponentInParent<Canvas>();
		}

		private void LateUpdate()
		{
			Camera view = Camera.main;
			if (view == null || _canvas == null) return;

			// враги, которых больше нет (погибли, удалены) — убираем их метки
			_stale.Clear();
			foreach (EnemyPerception perception in _markers.Keys)
				if (perception == null || !perception.isActiveAndEnabled) _stale.Add(perception);
			foreach (EnemyPerception perception in _stale)
			{
				if (_markers[perception] != null) Destroy(_markers[perception].gameObject);
				_markers.Remove(perception);
				_alertedSince.Remove(perception);
			}

			foreach (EnemyPerception perception in EnemyPerception.All)
				UpdateMarker(perception, view);
		}

		private void UpdateMarker(EnemyPerception perception, Camera view)
		{
			DetectionMarkerGraphic marker = GetMarker(perception);

			Vector3 worldPoint = perception.EyePosition + Vector3.up * heightAboveEyes;
			Vector3 screenPoint = view.WorldToScreenPoint(worldPoint);

			// считаем каждый кадр, а не только когда метка видна: иначе после мгновенного "забывания" (враг сдался)
			// осталась бы старая отметка времени, и при следующем обнаружении метка спряталась бы сразу
			bool hiddenAfterAlert = HiddenAfterAlert(perception);

			bool visible = !perception.IgnoresPlayer &&
				perception.Awareness >= showThreshold &&
				!hiddenAfterAlert &&
				screenPoint.z > 0f && // не за спиной
				(maxDistance <= 0f || screenPoint.z <= maxDistance);

			if (marker.gameObject.activeSelf != visible) marker.gameObject.SetActive(visible);
			if (!visible) return;

			// экранная точка → координаты внутри Canvas (для Overlay камера не нужна, для Screen Space - Camera — её камера)
			Camera canvasCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
			if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, screenPoint, canvasCamera, out Vector2 local))
				marker.rectTransform.anchoredPosition = local;

			marker.Fill = perception.Awareness;
			marker.color = perception.IsAlerted ? alertedColor : suspiciousColor;
		}

		// враг давно в полном обнаружении — метка уже "отработала" и прячется, пока он не потеряет игрока
		private bool HiddenAfterAlert(EnemyPerception perception)
		{
			if (!perception.IsAlerted)
			{
				_alertedSince.Remove(perception); // потерял игрока — в следующий раз отсчёт заново
				return false;
			}

			if (!_alertedSince.TryGetValue(perception, out float since))
			{
				since = Time.time;
				_alertedSince[perception] = since;
			}
			return hideAfterAlerted > 0f && Time.time - since >= hideAfterAlerted;
		}

		private DetectionMarkerGraphic GetMarker(EnemyPerception perception)
		{
			if (_markers.TryGetValue(perception, out DetectionMarkerGraphic marker) && marker != null) return marker;

			GameObject markerObject = new GameObject($"Detection_{perception.name}", typeof(RectTransform));
			markerObject.layer = gameObject.layer; // слой HUD — чтобы попасть в камеру HUD
			RectTransform markerRect = (RectTransform)markerObject.transform;
			markerRect.SetParent(_rect, false);
			markerRect.anchorMin = markerRect.anchorMax = new Vector2(0.5f, 0.5f);
			markerRect.pivot = new Vector2(0.5f, 0f); // нижний край — над головой, квадрат растёт вверх
			markerRect.sizeDelta = new Vector2(size, size);

			marker = markerObject.AddComponent<DetectionMarkerGraphic>();
			marker.raycastTarget = false;
			marker.Configure(backgroundColor, borderColor, borderThickness);
			markerObject.SetActive(false);

			_markers[perception] = marker;
			return marker;
		}
	}
}
