using Sound;
using UnityEngine;

namespace Interactables
{
	// Звуки интерфейса терминала. Вешается на КОРЕНЬ префаба терминала (рядом с TerminalInstance) вместе с
	// AudioSource — звук идёт из самого терминала в мире. TerminalMainController и модули (TerminalApp)
	// находят этот компонент сами (GetComponentInParent) и вызывают нужный звук — вручную связывать не нужно.
	//
	// AudioSource на объекте служит ШАБЛОНОМ: на старте из него делается несколько копий (Voices, см. SoundVoices),
	// и звуки играют по очереди из разных копий.
	[DisallowMultipleComponent]
	[RequireComponent(typeof(AudioSource))]
	public class TerminalAudio : MonoBehaviour
	{
		[Header("Навигация")]
		[Tooltip("Перемещение выделения стрелками: окна меню, список писем, переключение колонок")]
		[SerializeField] private RandomSound navigate = new RandomSound { pitchRange = new Vector2(0.93f, 1.07f) };
		[Tooltip("Enter — открыть модуль (окно меню)")]
		[SerializeField] private RandomSound confirm = new RandomSound();
		[Tooltip("Escape — шаг назад внутри терминала (из модуля в меню, из переписки в список)")]
		[SerializeField] private RandomSound back = new RandomSound();

		[Header("Вход / выход")]
		[Tooltip("Игрок начал работать с терминалом")]
		[SerializeField] private RandomSound enter = new RandomSound { pitchRange = new Vector2(0.98f, 1.02f) };
		[Tooltip("Игрок вышел из терминала")]
		[SerializeField] private RandomSound exit = new RandomSound { pitchRange = new Vector2(0.98f, 1.02f) };

		[Header("Пароль")]
		[Tooltip("Нажатие клавиши при вводе пароля (ввод и стирание символа)")]
		[SerializeField] private RandomSound typing = new RandomSound { pitchRange = new Vector2(0.9f, 1.1f) };
		[Tooltip("Верный пароль")]
		[SerializeField] private RandomSound accessGranted = new RandomSound { pitchRange = Vector2.one };
		[Tooltip("Неверный пароль")]
		[SerializeField] private RandomSound accessDenied = new RandomSound { pitchRange = new Vector2(0.97f, 1.03f) };

		[Header("Voices")]
		[Tooltip("Сколько звуков могут звучать одновременно, не мешая pitch друг другу")]
		[Range(1, 8)]
		[SerializeField] private int voices = 4;

		private SoundVoices _voices;

		public void PlayNavigate() => Play(navigate);
		public void PlayConfirm() => Play(confirm);
		public void PlayBack() => Play(back);
		public void PlayEnter() => Play(enter);
		public void PlayExit() => Play(exit);
		public void PlayTyping() => Play(typing);
		public void PlayAccessGranted() => Play(accessGranted);
		public void PlayAccessDenied() => Play(accessDenied);

		private void Awake()
		{
			_voices = new SoundVoices(GetComponent<AudioSource>(), voices);
		}

		private void Play(RandomSound sound) => _voices?.Play(sound);
	}
}
