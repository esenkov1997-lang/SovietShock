using System.Collections.Generic;
using UnityEngine;

namespace Interactables
{
	// Содержимое одного терминала — ассет в проекте, а не данные на компоненте в сцене.
	// Один и тот же префаб UITerminal можно поставить в нескольких местах уровня и дать каждому свой
	// ассет с письмами. Тексты здесь хранятся ключами локализации (см. MailData / ChatMessage),
	// поэтому ассет один на все языки.
	//
	// Создание: Project → Create → Interactables → Terminal Data.
	// Подключение: поле Data у MailAppController (на Mail_Details_Panel).
	[CreateAssetMenu(fileName = "TerminalData", menuName = "Interactables/Terminal Data")]
	public class TerminalDataSO : ScriptableObject
	{
		[Tooltip("Письма в том порядке, в котором они будут показаны в списке")]
		[SerializeField] private List<MailData> mails = new List<MailData>();

		public IReadOnlyList<MailData> Mails => mails;
	}
}
