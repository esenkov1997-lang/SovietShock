using UnityEngine;
using Weapons;

namespace HUD
{
	// Круговой индикатор ремонта на месте прицела: пока горелка чинит объект (WeaponController.RepairTarget),
	// перекрестие скрывается, а вместо него появляется кольцо, заполняющееся по IRepairable.RepairProgress.
	// Луч ушёл с объекта / отпустили ПКМ — кольцо гаснет, прицел возвращается.
	// Рисует RadialProgressGraphic — вешается на тот же объект, по центру HUD-канваса (там же, где прицел).
	[RequireComponent(typeof(RadialProgressGraphic))]
	public class RepairProgressUI : PlayerHUDBinding
	{
		[Tooltip("Скорость, с которой кольцо догоняет реальный прогресс. Ремонт идёт целыми единицами прочности — " +
			"без сглаживания заполнение шло бы рывками. 0 — без сглаживания")]
		[SerializeField] private float fillSmoothing = 12f;
		[Tooltip("Скорость появления/исчезновения. 0 — мгновенно")]
		[SerializeField] private float fadeSpeed = 12f;

		private RadialProgressGraphic _graphic;
		private IRepairable _shownTarget;
		private float _alpha;

		private void Awake()
		{
			_graphic = GetComponent<RadialProgressGraphic>();
			_graphic.canvasRenderer.SetAlpha(0f);
		}

		protected override void OnDisable()
		{
			base.OnDisable();
			CrosshairUI.SetHidden(this, false);
		}

		private void Update()
		{
			IRepairable target = Weapons != null ? Weapons.RepairTarget : null;
			// объект могли уничтожить прямо во время ремонта — интерфейсная ссылка на него не становится null сама
			if (target is Object unityObject && unityObject == null) target = null;

			float progress = target != null ? target.RepairProgress : -1f;
			bool visible = progress >= 0f;

			CrosshairUI.SetHidden(this, visible);

			if (visible)
			{
				// новый объект — сразу его прогресс, а не "доезд" кольца от значения предыдущего
				if (target != _shownTarget) _graphic.Fill = progress;
				_shownTarget = target;

				_graphic.Fill = fillSmoothing > 0f
					? Mathf.Lerp(_graphic.Fill, progress, 1f - Mathf.Exp(-fillSmoothing * Time.deltaTime))
					: progress;
			}
			else
			{
				_shownTarget = null;
			}

			// при исчезновении кольцо гаснет с последним заполнением, а не обнуляется на глазах
			float targetAlpha = visible ? 1f : 0f;
			_alpha = fadeSpeed > 0f ? Mathf.MoveTowards(_alpha, targetAlpha, fadeSpeed * Time.deltaTime) : targetAlpha;
			_graphic.canvasRenderer.SetAlpha(_alpha);
		}
	}
}
