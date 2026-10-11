using System;
using Interactables;
using Inventory;
using Player;
using StarterAssets;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Menus
{
	// Меню паузы по действию Menu/Pause из StarterAssets.inputactions (Escape / Start). Вешается на объект, который всегда активен (например, на сам Canvas сессионного UI),
	// а panel — отдельный дочерний объект, который включается/выключается (та же схема, что у Inventory.InventoryUI).
	//
	// Внутри — "страницы": mainPage (Продолжить / Отладка / Выход) и debugPage (заготовка под кнопки отладки).
	// Кнопки подключаются в инспекторе через OnClick к публичным методам ниже: Resume, OpenDebug, BackToMain, QuitGame.
	// Escape работает как "назад": из debugPage — на mainPage, из mainPage — закрыть паузу.
	//
	// Приоритет Escape у других систем:
	// - пока идёт фокус на терминале (PlayerCameraFocus.IsBusy) — Escape принадлежит терминалу, пауза не открывается;
	// - если открыт инвентарь — первый Escape просто закрывает его.
	public class PauseMenu : MonoBehaviour
	{
		[Header("UI References")]
		[Tooltip("Корневая панель паузы (затемнение + страницы) — включается/выключается целиком. " +
			"Не должна быть тем же объектом, на котором висит этот скрипт")]
		[SerializeField] private GameObject panel;
		[Tooltip("Главная страница: Продолжить / Отладка / Выход")]
		[SerializeField] private GameObject mainPage;
		[Tooltip("Страница отладки — сюда складываются кнопки для тестирования механик")]
		[SerializeField] private GameObject debugPage;
		[Tooltip("Кнопка \"Отладка\" на главной странице — прячется, если отладочное меню недоступно (см. debugMenuOnlyInDevBuilds)")]
		[SerializeField] private GameObject debugButton;

		[Header("Keyboard Navigation (optional)")]
		[Tooltip("Что выделить при открытии главной страницы — чтобы меню работало стрелками/Enter без мыши")]
		[SerializeField] private GameObject mainFirstSelected;
		[Tooltip("Что выделить при открытии страницы отладки")]
		[SerializeField] private GameObject debugFirstSelected;

		[Header("Behaviour")]
		[Tooltip("Действие Menu/Pause из StarterAssets.inputactions (по умолчанию Escape / Start на геймпаде). "
			+ "Лежит в отдельной карте Menu, а не в Player: на паузе карта Player выключается целиком, "
			+ "а Pause должна продолжать работать, чтобы закрыть меню")]
		[SerializeField] private InputActionReference pauseAction;
		[Tooltip("Останавливать время (Time.timeScale = 0) — враги, физика, анимации замирают")]
		[SerializeField] private bool freezeTime = true;
		[Tooltip("Ставить на паузу все AudioSource (AudioListener.pause)")]
		[SerializeField] private bool pauseAudio = true;
		[Tooltip("Кнопка \"Отладка\" видна только в редакторе и Development Build — в релизной сборке её не будет")]
		[SerializeField] private bool debugMenuOnlyInDevBuilds = true;

		// глобальный флаг для других систем (например, InventoryUI не открывается во время паузы)
		public static bool IsPaused { get; private set; }
		public static event Action<bool> PausedChanged;

		private enum Page { Main, Debug }

		private StarterAssetsInputs _input;
		private PlayerInput _playerInput;
		private PlayerCameraFocus _focus;
		private InventoryUI _inventory;
		private Page _page;
		private float _timeScaleBeforePause = 1f;

		private bool DebugMenuAvailable => !debugMenuOnlyInDevBuilds || Debug.isDebugBuild;

		private void Awake()
		{
			if (panel == null)
			{
				Debug.LogError("PauseMenu: не назначен panel", this);
				enabled = false;
				return;
			}

			// panel выключается при закрытии паузы — если скрипт висит на нём самом или на любом его дочернем
			// объекте, он выключится вместе с ним, и Update больше не поймает нажатие паузы
			if (transform.IsChildOf(panel.transform))
			{
				Debug.LogError($"PauseMenu: скрипт висит на '{name}' — это сам panel или объект внутри него. " +
					"Перевесь PauseMenu на объект, который всегда активен (например, корень UIInterface).", this);
				enabled = false;
				return;
			}

			if (pauseAction == null)
			{
				Debug.LogError("PauseMenu: не назначен pauseAction (StarterAssets → Menu/Pause)", this);
				enabled = false;
				return;
			}

			if (debugButton != null) debugButton.SetActive(DebugMenuAvailable);

			// InventoryUI обычно на том же Canvas — ищем по сцене, ссылка в инспекторе не нужна
			_inventory = FindFirstObjectByType<InventoryUI>();

			panel.SetActive(false);
		}

		private void OnEnable()
		{
			PlayerStart.PlayerSpawned += HandlePlayerSpawned;
			if (_input == null) Bind(FindFirstObjectByType<StarterAssetsInputs>());

			// карту Menu PlayerInput сам не включает (он включает только карту по умолчанию — Player),
			// поэтому включаем действие сами; DeactivateInput на паузе выключает только Player и его не трогает
			pauseAction.action.Enable();
		}

		private void OnDisable()
		{
			PlayerStart.PlayerSpawned -= HandlePlayerSpawned;
			if (pauseAction != null) pauseAction.action.Disable();
			// выгрузка сцены / выключение объекта посреди паузы не должны оставить игру замороженной
			if (IsPaused) SetPaused(false);
		}

		private void HandlePlayerSpawned(GameObject player)
		{
			Bind(player.GetComponentInChildren<StarterAssetsInputs>());
		}

		private void Bind(StarterAssetsInputs input)
		{
			_input = input;
			_playerInput = input != null ? input.GetComponent<PlayerInput>() : null;
			_focus = input != null ? input.GetComponentInParent<PlayerCameraFocus>() : null;
		}

		private void Update()
		{
			if (!pauseAction.action.WasPressedThisFrame()) return;

			if (IsPaused)
			{
				Back();
				return;
			}

			// Escape во время терминала — это выход из терминала, а не пауза
			if (_focus != null && _focus.IsBusy) return;

			if (_inventory != null && _inventory.IsOpen)
			{
				_inventory.Close();
				return;
			}

			SetPaused(true);
		}

		// ---------- Кнопки (OnClick в инспекторе) ----------

		public void Resume() => SetPaused(false);

		public void OpenDebug()
		{
			if (DebugMenuAvailable && debugPage != null) ShowPage(Page.Debug);
		}

		public void BackToMain() => ShowPage(Page.Main);

		public void QuitGame()
		{
			// главного меню пока нет — просто выходим из игры (в редакторе — останавливаем Play Mode)
			SetPaused(false);
#if UNITY_EDITOR
			UnityEditor.EditorApplication.isPlaying = false;
#else
			Application.Quit();
#endif
		}

		// ---------- Логика ----------

		private void Back()
		{
			if (_page == Page.Debug) ShowPage(Page.Main);
			else SetPaused(false);
		}

		private void SetPaused(bool paused)
		{
			if (IsPaused == paused) return;
			IsPaused = paused;

			panel.SetActive(paused);
			if (paused) ShowPage(Page.Main);

			if (freezeTime)
			{
				if (paused)
				{
					_timeScaleBeforePause = Time.timeScale;
					Time.timeScale = 0f;
				}
				else
				{
					Time.timeScale = _timeScaleBeforePause;
				}
			}

			if (pauseAudio) AudioListener.pause = paused;

			Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
			Cursor.visible = paused;

			ApplyInputLock(paused);

			if (!paused && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

			PausedChanged?.Invoke(paused);
		}

		// Глушим весь игровой ввод разом: при timeScale = 0 движение и так стоит, но мышиный обзор не зависит
		// от deltaTime, а E/R/колесо продолжили бы срабатывать. DeactivateInput отключает все действия
		// PlayerInput, а накопленные значения сбрасываем руками — иначе после паузы они "дотекут" до игрока
		private void ApplyInputLock(bool paused)
		{
			if (_input == null) return;

			if (paused)
			{
				_input.move = Vector2.zero;
				_input.look = Vector2.zero;
				_input.jump = false;
				_input.sprint = false;
				_input.interact = false;
				_input.reload = false;
				_input.dropWeapon = false;
				_input.weaponScroll = 0f;
				_input.GameplayInputEnabled = false; // заодно сбрасывает зажатые fire/aim
				if (_playerInput != null) _playerInput.DeactivateInput();
			}
			else
			{
				if (_playerInput != null) _playerInput.ActivateInput();
				_input.look = Vector2.zero;
				_input.GameplayInputEnabled = true;
			}
		}

		private void ShowPage(Page page)
		{
			_page = page;
			if (mainPage != null) mainPage.SetActive(page == Page.Main);
			if (debugPage != null) debugPage.SetActive(page == Page.Debug);

			GameObject selected = page == Page.Main ? mainFirstSelected : debugFirstSelected;
			if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(selected);
		}
	}
}
