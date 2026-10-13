using System.Collections;
using HUD;
using Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Weapons;

namespace Menus
{
	// Экран смерти в стиле "сигнал потерян": по лору игрок смотрит через камеру шлема, и смерть носителя — это обрыв
	// связи, а не затемнение. Вешается на объект в UIInterface, который всегда активен (как PauseMenu); panel —
	// отдельный дочерний объект на весь экран.
	//
	// После PlayerDeath.Died: Show Delay секунд, чтобы увидеть падение → короткий сбой (экран мигает чёрным) →
	// чёрный экран, звук обрывается, HUD прячется → через Buttons Delay появляются кнопки.
	// "Переподключиться" (Restart) перезапускает сцену. "Загрузить" пока неактивна — сохранений ещё нет.
	public class DeathScreen : PlayerHUDBinding
	{
		[Header("UI")]
		[Tooltip("Корневая панель экрана смерти на весь экран (с чёрным фоном). Выключена, пока игрок жив. " +
			"Не должна быть тем же объектом, на котором висит этот скрипт")]
		[SerializeField] private GameObject panel;
		[Tooltip("Кнопки (и всё, что должно появиться с задержкой) — дочерний объект панели")]
		[SerializeField] private GameObject buttonsRoot;
		[Tooltip("Кнопка \"Загрузить\" — неактивна, пока нет системы сохранений")]
		[SerializeField] private Button loadButton;
		[Tooltip("Что выделить при появлении кнопок — чтобы меню работало с клавиатуры")]
		[SerializeField] private GameObject firstSelected;
		[Tooltip("Что спрятать вместе с обрывом сигнала (HUD: прицел, патроны, здоровье)")]
		[SerializeField] private GameObject[] hideOnShow;

		[Header("Тайминги (сек)")]
		[Tooltip("Сколько после смерти ещё видно картинку — чтобы игрок увидел падение")]
		[SerializeField] private float showDelay = 2.5f;
		[Tooltip("Длительность сбоя сигнала перед полным обрывом. 0 — мгновенный обрыв")]
		[SerializeField] private float glitchDuration = 0.3f;
		[Tooltip("Сколько раз экран мигнёт чёрным за время сбоя")]
		[SerializeField] private int glitchFlickers = 4;
		[Tooltip("Через сколько после обрыва появляются кнопки — чтобы игрок не нажал их случайно")]
		[SerializeField] private float buttonsDelay = 1f;

		[Header("Помехи (эффект SignalGlitch в рендерере)")]
		[Tooltip("Сила помех от момента смерти до обрыва: по X — секунды с момента смерти (до Show Delay), по Y — сила 0..1. " +
			"По умолчанию: всплеск в момент удара, слабые помехи во время падения, нарастание перед обрывом")]
		[SerializeField] private AnimationCurve glitchOverTime = new AnimationCurve(
			new Keyframe(0f, 0.7f), new Keyframe(0.25f, 0.1f), new Keyframe(1.6f, 0.15f), new Keyframe(2.5f, 0.9f));
		[Tooltip("Сила помех в \"просветах\" между чёрными кадрами сбоя")]
		[Range(0f, 1f)]
		[SerializeField] private float flickerGlitchIntensity = 1f;

		[Header("Звук")]
		[Tooltip("Обрывать весь звук вместе с сигналом")]
		[SerializeField] private bool muteAudio = true;

		// с момента смерти — меню паузы не открывается (Escape не должен вытащить паузу поверх экрана смерти)
		public static bool IsActive { get; private set; }

		private PlayerDeath _death;
		private float _volumeBeforeMute = 1f;

		private void Awake()
		{
			if (panel == null)
			{
				Debug.LogError("DeathScreen: не назначен panel", this);
				enabled = false;
				return;
			}
			if (transform.IsChildOf(panel.transform))
				Debug.LogError($"DeathScreen: скрипт висит на '{name}' внутри panel — при старте он выключится вместе с ней. " +
					"Перевесь на объект, который всегда активен", this);

			panel.SetActive(false);
			if (loadButton != null) loadButton.interactable = false;
		}

		// остановили Play Mode или выгрузили сцену посреди помех — глобальный параметр шейдера иначе остался бы включённым
		protected override void OnDisable()
		{
			base.OnDisable();
			SignalGlitch.Clear(this);
		}

		protected override void OnBind(WeaponController weapons)
		{
			_death = Movement != null ? Movement.GetComponent<PlayerDeath>() : null;
			if (_death == null)
			{
				Debug.LogWarning($"{name}: у игрока нет PlayerDeath — экран смерти не появится", this);
				return;
			}
			_death.Died += HandleDied;
		}

		protected override void OnUnbind(WeaponController weapons)
		{
			if (_death != null) _death.Died -= HandleDied;
			_death = null;
		}

		private void HandleDied()
		{
			IsActive = true;
			StartCoroutine(ShowRoutine());
		}

		// реальное время, а не игровое: будущий slow-mo в момент смерти не должен растягивать паузу перед экраном
		private IEnumerator ShowRoutine()
		{
			// картинка ещё видна, но уже с помехами — по кривой Glitch Over Time
			for (float elapsed = 0f; elapsed < showDelay; elapsed += Time.unscaledDeltaTime)
			{
				SignalGlitch.SetIntensity(this, glitchOverTime.Evaluate(elapsed));
				yield return null;
			}

			if (buttonsRoot != null) buttonsRoot.SetActive(false);

			// сбой: экран несколько раз пропадает и возвращается, интервалы случайные — как помехи
			if (glitchDuration > 0f && glitchFlickers > 0)
			{
				float step = glitchDuration / (glitchFlickers * 2);
				for (int i = 0; i < glitchFlickers; i++)
				{
					panel.SetActive(true);
					yield return new WaitForSecondsRealtime(step * Random.Range(0.5f, 1.5f));
					panel.SetActive(false);
					SignalGlitch.SetIntensity(this, flickerGlitchIntensity);
					yield return new WaitForSecondsRealtime(step * Random.Range(0.5f, 1.5f));
				}
			}

			// обрыв
			panel.SetActive(true);
			SignalGlitch.Clear(this); // дальше чёрный экран — эффект не нужен
			foreach (GameObject hidden in hideOnShow)
				if (hidden != null) hidden.SetActive(false);
			if (muteAudio)
			{
				_volumeBeforeMute = AudioListener.volume;
				AudioListener.volume = 0f;
			}

			yield return new WaitForSecondsRealtime(buttonsDelay);

			if (buttonsRoot != null) buttonsRoot.SetActive(true);
			Cursor.lockState = CursorLockMode.None;
			Cursor.visible = true;
			if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(firstSelected);
		}

		// ---------- Кнопки (OnClick в инспекторе) ----------

		// "Переподключиться" — пока просто перезапуск сцены
		public void Restart()
		{
			SignalGlitch.Clear(this);
			// громкость и время — глобальные, переживают загрузку сцены; возвращаем до неё
			if (muteAudio) AudioListener.volume = _volumeBeforeMute;
			Time.timeScale = 1f;
			IsActive = false;

			Scene scene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
			// тестовая сцена может быть не добавлена в Build Profiles — по имени/индексу её тогда не загрузить,
			// но в редакторе можно загрузить по пути
			if (scene.buildIndex < 0)
			{
				UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path,
					new LoadSceneParameters(LoadSceneMode.Single));
				return;
			}
#endif
			SceneManager.LoadScene(scene.buildIndex);
		}

		public void Load()
		{
			Debug.Log("DeathScreen: загрузка пока недоступна — система сохранений ещё не сделана");
		}

		// сброс при каждом запуске Play Mode (на случай отключённого Domain Reload)
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			IsActive = false;
		}
	}
}
