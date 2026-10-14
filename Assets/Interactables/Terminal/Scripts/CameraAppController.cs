using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Interactables
{
	// Модуль "Камеры". Вешается на Camera_Panel (или любой объект терминала) и назначается в поле app окна Camera
	// у TerminalMainController. Список камер у каждого терминала свой — задаётся в TerminalInstance на корне
	// терминала (камеры — объекты сцены с компонентом SecurityCamera).
	//
	// Два режима:
	//   Список    — ↑/↓ (W/S) выбор камеры, Enter (или клавиша взаимодействия) — смотреть через неё,
	//               Escape — выход в меню терминала
	//   Просмотр  — основной вид переключён на камеру (см. SecurityCamera.Activate). Мышь (действие Look из
	//               Input System, с той же чувствительностью, что у игрока) поворачивает камеру в её пределах,
	//               ←/→ (A/D) — предыдущая/следующая камера, Escape — назад к списку
	//
	// Мир в это время продолжает жить: игрок стоит у терминала, и его могут атаковать.
	public class CameraAppController : TerminalApp
	{
		[Header("Data")]
		[Tooltip("Камеры этого терминала. Обычно задаются в TerminalInstance на корне терминала — тогда здесь " +
			"оставь пусто (префаб не может ссылаться на объекты сцены)")]
		[SerializeField] private SecurityCamera[] cameras = new SecurityCamera[0];

		[Header("List")]
		[Tooltip("Панель модуля внутри FullScreen_Window — включается при открытии")]
		[SerializeField] private GameObject panel;
		[Tooltip("Контейнер списка с Vertical Layout Group")]
		[SerializeField] private RectTransform listContainer;
		[Tooltip("Префаб Item_Tab (с компонентом ItemTabUI) из окна Project")]
		[SerializeField] private ItemTabUI itemTabPrefab;
		[Tooltip("Необязательно: объект с текстом \"Нет доступных камер\" — показывается, если список пуст")]
		[SerializeField] private GameObject emptyListObject;
		[Tooltip("↑ на первой камере переходит на последнюю и наоборот (и ←/→ при просмотре)")]
		[SerializeField] private bool wrapNavigation = true;

		[Header("View")]
		[Tooltip("Действие обзора игрока (StarterAssets → Player/Look). Мышь и геймпад поворачивают камеру " +
			"с теми же настройками, что и обычный обзор")]
		[SerializeField] private InputActionReference lookAction;
		[Tooltip("Необязательно: оверлей поверх вида с камеры (Canvas в режиме Screen Space - Overlay) — " +
			"включён, пока игрок смотрит через камеру")]
		[SerializeField] private GameObject viewOverlay;
		[Tooltip("Необязательно: текст на оверлее с названием текущей камеры")]
		[SerializeField] private TMP_Text viewCameraName;

		[Header("Events")]
		[Tooltip("Игрок начал смотреть через камеры (например, спрятать HUD)")]
		public UnityEvent OnViewStarted;
		[Tooltip("Игрок вернулся к экрану терминала")]
		public UnityEvent OnViewEnded;

		private readonly List<SecurityCamera> _available = new List<SecurityCamera>();
		private readonly List<ItemTabUI> _tabs = new List<ItemTabUI>();
		private int _selected;
		private bool _viewing;

		public override int ItemCount => CountCameras();

		// Для TerminalInstance на корне префаба: у каждого терминала в уровне свои камеры
		public SecurityCamera[] Cameras
		{
			get => cameras;
			set => cameras = value ?? new SecurityCamera[0];
		}

		private void OnDisable()
		{
			// терминал выключили/уничтожили посреди просмотра — не оставляем игрока смотреть через камеру
			ExitView();
		}

		// ---------- TerminalApp ----------

		public override void Open()
		{
			if (panel != null) panel.SetActive(true);
			if (viewOverlay != null) viewOverlay.SetActive(false);

			CollectCameras();
			SpawnTabs();
			if (emptyListObject != null) emptyListObject.SetActive(_available.Count == 0);

			// возвращаемся к той камере, что была выбрана в прошлый раз
			Select(Mathf.Clamp(_selected, 0, Mathf.Max(0, _available.Count - 1)), force: true);
		}

		public override void Close()
		{
			ExitView();
			ClearTabs();
			_available.Clear();

			if (emptyListObject != null) emptyListObject.SetActive(false);
			if (panel != null) panel.SetActive(false);
		}

		public override void HandleInput()
		{
			if (_available.Count == 0) return;

			if (_viewing)
			{
				UpdateView();
				return;
			}

			if (TerminalMainController.UpPressed()) Step(-1);
			else if (TerminalMainController.DownPressed()) Step(+1);
			else if (TerminalMainController.SubmitPressed()) EnterView();
		}

		// Escape при просмотре — назад к списку камер, а не выход из модуля
		public override bool HandleBack()
		{
			if (!_viewing) return false;

			ExitView();
			if (Audio != null) Audio.PlayBack();
			return true;
		}

		// ---------- Список ----------

		private int CountCameras()
		{
			int count = 0;
			foreach (SecurityCamera camera in cameras)
			{
				if (camera != null) count++;
			}
			return count;
		}

		// пустые слоты (камеру удалили со сцены) просто пропускаем
		private void CollectCameras()
		{
			_available.Clear();
			foreach (SecurityCamera camera in cameras)
			{
				if (camera != null) _available.Add(camera);
			}
		}

		private void SpawnTabs()
		{
			ClearTabs();
			if (itemTabPrefab == null || listContainer == null)
			{
				Debug.LogError($"{nameof(CameraAppController)} на {name}: не назначен Item Tab Prefab или List Container", this);
				return;
			}

			// та же ловушка, что у почты: образец из самого списка вместо префаба из окна Project
			if (itemTabPrefab.transform.IsChildOf(listContainer))
			{
				Debug.LogError($"{nameof(CameraAppController)} на {name}: в Item Tab Prefab назначен объект из списка. " +
					"Перетащи сюда префаб Item_Tab из окна Project (Assets/Interactables/Terminal)", this);
				return;
			}

			for (int i = 0; i < _available.Count; i++)
			{
				SecurityCamera camera = _available[i];
				ItemTabUI tab = Instantiate(itemTabPrefab, listContainer);
				if (camera.HasDisplayName) tab.Setup(i, camera.DisplayName, camera.HasLocation ? camera.Location : null);
				else tab.Setup(i, camera.name);
				tab.Clicked += HandleTabClicked;
				_tabs.Add(tab);
			}
		}

		private void HandleTabClicked(ItemTabUI tab) => Select(tab.Index);

		private void Step(int direction)
		{
			int previous = _selected;
			Select(WrapIndex(_selected + direction));
			if (_selected != previous && Audio != null) Audio.PlayNavigate();
		}

		private int WrapIndex(int index)
		{
			int count = _available.Count;
			return wrapNavigation ? (index % count + count) % count : Mathf.Clamp(index, 0, count - 1);
		}

		private void Select(int index, bool force = false)
		{
			if (_available.Count == 0) return;

			index = Mathf.Clamp(index, 0, _available.Count - 1);
			if (index == _selected && !force) return;

			_selected = index;
			for (int i = 0; i < _tabs.Count; i++)
			{
				_tabs[i].SetSelected(i == _selected);
			}
		}

		// ---------- Просмотр ----------

		private void EnterView()
		{
			if (_selected < 0 || _selected >= _available.Count) return;

			_viewing = true;
			SecurityCamera.ActiveChanged += HandleActiveCameraChanged;
			if (Audio != null) Audio.PlayConfirm();

			ShowCamera(_available[_selected]);
			if (viewOverlay != null) viewOverlay.SetActive(true);
			OnViewStarted?.Invoke();
		}

		private void ExitView()
		{
			if (!_viewing) return;

			_viewing = false;
			SecurityCamera.ActiveChanged -= HandleActiveCameraChanged;
			SecurityCamera.ReleaseActive();

			if (viewOverlay != null) viewOverlay.SetActive(false);
			OnViewEnded?.Invoke();
		}

		private void UpdateView()
		{
			// ←/→ — соседняя камера; в этом кадре мышь уже не крутит, чтобы рывок не достался новой камере
			if (TerminalMainController.LeftPressed()) { SwitchCamera(-1); return; }
			if (TerminalMainController.RightPressed()) { SwitchCamera(+1); return; }

			SecurityCamera active = SecurityCamera.Active;
			if (active != null) active.Look(ReadLook());
		}

		private void SwitchCamera(int direction)
		{
			int next = WrapIndex(_selected + direction);
			if (next == _selected) return; // одна камера или край списка без wrap

			Select(next);
			ShowCamera(_available[_selected]);
			if (Audio != null) Audio.PlayNavigate();
		}

		private void ShowCamera(SecurityCamera camera)
		{
			camera.Activate();

			if (viewCameraName == null) return;
			if (camera.HasDisplayName) LocalizedTextBinding.Bind(viewCameraName, camera.DisplayName);
			else LocalizedTextBinding.SetPlain(viewCameraName, camera.name);
		}

		// Вид с камеры выключили снаружи (например, игрок погиб) — возвращаемся к списку
		private void HandleActiveCameraChanged(SecurityCamera active)
		{
			if (active == null && _viewing) ExitView();
		}

		// Тот же расчёт, что у FirstPersonController: мышь даёт смещение за кадр (deltaTime не нужен),
		// стик — скорость (умножаем на deltaTime). Процессоры действия (масштаб, инверсия Y) уже применены
		private Vector2 ReadLook()
		{
			if (lookAction == null || lookAction.action == null) return Vector2.zero;

			InputAction action = lookAction.action;
			Vector2 look = action.ReadValue<Vector2>();
			bool isMouse = action.activeControl == null || action.activeControl.device is Pointer;
			return isMouse ? look : look * Time.deltaTime;
		}

		// ---------- Очистка ----------

		// как у почты: чистим всех детей контейнера — образцы для вёрстки в префабе не попадут в игру
		private void ClearTabs()
		{
			if (listContainer != null)
			{
				for (int i = listContainer.childCount - 1; i >= 0; i--)
				{
					GameObject child = listContainer.GetChild(i).gameObject;
					child.SetActive(false);
					Destroy(child);
				}
			}
			_tabs.Clear();
		}
	}
}
