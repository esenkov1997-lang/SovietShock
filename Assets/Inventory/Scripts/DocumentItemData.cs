using UnityEngine;
using UnityEngine.Localization;

namespace Inventory
{
	// Документ: записка, приказ, страница журнала. Подбирается во вкладку "Улики" и читается в окне документа
	// (клик по нему в списке). Заголовок окна — название предмета (Item Name), текст — Body.
	// Тексты — ключи таблиц локализации (например, отдельная таблица Documents).
	[CreateAssetMenu(fileName = "NewDocumentItemData", menuName = "Inventory/Document Item Data")]
	public class DocumentItemData : ItemData
	{
		[Header("Document")]
		[Tooltip("Текст документа — ключ таблицы локализации. Переводы строк в тексте сохраняются")]
		public LocalizedString body = new LocalizedString();

		// документ всегда улика — незачем давать выставить это вручную и промахнуться
		private void OnValidate()
		{
			type = ItemType.Document;
		}
	}
}
