using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Enemies
{
	// Случайные взгляды по сторонам, когда враг спокоен: постоял — глянул влево, подержал взгляд — вернулся,
	// через несколько секунд — снова. Мелочь, но без неё стоящий на посту враг выглядит как манекен.
	//
	// Не компонент, а набор настроек внутри мозга (DogAI): мозг сам решает, когда враг спокоен, и спрашивает
	// отсюда точку для EnemyHeadLook. Направление взгляда хранится относительно тела — на ходу (патруль)
	// собака косится в сторону от курса, а не на одну точку в мире.
	[Serializable]
	public class IdleGlance
	{
		[Tooltip("Оглядываться по сторонам в спокойном состоянии")]
		[SerializeField] private bool enabled = true;
		[Tooltip("Пауза (сек) между взглядами: случайно между X и Y")]
		[SerializeField] private Vector2 pause = new Vector2(3f, 8f);
		[Tooltip("Сколько секунд держать взгляд: случайно между X и Y")]
		[SerializeField] private Vector2 hold = new Vector2(1f, 2.5f);
		[Tooltip("Насколько далеко (градусы) влево/вправо от направления тела. Меньше Max Yaw у EnemyHeadLook — иначе голова упрётся в предел")]
		[Range(0f, 120f)]
		[SerializeField] private float maxYaw = 60f;
		[Tooltip("Наклон взгляда (градусы): случайно между X (вниз, отрицательное) и Y (вверх)")]
		[SerializeField] private Vector2 pitch = new Vector2(-15f, 10f);
		[Tooltip("Взгляд не уходит ближе этого угла (градусы) к направлению тела — иначе поворот почти не заметен")]
		[Range(0f, 90f)]
		[SerializeField] private float minYaw = 20f;

		[NonSerialized] private float _timer = -1f;
		[NonSerialized] private bool _looking;
		[NonSerialized] private Vector3 _localDirection;

		// враг перестал быть спокойным — следующий взгляд начнётся с паузы, а не мгновенно
		public void Reset()
		{
			_timer = -1f;
			_looking = false;
		}

		public bool TryGetPoint(Transform body, Vector3 eyePosition, out Vector3 point)
		{
			point = default;
			if (!enabled) return false;

			if (_timer < 0f) _timer = Random.Range(pause.x, pause.y);
			_timer -= Time.deltaTime;
			if (_timer <= 0f)
			{
				_looking = !_looking;
				_timer = _looking ? Random.Range(hold.x, hold.y) : Random.Range(pause.x, pause.y);
				if (_looking) PickDirection();
			}

			if (!_looking) return false;
			point = eyePosition + body.TransformDirection(_localDirection) * 3f;
			return true;
		}

		private void PickDirection()
		{
			float yaw = Random.Range(Mathf.Min(minYaw, maxYaw), maxYaw) * (Random.value < 0.5f ? -1f : 1f);
			float pitchAngle = Random.Range(pitch.x, pitch.y);
			_localDirection = Quaternion.Euler(-pitchAngle, yaw, 0f) * Vector3.forward;
		}

		public void Validate()
		{
			pause.x = Mathf.Max(0.1f, pause.x);
			pause.y = Mathf.Max(pause.x, pause.y);
			hold.x = Mathf.Max(0.1f, hold.x);
			hold.y = Mathf.Max(hold.x, hold.y);
			pitch.y = Mathf.Max(pitch.x, pitch.y);
		}
	}
}
