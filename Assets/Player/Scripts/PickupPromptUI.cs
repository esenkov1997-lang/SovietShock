using Interactables;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;

namespace Player
{
	// Живёт на Canvas с подсказкой подбора (внутри UIInterface — сессионный UI, не часть Player.prefab
	// и не дублируется при каждом спавне игрока). PlayerInteractor обращается сюда через Instance,
	// а не через прямую ссылку в инспекторе — такая ссылка не переживает упаковку PlayerCapsule
	// в Player.prefab, потому что Unity не умеет сериализовать в префаб ссылку на объект сцены.
	//
	// Текст подсказки задаётся кодом (SetPrompt) — НЕ вешай на promptText компонент LocalizeStringEvent.
	public class PickupPromptUI : MonoBehaviour
	{
		[SerializeField] private Canvas promptCanvas;
		[SerializeField] private TMP_Text promptText;

		public static PickupPromptUI Instance { get; private set; }

		private void Awake()
		{
			Instance = this;
			SetVisible(false);
		}

		private void OnDestroy()
		{
			if (Instance == this) Instance = null;
		}

		// Локализованная подсказка: перевод подставится сам и обновится при смене языка.
		// prefix — нелокализуемая часть перед текстом, например подпись клавиши "[E] "
		public void SetPrompt(LocalizedString prompt, string prefix = null)
		{
			if (promptText == null) return;

			if (prompt == null)
			{
				LocalizedTextBinding.SetPlain(promptText, prefix ?? string.Empty);
				return;
			}

			LocalizedTextBinding.Bind(promptText, prompt,
				value => string.IsNullOrEmpty(prefix) ? value : prefix + value);
		}

		// Обычный текст (без локализации) — например, для отладки
		public void SetText(string text)
		{
			LocalizedTextBinding.SetPlain(promptText, text);
		}

		public void SetVisible(bool visible)
		{
			if (promptCanvas != null) promptCanvas.enabled = visible;
		}
	}
}
