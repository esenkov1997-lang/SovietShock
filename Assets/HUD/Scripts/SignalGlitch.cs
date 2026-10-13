using System.Collections.Generic;
using UnityEngine;

namespace HUD
{
	// Управление полноэкранными помехами "сбой сигнала" (шейдер SovietShock/SignalGlitch в Full Screen Pass рендерера).
	// Сила задаётся глобальным параметром шейдера, а не свойством материала: изменения в Play Mode не сохраняются
	// в ассет материала, и любой код может управлять эффектом без ссылки на материал.
	//
	// Источников может быть несколько (урон, экран смерти...): каждый задаёт СВОЮ силу, на экран идёт наибольшая —
	// так источники не перебивают друг друга. Источник обязан убрать себя (Clear) в OnDisable.
	public static class SignalGlitch
	{
		private static readonly int IntensityId = Shader.PropertyToID("_SignalGlitchIntensity");
		private static readonly Dictionary<object, float> Sources = new Dictionary<object, float>();

		// source — кто задаёт (обычно this), intensity — 0 (чисто) .. 1 (максимальный сбой)
		public static void SetIntensity(object source, float intensity)
		{
			intensity = Mathf.Clamp01(intensity);
			if (intensity <= 0f) Sources.Remove(source);
			else Sources[source] = intensity;
			Apply();
		}

		public static void Clear(object source)
		{
			if (Sources.Remove(source)) Apply();
		}

		private static void Apply()
		{
			float max = 0f;
			foreach (float intensity in Sources.Values)
				if (intensity > max) max = intensity;
			Shader.SetGlobalFloat(IntensityId, max);
		}

		// при каждом запуске Play Mode — чистая картинка (глобальные параметры шейдеров переживают остановку игры)
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			Sources.Clear();
			Apply();
		}
	}
}
