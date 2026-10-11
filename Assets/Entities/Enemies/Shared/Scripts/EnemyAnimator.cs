using UnityEngine;

namespace Enemies
{
	// Связывает движение и анимацию: каждый кадр отдаёт скорость (м/с) в параметр аниматора — фактическую
	// скорость агента, а в режиме root motion желаемую (см. EnemyMovement.AnimatorSpeed). Blend Tree по этому
	// числу сам смешивает Idle → Walk → Run. Логики здесь нет — аниматор только отражает то, что реально происходит с телом. Мозг может дополнительно
	// включать "осмотр" (SetSearching) — например, анимацию принюхивания.
	[RequireComponent(typeof(EnemyMovement))]
	public class EnemyAnimator : MonoBehaviour
	{
		[Tooltip("Animator на модели. Если пусто — ищется среди дочерних объектов")]
		[SerializeField] private Animator animator;
		[Tooltip("Имя float-параметра скорости в Animator Controller")]
		[SerializeField] private string speedParameter = "Speed";
		[Tooltip("Сглаживание параметра скорости (сек). Больше — плавнее переходы между Idle/Walk/Run, но с запаздыванием")]
		[SerializeField] private float speedDampTime = 0.1f;
		[Tooltip("Имя bool-параметра \"осматривается/принюхивается\". Если такого параметра в контроллере нет — просто игнорируется")]
		[SerializeField] private string searchingParameter = "Searching";
		[Tooltip("Имя float-параметра поворота на ходу (-1 влево .. 1 вправо) — для наклона корпуса или клипов поворота в 2D Blend Tree. " +
			"Если такого параметра в контроллере нет — просто игнорируется")]
		[SerializeField] private string turnParameter = "Turn";
		[Tooltip("Скорость поворота (град/с), которая соответствует Turn = 1")]
		[SerializeField] private float turnRateForFullTurn = 150f;
		[Tooltip("Сглаживание параметра поворота (сек)")]
		[SerializeField] private float turnDampTime = 0.15f;

		private EnemyMovement _movement;
		private int _speedHash;
		private int _searchingHash;
		private bool _hasSearchingParameter;
		private int _turnHash;
		private bool _hasTurnParameter;

		private void Awake()
		{
			_movement = GetComponent<EnemyMovement>();
			if (animator == null) animator = GetComponentInChildren<Animator>();
			if (animator == null)
			{
				Debug.LogError($"{name}: EnemyAnimator не нашёл Animator в дочерних объектах", this);
				enabled = false;
				return;
			}

			// без root motion объект двигает NavMeshAgent — перемещение из клипов спорило бы с ним.
			// С root motion перемещение забирает EnemyRootMotion (OnAnimatorMove) и применяет через EnemyMovement
			animator.applyRootMotion = _movement.UseRootMotion;
			_speedHash = Animator.StringToHash(speedParameter);
			_searchingHash = Animator.StringToHash(searchingParameter);
			_hasSearchingParameter = HasParameter(_searchingHash, AnimatorControllerParameterType.Bool);
			_turnHash = Animator.StringToHash(turnParameter);
			_hasTurnParameter = HasParameter(_turnHash, AnimatorControllerParameterType.Float);
		}

		private void Update()
		{
			animator.SetFloat(_speedHash, _movement.AnimatorSpeed, speedDampTime, Time.deltaTime);
			if (_hasTurnParameter)
			{
				float turn = turnRateForFullTurn > 0f ? Mathf.Clamp(_movement.TurnRate / turnRateForFullTurn, -1f, 1f) : 0f;
				animator.SetFloat(_turnHash, turn, turnDampTime, Time.deltaTime);
			}
		}

		public void SetSearching(bool searching)
		{
			if (_hasSearchingParameter) animator.SetBool(_searchingHash, searching);
		}

		private bool HasParameter(int hash, AnimatorControllerParameterType type)
		{
			foreach (AnimatorControllerParameter parameter in animator.parameters)
				if (parameter.nameHash == hash && parameter.type == type) return true;
			return false;
		}
	}
}
