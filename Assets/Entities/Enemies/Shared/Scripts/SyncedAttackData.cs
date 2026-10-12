using UnityEngine;

namespace Enemies
{
	// Настройки синхронной (парной) атаки — когда враг и игрок играют две анимации, сделанные вместе в одной
	// сцене Blender (собака вцепляется в руку). Один ассет на один вид захвата; создаётся через
	// Assets/Create/Enemies/Synced Attack и назначается в EnemySyncedAttack на враге.
	[CreateAssetMenu(fileName = "SyncedAttack", menuName = "Enemies/Synced Attack")]
	public class SyncedAttackData : ScriptableObject
	{
		[Header("Анимации")]
		[Tooltip("Имя состояния врага в его Animator Controller (например, DogGrab). Запускается напрямую по имени")]
		public string EnemyStateName = "DogGrab";
		[Tooltip("Tag этого состояния (поле Tag в инспекторе состояния). Пока враг в состоянии с этим тегом — захват идёт")]
		public string EnemyStateTag = "Grab";
		[Tooltip("Плавность входа врага в анимацию захвата (сек)")]
		public float EnemyCrossFade = 0.1f;
		[Tooltip("Клип рук и камеры игрока (например, PlayerDogGrab). Управление вернётся игроку, когда он доиграет")]
		public AnimationClip PlayerClip;
		[Tooltip("Доворот рук игрока по вертикальной оси (градусы). Если в игре руки оказались позади или сбоку — поставь 180 / ±90")]
		public float PlayerArmsYawOffset;

		[Header("Стыковка — смещение врага относительно игрока в сцене Blender")]
		[Tooltip("На сколько метров ВПЕРЁД от игрока стоит враг на первом кадре")]
		public float ForwardOffset = 1.1f;
		[Tooltip("На сколько метров ВПРАВО от игрока (влево — отрицательное)")]
		public float SideOffset;
		[Tooltip("На сколько градусов враг развёрнут относительно игрока (180 — мордой к игроку)")]
		public float YawOffset = 180f;
		[Tooltip("За сколько секунд враг подтягивается в точку стыковки, а игрок разворачивается к нему")]
		public float AlignDuration = 0.25f;

		[Header("Камера игрока")]
		[Tooltip("За сколько секунд камера плавно переходит из обычного взгляда в анимацию")]
		public float CameraBlendIn = 0.3f;
		[Tooltip("За сколько секунд камера возвращается под управление мыши после анимации")]
		public float CameraBlendOut = 0.3f;

		[Header("Урон")]
		[Tooltip("Урон за каждое событие GrabBite в клипе врага")]
		public int DamagePerBite = 10;

		[Header("Когда срабатывает")]
		[Tooltip("Шанс, что вместо обычной атаки враг сделает захват (0.3 = 30%). Для теста поставь 1")]
		[Range(0f, 1f)]
		public float Chance = 0.3f;
		[Tooltip("Минимальная пауза (сек) между захватами одного врага")]
		public float Cooldown = 10f;
		[Tooltip("Захват только спереди: враг должен быть в пределах этого угла (градусы) от взгляда игрока")]
		public float MaxAngleFromPlayerView = 60f;
		[Tooltip("Максимальная разница высоты (м) между игроком и врагом — на лестнице захват не начнётся")]
		public float MaxHeightDifference = 0.3f;
		[Tooltip("Сколько свободного места (м) нужно за спиной врага в точке стыковки — для отскока в конце клипа")]
		public float RequiredSpaceBehind = 1.5f;
		[Tooltip("Подстраховка: захват принудительно заканчивается через столько секунд")]
		public float MaxDuration = 10f;
	}
}
