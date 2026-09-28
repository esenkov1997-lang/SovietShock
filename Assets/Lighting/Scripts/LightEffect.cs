using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Lighting
{
	// Универсальное "поведение" источника света: вешается на объект с Light любого типа (Point/Spot/Area)
	// и каждый кадр пересчитывает его интенсивность из базовой и включённых эффектов. Эффекты независимы
	// и перемножаются, поэтому их можно сочетать: например, Pulse + Flicker — "дышащая" аварийная лампа
	// с дрожанием, Flicker + Failure — умирающая люминесцентная трубка, Blink — маячок/сигнализация.
	//
	// Кроме самого Light может синхронно гасить/зажигать материал лампы (Material Renderers) — иначе при
	// мигании свет в комнате пропадает, а сама лампочка продолжает гореть. Свойство материала задаётся
	// по имени: Color (например, _EmissionColor у URP Lit) или Float (свой параметр в Shader Graph).
	// Пламя/эффекты оружия — не сюда, это WeaponMuzzle (он может мерцать синхронно через EffectsMultiplier).
	//
	// Можно вешать вместе с Weapons.MuzzleFlash (свет горелки/дула): тогда MuzzleFlash только включает/выключает
	// этот компонент через SetOn, а яркость и эффекты задаются здесь.
	[RequireComponent(typeof(Light))]
	public class LightEffect : MonoBehaviour
	{
		[Header("Base")]
		[Tooltip("Включён ли свет при старте. Дальше управляется через SetOn/Toggle (например, из выключателя или триггера)")]
		[SerializeField] private bool startOn = true;
		[Tooltip("Интенсивность Light, от которой считаются все эффекты. При добавлении компонента берётся из Light")]
		[SerializeField] private float baseIntensity = 1f;
		[Tooltip("За сколько секунд свет плавно загорается/гаснет при SetOn. 0 — мгновенно")]
		[SerializeField] private float fadeDuration = 0f;
		[Tooltip("Случайный сдвиг фазы эффектов у каждого экземпляра — одинаковые лампы в коридоре не мигают синхронно")]
		[SerializeField] private bool randomizePhase = true;

		[Header("Flicker (дрожание, огонь/плохой контакт)")]
		[SerializeField] private bool flicker;
		[Tooltip("Насколько сильно проседает яркость: 0 — не проседает, 1 — может уходить почти в ноль")]
		[Range(0f, 1f)]
		[SerializeField] private float flickerAmount = 0.3f;
		[Tooltip("Скорость дрожания. ~2-4 — пламя свечи/костра, ~10-20 — нервное электрическое дрожание")]
		[SerializeField] private float flickerSpeed = 8f;

		[Header("Pulse (плавное \"дыхание\")")]
		[SerializeField] private bool pulse;
		[Tooltip("Яркость в нижней точке пульсации, доля от базовой")]
		[Range(0f, 1f)]
		[SerializeField] private float pulseMin = 0.2f;
		[Tooltip("Длительность одного полного цикла (ярко → тускло → ярко), сек")]
		[SerializeField] private float pulsePeriod = 2f;

		[Header("Blink (мигание вкл/выкл)")]
		[SerializeField] private bool blink;
		[Tooltip("Сколько секунд свет горит в каждом цикле мигания")]
		[SerializeField] private float blinkOnTime = 0.5f;
		[Tooltip("Сколько секунд свет погашен в каждом цикле мигания")]
		[SerializeField] private float blinkOffTime = 0.5f;
		[Tooltip("Случайный разброс длительности каждой фазы, ± сек. 0 — ровный метроном")]
		[SerializeField] private float blinkJitter = 0f;

		[Header("Failure (сбои: лампа время от времени \"заикается\")")]
		[SerializeField] private bool failure;
		[Tooltip("Пауза между сбоями, сек (случайная в диапазоне X..Y)")]
		[SerializeField] private Vector2 failureInterval = new Vector2(3f, 8f);
		[Tooltip("Длительность одного сбоя, сек (случайная в диапазоне X..Y)")]
		[SerializeField] private Vector2 failureDuration = new Vector2(0.2f, 0.8f);
		[Tooltip("Сколько раз в секунду свет щёлкает вкл/выкл во время сбоя")]
		[SerializeField] private float failureStutterRate = 20f;
		[Tooltip("Яркость в \"выключенные\" моменты сбоя, доля от базовой. 0 — полностью гаснет")]
		[Range(0f, 1f)]
		[SerializeField] private float failureDimLevel = 0f;

		public enum MaterialPropertyType { Color, Float }

		[Header("Material (светящийся меш лампы)")]
		[Tooltip("Меши лампы, чей материал гаснет/зажигается и мерцает вместе со светом. Для пламени оружия не используется — его ведёт WeaponMuzzle")]
		[FormerlySerializedAs("emissiveRenderers")]
		[SerializeField] private Renderer[] materialRenderers;
		[Tooltip("Reference-имя свойства в шейдере. _EmissionColor — стандартная эмиссия URP Lit (тип Color, Emission в материале должна быть включена); для своего Shader Graph — Reference своего параметра, например _Fade (тип Float)")]
		[SerializeField] private string materialProperty = "_EmissionColor";
		[SerializeField] private MaterialPropertyType materialPropertyType = MaterialPropertyType.Color;
		[Tooltip("Тип Color: цвет при полной яркости (HDR). Эффекты умножаются на него")]
		[ColorUsage(false, true)]
		[SerializeField] private Color emissionColor = Color.white;
		[Tooltip("Тип Float: значение при полной яркости. Эффекты умножаются на него, погашенный свет = 0")]
		[SerializeField] private float materialFloatMax = 1f;
		[Tooltip("Выключать Renderer, когда свет полностью погас — меш не рисуется вовсе, а не просто становится прозрачным/чёрным")]
		[SerializeField] private bool hideRenderersWhenOff = true;

		private int _materialPropertyId;
		// экземпляры материалов (Renderer.materials), а не MaterialPropertyBlock: с включённым SRP Batcher в URP
		// это рекомендованный Unity способ менять свойства одного объекта — MPB выбивает рендерер из батчера
		// и с Shader Graph ведёт себя ненадёжно
		private readonly List<Material> _materialInstances = new List<Material>();

		private Light _light;

		private bool _isOn;
		private float _fade; // 0..1 — текущая стадия плавного включения/выключения
		private float _phaseOffset;

		private bool _blinkLit = true;
		private float _blinkPhaseEnd;

		private bool _failureActive;
		private float _failureStateEnd;
		private float _nextStutterTime;
		private bool _stutterLit = true;

		public bool IsOn => _isOn;

		// текущий множитель эффектов (фликер/пульс/мигание/сбои) без учёта включения и fade — по нему другие
		// компоненты могут мерцать синхронно с этим светом (например, пламя горелки в WeaponMuzzle)
		public float EffectsMultiplier { get; private set; } = 1f;

		private void Reset()
		{
			// при добавлении компонента сразу подхватываем текущую яркость, чтобы свет не "прыгнул"
			Light source = GetComponent<Light>();
			if (source != null) baseIntensity = source.intensity;
		}

		private void Awake()
		{
			_light = GetComponent<Light>();
			CollectMaterialInstances();
			_phaseOffset = randomizePhase ? Random.Range(0f, 1000f) : 0f;

			_isOn = startOn;
			_fade = startOn ? 1f : 0f;

			_blinkPhaseEnd = Time.time + NextBlinkDuration(true) * (randomizePhase ? Random.value : 1f);
			_failureStateEnd = Time.time + Random.Range(failureInterval.x, failureInterval.y);

			EffectsMultiplier = ComputeMultiplier();
			Apply(EffectsMultiplier * _fade);
		}

		private void CollectMaterialInstances()
		{
			if (materialRenderers == null || string.IsNullOrEmpty(materialProperty)) return;

			_materialPropertyId = Shader.PropertyToID(materialProperty);
			foreach (Renderer target in materialRenderers)
			{
				if (target == null) continue;
				// .materials создаёт копии только для этого рендерера — остальные лампы с тем же материалом не затронет
				foreach (Material material in target.materials)
				{
					if (material.HasProperty(_materialPropertyId)) _materialInstances.Add(material);
				}
			}
		}

		private void OnDestroy()
		{
			// копии из Renderer.materials не удаляются сами вместе с объектом
			foreach (Material material in _materialInstances)
			{
				if (material != null) Destroy(material);
			}
		}

		// включить/выключить свет (с учётом fadeDuration) — для выключателей, триггеров, скриптовых событий,
		// а также Weapons.MuzzleFlash (свет горелки/дула). instant — без fade, сразу в конечное состояние
		public void SetOn(bool on, bool instant = false)
		{
			_isOn = on;
			if (instant || fadeDuration <= 0f) _fade = on ? 1f : 0f;
		}

		public void Toggle()
		{
			SetOn(!_isOn);
		}

		private void Update()
		{
			if (fadeDuration > 0f)
			{
				_fade = Mathf.MoveTowards(_fade, _isOn ? 1f : 0f, Time.deltaTime / fadeDuration);
			}

			EffectsMultiplier = ComputeMultiplier();
			Apply(EffectsMultiplier * _fade);
		}

		private float ComputeMultiplier()
		{
			float t = Time.time + _phaseOffset;
			float multiplier = 1f;

			if (pulse && pulsePeriod > 0f)
			{
				float wave = 0.5f + 0.5f * Mathf.Cos(t * Mathf.PI * 2f / pulsePeriod);
				multiplier *= Mathf.Lerp(pulseMin, 1f, wave);
			}

			if (flicker)
			{
				// две октавы шума Перлина — одна даёт "волну", вторая мелкую рябь, вместе меньше похоже на синусоиду
				float noise = Mathf.PerlinNoise(t * flickerSpeed, 0f) * 0.7f + Mathf.PerlinNoise(t * flickerSpeed * 2.3f, 17f) * 0.3f;
				multiplier *= 1f - flickerAmount * noise;
			}

			if (blink) multiplier *= UpdateBlink() ? 1f : 0f;

			if (failure) multiplier *= UpdateFailure() ? 1f : failureDimLevel;

			return multiplier;
		}

		private bool UpdateBlink()
		{
			if (Time.time >= _blinkPhaseEnd)
			{
				_blinkLit = !_blinkLit;
				_blinkPhaseEnd = Time.time + NextBlinkDuration(_blinkLit);
			}
			return _blinkLit;
		}

		private float NextBlinkDuration(bool lit)
		{
			float duration = lit ? blinkOnTime : blinkOffTime;
			return Mathf.Max(0.01f, duration + Random.Range(-blinkJitter, blinkJitter));
		}

		// вне сбоя свет горит ровно; во время сбоя щёлкает вкл/выкл со случайными интервалами вокруг failureStutterRate
		private bool UpdateFailure()
		{
			if (Time.time >= _failureStateEnd)
			{
				_failureActive = !_failureActive;
				Vector2 range = _failureActive ? failureDuration : failureInterval;
				_failureStateEnd = Time.time + Random.Range(range.x, range.y);
				_stutterLit = true;
				_nextStutterTime = Time.time;
			}

			if (!_failureActive) return true;

			if (Time.time >= _nextStutterTime)
			{
				_stutterLit = !_stutterLit;
				float step = failureStutterRate > 0f ? 1f / failureStutterRate : 0.05f;
				_nextStutterTime = Time.time + step * Random.Range(0.5f, 1.5f);
			}
			return _stutterLit;
		}

		private void Apply(float multiplier)
		{
			float intensity = baseIntensity * multiplier;
			_light.intensity = intensity;
			// полностью погашенный свет выключаем — не тратит ресурсы на освещение и тени
			bool lit = multiplier > 0.001f;
			_light.enabled = lit;

			if (materialRenderers == null) return;

			if (hideRenderersWhenOff)
			{
				foreach (Renderer target in materialRenderers)
				{
					if (target != null) target.enabled = lit;
				}
			}

			foreach (Material material in _materialInstances)
			{
				if (materialPropertyType == MaterialPropertyType.Float) material.SetFloat(_materialPropertyId, materialFloatMax * multiplier);
				else material.SetColor(_materialPropertyId, emissionColor * multiplier);
			}
		}
	}
}
