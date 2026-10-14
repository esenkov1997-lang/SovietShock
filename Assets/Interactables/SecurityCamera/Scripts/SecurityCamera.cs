using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Localization;

namespace Interactables
{
	// Камера наблюдения в сцене. Вешается на ПОВОРОТНУЮ часть камеры (pivot): этот объект крутится, когда игрок
	// смотрит через камеру мышью, — модель "головы" камеры кладётся в него дочерним объектом и поворачивается
	// вместе с видом. Направление, в котором объект стоит в сцене (его синяя ось Z), — центр обзора; пределы
	// поворота отсчитываются от него и видны в сцене гизмо, когда объект выделен.
	//
	// Вид — обычная CinemachineCamera (дочерний объект View, создаётся сам при добавлении компонента, его можно
	// сдвинуть к объективу). Пока камера не активна, CinemachineCamera выключена. Activate() включает её с
	// высоким приоритетом — CinemachineBrain на MainCamera сам переключает основной вид сюда, а после
	// Deactivate() — обратно на камеру игрока. Переходы на камеры наблюдения и с них — всегда мгновенные (Cut),
	// независимо от Default Blend у Brain.
	//
	// Список камер для конкретного терминала задаётся в TerminalInstance на корне терминала.
	[DisallowMultipleComponent]
	public class SecurityCamera : MonoBehaviour
	{
		[Header("Info")]
		[Tooltip("Название камеры в списке терминала и на экране просмотра. Пусто — имя объекта")]
		[SerializeField] private LocalizedString displayName = new LocalizedString();
		[Tooltip("Необязательно: вторая строка в списке (место, сектор)")]
		[SerializeField] private LocalizedString location = new LocalizedString();

		[Header("View")]
		[Tooltip("CinemachineCamera, через которую смотрит игрок. Если не задано — ищется среди дочерних объектов. " +
			"Объектив (Field Of View, Near Clip) настраивается прямо в ней")]
		[SerializeField] private CinemachineCamera viewCamera;
		[Tooltip("Приоритет CinemachineCamera во время просмотра — должен быть выше, чем у камеры игрока")]
		[SerializeField] private int activePriority = 100;

		[Header("Rotation Limits (от направления в сцене)")]
		[Tooltip("Поворот влево/вправо от центра, в градусах (60 — сектор 120°)")]
		[Range(0f, 180f)]
		[SerializeField] private float yawLimit = 60f;
		[Tooltip("Насколько можно поднять взгляд от центра, в градусах")]
		[Range(0f, 89f)]
		[SerializeField] private float pitchUpLimit = 20f;
		[Tooltip("Насколько можно опустить взгляд от центра, в градусах")]
		[Range(0f, 89f)]
		[SerializeField] private float pitchDownLimit = 30f;

		[Header("Control")]
		[Tooltip("Множитель чувствительности относительно обычного обзора игрока (1 — так же, как MainCamera)")]
		[SerializeField] private float sensitivity = 1f;
		[Tooltip("Инерция поворота в секундах: камера догоняет мышь плавно, как на сервоприводе. 0 — без инерции")]
		[Range(0f, 0.5f)]
		[SerializeField] private float smoothTime = 0.1f;
		[Tooltip("При каждом входе в камеру возвращать её в центр. Выключено — камера остаётся повёрнутой туда, " +
			"куда её оставил игрок (и модель в мире тоже)")]
		[SerializeField] private bool resetOnActivate;

		// Камера, через которую сейчас смотрит игрок (null — смотрит своими глазами). Для HUD, звука и т.п.
		public static SecurityCamera Active { get; private set; }
		public static event Action<SecurityCamera> ActiveChanged;

		// виртуальные камеры всех камер наблюдения — для правила "переходы на них и с них мгновенные"
		private static readonly HashSet<ICinemachineCamera> SecurityViews = new HashSet<ICinemachineCamera>();
		private static bool _blendOverrideInstalled;

		// центр обзора в пространстве родителя: от него считаются пределы
		private float _baseYaw;
		private float _basePitch;
		private bool _baseCaptured;

		// смещение от центра: куда тянет мышь (target) и где камера сейчас (current); pitch > 0 — вниз, как у Unity
		private Vector2 _target;
		private Vector2 _current;
		private Vector2 _velocity;

		public LocalizedString DisplayName => displayName;
		public LocalizedString Location => location;
		public bool HasDisplayName => displayName != null && !displayName.IsEmpty;
		public bool HasLocation => location != null && !location.IsEmpty;
		public bool IsActive => Active == this;

		private void Reset()
		{
			// новая камера сразу готова к работе: дочерний View с CinemachineCamera чуть впереди pivot
			if (GetComponentInChildren<CinemachineCamera>(true) != null) return;

			GameObject view = new GameObject("View");
			view.transform.SetParent(transform, false);
			view.transform.localPosition = new Vector3(0f, 0f, 0.1f);
			viewCamera = view.AddComponent<CinemachineCamera>();
			viewCamera.Lens.FieldOfView = 70f;
			viewCamera.Lens.NearClipPlane = 0.05f;
#if UNITY_EDITOR
			UnityEditor.Undo.RegisterCreatedObjectUndo(view, "Create Security Camera View");
#endif
		}

		private void Awake()
		{
			if (viewCamera == null) viewCamera = GetComponentInChildren<CinemachineCamera>(true);
			if (viewCamera == null)
			{
				Debug.LogError($"{nameof(SecurityCamera)} на {name}: не найдена CinemachineCamera среди дочерних объектов — " +
					"через камеру нельзя будет смотреть. Удали и добавь компонент заново, он создаст View сам", this);
				return;
			}

			// до активации камера не участвует в выборе вида
			viewCamera.enabled = false;
			SecurityViews.Add(viewCamera);
			InstallBlendOverride();

			CaptureBase();
		}

		private void OnDestroy()
		{
			if (viewCamera != null) SecurityViews.Remove(viewCamera);
			if (IsActive) SetActive(null);
		}

		private void Update()
		{
			if (!IsActive && _current == _target) return;

			_current = smoothTime > 0f
				? Vector2.SmoothDamp(_current, _target, ref _velocity, smoothTime)
				: _target;
			// после выхода из камеры она плавно доезжает и останавливается — SmoothDamp сам точно не сойдётся
			if ((_current - _target).sqrMagnitude < 0.0001f)
			{
				_current = _target;
				_velocity = Vector2.zero;
			}
			ApplyRotation();
		}

		// ---------- Управление ----------

		// Переключает основной вид на эту камеру. Предыдущая активная камера (если была) выключается
		public void Activate()
		{
			if (viewCamera == null || IsActive) return;

			// предыдущую выключаем без события "вид вернулся к игроку" — это переключение между камерами
			if (Active != null) Active.DisableView();

			if (resetOnActivate)
			{
				_target = _current = _velocity = Vector2.zero;
				ApplyRotation();
			}

			viewCamera.Priority = activePriority;
			viewCamera.enabled = true;
			SetActive(this);
		}

		// Возвращает вид на камеру игрока (если эта камера активна)
		public void Deactivate()
		{
			if (!IsActive) return;

			DisableView();
			SetActive(null);
		}

		private void DisableView()
		{
			if (viewCamera != null) viewCamera.enabled = false;
		}

		// Вызывается каждый кадр, пока игрок смотрит через камеру. degrees — смещение взгляда в градусах в том же
		// виде, что у обзора игрока: x — вправо, y — вниз
		public void Look(Vector2 degrees)
		{
			_target.x = Mathf.Clamp(_target.x + degrees.x * sensitivity, -yawLimit, yawLimit);
			_target.y = Mathf.Clamp(_target.y + degrees.y * sensitivity, -pitchUpLimit, pitchDownLimit);
		}

		// Отключить вид с камер наблюдения, кто бы его ни включил (например, при смерти игрока)
		public static void ReleaseActive()
		{
			if (Active != null) Active.Deactivate();
		}

		// ---------- Внутреннее ----------

		private static void SetActive(SecurityCamera camera)
		{
			Active = camera;
			ActiveChanged?.Invoke(camera);
		}

		// Направление объекта в сцене — центр обзора. Крен (наклон вбок) не сохраняется: горизонт у камеры всегда
		// ровный, иначе поворот влево-вправо заваливал бы картинку
		private void CaptureBase()
		{
			Vector3 forward = transform.localRotation * Vector3.forward;
			_baseYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
			_basePitch = -Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
			_baseCaptured = true;
		}

		private void ApplyRotation()
		{
			if (!_baseCaptured) CaptureBase();
			float pitch = Mathf.Clamp(_basePitch + _current.y, -89f, 89f);
			transform.localRotation = Quaternion.Euler(pitch, _baseYaw + _current.x, 0f);
		}

		// Переходы на камеру наблюдения и обратно — мгновенные: плавный перелёт "сквозь стены" от терминала к
		// камере в другой комнате выглядел бы странно. Остальные переходы Cinemachine не трогаем
		private static void InstallBlendOverride()
		{
			if (_blendOverrideInstalled) return;
			_blendOverrideInstalled = true;

			CinemachineCore.GetBlendOverride -= CutToSecurityViews;
			CinemachineCore.GetBlendOverride += CutToSecurityViews;
		}

		private static CinemachineBlendDefinition CutToSecurityViews(ICinemachineCamera from, ICinemachineCamera to,
			CinemachineBlendDefinition defaultBlend, UnityEngine.Object owner)
		{
			bool involvesSecurityView = (from != null && SecurityViews.Contains(from)) || (to != null && SecurityViews.Contains(to));
			return involvesSecurityView
				? new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f)
				: defaultBlend;
		}

		// при выключенном Domain Reload статика переживает перезапуск Play Mode
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			Active = null;
			ActiveChanged = null;
			SecurityViews.Clear();
			_blendOverrideInstalled = false;
		}

		// ---------- Гизмо ----------

		private void OnDrawGizmosSelected()
		{
			// в игре центр — запомненное направление, в редакторе — текущее
			float baseYaw, basePitch;
			if (Application.isPlaying && _baseCaptured)
			{
				baseYaw = _baseYaw;
				basePitch = _basePitch;
			}
			else
			{
				Vector3 forward = transform.localRotation * Vector3.forward;
				baseYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
				basePitch = -Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
			}

			Quaternion parent = transform.parent != null ? transform.parent.rotation : Quaternion.identity;
			Vector3 origin = transform.position;
			const float length = 1.5f;
			const int segments = 16;

			Vector3 Direction(float yaw, float pitch) =>
				parent * Quaternion.Euler(Mathf.Clamp(basePitch + pitch, -89f, 89f), baseYaw + yaw, 0f) * Vector3.forward;

			// центр
			Gizmos.color = Color.cyan;
			Gizmos.DrawRay(origin, Direction(0f, 0f) * length);

			// рамка доступного обзора: дуги по верхнему и нижнему пределу + боковые границы
			Gizmos.color = new Color(1f, 0.6f, 0.1f);
			Vector3 prevTop = origin + Direction(-yawLimit, -pitchUpLimit) * length;
			Vector3 prevBottom = origin + Direction(-yawLimit, pitchDownLimit) * length;
			Gizmos.DrawLine(origin, prevTop);
			Gizmos.DrawLine(origin, prevBottom);
			Gizmos.DrawLine(prevTop, prevBottom);
			for (int i = 1; i <= segments; i++)
			{
				float yaw = Mathf.Lerp(-yawLimit, yawLimit, i / (float)segments);
				Vector3 top = origin + Direction(yaw, -pitchUpLimit) * length;
				Vector3 bottom = origin + Direction(yaw, pitchDownLimit) * length;
				Gizmos.DrawLine(prevTop, top);
				Gizmos.DrawLine(prevBottom, bottom);
				prevTop = top;
				prevBottom = bottom;
			}
			Gizmos.DrawLine(origin, prevTop);
			Gizmos.DrawLine(origin, prevBottom);
			Gizmos.DrawLine(prevTop, prevBottom);
		}
	}
}
