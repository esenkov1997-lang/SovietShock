using UnityEngine;

namespace HUD
{
	// Управление полноэкранными помехами "сбой сигнала" (шейдер SovietShock/SignalGlitch в Full Screen Pass рендерера).
	// Сила задаётся глобальным параметром шейдера, а не свойством материала: изменения в Play Mode не сохраняются
	// в ассет материала, и любой код (экран смерти, позже — урон) может управлять эффектом без ссылки на материал.
	public static class SignalGlitch
	{
		private static readonly int IntensityId = Shader.PropertyToID("_SignalGlitchIntensity");

		// 0 — чистая картинка, 1 — максимальный сбой
		public static void SetIntensity(float intensity)
		{
			Shader.SetGlobalFloat(IntensityId, Mathf.Clamp01(intensity));
		}

		// при каждом запуске Play Mode — чистая картинка (глобальные параметры шейдеров переживают остановку игры)
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			SetIntensity(0f);
		}
	}
}
