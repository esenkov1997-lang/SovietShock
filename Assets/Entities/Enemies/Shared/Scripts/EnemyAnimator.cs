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

		private EnemyMovement _movement;
		private int _speedHash;
		private int _searchingHash;
		private bool _hasSearchingParameter;

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
		}

		private void Update()
		{
			animator.SetFloat(_speedHash, _movement.AnimatorSpeed, speedDampTime, Time.deltaTime);
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
