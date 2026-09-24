using System;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;

namespace Interactables
{
	// Связывает TMP_Text с LocalizedString и держит текст актуальным: сразу после загрузки строки
	// и при каждой смене языка (LocalizationSettings.SelectedLocale). Добавляется из кода на объект
	// с текстом — вручную вешать не нужно, см. LocalizedTextBinding.Bind(...).
	//
	// Почему не стандартный LocalizeStringEvent: тексты терминала спавнятся и перепривязываются
	// из кода (строки писем, заголовок переписки, статус пароля), а часть из них ещё и форматируется
	// ("{автор} [{время}]"). Здесь это одна строка кода, без настройки UnityEvent на каждом префабе.
	// Механизм тот же, что у LocalizeStringEvent: подписка на LocalizedString.StringChanged,
	// пока объект активен, и отписка при выключении/уничтожении (утечек подписок нет).
	[DisallowMultipleComponent]
	public class LocalizedTextBinding : MonoBehaviour
	{
		private TMP_Text _target;
		private LocalizedString _string;
		private Func<string, string> _format;
		private Action _onChanged;
		private bool _subscribed;

		// Привязать текст к локализованной строке. Повторный вызов на том же тексте заменяет привязку.
		//   format    — необязательно: как собрать итоговый текст из перевода (например, добавить время)
		//   onChanged — необязательно: вызывается после каждого обновления текста (например, чтобы
		//               пересчитать прокрутку — длина текста в другом языке другая)
		// Пустой LocalizedString (не выбрана запись в таблице) даёт format("") или пустой текст.
		public static void Bind(TMP_Text target, LocalizedString localizedString,
			Func<string, string> format = null, Action onChanged = null)
		{
			if (target == null) return;
			GetOrAdd(target).Set(target, localizedString, format, onChanged);
		}

		// Поставить обычный (нелокализуемый) текст и снять привязку, если была — иначе следующая
		// смена языка перезаписала бы текст старой строкой
		public static void SetPlain(TMP_Text target, string text)
		{
			if (target == null) return;

			LocalizedTextBinding binding = target.GetComponent<LocalizedTextBinding>();
			if (binding != null) binding.Set(target, null, null, null);
			target.text = text;
		}

		private static LocalizedTextBinding GetOrAdd(TMP_Text target)
		{
			LocalizedTextBinding binding = target.GetComponent<LocalizedTextBinding>();
			return binding != null ? binding : target.gameObject.AddComponent<LocalizedTextBinding>();
		}

		private void Set(TMP_Text target, LocalizedString localizedString, Func<string, string> format, Action onChanged)
		{
			Unsubscribe();

			_target = target;
			_string = localizedString;
			_format = format;
			_onChanged = onChanged;

			if (_string == null) return;

			// пока строка грузится (первый доступ к таблице асинхронный), не показываем текст-заглушку из префаба
			Apply(string.Empty);

			// на выключенном объекте подпишемся в OnEnable — там же, где это делает LocalizeStringEvent
			if (isActiveAndEnabled) Subscribe();
		}

		private void OnEnable() => Subscribe();
		private void OnDisable() => Unsubscribe();

		private void Subscribe()
		{
			if (_subscribed || _string == null || _string.IsEmpty) return;

			// StringChanged: если строка уже загружена — обработчик вызывается сразу, иначе по окончании
			// загрузки; и затем при каждой смене языка
			_string.StringChanged += Apply;
			_subscribed = true;
		}

		private void Unsubscribe()
		{
			if (!_subscribed) return;

			_string.StringChanged -= Apply;
			_subscribed = false;
		}

		private void Apply(string value)
		{
			if (_target == null) return;

			value ??= string.Empty;
			_target.text = _format != null ? _format(value) : value;
			_onChanged?.Invoke();
		}
	}
}
