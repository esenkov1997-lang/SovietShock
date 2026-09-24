using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Interactables
{
	// Модуль "Почта". Вешается на Mail_Details_Panel (или любой объект терминала) и назначается в поле app
	// окна Mail у TerminalMainController. Письма берутся из ассета TerminalDataSO (поле Data).
	//
	// Открытие: заполняет LeftPanel_List строками Item_Tab и сразу выделяет первое письмо.
	// При смене письма правая панель пересобирается из Message_Row_Left/Message_Row_Right и
	// прокручивается к последнему сообщению.
	//
	// Локализация: все тексты — LocalizedString из данных, привязываются к TMP_Text через
	// LocalizedTextBinding и сами обновляются при смене языка. Если переписка была прокручена
	// до конца, после смены языка она остаётся внизу (длина переводов разная).
	//
	// Управление — две колонки с фокусом:
	//   ←/→ (A/D)    — фокус на список писем / на переписку
	//   ↑/↓ (W/S)    — в списке: выбор письма; в переписке: плавная прокрутка, пока зажато
	//   Escape       — из переписки: фокус обратно в список; из списка: выход в меню терминала
	//   колесо мыши, PageUp/PageDown, Home/End — прокрутка переписки при любом фокусе
	public class MailAppController : TerminalApp
	{
		[Header("Data")]
		[Tooltip("Ассет с письмами этого терминала (Create → Interactables → Terminal Data)")]
		[SerializeField] private TerminalDataSO data;

		[Header("Left Panel")]
		[Tooltip("LeftPanel_List — контейнер с Vertical Layout Group")]
		[SerializeField] private RectTransform listContainer;
		[Tooltip("Префаб Item_Tab (с компонентом ItemTabUI)")]
		[SerializeField] private ItemTabUI itemTabPrefab;

		[Header("Right Panel")]
		[Tooltip("Mail_Details_Panel — включается при открытии модуля")]
		[SerializeField] private GameObject detailsPanel;
		[Tooltip("Необязательно: заголовок над перепиской (тема выбранного письма)")]
		[SerializeField] private TMP_Text headerText;
		[Tooltip("ScrollRect с перепиской")]
		[SerializeField] private ScrollRect messagesScroll;
		[Tooltip("Куда спавнятся сообщения. Если не задано — Content у ScrollRect. " +
			"Нужны Vertical Layout Group + Content Size Fitter (Vertical Fit = Preferred Size)")]
		[SerializeField] private RectTransform messagesContainer;
		[Tooltip("Входящее сообщение (Message_Row_Left)")]
		[SerializeField] private GameObject messageRowLeftPrefab;
		[Tooltip("Исходящее сообщение (Message_Row_Right)")]
		[SerializeField] private GameObject messageRowRightPrefab;
		[Tooltip("Имя объекта с TMP_Text для автора/времени внутри префабов строк")]
		[SerializeField] private string rowHeaderTextName = "Text_Header";
		[Tooltip("Имя объекта с TMP_Text для текста сообщения внутри префабов строк")]
		[SerializeField] private string rowBodyTextName = "Text_Body";

		[Header("Navigation")]
		[Tooltip("↑ на первом письме переходит на последнее и наоборот")]
		[SerializeField] private bool wrapNavigation = true;

		[Header("Column Focus")]
		[Tooltip("Необязательно: рамка/подсветка вокруг списка писем — включена, пока фокус на списке")]
		[SerializeField] private GameObject listFocusIndicator;
		[Tooltip("Необязательно: рамка/подсветка вокруг переписки — включена, пока фокус на переписке")]
		[SerializeField] private GameObject chatFocusIndicator;

		[Header("Chat Scrolling")]
		[Tooltip("Скорость прокрутки переписки стрелками ↑/↓ (пока зажаты), в пикселях canvas в секунду")]
		[SerializeField] private float arrowScrollSpeed = 700f;
		[Tooltip("На сколько пикселей canvas прокручивается переписка за один щелчок колеса мыши")]
		[SerializeField] private float wheelScrollStep = 80f;
		[Tooltip("PageUp/PageDown прокручивают на эту долю видимой высоты (0.8 — чуть меньше экрана, " +
			"чтобы последняя строка осталась видна для ориентира)")]
		[Range(0.1f, 1f)]
		[SerializeField] private float pageScrollFraction = 0.8f;

		private readonly List<ItemTabUI> _tabs = new List<ItemTabUI>();
		private readonly List<GameObject> _rows = new List<GameObject>();
		private int _selected = -1;
		private Coroutine _scrollRoutine;

		// true — переписка "прилипла" к низу: при изменении высоты текста (подгрузилась строка, сменился
		// язык) держим её внизу. Сбрасывается, как только игрок сам прокрутил вверх
		private bool _stickToBottom;
		private bool _layoutDirty;

		private static readonly IReadOnlyList<MailData> NoMails = new MailData[0];
		private IReadOnlyList<MailData> Mails => data != null ? data.Mails : NoMails;

		private enum Column { List, Chat }
		private Column _focus = Column.List;

		public int SelectedIndex => _selected;

		// Number на окне Mail в каскадном меню — количество писем
		public override int ItemCount => Mails.Count;

		// Для TerminalInstance на корне префаба: у каждого терминала в уровне свои письма
		public TerminalDataSO Data
		{
			get => data;
			set => data = value;
		}

		// ---------- TerminalApp ----------

		public override void Open()
		{
			if (detailsPanel != null) detailsPanel.SetActive(true);
			if (data == null) Debug.LogWarning($"{nameof(MailAppController)} на {name}: не назначен Data (TerminalDataSO) — список писем пуст", this);

			SpawnTabs();
			_selected = -1;
			SelectMail(0);
			SetFocus(Column.List);
		}

		public override void Close()
		{
			StopScrollRoutine();
			ClearTabs();
			ClearMessages();
			_selected = -1;

			// индикаторы могут жить вне detailsPanel (например, рамка вокруг LeftPanel_List) — гасим явно
			if (listFocusIndicator != null) listFocusIndicator.SetActive(false);
			if (chatFocusIndicator != null) chatFocusIndicator.SetActive(false);

			if (detailsPanel != null) detailsPanel.SetActive(false);
		}

		public override void HandleInput()
		{
			if (_tabs.Count == 0) return;

			// ← / → — переключение колонки; в этом кадре больше ничего не делаем, чтобы одно нажатие
			// не успело и сменить фокус, и сработать уже в новой колонке
			if (TerminalMainController.LeftPressed() && _focus != Column.List) { SetFocus(Column.List); return; }
			if (TerminalMainController.RightPressed() && _focus != Column.Chat) { SetFocus(Column.Chat); return; }

			if (_focus == Column.List)
			{
				if (TerminalMainController.UpPressed()) Step(-1);
				else if (TerminalMainController.DownPressed()) Step(+1);
			}
			else
			{
				// плавно, пока зажато: вверх — к старым сообщениям
				float direction = (TerminalMainController.UpHeld() ? 1f : 0f) - (TerminalMainController.DownHeld() ? 1f : 0f);
				if (direction != 0f) ScrollChatBy(direction * arrowScrollSpeed * Time.deltaTime);
			}

			HandleChatScroll();
		}

		// Escape из переписки — сначала "назад" в список, а не сразу выход из почты
		public override bool HandleBack()
		{
			if (_focus != Column.Chat) return false;
			SetFocus(Column.List);
			return true;
		}

		private void SetFocus(Column column)
		{
			_focus = column;
			if (listFocusIndicator != null) listFocusIndicator.SetActive(column == Column.List);
			if (chatFocusIndicator != null) chatFocusIndicator.SetActive(column == Column.Chat);
		}

		// Прокрутка переписки, работает при любом фокусе. Своя, а не встроенная в ScrollRect: курсор зажат,
		// а экран терминала — RenderTexture, поэтому события мыши до ScrollRect не доходят. Колесо читаем
		// напрямую — Input.mouseScrollDelta работает и при зажатом курсоре
		private void HandleChatScroll()
		{
			if (messagesScroll == null) return;

			float wheel = Input.mouseScrollDelta.y;
			if (wheel != 0f) ScrollChatBy(wheel * wheelScrollStep);

			float page = messagesScroll.viewport != null ? messagesScroll.viewport.rect.height * pageScrollFraction : 0f;
			if (Input.GetKeyDown(KeyCode.PageUp)) ScrollChatBy(page);
			if (Input.GetKeyDown(KeyCode.PageDown)) ScrollChatBy(-page);

			// Home/End — в начало/конец переписки; ручная прокрутка отменяет незаконченный автоскролл вниз
			if (Input.GetKeyDown(KeyCode.Home))
			{
				StopScrollRoutine();
				_stickToBottom = false;
				messagesScroll.verticalNormalizedPosition = 1f;
			}
			if (Input.GetKeyDown(KeyCode.End)) ScrollToBottom();
		}

		// pixels > 0 — вверх (к старым сообщениям), < 0 — вниз
		private void ScrollChatBy(float pixels)
		{
			RectTransform content = messagesScroll.content;
			RectTransform viewport = messagesScroll.viewport != null ? messagesScroll.viewport : (RectTransform)messagesScroll.transform;
			if (content == null) return;

			// переводим пиксели в нормализованную позицию ScrollRect (1 — верх, 0 — низ)
			float scrollable = content.rect.height - viewport.rect.height;
			if (scrollable <= 0f) return; // всё и так помещается — прокручивать нечего

			StopScrollRoutine();
			messagesScroll.verticalNormalizedPosition =
				Mathf.Clamp01(messagesScroll.verticalNormalizedPosition + pixels / scrollable);

			// докрутил до самого низа сам — снова "прилипаем", ушёл вверх — больше не тянем вниз
			_stickToBottom = messagesScroll.verticalNormalizedPosition <= 0.001f;
		}

		// ---------- Список писем ----------

		private void SpawnTabs()
		{
			ClearTabs();
			if (itemTabPrefab == null || listContainer == null)
			{
				Debug.LogError($"{nameof(MailAppController)} на {name}: не назначен Item Tab Prefab или List Container", this);
				return;
			}

			// частая ошибка: в поле перетащили образец Item_Tab из самого списка, а не префаб из окна Project.
			// ClearTabs выше его уже выключил и удаляет — копии из него получились бы невидимыми
			if (itemTabPrefab.transform.IsChildOf(listContainer))
			{
				Debug.LogError($"{nameof(MailAppController)} на {name}: в Item Tab Prefab назначен объект из LeftPanel_List. " +
					"Перетащи сюда префаб Item_Tab из окна Project (Assets/Interactables/Terminal)", this);
				return;
			}

			IReadOnlyList<MailData> mails = Mails;
			for (int i = 0; i < mails.Count; i++)
			{
				ItemTabUI tab = Instantiate(itemTabPrefab, listContainer);
				tab.Setup(i, mails[i].subject, mails[i].sender);
				tab.Clicked += HandleTabClicked;
				_tabs.Add(tab);
			}
		}

		private void HandleTabClicked(ItemTabUI tab) => SelectMail(tab.Index);

		private void Step(int direction)
		{
			int next = _selected + direction;
			if (wrapNavigation) next = (next % _tabs.Count + _tabs.Count) % _tabs.Count;
			SelectMail(next);
		}

		public void SelectMail(int index)
		{
			if (_tabs.Count == 0)
			{
				ClearMessages();
				LocalizedTextBinding.SetPlain(headerText, string.Empty);
				return;
			}

			index = Mathf.Clamp(index, 0, _tabs.Count - 1);
			if (index == _selected) return;

			if (_selected >= 0 && _selected < _tabs.Count) _tabs[_selected].SetSelected(false);
			_selected = index;
			_tabs[_selected].SetSelected(true);

			ShowMail(Mails[_selected]);
		}

		// ---------- Переписка ----------

		private void ShowMail(MailData mail)
		{
			ClearMessages();
			// перепривязка на тему нового письма; смена языка обновит заголовок сама
			LocalizedTextBinding.Bind(headerText, mail.subject);

			// не в Awake: модуль может лежать на выключенном объекте, и его Awake к этому моменту ещё не вызывался
			if (messagesContainer == null && messagesScroll != null) messagesContainer = messagesScroll.content;
			if (messagesContainer == null)
			{
				Debug.LogError($"{nameof(MailAppController)} на {name}: не назначен Messages Container / ScrollRect", this);
				return;
			}

			foreach (ChatMessage message in mail.messages)
			{
				GameObject prefab = message.isOutgoing ? messageRowRightPrefab : messageRowLeftPrefab;
				if (prefab == null) continue;

				GameObject row = Instantiate(prefab, messagesContainer);
				FillRow(row, message);
				_rows.Add(row);
			}

			ScrollToBottom();
		}

		// тексты ищутся по имени объекта (Text_Header / Text_Body), а не "первый попавшийся TMP_Text" —
		// в строке их два, и порядок в иерархии не должен решать, куда попадёт текст сообщения
		private void FillRow(GameObject row, ChatMessage message)
		{
			TMP_Text header = null;
			TMP_Text body = null;
			foreach (TMP_Text t in row.GetComponentsInChildren<TMP_Text>(true))
			{
				if (t.name == rowHeaderTextName) header = t;
				else if (t.name == rowBodyTextName) body = t;
			}

			// onChanged: текст пришёл (первая загрузка таблицы асинхронная) или сменился язык — высота строки
			// поменялась, нужно пересчитать прокрутку (см. LateUpdate)
			if (body != null) LocalizedTextBinding.Bind(body, message.body, onChanged: MarkLayoutDirty);
			else Debug.LogError($"{nameof(MailAppController)}: в префабе {row.name} нет объекта \"{rowBodyTextName}\" с TMP_Text", row);

			if (header != null)
			{
				// без автора и времени заголовок не нужен — прячем, чтобы не оставлял пустую строку в пузыре.
				// Прячем ДО привязки: на выключенном объекте привязка просто не подпишется
				header.gameObject.SetActive(message.HasHeader);
				if (message.HasHeader)
				{
					// время к переведённому имени добавляется при каждом обновлении — "J.Smith [14:23]"
					LocalizedTextBinding.Bind(header, message.author, message.FormatHeader, MarkLayoutDirty);
				}
			}
		}

		private void MarkLayoutDirty() => _layoutDirty = true;

		// Все изменения текстов за кадр (их может быть десятки — по одному на строку) сводятся в один
		// пересчёт прокрутки в конце кадра
		private void LateUpdate()
		{
			if (!_layoutDirty) return;
			_layoutDirty = false;

			if (_stickToBottom && messagesScroll != null) ApplyScrollToBottom();
		}

		private void ScrollToBottom()
		{
			if (messagesScroll == null) return;

			_stickToBottom = true;
			StopScrollRoutine();
			ApplyScrollToBottom();
			// TMP может досчитать высоту новых строк только к следующему кадру — тогда повторяем,
			// иначе последнее сообщение окажется наполовину за нижним краем
			if (isActiveAndEnabled) _scrollRoutine = StartCoroutine(ScrollToBottomNextFrame());
		}

		private IEnumerator ScrollToBottomNextFrame()
		{
			yield return null;
			_scrollRoutine = null;
			ApplyScrollToBottom();
		}

		private void ApplyScrollToBottom()
		{
			// пересобираем layout сразу, а не ждём конца кадра — иначе ScrollRect ещё не знает новую высоту контента
			Canvas.ForceUpdateCanvases();
			if (messagesContainer != null) LayoutRebuilder.ForceRebuildLayoutImmediate(messagesContainer);
			messagesScroll.verticalNormalizedPosition = 0f; // 0 — самый низ
		}

		private void StopScrollRoutine()
		{
			if (_scrollRoutine != null) StopCoroutine(_scrollRoutine);
			_scrollRoutine = null;
		}

		// ---------- Очистка ----------

		// чистим ВСЕ дочерние объекты контейнеров, а не только заспавненные нами: в префабе там можно держать
		// строки-образцы для вёрстки (Item_Tab, Message_Row_*) — в игре они не смешаются с настоящими
		private void ClearTabs()
		{
			ClearChildren(listContainer);
			_tabs.Clear();
		}

		private void ClearMessages()
		{
			if (messagesContainer == null && messagesScroll != null) messagesContainer = messagesScroll.content;
			ClearChildren(messagesContainer);
			_rows.Clear();
		}

		private static void ClearChildren(RectTransform container)
		{
			if (container == null) return;
			for (int i = container.childCount - 1; i >= 0; i--)
			{
				RemoveFromLayout(container.GetChild(i).gameObject);
			}
		}

		// Destroy откладывается до конца кадра, а Layout Group учитывает объект, пока тот активен. Выключаем
		// сразу — иначе ForceRebuildLayoutImmediate в этом же кадре посчитал бы высоту вместе со старыми строками
		private static void RemoveFromLayout(GameObject go)
		{
			go.SetActive(false);
			Destroy(go);
		}
	}
}
