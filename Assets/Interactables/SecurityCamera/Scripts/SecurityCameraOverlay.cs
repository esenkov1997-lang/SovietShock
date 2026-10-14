using System;
using System.Globalization;
using HUD;
using TMPro;
using UnityEngine;

namespace Interactables
{
	// Эффекты "вида с камеры наблюдения". Вешается на корень оверлея (объект View Overlay у CameraAppController):
	// CameraAppController включает этот объект, пока игрок смотрит через камеру, и выключает при выходе —
	// поэтому всё, что лежит внутри, работает ровно во время просмотра:
	//   - Volume (URP, Global) с профилем камеры — обесцвечивание, зерно, виньетка, искажение линзы;
	//   - Image с материалом на шейдере UICRTOverlay — сканлайны, бегущая полоса, снег, мерцание;
	//   - этот скрипт: помехи сигнала (SignalGlitch) при входе/переключении и случайные сбои, мигающий REC, часы.
	//
	// Помехи SignalGlitch — тот же полноэкранный эффект, что при уроне: источники не перебивают друг друга,
	// на экран идёт самый сильный.
	public class SecurityCameraOverlay : MonoBehaviour
	{
		[Header("Signal Glitch (вход и переключение камер)")]
		[Tooltip("Сила помех после входа в камеру или переключения: ось X — секунды, ось Y — сила 0..1. " +
			"Длительность вспышки — время последнего ключа")]
		[SerializeField] private AnimationCurve switchGlitch = new AnimationCurve(
			new Keyframe(0f, 1f), new Keyframe(0.08f, 0.8f), new Keyframe(0.3f, 0f));
		[Tooltip("Постоянные помехи на всех камерах (0 — чистая картинка). У отдельной камеры можно добавить " +
			"своих в SecurityCamera → Signal Interference")]
		[Range(0f, 1f)]
		[SerializeField] private float baseInterference;

		[Header("Random Glitches")]
		[Tooltip("Случайные короткие сбои во время просмотра")]
		[SerializeField] private bool randomGlitches = true;
		[Tooltip("Пауза между случайными сбоями, секунды (случайно в диапазоне)")]
		[SerializeField] private Vector2 randomGlitchInterval = new Vector2(6f, 15f);
		[Tooltip("Сила случайного сбоя — множитель кривой Switch Glitch")]
		[Range(0f, 1f)]
		[SerializeField] private float randomGlitchStrength = 0.35f;

		[Header("REC")]
		[Tooltip("Необязательно: индикатор записи (точка + \"REC\") — мигает")]
		[SerializeField] private GameObject recIndicator;
		[Tooltip("Период мигания: столько секунд виден и столько же скрыт")]
		[SerializeField] private float recBlinkInterval = 0.6f;

		[Header("Timestamp")]
		[Tooltip("Необязательно: текст с датой и временем записи")]
		[SerializeField] private TMP_Text timestampText;
		[Tooltip("Дата и время на часах камер в начале уровня (ГГГГ-ММ-ДД чч:мм:сс). Дальше часы идут вместе с игрой")]
		[SerializeField] private string startDateTime = "1984-11-07 03:12:00";
		[Tooltip("Формат часов (стандартный формат даты C#: dd — день, MM — месяц, HH:mm:ss — время)")]
		[SerializeField] private string timestampFormat = "dd.MM.yyyy  HH:mm:ss";

		[Header("Sound")]
		[Tooltip("Необязательно: 2D AudioSource для треска помех при входе/переключении")]
		[SerializeField] private AudioSource audioSource;
		[Tooltip("Звуки помех — каждый раз случайный")]
		[SerializeField] private AudioClip[] switchClips = new AudioClip[0];
		[Range(0f, 1f)]
		[SerializeField] private float switchVolume = 0.6f;

		private DateTime _startDate;
		private bool _hasStartDate;
		private long _shownSecond = -1;

		private float _burstStart = float.NegativeInfinity;
		private float _burstScale;
		private float _nextRandomGlitch;

		private void Awake()
		{
			_hasStartDate = DateTime.TryParse(startDateTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out _startDate);
			if (!_hasStartDate && timestampText != null)
				Debug.LogWarning($"{nameof(SecurityCameraOverlay)} на {name}: не удалось разобрать Start Date Time " +
					$"\"{startDateTime}\" — нужен формат ГГГГ-ММ-ДД чч:мм:сс", this);
		}

		private void OnEnable()
		{
			SecurityCamera.ActiveChanged += HandleCameraChanged;
			_shownSecond = -1;
			// оверлей включается уже после переключения вида на камеру — вспышку запускаем сами
			PlaySwitchEffect();
		}

		private void OnDisable()
		{
			SecurityCamera.ActiveChanged -= HandleCameraChanged;
			SignalGlitch.Clear(this);
		}

		private void Update()
		{
			UpdateGlitch();
			UpdateRec();
			UpdateTimestamp();
		}

		// переключение на другую камеру (null — выход из просмотра, тогда оверлей и так выключится)
		private void HandleCameraChanged(SecurityCamera active)
		{
			if (active != null) PlaySwitchEffect();
		}

		private void PlaySwitchEffect()
		{
			StartBurst(1f);
			PlaySwitchSound();
		}

		private void StartBurst(float scale)
		{
			_burstStart = Time.time;
			_burstScale = scale;
			ScheduleRandomGlitch();
		}

		private void ScheduleRandomGlitch()
		{
			float min = Mathf.Max(0.1f, Mathf.Min(randomGlitchInterval.x, randomGlitchInterval.y));
			float max = Mathf.Max(min, randomGlitchInterval.y);
			_nextRandomGlitch = Time.time + UnityEngine.Random.Range(min, max);
		}

		private void UpdateGlitch()
		{
			if (randomGlitches && Time.time >= _nextRandomGlitch) StartBurst(randomGlitchStrength);

			float burst = switchGlitch.length > 0 ? switchGlitch.Evaluate(Time.time - _burstStart) * _burstScale : 0f;
			SecurityCamera active = SecurityCamera.Active;
			float interference = Mathf.Max(baseInterference, active != null ? active.SignalInterference : 0f);

			SignalGlitch.SetIntensity(this, Mathf.Max(burst, interference));
		}

		private void UpdateRec()
		{
			if (recIndicator == null || recBlinkInterval <= 0f) return;

			bool visible = Mathf.FloorToInt(Time.time / recBlinkInterval) % 2 == 0;
			if (recIndicator.activeSelf != visible) recIndicator.SetActive(visible);
		}

		private void UpdateTimestamp()
		{
			if (timestampText == null || !_hasStartDate) return;

			// текст пересобираем раз в секунду, а не каждый кадр
			long second = (long)Time.timeSinceLevelLoad;
			if (second == _shownSecond) return;
			_shownSecond = second;

			timestampText.text = _startDate.AddSeconds(second).ToString(timestampFormat, CultureInfo.InvariantCulture);
		}

		private void PlaySwitchSound()
		{
			if (audioSource == null || switchClips == null || switchClips.Length == 0) return;

			AudioClip clip = switchClips[UnityEngine.Random.Range(0, switchClips.Length)];
			if (clip != null) audioSource.PlayOneShot(clip, switchVolume);
		}
	}
}
