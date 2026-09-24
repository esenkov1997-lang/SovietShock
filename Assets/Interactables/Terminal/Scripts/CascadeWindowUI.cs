using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace Interactables
{
	// Вешается на корень префаба Tab window — одно окно каскадного меню терминала (Files / Camera / Mail).
	// Всё оформление (что подсвечивать, какими цветами, формат счётчика) живёт здесь, в самом префабе,
	// поэтому меняется в одном месте для всех окон сразу. Экземплярам в UITerminal ничего переопределять
	// не нужно: заголовок и число подставляет TerminalMainController из своих настроек и из модуля.
	public class CascadeWindowUI : MonoBehaviour
	{
		[Header("Texts")]
		[Tooltip("Titul Name — название окна (FILES / CAMERA / MAIL)")]
		[SerializeField] private TMP_Text titleText;
		[Tooltip("Number — количество элементов в модуле (писем, камер, файлов)")]
		[SerializeField] private TMP_Text countText;
		[Tooltip("Формат числа: \"00\" — 02, 15; \"0\" — 2, 15")]
		[SerializeField] private string countFormat = "00";

		[Header("Selection")]
		[Tooltip("Что перекрашивается при выборе — например, Image самого окна и Image у Titul window")]
		[SerializeField] private Graphic[] highlightGraphics = new Graphic[0];
		[SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0.4f);
		[SerializeField] private Color selectedColor = Color.white;
		[Tooltip("Необязательно: рамка/стрелка, которая включается только у выбранного окна")]
		[SerializeField] private GameObject selectedMarker;

		public RectTransform RectTransform => (RectTransform)transform;

		// Локализованный заголовок — обновляется сам при смене языка. Пустой (запись не выбрана) —
		// остаётся текст, который стоит в префабе
		public void SetTitle(LocalizedString title)
		{
			if (title == null || title.IsEmpty) return;
			LocalizedTextBinding.Bind(titleText, title);
		}

		// count < 0 — у модуля нет осмысленного количества (или модуля нет вовсе): счётчик прячется
		public void SetCount(int count)
		{
			if (countText == null) return;

			countText.gameObject.SetActive(count >= 0);
			if (count >= 0) countText.text = count.ToString(countFormat);
		}

		public void SetSelected(bool isSelected)
		{
			foreach (Graphic graphic in highlightGraphics)
			{
				if (graphic != null) graphic.color = isSelected ? selectedColor : normalColor;
			}
			if (selectedMarker != null) selectedMarker.SetActive(isSelected);
		}
	}
}
