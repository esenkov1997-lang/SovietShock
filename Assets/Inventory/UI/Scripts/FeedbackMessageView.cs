using Interactables;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Localization;

namespace Inventory
{
	// Короткое сообщение, которое появляется и гаснет само ("Нет места"). Два варианта одного компонента:
	//   - у курсора (Follow Pointer ✔) — в открытом инвентаре, появляется рядом с курсором;
	//   - на месте (Follow Pointer ✘) — на HUD при закрытом инвентаре (подбор с пола), стоит, где сверстали.
	// Новое сообщение заменяет текущее и запускает его заново.
	//
	// Вешается на объект с TextMeshPro. Raycast Target у текста выключи — сообщение не должно ловить клики.
	// Объект изначально можно оставить включённым: код сам прячет текст, пока сообщения нет.
	[RequireComponent(typeof(RectTransform))]
	public class FeedbackMessageView : MonoBehaviour
	{
		[SerializeField] private TMP_Text text;
		[Tooltip("Появляться у курсора (инвентарь). Выключено — на том месте, где объект стоит в вёрстке (HUD)")]
		[SerializeField] private bool followPointer = true;
		[Tooltip("Смещение от курсора, пиксели интерфейса (x вправо, y вверх)")]
		[SerializeField] private Vector2 pointerOffset = new Vector2(12f, 12f);

		[Header("Timing")]
		[Tooltip("Сколько сообщение видно полностью, секунды")]
		[Min(0f)]
		[SerializeField] private float holdTime = 0.8f;
		[Tooltip("Сколько гаснет, секунды")]
		[Min(0f)]
		[SerializeField] private float fadeTime = 0.4f;
		[Tooltip("На сколько всплывает вверх за всё время, пиксели интерфейса")]
		[SerializeField] private float rise = 8f;

		private RectTransform _rect;
		private Vector2 _startPosition;
		private float _time = -1f;
		private Color _color;

		private void Awake()
		{
			_rect = (RectTransform)transform;
			_startPosition = _rect.anchoredPosition;
			if (text == null) text = GetComponent<TMP_Text>();
			if (text != null)
			{
				_color = text.color;
				text.raycastTarget = false;
				text.enabled = false;
			}
		}

		public void Show(LocalizedString message)
		{
			if (text == null || message == null || message.IsEmpty) return;

			LocalizedTextBinding.Bind(text, message);
			text.enabled = true;
			transform.SetAsLastSibling();

			if (followPointer) _startPosition = PointerPosition() + pointerOffset;
			_time = 0f;
			Apply();
		}

		public void Hide()
		{
			_time = -1f;
			if (text != null) text.enabled = false;
		}

		private void Update()
		{
			if (_time < 0f) return;

			// время без учёта паузы/замедления — сообщение всегда живёт одинаково
			_time += Time.unscaledDeltaTime;
			if (_time >= holdTime + fadeTime) Hide();
			else Apply();
		}

		private void Apply()
		{
			float total = Mathf.Max(0.0001f, holdTime + fadeTime);
			float alpha = _time <= holdTime || fadeTime <= 0f ? 1f : 1f - (_time - holdTime) / fadeTime;

			text.color = new Color(_color.r, _color.g, _color.b, _color.a * Mathf.Clamp01(alpha));
			// на целый пиксель — пиксельный шрифт не размывается
			Vector2 position = _startPosition + Vector2.up * (rise * Mathf.Clamp01(_time / total));
			_rect.anchoredPosition = new Vector2(Mathf.Round(position.x), Mathf.Round(position.y));
		}

		// курсор в координатах родителя (с учётом якорей этого объекта)
		private Vector2 PointerPosition()
		{
			RectTransform parent = _rect.parent as RectTransform;
			if (parent == null || Pointer.current == null) return _startPosition;

			Canvas canvas = GetComponentInParent<Canvas>();
			Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Pointer.current.position.ReadValue(),
				eventCamera, out Vector2 local))
			{
				return _startPosition;
			}

			// anchoredPosition считается от точки якоря внутри родителя — переводим
			Rect bounds = parent.rect;
			Vector2 anchor = new Vector2(Mathf.Lerp(bounds.xMin, bounds.xMax, _rect.anchorMin.x),
				Mathf.Lerp(bounds.yMin, bounds.yMax, _rect.anchorMin.y));
			return local - anchor;
		}
	}
}
