using Interactables;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Inventory
{
	// Вешается на InventorySlotPrefab — теперь это не "ячейка сетки", а представление ОДНОГО лежащего
	// предмета: InventoryUI сам выставляет RectTransform.sizeDelta/anchoredPosition под реальный размер
	// (ItemData.GridSize * размер ячейки), этот компонент отвечает только за содержимое (иконка/тексты)
	// и перетаскивание. Иконка предмета — это Image прямо на корневом объекте префаба.
	//
	// Drag не двигает сам объект напрямую (после отпускания решение "можно ли сюда" всё равно принимает
	// InventoryHolder.TryMoveItem — сам объект просто вернётся на актуальную позицию при перерисовке), а
	// создаёт "призрак"-иконку поверх Canvas, следующую за курсором. Куда именно бросили — вычисляет
	// InventoryGridDropZone на Content (там же, где Grid Layout Group задаёт размер ячейки).
	[RequireComponent(typeof(Image))]
	public class InventorySlotUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
	{
		[SerializeField] private TMP_Text itemName;
		[SerializeField] private TMP_Text itemCount;

		// один активный призрак на весь инвентарь сразу — если что-то (RedrawUI посреди драга, закрытие
		// панели и т.п.) прервёт жест не через штатный OnEndDrag, следующий же BeginDrag или
		// InventoryUI.DestroyActiveGhost подчистят оставшийся объект, а не расплодят их
		private static GameObject s_activeGhost;

		private Image _icon;
		private InventoryHolder _inventory;
		private int _slotIndex;
		private RectTransform _dragGhost;

		// читает InventoryGridDropZone, чтобы понять, какой именно предмет перетаскивают и куда его класть
		public int SlotIndex => _slotIndex;
		public InventoryHolder Inventory => _inventory;

		// вызывается InventoryUI.RedrawUI сразу после Instantiate — заполняет визуал одним ItemStack.
		// slotIndex — текущий индекс этого стека в InventoryHolder.Items на момент перерисовки
		public void SetupSlot(ItemData data, int count, int slotIndex, InventoryHolder inventory)
		{
			// ищем Image здесь, а не в Awake: слот часто инстанцируется внутрь выключенной панели
			// инвентаря, а у неактивного объекта Awake не вызывается — _icon так и остался бы null
			if (_icon == null) _icon = GetComponent<Image>();

			_icon.sprite = data != null ? data.icon : null;
			// название — перевод из таблицы; обновится само, если язык сменят при открытом инвентаре
			if (data != null) LocalizedTextBinding.Bind(itemName, data.itemName);
			else LocalizedTextBinding.SetPlain(itemName, string.Empty);

			// стопка из одного предмета не нуждается в цифре "1" — визуальный шум
			itemCount.text = data != null && count > 1 ? count.ToString() : string.Empty;

			_slotIndex = slotIndex;
			_inventory = inventory;
		}

		private void OnDisable()
		{
			// объект могли уничтожить/выключить прямо посреди драга (RedrawUI от чужого изменения
			// инвентаря, закрытие панели и т.п.) — OnEndDrag в таком случае не вызывается вовсе,
			// поэтому подчищаем призрака и здесь, а не только там
			DestroyOwnGhost();
		}

		public void OnBeginDrag(PointerEventData eventData)
		{
			if (s_activeGhost != null) Destroy(s_activeGhost); // подстраховка от утёкшего с прошлого раза

			Canvas canvas = GetComponentInParent<Canvas>();
			if (canvas == null) return;

			// призрак — отдельный объект прямо на Canvas (не внутри Content/Viewport), иначе его обрежет
			// маска ScrollRect
			GameObject ghost = new GameObject("DragGhost", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			ghost.transform.SetParent(canvas.transform, false);
			ghost.transform.SetAsLastSibling();

			Image ghostImage = ghost.GetComponent<Image>();
			ghostImage.sprite = _icon.sprite;
			ghostImage.raycastTarget = false; // иначе призрак сам себе мешал бы поймать OnDrop под курсором

			_dragGhost = (RectTransform)ghost.transform;
			// тот же размер, что и у самого предмета — то есть кратный ItemData.GridSize, а не 1x1
			_dragGhost.sizeDelta = ((RectTransform)transform).sizeDelta;
			_dragGhost.position = eventData.position;

			s_activeGhost = ghost;
		}

		public void OnDrag(PointerEventData eventData)
		{
			if (_dragGhost != null) _dragGhost.position = eventData.position;
		}

		public void OnEndDrag(PointerEventData eventData)
		{
			DestroyOwnGhost();
		}

		private void DestroyOwnGhost()
		{
			if (_dragGhost == null) return;

			Destroy(_dragGhost.gameObject);
			if (s_activeGhost == _dragGhost.gameObject) s_activeGhost = null;
			_dragGhost = null;
		}

		// вызывается InventoryUI при закрытии панели инвентаря — на случай, если драг был прерван
		// не отпусканием кнопки мыши (например, инвентарь закрыли клавишей прямо во время жеста)
		public static void DestroyActiveGhost()
		{
			if (s_activeGhost == null) return;

			Destroy(s_activeGhost);
			s_activeGhost = null;
		}
	}
}
