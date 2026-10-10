using System;
using UnityEngine;

namespace Enemies
{
	// Один "звук" для ИИ — не аудио, а игровое событие: где прозвучал и на сколько метров слышен.
	public readonly struct NoiseEvent
	{
		public readonly Vector3 Position;
		public readonly float Radius;
		public readonly GameObject Source;

		public NoiseEvent(Vector3 position, float radius, GameObject source)
		{
			Position = position;
			Radius = radius;
			Source = source;
		}
	}

	// Общий "эфир" шума. Кто шумит (шаги игрока, выстрел, разбитая бутылка) — вызывает Emit,
	// кто слушает (EnemyPerception каждого врага) — подписан на NoiseEmitted и сам решает, услышал ли.
	// Шумящему не нужно знать ни про каких врагов, а врагу — про то, кто именно шумит.
	public static class NoiseSystem
	{
		public static event Action<NoiseEvent> NoiseEmitted;

		// position — откуда звук, radius — на сколько метров он слышен на открытом месте (через стену меньше)
		public static void Emit(Vector3 position, float radius, GameObject source)
		{
			if (radius <= 0f) return;
			NoiseEmitted?.Invoke(new NoiseEvent(position, radius, source));
		}

		// рисует круг шума в Scene view на duration секунд — для отладки радиусов
		public static void DrawDebug(NoiseEvent noise, Color color, float duration = 0.5f)
		{
			const int segments = 24;
			Vector3 previous = noise.Position + Vector3.forward * noise.Radius;
			for (int i = 1; i <= segments; i++)
			{
				float angle = i * Mathf.PI * 2f / segments;
				Vector3 next = noise.Position + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * noise.Radius;
				Debug.DrawLine(previous, next, color, duration);
				previous = next;
			}
		}

		// сброс подписчиков при каждом запуске Play Mode (на случай отключённого Domain Reload)
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			NoiseEmitted = null;
		}
	}
}
