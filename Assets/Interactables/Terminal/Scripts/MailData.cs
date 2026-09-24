using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;

namespace Interactables
{
	// Одна реплика в переписке. Тексты — не готовые строки, а ссылки на записи таблицы локализации
	// (LocalizedString): в инспекторе выбираешь таблицу и ключ, текст подставится на текущем языке.
	[Serializable]
	public class ChatMessage
	{
		[Tooltip("Включено — исходящее (справа, Message_Row_Right). Выключено — входящее (слева, Message_Row_Left)")]
		public bool isOutgoing;

		[Tooltip("Имя автора в заголовке сообщения (Text_Header). Можно оставить пустым")]
		public LocalizedString author = new LocalizedString();

		[Tooltip("Время в заголовке, например \"14:23\". Не переводится — одинаково на всех языках")]
		public string time;

		[Tooltip("Текст сообщения (Text_Body)")]
		public LocalizedString body = new LocalizedString();

		// true — заголовок (автор/время) показывать; иначе он прячется, чтобы не оставлял пустую строку в пузыре
		public bool HasHeader => !author.IsEmpty || !string.IsNullOrEmpty(time);

		// "J.Smith [14:23]" / "J.Smith" / "[14:23]". localizedAuthor — уже переведённое имя
		public string FormatHeader(string localizedAuthor)
		{
			if (string.IsNullOrEmpty(time)) return localizedAuthor;
			if (string.IsNullOrEmpty(localizedAuthor)) return $"[{time}]";
			return $"{localizedAuthor} [{time}]";
		}
	}

	// Одно письмо = тема + отправитель + цепочка сообщений
	[Serializable]
	public class MailData
	{
		[Tooltip("Тема письма — строка в списке слева и заголовок над перепиской")]
		public LocalizedString subject = new LocalizedString();

		[Tooltip("Отправитель — подзаголовок в строке списка (если в Item_Tab назначен Subtitle Text). Можно оставить пустым")]
		public LocalizedString sender = new LocalizedString();

		public List<ChatMessage> messages = new List<ChatMessage>();
	}
}
