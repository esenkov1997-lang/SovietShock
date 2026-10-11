using UnityEngine;

namespace Enemies
{
	// Повороты на месте анимацией (Turn90_L/R и т.п.) вместо программного вращения "турелью".
	// Клип поворота вращает root-кость, Unity отдаёт это вращение как root motion, и EnemyMovement применяет
	// его к врагу — разворачивается сам объект (конус зрения, направление укуса), а не только модель.
	//
	// Как работает: EnemyMovement.FaceDirection(..., animated: true) при большом угле зовёт TryStartTurn —
	// в аниматор уходят float TurnAngle (знаковый угол: − влево, + вправо) и trigger Turn. Переходы в
	// контроллере сами выбирают клип по углу. Пока играет состояние с тегом "Turn", враг считается
	// поворачивающимся: программный поворот и движение ждут. Остаток угла (цель на 70°, клип на 90°)
	// потом доводится программно — медленно, это незаметно.
	[RequireComponent(typeof(EnemyMovement))]
	public class EnemyTurnInPlace : MonoBehaviour
	{
		[Tooltip("Animator на модели. Если пусто — ищется среди дочерних объектов")]
		[SerializeField] private Animator animator;
		[Tooltip("Повороты на меньший угол (градусы) делаются программно — анимацию на них не тратим")]
		[SerializeField] private float minAngle = 60f;

		[Header("Animator")]
		[Tooltip("Trigger-параметр, запускающий поворот")]
		[SerializeField] private string turnTrigger = "Turn";
		[Tooltip("Float-параметр со знаковым углом поворота (− влево, + вправо) — по нему переходы выбирают клип")]
		[SerializeField] private string angleParameter = "TurnAngle";
		[Tooltip("Tag, который стоит у всех состояний поворота в Animator Controller (поле Tag в инспекторе состояния)")]
		[SerializeField] private string turnStateTag = "Turn";
		[Tooltip("Подстраховка: поворот считается законченным не позже этого времени (сек)")]
		[SerializeField] private float maxTurnDuration = 2f;

		// если аниматор так и не вошёл в состояние поворота за это время — переходы не настроены
		private const float EnterTimeout = 0.3f;

		private int _turnHash;
		private int _angleHash;
		private bool _enteredState;
		private float _startTime;

		public float MinAngle => minAngle;
		public bool IsTurning { get; private set; }

		private void Awake()
		{
			if (animator == null) animator = GetComponentInChildren<Animator>();
			_turnHash = Animator.StringToHash(turnTrigger);
			_angleHash = Animator.StringToHash(angleParameter);

			if (animator == null || !HasParameter(_turnHash, AnimatorControllerParameterType.Trigger) ||
				!HasParameter(_angleHash, AnimatorControllerParameterType.Float))
			{
				Debug.LogWarning($"{name}: EnemyTurnInPlace — нет Animator или параметров \"{turnTrigger}\" (Trigger) / " +
					$"\"{angleParameter}\" (Float) в контроллере. Повороты будут программными", this);
				enabled = false;
				return;
			}

			if (!GetComponent<EnemyMovement>().UseRootMotion)
				Debug.LogWarning($"{name}: EnemyTurnInPlace работает только с Use Root Motion в EnemyMovement — " +
					"иначе клип повернёт модель, но не врага", this);
		}

		private void OnDisable()
		{
			IsTurning = false;
		}

		// signedAngle: − влево, + вправо. false — поворот сейчас не начать (уже идёт или компонент выключен)
		public bool TryStartTurn(float signedAngle)
		{
			if (!enabled || IsTurning) return false;

			animator.SetFloat(_angleHash, signedAngle);
			animator.SetTrigger(_turnHash);
			IsTurning = true;
			_enteredState = false;
			_startTime = Time.time;
			return true;
		}

		private void Update()
		{
			if (!IsTurning) return;

			// во время перехода смотрим, КУДА переходим: в поворот — ещё поворачиваем, из поворота — уже нет,
			// враг может начинать двигаться, пока клип плавно перетекает в стойку
			bool inTurnState = animator.IsInTransition(0)
				? animator.GetNextAnimatorStateInfo(0).IsTag(turnStateTag)
				: animator.GetCurrentAnimatorStateInfo(0).IsTag(turnStateTag);

			if (inTurnState)
			{
				_enteredState = true;
			}
			else if (_enteredState)
			{
				Finish();
				return;
			}
			else if (animator.IsInTransition(0))
			{
				// аниматор ещё доигрывает переход из прошлого состояния (например, из предыдущего поворота
				// в стойку при развороте на 180° двумя Turn90) — новый переход начнётся после него, ждать законно
				_startTime = Time.time;
			}
			else if (Time.time - _startTime > EnterTimeout)
			{
				Debug.LogWarning($"{name}: аниматор не перешёл в состояние поворота с тегом \"{turnStateTag}\" — " +
					"проверь переходы и Tag у состояний поворота", this);
				Finish();
				return;
			}

			if (Time.time - _startTime > maxTurnDuration) Finish();
		}

		private void Finish()
		{
			IsTurning = false;
			animator.ResetTrigger(_turnHash);
		}

		private bool HasParameter(int hash, AnimatorControllerParameterType type)
		{
			foreach (AnimatorControllerParameter parameter in animator.parameters)
				if (parameter.nameHash == hash && parameter.type == type) return true;
			return false;
		}
	}
}
