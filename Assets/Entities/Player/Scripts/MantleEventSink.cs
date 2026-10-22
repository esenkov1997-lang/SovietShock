using UnityEngine;

namespace Player
{
	// Пустой получатель меток фаз из клипа подъёма (MantleGrab / MantleRelease) для рук без оружия.
	// MantleArmsAnimation читает ВРЕМЯ этих событий прямо из клипа, а при проигрывании Unity требует, чтобы
	// у события был метод-получатель на объекте с Animator, — иначе сыплет ошибку. Добавляется кодом сам.
	public class MantleEventSink : MonoBehaviour
	{
		public void MantleGrab() { }
		public void MantleRelease() { }
	}
}
