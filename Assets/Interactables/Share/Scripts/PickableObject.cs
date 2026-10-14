using Player;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Localization;

namespace Interactables
{
	// Маркер физического предмета, который игрок может взять в руки и держать перед собой (без инвентаря) —
	// ящики, обломки, решётки. Подобрать можно только объекты с этим компонентом, а не всё подряд с Rigidbody.
	// Работает через обычную систему интеракции (IInteractable + Player.PlayerInteractor): по кнопке Interact
	// передаёт себя в Player.ItemHoldController игрока, а сам хранит только настройки для инспектора.
	[RequireComponent(typeof(Rigidbody))]
	public class PickableObject : MonoBehaviour, IInteractable
	{
		[Tooltip("Подсказка при наведении. Ключ prompt.carry нужно завести в таблице локализации UI (\"Взять\" / \"Pick up\")")]
		[SerializeField] private LocalizedString interactionPrompt = new LocalizedString("UI", "prompt.carry");

		[Header("Hold Offset")]
		[Tooltip("Смещение предмета относительно HoldPoint игрока, пока он в руках (локальные оси HoldPoint: Z — вперёд от камеры)")]
		[SerializeField] private Vector3 holdOffsetPosition;
		[Tooltip("Поворот предмета относительно HoldPoint, пока он в руках, в градусах")]
		[SerializeField] private Vector3 holdOffsetRotation;

		[Header("Transparency")]
		[Tooltip("Полупрозрачный материал, который подставляется на все рендереры предмета, пока он в руках, — чтобы большой " +
			"предмет не перекрывал обзор. Пусто — материалы не меняются")]
		[SerializeField] private Material transparentMaterial;

		[Header("Events")]
		[Tooltip("Вызывается в момент подбора — до того, как Rigidbody станет kinematic. " +
			"Например, для обломка: BreakableDebris.CancelFreeze, чтобы заморозка не сработала уже после броска")]
		public UnityEvent OnPickedUp;
		[Tooltip("Вызывается, когда предмет отпустили или бросили — физика уже включена")]
		public UnityEvent OnReleased;

		private Renderer[] _renderers;
		private Material[][] _originalMaterials;
		private Transform[] _hierarchy;
		private int[] _originalLayers;

		public LocalizedString InteractionPrompt => interactionPrompt;
		public Vector3 HoldOffsetPosition => holdOffsetPosition;
		public Quaternion HoldOffsetRotation => Quaternion.Euler(holdOffsetRotation);
		public Rigidbody Body { get; private set; }
		public Collider[] Colliders { get; private set; }
		public bool IsHeld { get; private set; }
		// габариты всех (не триггерных) коллайдеров предмета в локальных осях его корня — по ним
		// ItemHoldController проверяет, влезает ли предмет между камерой и стеной
		public Bounds LocalBounds { get; private set; }

		private void Awake()
		{
			Body = GetComponent<Rigidbody>();
			Colliders = GetComponentsInChildren<Collider>(true);
			_renderers = GetComponentsInChildren<Renderer>(true);
			_hierarchy = GetComponentsInChildren<Transform>(true);
			LocalBounds = CalculateLocalBounds();
		}

		// Переводит весь предмет (со всеми дочерними объектами) на слой удержания. Исходные слои запоминаются
		// только при первом вызове — если предмет подобрали снова, пока он ещё не вернул свой слой после
		// прошлого броска, "исходным" не станет сам слой удержания
		internal void SetHeldLayer(int layer)
		{
			if (_originalLayers == null)
			{
				_originalLayers = new int[_hierarchy.Length];
				for (int i = 0; i < _hierarchy.Length; i++) _originalLayers[i] = _hierarchy[i].gameObject.layer;
			}

			foreach (Transform part in _hierarchy)
			{
				if (part != null) part.gameObject.layer = layer;
			}
		}

		internal void RestoreLayers()
		{
			if (_originalLayers == null) return;

			for (int i = 0; i < _hierarchy.Length; i++)
			{
				if (_hierarchy[i] != null) _hierarchy[i].gameObject.layer = _originalLayers[i];
			}
			_originalLayers = null;
		}

		public void Interact(GameObject interactor)
		{
			if (IsHeld) return;

			ItemHoldController holder = interactor.GetComponentInParent<ItemHoldController>();
			if (holder == null)
			{
				Debug.LogError($"{nameof(PickableObject)} на {name}: у {interactor.name} " +
					$"(и его родителей) нет компонента {nameof(ItemHoldController)}", this);
				return;
			}

			holder.Pickup(this);
		}

		public bool CanInteractFrom(Vector3 viewerPosition) => !IsHeld;

		// Вызываются только из ItemHoldController
		internal void NotifyPickedUp()
		{
			IsHeld = true;
			OnPickedUp?.Invoke();
			SetTransparent(true);
		}

		internal void NotifyReleased()
		{
			IsHeld = false;
			SetTransparent(false);
			OnReleased?.Invoke();
		}

		private Bounds CalculateLocalBounds()
		{
			Matrix4x4 toRoot = transform.worldToLocalMatrix;
			bool any = false;
			Bounds result = default;

			foreach (Collider part in Colliders)
			{
				if (part.isTrigger || !TryGetShapeBounds(part, out Bounds shape)) continue;

				// 8 углов формы коллайдера → в локальные оси корня предмета
				Matrix4x4 partToRoot = toRoot * part.transform.localToWorldMatrix;
				for (int i = 0; i < 8; i++)
				{
					Vector3 corner = shape.center + Vector3.Scale(shape.extents,
						new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
					Vector3 point = partToRoot.MultiplyPoint3x4(corner);

					if (!any) { result = new Bounds(point, Vector3.zero); any = true; }
					else result.Encapsulate(point);
				}
			}

			return any ? result : new Bounds(Vector3.zero, Vector3.one * 0.1f);
		}

		// форма коллайдера в его собственных локальных осях
		private static bool TryGetShapeBounds(Collider part, out Bounds shape)
		{
			switch (part)
			{
				case BoxCollider box:
					shape = new Bounds(box.center, box.size);
					return true;
				case SphereCollider sphere:
					shape = new Bounds(sphere.center, Vector3.one * sphere.radius * 2f);
					return true;
				case CapsuleCollider capsule:
					Vector3 size = Vector3.one * capsule.radius * 2f;
					size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
					shape = new Bounds(capsule.center, size);
					return true;
				case MeshCollider mesh when mesh.sharedMesh != null:
					shape = mesh.sharedMesh.bounds;
					return true;
				default:
					shape = default;
					return false;
			}
		}

		// sharedMaterials, а не materials — не плодим копии материалов при каждом подборе
		private void SetTransparent(bool transparent)
		{
			if (transparentMaterial == null) return;

			if (transparent)
			{
				_originalMaterials = new Material[_renderers.Length][];
				for (int i = 0; i < _renderers.Length; i++)
				{
					Material[] original = _renderers[i].sharedMaterials;
					_originalMaterials[i] = original;

					Material[] replaced = new Material[original.Length];
					for (int j = 0; j < replaced.Length; j++) replaced[j] = transparentMaterial;
					_renderers[i].sharedMaterials = replaced;
				}
			}
			else if (_originalMaterials != null)
			{
				for (int i = 0; i < _renderers.Length; i++)
				{
					if (_renderers[i] != null) _renderers[i].sharedMaterials = _originalMaterials[i];
				}
				_originalMaterials = null;
			}
		}
	}
}
