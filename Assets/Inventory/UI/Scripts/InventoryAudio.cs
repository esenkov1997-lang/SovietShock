using Sound;
using UnityEngine;

namespace Inventory
{
	// Звуки инвентаря. Когда что играть, решает экран инвентаря (InventoryUI) — здесь только сами звуки.
	// Вешается на канвас инвентаря (рядом с InventoryUI) вместе с AudioSource: Spatial Blend = 0 (2D), Output —
	// группа интерфейса в микшере, если есть. AudioSource — шаблон, из него делается несколько копий (Voices).
	// Пустой звук (без клипов) просто не играет.
	[DisallowMultipleComponent]
	[RequireComponent(typeof(AudioSource))]
	public class InventoryAudio : MonoBehaviour
	{
		[Header("Экран")]
		[SerializeField] private RandomSound open = new RandomSound { pitchRange = new Vector2(0.98f, 1.02f) };
		[SerializeField] private RandomSound close = new RandomSound { pitchRange = new Vector2(0.98f, 1.02f) };
		[Tooltip("Переключение вкладки (Предметы / Улики)")]
		[SerializeField] private RandomSound tab = new RandomSound();
		[Tooltip("Открыли документ для чтения")]
		[SerializeField] private RandomSound read = new RandomSound();

		[Header("Предметы")]
		[Tooltip("Предмет взяли мышью (начало перетаскивания)")]
		[SerializeField] private RandomSound grab = new RandomSound { pitchRange = new Vector2(0.93f, 1.07f) };
		[Tooltip("Предмет лёг на место: перетаскивание, двойной клик, пункт меню, \"Взять все\"")]
		[SerializeField] private RandomSound place = new RandomSound { pitchRange = new Vector2(0.93f, 1.07f) };
		[Tooltip("Отказ: нет места, сюда положить нельзя")]
		[SerializeField] private RandomSound deny = new RandomSound { pitchRange = new Vector2(0.97f, 1.03f) };
		[Tooltip("Предмет выброшен в мир")]
		[SerializeField] private RandomSound dropToWorld = new RandomSound();
		[Tooltip("Подобрали предмет с пола (инвентарь может быть и закрыт)")]
		[SerializeField] private RandomSound pickup = new RandomSound { pitchRange = new Vector2(0.93f, 1.07f) };

		[Header("Колесо")]
		[Tooltip("Курсор перешёл на другой сектор колеса (и в инвентаре, и в быстром выборе по Q)")]
		[SerializeField] private RandomSound wheelHover = new RandomSound { pitchRange = new Vector2(0.95f, 1.05f) };

		[Header("Voices")]
		[Tooltip("Сколько звуков могут звучать одновременно, не мешая pitch друг другу")]
		[Range(1, 8)]
		[SerializeField] private int voices = 4;

		private SoundVoices _voices;

		public void PlayOpen() => Play(open);
		public void PlayClose() => Play(close);
		public void PlayTab() => Play(tab);
		public void PlayRead() => Play(read);
		public void PlayGrab() => Play(grab);
		public void PlayPlace() => Play(place);
		public void PlayDeny() => Play(deny);
		public void PlayDropToWorld() => Play(dropToWorld);
		public void PlayPickup() => Play(pickup);
		public void PlayWheelHover() => Play(wheelHover);

		private void Awake()
		{
			_voices = new SoundVoices(GetComponent<AudioSource>(), voices);
		}

		private void Play(RandomSound sound) => _voices?.Play(sound);
	}
}
