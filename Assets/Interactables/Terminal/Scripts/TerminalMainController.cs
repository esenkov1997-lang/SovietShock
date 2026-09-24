using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization;
using Player;

namespace Interactables
{
	public enum TerminalState { Password, CascadeMenu, FullScreen }

	// Базовый класс модуля (приложения) терминала — Почта, Файлы, Камера и т.д. TerminalMainController
	// открывает/закрывает модуль и каждый кадр, пока тот открыт на весь экран, передаёт ему ввод через
	// HandleInput — сам модуль в Update ничего не читает. Так Escape/Enter гарантированно обрабатываются
	// ровно в одном месте за кадр и не "пробивают" сразу два уровня навигации.
	public abstract class TerminalApp : MonoBehaviour
	{
		// показать свою панель внутри FullScreen_Window и заполнить её
		public abstract void Open();
		// спрятать свою панель и подчистить заспавненное
		public abstract void Close();
		// вызывается каждый кадр, пока модуль открыт. Escape сюда не доходит — его обрабатывает главный контроллер
		public virtual void HandleInput() { }
		// Escape внутри модуля: true — модуль обработал его сам (например, вернул фокус из переписки в список),
		// false — модуль закрывается и терминал возвращается в каскадное меню
		public virtual bool HandleBack() => false;
		// число на окне каскада (Number): писем, камер, файлов. -1 — не показывать
		public virtual int ItemCount => -1;

		// звуки интерфейса этого терминала (TerminalAudio на корне префаба). Может быть null — тогда без звука
		private TerminalAudio _audio;
		private bool _audioSearched;
		protected TerminalAudio Audio
		{
			get
			{
				if (!_audioSearched)
				{
					_audio = GetComponentInParent<TerminalAudio>(true);
					_audioSearched = true;
				}
				return _audio;
			}
		}
	}

	// Главный менеджер терминала. Вешается на TerminalCanvas (объект, который всегда активен).
	// Связь с системой фокусировки (InteractableFocusObject + PlayerCameraFocus) — сама, через события:
	// OnInteractionStart → OpenTerminal(), OnInteractionEnd → CloseTerminal(). Выход из терминала по Escape
	// тоже делает этот скрипт (см. HandlesExitKeyExternally) — PlayerCameraFocus в это не вмешивается.
	//
	//   Password    — ввод пароля. Enter — проверка, Escape — выход из терминала
	//   CascadeMenu — 3 каскадных окна. W/S, ↑/↓ — выбор, Enter или клавиша взаимодействия — открыть модуль, Escape — выход
	//   FullScreen  — открытый модуль. Escape — сначала модулю (TerminalApp.HandleBack), если тот не
	//                 обработал — назад в CascadeMenu. Остальной ввод — модулю
	public class TerminalMainController : MonoBehaviour
	{
		[Serializable]
		private class CascadeWindow
		{
			[Tooltip("Заголовок окна (ключ локализации) — подставляется в Titul Name. Переопределять текст " +
				"в экземпляре префаба не нужно")]
			public LocalizedString title = new LocalizedString();
			[Tooltip("Окно (экземпляр префаба Tab window с CascadeWindowUI) внутри MainMenu_CascadePanel")]
			public CascadeWindowUI window;
			[Tooltip("Модуль, который открывается по Enter (например, MailAppController). Его ItemCount идёт в Number")]
			public TerminalApp app;
		}

		[Header("Interaction")]
		[Tooltip("Если не задано — ищется на родителях (TerminalCanvas обычно лежит внутри объекта терминала)")]
		[SerializeField] private InteractableFocusObject focusObject;

		[Header("Password")]
		[SerializeField] private bool requirePassword = true;
		[SerializeField] private string correctPassword = "1234";
		[Tooltip("После верного пароля больше не спрашивать его при следующих входах в этот терминал")]
		[SerializeField] private bool rememberUnlock = true;
		[Tooltip("Password_Panel")]
		[SerializeField] private GameObject passwordPanel;
		[Tooltip("Password_InputField")]
		[SerializeField] private TMP_InputField passwordInput;
		[Tooltip("Text_Status. Его текст задаёт этот скрипт — НЕ вешай на него LocalizeStringEvent")]
		[SerializeField] private TMP_Text statusText;
		[Tooltip("Статус в ожидании ввода (\"AWAITING INPUT...\"). Пусто — остаётся текст из префаба")]
		[SerializeField] private LocalizedString idleStatusMessage = new LocalizedString();
		[Tooltip("Статус при неверном пароле (\"ACCESS DENIED\"). Пусто — английский текст по умолчанию")]
		[SerializeField] private LocalizedString deniedMessage = new LocalizedString();
		[SerializeField] private Color deniedColor = Color.red;

		[Header("Cascade Menu")]
		[Tooltip("MainMenu_CascadePanel")]
		[SerializeField] private GameObject cascadePanel;
		[Tooltip("Окна в порядке навигации сверху вниз: Files, Camera, Mail")]
		[SerializeField] private CascadeWindow[] cascadeWindows = new CascadeWindow[0];

		[Header("Full Screen")]
		[Tooltip("FullScreen_Window")]
		[SerializeField] private GameObject fullScreenWindow;

		public TerminalState State { get; private set; }
		// true, пока игрок работает с терминалом. Закрытый терминал просто показывает экран и не читает ввод
		public bool IsOpen { get; private set; }

		private bool _unlocked;
		private string _defaultStatusText = string.Empty;
		private Color _defaultStatusColor = Color.white;
		private int _selectedWindow;
		private TerminalApp _activeApp;
		private Coroutine _focusRoutine;
		private TerminalAudio _audio; // необязателен — без него терминал просто беззвучный
		private int _openedFrame = -1;

		private bool NeedsPassword => requirePassword && !(rememberUnlock && _unlocked);

		// Для TerminalInstance на корне префаба: у каждого терминала в уровне свой пароль. Вызывается до Awake
		// (TerminalInstance выполняется раньше, см. DefaultExecutionOrder), поэтому первый экран уже правильный
		public void ConfigurePassword(bool require, string password)
		{
			requirePassword = require;
			correctPassword = password;
		}

		private void Awake()
		{
			if (statusText != null)
			{
				_defaultStatusText = statusText.text;
				_defaultStatusColor = statusText.color;
			}

			// настройки конкретного терминала (пароль, письма) живут на корне префаба — забираем их до того,
			// как решать, какой экран показать. Работает при любом порядке Awake (см. TerminalInstance)
			TerminalInstance instance = GetComponentInParent<TerminalInstance>();
			if (instance != null) instance.ApplySettings();

			_audio = GetComponentInParent<TerminalAudio>(true);
			if (_audio == null && instance != null) _audio = instance.GetComponentInChildren<TerminalAudio>(true);

			if (focusObject == null) focusObject = GetComponentInParent<InteractableFocusObject>();
			// в префабе терминала InteractableFocusObject обычно висит на экране — соседе, а не родителе canvas
			if (focusObject == null && instance != null) focusObject = instance.GetComponentInChildren<InteractableFocusObject>(true);
			if (focusObject == null)
			{
				Debug.LogError($"{nameof(TerminalMainController)} на {name}: не найден {nameof(InteractableFocusObject)} — " +
					"терминал не откроется по клавише взаимодействия. Назначь его в поле Focus Object", this);
			}
			else
			{
				// Escape внутри терминала — это навигация ("назад"), выход делаем сами через ExitTerminal()
				focusObject.HandlesExitKeyExternally = true;
			}

			// все модули изначально закрыты — на случай, если в префабе какая-то панель осталась включённой;
			// заголовки окон берутся отсюда, а не из экземпляров префаба — так префаб Tab window остаётся "чистым"
			foreach (CascadeWindow entry in cascadeWindows)
			{
				if (entry.app != null) entry.app.Close();
				if (entry.window != null) entry.window.SetTitle(entry.title);
			}

			ShowIdleScreen();
		}

		private void OnEnable()
		{
			if (passwordInput != null) passwordInput.onValueChanged.AddListener(HandlePasswordTyped);
			if (focusObject == null) return;
			focusObject.OnInteractionStart.AddListener(OpenTerminal);
			focusObject.OnInteractionEnd.AddListener(CloseTerminal);
		}

		private void OnDisable()
		{
			if (passwordInput != null) passwordInput.onValueChanged.RemoveListener(HandlePasswordTyped);
			if (focusObject == null) return;
			focusObject.OnInteractionStart.RemoveListener(OpenTerminal);
			focusObject.OnInteractionEnd.RemoveListener(CloseTerminal);
		}

		// ---------- Открытие / закрытие ----------

		// Вызывается автоматически по началу фокуса (можно и вручную — повторный вызов ничего не делает)
		public void OpenTerminal()
		{
			if (IsOpen) return;
			IsOpen = true;
			_openedFrame = Time.frameCount;
			if (_audio != null) _audio.PlayEnter();

			if (NeedsPassword) EnterPassword();
			else EnterCascade();
		}

		// Перестаёт читать ввод и возвращает экран в "спящий" вид. Вызывается при выходе
		// (и ещё раз, безопасно, по окончании возврата камеры — OnInteractionEnd)
		public void CloseTerminal()
		{
			if (!IsOpen) return;
			IsOpen = false;
			// здесь, а не в ExitTerminal: сюда приходит любой выход, в том числе прерванный извне
			if (_audio != null) _audio.PlayExit();

			CloseActiveApp();
			StopFocusRoutine();
			if (passwordInput != null) passwordInput.DeactivateInputField();
			ClearUISelection();

			ShowIdleScreen();
		}

		// Выход из терминала: закрываем UI сразу, а камеру возвращает PlayerCameraFocus
		private void ExitTerminal()
		{
			CloseTerminal();
			if (focusObject != null) focusObject.ExitInteraction();
		}

		// Что видно на экране, пока с терминалом никто не работает
		private void ShowIdleScreen()
		{
			if (NeedsPassword)
			{
				SetState(TerminalState.Password);
				ResetPasswordField();
			}
			else
			{
				SetState(TerminalState.CascadeMenu);
				RefreshCounts();
				SelectWindow(_selectedWindow);
			}
		}

		// ---------- Ввод ----------

		private void Update()
		{
			if (!IsOpen) return;
			// терминал открывается нажатием клавиши взаимодействия — в этом же кадре она ещё "нажата", и без
			// этой проверки то же нажатие сразу открыло бы выбранное окно меню
			if (Time.frameCount == _openedFrame) return;

			// по одному состоянию за кадр: если Enter/Escape переключили состояние, новое начнёт
			// читать ввод только со следующего кадра — одно нажатие не срабатывает дважды
			switch (State)
			{
				case TerminalState.Password: UpdatePassword(); break;
				case TerminalState.CascadeMenu: UpdateCascade(); break;
				case TerminalState.FullScreen: UpdateFullScreen(); break;
			}
		}

		private void UpdatePassword()
		{
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				ExitTerminal();
				return;
			}

			if (EnterKeyPressed()) CheckPassword();
		}

		private void UpdateCascade()
		{
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				ExitTerminal();
				return;
			}

			if (UpPressed()) NavigateWindow(-1);
			else if (DownPressed()) NavigateWindow(+1);
			else if (SubmitPressed()) EnterFullScreen();
		}

		private void NavigateWindow(int direction)
		{
			int previous = _selectedWindow;
			SelectWindow(_selectedWindow + direction);
			// с одним окном выделение никуда не сдвинулось — и щёлкать нечему
			if (_selectedWindow != previous && _audio != null) _audio.PlayNavigate();
		}

		private void UpdateFullScreen()
		{
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				// сначала даём модулю шанс обработать "назад" самому — выходим в меню, только если ему некуда
				if (_activeApp != null && _activeApp.HandleBack()) return;

				if (_audio != null) _audio.PlayBack();
				CloseActiveApp();
				EnterCascade();
				return;
			}

			if (_activeApp != null) _activeApp.HandleInput();
		}

		// общие хелперы клавиш — модули используют их же, чтобы навигация везде была одинаковой
		public static bool UpPressed() => Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W);
		public static bool DownPressed() => Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S);
		public static bool LeftPressed() => Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A);
		public static bool RightPressed() => Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D);
		// "зажато" — для плавных действий (прокрутка), в отличие от "нажато в этом кадре" выше
		public static bool UpHeld() => Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W);
		public static bool DownHeld() => Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S);
		public static bool EnterKeyPressed() => Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
		// "выбрать": Enter или клавиша взаимодействия (та же, что открывает терминал, см. Player.InteractKey).
		// Не для поля пароля — там клавиша взаимодействия может оказаться обычной буквой пароля
		public static bool SubmitPressed() => EnterKeyPressed() || InteractKey.WasPressedThisFrame;

		// ---------- Password ----------

		private void EnterPassword()
		{
			SetState(TerminalState.Password);
			ResetPasswordField();
			FocusPasswordField();
		}

		private void CheckPassword()
		{
			string entered = passwordInput != null ? passwordInput.text : string.Empty;

			if (entered == correctPassword)
			{
				_unlocked = true;
				if (passwordInput != null) passwordInput.DeactivateInputField();
				if (_audio != null) _audio.PlayAccessGranted();
				EnterCascade();
				return;
			}

			if (_audio != null) _audio.PlayAccessDenied();
			SetStatus(deniedMessage, DefaultDeniedText, deniedColor);
			// WithoutNotify — очистка поля не должна звучать как нажатие клавиши (см. HandlePasswordTyped)
			if (passwordInput != null) passwordInput.SetTextWithoutNotify(string.Empty);
			FocusPasswordField(); // TMP_InputField сам снимает фокус по Enter — возвращаем, чтобы можно было сразу вводить снова
		}

		// Любое изменение текста игроком — ввод или стирание символа. Программные очистки идут через
		// SetTextWithoutNotify и сюда не попадают
		private void HandlePasswordTyped(string _)
		{
			if (IsOpen && State == TerminalState.Password && _audio != null) _audio.PlayTyping();
		}

		private void ResetPasswordField()
		{
			if (passwordInput != null) passwordInput.SetTextWithoutNotify(string.Empty);
			// idleStatusMessage не задан — возвращаем то, что было в Text_Status в префабе
			SetStatus(idleStatusMessage, _defaultStatusText, _defaultStatusColor);
		}

		private const string DefaultDeniedText = "ACCESS DENIED";

		// Статус показывается переводом (и обновляется при смене языка, даже пока висит на экране);
		// если ключ не выбран — обычным текстом fallback
		private void SetStatus(LocalizedString message, string fallback, Color color)
		{
			if (statusText == null) return;

			if (message != null && !message.IsEmpty) LocalizedTextBinding.Bind(statusText, message);
			else LocalizedTextBinding.SetPlain(statusText, fallback);

			statusText.color = color;
		}

		// Фокус ставится через кадр, а не сразу:
		// 1) терминал открывается клавишей взаимодействия — сфокусируй поле в тот же кадр, и её буква попадёт в пароль;
		// 2) после неверного пароля TMP_InputField может снять фокус по Enter уже после нашего Update.
		private void FocusPasswordField()
		{
			if (passwordInput == null) return;

			StopFocusRoutine();
			_focusRoutine = StartCoroutine(FocusPasswordFieldNextFrame());
		}

		private IEnumerator FocusPasswordFieldNextFrame()
		{
			yield return null;
			_focusRoutine = null;

			if (!IsOpen || State != TerminalState.Password) yield break;

			passwordInput.Select();
			passwordInput.ActivateInputField();
		}

		private void StopFocusRoutine()
		{
			if (_focusRoutine != null) StopCoroutine(_focusRoutine);
			_focusRoutine = null;
		}

		// ---------- Cascade Menu ----------

		private void EnterCascade()
		{
			SetState(TerminalState.CascadeMenu);
			// в меню ничего не должно быть выделено в EventSystem — иначе стрелки параллельно
			// двигали бы ещё и её собственную навигацию по кнопкам
			ClearUISelection();
			RefreshCounts();
			SelectWindow(_selectedWindow);
		}

		// Число на каждом окне — ItemCount его модуля. Пересчитывается при каждом показе меню, так что если
		// данные модуля поменялись (пришло новое письмо), окно это покажет при следующем возврате в меню
		private void RefreshCounts()
		{
			foreach (CascadeWindow entry in cascadeWindows)
			{
				if (entry.window != null) entry.window.SetCount(entry.app != null ? entry.app.ItemCount : -1);
			}
		}

		private void SelectWindow(int index)
		{
			if (cascadeWindows.Length == 0) return;

			// навигация по кругу: вверх с первого окна — на последнее
			_selectedWindow = (index % cascadeWindows.Length + cascadeWindows.Length) % cascadeWindows.Length;

			// как выглядит выделение, решает сам префаб окна (CascadeWindowUI) — здесь только "какое выбрано"
			for (int i = 0; i < cascadeWindows.Length; i++)
			{
				if (cascadeWindows[i].window != null) cascadeWindows[i].window.SetSelected(i == _selectedWindow);
			}

			CascadeWindowUI window = cascadeWindows[_selectedWindow].window;
			if (window != null) window.RectTransform.SetAsLastSibling();
		}

		// ---------- Full Screen ----------

		private void EnterFullScreen()
		{
			if (cascadeWindows.Length == 0) return;

			CascadeWindow entry = cascadeWindows[_selectedWindow];
			if (entry.app == null)
			{
				Debug.LogWarning($"{nameof(TerminalMainController)}: у окна \"{entry.title}\" не назначен модуль (app)", this);
				return;
			}

			if (_audio != null) _audio.PlayConfirm();

			// сначала включаем окно, потом модуль: модулю для корутин (прокрутка чата) нужен активный объект
			SetState(TerminalState.FullScreen);
			_activeApp = entry.app;
			_activeApp.Open();
		}

		private void CloseActiveApp()
		{
			if (_activeApp == null) return;
			_activeApp.Close();
			_activeApp = null;
		}

		// ---------- Общее ----------

		private void SetState(TerminalState state)
		{
			State = state;
			if (passwordPanel != null) passwordPanel.SetActive(state == TerminalState.Password);
			if (cascadePanel != null) cascadePanel.SetActive(state == TerminalState.CascadeMenu);
			if (fullScreenWindow != null) fullScreenWindow.SetActive(state == TerminalState.FullScreen);
		}

		private static void ClearUISelection()
		{
			if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
		}
	}
}
